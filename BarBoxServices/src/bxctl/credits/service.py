import asyncio
from collections import defaultdict
from datetime import UTC, datetime
from uuid import UUID, uuid4

from fastapi import HTTPException, status
from structlog import get_logger

from bxctl import errors
from bxctl.credits import schemas
from bxctl.db import defs
from bxctl.db.service import CRUD
from bxctl.registry import CoreEvent

logger = get_logger()

# Serializes check-then-insert consume requests per box+game pot so two
# concurrent consumes can't both read the same pre-insert balance and
# overdraw it. Single-process deployment (see fly.toml/start_backend.sh) makes
# an in-process lock sufficient; SQLite has no row-level locking to lean on
# instead.
_consume_locks: defaultdict[tuple[str, str], asyncio.Lock] = defaultdict(asyncio.Lock)


async def get_balance(
    game_tag: str,
    box_id: UUID,
    db_service: CRUD,
) -> schemas.MachineCreditsResponse:
    # Aggregate machine_credit/deposit and machine_credit/consume events in a
    # single table scan: group by player_id (NULL for consume events, which
    # carry no player_id) so both the per-player deposit total and the
    # overall signed balance can be derived from the same result set.
    sql = """
	SELECT
		json_extract(bse.payload, '$.player_id') as player_id,
		COALESCE(SUM(CASE
			WHEN bse.type = :deposit_type THEN
				CAST(json_extract(bse.payload, '$.amount') AS INTEGER)
			ELSE 0
		END), 0) as deposited,
		COALESCE(SUM(CASE
			WHEN bse.type = :consume_type THEN
				CAST(json_extract(bse.payload, '$.amount') AS INTEGER)
			ELSE 0
		END), 0) as consumed
	FROM box_session_event bse
	WHERE bse.type IN (:deposit_type, :consume_type)
	AND json_extract(bse.payload, '$.box_id') = :box_id
	AND json_extract(bse.payload, '$.game_tag') = :game_tag
	GROUP BY player_id
	ORDER BY player_id
	"""

    result = await db_service.get_many_raw(
        sql,
        {
            "box_id": box_id.hex,
            "game_tag": game_tag,
            "deposit_type": CoreEvent.MACHINE_CREDIT_DEPOSIT.value,
            "consume_type": CoreEvent.MACHINE_CREDIT_CONSUME.value,
        },
    )

    balance = 0
    contributions: list[schemas.MachinePlayerContribution] = []
    for player_id, deposited_raw, consumed_raw in result.tuples():
        deposited_amount = int(deposited_raw)
        consumed_amount = int(consumed_raw)
        balance += deposited_amount - consumed_amount
        # consume events carry no player_id, so their row groups under NULL
        # and is excluded from per-player contributions.
        if player_id is not None:
            contributions.append(
                schemas.MachinePlayerContribution(
                    player_id=UUID(str(player_id)), amount=deposited_amount
                )
            )

    logger.info(
        "machine_credits_query",
        box_id=str(box_id),
        game_tag=game_tag,
        balance=balance,
        contribution_count=len(contributions),
    )

    return schemas.MachineCreditsResponse(
        box_id=box_id, game_tag=game_tag, balance=balance, contributions=contributions
    )


async def deposit(
    game_tag: str,
    request: schemas.MachineCreditsDepositRequest,
    authenticated_box_id: UUID,
    authenticated_player_id: UUID,
    db_service: CRUD,
) -> schemas.MachineCreditsResponse:
    # Verify box_id matches authenticated box
    if request.box_id != authenticated_box_id:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={
                "code": errors.ErrorCode.VALIDATION_ERROR,
                "message": "Box ID in request does not match authenticated box.",
            },
        )

    # Verify player_id matches authenticated player
    if request.player_id != authenticated_player_id:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={
                "code": errors.ErrorCode.VALIDATION_ERROR,
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
            "type": CoreEvent.MACHINE_CREDIT_DEPOSIT,
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
    return await get_balance(game_tag, request.box_id, db_service)


async def consume(
    game_tag: str,
    request: schemas.MachineCreditsConsumeRequest,
    authenticated_box_id: UUID,
    db_service: CRUD,
) -> schemas.MachineCreditsResponse:
    # Verify box_id matches authenticated box
    if request.box_id != authenticated_box_id:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={
                "code": errors.ErrorCode.VALIDATION_ERROR,
                "message": "Box ID in request does not match authenticated box.",
            },
        )

    # Serialize check-then-insert per box+game pot to prevent concurrent
    # consumes from both passing the balance check and overdrawing it.
    lock = _consume_locks[(game_tag, request.box_id.hex)]
    async with lock:
        current = await get_balance(game_tag, request.box_id, db_service)
        if current.balance < request.amount:
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail={
                    "code": errors.ErrorCode.INSUFFICIENT_CREDITS,
                    "message": (
                        f"Insufficient machine credits: have {current.balance},"
                        f" need {request.amount}"
                    ),
                },
            )

        # Create machine_credit/consume event
        event_id = uuid4()
        now = datetime.now(UTC)

        await db_service.create(
            target=defs.BoxSessionEvent,
            data={
                "id": event_id,
                "session_id": request.game_session_id,
                "type": CoreEvent.MACHINE_CREDIT_CONSUME,
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
    return await get_balance(game_tag, request.box_id, db_service)
