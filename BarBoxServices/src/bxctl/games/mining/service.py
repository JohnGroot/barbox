"""Business logic and database queries for Mining game."""

import asyncio
from datetime import UTC, datetime
from uuid import UUID, uuid4

from sqlalchemy import select
from sqlalchemy.exc import IntegrityError

from bxctl.db import defs
from bxctl.db import service as db_service

from . import schemas


async def _get_player_mining_inventory(
    db: db_service.CRUD,
    player_id: UUID,
) -> schemas.MiningInventoryResponse:
    """
    Get player's global mining inventory aggregated from events.
    Calculates: (extracted gems - spent gems from credits/upgrades)

    Args:
        db: Database CRUD service
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
        WHERE bs.host_player_id = :player_id
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
        WHERE bs.host_player_id = :player_id
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
        WHERE bs.host_player_id = :player_id
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

    result = await db.get_many_raw(sql, {"player_id": player_id.hex})

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
            row_timestamp = datetime.fromisoformat(row_timestamp_str).replace(
                tzinfo=UTC
            )
            last_updated = max(last_updated, row_timestamp)

    return schemas.MiningInventoryResponse(
        player_id=player_id,
        gems=gems,
        last_updated=last_updated,
    )


async def _get_player_mining_upgrades(
    db: db_service.CRUD,
    player_id: UUID,
    location_id: str,
) -> schemas.MiningUpgradesResponse:
    """
    Get player's upgrade levels for a specific location.
    Upgrades are location-scoped - each venue has independent upgrade progression.

    Args:
        db: Database CRUD service
        player_id: Player UUID
        location_id: Location identifier (venue name)

    Returns:
        Player's upgrade levels for this location
    """

    # Aggregate upgrades from mining/upgrade_purchase events filtered by location
    sql = """
    SELECT
        json_extract(bse.payload, '$.upgrade_type') as upgrade_type,
        MAX(CAST(json_extract(bse.payload, '$.level') AS INTEGER)) as max_level,
        MAX(bse.timestamp) as last_updated
    FROM box_session_event bse
    JOIN box_session bs ON bse.session_id = bs.id
    WHERE bs.host_player_id = :player_id
    AND bse.type = 'mining/upgrade_purchase'
    AND json_extract(bse.payload, '$.location_id') = :location_id
    GROUP BY upgrade_type
    """

    result = await db.get_many_raw(
        sql, {"player_id": player_id.hex, "location_id": location_id}
    )

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
            row_timestamp = datetime.fromisoformat(row_timestamp_str).replace(
                tzinfo=UTC
            )
            last_updated = max(last_updated, row_timestamp)

    return schemas.MiningUpgradesResponse(
        player_id=player_id,
        location_id=location_id,
        upgrades=upgrades,
        last_updated=last_updated,
    )


async def _get_player_mining_timestamp(
    db: db_service.CRUD,
    player_id: UUID,
    location_id: str,
) -> schemas.MiningTimestampResponse:
    """
    Get last mining timestamp for player at specific location.

    Args:
        db: Database CRUD service
        player_id: Player UUID
        location_id: Location identifier

    Returns:
        Last mining time for offline progress calculation
    """

    # Get last extraction time from mining/extract_complete events
    # COALESCE fallback: payload timestamp (new events) → event timestamp (legacy support)
    # Kept for backwards compatibility with events that lack last_extraction_time field
    sql = """
    SELECT COALESCE(
        json_extract(bse.payload, '$.last_extraction_time'),
        bse.timestamp
    ) as last_mining_time
    FROM box_session_event bse
    JOIN box_session bs ON bse.session_id = bs.id
    WHERE bs.host_player_id = :player_id
    AND json_extract(bse.payload, '$.location_id') = :location_id
    AND bse.type = 'mining/extract_complete'
    ORDER BY bse.timestamp DESC
    LIMIT 1
    """

    result = await db.get_many_raw(
        sql, {"player_id": player_id.hex, "location_id": location_id}
    )
    row = result.first()

    if row and row[0]:
        last_mining_time = datetime.fromisoformat(row[0])
    else:
        # No extraction history - return None to let client decide default
        last_mining_time = None

    return schemas.MiningTimestampResponse(
        player_id=player_id,
        location_id=location_id,
        last_mining_time=last_mining_time,
    )


async def _get_player_mining_metadata(
    db: db_service.CRUD,
    player_id: UUID,
    location_id: str,
) -> schemas.MiningMetadataResponse:
    """
    Get player mining metadata for a specific location (first-time bonus status, statistics).
    First-time bonus and event statistics are tracked per-location.

    Args:
        db: Database CRUD service
        player_id: Player UUID
        location_id: Location identifier (venue name)

    Returns:
        Player mining metadata for this location including bonus status and event counts
    """

    # Get metadata about player's mining activity at this location
    sql = """
    SELECT
        COUNT(*) as total_events,
        MIN(bse.timestamp) as first_event_time,
        MAX(bse.timestamp) as last_event_time,
        SUM(CASE WHEN bse.type = 'mining/first_time_bonus' THEN 1 ELSE 0 END) as bonus_count
    FROM box_session_event bse
    JOIN box_session bs ON bse.session_id = bs.id
    WHERE bs.host_player_id = :player_id
    AND bse.type LIKE 'mining/%'
    AND json_extract(bse.payload, '$.location_id') = :location_id
    """

    result = await db.get_many_raw(
        sql, {"player_id": player_id.hex, "location_id": location_id}
    )
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

    return schemas.MiningMetadataResponse(
        player_id=player_id,
        location_id=location_id,
        has_received_bonus_at_location=bonus_count > 0,
        total_events=total_events,
        first_event_time=first_event_time,
        last_event_time=last_event_time,
    )


async def get_player_state(
    db: db_service.CRUD,
    player_id: UUID,
    location_id: str,
) -> schemas.MiningStateResponse:
    """
    Get complete player mining state in single query.
    Combines inventory, upgrades, timestamp, and metadata.

    Args:
        db: Database CRUD service
        player_id: Player UUID
        location_id: Location identifier for timestamp

    Returns:
        Unified state response with all player mining data
    """

    # Call all internal query methods in parallel using asyncio.gather
    (
        inventory_response,
        upgrades_response,
        timestamp_response,
        metadata_response,
    ) = await asyncio.gather(
        _get_player_mining_inventory(db, player_id),
        _get_player_mining_upgrades(db, player_id, location_id),
        _get_player_mining_timestamp(db, player_id, location_id),
        _get_player_mining_metadata(db, player_id, location_id),
    )

    return schemas.MiningStateResponse(
        player_id=player_id,
        location_id=location_id,
        inventory=inventory_response.gems,
        upgrades=upgrades_response.upgrades,
        last_extraction_time=timestamp_response.last_mining_time,
        metadata=metadata_response,
    )


# ============= LOCATION REGISTRATION =============


async def get_location_by_venue_name(
    db: db_service.CRUD,
    venue_name: str,
) -> defs.MiningLocation | None:
    """
    Get a mining location by venue name.

    Args:
        db: Database CRUD service
        venue_name: Venue identifier (e.g., "best_intentions")

    Returns:
        MiningLocation if found, None otherwise
    """
    stmt = select(defs.MiningLocation).where(
        defs.MiningLocation.venue_name == venue_name
    )
    result = await db.session.execute(stmt)
    return result.scalar_one_or_none()


async def get_gem_type_distribution(
    db: db_service.CRUD,
) -> dict[str, int]:
    """
    Get count of locations per gem type for balanced assignment.

    Args:
        db: Database CRUD service

    Returns:
        Dictionary mapping gem type to location count
    """
    sql = """
    SELECT gem_type, COUNT(*) as count
    FROM mining_location
    GROUP BY gem_type
    """
    result = await db.get_many_raw(sql, {})

    # Initialize all gem types to 0
    distribution = dict.fromkeys(schemas.GEM_TYPES, 0)

    # Update with actual counts
    for row in result.tuples():
        gem_type, count = row[0], row[1]
        if gem_type in distribution:
            distribution[gem_type] = count

    return distribution


async def register_or_get_location(
    db: db_service.CRUD,
    venue_name: str,
) -> schemas.MiningLocationResponse:
    """
    Idempotent location registration with balanced gem type assignment.

    If location already exists, returns it. Otherwise registers new location
    with the least-used gem type for balance across all locations.

    Handles race conditions via IntegrityError catch and re-query.

    Args:
        db: Database CRUD service
        venue_name: Venue identifier (e.g., "best_intentions")

    Returns:
        MiningLocationResponse with venue details and assigned gem type
    """
    # Check existing first (common case - fast path)
    existing = await get_location_by_venue_name(db, venue_name)
    if existing:
        return schemas.MiningLocationResponse(
            venue_name=existing.venue_name,
            gem_type=existing.gem_type,
            display_name=existing.display_name,
        )

    # Get distribution and assign least-used gem type
    distribution = await get_gem_type_distribution(db)
    gem_type = min(schemas.GEM_TYPES, key=lambda g: distribution.get(g, 0))

    # Generate display name from venue_name
    display_name = venue_name.replace("_", " ").title()

    # Create new location using db.create() pattern with explicit id
    try:
        await db.create(
            target=defs.MiningLocation,
            data={
                "id": uuid4(),
                "venue_name": venue_name,
                "gem_type": gem_type,
                "display_name": display_name,
                "created_at": datetime.now(UTC),
            },
        )

        return schemas.MiningLocationResponse(
            venue_name=venue_name,
            gem_type=gem_type,
            display_name=display_name,
        )

    except IntegrityError:
        # Race condition: another request registered this venue simultaneously
        # Rollback and return the existing record
        await db.session.rollback()
        existing = await get_location_by_venue_name(db, venue_name)
        if existing:
            return schemas.MiningLocationResponse(
                venue_name=existing.venue_name,
                gem_type=existing.gem_type,
                display_name=existing.display_name,
            )
        # Should not happen, but handle gracefully
        msg = f"Failed to register or retrieve location: {venue_name}"
        raise ValueError(msg)


async def get_all_locations(
    db: db_service.CRUD,
) -> schemas.MiningLocationListResponse:
    """
    Get all registered mining locations with gem distribution stats.

    Args:
        db: Database CRUD service

    Returns:
        List of all locations with gem type distribution counts
    """
    stmt = select(defs.MiningLocation).order_by(defs.MiningLocation.venue_name)
    result = await db.session.execute(stmt)
    locations = result.scalars().all()

    # Build response list
    location_responses = [
        schemas.MiningLocationResponse(
            venue_name=loc.venue_name,
            gem_type=loc.gem_type,
            display_name=loc.display_name,
        )
        for loc in locations
    ]

    # Calculate distribution
    distribution = dict.fromkeys(schemas.GEM_TYPES, 0)
    for loc in locations:
        if loc.gem_type in distribution:
            distribution[loc.gem_type] += 1

    return schemas.MiningLocationListResponse(
        locations=location_responses,
        gem_distribution=distribution,
    )
