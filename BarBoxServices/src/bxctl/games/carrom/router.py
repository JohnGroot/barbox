"""FastAPI router for Carrom game endpoints."""

from fastapi import APIRouter

from bxctl.app import dependencies
from bxctl.games import common

from . import schemas, service

router = APIRouter(prefix="/game/carrom", tags=["Game: Carrom"])


@router.get("/leaderboard")
async def get_carrom_leaderboard(
    db_service: dependencies.Database,
    metric: str = "total_score",
    limit: int = common.DEFAULT_LEADERBOARD_LIMIT,
) -> schemas.CarromLeaderboardResponse:
    """
    Get carrom leaderboard aggregated from carrom/round_finish events.

    Args:
        metric: "total_score" or "total_wins"
        limit: Maximum number of entries to return

    Returns:
        Carrom leaderboard with player rankings
    """
    return await service.get_carrom_leaderboard(db_service, metric, limit)
