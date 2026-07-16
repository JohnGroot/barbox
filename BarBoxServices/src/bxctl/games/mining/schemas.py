"""Pydantic models for Mining game events and API responses."""

from datetime import datetime
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, Field, field_validator

# ============= CONSTANTS =============

GEM_TYPES = ["ruby", "sapphire", "emerald", "diamond", "amethyst"]


# ============= EVENT TYPES =============

MiningEventType = Literal[
    "mining/extract_complete",
    "mining/upgrade_purchase",
    "mining/credit_deposit",
    "mining/first_time_bonus",
]

# Canonical event type name for generic access
EventType = MiningEventType


# ============= EVENT PAYLOADS =============


class MiningExtractCompletePayload(BaseModel):
    """Payload for mining/extract_complete event."""

    gem_type: str  # "ruby", "sapphire", etc.
    quantity: int
    location_id: str


class MiningUpgradePurchasePayload(BaseModel):
    """Payload for mining/upgrade_purchase event."""

    upgrade_type: str  # "capacity", "mining_speed", "mining_amount"
    level: int
    cost: dict[str, int]  # {gem_type: quantity}
    location_id: str  # Venue name where upgrade was purchased (e.g., "best_intentions")


class MiningCreditDepositPayload(BaseModel):
    """Payload for mining/credit_deposit event."""

    gem_type: str  # Type of gem spent (e.g., "ruby", "sapphire")
    gems_spent: int  # Number of gems spent
    credits_earned: int  # Machine credits earned
    location_id: str  # Venue name where deposit occurred


class MiningFirstTimeBonusPayload(BaseModel):
    """Payload for mining/first_time_bonus event."""

    location_id: str  # Venue name where bonus was granted
    bonus_gems: dict[str, int]  # {gem_type: quantity} granted as bonus


# ============= API RESPONSE MODELS =============


class MiningInventoryResponse(BaseModel):
    """Player's mining inventory response."""

    player_id: UUID
    gems: dict[str, int]  # {gem_type: quantity}
    last_updated: datetime


class MiningUpgradesResponse(BaseModel):
    """Player's mining upgrades response for a specific location."""

    player_id: UUID
    location_id: str  # Venue name
    upgrades: dict[str, int]  # {upgrade_type: level} - scoped to this location
    last_updated: datetime


class MiningTimestampResponse(BaseModel):
    """Last mining timestamp response."""

    player_id: UUID
    location_id: str
    last_mining_time: datetime | None  # None = no extraction history


class MiningMetadataResponse(BaseModel):
    """Player mining metadata response for a specific location."""

    player_id: UUID
    location_id: str  # Venue name
    has_received_bonus_at_location: (
        bool  # Whether first-time bonus received at this location
    )
    total_events: int  # Total events at this location
    first_event_time: datetime | None
    last_event_time: datetime | None


class MiningStateResponse(BaseModel):
    """
    Unified mining state response combining all player data.

    Data Scoping:
    - GLOBAL (all locations): inventory (gems from all locations combined)
    - LOCATION-SPECIFIC: upgrades, last_extraction_time, metadata
    """

    player_id: UUID
    location_id: str  # Venue name for location-scoped data
    inventory: dict[str, int]  # {gem_type: quantity} - GLOBAL across all locations
    upgrades: dict[str, int]  # {upgrade_type: level} - LOCATION-SPECIFIC
    last_extraction_time: (
        datetime | None
    )  # None = no extraction history - LOCATION-SPECIFIC
    metadata: MiningMetadataResponse  # LOCATION-SPECIFIC (bonus status per location)


# ============= LOCATION REGISTRATION MODELS =============


class MiningLocationResponse(BaseModel):
    """Response from location registration/query."""

    venue_name: str = Field(description="Venue identifier (e.g., 'best_intentions')")
    gem_type: Literal["ruby", "sapphire", "emerald", "diamond", "amethyst"]
    display_name: str = Field(description="Human-readable location name")

    @field_validator("gem_type")
    @classmethod
    def validate_gem_type(cls, v: str) -> str:
        if v not in GEM_TYPES:
            msg = f"Invalid gem type: {v}. Must be one of {GEM_TYPES}"
            raise ValueError(msg)
        return v


class MiningLocationListResponse(BaseModel):
    """List of all registered mining locations with distribution stats."""

    locations: list[MiningLocationResponse]
    gem_distribution: dict[str, int] = Field(
        description="Count of locations per gem type",
        examples=[
            {"ruby": 5, "sapphire": 4, "emerald": 3, "diamond": 2, "amethyst": 1}
        ],
    )
