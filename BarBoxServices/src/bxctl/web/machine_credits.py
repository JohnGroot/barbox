"""Machine credit management endpoints

Per box+game credit pools - credits transferred to machines persist until consumed by game start.
"""

from datetime import UTC, datetime
from uuid import UUID, uuid4

from fastapi import APIRouter, HTTPException, status
from structlog import get_logger

from bxctl import structures
from bxctl.db import defs
from . import dependencies

logger = get_logger()
router = APIRouter(prefix="/game")


@router.get("/{game_tag}/machine-credits")
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
		GET /game/carrom/machine-credits?box_id={box_id}
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

	result = await db_service.get_many_raw(sql, {
		"box_id": box_id.hex,
		"game_tag": game_tag
	})

	balance = result.scalar() or 0

	# Query player contributions (only deposits that haven't been consumed yet)
	# Group by player_id and sum their deposit amounts
	contributions_sql = """
	WITH deposits AS (
		SELECT
			json_extract(bse.payload, '$.player_id') as player_id,
			SUM(CAST(json_extract(bse.payload, '$.amount') AS INTEGER)) as deposited
		FROM box_session_event bse
		WHERE bse.type = 'machine_credit/deposit'
		AND json_extract(bse.payload, '$.box_id') = :box_id
		AND json_extract(bse.payload, '$.game_tag') = :game_tag
		GROUP BY player_id
	),
	consumes AS (
		SELECT
			SUM(CAST(json_extract(bse.payload, '$.amount') AS INTEGER)) as consumed
		FROM box_session_event bse
		WHERE bse.type = 'machine_credit/consume'
		AND json_extract(bse.payload, '$.box_id') = :box_id
		AND json_extract(bse.payload, '$.game_tag') = :game_tag
	)
	SELECT
		d.player_id,
		CAST(
			d.deposited * 1.0 /
			(SELECT SUM(deposited) FROM deposits) *
			(SELECT deposited FROM deposits) -
			COALESCE((SELECT consumed FROM consumes), 0)
		AS INTEGER) as remaining
	FROM deposits d
	WHERE d.deposited > 0
	ORDER BY d.player_id
	"""

	# For now, use simpler logic: just return all deposit contributions
	# The proportional calculation above is complex and may need refinement
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

	contributions_result = await db_service.get_many_raw(simple_contributions_sql, {
		"box_id": box_id.hex,
		"game_tag": game_tag
	})

	contributions = [
		structures.MachinePlayerContribution(
			player_id=UUID(str(row[0])),
			amount=int(row[1])
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
		box_id=box_id,
		game_tag=game_tag,
		balance=balance,
		contributions=contributions
	)


@router.post("/{game_tag}/machine-credits/deposit", status_code=201)
async def deposit_machine_credits(
	game_tag: str,
	box_id: UUID,
	player_id: UUID,
	amount: int,
	lobby_session_id: UUID,
	db_service: dependencies.Database,
) -> structures.MachineCreditsResponse:
	"""Deposit credits to machine pot (from player account)

	This endpoint records a machine_credit/deposit event. The caller must ensure
	the player has sufficient balance and has already created a credit/spend event.

	Args:
		game_tag: Game type identifier
		box_id: Physical box identifier (query param)
		player_id: Player making the deposit (query param)
		amount: Credits to deposit (query param)
		lobby_session_id: Player's lobby session ID (query param)

	Returns:
		Updated machine credit pot balance and contributions

	Example:
		POST /game/carrom/machine-credits/deposit?box_id={box}&player_id={player}&amount=5&lobby_session_id={session}
	"""

	if amount <= 0:
		raise HTTPException(
			status_code=status.HTTP_400_BAD_REQUEST,
			detail="Deposit amount must be positive"
		)

	# Create machine_credit/deposit event
	event_id = uuid4()
	now = datetime.now(UTC)

	await db_service.create(
		target=defs.BoxSessionEvent,
		data={
			"id": event_id,
			"session_id": lobby_session_id,
			"type": "machine_credit/deposit",
			"timestamp": now,
			"payload": {
				"box_id": box_id.hex,
				"game_tag": game_tag,
				"player_id": player_id.hex,
				"amount": amount,
			}
		}
	)

	logger.info(
		"machine_credit_deposited",
		box_id=str(box_id),
		game_tag=game_tag,
		player_id=str(player_id),
		amount=amount,
		event_id=str(event_id),
	)

	# Return updated balance
	return await get_machine_credits(game_tag, box_id, db_service)


@router.post("/{game_tag}/machine-credits/consume", status_code=200)
async def consume_machine_credits(
	game_tag: str,
	box_id: UUID,
	amount: int,
	game_session_id: UUID,
	db_service: dependencies.Database,
) -> structures.MachineCreditsResponse:
	"""Consume credits from machine pot (for game start)

	Args:
		game_tag: Game type identifier
		box_id: Physical box identifier (query param)
		amount: Credits to consume (query param)
		game_session_id: Game session ID (query param)

	Returns:
		Updated machine credit pot balance (should be reduced by amount)

	Example:
		POST /game/carrom/machine-credits/consume?box_id={box}&amount=8&game_session_id={session}
	"""

	if amount <= 0:
		raise HTTPException(
			status_code=status.HTTP_400_BAD_REQUEST,
			detail="Consume amount must be positive"
		)

	# Verify sufficient balance
	current = await get_machine_credits(game_tag, box_id, db_service)
	if current.balance < amount:
		raise HTTPException(
			status_code=status.HTTP_400_BAD_REQUEST,
			detail=f"Insufficient machine credits: have {current.balance}, need {amount}"
		)

	# Create machine_credit/consume event
	event_id = uuid4()
	now = datetime.now(UTC)

	await db_service.create(
		target=defs.BoxSessionEvent,
		data={
			"id": event_id,
			"session_id": game_session_id,
			"type": "machine_credit/consume",
			"timestamp": now,
			"payload": {
				"box_id": box_id.hex,
				"game_tag": game_tag,
				"amount": amount,
			}
		}
	)

	logger.info(
		"machine_credit_consumed",
		box_id=str(box_id),
		game_tag=game_tag,
		amount=amount,
		event_id=str(event_id),
	)

	# Return updated balance
	return await get_machine_credits(game_tag, box_id, db_service)
