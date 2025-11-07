"""Pydantic models for Carrom game events and API responses."""

from datetime import datetime
from typing import Literal
from uuid import UUID

from pydantic import BaseModel


# ============= EVENT PAYLOADS =============

class CarromRoundFinishPayload(BaseModel):
    """Payload for carrom/round_finish event."""
    mode: Literal["practice", "competitive"]
    winner: str  # player_id
    scores: dict[str, int]  # {player_id: score}


# ============= API RESPONSE MODELS =============

class CarromLeaderboardEntry(BaseModel):
    """Single entry in Carrom leaderboard."""
    player_id: UUID
    username: str
    total_score: int
    total_wins: int | None = None
    entry_date: datetime


class CarromLeaderboardResponse(BaseModel):
    """Carrom leaderboard response."""
    metric: str  # "total_score" or "total_wins"
    leaderboard: list[CarromLeaderboardEntry]
