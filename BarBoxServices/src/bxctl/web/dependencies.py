from collections.abc import AsyncIterator
from datetime import UTC, datetime
from typing import Annotated

from fastapi import Depends

from bxctl import db


async def _acquire_crud() -> AsyncIterator[db.service.CRUD]:
    async with db.connectivity.db_session() as session:
        yield db.service.CRUD(session)


Database = Annotated[db.service.CRUD, Depends(_acquire_crud)]


def _current_timestamp() -> datetime:
    return datetime.now(tz=UTC)


Now = Annotated[datetime, Depends(_current_timestamp, use_cache=False)]
