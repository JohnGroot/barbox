"""Central event validation for all games."""

from typing import Any

from pydantic import ValidationError

from .carrom import events as carrom_events, schemas as carrom_schemas
from .mining import events as mining_events, schemas as mining_schemas
from .racing import events as racing_events, schemas as racing_schemas


# Mapping of event types to their Pydantic payload models
EVENT_PAYLOAD_MODELS: dict[str, type] = {
    # Carrom events
    carrom_events.ROUND_FINISH: carrom_schemas.CarromRoundFinishPayload,

    # Racing events
    racing_events.LAP_COMPLETE: racing_schemas.RacingLapCompletePayload,
    racing_events.RACE_FINISH: racing_schemas.RacingRaceFinishPayload,

    # Mining events
    mining_events.EXTRACT_COMPLETE: mining_schemas.MiningExtractCompletePayload,
    mining_events.UPGRADE_PURCHASE: mining_schemas.MiningUpgradePurchasePayload,
}


def get_game_from_event_type(event_type: str) -> str | None:
    """Extract game name from event type (e.g., 'carrom/round_finish' -> 'carrom')."""
    if "/" in event_type:
        return event_type.split("/")[0]
    return None


def is_valid_event_type(event_type: str) -> bool:
    """
    Check if event type is valid for any game.

    Returns True if:
    - Event type matches a known game event pattern (game/event_name)
    - The game recognizes this event type
    """
    game = get_game_from_event_type(event_type)

    if game == "carrom":
        return carrom_events.is_valid_event_type(event_type)
    elif game == "racing":
        return racing_events.is_valid_event_type(event_type)
    elif game == "mining":
        return mining_events.is_valid_event_type(event_type)
    elif event_type in {"play/begin", "play/score", "play/finish", "quit", "user/login", "user/logout", "credit/spend", "credit/earn"}:
        # Core event types are always valid
        return True

    return False


def validate_event_payload(event_type: str, payload: dict[str, Any]) -> tuple[bool, str | None, dict | None]:
    """
    Validate event payload against its schema.

    Returns:
        (is_valid, error_message, validated_payload)
        - is_valid: True if validation passed
        - error_message: None if valid, error description if invalid
        - validated_payload: Validated payload dict if valid, None if invalid
    """
    # Check if event type is valid first
    if not is_valid_event_type(event_type):
        return False, f"Unknown event type: {event_type}", None

    # Check if we have a payload model for this event
    payload_model = EVENT_PAYLOAD_MODELS.get(event_type)

    if payload_model is None:
        # No validation model defined, accept any payload
        return True, None, payload

    # Validate payload with Pydantic model
    try:
        validated = payload_model.model_validate(payload)
        return True, None, validated.model_dump()
    except ValidationError as e:
        error_details = "; ".join([f"{err['loc'][0]}: {err['msg']}" for err in e.errors()])
        return False, f"Payload validation failed: {error_details}", None


def get_all_event_types() -> dict[str, list[str]]:
    """Get all registered event types grouped by game."""
    return {
        "carrom": list(carrom_events.ALL_EVENT_TYPES),
        "racing": list(racing_events.ALL_EVENT_TYPES),
        "mining": list(mining_events.ALL_EVENT_TYPES),
        "core": ["play/begin", "play/score", "play/finish", "quit", "user/login", "user/logout", "credit/spend", "credit/earn"],
    }
