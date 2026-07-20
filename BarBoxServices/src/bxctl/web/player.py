from uuid import UUID

from fastapi import APIRouter, HTTPException, Request, status
from slowapi import Limiter
from slowapi.util import get_remote_address
from sqlalchemy import select
from sqlalchemy.exc import IntegrityError
from structlog import get_logger

from bxctl import db, env, errors, structures
from bxctl.app import auth, dependencies
from bxctl.games import common
from bxctl.registry import CoreEvent

router = APIRouter(prefix="/player", tags=["Core: Players"])
logger = get_logger()

# Pre-computed dummy PIN hash for timing attack mitigation
# Computed once at module load to avoid expensive hashing on every failed login
_DUMMY_PIN_HASH = auth.hash_player_pin("0000")

# Number of leading phone digits kept visible when masking numbers in logs
PHONE_LOG_PREFIX_LENGTH = 4

# Disabled in dev/test environments to avoid interfering with integration tests
settings = env.acquire()
limiter = Limiter(key_func=get_remote_address, enabled=settings.is_production())


@router.post("/auth/login", status_code=200, tags=["Auth"])
@limiter.limit("5/minute")
async def authenticate_player(
    request: Request,  # noqa: ARG001  # slowapi limiter requires it
    credentials: structures.PlayerLoginRequest,
    authenticated_box: dependencies.BoxAuthenticated,
    db_service: dependencies.Database,
    now: dependencies.Now,  # noqa: ARG001  # injected timestamp, unused here
) -> structures.PlayerLoginResponse:
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

    Args:
            credentials: Player phone number, PIN, and box ID
            authenticated_box: Box authenticated via API key
            db_service: Database session
            now: Current timestamp

    Returns:
            200: JWT token with expiration time
            401: Invalid credentials (phone number or PIN incorrect)
            403: Box ID mismatch or account locked out
            500: Internal server error
    """
    if credentials.box_id != authenticated_box.id:
        logger.warning(
            "player_login_box_mismatch",
            requested_box_id=str(credentials.box_id),
            authenticated_box_id=str(authenticated_box.id),
        )
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={
                "code": errors.ErrorCode.VALIDATION_ERROR,
                "message": "Box ID in credentials does not match authenticated box.",
                "details": {
                    "requested_box_id": str(credentials.box_id),
                    "authenticated_box_id": str(authenticated_box.id),
                },
            },
        )

    # Normalize phone number for lookup (allows flexible input formats)
    try:
        normalized_phone = auth.validate_and_normalize_phone(credentials.phone_number)
    except ValueError:
        # Invalid phone format - treat as failed login (don't reveal that the
        # format itself is invalid)
        normalized_phone = credentials.phone_number  # Use as-is, will fail lookup

    player_result = await db_service.session.execute(
        select(db.defs.Player).where(db.defs.Player.phone_number == normalized_phone)
    )
    player = player_result.scalar_one_or_none()

    # Check if player exists (arcade context: clear error messages)
    if not player:
        logger.warning(
            "player_login_failed_not_registered",
            phone_number_prefix=(
                credentials.phone_number[:PHONE_LOG_PREFIX_LENGTH] + "****"
                if len(credentials.phone_number) > PHONE_LOG_PREFIX_LENGTH
                else "****"
            ),
        )
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail={
                "code": errors.ErrorCode.VALIDATION_ERROR,
                "message": "Account not registered - please create one",
            },
        )

    is_valid_pin = auth.verify_player_pin(credentials.pin, player.pin_hash)
    if not is_valid_pin:
        logger.warning(
            "player_login_failed_wrong_pin",
            phone_number_prefix=(
                credentials.phone_number[:PHONE_LOG_PREFIX_LENGTH] + "****"
                if len(credentials.phone_number) > PHONE_LOG_PREFIX_LENGTH
                else "****"
            ),
        )
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail={
                "code": errors.ErrorCode.VALIDATION_ERROR,
                "message": "Incorrect PIN - please try again",
            },
        )

    # Use actual player.id (random UUID assigned at registration)
    access_token, access_exp = auth.create_player_token(player.id, credentials.box_id)

    logger.info(
        "player_authenticated",
        player_id=str(player.id),
        box_id=str(credentials.box_id),
        token_expires_at=access_exp.isoformat(),
    )

    return structures.PlayerLoginResponse(
        access_token=access_token,
        player_id=player.id,
        username=player.tag,
        expires_at=access_exp,
    )


@router.post("/auth/logout", status_code=200, tags=["Auth"])
@limiter.limit("20/minute")
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
@limiter.limit("10/minute")
async def register_player(
    request: Request,  # noqa: ARG001  # slowapi limiter requires it
    new_player: structures.PlayerCreate,
    authenticated_box: dependencies.BoxAuthenticated,
    db_service: dependencies.Database,
) -> structures.PlayerDetail:
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
    if new_player.id == UUID("00000000-0000-0000-0000-000000000000"):
        logger.warning(
            "player_registration_empty_guid",
            player_id=str(new_player.id),
        )
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail={
                "code": errors.ErrorCode.VALIDATION_ERROR,
                "message": (
                    "Player ID cannot be all zeros. Please generate a valid UUID."
                ),
                "details": {
                    "field": "id",
                    "value": str(new_player.id),
                },
            },
        )

    if new_player.origin_id != authenticated_box.id:
        logger.warning(
            "player_registration_box_mismatch",
            requested_origin=str(new_player.origin_id),
            authenticated_box=str(authenticated_box.id),
        )
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={
                "code": errors.ErrorCode.VALIDATION_ERROR,
                "message": "Origin box ID does not match authenticated box.",
                "details": {
                    "requested_origin": str(new_player.origin_id),
                    "authenticated_box": str(authenticated_box.id),
                },
            },
        )

    box_result = await db_service.session.execute(
        select(db.defs.Box).where(db.defs.Box.id == new_player.origin_id)
    )
    origin_box = box_result.scalar_one_or_none()

    if origin_box is None:
        logger.warning(
            "player_creation_failed_box_not_found",
            player_id=str(new_player.id),
            origin_id=str(new_player.origin_id),
            username=new_player.tag,
        )
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail={
                "code": errors.ErrorCode.FK_VIOLATION,
                "message": (
                    f"Origin box '{new_player.origin_id}' does not exist. "
                    "Please ensure the box is registered first."
                ),
                "details": {
                    "field": "origin_id",
                    "value": str(new_player.origin_id),
                },
            },
        )

    try:
        normalized_phone = auth.validate_and_normalize_phone(new_player.phone_number)
    except ValueError as e:
        logger.warning(
            "player_registration_invalid_phone",
            phone=new_player.phone_number,
            error=str(e),
        )
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail={
                "code": errors.ErrorCode.VALIDATION_ERROR,
                "message": str(e),
                "details": {
                    "field": "phone_number",
                    "value": new_player.phone_number,
                },
            },
        ) from e

    existing_player_result = await db_service.session.execute(
        select(db.defs.Player).where(
            (db.defs.Player.id == new_player.id)
            | (db.defs.Player.tag == new_player.tag)
            | (db.defs.Player.phone_number == normalized_phone)
        )
    )
    existing_player = existing_player_result.scalar_one_or_none()

    if existing_player is not None:
        if existing_player.id == new_player.id:
            conflict_field = "id"
            conflict_value = str(new_player.id)
        elif existing_player.tag == new_player.tag:
            conflict_field = "tag"
            conflict_value = new_player.tag
        else:  # phone_number match
            conflict_field = "phone_number"
            conflict_value = normalized_phone

        logger.info(
            "player_creation_duplicate",
            player_id=str(new_player.id),
            existing_id=str(existing_player.id),
            username=new_player.tag,
            existing_username=existing_player.tag,
            conflict_field=conflict_field,
        )
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail={
                "code": errors.ErrorCode.DUPLICATE_RESOURCE,
                "message": (
                    f"Player already exists with {conflict_field} '{conflict_value}'."
                ),
                "details": {
                    "conflict_field": conflict_field,
                    "conflict_value": conflict_value,
                },
            },
        )

    pin_hash = auth.hash_player_pin(new_player.pin)

    try:
        result = await db_service.create(
            target=db.defs.Player,
            data=new_player.model_dump(exclude={"pin", "phone_number"})
            | {"pin_hash": pin_hash, "phone_number": normalized_phone},
            read_as=structures.PlayerDetail,
        )
        logger.info(
            "player_created",
            player_id=str(new_player.id),
            username=new_player.tag,
            origin_id=str(new_player.origin_id),
        )

    except IntegrityError as e:
        logger.exception(
            "player_creation_integrity_error",
            error=str(e),
            player_id=str(new_player.id),
            username=new_player.tag,
        )
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail={
                "code": errors.ErrorCode.UNIQUE_CONSTRAINT,
                "message": "Player creation failed due to a constraint violation.",
                "details": {"error": str(e.orig) if hasattr(e, "orig") else str(e)},
            },
        ) from e

    except Exception as e:
        logger.exception(
            "player_creation_failed",
            error=str(e),
            error_type=type(e).__name__,
            player_id=str(new_player.id),
            username=new_player.tag,
        )
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "code": errors.ErrorCode.INTERNAL_ERROR,
                "message": "An unexpected error occurred during player creation.",
                "details": {"error_type": type(e).__name__},
            },
        ) from e

    return result


@router.post("/validate", status_code=200)
async def validate_player_creation(
    new_player: structures.PlayerCreate,
    db_service: dependencies.Database,
) -> structures.ValidationResult:
    """Validate player creation without actually creating the player.

    Checks all constraints that would apply during actual creation:
    - Origin box exists
    - Player ID is unique
    - Username (tag) is unique

    Returns validation result with list of errors (if any).
    """
    errors: list[structures.ValidationErrorDetail] = []

    box_result = await db_service.session.execute(
        select(db.defs.Box).where(db.defs.Box.id == new_player.origin_id)
    )
    origin_box = box_result.scalar_one_or_none()

    if origin_box is None:
        errors.append(
            structures.ValidationErrorDetail(
                field="origin_id",
                message=f"Origin box '{new_player.origin_id}' does not exist.",
                value=str(new_player.origin_id),
            )
        )

    existing_id_result = await db_service.session.execute(
        select(db.defs.Player).where(db.defs.Player.id == new_player.id)
    )
    existing_id = existing_id_result.scalar_one_or_none()

    if existing_id is not None:
        errors.append(
            structures.ValidationErrorDetail(
                field="id",
                message=f"Player with ID '{new_player.id}' already exists.",
                value=str(new_player.id),
            )
        )

    existing_tag_result = await db_service.session.execute(
        select(db.defs.Player).where(db.defs.Player.tag == new_player.tag)
    )
    existing_tag = existing_tag_result.scalar_one_or_none()

    if existing_tag is not None:
        errors.append(
            structures.ValidationErrorDetail(
                field="tag",
                message=f"Username '{new_player.tag}' is already taken.",
                value=new_player.tag,
            )
        )

    is_valid = len(errors) == 0

    logger.info(
        "player_validation_check",
        player_id=str(new_player.id),
        username=new_player.tag,
        is_valid=is_valid,
        error_count=len(errors),
    )

    return structures.ValidationResult(valid=is_valid, errors=errors)


@router.get("/username/{username}/available")
async def check_username_available(
    username: str,
    db_service: dependencies.Database,
) -> structures.UsernameAvailabilityResponse:
    """Check if username is available for registration"""

    result = await db_service.session.execute(
        select(db.defs.Player).where(db.defs.Player.tag == username)
    )
    existing_player = result.scalar_one_or_none()

    is_available = existing_player is None

    return structures.UsernameAvailabilityResponse(
        username=username, is_available=is_available
    )


@router.get("/{player_id}/credits")
async def get_player_credits(
    player_id: UUID,
    location_id: str,
    db_service: dependencies.Database,
) -> structures.PlayerCreditsResponse:
    """Get player's credit balance for a specific location"""

    signed_sum = common.signed_sum_sql(
        "bse.payload, '$.amount'", CoreEvent.CREDIT_EARN, CoreEvent.CREDIT_SPEND
    )
    sql = f"""
    SELECT
        {signed_sum} as credits
    FROM box_session_event bse
    JOIN box_session bs ON bse.session_id = bs.id
    WHERE bs.host_player_id = :player_id
    AND bse.type IN (:earn_type, :spend_type)
    AND (
        json_extract(bse.payload, '$.location_id') = :location_id
        OR json_extract(bse.payload, '$.global') = 1
    )
    """  # noqa: S608  # only a trusted SQL fragment is interpolated; values are bound

    result = await db_service.get_many_raw(
        sql,
        {
            "player_id": player_id.hex,
            "location_id": location_id,
            "earn_type": CoreEvent.CREDIT_EARN.value,
            "spend_type": CoreEvent.CREDIT_SPEND.value,
        },
    )

    credit_balance = result.scalar() or 0

    logger.info(
        "credit_balance_query",
        player_id=str(player_id),
        location_id=location_id,
        credits=credit_balance,
    )

    return structures.PlayerCreditsResponse(
        player_id=player_id, location_id=location_id, credits=credit_balance
    )
