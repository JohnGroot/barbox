"""Event type definitions and validation for Racing game."""

from typing import Literal

# Racing event types
RacingEventType = Literal[
    "racing/lap_complete",
    "racing/checkpoint",
    "racing/race_finish",
]

# Event type constants
LAP_COMPLETE = "racing/lap_complete"
CHECKPOINT = "racing/checkpoint"
RACE_FINISH = "racing/race_finish"

# All event types for validation
ALL_EVENT_TYPES = {LAP_COMPLETE, CHECKPOINT, RACE_FINISH}


def is_valid_event_type(event_type: str) -> bool:
    """Check if event type is valid for Racing game."""
    return event_type in ALL_EVENT_TYPES
