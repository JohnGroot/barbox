import json
from uuid import UUID

from fastapi import APIRouter, HTTPException

from bxctl import structures

from . import dependencies

router = APIRouter(prefix="/game/racing")


def _parse_lap_times(value) -> list[float] | None:
	"""
	Parse lap_times from SQL result, handling both JSON strings and already-parsed lists.

	SQLite's json_extract() returns:
	- Parsed Python list/dict for JSON objects/arrays
	- String for JSON strings
	- None for SQL NULL
	"""
	if value is None:
		return None

	# Already parsed by SQLite - return as-is
	if isinstance(value, list):
		try:
			return [float(x) for x in value]
		except (ValueError, TypeError) as e:
			print(f"[ERROR] Failed to convert lap_times list elements to float: {value} - {e}")
			return None

	# JSON string - parse it
	if isinstance(value, str):
		if not value or value == "null":
			return None
		try:
			parsed = json.loads(value)
			if isinstance(parsed, list):
				return [float(x) for x in parsed]
			else:
				print(f"[WARNING] Parsed lap_times is not a list: {type(parsed)} - {parsed}")
				return None
		except (json.JSONDecodeError, ValueError, TypeError) as e:
			print(f"[ERROR] Failed to parse lap_times JSON: {value} - {e}")
			return None

	# Unexpected type
	print(f"[WARNING] Unexpected lap_times type: {type(value)} - {value}")
	return None


@router.get("/leaderboard")
async def get_racing_leaderboard(
    track_id: str,
    db_service: dependencies.Database,
    metric: str = "best_race",
    laps: int | None = None,
    limit: int = 10,
) -> structures.RacingLeaderboardResponse:
    """
    Get racing leaderboard aggregated from racing/race_finish events.

    Args:
        track_id: Track identifier (e.g. "gocart_track")
        metric: "best_lap" or "best_race"
        laps: Required for best_race metric (number of laps for the race)
        limit: Maximum number of entries to return

    Returns:
        Racing leaderboard with player rankings
    """

    try:
        if metric == "best_lap":
            # Aggregate best lap times from racing/lap_complete events
            # LEFT JOIN player to handle cases where player hasn't been registered yet
            # Use host_player_id for single-player games
            sql = """
            SELECT
                bs.host_player_id,
                COALESCE(p.tag, json_extract(bse.payload, '$.username'), 'Player ' || SUBSTR(bs.host_player_id, 1, 8)) as username,
                MIN(CAST(json_extract(bse.payload, '$.lap_time') AS REAL)) as metric_value,
                MAX(bse.timestamp) as entry_date
            FROM box_session_event bse
            JOIN box_session bs ON bse.session_id = bs.id
            LEFT JOIN player p ON bs.host_player_id = p.id
            WHERE bse.type = 'racing/lap_complete'
            AND json_extract(bse.payload, '$.track_id') = :track_id
            GROUP BY bs.host_player_id, COALESCE(p.tag, json_extract(bse.payload, '$.username'), 'Player ' || SUBSTR(bs.host_player_id, 1, 8))
            ORDER BY metric_value ASC
            LIMIT :limit
            """
            result = await db_service.get_many_raw(sql, {"track_id": track_id, "limit": limit})

            leaderboard = [
                structures.RacingLeaderboardEntry(
                    player_id=UUID(str(row[0])) if row[0] else UUID('00000000-0000-0000-0000-000000000000'),
                    username=row[1] if row[1] else "Unknown",
                    metric_value=row[2],
                    entry_date=row[3],
                )
                for row in result.tuples()
            ]

        elif metric == "best_race":
            # Aggregate best race times from racing/race_finish events
            # LEFT JOIN player to handle cases where player hasn't been registered yet
            # Use host_player_id for single-player games
            if laps is None:
                # If laps not specified, get best overall time regardless of lap count
                sql = """
                SELECT
                    bs.host_player_id,
                    COALESCE(p.tag, json_extract(bse.payload, '$.username'), 'Player ' || SUBSTR(bs.host_player_id, 1, 8)) as username,
                    MIN(CAST(json_extract(bse.payload, '$.total_time') AS REAL)) as metric_value,
                    MAX(bse.timestamp) as entry_date,
                    json_extract(bse.payload, '$.lap_times') as lap_times
                FROM box_session_event bse
                JOIN box_session bs ON bse.session_id = bs.id
                LEFT JOIN player p ON bs.host_player_id = p.id
                WHERE bse.type = 'racing/race_finish'
                AND json_extract(bse.payload, '$.track_id') = :track_id
                GROUP BY bs.host_player_id, COALESCE(p.tag, json_extract(bse.payload, '$.username'), 'Player ' || SUBSTR(bs.host_player_id, 1, 8))
                ORDER BY metric_value ASC
                LIMIT :limit
                """
                result = await db_service.get_many_raw(sql, {"track_id": track_id, "limit": limit})
            else:
                # Filter by specific lap count
                # Use host_player_id for single-player games
                sql = """
                SELECT
                    bs.host_player_id,
                    COALESCE(p.tag, json_extract(bse.payload, '$.username'), 'Player ' || SUBSTR(bs.host_player_id, 1, 8)) as username,
                    MIN(CAST(json_extract(bse.payload, '$.total_time') AS REAL)) as metric_value,
                    MAX(bse.timestamp) as entry_date,
                    json_extract(bse.payload, '$.lap_times') as lap_times
                FROM box_session_event bse
                JOIN box_session bs ON bse.session_id = bs.id
                LEFT JOIN player p ON bs.host_player_id = p.id
                WHERE bse.type = 'racing/race_finish'
                AND json_extract(bse.payload, '$.track_id') = :track_id
                AND CAST(json_extract(bse.payload, '$.total_laps') AS INTEGER) = :laps
                GROUP BY bs.host_player_id, COALESCE(p.tag, json_extract(bse.payload, '$.username'), 'Player ' || SUBSTR(bs.host_player_id, 1, 8))
                ORDER BY metric_value ASC
                LIMIT :limit
                """
                result = await db_service.get_many_raw(
                    sql, {"track_id": track_id, "laps": laps, "limit": limit}
                )

            # Build leaderboard with per-entry error handling
            leaderboard = []
            for row in result.tuples():
                try:
                    entry = structures.RacingLeaderboardEntry(
                        player_id=UUID(str(row[0])) if row[0] else UUID('00000000-0000-0000-0000-000000000000'),
                        username=row[1] if row[1] else "Unknown",
                        metric_value=row[2],
                        entry_date=row[3],
                        lap_times=_parse_lap_times(row[4]) if len(row) > 4 else None,
                    )
                    leaderboard.append(entry)
                except Exception as e:
                    # Log but don't crash - skip this problematic entry
                    print(f"[ERROR] Failed to parse leaderboard entry: row={row}, error={e}")
                    import traceback
                    traceback.print_exc()
                    continue
        else:
            # Invalid metric
            leaderboard = []

        return structures.RacingLeaderboardResponse(
            track_id=track_id,
            metric=metric,
            leaderboard=leaderboard,
        )

    except ValueError as e:
        # Handle UUID conversion errors
        import traceback
        error_trace = traceback.format_exc()
        print(f"[ERROR] Invalid player ID format: {str(e)}")
        print(f"[ERROR] Traceback: {error_trace}")
        raise HTTPException(
            status_code=400,
            detail=f"Invalid player ID format in database: {str(e)}"
        )
    except Exception as e:
        # Handle SQL and other errors
        import traceback
        error_trace = traceback.format_exc()
        print(f"[ERROR] Leaderboard query failed: {str(e)}")
        print(f"[ERROR] Traceback: {error_trace}")
        print(f"[ERROR] Query parameters: track_id={track_id}, metric={metric}, laps={laps}, limit={limit}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to query leaderboard: {str(e)}"
        )
