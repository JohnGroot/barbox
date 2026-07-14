"""Test endpoints for development and testing environments.

These endpoints are ONLY available in dev/test modes and will return 404 in production.
"""

from datetime import datetime
from uuid import UUID, uuid4

from fastapi import APIRouter, HTTPException, status
from pydantic import BaseModel, Field
from sqlalchemy import select, text
from structlog import get_logger

from bxctl import db, env, structures

from . import auth, dependencies
from .payments import service as payments_service

router = APIRouter(prefix="/test", tags=["Testing"])
logger = get_logger()


class MockWebhookRequest(BaseModel):
    """Test-only webhook request with primitive fields (no Stripe structures).

    WARNING: This model is used by the /test/payments/webhook-mock endpoint
    which issues real credits. Only available in dev/test environments.

    Uses primitive types to avoid coupling test code to Stripe SDK structures.
    """

    event_id: str = Field(description="Test event ID (must start with 'evt_test_')")
    session_id: str = Field(description="Checkout session ID")
    payment_intent_id: str = Field(description="Payment intent ID")
    player_id: UUID = Field(description="Player receiving credits")
    box_id: UUID = Field(description="Box where purchase was made")
    credits: int = Field(gt=0, description="Base credits to issue (must be positive)")
    bonus_credits: int = Field(
        ge=0, default=0, description="Bonus credits (non-negative)"
    )
    amount_cents: int = Field(gt=0, description="Payment amount in cents")
    pack_id: str = Field(description="Credit pack identifier (pack_5, pack_10, etc.)")


async def _seed_test_box_and_players(
    db_service: db.service.CRUD,
    now: datetime,
) -> dict:
    """Shared function to seed test box and players.

    Idempotent: Skips creation if test box already exists.
    Used by both /test/seed endpoint and auto-seeding in main.py.

    Returns:
        dict with seeding results
    """
    # Deterministic UUIDs for testing
    test_box_id = UUID("00000000-0000-0000-0000-000000000001")
    test_player1_id = UUID("11111111-1111-1111-1111-111111111111")
    test_player2_id = UUID("22222222-2222-2222-2222-222222222222")

    # API key is now derived deterministically from box_id
    derived_api_key = auth.derive_box_api_key(test_box_id)

    # Check if test box already exists (idempotent)
    result = await db_service.session.execute(
        select(db.defs.Box).where(db.defs.Box.id == test_box_id)
    )
    existing_box = result.scalar_one_or_none()

    if existing_box:
        logger.info(
            "test_box_already_exists",
            box_id=str(test_box_id),
            message="Skipping test data seeding - already exists",
        )
        return {
            "status": "skipped",
            "message": "Test data already exists",
            "data": {
                "box_id": str(test_box_id),
                "box_api_key": derived_api_key,
                "player_ids": [str(test_player1_id), str(test_player2_id)],
            },
        }

    # Create test box (no API key hash storage - key is derived on demand)
    test_box_data = {
        "id": test_box_id,
        "name": "Test Box",
        "tag": "testbox",
        "created_at": now,
        "last_seen": None,
    }
    await db_service.create(target=db.defs.Box, data=test_box_data)
    logger.info("test_box_created", box_id=str(test_box_id))

    # Create test players with hashed PINs and normalized phones
    test_player1_phone = "+12125551111"
    test_player1_pin_hash = auth.hash_player_pin("1111")
    normalized_phone1 = auth.validate_and_normalize_phone(test_player1_phone)

    await db_service.create(
        target=db.defs.Player,
        data={
            "id": test_player1_id,
            "tag": "testuser1",
            "phone_number": normalized_phone1,
            "pin_hash": test_player1_pin_hash,
            "origin_id": test_box_id,
        },
    )
    logger.info("test_player_created", player_id=str(test_player1_id))

    test_player2_phone = "+12125552222"
    test_player2_pin_hash = auth.hash_player_pin("2222")
    normalized_phone2 = auth.validate_and_normalize_phone(test_player2_phone)

    await db_service.create(
        target=db.defs.Player,
        data={
            "id": test_player2_id,
            "tag": "testuser2",
            "phone_number": normalized_phone2,
            "pin_hash": test_player2_pin_hash,
            "origin_id": test_box_id,
        },
    )
    logger.info("test_player_created", player_id=str(test_player2_id))

    return {
        "status": "success",
        "message": "Test data seeded successfully",
        "data": {
            "box_id": str(test_box_id),
            "box_api_key": derived_api_key,
            "player_ids": [str(test_player1_id), str(test_player2_id)],
        },
    }


@router.post("/reset", status_code=200)
async def reset_database(
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> dict:
    """Reset the database by dropping and recreating all tables.

    WARNING: This will delete ALL data in the database.
    In dev mode, automatically re-seeds test data after reset.
    Only available in dev/test environments.

    Returns:
        200: Database reset successfully (with auto-seeding in dev mode)
        404: Endpoint not available in production
    """
    if env.is_production():
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Test endpoints are not available in production",
        )

    try:
        # Drop all tables
        async with db.connectivity.engine.begin() as conn:
            await conn.run_sync(db.defs.Base.metadata.drop_all)
            logger.info("database_tables_dropped")

        # Recreate all tables
        async with db.connectivity.engine.begin() as conn:
            await conn.run_sync(db.defs.Base.metadata.create_all)
            logger.info("database_tables_created")

        # Auto-seed in dev mode for convenience
        settings = env.acquire()
        seed_result = None
        if settings.is_dev_mode():
            try:
                seed_result = await _seed_test_box_and_players(db_service, now)
                logger.info(
                    "auto_seed_after_reset_completed", result=seed_result["status"]
                )
            except Exception as e:
                logger.warning(
                    "auto_seed_after_reset_failed",
                    error=str(e),
                    message="Database reset succeeded but auto-seeding failed. Call POST /test/seed manually.",
                )

        return {
            "status": "success",
            "message": "Database reset successfully"
            + (" (auto-seeded)" if seed_result else ""),
            "environment": settings.env,
            "auto_seeded": seed_result is not None,
        }

    except Exception as e:
        logger.error("database_reset_failed", error=str(e))
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={"code": "OPERATION_FAILED", "message": str(e)},
        )


@router.post("/seed", status_code=200)
async def seed_test_data(
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> dict:
    """Seed the database with test data.

    Creates sample boxes, players, and sessions for testing.
    Idempotent: Skips creation if test box already exists.
    Only available in dev/test environments.

    Returns:
        200: Test data seeded successfully (or skipped if already exists)
        404: Endpoint not available in production
    """
    if env.is_production():
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Test endpoints are not available in production",
        )

    try:
        return await _seed_test_box_and_players(db_service, now)

    except Exception as e:
        logger.error("test_seed_failed", error=str(e))
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={"code": "OPERATION_FAILED", "message": str(e)},
        )


@router.get("/environment", status_code=200)
async def get_environment_info() -> dict:
    """Get current environment information.

    Returns environment mode, database path, and other configuration.
    Available in all environments.
    """
    settings = env.acquire()

    return {
        "environment": settings.env,
        "database_path": settings.sqlite_path,
        "is_production": settings.is_production(),
        "is_test_mode": settings.is_test_mode(),
        "is_dev_mode": settings.is_dev_mode(),
    }


@router.post("/payments/webhook-mock", status_code=200)
async def mock_webhook(
    request: MockWebhookRequest,
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> dict:
    """Test-only webhook that bypasses Stripe signature verification.

    WARNING: This endpoint issues real credits. Only available in dev/test.
    All invocations are audit logged.

    Use this endpoint to test the credit issuance flow without:
    - Real Stripe webhook events
    - Stripe signature verification
    - Stripe API calls

    Event IDs must start with 'evt_test_' prefix for safety.

    Returns:
        200: Credits issued successfully
        400: Invalid request (bad event_id prefix, validation error)
        404: Endpoint not available in production
    """
    # CRITICAL: Production guard (first line)
    if env.is_production():
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Test endpoints are not available in production",
        )

    # CRITICAL: Event ID validation - must use test prefix
    if not request.event_id.startswith("evt_test_"):
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail={
                "code": "VALIDATION_ERROR",
                "message": "Test events must use 'evt_test_' prefix",
                "field": "event_id",
            },
        )

    # CRITICAL: Audit logging for all test credit grants
    logger.warning(
        "test_webhook_invoked",
        event_id=request.event_id,
        session_id=request.session_id,
        player_id=str(request.player_id),
        box_id=str(request.box_id),
        credits=request.credits,
        bonus_credits=request.bonus_credits,
        pack_id=request.pack_id,
    )

    # Check for idempotency by session_id (simpler than real webhook's event_id check)
    existing_payment = await db_service.session.execute(
        select(db.defs.StripePaymentIntent).where(
            db.defs.StripePaymentIntent.stripe_session_id == request.session_id
        )
    )
    if existing_payment.scalar_one_or_none():
        logger.info("test_webhook_already_processed", session_id=request.session_id)
        return {"status": "already_processed", "session_id": request.session_id}

    # Call pure business logic (same function used by real webhook)
    try:
        result = await payments_service.issue_credits_for_payment(
            event_id=request.event_id,
            session_id=request.session_id,
            payment_intent_id=request.payment_intent_id,
            player_id=request.player_id,
            box_id=request.box_id,
            credits=request.credits,
            bonus_credits=request.bonus_credits,
            amount_cents=request.amount_cents,
            pack_id=request.pack_id,
            price_id=None,  # No Stripe Price ID for test
            payment_method_type="test",
            db_service=db_service,
            now=now,
            webhook_event_id=None,  # No StripeWebhookEvent for test
        )
        return result
    except Exception as e:
        logger.error(
            "test_webhook_failed",
            event_id=request.event_id,
            error=str(e),
            error_type=type(e).__name__,
        )
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "code": "PROCESSING_FAILED",
                "message": str(e),
            },
        ) from e
