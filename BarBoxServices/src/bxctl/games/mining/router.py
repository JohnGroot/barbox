"""FastAPI router for Mining game endpoints."""

from uuid import UUID

from fastapi import APIRouter, HTTPException, status
from sqlalchemy import text
from structlog import get_logger

from bxctl import env
from bxctl.web import dependencies

from . import schemas, service

logger = get_logger()
router = APIRouter(prefix="/game/mining", tags=["Game: Mining"])


@router.get("/player/{player_id}/state")
async def get_player_state(
    player_id: UUID,
    location_id: str,
    db_service: dependencies.Database,
) -> schemas.MiningStateResponse:
    """
    Get complete player mining state for specific location.

    Data Scoping:
    - GLOBAL: Inventory (gems from all locations combined)
    - LOCATION-SPECIFIC: Upgrades, timestamp, metadata (first-time bonus)

    This unified endpoint provides all necessary data to initialize the mining game
    at a specific location, combining global inventory with location-specific progress.

    Args:
        player_id: Player UUID
        location_id: Location identifier (venue name, e.g., "best_intentions")

    Returns:
        Unified state with location-scoped upgrades and global inventory
    """
    return await service.get_player_state(db_service, player_id, location_id)


@router.delete("/player/{player_id}/reset-state", status_code=200)
async def reset_player_mining_state(
    player_id: UUID,
    location_id: str,
    db_service: dependencies.Database,
) -> dict:
    """Reset a player's mining state at a specific location by deleting location-scoped events.

    This is a surgical reset that only affects mining game data for the specified player
    at the specified location. Other locations, other game data, and the player account remain intact.

    Data Deleted (per-location):
    - Upgrade purchases at this location
    - Extraction timestamps at this location
    - First-time bonus status at this location

    Data Preserved:
    - Global inventory (gems extracted at this location are NOT removed from inventory)
    - Mining progress at other locations
    - All other game data

    WARNING: This deletes location-specific mining progress for the player.
    Only available in dev/test environments.

    Args:
        player_id: UUID of the player whose mining state should be reset
        location_id: Location identifier (venue name) to reset (query parameter)

    Returns:
        200: Mining state reset successfully with count of deleted events
        404: Endpoint not available in production
    """
    if env.is_production():
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Test endpoints are not available in production",
        )

    try:
        # First, count events that will be deleted
        count_sql = """
        WITH mining_sessions AS (
            SELECT id
            FROM box_session
            WHERE host_player_id = :player_id
            AND game_tag = 'mining'
        )
        SELECT COUNT(*)
        FROM box_session_event
        WHERE session_id IN (SELECT id FROM mining_sessions)
        AND type LIKE 'mining/%'
        AND json_extract(payload, '$.location_id') = :location_id
        """

        count_result = await db_service.session.execute(
            text(count_sql),
            {"player_id": str(player_id), "location_id": location_id},
        )
        deleted_count = count_result.scalar()

        # Then delete mining events for this player at this location
        delete_sql = """
        WITH mining_sessions AS (
            SELECT id
            FROM box_session
            WHERE host_player_id = :player_id
            AND game_tag = 'mining'
        )
        DELETE FROM box_session_event
        WHERE session_id IN (SELECT id FROM mining_sessions)
        AND type LIKE 'mining/%'
        AND json_extract(payload, '$.location_id') = :location_id
        """

        await db_service.session.execute(
            text(delete_sql),
            {"player_id": str(player_id), "location_id": location_id},
        )
        await db_service.session.commit()

        logger.info(
            "mining_state_reset",
            player_id=str(player_id),
            location_id=location_id,
            deleted_events=deleted_count,
        )

        return {
            "status": "success",
            "message": f"Mining state reset for player {player_id} at location {location_id}",
            "deleted_events": deleted_count,
            "player_id": str(player_id),
            "location_id": location_id,
        }

    except Exception as e:
        logger.exception(
            "mining_state_reset_failed", player_id=str(player_id), error=str(e)
        )
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={"code": "OPERATION_FAILED", "message": str(e)},
        )


# ============= LOCATION REGISTRATION =============


@router.post("/location/register")
async def register_location(
    venue_name: str,
    db_service: dependencies.Database,
) -> schemas.MiningLocationResponse:
    """
    Register a mining location with balanced gem type assignment.

    This is an idempotent endpoint - calling it multiple times with the same
    venue_name returns the same location data. New locations are assigned
    the gem type with the fewest existing locations for balance.

    Args:
        venue_name: Venue identifier (e.g., "best_intentions")

    Returns:
        Location details including assigned gem type
    """
    try:
        return await service.register_or_get_location(db_service, venue_name)
    except ValueError as e:
        logger.exception(
            "location_registration_failed", venue_name=venue_name, error=str(e)
        )
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={"code": "REGISTRATION_FAILED", "message": str(e)},
        )


@router.get("/locations")
async def get_all_locations(
    db_service: dependencies.Database,
) -> schemas.MiningLocationListResponse:
    """
    Get all registered mining locations with gem distribution stats.

    Returns:
        List of all locations with gem type distribution counts
    """
    return await service.get_all_locations(db_service)
