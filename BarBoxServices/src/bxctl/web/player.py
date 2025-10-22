from uuid import UUID

from fastapi import APIRouter, HTTPException, status
from sqlalchemy import select
from sqlalchemy.exc import IntegrityError
from structlog import get_logger

from bxctl import db, structures

from . import dependencies

router = APIRouter(prefix="/player")
logger = get_logger()


@router.post("/", status_code=201)
async def register_player(
    new_player: structures.PlayerCreate,
    db_service: dependencies.Database,
) -> structures.PlayerDetail:
    """Register a new player account.

    Validates that:
    - Origin box exists
    - Player ID is unique
    - Username (tag) is unique

    Returns:
        201: Player created successfully
        400: Validation failed (origin box doesn't exist)
        409: Player already exists (duplicate ID or username)
        500: Internal server error
    """

    # Validate that origin box exists before attempting to create player
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
                "code": structures.ErrorCode.FK_VIOLATION,
                "message": f"Origin box '{new_player.origin_id}' does not exist. Please ensure the box is registered first.",
                "details": {
                    "field": "origin_id",
                    "value": str(new_player.origin_id),
                },
            },
        )

    # Check if player already exists (either by ID or username)
    existing_player_result = await db_service.session.execute(
        select(db.defs.Player).where(
            (db.defs.Player.id == new_player.id) | (db.defs.Player.tag == new_player.tag)
        )
    )
    existing_player = existing_player_result.scalar_one_or_none()

    if existing_player is not None:
        logger.info(
            "player_creation_duplicate",
            player_id=str(new_player.id),
            existing_id=str(existing_player.id),
            username=new_player.tag,
            existing_username=existing_player.tag,
        )
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail={
                "code": structures.ErrorCode.DUPLICATE_RESOURCE,
                "message": (
                    f"Player already exists with {'ID' if existing_player.id == new_player.id else 'username'} "
                    f"'{new_player.id if existing_player.id == new_player.id else new_player.tag}'."
                ),
                "details": {
                    "conflict_field": "id" if existing_player.id == new_player.id else "tag",
                    "conflict_value": str(new_player.id) if existing_player.id == new_player.id else new_player.tag,
                },
            },
        )

    # Attempt to create player
    try:
        result = await db_service.create(
            target=db.defs.Player,
            data=new_player,
            read_as=structures.PlayerDetail,
        )
        logger.info(
            "player_created",
            player_id=str(new_player.id),
            username=new_player.tag,
            origin_id=str(new_player.origin_id),
        )
        return result

    except IntegrityError as e:
        # Catch any database constraint violations that weren't caught above
        logger.error(
            "player_creation_integrity_error",
            error=str(e),
            player_id=str(new_player.id),
            username=new_player.tag,
        )
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail={
                "code": structures.ErrorCode.UNIQUE_CONSTRAINT,
                "message": "Player creation failed due to a constraint violation.",
                "details": {"error": str(e.orig) if hasattr(e, 'orig') else str(e)},
            },
        )

    except Exception as e:
        # Catch unexpected errors
        logger.error(
            "player_creation_failed",
            error=str(e),
            error_type=type(e).__name__,
            player_id=str(new_player.id),
            username=new_player.tag,
        )
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "code": structures.ErrorCode.INTERNAL_ERROR,
                "message": "An unexpected error occurred during player creation.",
                "details": {"error_type": type(e).__name__},
            },
        )


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

    # Check if origin box exists
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

    # Check if player ID already exists
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

    # Check if username already exists
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
