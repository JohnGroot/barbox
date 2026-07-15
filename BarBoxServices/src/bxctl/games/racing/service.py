"""Business logic and database queries for Racing game."""

from fastapi import HTTPException
from structlog import get_logger

from bxctl.db import service as db_service
from bxctl.games import common

from . import schemas

logger = get_logger(__name__)


async def get_racing_leaderboard(
    db: db_service.CRUD,
    track_id: str,
    metric: str = "best_race",
    laps: int | None = None,
    limit: int = 10,
) -> schemas.RacingLeaderboardResponse:
    """
    Get racing leaderboard aggregated from racing/race_finish events.

    Args:
        db: Database CRUD service
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
            event_type = "racing/lap_complete"
            value_json_path = "$.lap_time"
            sql = common.uuid_join_leaderboard_sql()
            result = await db.get_many_raw(
                sql,
                {
                    "track_id": track_id,
                    "limit": limit,
                    "event_type": event_type,
                    "value_json_path": value_json_path,
                },
            )

            # Parse leaderboard with per-entry error handling
            def parse_row(row):
                return schemas.RacingLeaderboardEntry(
                    player_id=common.parse_uuid_safe(row[0]),
                    username=common.parse_username_safe(row[1]),
                    metric_value=row[2],
                    entry_date=row[3],
                )

            leaderboard = common.safe_parse_leaderboard(result.tuples(), parse_row)

        elif metric == "best_race":
            # Aggregate best race times from racing/race_finish events
            # LEFT JOIN player to handle cases where player hasn't been registered yet
            # Use host_player_id for single-player games
            extra_select = ", json_extract(bse.payload, '$.lap_times') as lap_times"
            event_type = "racing/race_finish"
            value_json_path = "$.total_time"

            if laps is None:
                # If laps not specified, get best overall time regardless of lap count
                sql = common.uuid_join_leaderboard_sql(extra_select=extra_select)
                result = await db.get_many_raw(
                    sql,
                    {
                        "track_id": track_id,
                        "limit": limit,
                        "event_type": event_type,
                        "value_json_path": value_json_path,
                    },
                )
            else:
                # Filter by specific lap count
                sql = common.uuid_join_leaderboard_sql(
                    extra_select=extra_select,
                    extra_where=(
                        "AND CAST(json_extract(bse.payload, '$.total_laps') "
                        "AS INTEGER) = :laps"
                    ),
                )
                result = await db.get_many_raw(
                    sql,
                    {
                        "track_id": track_id,
                        "laps": laps,
                        "limit": limit,
                        "event_type": event_type,
                        "value_json_path": value_json_path,
                    },
                )

            # Parse leaderboard with per-entry error handling
            def parse_row(row):
                return schemas.RacingLeaderboardEntry(
                    player_id=common.parse_uuid_safe(row[0]),
                    username=common.parse_username_safe(row[1]),
                    metric_value=row[2],
                    entry_date=row[3],
                    lap_times=common.parse_float_list(row[4]) if len(row) > 4 else None,
                )

            leaderboard = common.safe_parse_leaderboard(result.tuples(), parse_row)
        else:
            # Invalid metric
            leaderboard = []

        return schemas.RacingLeaderboardResponse(
            track_id=track_id,
            metric=metric,
            leaderboard=leaderboard,
        )

    except ValueError as e:
        # Handle UUID conversion errors
        logger.error("invalid_player_id_format", error=str(e), exc_info=True)
        raise HTTPException(
            status_code=400, detail=f"Invalid player ID format in database: {str(e)}"
        )
    except Exception as e:
        # Handle SQL and other errors
        logger.error(
            "leaderboard_query_failed",
            error=str(e),
            track_id=track_id,
            metric=metric,
            laps=laps,
            limit=limit,
            exc_info=True,
        )
        raise HTTPException(
            status_code=500, detail=f"Failed to query leaderboard: {str(e)}"
        )
