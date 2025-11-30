"""Central event validation for all games."""

from typing import Any, get_args

from pydantic import ValidationError

from bxctl.structures import CoreEventType, GAMES

# Build event type registry dynamically from GAMES registry
# Uses canonical EventType alias from each game's schemas module
_EVENT_REGISTRY: dict[str, set[str]] = {
    game_name: set(get_args(game_data["schemas"].EventType))
    for game_name, game_data in GAMES.items()
}

# Core event types (non-game-specific)
_CORE_EVENTS = set(get_args(CoreEventType))


# Mapping of event types to their Pydantic payload models
# Import schemas from GAMES registry for consistency
EVENT_PAYLOAD_MODELS: dict[str, type] = {
    # Carrom events
    "carrom/round_finish": GAMES["carrom"]["schemas"].CarromRoundFinishPayload,
    # Racing events
    "racing/lap_complete": GAMES["racing"]["schemas"].RacingLapCompletePayload,
    "racing/race_finish": GAMES["racing"]["schemas"].RacingRaceFinishPayload,
    # Mining events
    "mining/extract_complete": GAMES["mining"]["schemas"].MiningExtractCompletePayload,
    "mining/upgrade_purchase": GAMES["mining"]["schemas"].MiningUpgradePurchasePayload,
    # Nines events
    "nines/jackpot_won": GAMES["nines"]["schemas"].NinesJackpotWonPayload,
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
    - Event type is a core event (play/*, user/*, credit/*)
    - Event type is registered in a game module

    Uses dynamic registry built from game Literal types.
    """
    # Check core events first
    if event_type in _CORE_EVENTS:
        return True

    # Check game-specific events
    game = get_game_from_event_type(event_type)
    if game and game in _EVENT_REGISTRY:
        return event_type in _EVENT_REGISTRY[game]

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
        **{game: list(events) for game, events in _EVENT_REGISTRY.items()},
        "core": list(_CORE_EVENTS),
    }
