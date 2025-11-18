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
        # Carrom stores scores as {"scores": {"player_id": score, ...}}
        # We need to extract scores for each player from the dynamic JSON keys
        # SQLite stores UUIDs without hyphens, but JSON keys have hyphens
        sql = """
        WITH player_scores AS (
            SELECT
                bs.host_player_id as player_id,
                -- Format UUID with hyphens: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
                SUBSTR(bs.host_player_id, 1, 8) || '-' ||
                SUBSTR(bs.host_player_id, 9, 4) || '-' ||
                SUBSTR(bs.host_player_id, 13, 4) || '-' ||
                SUBSTR(bs.host_player_id, 17, 4) || '-' ||
                SUBSTR(bs.host_player_id, 21) as player_id_formatted,
                bse.payload,
                bse.timestamp
            FROM box_session bs
            JOIN box_session_event bse ON bse.session_id = bs.id
            WHERE bse.type = 'carrom/round_finish'
              AND bs.game_tag = 'carrom'
        )
        SELECT
            ps.player_id,
            COALESCE(p.tag, 'Player ' || SUBSTR(ps.player_id, 1, 8)) as username,
            SUM(
                CAST(
                    json_extract(ps.payload, '$.scores."' || ps.player_id_formatted || '"') AS INTEGER
                )
            ) as total_score,
            MAX(ps.timestamp) as entry_date
        FROM player_scores ps
        LEFT JOIN player p ON ps.player_id = p.id
        WHERE json_extract(ps.payload, '$.scores."' || ps.player_id_formatted || '"') IS NOT NULL
        GROUP BY ps.player_id, COALESCE(p.tag, 'Player ' || SUBSTR(ps.player_id, 1, 8))
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
        # JSON payload has UUIDs with hyphens, need to convert DB UUIDs for matching
        sql = """
        WITH win_events AS (
            SELECT
                json_extract(bse.payload, '$.winner') as winner_formatted,
                bs.host_player_id,
                bse.timestamp
            FROM box_session bs
            JOIN box_session_event bse ON bse.session_id = bs.id
            WHERE bse.type = 'carrom/round_finish'
              AND bs.game_tag = 'carrom'
              AND json_extract(bse.payload, '$.winner') IS NOT NULL
        ),
        winner_ids AS (
            SELECT
                -- Convert hyphenated UUID back to non-hyphenated for player lookup
                REPLACE(we.winner_formatted, '-', '') as player_id,
                we.timestamp
            FROM win_events we
        )
        SELECT
            wi.player_id,
            COALESCE(p.tag, 'Player ' || SUBSTR(wi.player_id, 1, 8)) as username,
            COUNT(*) as total_wins,
            0 as total_score,
            MAX(wi.timestamp) as entry_date
        FROM winner_ids wi
        LEFT JOIN player p ON wi.player_id = p.id
        GROUP BY wi.player_id, COALESCE(p.tag, 'Player ' || SUBSTR(wi.player_id, 1, 8))
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
