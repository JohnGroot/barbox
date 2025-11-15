"""FastAPI router for Racing game endpoints."""

from fastapi import APIRouter

from bxctl.web import dependencies

from . import schemas, service

router = APIRouter(prefix="/game/racing", tags=["Game: Racing"])


@router.get("/leaderboard")
async def get_racing_leaderboard(
    track_id: str,
    db_service: dependencies.Database,
    metric: str = "best_race",
    laps: int | None = None,
    limit: int = 10,
) -> schemas.RacingLeaderboardResponse:
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
    return await service.get_racing_leaderboard(db_service, track_id, metric, laps, limit)
