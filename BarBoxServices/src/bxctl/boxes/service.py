import json
from datetime import datetime
from uuid import UUID, uuid4

from fastapi import HTTPException, status
from sqlalchemy import select, update
from sqlalchemy.orm import joinedload
from sqlalchemy.orm.attributes import set_committed_value
from structlog import get_logger

from bxctl import db, errors
from bxctl.app import auth, dependencies
from bxctl.boxes import schemas
from bxctl.db.defs import SessionType
from bxctl.db.service import CRUD
from bxctl.games import validation as game_validation
from bxctl.registry import CoreEvent
from bxctl.schemas import Identifiable

logger = get_logger()


async def create_box(
    new_box: schemas.BoxCreate,
    db_service: CRUD,
    now: datetime,
) -> schemas.BoxDetailWithAPIKey:
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
    async with errors.creation_error_boundary(
        log_event_stem="box_creation",
        conflict_message="Box creation failed due to a constraint violation.",
        failure_message="An unexpected error occurred during box creation.",
        box_id=str(new_box.id),
        tag=new_box.tag,
    ):
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

        return schemas.BoxDetailWithAPIKey(
            id=new_box.id,
            name=new_box.name,
            tag=new_box.tag,
            api_key=api_key,
        )


async def register_box(
    box_id: UUID,
    box_data: schemas.BoxCreate,
    db_service: CRUD,
    now: datetime,
    x_registration_secret: str | None,
) -> schemas.BoxDetailWithAPIKey:
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
        return schemas.BoxDetailWithAPIKey(
            id=existing_box.id,
            name=existing_box.name,
            tag=existing_box.tag,
            api_key=api_key,
            warning=warning,
        )

    # Box doesn't exist - creating one mints a new key, so require the
    # registration secret. The recovery branch above never reaches here.
    await dependencies.verify_registration_secret_header(x_registration_secret)

    async with errors.creation_error_boundary(
        log_event_stem="box_registration",
        conflict_message="Box registration failed due to a constraint violation.",
        failure_message="An unexpected error occurred during box registration.",
        box_id=str(box_id),
        tag=box_data.tag,
    ):
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

        return schemas.BoxDetailWithAPIKey(
            id=box_data.id,
            name=box_data.name,
            tag=box_data.tag,
            api_key=api_key,
        )


async def create_lobby_session(
    box_id: UUID,
    authenticated_box_id: UUID,
    player_id: UUID,
    db_service: CRUD,
    now: datetime,
) -> schemas.BoxSessionDetail:
    if box_id != authenticated_box_id:
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
        player_id=str(player_id),
    )

    await db_service.create(
        target=db.defs.BoxSession,
        data={
            "id": session_id,
            "box_id": box_id,
            "host_player_id": player_id,
            "player_ids": [str(player_id)],
            "game_tag": SessionType.LOBBY,
            "session_type": SessionType.LOBBY,
            "start_time": now,
        },
    )

    return schemas.BoxSessionDetail(id=session_id, game_tag=SessionType.LOBBY)


async def create_game_session(  # noqa: PLR0913  # mirrors the endpoint's parameter surface
    box_id: UUID,
    session_id: UUID,
    game_tag: str,
    authenticated_box_id: UUID,
    db_service: CRUD,
    now: datetime,
    player_id: UUID | None,
    player_ids: str | None,
) -> schemas.BoxSessionDetail:
    if box_id != authenticated_box_id:
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
        session_type = SessionType.PRACTICE
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
            session_type = SessionType.GAME
        except (json.JSONDecodeError, ValueError, IndexError) as e:
            logger.exception("invalid_player_ids", error=str(e), player_ids=player_ids)
            # Fall back to single player from header
            if player_id is not None:
                player_id_list = [str(player_id)]
                host_id = player_id
                session_type = SessionType.GAME
            else:
                # No valid player data - treat as practice mode
                player_id_list = []
                host_id = None
                session_type = SessionType.PRACTICE
    # Single-player: Use player from header
    elif player_id is not None:
        player_id_list = [str(player_id)]
        host_id = player_id
        session_type = SessionType.GAME
    else:
        # Practice mode
        player_id_list = []
        host_id = None
        session_type = SessionType.PRACTICE

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
            "session_type": session_type,
            "start_time": now,
        },
    )
    return schemas.BoxSessionDetail(id=session_id, game_tag=game_tag)


async def add_session_event(
    event: schemas.SessionEventBase,
    session_id: UUID,
    db_service: CRUD,
    now: datetime,
) -> Identifiable:
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

    return Identifiable(id=new_id)


async def close_session(
    session_id: UUID,
    db_service: CRUD,
    now: datetime,
) -> Identifiable:
    logger.info("closing_session", session_id=str(session_id))

    await db_service.session.execute(
        update(db.defs.BoxSession)
        .where(db.defs.BoxSession.id == session_id)
        .values(end_time=now)
    )
    await db_service.session.commit()

    return Identifiable(id=session_id)


async def get_session(
    session_id: UUID,
    db_service: CRUD,
    *,
    include_events: bool,
) -> schemas.BoxSession:
    query = select(db.defs.BoxSession).where(db.defs.BoxSession.id == session_id)
    if include_events:
        query = query.options(joinedload(db.defs.BoxSession.events))

    result = (await db_service.session.execute(query)).unique().scalar_one()

    if not include_events:
        # Mark the relationship loaded-empty instead of actually loading it,
        # so model_validate's from_attributes access below doesn't trigger a
        # lazy load - which raises MissingGreenlet on an async session.
        set_committed_value(result, "events", [])

    return schemas.BoxSession.model_validate(
        result,
        from_attributes=True,
    )
