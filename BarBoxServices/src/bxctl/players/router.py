from uuid import UUID

from fastapi import APIRouter, Request
from slowapi import Limiter
from slowapi.util import get_remote_address
from structlog import get_logger

from bxctl import env
from bxctl.app import dependencies
from bxctl.players import schemas, service

router = APIRouter(prefix="/player", tags=["Core: Players"])
logger = get_logger()

# Disabled in dev/test environments to avoid interfering with integration tests
settings = env.acquire()
limiter = Limiter(key_func=get_remote_address, enabled=settings.is_production())

LOGIN_RATE_LIMIT = "5/minute"
LOGOUT_RATE_LIMIT = "20/minute"
REGISTRATION_RATE_LIMIT = "10/minute"


@router.post("/auth/login", status_code=200, tags=["Auth"])
@limiter.limit(LOGIN_RATE_LIMIT)
async def authenticate_player(
    request: Request,  # noqa: ARG001  # slowapi limiter requires it
    credentials: schemas.PlayerLoginRequest,
    authenticated_box: dependencies.BoxAuthenticated,
    db_service: dependencies.Database,
    now: dependencies.Now,  # noqa: ARG001  # injected timestamp, unused here
) -> schemas.PlayerLoginResponse:
    """Authenticate player and issue JWT token.

    Validates player credentials (phone number + PIN) and generates a JWT token
    for authenticated API access. Uses phone number lookup to retrieve player ID
    (secure, non-deterministic UUIDs).

    Box authentication is required via API key to prevent unauthorized login attempts.

    Security features:
    - Rate limiting: Max 5 attempts per IP per minute
    - Account lockout: Max 5 failed attempts per phone in 30min window
    - Timing-safe validation: Prevents account enumeration
    - Generic error messages: Doesn't reveal which part failed

    Returns:
            200: JWT token with expiration time
            401: Invalid credentials (phone number or PIN incorrect)
            403: Box ID mismatch or account locked out
            500: Internal server error
    """
    return await service.authenticate_player(
        credentials, authenticated_box.id, db_service
    )


@router.post("/auth/logout", status_code=200, tags=["Auth"])
@limiter.limit(LOGOUT_RATE_LIMIT)
async def logout_player(
    request: Request,  # noqa: ARG001  # slowapi limiter requires it
    player_id: dependencies.AuthenticatedPlayer,
) -> dict:
    """Logout player (client-side).

    Requires valid JWT token in Authorization header.
    Client should forget the token locally. Token will naturally expire after 2 hours.

    Arcade-optimized approach:
    - No server-side token revocation (unnecessary complexity)
    - 2-hour token expiry + 10-minute idle timeout provides adequate security
    - Simpler deployment (no revocation database table, no cleanup jobs)

    Returns:
            200: Successfully logged out
            401: Invalid or missing token
    """
    logger.info("player_logged_out", player_id=str(player_id))

    return {
        "message": "Successfully logged out. Token will expire in 2 hours.",
        "player_id": str(player_id),
    }


@router.post("/", status_code=201)
@limiter.limit(REGISTRATION_RATE_LIMIT)
async def register_player(
    request: Request,  # noqa: ARG001  # slowapi limiter requires it
    new_player: schemas.PlayerCreate,
    authenticated_box: dependencies.BoxAuthenticated,
    db_service: dependencies.Database,
) -> schemas.PlayerDetail:
    """Register a new player account.

    **Authentication**: Requires Box API key.

    Validates that:
    - Origin box exists
    - Player ID is unique
    - Username (tag) is unique
    - Origin box matches authenticated box

    Returns:
        201: Player created successfully
        400: Validation failed (origin box doesn't exist)
        403: Origin box doesn't match authenticated box
        409: Player already exists (duplicate ID or username)
        500: Internal server error

    Headers:
        X-Box-API-Key: Box API key for authentication
    """
    return await service.register_player(new_player, authenticated_box.id, db_service)


@router.post("/validate", status_code=200)
async def validate_player_creation(
    new_player: schemas.PlayerCreate,
    db_service: dependencies.Database,
) -> schemas.ValidationResult:
    """Validate player creation without actually creating the player.

    Checks all constraints that would apply during actual creation:
    - Origin box exists
    - Player ID is unique
    - Username (tag) is unique

    Returns validation result with list of errors (if any).
    """
    return await service.validate_player_creation(new_player, db_service)


@router.get("/username/{username}/available")
async def check_username_available(
    username: str,
    db_service: dependencies.Database,
) -> schemas.UsernameAvailabilityResponse:
    """Check if username is available for registration"""
    return await service.check_username_available(username, db_service)


@router.get("/{player_id}/credits")
async def get_player_credits(
    player_id: UUID,
    location_id: str,
    db_service: dependencies.Database,
) -> schemas.PlayerCreditsResponse:
    """Get player's credit balance for a specific location"""
    return await service.get_player_credits(player_id, location_id, db_service)
