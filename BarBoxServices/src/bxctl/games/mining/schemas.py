"""Pydantic models for Mining game events and API responses."""

from datetime import datetime
from typing import Literal
from uuid import UUID

from pydantic import BaseModel


# ============= EVENT TYPES =============

MiningEventType = Literal[
    "mining/extract_start",
    "mining/extract_complete",
    "mining/upgrade_purchase",
    "mining/credit_deposit",
    "mining/tick_update",
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


# ============= API RESPONSE MODELS =============

class MiningInventoryResponse(BaseModel):
    """Player's mining inventory response."""
    player_id: UUID
    gems: dict[str, int]  # {gem_type: quantity}
    last_updated: datetime


class MiningUpgradesResponse(BaseModel):
    """Player's mining upgrades response."""
    player_id: UUID
    upgrades: dict[str, int]  # {upgrade_type: level}
    last_updated: datetime


class MiningTimestampResponse(BaseModel):
    """Last mining timestamp response."""
    player_id: UUID
    location_id: str
    last_mining_time: datetime


class MiningMetadataResponse(BaseModel):
    """Player mining metadata response."""
    player_id: UUID
    has_received_bonus: bool
    total_events: int
    first_event_time: datetime | None
    last_event_time: datetime | None
