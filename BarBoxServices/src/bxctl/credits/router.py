"""Machine credit management endpoints

Per box+game credit pools - credits transferred to machines persist until
consumed by game start.
"""

from uuid import UUID

from fastapi import APIRouter

from bxctl.app import dependencies
from bxctl.credits import schemas, service

router = APIRouter(prefix="/machine-credits", tags=["Core: Machine Credits"])


@router.get("/{game_tag}")
async def get_machine_credits(
    game_tag: str,
    box_id: UUID,
    db_service: dependencies.Database,
) -> schemas.MachineCreditsResponse:
    """Get machine credit pot balance and player contributions

    Args:
            game_tag: Game type identifier (e.g., "carrom")
            box_id: Physical box identifier (query param)

    Returns:
            Machine credit pot balance and individual player contributions

    Example:
            GET /machine-credits/carrom?box_id={box_id}
            → {"box_id": "...", "game_tag": "carrom", "balance": 8,
               "contributions": [...]}
    """
    return await service.get_balance(game_tag, box_id, db_service)


@router.post("/{game_tag}/deposit", status_code=201)
async def deposit_machine_credits(
    game_tag: str,
    request: schemas.MachineCreditsDepositRequest,
    authenticated_box: dependencies.BoxAuthenticated,
    authenticated_player: dependencies.AuthenticatedPlayer,
    db_service: dependencies.Database,
) -> schemas.MachineCreditsResponse:
    """Deposit credits to machine pot (from player account)

    **Authentication**: Requires both Box API key AND Player JWT token.

    This endpoint records a machine_credit/deposit event. The caller must ensure
    the player has sufficient balance and has already created a credit/spend event.

    Args:
            game_tag: Game type identifier
            request: Deposit request containing box_id, player_id, amount,
                    lobby_session_id

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
    return await service.deposit(
        game_tag, request, authenticated_box.id, authenticated_player, db_service
    )


@router.post("/{game_tag}/consume", status_code=200)
async def consume_machine_credits(
    game_tag: str,
    request: schemas.MachineCreditsConsumeRequest,
    authenticated_box: dependencies.BoxAuthenticated,
    db_service: dependencies.Database,
) -> schemas.MachineCreditsResponse:
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
    return await service.consume(game_tag, request, authenticated_box.id, db_service)
