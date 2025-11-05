import json
from typing import Annotated
from uuid import UUID, uuid4

from fastapi import APIRouter, Header, Query, status
from sqlalchemy import select
from sqlalchemy.orm import joinedload
from structlog import get_logger

from bxctl import db, structures

from . import dependencies

logger = get_logger()
router = APIRouter(prefix="/box")


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


@router.put("/{box_id}/session/{session_id}", status_code=status.HTTP_202_ACCEPTED)
async def create_box_session(
    box_id: UUID,
    session_id: UUID,
    player_id: Annotated[UUID, Header()],
    db_service: dependencies.Database,
    now: dependencies.Now,
    player_ids: Annotated[str | None, Query()] = None,  # Optional JSON array of player IDs for multiplayer
) -> structures.Identifiable:
    """
    Create a game session for single or multiple players.

    For single-player games (Racing, Mining):
    - Pass player_id via header only
    - player_ids will be set to [player_id]

    For multiplayer games (Carrom):
    - Pass player_ids as JSON array via query parameter: ?player_ids=["uuid1","uuid2","uuid3"]
    - First player in array becomes host_player_id
    """
    # Determine player list
    if player_ids is not None:
        # Multiplayer: Parse JSON array of player IDs
        try:
            player_id_list = json.loads(player_ids)
            if not isinstance(player_id_list, list) or len(player_id_list) == 0:
                raise ValueError("player_ids must be a non-empty array")
            host_id = UUID(player_id_list[0])  # First player is host
        except (json.JSONDecodeError, ValueError, IndexError) as e:
            logger.error("invalid_player_ids", error=str(e), player_ids=player_ids)
            # Fall back to single player from header
            player_id_list = [str(player_id)]
            host_id = player_id
    else:
        # Single-player: Use player from header
        player_id_list = [str(player_id)]
        host_id = player_id

    logger.info(
        "creating_session",
        session_id=str(session_id),
        box_id=str(box_id),
        host_player_id=str(host_id),
        player_count=len(player_id_list),
    )

    await db_service.create(
        target=db.defs.BoxSession,
        data={
            "id": session_id,
            "box_id": box_id,
            "host_player_id": host_id,
            "player_ids": player_id_list,
            "start_time": now,
        },
    )
    return structures.Identifiable(id=session_id)


@router.post("/session/{session_id}", status_code=status.HTTP_201_CREATED)
async def add_session_event(
    event: structures.SessionEventBase,
    session_id: UUID,
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> structures.Identifiable:
    new_id = uuid4()
    await db_service.create(
        target=db.defs.BoxSessionEvent,
        data=event.model_dump()
        | {"session_id": session_id, "id": new_id, "timestamp": now},
    )
    return structures.Identifiable(id=new_id)


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
