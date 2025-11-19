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
type PlayerIdArray = list[str]  # JSON array of player UUID strings


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
        PlayerIdArray: sqlite.JSON,
    }


def fk_to(model: type[Base]) -> MappedColumn[Any]:
    """Helper to create foreign key columns with correct target table name."""
    name = f"{model.__tablename__}.id"
    return mapped_column(ForeignKey(name))


class Box(Base):
    name: Mapped[str]
    tag: Mapped[str]
    api_key_hash: Mapped[str]  # bcrypt hash of API key (secure storage)
    api_key_hash_lookup: Mapped[str] = mapped_column(index=True, unique=True)  # SHA256 hash for fast O(1) DB lookups (indexed)
    created_at: Mapped[datetime]
    last_seen: Mapped[datetime | None]


type BoxFk = Annotated[UUID, fk_to(Box)]


class Game(Base):
    name: Mapped[str]
    tag: Mapped[str]


class Player(Base):
    tag: Mapped[str]
    origin_id: Mapped[BoxFk]
    pin_hash: Mapped[str]  # bcrypt hash of player PIN
    phone_number: Mapped[str]  # E.164 format phone number (e.g., +15551234567)


class BoxSession(Base):
    box_id: Mapped[BoxFk]
    host_player_id: Mapped[Annotated[UUID, fk_to(Player)] | None]  # Primary player who created session (nullable for practice mode)
    player_ids: Mapped[PlayerIdArray]  # All players (single-player: ["uuid"], multi-player: ["uuid1", "uuid2", ...])
    game_tag: Mapped[str]  # Game type identifier (e.g., "carrom", "racing", "mining", "lobby")
    session_type: Mapped[str]  # Session type: "lobby" | "game" | "practice"
    start_time: Mapped[datetime]
    end_time: Mapped[datetime | None]
    events: Mapped[list["BoxSessionEvent"]] = relationship(back_populates="session")


class BoxSessionEvent(Base):
    session_id: Mapped[Annotated[UUID, fk_to(BoxSession)]]
    type: Mapped[str]
    timestamp: Mapped[datetime]
    payload: Mapped[JsonObject]
    session: Mapped["BoxSession"] = relationship(back_populates="events")
