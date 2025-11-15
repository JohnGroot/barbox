import json
from typing import Annotated
from uuid import UUID, uuid4

from fastapi import APIRouter, Header, HTTPException, Query, status
from sqlalchemy import select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import joinedload
from structlog import get_logger

from bxctl import db, structures
from bxctl.games import validation as game_validation

from . import dependencies

logger = get_logger()
router = APIRouter(prefix="/box")


@router.post("/", status_code=201)
async def create_box(
    new_box: structures.BoxCreate,
    db_service: dependencies.Database,
) -> structures.BoxDetail:
    """Create a new box.

    Standard creation endpoint for test suites and API clients.
    Validates that:
    - Box ID is unique
    - Box tag (identifier) is unique

    Returns:
        201: Box created successfully
        409: Box already exists (duplicate ID or tag)
        500: Internal server error

    For idempotent box registration, use PUT /box/{box_id} instead.
    """

    # Check if box already exists (either by ID or tag)
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
                "code": structures.ErrorCode.DUPLICATE_RESOURCE,
                "message": (
                    f"Box already exists with {'ID' if existing_box.id == new_box.id else 'tag'} "
                    f"'{new_box.id if existing_box.id == new_box.id else new_box.tag}'."
                ),
                "details": {
                    "conflict_field": "id" if existing_box.id == new_box.id else "tag",
                    "conflict_value": str(new_box.id) if existing_box.id == new_box.id else new_box.tag,
                },
            },
        )

    # Attempt to create box
    try:
        result = await db_service.create(
            target=db.defs.Box,
            data=new_box,
            read_as=structures.BoxDetail,
        )
        logger.info(
            "box_created",
            box_id=str(new_box.id),
            name=new_box.name,
            tag=new_box.tag,
        )
        return result

    except IntegrityError as e:
        # Catch any database constraint violations that weren't caught above
        logger.error(
            "box_creation_integrity_error",
            error=str(e),
            box_id=str(new_box.id),
            tag=new_box.tag,
        )
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail={
                "code": structures.ErrorCode.UNIQUE_CONSTRAINT,
                "message": "Box creation failed due to a constraint violation.",
                "details": {"error": str(e.orig) if hasattr(e, 'orig') else str(e)},
            },
        )

    except Exception as e:
        # Catch unexpected errors
        logger.error(
            "box_creation_failed",
            error=str(e),
            error_type=type(e).__name__,
            box_id=str(new_box.id),
            tag=new_box.tag,
        )
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "code": structures.ErrorCode.INTERNAL_ERROR,
                "message": "An unexpected error occurred during box creation.",
                "details": {"error_type": type(e).__name__},
            },
        )


@router.put("/{box_id}", status_code=200)
async def register_box(
    box_id: UUID,
    box_data: structures.BoxCreate,
    db_service: dependencies.Database,
) -> structures.BoxDetail:
    # Check if box already exists
    result = await db_service.session.execute(
        select(db.defs.Box).where(db.defs.Box.id == box_id)
    )
    existing_box = result.scalar_one_or_none()

    if existing_box is not None:
        # Box already exists - return existing (idempotent)
        logger.info("box_already_registered", box_id=str(box_id))
        return structures.BoxDetail.model_validate(existing_box, from_attributes=True)

    # Box doesn't exist - create it
    logger.info("registering_new_box", box_id=str(box_id), name=box_data.name)
    return await db_service.create(
        target=db.defs.Box,
        data=box_data,
        read_as=structures.BoxDetail,
    )


@router.post("/{box_id}/lobby/session", status_code=status.HTTP_201_CREATED)
async def create_lobby_session(
    box_id: UUID,
    player_id: Annotated[UUID, Header()],
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> structures.BoxSessionDetail:
    """
    Create a lobby session for a logged-in player.

    Lobby sessions are long-lived sessions that persist while a player is logged in
    but not actively playing a game. They receive user-scoped events like:
    - credit/earn, credit/spend (credit transactions)
    - user/login, user/logout (authentication events)

    The lobby session is closed when the player logs out.
    """
    session_id = uuid4()  # Server-generated session ID

    logger.info(
        "creating_lobby_session",
        session_id=str(session_id),
        box_id=str(box_id),
        player_id=str(player_id),
    )

    await db_service.create(
        target=db.defs.BoxSession,
        data={
            "id": session_id,
            "box_id": box_id,
            "host_player_id": player_id,
            "player_ids": [str(player_id)],
            "game_tag": "lobby",
            "session_type": "lobby",
            "start_time": now,
        },
    )

    return structures.BoxSessionDetail(id=session_id, game_tag="lobby")


@router.put("/{box_id}/session/{session_id}", status_code=status.HTTP_202_ACCEPTED)
async def create_box_session(
    box_id: UUID,
    session_id: UUID,
    game_tag: Annotated[str, Query()],  # Required: Game type identifier
    db_service: dependencies.Database,
    now: dependencies.Now,
    player_id: Annotated[UUID | None, Header()] = None,  # Optional: Required for logged-in play, None for practice mode
    player_ids: Annotated[str | None, Query()] = None,  # Optional JSON array of player IDs for multiplayer
) -> structures.BoxSessionDetail:
    """
    Create a game session for single or multiple players, or practice mode.

    For practice mode (anonymous gameplay):
    - Omit player_id header
    - Pass game_tag as query parameter: ?game_tag=racing
    - Session will have host_player_id=NULL, session_type="practice"

    For single-player games (Racing, Mining):
    - Pass player_id via header
    - Pass game_tag as query parameter: ?game_tag=racing
    - player_ids will be set to [player_id]

    For multiplayer games (Carrom):
    - Pass player_ids as JSON array via query parameter: ?player_ids=["uuid1","uuid2","uuid3"]
    - Pass game_tag as query parameter: ?game_tag=carrom
    - First player in array becomes host_player_id
    """
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
                raise ValueError("player_ids must be a non-empty array")
            host_id = UUID(player_id_list[0])  # First player is host
            session_type = "game"
        except (json.JSONDecodeError, ValueError, IndexError) as e:
            logger.error("invalid_player_ids", error=str(e), player_ids=player_ids)
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
    else:
        # Single-player: Use player from header
        if player_id is not None:
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
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> structures.Identifiable:
    """
    Add an event to a session with payload validation.

    Events are validated against game-specific schemas when available.
    Unknown event types are rejected to prevent typos and data corruption.
    """
    # Validate event type
    if not game_validation.is_valid_event_type(event.type):
        logger.warning(
            "invalid_event_type",
            event_type=event.type,
            session_id=str(session_id),
        )
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail={
                "code": structures.ErrorCode.VALIDATION_ERROR,
                "message": f"Unknown event type: '{event.type}'",
                "details": {
                    "event_type": event.type,
                    "valid_event_types": game_validation.get_all_event_types(),
                },
            },
        )

    # Validate payload if schema exists
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
                "code": structures.ErrorCode.VALIDATION_ERROR,
                "message": f"Invalid payload for event type '{event.type}': {error_msg}",
                "details": {"event_type": event.type},
            },
        )

    # Use validated payload if available, otherwise original
    final_payload = validated_payload if validated_payload is not None else event.payload

    # Log credit event payloads for debugging
    if event.type in ("credit/earn", "credit/spend"):
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

    # Explicitly commit before returning to ensure event is visible to subsequent queries
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
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> structures.Identifiable:
    """
    Close an activity session by setting end_time.

    Call this when a game exits to properly close the session.
    """
    from sqlalchemy import update

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
) -> structures.BoxSession:
    result = (
        (
            await db_service.session.execute(
                select(db.defs.BoxSession)
                .options(joinedload(db.defs.BoxSession.events))
                .where(db.defs.BoxSession.id == session_id),
            )
        )
        .unique()
        .scalar_one()
    )
    return structures.BoxSession.model_validate(
        result,
        from_attributes=True,
    )
