"""Test endpoints for development and testing environments.

These endpoints are ONLY available in dev/test modes and will return 404 in production.
"""

from uuid import UUID, uuid4

from fastapi import APIRouter, HTTPException, status
from sqlalchemy import text
from structlog import get_logger

from bxctl import db, env, structures

from . import auth, dependencies

router = APIRouter(prefix="/test", tags=["Testing"])
logger = get_logger()


@router.post("/reset", status_code=200)
async def reset_database(db_service: dependencies.Database) -> dict:
    """Reset the database by dropping and recreating all tables.

    WARNING: This will delete ALL data in the database.
    Only available in dev/test environments.

    Returns:
        200: Database reset successfully
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

        return {
            "status": "success",
            "message": "Database reset successfully",
            "environment": env.acquire().env,
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
    Only available in dev/test environments.

    Returns:
        200: Test data seeded successfully
        404: Endpoint not available in production
    """
    if env.is_production():
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Test endpoints are not available in production",
        )

    try:
        # Deterministic UUIDs for testing
        test_box_id = UUID("00000000-0000-0000-0000-000000000001")
        test_player1_id = UUID("11111111-1111-1111-1111-111111111111")
        test_player2_id = UUID("22222222-2222-2222-2222-222222222222")

        # Use fixed test API key for development consistency (matches .env.local)
        # This prevents API key mismatch when database is reset during testing
        TEST_API_KEY = "ndE63953HvBEqNP5XKPFe3vN4Ei9bDF-g9p13KoOmKs"
        api_key = TEST_API_KEY
        api_key_hash = auth.hash_api_key(api_key)
        api_key_hash_lookup = auth.hash_api_key_lookup(api_key)

        # Create test box with API key
        test_box_data = {
            "id": test_box_id,
            "name": "Test Box",
            "tag": "testbox",
            "api_key_hash": api_key_hash,
            "api_key_hash_lookup": api_key_hash_lookup,
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
                "box_api_key": api_key,
                "player_ids": [str(test_player1_id), str(test_player2_id)],
            },
        }

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


