from datetime import datetime
from typing import Annotated, Any
from uuid import UUID, uuid4

from sqlalchemy import ForeignKey
from sqlalchemy.dialects import sqlite
from sqlalchemy.orm import (
    DeclarativeBase,
    Mapped,
    MappedAsDataclass,
    MappedColumn,
    declared_attr,
    mapped_column,
    relationship,
)

type JsonObject = dict[str, Any]
type JsonArray = list[JsonObject]


class PkMixin:
    # defined as a mixin to avoid kerfuffles with Base's generated __init__ signature
    # (kerfuffle example: id seems 'required', but in reality has a default)
    id: Mapped[Annotated[UUID, mapped_column(primary_key=True, default_factory=uuid4)]]


class Base(PkMixin, MappedAsDataclass, DeclarativeBase):
    @declared_attr.directive
    def __tablename__(cls) -> str:  # noqa: N805
        # Convert PascalCase to snake_case
        return "".join(
            f"_{c.lower()}" if c.isupper() and i > 0 else c.lower()
            for i, c in enumerate(cls.__name__)
        )

    type_annotation_map = {  # noqa: RUF012
        JsonObject: sqlite.JSON,
        JsonArray: sqlite.JSON,
    }


def fk_to(model: type[Base]) -> MappedColumn[Any]:
    """Helper to create foreign key columns with correct target table name."""
    name = f"{model.__tablename__}.id"
    return mapped_column(ForeignKey(name))


class Box(Base):
    name: Mapped[str]
    tag: Mapped[str]


type BoxFk = Annotated[UUID, fk_to(Box)]


class Game(Base):
    name: Mapped[str]
    tag: Mapped[str]


class Player(Base):
    tag: Mapped[str]
    origin_id: Mapped[BoxFk]


class BoxSession(Base):
    box_id: Mapped[BoxFk]
    player_id: Mapped[Annotated[UUID, fk_to(Player)]]
    start_time: Mapped[datetime]
    end_time: Mapped[datetime | None]
    events: Mapped[list["BoxSessionEvent"]] = relationship(back_populates="session")


class BoxSessionEvent(Base):
    session_id: Mapped[Annotated[UUID, fk_to(BoxSession)]]
    type: Mapped[str]
    timestamp: Mapped[datetime]
    payload: Mapped[JsonObject]
    session: Mapped["BoxSession"] = relationship(back_populates="events")
