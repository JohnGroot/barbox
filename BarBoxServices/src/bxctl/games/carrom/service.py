"""Business logic and database queries for Carrom game."""

from bxctl.db import service as db_service
from bxctl.games import common

from . import schemas


async def get_carrom_leaderboard(
    db: db_service.CRUD,
    metric: str = "total_score",
    limit: int = 10,
) -> schemas.CarromLeaderboardResponse:
    """
    Get carrom leaderboard aggregated from carrom/round_finish events.

    Args:
        db: Database CRUD service
        metric: "total_score" or "total_wins"
        limit: Maximum number of entries to return

    Returns:
        Carrom leaderboard with player rankings
    """

    if metric == "total_score":
        # Aggregate total scores from carrom/round_finish events
        # Use json_each() to unnest player_ids array and extract each player's score
        sql = """
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
            SUM(
                CAST(
                    json_extract(
                        bse.payload,
                        '$.scores.' || ps.player_id
                    ) AS INTEGER
                )
            ) as total_score,
            MAX(bse.timestamp) as entry_date
        FROM player_sessions ps
        JOIN box_session_event bse ON bse.session_id = ps.session_id
        JOIN player p ON p.id = ps.player_id
        WHERE bse.type = 'carrom/round_finish'
        GROUP BY ps.player_id, p.tag
        ORDER BY total_score DESC
        LIMIT :limit
        """

        result = await db.get_many_raw(sql, {"limit": limit})

        # Parse leaderboard with per-entry error handling
        def parse_row(row):
            return schemas.CarromLeaderboardEntry(
                player_id=common.parse_uuid_safe(row[0]),
                username=common.parse_username_safe(row[1]),
                total_score=row[2],
                entry_date=row[3],
            )

        leaderboard = common.safe_parse_leaderboard(result.tuples(), parse_row)

    elif metric == "total_wins":
        # Count wins from carrom/round_finish events where winner = player_id
        # Use json_each() to unnest player_ids array
        sql = """
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
            COUNT(*) as total_wins,
            0 as total_score,
            MAX(bse.timestamp) as entry_date
        FROM player_sessions ps
        JOIN box_session_event bse ON bse.session_id = ps.session_id
        JOIN player p ON p.id = ps.player_id
        WHERE bse.type = 'carrom/round_finish'
        AND json_extract(bse.payload, '$.winner') = ps.player_id
        GROUP BY ps.player_id, p.tag
        ORDER BY total_wins DESC
        LIMIT :limit
        """

        result = await db.get_many_raw(sql, {"limit": limit})

        # Parse leaderboard with per-entry error handling
        def parse_row(row):
            return schemas.CarromLeaderboardEntry(
                player_id=common.parse_uuid_safe(row[0]),
                username=common.parse_username_safe(row[1]),
                total_score=row[3],  # Not used for wins metric
                total_wins=row[2],
                entry_date=row[4],
            )

        leaderboard = common.safe_parse_leaderboard(result.tuples(), parse_row)
    else:
        # Invalid metric
        leaderboard = []

    return schemas.CarromLeaderboardResponse(
        metric=metric,
        leaderboard=leaderboard,
    )
