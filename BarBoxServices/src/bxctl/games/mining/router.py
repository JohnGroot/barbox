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


@router.get("/player/{player_id}/inventory")
async def get_player_mining_inventory(
    player_id: UUID,
    db_service: dependencies.Database,
) -> schemas.MiningInventoryResponse:
    """
    Get player's global mining inventory aggregated from events.
    Calculates: (extracted gems - spent gems from credits/upgrades)

    Args:
        player_id: Player UUID

    Returns:
        Player's gem inventory with quantities by gem type
    """
    return await service.get_player_mining_inventory(db_service, player_id)


@router.get("/player/{player_id}/upgrades")
async def get_player_mining_upgrades(
    player_id: UUID,
    db_service: dependencies.Database,
) -> schemas.MiningUpgradesResponse:
    """
    Get player's upgrade levels aggregated from mining/upgrade_purchase events.

    Args:
        player_id: Player UUID

    Returns:
        Player's upgrade levels by upgrade type
    """
    return await service.get_player_mining_upgrades(db_service, player_id)


@router.get("/player/{player_id}/mining_timestamp")
async def get_player_mining_timestamp(
    player_id: UUID,
    location_id: str,
    db_service: dependencies.Database,
) -> schemas.MiningTimestampResponse:
    """
    Get last mining timestamp for player at specific location.

    Args:
        player_id: Player UUID
        location_id: Location identifier (query parameter)

    Returns:
        Last mining time for offline progress calculation
    """
    return await service.get_player_mining_timestamp(db_service, player_id, location_id)


@router.get("/player/{player_id}/metadata")
async def get_player_mining_metadata(
    player_id: UUID,
    db_service: dependencies.Database,
) -> schemas.MiningMetadataResponse:
    """
    Get player mining metadata (first-time bonus status, statistics).

    Args:
        player_id: Player UUID

    Returns:
        Player mining metadata including bonus status and event counts
    """
    return await service.get_player_mining_metadata(db_service, player_id)


@router.delete("/player/{player_id}/reset-state", status_code=200)
async def reset_player_mining_state(
    player_id: UUID,
    db_service: dependencies.Database,
) -> dict:
    """Reset a player's mining state by deleting all mining-related events.

    This is a surgical reset that only affects mining game data for the specified player.
    Other game data and the player account remain intact.

    WARNING: This deletes all mining progress for the player.
    Only available in dev/test environments.

    Args:
        player_id: UUID of the player whose mining state should be reset

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
        """

        count_result = await db_service.session.execute(
            text(count_sql),
            {"player_id": str(player_id)},
        )
        deleted_count = count_result.scalar()

        # Then delete all mining events for this player
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
        """

        await db_service.session.execute(
            text(delete_sql),
            {"player_id": str(player_id)},
        )
        await db_service.session.commit()

        logger.info(
            "mining_state_reset",
            player_id=str(player_id),
            deleted_events=deleted_count,
        )

        return {
            "status": "success",
            "message": f"Mining state reset for player {player_id}",
            "deleted_events": deleted_count,
            "player_id": str(player_id),
        }

    except Exception as e:
        logger.error("mining_state_reset_failed", player_id=str(player_id), error=str(e))
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={"code": "OPERATION_FAILED", "message": str(e)},
        )
