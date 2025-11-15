"""FastAPI router for Mining game endpoints."""

from uuid import UUID

from fastapi import APIRouter

from bxctl.web import dependencies

from . import schemas, service

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
