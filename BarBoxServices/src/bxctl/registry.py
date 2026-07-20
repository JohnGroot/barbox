from enum import StrEnum
from typing import Any, Literal, get_args

from bxctl.games import carrom, mining, nines, racing

# Single source of truth: All games registered here
GAMES = {
    "carrom": {"schemas": carrom.schemas, "router": carrom.router},
    "racing": {"schemas": racing.schemas, "router": racing.router},
    "mining": {"schemas": mining.schemas, "router": mining.router},
    "nines": {"schemas": nines.schemas, "router": nines.router},
}


def game_module(entry: dict[str, Any], key: str) -> Any:  # noqa: ANN401  # see below
    """Return a game's "schemas" or "router" submodule from a GAMES entry.

    GAMES stores real submodules (each game's own schemas.py/router.py) as
    plain dict values, so indexing them loses their specific attributes
    (EventType, payload classes, `.router`, ...) to static analysis.
    Declared Any because callers already know which submodule and
    attribute they're asking for.
    """
    return entry[key]


# Core event types (non-game-specific)
CoreEventType = Literal[
    # Generic session events
    "play/begin",
    "play/score",
    "play/finish",
    "quit",
    # User events
    "user/login",
    "user/logout",
    # Credit events
    "credit/spend",
    "credit/earn",
    # Machine credit events (per box+game credit pools)
    "machine_credit/deposit",
    "machine_credit/consume",
]


class CoreEvent(StrEnum):
    """Named accessors for CoreEventType values.

    Validation stays Literal-based (CoreEventType feeds SessionEventType and
    the get_args() machinery); this enum exists so code producing or querying
    events references named members instead of raw strings. An import-time
    guard below keeps it in lockstep with CoreEventType.
    """

    PLAY_BEGIN = "play/begin"
    PLAY_SCORE = "play/score"
    PLAY_FINISH = "play/finish"
    QUIT = "quit"
    USER_LOGIN = "user/login"
    USER_LOGOUT = "user/logout"
    CREDIT_SPEND = "credit/spend"
    CREDIT_EARN = "credit/earn"
    MACHINE_CREDIT_DEPOSIT = "machine_credit/deposit"
    MACHINE_CREDIT_CONSUME = "machine_credit/consume"


# Compose SessionEventType from core and game-specific types
# Single source of truth: game event types defined in games/{game}/schemas.py
type SessionEventType = (
    CoreEventType
    | carrom.schemas.CarromEventType
    | racing.schemas.RacingEventType
    | mining.schemas.MiningEventType
    | nines.schemas.NinesEventType
)


def _check_session_event_type_coverage() -> None:
    """Fail fast if a registered game's EventType isn't in SessionEventType.

    Adding a game to GAMES without also adding its schemas.EventType to the
    SessionEventType union above would otherwise only surface as a runtime
    422 on that game's first event.
    """
    covered = {
        literal
        for member in get_args(SessionEventType.__value__)
        for literal in get_args(member)
    }
    for game_name, game_data in GAMES.items():
        game_events = set(get_args(game_module(game_data, "schemas").EventType))
        missing = game_events - covered
        if missing:
            msg = (
                f"Game '{game_name}' event type(s) {sorted(missing)} are not "
                "reachable through SessionEventType in registry.py. Add the "
                "game's EventType to the SessionEventType union."
            )
            raise RuntimeError(msg)

    core_mismatch = {member.value for member in CoreEvent} ^ set(
        get_args(CoreEventType)
    )
    if core_mismatch:
        msg = (
            f"CoreEvent and CoreEventType disagree on {sorted(core_mismatch)} "
            "in registry.py. Keep the enum and the Literal in lockstep."
        )
        raise RuntimeError(msg)


_check_session_event_type_coverage()
