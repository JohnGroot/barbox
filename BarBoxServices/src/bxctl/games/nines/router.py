"""FastAPI router for Nines game endpoints."""

from fastapi import APIRouter

from bxctl.app import dependencies

from . import schemas, service

router = APIRouter(prefix="/game/nines", tags=["Game: Nines"])


@router.get("/jackpot/{venue_name}")
async def get_jackpot_state(
    venue_name: str,
    db_service: dependencies.Database,
) -> schemas.NinesJackpotResponse:
    """
    Get jackpot state for a venue.

    Returns the last jackpot win timestamp and details for the specified venue.
    Client uses this to calculate the current jackpot value based on:

    `current_jackpot = base_value + (days_since_last_win * daily_growth)`

    If no jackpot has ever been won at this venue, last_win_timestamp will be null,
    and client should use its configured base jackpot value.

    Args:
        venue_name: Venue identifier (e.g., "best_intentions")

    Returns:
        NinesJackpotResponse with last win details
    """
    return await service.get_jackpot_state(db_service, venue_name)
