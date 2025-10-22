"""Test endpoints for development and testing environments.

These endpoints are ONLY available in dev/test modes and will return 404 in production.
"""

from uuid import UUID, uuid4

from fastapi import APIRouter, HTTPException, status
from structlog import get_logger

from bxctl import db, env, structures

from . import dependencies

router = APIRouter(prefix="/test")
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
async def seed_test_data(db_service: dependencies.Database) -> dict:
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

        # Create test box
        test_box = structures.BoxCreate(
            id=test_box_id,
            name="Test Box",
            tag="testbox",
        )
        await db_service.create(target=db.defs.Box, data=test_box)
        logger.info("test_box_created", box_id=str(test_box_id))

        # Create test players
        test_player1 = structures.PlayerCreate(
            id=test_player1_id,
            tag="testuser1",
            origin_id=test_box_id,
        )
        await db_service.create(target=db.defs.Player, data=test_player1)
        logger.info("test_player_created", player_id=str(test_player1_id))

        test_player2 = structures.PlayerCreate(
            id=test_player2_id,
            tag="testuser2",
            origin_id=test_box_id,
        )
        await db_service.create(target=db.defs.Player, data=test_player2)
        logger.info("test_player_created", player_id=str(test_player2_id))

        return {
            "status": "success",
            "message": "Test data seeded successfully",
            "data": {
                "box_id": str(test_box_id),
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
