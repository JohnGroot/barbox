from typing import Annotated
from uuid import UUID, uuid4

from fastapi import APIRouter, Header, status
from sqlalchemy import select
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


@router.put("/{box_id}/session", status_code=status.HTTP_202_ACCEPTED)
async def create_box_session(
    box_id: UUID,
    player_id: Annotated[UUID, Header()],
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> structures.Identifiable:
    session_id = uuid4()
    logger.info(
        "session_started",
        box_id=box_id,
        player_id=player_id,
        session_id=session_id,
    )
    await db_service.create(
        target=db.defs.BoxSession,
        data=structures.BoxSession(
            id=session_id,
            box_id=box_id,
            player_id=player_id,
            events=[],
            start_time=now,
        ),
    )
    return structures.Identifiable(id=session_id)


@router.post("/session/{session_id}", status_code=status.HTTP_202_ACCEPTED)
async def add_session_event(
    event: structures.SessionEvent,
    session_id: UUID,
) -> structures.Identifiable:
    return structures.Identifiable(id=session_id)


@router.get("/session/{session_id}", response_model=structures.BoxSession)
async def get_box_session(
    session_id: UUID,
    db_service: dependencies.Database,
) -> structures.BoxSession:
    return await db_service.get(
        selection=select(db.defs.BoxSession).where(db.defs.BoxSession.id == session_id),
        read_as=structures.BoxSession,
    )
