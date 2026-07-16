"""Pydantic models for Racing game events and API responses."""

from datetime import datetime
from typing import Literal
from uuid import UUID

from pydantic import BaseModel

# ============= EVENT TYPES =============

RacingEventType = Literal[
    "racing/lap_complete",
    "racing/checkpoint",
    "racing/race_finish",
]

# Canonical event type name for generic access
EventType = RacingEventType


# ============= EVENT PAYLOADS =============


class CheckpointData(BaseModel):
    """Checkpoint data for racing events."""

    index: int
    time: float
    gap: float


class RacingLapCompletePayload(BaseModel):
    """Payload for racing/lap_complete event."""

    lap_num: int
    lap_time: float
    checkpoints: list[CheckpointData]


class RacingRaceFinishPayload(BaseModel):
    """Payload for racing/race_finish event."""

    track_id: str
    total_time: float
    total_laps: int
    lap_times: list[float]
    checkpoints: list[CheckpointData] | None = None


# ============= API RESPONSE MODELS =============


class RacingLeaderboardEntry(BaseModel):
    """Single entry in Racing leaderboard."""

    player_id: UUID
    username: str
    metric_value: float  # lap time or race time
    entry_date: datetime
    lap_times: list[float] | None = None


class RacingLeaderboardResponse(BaseModel):
    """Racing leaderboard response."""

    track_id: str
    metric: str  # "best_lap" or "best_race"
    leaderboard: list[RacingLeaderboardEntry]
