"""Deterministic test data seeding, shared by /test endpoints and dev boot.

These IDs/credentials are mirrored by test/fixtures/test_constants.py and
scripts/seed-test-data.sh for the Hurl suite - keep them in sync.
"""

from datetime import datetime
from typing import Final
from uuid import UUID

from sqlalchemy import select
from structlog import get_logger

from bxctl import db
from bxctl.app import auth

logger = get_logger()

# Deterministic UUIDs for testing
TEST_BOX_ID: Final = UUID("00000000-0000-0000-0000-000000000001")
TEST_PLAYER1_ID: Final = UUID("11111111-1111-1111-1111-111111111111")
TEST_PLAYER2_ID: Final = UUID("22222222-2222-2222-2222-222222222222")

TEST_PLAYER1_PHONE: Final = "+12125551111"
TEST_PLAYER1_PIN: Final = "1111"
TEST_PLAYER2_PHONE: Final = "+12125552222"
TEST_PLAYER2_PIN: Final = "2222"


async def seed_test_box_and_players(
    db_service: db.service.CRUD,
    now: datetime,
) -> dict:
    """Seed the test box and players.

    Idempotent: Skips creation if test box already exists.
    Used by the /test endpoints and auto-seeding in app/main.py.

    Returns:
        dict with seeding results
    """
    derived_api_key = auth.derive_box_api_key(TEST_BOX_ID)

    # Check if test box already exists (idempotent)
    result = await db_service.session.execute(
        select(db.defs.Box).where(db.defs.Box.id == TEST_BOX_ID)
    )
    existing_box = result.scalar_one_or_none()

    if existing_box:
        logger.info(
            "test_box_already_exists",
            box_id=str(TEST_BOX_ID),
            message="Skipping test data seeding - already exists",
        )
        return {
            "status": "skipped",
            "message": "Test data already exists",
            "data": {
                "box_id": str(TEST_BOX_ID),
                "box_api_key": derived_api_key,
                "player_ids": [str(TEST_PLAYER1_ID), str(TEST_PLAYER2_ID)],
            },
        }

    # Create test box (no API key hash storage - key is derived on demand)
    test_box_data = {
        "id": TEST_BOX_ID,
        "name": "Test Box",
        "tag": "testbox",
        "created_at": now,
        "last_seen": None,
    }
    await db_service.create(target=db.defs.Box, data=test_box_data)
    logger.info("test_box_created", box_id=str(TEST_BOX_ID))

    # Create test players with hashed PINs and normalized phones
    test_player1_pin_hash = auth.hash_player_pin(TEST_PLAYER1_PIN)
    normalized_phone1 = auth.validate_and_normalize_phone(TEST_PLAYER1_PHONE)

    await db_service.create(
        target=db.defs.Player,
        data={
            "id": TEST_PLAYER1_ID,
            "tag": "testuser1",
            "phone_number": normalized_phone1,
            "pin_hash": test_player1_pin_hash,
            "origin_id": TEST_BOX_ID,
        },
    )
    logger.info("test_player_created", player_id=str(TEST_PLAYER1_ID))

    test_player2_pin_hash = auth.hash_player_pin(TEST_PLAYER2_PIN)
    normalized_phone2 = auth.validate_and_normalize_phone(TEST_PLAYER2_PHONE)

    await db_service.create(
        target=db.defs.Player,
        data={
            "id": TEST_PLAYER2_ID,
            "tag": "testuser2",
            "phone_number": normalized_phone2,
            "pin_hash": test_player2_pin_hash,
            "origin_id": TEST_BOX_ID,
        },
    )
    logger.info("test_player_created", player_id=str(TEST_PLAYER2_ID))

    return {
        "status": "success",
        "message": "Test data seeded successfully",
        "data": {
            "box_id": str(TEST_BOX_ID),
            "box_api_key": derived_api_key,
            "player_ids": [str(TEST_PLAYER1_ID), str(TEST_PLAYER2_ID)],
        },
    }
