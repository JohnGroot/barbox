"""
Test Constants for BarBox Integration Tests

This file defines seeded test data IDs that are pre-registered in the test database.
These IDs are used by both backend (Hurl) and frontend (C#/Godot) tests to ensure
consistent test data across the entire platform.

IMPORTANT: These IDs must be seeded in the test database before running tests.
Run `scripts/seed-test-data.sh` to populate the database with these test entities.
"""

from uuid import UUID

from bxctl.testing.seeding import TEST_BOX_ID as TEST_BOX_ID_1

# Test Box IDs (pre-registered physical terminals)
TEST_BOX_ID_2 = UUID("00000000-0000-0000-0000-000000000002")
TEST_BOX_ID_3 = UUID("00000000-0000-0000-0000-000000000003")

# Test Player IDs (pre-registered user accounts)
TEST_PLAYER_ID_1 = UUID("10000000-0000-0000-0000-000000000001")
TEST_PLAYER_ID_2 = UUID("10000000-0000-0000-0000-000000000002")
TEST_PLAYER_ID_3 = UUID("10000000-0000-0000-0000-000000000003")

# Test Location IDs
TEST_LOCATION_ID_1 = "test_location_1"
TEST_LOCATION_ID_2 = "test_location_2"

# Test Player Credentials (phone numbers and PINs)
TEST_PLAYER_1_PHONE = "5555550001"
TEST_PLAYER_1_PIN = "1111"
TEST_PLAYER_1_USERNAME = "test1"

TEST_PLAYER_2_PHONE = "5555550002"
TEST_PLAYER_2_PIN = "2222"
TEST_PLAYER_2_USERNAME = "test2"

TEST_PLAYER_3_PHONE = "5555550003"
TEST_PLAYER_3_PIN = "3333"
TEST_PLAYER_3_USERNAME = "test3"

# Test Box API Keys (generated during seeding)
# These will be populated by the seed script
TEST_BOX_1_API_KEY = "test_box_1_api_key"
TEST_BOX_2_API_KEY = "test_box_2_api_key"
TEST_BOX_3_API_KEY = "test_box_3_api_key"

# Game Tags for testing
TEST_GAME_TAG_RACING = "racing"
TEST_GAME_TAG_MINING = "mining"
TEST_GAME_TAG_CARROM = "carrom"


# Helper function to get test constants as environment variables
def get_env_vars() -> dict[str, str]:
    """
    Returns test constants as environment variable dictionary.
    Useful for setting up test environments.
    """
    return {
        "TEST_BOX_ID_1": str(TEST_BOX_ID_1),
        "TEST_BOX_ID_2": str(TEST_BOX_ID_2),
        "TEST_BOX_ID_3": str(TEST_BOX_ID_3),
        "TEST_PLAYER_ID_1": str(TEST_PLAYER_ID_1),
        "TEST_PLAYER_ID_2": str(TEST_PLAYER_ID_2),
        "TEST_PLAYER_ID_3": str(TEST_PLAYER_ID_3),
        "TEST_LOCATION_ID_1": TEST_LOCATION_ID_1,
        "TEST_LOCATION_ID_2": TEST_LOCATION_ID_2,
        "TEST_PLAYER_1_PHONE": TEST_PLAYER_1_PHONE,
        "TEST_PLAYER_1_PIN": TEST_PLAYER_1_PIN,
        "TEST_PLAYER_2_PHONE": TEST_PLAYER_2_PHONE,
        "TEST_PLAYER_2_PIN": TEST_PLAYER_2_PIN,
        "TEST_PLAYER_3_PHONE": TEST_PLAYER_3_PHONE,
        "TEST_PLAYER_3_PIN": TEST_PLAYER_3_PIN,
    }
