from uuid import UUID

from fastapi import APIRouter

from bxctl import structures

from . import dependencies

router = APIRouter(prefix="/game/carrom")


@router.get("/leaderboard")
async def get_carrom_leaderboard(
    db_service: dependencies.Database,
    metric: str = "total_score",
    limit: int = 10,
) -> structures.CarromLeaderboardResponse:
    """
    Get carrom leaderboard aggregated from carrom/round_finish events.

    Args:
        metric: "total_score" or "total_wins"
        limit: Maximum number of entries to return

    Returns:
        Carrom leaderboard with player rankings
    """

    if metric == "total_score":
        # Aggregate total scores from carrom/round_finish events
        # Score is stored in payload.scores[player_id]
        sql = """
        SELECT
            bs.player_id,
            p.tag as username,
            SUM(
                CAST(
                    json_extract(
                        bse.payload,
                        '$.scores.' || bs.player_id
                    ) AS INTEGER
                )
            ) as total_score
        FROM box_session_event bse
        JOIN box_session bs ON bse.session_id = bs.id
        JOIN player p ON bs.player_id = p.id
        WHERE bse.type = 'carrom/round_finish'
        GROUP BY bs.player_id, p.tag
        ORDER BY total_score DESC
        LIMIT :limit
        """

        result = await db_service.get_many_raw(sql, {"limit": limit})

        leaderboard = [
            structures.CarromLeaderboardEntry(
                player_id=UUID(str(row[0])),
                username=row[1],
                total_score=row[2],
            )
            for row in result.tuples()
        ]

    elif metric == "total_wins":
        # Count wins from carrom/round_finish events where winner = player_id
        sql = """
        SELECT
            bs.player_id,
            p.tag as username,
            COUNT(*) as total_wins,
            0 as total_score
        FROM box_session_event bse
        JOIN box_session bs ON bse.session_id = bs.id
        JOIN player p ON bs.player_id = p.id
        WHERE bse.type = 'carrom/round_finish'
        AND json_extract(bse.payload, '$.winner') = bs.player_id
        GROUP BY bs.player_id, p.tag
        ORDER BY total_wins DESC
        LIMIT :limit
        """

        result = await db_service.get_many_raw(sql, {"limit": limit})

        leaderboard = [
            structures.CarromLeaderboardEntry(
                player_id=UUID(str(row[0])),
                username=row[1],
                total_score=row[3],  # Not used for wins metric
                total_wins=row[2],
            )
            for row in result.tuples()
        ]
    else:
        # Invalid metric
        leaderboard = []

    return structures.CarromLeaderboardResponse(
        metric=metric,
        leaderboard=leaderboard,
    )
