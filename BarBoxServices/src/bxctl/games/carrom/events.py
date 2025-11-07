"""Event type definitions and validation for Carrom game."""

from typing import Literal

# Carrom event types
CarromEventType = Literal[
    "carrom/round_start",
    "carrom/piece_pocketed",
    "carrom/round_finish",
]

# Event type constants
ROUND_START = "carrom/round_start"
PIECE_POCKETED = "carrom/piece_pocketed"
ROUND_FINISH = "carrom/round_finish"

# All event types for validation
ALL_EVENT_TYPES = {ROUND_START, PIECE_POCKETED, ROUND_FINISH}


def is_valid_event_type(event_type: str) -> bool:
    """Check if event type is valid for Carrom game."""
    return event_type in ALL_EVENT_TYPES
