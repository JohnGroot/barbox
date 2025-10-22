from fastapi import APIRouter
from sqlalchemy import select

from bxctl import db, structures

from . import dependencies

router = APIRouter(prefix="/player")


@router.post("/", status_code=201)
async def register_player(
    new_player: structures.PlayerCreate,
    db_service: dependencies.Database,
) -> structures.PlayerDetail:
    return await db_service.create(
        target=db.defs.Player,
        data=new_player,
        read_as=structures.PlayerDetail,
    )


@router.get("/username/{username}/available")
async def check_username_available(
    username: str,
    db_service: dependencies.Database,
) -> structures.UsernameAvailabilityResponse:
    """Check if username is available for registration"""

    # Query for existing player with this username
    result = await db_service.session.execute(
        select(db.defs.Player).where(db.defs.Player.tag == username)
    )
    existing_player = result.scalar_one_or_none()

    is_available = existing_player is None

    return structures.UsernameAvailabilityResponse(
        username=username,
        is_available=is_available
    )


@router.get("/{player_id}/credits")
async def get_player_credits(
    player_id: UUID,
    location_id: str,
    db_service: dependencies.Database,
) -> structures.PlayerCreditsResponse:
    """Get player's credit balance for a specific location"""

    # Aggregate credits from credit/earn and credit/spend events
    sql = """
    SELECT
        COALESCE(
            SUM(CASE
                WHEN bse.type = 'credit/earn' THEN
                    CAST(json_extract(bse.payload, '$.amount') AS INTEGER)
                WHEN bse.type = 'credit/spend' THEN
                    -CAST(json_extract(bse.payload, '$.amount') AS INTEGER)
                ELSE 0
            END),
            0
        ) as credits
    FROM box_session_event bse
    JOIN box_session bs ON bse.session_id = bs.id
    WHERE bs.player_id = :player_id
    AND bse.type IN ('credit/earn', 'credit/spend')
    AND json_extract(bse.payload, '$.location_id') = :location_id
    """

    result = await db_service.get_one_raw(sql, {
        "player_id": str(player_id),
        "location_id": location_id
    })

    credits = result.scalar() or 0

    return structures.PlayerCreditsResponse(
        player_id=player_id,
        location_id=location_id,
        credits=credits
    )
