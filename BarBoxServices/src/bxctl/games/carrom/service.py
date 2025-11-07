"""Business logic and database queries for Carrom game."""

from uuid import UUID

from bxctl.db import service as db_service

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

        leaderboard = []
        for row in result.tuples():
            try:
                entry = schemas.CarromLeaderboardEntry(
                    player_id=UUID(str(row[0])) if row[0] else UUID('00000000-0000-0000-0000-000000000000'),
                    username=row[1] if row[1] else "Unknown",
                    total_score=row[2],
                    entry_date=row[3],
                )
                leaderboard.append(entry)
            except Exception as e:
                # Log but don't crash - skip this problematic entry
                print(f"[ERROR] Failed to parse leaderboard entry: row={row}, error={e}")
                import traceback
                traceback.print_exc()
                continue

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

        leaderboard = []
        for row in result.tuples():
            try:
                entry = schemas.CarromLeaderboardEntry(
                    player_id=UUID(str(row[0])) if row[0] else UUID('00000000-0000-0000-0000-000000000000'),
                    username=row[1] if row[1] else "Unknown",
                    total_score=row[3],  # Not used for wins metric
                    total_wins=row[2],
                    entry_date=row[4],
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

    return schemas.CarromLeaderboardResponse(
        metric=metric,
        leaderboard=leaderboard,
    )
