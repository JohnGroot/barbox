from uuid import UUID

from fastapi import APIRouter

from bxctl import structures

from . import dependencies

router = APIRouter(prefix="/game/racing")


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

    if metric == "best_lap":
        # Aggregate best lap times from racing/lap_complete events
        sql = """
        SELECT
            bs.player_id,
            p.tag as username,
            MIN(CAST(json_extract(bse.payload, '$.lap_time') AS REAL)) as metric_value,
            MAX(bse.timestamp) as entry_date
        FROM box_session_event bse
        JOIN box_session bs ON bse.session_id = bs.id
        JOIN player p ON bs.player_id = p.id
        WHERE bse.type = 'racing/lap_complete'
        AND json_extract(bse.payload, '$.track_id') = :track_id
        GROUP BY bs.player_id, p.tag
        ORDER BY metric_value ASC
        LIMIT :limit
        """
        result = await db_service.get_many_raw(sql, {"track_id": track_id, "limit": limit})

        leaderboard = [
            structures.RacingLeaderboardEntry(
                player_id=UUID(str(row[0])),
                username=row[1],
                metric_value=row[2],
                entry_date=row[3],
            )
            for row in result.tuples()
        ]

    elif metric == "best_race":
        # Aggregate best race times from racing/race_finish events
        if laps is None:
            # If laps not specified, get best overall time regardless of lap count
            sql = """
            SELECT
                bs.player_id,
                p.tag as username,
                MIN(CAST(json_extract(bse.payload, '$.total_time') AS REAL)) as metric_value,
                MAX(bse.timestamp) as entry_date,
                json_extract(bse.payload, '$.lap_times') as lap_times
            FROM box_session_event bse
            JOIN box_session bs ON bse.session_id = bs.id
            JOIN player p ON bs.player_id = p.id
            WHERE bse.type = 'racing/race_finish'
            AND json_extract(bse.payload, '$.track_id') = :track_id
            GROUP BY bs.player_id, p.tag
            ORDER BY metric_value ASC
            LIMIT :limit
            """
            result = await db_service.get_many_raw(sql, {"track_id": track_id, "limit": limit})
        else:
            # Filter by specific lap count
            sql = """
            SELECT
                bs.player_id,
                p.tag as username,
                MIN(CAST(json_extract(bse.payload, '$.total_time') AS REAL)) as metric_value,
                MAX(bse.timestamp) as entry_date,
                json_extract(bse.payload, '$.lap_times') as lap_times
            FROM box_session_event bse
            JOIN box_session bs ON bse.session_id = bs.id
            JOIN player p ON bs.player_id = p.id
            WHERE bse.type = 'racing/race_finish'
            AND json_extract(bse.payload, '$.track_id') = :track_id
            AND CAST(json_extract(bse.payload, '$.total_laps') AS INTEGER) = :laps
            GROUP BY bs.player_id, p.tag
            ORDER BY metric_value ASC
            LIMIT :limit
            """
            result = await db_service.get_many_raw(
                sql, {"track_id": track_id, "laps": laps, "limit": limit}
            )

        leaderboard = [
            structures.RacingLeaderboardEntry(
                player_id=UUID(str(row[0])),
                username=row[1],
                metric_value=row[2],
                entry_date=row[3],
                lap_times=row[4] if len(row) > 4 else None,
            )
            for row in result.tuples()
        ]
    else:
        # Invalid metric
        leaderboard = []

    return structures.RacingLeaderboardResponse(
        track_id=track_id,
        metric=metric,
        leaderboard=leaderboard,
    )
