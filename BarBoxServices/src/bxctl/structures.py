from datetime import datetime
from typing import Annotated, Literal
from uuid import UUID

from pydantic import BaseModel, Field, PlainSerializer


class Named(BaseModel):
    name: str


class Tagged(BaseModel):
    tag: str


class Identifiable(BaseModel):
    id: UUID


class BoxCreate(Named, Tagged):
    pass


class BoxDetail(Identifiable):
    pass


class PlayerCreate(Tagged):
    origin_id: UUID


class PlayerDetail(Identifiable):
    pass


class GameCreate(Named, Tagged):
    pass


class GameDetail(Identifiable):
    pass


type SessionEventType = Literal[
    "begin",
    "browse",
    "playthrough/begin",
    "playthrough/score",
    "playthrough/finish",
    "quit",
]


class SessionEventBase(BaseModel):
    type: SessionEventType
    timestamp: Annotated[
        datetime,
        Field(default_factory=datetime.now),
        PlainSerializer(lambda v: v.isoformat()),
    ]


type GameTag = Literal["carrom"]


class SessionStart(SessionEventBase):
    event_type: Literal["begin"] = "begin"


class SessionEnd(SessionEventBase):
    event_type: Literal["quit"] = "quit"


class SessionBrowse(SessionEventBase):
    event_type: Literal["browse"] = "browse"


class SessionEvent(BaseModel):
    event: Annotated[
        SessionStart | SessionEnd | SessionBrowse,
        Field(discriminator="event_type"),
    ]


class BoxSession(Identifiable):
    box_id: UUID
    player_id: UUID
    start_time: Annotated[datetime, Field(default_factory=datetime.now)]
    events: Annotated[
        list[SessionEvent],
        Field(default_factory=lambda: [SessionStart()]),
    ]
