from datetime import UTC, datetime
from typing import Annotated, Any, Literal
from uuid import UUID

from pydantic import BaseModel, Field, PlainSerializer

from bxctl.registry import SessionEventType
from bxctl.schemas import Identifiable, Named, Tagged


class BoxCreate(Identifiable, Named, Tagged):
    pass


class BoxDetailWithAPIKey(Identifiable, Named, Tagged):
    """Box details with API key - only returned on box creation."""

    api_key: str
    warning: str = "Save this API key securely - it will not be shown again"


class SessionEventBase(BaseModel):
    type: SessionEventType
    timestamp: Annotated[
        datetime,
        Field(default_factory=lambda: datetime.now(UTC)),
        PlainSerializer(lambda v: v.isoformat()),
    ]
    payload: Any


class BeginPlay(SessionEventBase):
    type: Literal["play/begin"]


class BoxSessionDetail(Identifiable):
    """Minimal session response for creation endpoints"""

    game_tag: str


class BoxSession(Identifiable):
    box_id: UUID
    host_player_id: UUID
    player_ids: list[str]  # JSON array of player UUID strings
    game_tag: str  # Game type identifier (e.g., "carrom", "racing", "mining")
    start_time: Annotated[datetime, Field(default_factory=datetime.now)]
    end_time: datetime | None = None
    events: Annotated[
        list[SessionEventBase],
        Field(
            default_factory=lambda: [
                BeginPlay(type="play/begin", timestamp=datetime.now(UTC), payload=None)
            ]
        ),
    ]
