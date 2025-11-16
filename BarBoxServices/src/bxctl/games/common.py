"""Common utilities and patterns shared across game modules."""

import json
from typing import Any
from structlog import get_logger

logger = get_logger(__name__)


def parse_json_field(value: Any) -> list | dict | None:
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
            logger.error("json_parse_failed", value=str(value)[:100], error=str(e))
            return None

    # Unexpected type - log warning
    logger.warning("unexpected_json_type", value_type=type(value).__name__, value_sample=str(value)[:100])
    return None


def parse_float_list(value: Any) -> list[float] | None:
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
        logger.warning("expected_list_got_other", value_type=type(parsed).__name__, value_sample=str(parsed)[:100])
        return None

    try:
        return [float(x) for x in parsed]
    except (ValueError, TypeError) as e:
        logger.error("float_conversion_failed", value=str(parsed)[:100], error=str(e))
        return None


def parse_uuid_safe(value: Any) -> Any:
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
    from uuid import UUID

    if value is None:
        return UUID("00000000-0000-0000-0000-000000000000")

    try:
        return UUID(str(value))
    except (ValueError, TypeError) as e:
        logger.warning("invalid_uuid_using_zero", value=str(value)[:100], error=str(e))
        return UUID("00000000-0000-0000-0000-000000000000")


def parse_username_safe(value: Any) -> str:
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


def safe_parse_leaderboard[T](
    rows: Any,
    parser_fn: Any,
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
        ...         score=row[2]
        ...     )
        >>> leaderboard = safe_parse_leaderboard(result.tuples(), parse_row)
    """
    entries = []
    for row in rows:
        try:
            entry = parser_fn(row)
            entries.append(entry)
        except Exception as e:
            logger.error("leaderboard_entry_parse_failed", row=str(row)[:200], error=str(e))
            # Continue processing remaining rows instead of crashing
            continue
    return entries
