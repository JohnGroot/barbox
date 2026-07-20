"""Common utilities and patterns shared across game modules."""

import json
from typing import Any, Final
from uuid import UUID

from structlog import get_logger

logger = get_logger(__name__)

# Sentinel for unparseable/missing UUIDs from raw SQL results
ZERO_UUID: Final = UUID(int=0)

# Log-field truncation lengths for raw SQL values/rows (keep log lines bounded)
LOG_VALUE_PREVIEW_CHARS: Final = 100
LOG_ROW_PREVIEW_CHARS: Final = 200

# Default number of entries returned by game leaderboard endpoints
DEFAULT_LEADERBOARD_LIMIT: Final = 10


def parse_json_field(value: Any) -> list | dict | None:  # noqa: ANN401  # raw SQL value
    """
    Parse JSON field from SQL result, handling SQLite's json_extract() behavior.

    SQLite's json_extract() returns:
    - Parsed Python list/dict for JSON objects/arrays
    - String for JSON strings (rare edge case)
    - None for SQL NULL

    This function robustly handles all cases to prevent parsing errors.

    Args:
        value: Value from SQL query result

    Returns:
        Parsed JSON structure (list/dict) or None if invalid/null

    Example:
        >>> parse_json_field([1.5, 2.3, 3.1])  # Already parsed by SQLite
        [1.5, 2.3, 3.1]
        >>> parse_json_field('{"key": "value"}')  # JSON string
        {'key': 'value'}
        >>> parse_json_field(None)
        None
    """
    if value is None:
        return None

    # Already parsed by SQLite - return as-is
    if isinstance(value, (list, dict)):
        return value

    # JSON string - parse it (legacy data or edge case)
    if isinstance(value, str):
        if not value or value == "null":
            return None
        try:
            return json.loads(value)
        except (json.JSONDecodeError, ValueError, TypeError) as e:
            logger.exception(
                "json_parse_failed",
                value=str(value)[:LOG_VALUE_PREVIEW_CHARS],
                error=str(e),
            )
            return None

    # Unexpected type - log warning
    logger.warning(
        "unexpected_json_type",
        value_type=type(value).__name__,
        value_sample=str(value)[:LOG_VALUE_PREVIEW_CHARS],
    )
    return None


def parse_float_list(value: Any) -> list[float] | None:  # noqa: ANN401  # raw SQL value
    """
    Parse a list of floats from SQL result.

    Similar to parse_json_field but ensures all elements are converted to float.
    Useful for lap times, timestamps, scores, etc.

    Args:
        value: Value from SQL query result

    Returns:
        List of floats or None if invalid/null

    Example:
        >>> parse_float_list([1, 2, 3])
        [1.0, 2.0, 3.0]
        >>> parse_float_list("[1.5, 2.3, 3.1]")
        [1.5, 2.3, 3.1]
    """
    parsed = parse_json_field(value)

    if parsed is None:
        return None

    if not isinstance(parsed, list):
        logger.warning(
            "expected_list_got_other",
            value_type=type(parsed).__name__,
            value_sample=str(parsed)[:LOG_VALUE_PREVIEW_CHARS],
        )
        return None

    try:
        return [float(x) for x in parsed]
    except (ValueError, TypeError) as e:
        logger.exception(
            "float_conversion_failed",
            value=str(parsed)[:LOG_VALUE_PREVIEW_CHARS],
            error=str(e),
        )
        return None


def parse_uuid_safe(value: Any) -> UUID:  # noqa: ANN401  # raw SQL value
    """
    Parse UUID from SQL result with fallback to zero UUID.

    Args:
        value: UUID value from SQL query (string or None)

    Returns:
        UUID object, or zero UUID if invalid/null

    Example:
        >>> parse_uuid_safe("550e8400-e29b-41d4-a716-446655440000")
        UUID('550e8400-e29b-41d4-a716-446655440000')
        >>> parse_uuid_safe(None)
        UUID('00000000-0000-0000-0000-000000000000')
        >>> parse_uuid_safe("invalid")
        UUID('00000000-0000-0000-0000-000000000000')
    """
    if value is None:
        return ZERO_UUID

    try:
        return UUID(str(value))
    except (ValueError, TypeError) as e:
        logger.warning(
            "invalid_uuid_using_zero",
            value=str(value)[:LOG_VALUE_PREVIEW_CHARS],
            error=str(e),
        )
        return ZERO_UUID


def parse_username_safe(value: Any) -> str:  # noqa: ANN401  # raw SQL value
    """
    Parse username from SQL result with fallback to "Unknown".

    Args:
        value: Username value from SQL query (string or None)

    Returns:
        Username string, or "Unknown" if invalid/null/empty

    Example:
        >>> parse_username_safe("player123")
        'player123'
        >>> parse_username_safe(None)
        'Unknown'
        >>> parse_username_safe("")
        'Unknown'
    """
    if value is None or str(value).strip() == "":
        return "Unknown"
    return str(value)


def signed_sum_sql(column: str, earn_type: str, spend_type: str) -> str:
    """
    Build the `SUM(CASE WHEN earn THEN + ... WHEN spend THEN - ... END)` SQL
    fragment shared by every credit-balance query (player credits, machine
    credit pots): balance is derived by summing signed amounts from an event
    log rather than stored directly.

    Args:
        column: JSON path to the amount field, e.g. "bse.payload, '$.amount'"
        earn_type: event `type` value that adds to the balance
        spend_type: event `type` value that subtracts from the balance

    Returns:
        A `COALESCE(SUM(CASE ...), 0)` SQL expression - embed it directly in
        a SELECT; the caller still owns the FROM/JOIN/WHERE clause, which
        differs per balance kind (player-scoped vs box+game-scoped).

    Example:
        >>> signed_sum_sql("bse.payload, '$.amount'", "credit/earn", "credit/spend")
        "COALESCE(SUM(CASE WHEN bse.type = 'credit/earn' THEN ... END), 0)"
    """
    return f"""COALESCE(
        SUM(CASE
            WHEN bse.type = '{earn_type}' THEN
                CAST(json_extract({column}) AS INTEGER)
            WHEN bse.type = '{spend_type}' THEN
                -CAST(json_extract({column}) AS INTEGER)
            ELSE 0
        END),
        0
    )"""


_ORDER_DIRECTIONS = frozenset({"ASC", "DESC"})


def uuid_join_leaderboard_sql(
    *,
    extra_select: str = "",
    extra_where: str = "",
    direction: str = "ASC",
) -> str:
    """
    Build a "best single numeric metric per player" leaderboard query for the
    box_session_event -> box_session (host_player_id) -> player join-key
    strategy (raw UUID join, `bs.host_player_id = p.id`). This is Racing's
    shape specifically - Carrom/Nines/Mining use different join-key
    strategies (de-hyphenated UUID, phone_number, no player join at all) and
    aren't forced into this shape; see games/CLAUDE.md for why.

    `event_type` (box_session_event.type to filter on) and `value_json_path`
    (JSON path for the metric value, e.g. "$.lap_time") are both genuine
    VALUES here - one is compared in a WHERE clause, the other is a function
    argument to json_extract - neither is a SQL identifier, so both are
    bind-paramable. Like `track_id` and `limit`, they are therefore not
    accepted as function arguments: the returned SQL references them as
    :event_type / :value_json_path, and the caller supplies the actual values
    in the params dict passed to db.get_many_raw, same as every other bind
    param this builder references.

    Args:
        extra_select: additional ", column" SELECT fragment (e.g. lap_times)
        extra_where: additional "AND ..." WHERE fragment for extra filters
                     (the caller supplies matching bind params)
        direction: ORDER BY direction for metric_value, "ASC" or "DESC"
                    (default "ASC", preserving prior hardcoded behavior).
                    ORDER BY direction is a SQL keyword, not a value, so it
                    can't be a bind param - it's validated against an
                    allowlist and interpolated directly instead.

    Returns:
        SQL string binding :track_id, :limit, :event_type, and
        :value_json_path; callers add any extra bind params referenced by
        extra_where.
    """
    if direction not in _ORDER_DIRECTIONS:
        msg = f"Invalid ORDER BY direction: {direction!r}"
        raise ValueError(msg)

    username_expr = (
        "COALESCE(p.tag, json_extract(bse.payload, '$.username'), "
        "'Player ' || SUBSTR(bs.host_player_id, 1, 8))"
    )
    return f"""
    SELECT
        bs.host_player_id,
        {username_expr} as username,
        MIN(CAST(json_extract(bse.payload, :value_json_path) AS REAL)) as metric_value,
        MAX(bse.timestamp) as entry_date
        {extra_select}
    FROM box_session_event bse
    JOIN box_session bs ON bse.session_id = bs.id
    LEFT JOIN player p ON bs.host_player_id = p.id
    WHERE bse.type = :event_type
    AND json_extract(bse.payload, '$.track_id') = :track_id
    {extra_where}
    GROUP BY bs.host_player_id, username
    ORDER BY metric_value {direction}
    LIMIT :limit
    """  # noqa: S608  # direction is allowlist-checked above; rest are bind params


def safe_parse_leaderboard[T](
    rows: Any,  # noqa: ANN401  # iterable of raw SQL result rows, shape varies per caller
    parser_fn: Any,  # noqa: ANN401  # callable signature varies per caller's row shape
) -> list[T]:
    """
    Parse leaderboard entries with per-entry error handling.

    This function prevents a single corrupt row from crashing the entire
    leaderboard query. Instead, it logs errors and skips problematic entries.

    Args:
        rows: Iterable of SQL result rows (typically result.tuples())
        parser_fn: Function that takes a row tuple and returns a leaderboard entry.
                  Should raise exceptions for invalid data.

    Returns:
        List of successfully parsed leaderboard entries (may be partial)

    Example:
        >>> def parse_row(row):
        ...     return LeaderboardEntry(
        ...         player_id=parse_uuid_safe(row[0]),
        ...         username=parse_username_safe(row[1]),
        ...         score=row[2],
        ...     )
        >>> leaderboard = safe_parse_leaderboard(result.tuples(), parse_row)
    """
    entries = []
    for row in rows:
        try:
            entry = parser_fn(row)
            entries.append(entry)
        except Exception as e:
            logger.exception(
                "leaderboard_entry_parse_failed",
                row=str(row)[:LOG_ROW_PREVIEW_CHARS],
                error=str(e),
            )
            # Continue processing remaining rows instead of crashing
            continue
    return entries
