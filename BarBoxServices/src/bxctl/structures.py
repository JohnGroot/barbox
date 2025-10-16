from datetime import UTC, datetime
from typing import Annotated, Literal
from uuid import UUID

from pydantic import BaseModel, Field, PlainSerializer


class Named(BaseModel):
    name: str


class Tagged(BaseModel):
    tag: str


class Identifiable(BaseModel):
    id: UUID


class BoxCreate(Identifiable, Named, Tagged):
    pass


class BoxDetail(Identifiable):
    pass


class PlayerCreate(Identifiable, Tagged):
    origin_id: UUID


class PlayerDetail(Identifiable):
    pass


class GameCreate(Named, Tagged):
    pass


class GameDetail(Identifiable):
    pass


SessionEventType = Literal[
    "play/begin",
    "play/score",
    "play/finish",
    "quit",
]


class SessionEventBase(BaseModel):
    type: SessionEventType
    timestamp: Annotated[
        datetime,
        Field(default_factory=lambda: datetime.now(UTC)),
        PlainSerializer(lambda v: v.isoformat()),
    ]


type GameTag = Literal["carrom"]


class BeginPlay(SessionEventBase):
    type: Literal["play/begin"]
    game: GameTag


class EndPlay(SessionEventBase):
    type: Literal["play/finish"]
    game: GameTag


class Score(SessionEventBase):
    type: Literal["play/score"]
    points: int


class SessionEvent(BaseModel):
    event: Annotated[
        BeginPlay | EndPlay | Score,
        Field(discriminator="type"),
    ]


class BoxSession(Identifiable):
    box_id: UUID
    player_id: UUID
    start_time: Annotated[datetime, Field(default_factory=datetime.now)]
    events: Annotated[
        list[SessionEvent],
        Field(default_factory=lambda: [BeginPlay()]),
    ]
