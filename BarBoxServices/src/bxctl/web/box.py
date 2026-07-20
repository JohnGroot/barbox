import json
from typing import Annotated
from uuid import UUID, uuid4

from fastapi import APIRouter, Header, HTTPException, Query, status
from sqlalchemy import select, update
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import joinedload
from sqlalchemy.orm.attributes import set_committed_value
from structlog import get_logger

from bxctl import db, errors, structures
from bxctl.games import validation as game_validation
from bxctl.registry import CoreEvent

from . import auth, dependencies

logger = get_logger()
router = APIRouter(prefix="/box", tags=["Core: Boxes & Sessions"])


@router.post("/", status_code=201)
async def create_box(
    new_box: structures.BoxCreate,
    db_service: dependencies.Database,
    now: dependencies.Now,
    _registration_auth: dependencies.RegistrationSecretRequired,
) -> structures.BoxDetailWithAPIKey:
    """Create a new box and return API key.

    The API key is deterministically derived from the box ID, so it can
    always be retrieved by calling PUT /box/{box_id}.

    Requires the X-Registration-Secret header, since this always mints a key
    for a box_id that doesn't exist yet.

    Validates that:
    - Box ID is unique
    - Box tag (identifier) is unique

    Returns:
        201: Box created successfully with API key
        401: Missing or invalid X-Registration-Secret header
        409: Box already exists (duplicate ID or tag)
        500: Internal server error

    For idempotent box registration, use PUT /box/{box_id} instead.
    """

    existing_box_result = await db_service.session.execute(
        select(db.defs.Box).where(
            (db.defs.Box.id == new_box.id) | (db.defs.Box.tag == new_box.tag)
        )
    )
    existing_box = existing_box_result.scalar_one_or_none()

    if existing_box is not None:
        logger.info(
            "box_creation_duplicate",
            box_id=str(new_box.id),
            existing_id=str(existing_box.id),
            tag=new_box.tag,
            existing_tag=existing_box.tag,
        )
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail={
                "code": errors.ErrorCode.DUPLICATE_RESOURCE,
                "message": (
                    "Box already exists with "
                    f"{'ID' if existing_box.id == new_box.id else 'tag'} "
                    f"'{new_box.id if existing_box.id == new_box.id else new_box.tag}'."
                ),
                "details": {
                    "conflict_field": "id" if existing_box.id == new_box.id else "tag",
                    "conflict_value": str(new_box.id)
                    if existing_box.id == new_box.id
                    else new_box.tag,
                },
            },
        )

    api_key = auth.derive_box_api_key(new_box.id)

    # Create box (no hash storage needed - key is derived on demand)
    try:
        box_data = new_box.model_dump() | {
            "created_at": now,
            "last_seen": None,
        }

        await db_service.create(
            target=db.defs.Box,
            data=box_data,
        )

        logger.info(
            "box_created",
            box_id=str(new_box.id),
            name=new_box.name,
            tag=new_box.tag,
        )

        return structures.BoxDetailWithAPIKey(
            id=new_box.id,
            name=new_box.name,
            tag=new_box.tag,
            api_key=api_key,
        )

    except IntegrityError as e:
        logger.exception(
            "box_creation_integrity_error",
            error=str(e),
            box_id=str(new_box.id),
            tag=new_box.tag,
        )
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail={
                "code": errors.ErrorCode.UNIQUE_CONSTRAINT,
                "message": "Box creation failed due to a constraint violation.",
                "details": {"error": str(e.orig) if hasattr(e, "orig") else str(e)},
            },
        ) from e

    except Exception as e:
        logger.exception(
            "box_creation_failed",
            error=str(e),
            error_type=type(e).__name__,
            box_id=str(new_box.id),
            tag=new_box.tag,
        )
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "code": errors.ErrorCode.INTERNAL_ERROR,
                "message": "An unexpected error occurred during box creation.",
                "details": {"error_type": type(e).__name__},
            },
        ) from e


@router.put("/{box_id}", status_code=200)
async def register_box(
    box_id: UUID,
    box_data: structures.BoxCreate,
    db_service: dependencies.Database,
    now: dependencies.Now,
    x_registration_secret: Annotated[str | None, Header()] = None,
) -> structures.BoxDetailWithAPIKey:
    """
    Idempotent box registration - creates if not exists, always returns API key.

    This endpoint is safe to call multiple times with the same box_id.
    Used by clients to ensure box exists and retrieve the API key.

    **Authentication**: Not required for recovering an *existing* box's key
    (needed for reinstalls). Required (X-Registration-Secret header) only
    when the box_id doesn't exist yet, to prevent minting keys for
    arbitrary chosen box_ids.

    The API key is deterministically derived from the box ID, so:
    - First call: Creates the box and returns API key (requires X-Registration-Secret)
    - Subsequent calls: Returns the same API key (idempotent, no auth needed)
    - Reinstalls: Just call this endpoint again to get the key (no auth needed)

    **Name/Tag Handling**:
    - Name and tag are set on first registration and cannot be updated
    - If called with different name/tag for existing box, returns existing values
    - Warning field indicates if name/tag mismatch was detected

    Args:
        box_id: Box ID from path parameter
        box_data: Box creation data (id, name, tag)
        db_service: Database service
        now: Current timestamp
        x_registration_secret: Required only when box_id doesn't exist yet

    Returns:
        200: BoxDetailWithAPIKey with deterministic API key and warning if applicable
        400: Box ID in path does not match request body
        401: box_id doesn't exist and X-Registration-Secret is missing/invalid
        500: Internal server error
    """
    if box_id != box_data.id:
        logger.warning(
            "box_registration_id_mismatch",
            path_box_id=str(box_id),
            body_box_id=str(box_data.id),
        )
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail={
                "code": errors.ErrorCode.VALIDATION_ERROR,
                "message": "Box ID in path does not match request body.",
                "details": {
                    "path_box_id": str(box_id),
                    "body_box_id": str(box_data.id),
                },
            },
        )

    api_key = auth.derive_box_api_key(box_id)

    existing_box_result = await db_service.session.execute(
        select(db.defs.Box).where(db.defs.Box.id == box_id)
    )
    existing_box = existing_box_result.scalar_one_or_none()

    if existing_box is not None:
        # Box already exists - return with api_key for recovery/re-deployment scenarios
        if existing_box.name != box_data.name or existing_box.tag != box_data.tag:
            logger.warning(
                "box_registration_mismatch",
                box_id=str(box_id),
                existing_name=existing_box.name,
                provided_name=box_data.name,
                existing_tag=existing_box.tag,
                provided_tag=box_data.tag,
            )
            warning = (
                f"Box exists with different name/tag. "
                f"Returning existing: name='{existing_box.name}', "
                f"tag='{existing_box.tag}'"
            )
        else:
            warning = "Box already registered"

        logger.info(
            "box_already_registered",
            box_id=str(box_id),
            name=existing_box.name,
        )
        return structures.BoxDetailWithAPIKey(
            id=existing_box.id,
            name=existing_box.name,
            tag=existing_box.tag,
            api_key=api_key,
            warning=warning,
        )

    # Box doesn't exist - creating one mints a new key, so require the
    # registration secret. The recovery branch above never reaches here.
    await dependencies.verify_registration_secret_header(x_registration_secret)

    try:
        box_data_dict = box_data.model_dump() | {
            "created_at": now,
            "last_seen": None,
        }

        await db_service.create(
            target=db.defs.Box,
            data=box_data_dict,
        )

        logger.info(
            "box_registered",
            box_id=str(box_id),
            name=box_data.name,
            tag=box_data.tag,
        )

        return structures.BoxDetailWithAPIKey(
            id=box_data.id,
            name=box_data.name,
            tag=box_data.tag,
            api_key=api_key,
        )

    except IntegrityError as e:
        logger.exception(
            "box_registration_integrity_error",
            error=str(e),
            box_id=str(box_id),
            tag=box_data.tag,
        )
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail={
                "code": errors.ErrorCode.UNIQUE_CONSTRAINT,
                "message": "Box registration failed due to a constraint violation.",
                "details": {"error": str(e.orig) if hasattr(e, "orig") else str(e)},
            },
        ) from e

    except Exception as e:
        logger.exception(
            "box_registration_failed",
            error=str(e),
            error_type=type(e).__name__,
            box_id=str(box_id),
            tag=box_data.tag,
        )
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "code": errors.ErrorCode.INTERNAL_ERROR,
                "message": "An unexpected error occurred during box registration.",
                "details": {"error_type": type(e).__name__},
            },
        ) from e


@router.post("/{box_id}/lobby/session", status_code=status.HTTP_201_CREATED)
async def create_lobby_session(
    box_id: UUID,
    authenticated_box: dependencies.BoxAuthenticatedWithPath,
    authenticated_player: dependencies.AuthenticatedPlayer,
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> structures.BoxSessionDetail:
    """
    Create a lobby session for a logged-in player.

    **Authentication**: Requires both Box API key AND Player JWT token.

    Lobby sessions are long-lived sessions that persist while a player is logged in
    but not actively playing a game. They receive user-scoped events like:
    - credit/earn, credit/spend (credit transactions)
    - user/login, user/logout (authentication events)

    The lobby session is closed when the player logs out.

    Headers:
        X-Box-API-Key: Box API key for authentication
        Authorization: Bearer <player_jwt_token>
    """
    if box_id != authenticated_box.id:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={
                "code": errors.ErrorCode.VALIDATION_ERROR,
                "message": "Box ID in path does not match authenticated box.",
            },
        )

    session_id = uuid4()

    logger.info(
        "creating_lobby_session",
        session_id=str(session_id),
        box_id=str(box_id),
        player_id=str(authenticated_player),
    )

    await db_service.create(
        target=db.defs.BoxSession,
        data={
            "id": session_id,
            "box_id": box_id,
            "host_player_id": authenticated_player,
            "player_ids": [str(authenticated_player)],
            "game_tag": "lobby",
            "session_type": "lobby",
            "start_time": now,
        },
    )

    return structures.BoxSessionDetail(id=session_id, game_tag="lobby")


@router.put("/{box_id}/session/{session_id}", status_code=status.HTTP_202_ACCEPTED)
async def create_box_session(  # noqa: PLR0913  # FastAPI dependency injection
    box_id: UUID,
    session_id: UUID,
    game_tag: Annotated[str, Query()],  # Required: Game type identifier
    authenticated_box: dependencies.BoxAuthenticatedWithPath,
    db_service: dependencies.Database,
    now: dependencies.Now,
    player_id: Annotated[
        UUID | None, Header()
    ] = None,  # Optional: Required for logged-in play, None for practice mode
    player_ids: Annotated[
        str | None, Query()
    ] = None,  # Optional JSON array of player IDs for multiplayer
) -> structures.BoxSessionDetail:
    """
    Create a game session for single or multiple players, or practice mode.

    **Authentication**: Requires Box API key.

    For practice mode (anonymous gameplay):
    - Omit player_id header
    - Pass game_tag as query parameter: ?game_tag=racing
    - Session will have host_player_id=NULL, session_type="practice"

    For single-player games (Racing, Mining):
    - Pass player_id via header
    - Pass game_tag as query parameter: ?game_tag=racing
    - player_ids will be set to [player_id]

    For multiplayer games (Carrom):
    - Pass player_ids as JSON array via query parameter:
      ?player_ids=["uuid1","uuid2","uuid3"]
    - Pass game_tag as query parameter: ?game_tag=carrom
    - First player in array becomes host_player_id

    Headers:
        X-Box-API-Key: Box API key for authentication
        Player-Id: (Optional) Player UUID for logged-in play
    """
    if box_id != authenticated_box.id:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={
                "code": errors.ErrorCode.VALIDATION_ERROR,
                "message": "Box ID in path does not match authenticated box.",
            },
        )
    # Determine session type and player list
    if player_id is None and (player_ids is None or player_ids == ""):
        # Practice mode: No player information provided
        session_type = "practice"
        host_id = None
        player_id_list = []
    elif player_ids is not None:
        # Multiplayer: Parse JSON array of player IDs
        try:
            player_id_list = json.loads(player_ids)
            if not isinstance(player_id_list, list) or len(player_id_list) == 0:
                msg = "player_ids must be a non-empty array"
                raise ValueError(msg)  # noqa: TRY301  # caught below for fallback
            host_id = UUID(player_id_list[0])  # First player is host
            session_type = "game"
        except (json.JSONDecodeError, ValueError, IndexError) as e:
            logger.exception("invalid_player_ids", error=str(e), player_ids=player_ids)
            # Fall back to single player from header
            if player_id is not None:
                player_id_list = [str(player_id)]
                host_id = player_id
                session_type = "game"
            else:
                # No valid player data - treat as practice mode
                player_id_list = []
                host_id = None
                session_type = "practice"
    # Single-player: Use player from header
    elif player_id is not None:
        player_id_list = [str(player_id)]
        host_id = player_id
        session_type = "game"
    else:
        # Practice mode
        player_id_list = []
        host_id = None
        session_type = "practice"

    logger.info(
        "creating_session",
        session_id=str(session_id),
        box_id=str(box_id),
        host_player_id=str(host_id) if host_id else "None",
        player_count=len(player_id_list),
        game_tag=game_tag,
        session_type=session_type,
    )

    await db_service.create(
        target=db.defs.BoxSession,
        data={
            "id": session_id,
            "box_id": box_id,
            "host_player_id": host_id,  # NULL for practice mode
            "player_ids": player_id_list,  # Empty array for practice mode
            "game_tag": game_tag,
            "session_type": session_type,  # "game" | "practice"
            "start_time": now,
        },
    )
    return structures.BoxSessionDetail(id=session_id, game_tag=game_tag)


@router.post("/session/{session_id}", status_code=status.HTTP_201_CREATED)
async def add_session_event(
    event: structures.SessionEventBase,
    session_id: UUID,
    authenticated_box: dependencies.BoxAuthenticatedBySession,  # noqa: ARG001  # auth gate only
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> structures.Identifiable:
    """
    Add an event to a session with payload validation.

    **Authentication**: Requires Box API key.

    Events are validated against game-specific schemas when available.
    Unknown event types are rejected to prevent typos and data corruption.

    Headers:
        X-Box-API-Key: Box API key for authentication
    """
    if not game_validation.is_valid_event_type(event.type):
        logger.warning(
            "invalid_event_type",
            event_type=event.type,
            session_id=str(session_id),
        )
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail={
                "code": errors.ErrorCode.VALIDATION_ERROR,
                "message": f"Unknown event type: '{event.type}'",
                "details": {
                    "event_type": event.type,
                    "valid_event_types": game_validation.get_all_event_types(),
                },
            },
        )

    is_valid, error_msg, validated_payload = game_validation.validate_event_payload(
        event.type, event.payload
    )

    if not is_valid:
        logger.warning(
            "invalid_event_payload",
            event_type=event.type,
            session_id=str(session_id),
            error=error_msg,
        )
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_ENTITY,
            detail={
                "code": errors.ErrorCode.VALIDATION_ERROR,
                "message": (
                    f"Invalid payload for event type '{event.type}': {error_msg}"
                ),
                "details": {"event_type": event.type},
            },
        )

    final_payload = (
        validated_payload if validated_payload is not None else event.payload
    )

    if event.type in (CoreEvent.CREDIT_EARN, CoreEvent.CREDIT_SPEND):
        logger.info(
            "credit_event_payload",
            event_type=event.type,
            payload=final_payload,
            session_id=str(session_id),
        )

    new_id = uuid4()
    await db_service.create(
        target=db.defs.BoxSessionEvent,
        data={
            "id": new_id,
            "session_id": session_id,
            "type": event.type,
            "timestamp": now,
            "payload": final_payload,
        },
    )

    # Explicitly commit before returning to ensure the event is visible to
    # subsequent queries

    await db_service.session.commit()

    logger.info(
        "session_event_created",
        event_id=str(new_id),
        session_id=str(session_id),
        event_type=event.type,
    )

    return structures.Identifiable(id=new_id)


@router.post("/session/{session_id}/close", status_code=status.HTTP_200_OK)
async def close_box_session(
    session_id: UUID,
    authenticated_box: dependencies.BoxAuthenticatedBySession,  # noqa: ARG001  # auth gate only
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> structures.Identifiable:
    """
    Close an activity session by setting end_time.

    **Authentication**: Requires Box API key.

    Call this when a game exits to properly close the session.

    Headers:
        X-Box-API-Key: Box API key for authentication
    """
    logger.info("closing_session", session_id=str(session_id))

    await db_service.session.execute(
        update(db.defs.BoxSession)
        .where(db.defs.BoxSession.id == session_id)
        .values(end_time=now)
    )
    await db_service.session.commit()

    return structures.Identifiable(id=session_id)


@router.get("/session/{session_id}", response_model=structures.BoxSession)
async def get_box_session(
    session_id: UUID,
    db_service: dependencies.Database,
    include_events: Annotated[bool, Query()] = False,  # noqa: FBT002  # FastAPI query param
) -> structures.BoxSession:
    query = select(db.defs.BoxSession).where(db.defs.BoxSession.id == session_id)
    if include_events:
        query = query.options(joinedload(db.defs.BoxSession.events))

    result = (await db_service.session.execute(query)).unique().scalar_one()

    if not include_events:
        # Mark the relationship loaded-empty instead of actually loading it,
        # so model_validate's from_attributes access below doesn't trigger a
        # lazy load - which raises MissingGreenlet on an async session.
        set_committed_value(result, "events", [])

    return structures.BoxSession.model_validate(
        result,
        from_attributes=True,
    )
