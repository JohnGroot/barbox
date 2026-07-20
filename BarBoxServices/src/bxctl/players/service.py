from uuid import UUID

from fastapi import HTTPException, status
from sqlalchemy import select
from structlog import get_logger

from bxctl import db
from bxctl import errors as bxctl_errors
from bxctl.app import auth
from bxctl.db.service import CRUD
from bxctl.games import common
from bxctl.players import schemas
from bxctl.registry import CoreEvent

logger = get_logger()

# Number of leading phone digits kept visible when masking numbers in logs
PHONE_LOG_PREFIX_LENGTH = 4


async def authenticate_player(
    credentials: schemas.PlayerLoginRequest,
    authenticated_box_id: UUID,
    db_service: CRUD,
) -> schemas.PlayerLoginResponse:
    if credentials.box_id != authenticated_box_id:
        logger.warning(
            "player_login_box_mismatch",
            requested_box_id=str(credentials.box_id),
            authenticated_box_id=str(authenticated_box_id),
        )
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={
                "code": bxctl_errors.ErrorCode.VALIDATION_ERROR,
                "message": "Box ID in credentials does not match authenticated box.",
                "details": {
                    "requested_box_id": str(credentials.box_id),
                    "authenticated_box_id": str(authenticated_box_id),
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
                "code": bxctl_errors.ErrorCode.VALIDATION_ERROR,
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
                "code": bxctl_errors.ErrorCode.VALIDATION_ERROR,
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

    return schemas.PlayerLoginResponse(
        access_token=access_token,
        player_id=player.id,
        username=player.tag,
        expires_at=access_exp,
    )


async def register_player(
    new_player: schemas.PlayerCreate,
    authenticated_box_id: UUID,
    db_service: CRUD,
) -> schemas.PlayerDetail:
    if new_player.id == common.ZERO_UUID:
        logger.warning(
            "player_registration_empty_guid",
            player_id=str(new_player.id),
        )
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail={
                "code": bxctl_errors.ErrorCode.VALIDATION_ERROR,
                "message": (
                    "Player ID cannot be all zeros. Please generate a valid UUID."
                ),
                "details": {
                    "field": "id",
                    "value": str(new_player.id),
                },
            },
        )

    if new_player.origin_id != authenticated_box_id:
        logger.warning(
            "player_registration_box_mismatch",
            requested_origin=str(new_player.origin_id),
            authenticated_box=str(authenticated_box_id),
        )
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail={
                "code": bxctl_errors.ErrorCode.VALIDATION_ERROR,
                "message": "Origin box ID does not match authenticated box.",
                "details": {
                    "requested_origin": str(new_player.origin_id),
                    "authenticated_box": str(authenticated_box_id),
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
                "code": bxctl_errors.ErrorCode.FK_VIOLATION,
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
                "code": bxctl_errors.ErrorCode.VALIDATION_ERROR,
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
                "code": bxctl_errors.ErrorCode.DUPLICATE_RESOURCE,
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

    async with bxctl_errors.creation_error_boundary(
        log_event_stem="player_creation",
        conflict_message="Player creation failed due to a constraint violation.",
        failure_message="An unexpected error occurred during player creation.",
        player_id=str(new_player.id),
        username=new_player.tag,
    ):
        result = await db_service.create(
            target=db.defs.Player,
            data=new_player.model_dump(exclude={"pin", "phone_number"})
            | {"pin_hash": pin_hash, "phone_number": normalized_phone},
            read_as=schemas.PlayerDetail,
        )
        logger.info(
            "player_created",
            player_id=str(new_player.id),
            username=new_player.tag,
            origin_id=str(new_player.origin_id),
        )

    return result


async def validate_player_creation(
    new_player: schemas.PlayerCreate,
    db_service: CRUD,
) -> schemas.ValidationResult:
    errors: list[schemas.ValidationErrorDetail] = []

    box_result = await db_service.session.execute(
        select(db.defs.Box).where(db.defs.Box.id == new_player.origin_id)
    )
    origin_box = box_result.scalar_one_or_none()

    if origin_box is None:
        errors.append(
            schemas.ValidationErrorDetail(
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
            schemas.ValidationErrorDetail(
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
            schemas.ValidationErrorDetail(
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

    return schemas.ValidationResult(valid=is_valid, errors=errors)


async def check_username_available(
    username: str,
    db_service: CRUD,
) -> schemas.UsernameAvailabilityResponse:
    result = await db_service.session.execute(
        select(db.defs.Player).where(db.defs.Player.tag == username)
    )
    existing_player = result.scalar_one_or_none()

    is_available = existing_player is None

    return schemas.UsernameAvailabilityResponse(
        username=username, is_available=is_available
    )


async def get_player_credits(
    player_id: UUID,
    location_id: str,
    db_service: CRUD,
) -> schemas.PlayerCreditsResponse:
    signed_sum = common.signed_sum_sql("bse.payload, '$.amount'")
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

    return schemas.PlayerCreditsResponse(
        player_id=player_id, location_id=location_id, credits=credit_balance
    )
