"""Common utilities and patterns shared across game modules."""

import json
from typing import Any


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
            print(f"[ERROR] Failed to parse JSON: {value} - {e}")
            return None

    # Unexpected type - log warning
    print(f"[WARNING] Unexpected JSON field type: {type(value)} - {value}")
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
        print(f"[WARNING] Expected list but got {type(parsed)}: {parsed}")
        return None

    try:
        return [float(x) for x in parsed]
    except (ValueError, TypeError) as e:
        print(f"[ERROR] Failed to convert list elements to float: {parsed} - {e}")
        return None


def build_player_leaderboard_query(
    event_type: str,
    metric_field: str,
    aggregate_func: str = "MAX",
    order: str = "DESC",
    additional_where: str = "",
) -> str:
    """
    Build a standard leaderboard query for player-based metrics.

    This extracts the common pattern used across all game leaderboards:
    - Join sessions with events
    - Extract metric from JSON payload
    - Group by player
    - Order by metric
    - Include player username

    Args:
        event_type: Event type to query (e.g., "carrom/round_finish")
        metric_field: JSON path to metric in payload (e.g., "$.points")
        aggregate_func: SQL aggregate function (MAX, MIN, SUM, AVG, COUNT)
        order: Sort order (ASC for times, DESC for scores)
        additional_where: Extra WHERE conditions (e.g., "AND json_extract(...) = :param")

    Returns:
        SQL query string with placeholders for :limit and additional params

    Example:
        >>> build_player_leaderboard_query(
        ...     event_type="foo/score",
        ...     metric_field="$.points",
        ...     aggregate_func="MAX",
        ...     order="DESC"
        ... )
        "SELECT bs.host_player_id, ... WHERE bse.type = 'foo/score' ..."
    """
    return f"""
    SELECT
        bs.host_player_id,
        COALESCE(p.tag, json_extract(bse.payload, '$.username'), 'Player ' || SUBSTR(bs.host_player_id, 1, 8)) as username,
        {aggregate_func}(CAST(json_extract(bse.payload, '{metric_field}') AS REAL)) as metric_value,
        MAX(bse.timestamp) as entry_date
    FROM box_session_event bse
    JOIN box_session bs ON bse.session_id = bs.id
    LEFT JOIN player p ON bs.host_player_id = p.id
    WHERE bse.type = '{event_type}'
    {additional_where}
    GROUP BY bs.host_player_id, COALESCE(p.tag, json_extract(bse.payload, '$.username'), 'Player ' || SUBSTR(bs.host_player_id, 1, 8))
    ORDER BY metric_value {order}
    LIMIT :limit
    """


def build_multiplayer_leaderboard_query(
    event_type: str,
    metric_field_template: str,
    aggregate_func: str = "SUM",
    order: str = "DESC",
    additional_where: str = "",
) -> str:
    """
    Build a leaderboard query for multiplayer games with player_ids array.

    This handles the more complex case where multiple players participate
    in a single session and each has their own score/metric.

    Args:
        event_type: Event type to query (e.g., "carrom/round_finish")
        metric_field_template: JSON path template with {player_id} placeholder
                              (e.g., "$.scores.{player_id}")
        aggregate_func: SQL aggregate function (SUM, MAX, MIN, AVG, COUNT)
        order: Sort order (ASC for times, DESC for scores)
        additional_where: Extra WHERE conditions

    Returns:
        SQL query string with placeholders for :limit

    Example:
        >>> build_multiplayer_leaderboard_query(
        ...     event_type="carrom/round_finish",
        ...     metric_field_template="$.scores.{player_id}",
        ...     aggregate_func="SUM"
        ... )
        "WITH player_sessions AS ... WHERE bse.type = 'carrom/round_finish' ..."
    """
    # Replace {player_id} placeholder with SQL concatenation
    metric_extraction = metric_field_template.replace(
        "{player_id}", "' || ps.player_id"
    ).replace("$.", "'$.")

    return f"""
    WITH player_sessions AS (
        SELECT
            bs.id as session_id,
            json_extract(player_data.value, '$') as player_id
        FROM box_session bs,
             json_each(bs.player_ids) as player_data
    )
    SELECT
        ps.player_id,
        p.tag as username,
        {aggregate_func}(
            CAST(
                json_extract(bse.payload, {metric_extraction})
                AS REAL
            )
        ) as metric_value,
        MAX(bse.timestamp) as entry_date
    FROM player_sessions ps
    JOIN box_session_event bse ON bse.session_id = ps.session_id
    JOIN player p ON p.id = ps.player_id
    WHERE bse.type = '{event_type}'
    {additional_where}
    GROUP BY ps.player_id, p.tag
    ORDER BY metric_value {order}
    LIMIT :limit
    """


# Common SQL patterns as constants

# Unnest player_ids JSON array
UNNEST_PLAYER_IDS_CTE = """
WITH player_sessions AS (
    SELECT
        bs.id as session_id,
        json_extract(player_data.value, '$') as player_id
    FROM box_session bs,
         json_each(bs.player_ids) as player_data
)
"""

# Get username with fallback
USERNAME_COALESCE = "COALESCE(p.tag, json_extract(bse.payload, '$.username'), 'Player ' || SUBSTR(bs.host_player_id, 1, 8))"

# Common WHERE clause for game-specific queries
EVENT_TYPE_WHERE = "WHERE bse.type = :event_type"
