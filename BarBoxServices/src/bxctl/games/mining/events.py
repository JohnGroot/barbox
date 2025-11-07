"""Event type definitions and validation for Mining game."""

from typing import Literal

# Mining event types
MiningEventType = Literal[
    "mining/extract_start",
    "mining/extract_complete",
    "mining/upgrade_purchase",
    "mining/credit_deposit",
    "mining/tick_update",
]

# Event type constants
EXTRACT_START = "mining/extract_start"
EXTRACT_COMPLETE = "mining/extract_complete"
UPGRADE_PURCHASE = "mining/upgrade_purchase"
CREDIT_DEPOSIT = "mining/credit_deposit"
TICK_UPDATE = "mining/tick_update"

# All event types for validation
ALL_EVENT_TYPES = {
    EXTRACT_START,
    EXTRACT_COMPLETE,
    UPGRADE_PURCHASE,
    CREDIT_DEPOSIT,
    TICK_UPDATE,
}


def is_valid_event_type(event_type: str) -> bool:
    """Check if event type is valid for Mining game."""
    return event_type in ALL_EVENT_TYPES
