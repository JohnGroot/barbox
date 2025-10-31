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
    Get player's global mining inventory aggregated from events.
    Calculates: (extracted gems - spent gems from credits/upgrades)

    Args:
        player_id: Player UUID

    Returns:
        Player's gem inventory with quantities by gem type
    """

    # Aggregate gems extracted and spent using CTEs
    sql = """
    WITH gem_extractions AS (
        SELECT
            json_extract(bse.payload, '$.gem_type') as gem_type,
            SUM(CAST(json_extract(bse.payload, '$.quantity') AS INTEGER)) as total_extracted,
            MAX(bse.timestamp) as last_extraction
        FROM box_session_event bse
        JOIN box_session bs ON bse.session_id = bs.id
        WHERE bs.player_id = :player_id
        AND bse.type = 'mining/extract_complete'
        GROUP BY gem_type
    ),
    gem_spending_credits AS (
        SELECT
            json_extract(bse.payload, '$.gem_type') as gem_type,
            SUM(CAST(json_extract(bse.payload, '$.gems_spent') AS INTEGER)) as total_spent,
            MAX(bse.timestamp) as last_spend
        FROM box_session_event bse
        JOIN box_session bs ON bse.session_id = bs.id
        WHERE bs.player_id = :player_id
        AND bse.type = 'mining/credit_deposit'
        GROUP BY gem_type
    ),
    gem_spending_upgrades AS (
        SELECT
            key as gem_type,
            SUM(CAST(value AS INTEGER)) as total_spent,
            MAX(bse.timestamp) as last_spend
        FROM box_session_event bse
        JOIN box_session bs ON bse.session_id = bs.id,
        json_each(bse.payload, '$.cost')
        WHERE bs.player_id = :player_id
        AND bse.type = 'mining/upgrade_purchase'
        GROUP BY gem_type
    )
    SELECT
        e.gem_type,
        COALESCE(e.total_extracted, 0) - COALESCE(c.total_spent, 0) - COALESCE(u.total_spent, 0) as net_quantity,
        MAX(e.last_extraction, c.last_spend, u.last_spend) as last_updated
    FROM gem_extractions e
    LEFT JOIN gem_spending_credits c ON e.gem_type = c.gem_type
    LEFT JOIN gem_spending_upgrades u ON e.gem_type = u.gem_type
    WHERE COALESCE(e.total_extracted, 0) - COALESCE(c.total_spent, 0) - COALESCE(u.total_spent, 0) > 0
    GROUP BY e.gem_type
    """

    result = await db_service.get_many_raw(sql, {"player_id": player_id.hex})

    # Build gems dictionary
    gems = {}
    last_updated = datetime.now(UTC)

    for row in result.tuples():
        gem_type = row[0]
        net_quantity = row[1]
        row_timestamp_str = row[2]

        gems[gem_type] = net_quantity

        # Track most recent update (parse string timestamp)
        if row_timestamp_str:
            row_timestamp = datetime.fromisoformat(row_timestamp_str).replace(tzinfo=UTC)
            if row_timestamp > last_updated:
                last_updated = row_timestamp

    return structures.MiningInventoryResponse(
        player_id=player_id,
        gems=gems,
        last_updated=last_updated,
    )


@router.get("/player/{player_id}/upgrades")
async def get_player_mining_upgrades(
    player_id: UUID,
    db_service: dependencies.Database,
) -> structures.MiningUpgradesResponse:
    """
    Get player's upgrade levels aggregated from mining/upgrade_purchase events.

    Args:
        player_id: Player UUID

    Returns:
        Player's upgrade levels by upgrade type
    """

    # Aggregate upgrades from mining/upgrade_purchase events
    sql = """
    SELECT
        json_extract(bse.payload, '$.upgrade_type') as upgrade_type,
        MAX(CAST(json_extract(bse.payload, '$.level') AS INTEGER)) as max_level,
        MAX(bse.timestamp) as last_updated
    FROM box_session_event bse
    JOIN box_session bs ON bse.session_id = bs.id
    WHERE bs.player_id = :player_id
    AND bse.type = 'mining/upgrade_purchase'
    GROUP BY upgrade_type
    """

    result = await db_service.get_many_raw(sql, {"player_id": player_id.hex})

    # Build upgrades dictionary
    upgrades = {}
    last_updated = datetime.now(UTC)

    for row in result.tuples():
        upgrade_type = row[0]
        max_level = row[1]
        row_timestamp_str = row[2]

        upgrades[upgrade_type] = max_level

        # Track most recent update (parse string timestamp)
        if row_timestamp_str:
            row_timestamp = datetime.fromisoformat(row_timestamp_str).replace(tzinfo=UTC)
            if row_timestamp > last_updated:
                last_updated = row_timestamp

    return structures.MiningUpgradesResponse(
        player_id=player_id,
        upgrades=upgrades,
        last_updated=last_updated,
    )


@router.get("/player/{player_id}/mining_timestamp")
async def get_player_mining_timestamp(
    player_id: UUID,
    location_id: str,
    db_service: dependencies.Database,
) -> structures.MiningTimestampResponse:
    """
    Get last mining timestamp for player at specific location.

    Args:
        player_id: Player UUID
        location_id: Location identifier (query parameter)

    Returns:
        Last mining time for offline progress calculation
    """

    # Get last mining event timestamp for this player at this location
    sql = """
    SELECT MAX(bse.timestamp) as last_mining_time
    FROM box_session_event bse
    JOIN box_session bs ON bse.session_id = bs.id
    WHERE bs.player_id = :player_id
    AND json_extract(bse.payload, '$.location_id') = :location_id
    AND bse.type IN ('mining/extract_complete', 'mining/upgrade_purchase')
    """

    result = await db_service.get_many_raw(sql, {
        "player_id": player_id.hex,
        "location_id": location_id
    })
    row = result.first()

    if row and row[0]:
        last_mining_time = datetime.fromisoformat(row[0])
    else:
        last_mining_time = datetime.now(UTC)

    return structures.MiningTimestampResponse(
        player_id=player_id,
        location_id=location_id,
        last_mining_time=last_mining_time,
    )


@router.get("/player/{player_id}/metadata")
async def get_player_mining_metadata(
    player_id: UUID,
    db_service: dependencies.Database,
) -> structures.MiningMetadataResponse:
    """
    Get player mining metadata (first-time bonus status, statistics).

    Args:
        player_id: Player UUID

    Returns:
        Player mining metadata including bonus status and event counts
    """

    # Get metadata about player's mining activity
    sql = """
    SELECT
        COUNT(*) as total_events,
        MIN(bse.timestamp) as first_event_time,
        MAX(bse.timestamp) as last_event_time,
        SUM(CASE WHEN bse.type = 'mining/first_time_bonus' THEN 1 ELSE 0 END) as bonus_count
    FROM box_session_event bse
    JOIN box_session bs ON bse.session_id = bs.id
    WHERE bs.player_id = :player_id
    AND bse.type LIKE 'mining/%'
    """

    result = await db_service.get_many_raw(sql, {"player_id": player_id.hex})
    row = result.first()

    if row:
        total_events = row[0] or 0
        first_event_time = row[1]
        last_event_time = row[2]
        bonus_count = row[3] or 0
    else:
        total_events = 0
        first_event_time = None
        last_event_time = None
        bonus_count = 0

    return structures.MiningMetadataResponse(
        player_id=player_id,
        has_received_bonus=bonus_count > 0,
        total_events=total_events,
        first_event_time=first_event_time,
        last_event_time=last_event_time,
    )
