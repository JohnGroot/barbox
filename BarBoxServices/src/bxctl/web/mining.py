from datetime import UTC, datetime
from uuid import UUID

from fastapi import APIRouter

from bxctl import structures

from . import dependencies

router = APIRouter(prefix="/game/mining")


@router.get("/player/{player_id}/inventory")
async def get_player_mining_inventory(
    player_id: UUID,
    db_service: dependencies.Database,
) -> structures.MiningInventoryResponse:
    """
    Get player's global mining inventory aggregated from mining/extract_complete events.

    Args:
        player_id: Player UUID

    Returns:
        Player's gem inventory with quantities by gem type
    """

    # Aggregate gems from all mining/extract_complete events for this player
    sql = """
    SELECT
        json_extract(bse.payload, '$.gem_type') as gem_type,
        SUM(CAST(json_extract(bse.payload, '$.quantity') AS INTEGER)) as total_quantity,
        MAX(bse.timestamp) as last_updated
    FROM box_session_event bse
    JOIN box_session bs ON bse.session_id = bs.id
    WHERE bs.player_id = :player_id
    AND bse.type = 'mining/extract_complete'
    GROUP BY gem_type
    """

    result = await db_service.get_many_raw(sql, {"player_id": str(player_id)})

    # Build gems dictionary
    gems = {}
    last_updated = datetime.now(UTC)

    for row in result.tuples():
        gem_type = row[0]
        quantity = row[1]
        row_timestamp = row[2]

        gems[gem_type] = quantity

        # Track most recent update
        if row_timestamp and row_timestamp > last_updated:
            last_updated = row_timestamp

    return structures.MiningInventoryResponse(
        player_id=player_id,
        gems=gems,
        last_updated=last_updated,
    )
