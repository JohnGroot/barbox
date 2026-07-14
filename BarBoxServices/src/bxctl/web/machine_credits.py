"""Machine credit management endpoints

Per box+game credit pools - credits transferred to machines persist until consumed by game start.
"""

import asyncio
from collections import defaultdict
from datetime import UTC, datetime
from uuid import UUID, uuid4

from fastapi import APIRouter, HTTPException, status
from structlog import get_logger

from bxctl import structures
from bxctl.db import defs
from . import dependencies

logger = get_logger()
router = APIRouter(prefix="/machine-credits", tags=["Core: Machine Credits"])

# Serializes check-then-insert consume requests per box+game pot so two
# concurrent consumes can't both read the same pre-insert balance and
# overdraw it. Single-process deployment (see fly.toml/start_backend.sh) makes
# an in-process lock sufficient; SQLite has no row-level locking to lean on
# instead.
_consume_locks: defaultdict[tuple[str, str], asyncio.Lock] = defaultdict(asyncio.Lock)


@router.get("/{game_tag}")
async def get_machine_credits(
    game_tag: str,
    box_id: UUID,
    db_service: dependencies.Database,
) -> structures.MachineCreditsResponse:
    """Get machine credit pot balance and player contributions

    Args:
            game_tag: Game type identifier (e.g., "carrom")
            box_id: Physical box identifier (query param)

    Returns:
            Machine credit pot balance and individual player contributions

    Example:
            GET /machine-credits/carrom?box_id={box_id}
            → {"box_id": "...", "game_tag": "carrom", "balance": 8, "contributions": [...]}
    """

    # Aggregate machine_credit/deposit and machine_credit/consume events
    sql = """
	SELECT
		COALESCE(
			SUM(CASE
				WHEN bse.type = 'machine_credit/deposit' THEN
					CAST(json_extract(bse.payload, '$.amount') AS INTEGER)
				WHEN bse.type = 'machine_credit/consume' THEN
					-CAST(json_extract(bse.payload, '$.amount') AS INTEGER)
				ELSE 0
			END),
			0
		) as balance
	FROM box_session_event bse
	WHERE bse.type IN ('machine_credit/deposit', 'machine_credit/consume')
	AND json_extract(bse.payload, '$.box_id') = :box_id
	AND json_extract(bse.payload, '$.game_tag') = :game_tag
	"""

    result = await db_service.get_many_raw(
        sql, {"box_id": box_id.hex, "game_tag": game_tag}
    )

    balance = result.scalar() or 0

    # Query player contributions
    simple_contributions_sql = """
	SELECT
		json_extract(bse.payload, '$.player_id') as player_id,
		SUM(CAST(json_extract(bse.payload, '$.amount') AS INTEGER)) as amount
	FROM box_session_event bse
	WHERE bse.type = 'machine_credit/deposit'
	AND json_extract(bse.payload, '$.box_id') = :box_id
	AND json_extract(bse.payload, '$.game_tag') = :game_tag
	GROUP BY player_id
	ORDER BY player_id
	"""

    contributions_result = await db_service.get_many_raw(
        simple_contributions_sql, {"box_id": box_id.hex, "game_tag": game_tag}
    )

    contributions = [
        structures.MachinePlayerContribution(
            player_id=UUID(str(row[0])), amount=int(row[1])
        )
        for row in contributions_result.tuples()
    ]

    logger.info(
        "machine_credits_query",
        box_id=str(box_id),
        game_tag=game_tag,
        balance=balance,
        contribution_count=len(contributions),
    )

    return structures.MachineCreditsResponse(
        box_id=box_id, game_tag=game_tag, balance=balance, contributions=contributions
    )


@router.post("/{game_tag}/deposit", status_code=201)
async def deposit_machine_credits(
    game_tag: str,
    request: structures.MachineCreditsDepositRequest,
    authenticated_box: dependencies.BoxAuthenticated,
    authenticated_player: dependencies.AuthenticatedPlayer,
    db_service: dependencies.Database,
) -> structures.MachineCreditsResponse:
    """Deposit credits to machine pot (from player account)

    **Authentication**: Requires both Box API key AND Player JWT token.

    This endpoint records a machine_credit/deposit event. The caller must ensure
    the player has sufficient balance and has already created a credit/spend event.

    Args:
            game_tag: Game type identifier
            request: Deposit request containing box_id, player_id, amount, lobby_session_id

    Returns:
            Updated machine credit pot balance and contributions

    Headers:
            X-Box-API-Key: Box API key for authentication
            Authorization: Bearer <player_jwt_token>

    Example:
            POST /machine-credits/carrom/deposit
            {
                    "box_id": "...",
                    "player_id": "...",
                    "amount": 5,
                    "lobby_session_id": "..."
            }
    """
    # Verify box_id matches authenticated box
    if request.box_id != authenticated_box.id:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={
                "code": structures.ErrorCode.VALIDATION_ERROR,
                "message": "Box ID in request does not match authenticated box.",
            },
        )

    # Verify player_id matches authenticated player
    if request.player_id != authenticated_player:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={
                "code": structures.ErrorCode.VALIDATION_ERROR,
                "message": "Player ID in request does not match authenticated player.",
            },
        )

    # Create machine_credit/deposit event
    event_id = uuid4()
    now = datetime.now(UTC)

    await db_service.create(
        target=defs.BoxSessionEvent,
        data={
            "id": event_id,
            "session_id": request.lobby_session_id,
            "type": "machine_credit/deposit",
            "timestamp": now,
            "payload": {
                "box_id": request.box_id.hex,
                "game_tag": game_tag,
                "player_id": request.player_id.hex,
                "amount": request.amount,
            },
        },
    )

    logger.info(
        "machine_credit_deposited",
        box_id=str(request.box_id),
        game_tag=game_tag,
        player_id=str(request.player_id),
        amount=request.amount,
        event_id=str(event_id),
    )

    # Return updated balance
    return await get_machine_credits(game_tag, request.box_id, db_service)


@router.post("/{game_tag}/consume", status_code=200)
async def consume_machine_credits(
    game_tag: str,
    request: structures.MachineCreditsConsumeRequest,
    authenticated_box: dependencies.BoxAuthenticated,
    db_service: dependencies.Database,
) -> structures.MachineCreditsResponse:
    """Consume credits from machine pot (for game start)

    **Authentication**: Requires Box API key.

    Args:
            game_tag: Game type identifier
            request: Consume request containing box_id, amount, game_session_id

    Returns:
            Updated machine credit pot balance (should be reduced by amount)

    Headers:
            X-Box-API-Key: Box API key for authentication

    Example:
            POST /machine-credits/carrom/consume
            {
                    "box_id": "...",
                    "amount": 8,
                    "game_session_id": "..."
            }
    """
    # Verify box_id matches authenticated box
    if request.box_id != authenticated_box.id:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={
                "code": structures.ErrorCode.VALIDATION_ERROR,
                "message": "Box ID in request does not match authenticated box.",
            },
        )

    # Serialize check-then-insert per box+game pot to prevent concurrent
    # consumes from both passing the balance check and overdrawing it.
    lock = _consume_locks[(game_tag, request.box_id.hex)]
    async with lock:
        current = await get_machine_credits(game_tag, request.box_id, db_service)
        if current.balance < request.amount:
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail=f"Insufficient machine credits: have {current.balance}, need {request.amount}",
            )

        # Create machine_credit/consume event
        event_id = uuid4()
        now = datetime.now(UTC)

        await db_service.create(
            target=defs.BoxSessionEvent,
            data={
                "id": event_id,
                "session_id": request.game_session_id,
                "type": "machine_credit/consume",
                "timestamp": now,
                "payload": {
                    "box_id": request.box_id.hex,
                    "game_tag": game_tag,
                    "amount": request.amount,
                },
            },
        )

    logger.info(
        "machine_credit_consumed",
        box_id=str(request.box_id),
        game_tag=game_tag,
        amount=request.amount,
        event_id=str(event_id),
    )

    # Return updated balance
    return await get_machine_credits(game_tag, request.box_id, db_service)
