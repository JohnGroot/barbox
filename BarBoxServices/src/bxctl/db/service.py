from typing import overload
from uuid import uuid4

from pydantic import BaseModel
from sqlalchemy import Select, insert
from sqlalchemy.ext.asyncio import AsyncSession

from . import defs


class DbError(Exception):
    pass


class DbCreateFailedError(DbError):
    pass


class CRUD:
    def __init__(self, db_session: AsyncSession) -> None:
        self.session = db_session

    @overload
    async def create[Target: defs.Base, Data: BaseModel](
        self,
        *,
        target: type[Target],
        data: Data,
    ) -> None: ...

    @overload
    async def create[Target: defs.Base, Data: BaseModel, ReadAs: BaseModel](
        self,
        *,
        target: type[Target],
        data: Data,
        read_as: type[ReadAs],
    ) -> ReadAs: ...

    async def create[Target: defs.Base, Data: BaseModel, ReadAs: BaseModel](
        self,
        *,
        target: type[Target],
        data: Data,
        read_as: type[ReadAs] | None = None,
    ) -> ReadAs | None:
        values = data.model_dump()
        values["id"] = values.pop("id", uuid4())
        if result := await self.session.scalar(
            insert(target).values(values).returning(target),
        ):
            return (
                read_as.model_validate(result, from_attributes=True)
                if read_as
                else None
            )
        raise DbCreateFailedError

    async def get[Selection: Select, ReadAs: BaseModel](
        self,
        *,
        selection: Selection,
        read_as: type[ReadAs],
    ) -> ReadAs:
        result = await self.session.execute(selection)
        return read_as.model_validate(result.scalar_one(), from_attributes=True)
