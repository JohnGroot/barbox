"""Pydantic models for Nines game events and API responses."""

from datetime import datetime
from typing import Literal

from pydantic import BaseModel, Field

NinesEventType = Literal["nines/jackpot_won",]

# Canonical event type name for generic access
EventType = NinesEventType


class NinesJackpotWonPayload(BaseModel):
    """Payload for nines/jackpot_won event."""

    venue_name: str = Field(description="Venue where jackpot was won")
    player_id: str = Field(description="Player who won the jackpot")
    jackpot_amount: int = Field(description="Credits won")
    timestamp: str = Field(description="ISO 8601 timestamp of win")


class NinesJackpotResponse(BaseModel):
    """
    Response from jackpot query endpoint.
    Contains the last win timestamp used for time-based jackpot calculation.

    Client calculates jackpot amount as:
    BaseJackpotValue + (DaysSinceLastWin * DailyJackpotGrowth)
    """

    venue_name: str = Field(description="Venue identifier")
    last_win_timestamp: datetime | None = Field(
        description="Timestamp of last jackpot win at this venue (None if never won)"
    )
    last_winner_name: str | None = Field(
        default=None, description="Display name of last jackpot winner"
    )
    last_jackpot_amount: int | None = Field(
        default=None, description="Amount of last jackpot won"
    )
