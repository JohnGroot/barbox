from datetime import datetime
from typing import Annotated, Any
from uuid import UUID, uuid4

from sqlalchemy import ForeignKey
from sqlalchemy.dialects import sqlite
from sqlalchemy.orm import (
    DeclarativeBase,
    Mapped,
    MappedAsDataclass,
    declared_attr,
    mapped_column,
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
        return cls.__name__.lower()

    type_annotation_map = {  # noqa: RUF012
        JsonObject: sqlite.JSON,
        JsonArray: sqlite.JSON,
    }


class Box(Base):
    name: Mapped[str]
    tag: Mapped[str]


type BoxFk = Annotated[UUID, mapped_column(ForeignKey("box.id"))]


class Game(Base):
    name: Mapped[str]
    tag: Mapped[str]


type GameFk = Annotated[UUID, mapped_column(ForeignKey("game.id"))]


class Player(Base):
    tag: Mapped[str]
    origin_id: Mapped[BoxFk]


type PlayerFk = Annotated[UUID, mapped_column(ForeignKey("player.id"))]


class PlayThrough(Base):
    player_id: Mapped[PlayerFk]
    game_id: Mapped[GameFk]
    box_id: Mapped[BoxFk]
    payload: Mapped[JsonObject]
    start_time: Mapped[datetime]
    end_time: Mapped[datetime | None]


class BoxSession(Base):
    box_id: Mapped[BoxFk]
    player_id: Mapped[PlayerFk]
    start_time: Mapped[datetime]
    end_time: Mapped[datetime | None]
    events: Mapped[JsonArray]
