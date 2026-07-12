"""Business logic and database queries for Nines game."""

from datetime import UTC, datetime

from bxctl.db import service as db_service

from . import schemas


async def get_jackpot_state(
    db: db_service.CRUD,
    venue_name: str,
) -> schemas.NinesJackpotResponse:
    """
    Get jackpot state for a venue.

    Queries for the most recent nines/jackpot_won event at this venue
    to determine the last win timestamp. Client uses this to calculate
    current jackpot value based on time elapsed.

    Args:
        db: Database CRUD service
        venue_name: Venue identifier (e.g., "best_intentions")

    Returns:
        NinesJackpotResponse with last win details (or None values if never won)
    """

    # Query for most recent jackpot win at this venue
    sql = """
    SELECT
        bse.timestamp as win_timestamp,
        json_extract(bse.payload, '$.player_id') as winner_id,
        CAST(json_extract(bse.payload, '$.jackpot_amount') AS INTEGER) as jackpot_amount,
        p.tag as winner_name
    FROM box_session_event bse
    JOIN box_session bs ON bse.session_id = bs.id
    -- Nines stores the winner's phone number (not a UUID) in payload.player_id,
    -- unlike carrom/racing which use the player UUID. Join on phone_number
    -- accordingly; this only matches if the client's PhoneNumber string is
    -- stored in the same E.164 form as player.phone_number.
    LEFT JOIN player p ON json_extract(bse.payload, '$.player_id') = p.phone_number
    WHERE bse.type = 'nines/jackpot_won'
    AND json_extract(bse.payload, '$.venue_name') = :venue_name
    ORDER BY bse.timestamp DESC
    LIMIT 1
    """

    result = await db.get_many_raw(sql, {"venue_name": venue_name})
    row = result.first()

    if row:
        # Parse timestamp from database
        timestamp_str = row[0]
        if timestamp_str:
            try:
                last_win_timestamp = datetime.fromisoformat(timestamp_str).replace(tzinfo=UTC)
            except ValueError:
                last_win_timestamp = None
        else:
            last_win_timestamp = None

        # Winner name - prefer player tag; fall back to "Unknown" rather than
        # exposing the raw phone number stored in payload.player_id.
        winner_name = row[3] if row[3] else "Unknown"
        jackpot_amount = row[2] if row[2] else 0

        return schemas.NinesJackpotResponse(
            venue_name=venue_name,
            last_win_timestamp=last_win_timestamp,
            last_winner_name=winner_name,
            last_jackpot_amount=jackpot_amount,
        )

    # No jackpot has ever been won at this venue
    return schemas.NinesJackpotResponse(
        venue_name=venue_name,
        last_win_timestamp=None,
        last_winner_name=None,
        last_jackpot_amount=None,
    )
