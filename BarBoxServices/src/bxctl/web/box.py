from typing import Annotated
from uuid import UUID, uuid4

from fastapi import APIRouter, Header, status
from sqlalchemy import select
from sqlalchemy.orm import joinedload
from structlog import get_logger

from bxctl import db, structures

from . import dependencies

logger = get_logger()
router = APIRouter(prefix="/box")


@router.post("/", status_code=201)
async def create_box(
    new_box: structures.BoxCreate,
    db_service: dependencies.Database,
) -> structures.BoxDetail:
    return await db_service.create(
        target=db.defs.Box,
        data=new_box,
        read_as=structures.BoxDetail,
    )


@router.put("/{box_id}/session/{session_id}", status_code=status.HTTP_202_ACCEPTED)
async def create_box_session(
    box_id: UUID,
    session_id: UUID,
    player_id: Annotated[UUID, Header()],
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> structures.Identifiable:
    logger.info(
        "session_started",
        box_id=box_id,
        player_id=player_id,
        session_id=session_id,
    )
    await db_service.create(
        target=db.defs.BoxSession,
        data={
            "id": session_id,
            "box_id": box_id,
            "start_time": now,
            "player_id": player_id,
        },
    )
    return structures.Identifiable(id=session_id)


@router.post("/session/{session_id}", status_code=status.HTTP_201_CREATED)
async def add_session_event(
    event: structures.SessionEvent,
    session_id: UUID,
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> structures.Identifiable:
    new_id = uuid4()
    await db_service.create(
        target=db.defs.BoxSessionEvent,
        data={
            "id": new_id,
            "type": str(event.event.type),
            "session_id": session_id,
            "timestamp": now,
            "payload": event.event.model_dump(exclude={"type", "timestamp"}),
        },
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
    return structures.BoxSession(
        box_id=result.box_id,
        player_id=result.player_id,
        start_time=result.start_time,
        id=result.id,
        events=[e.__dict__ for e in result.events],
    )
