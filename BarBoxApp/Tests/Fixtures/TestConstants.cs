using System;

/// <summary>
/// Test Constants for BarBox Integration Tests
///
/// This file defines seeded test data IDs that are pre-registered in the test database.
/// These IDs are used by both backend (Python/Hurl) and frontend (C#/Godot) tests to ensure
/// consistent test data across the entire platform.
///
/// IMPORTANT: These IDs must be seeded in the test database before running tests.
/// The backend test runner automatically seeds this data via scripts/seed-test-data.sh
/// </summary>
public static class TestConstants
{
	// Test Box IDs (pre-registered physical terminals)
	public static readonly Guid TEST_BOX_ID_1 = new Guid("00000000-0000-0000-0000-000000000001");
	public static readonly Guid TEST_BOX_ID_2 = new Guid("00000000-0000-0000-0000-000000000002");
	public static readonly Guid TEST_BOX_ID_3 = new Guid("00000000-0000-0000-0000-000000000003");

	// Test Player IDs (pre-registered user accounts)
	public static readonly Guid TEST_PLAYER_ID_1 = new Guid("10000000-0000-0000-0000-000000000001");
	public static readonly Guid TEST_PLAYER_ID_2 = new Guid("10000000-0000-0000-0000-000000000002");
	public static readonly Guid TEST_PLAYER_ID_3 = new Guid("10000000-0000-0000-0000-000000000003");

	// Test Location IDs
	public const string TEST_LOCATION_ID_1 = "test_location_1";
	public const string TEST_LOCATION_ID_2 = "test_location_2";

	// Test Player Credentials (phone numbers and PINs)
	// Phone numbers in E.164 format: +1 (country code) + 503 (Portland area code) + 555 (fictional exchange) + 01XX (test suffix)
	// This format matches TestHelpers.GenerateTestPhoneNumber() pattern and passes libphonenumber validation
	public const string TEST_PLAYER_1_PHONE = "+15035550101";
	public const string TEST_PLAYER_1_PIN = "1111";
	public const string TEST_PLAYER_1_USERNAME = "test1";

	public const string TEST_PLAYER_2_PHONE = "+15035550102";
	public const string TEST_PLAYER_2_PIN = "2222";
	public const string TEST_PLAYER_2_USERNAME = "test2";

	public const string TEST_PLAYER_3_PHONE = "+15035550103";
	public const string TEST_PLAYER_3_PIN = "3333";
	public const string TEST_PLAYER_3_USERNAME = "test3";

	// Game Tags for testing
	public const string TEST_GAME_TAG_RACING = "racing";
	public const string TEST_GAME_TAG_MINING = "mining";
	public const string TEST_GAME_TAG_CARROM = "carrom";

	/// <summary>
	/// Get default test box ID for integration tests
	/// </summary>
	public static Guid GetDefaultTestBoxId() => TEST_BOX_ID_1;

	/// <summary>
	/// Get default test player ID for integration tests
	/// </summary>
	public static Guid GetDefaultTestPlayerId() => TEST_PLAYER_ID_1;

	/// <summary>
	/// Get default test location ID for integration tests
	/// </summary>
	public static string GetDefaultTestLocationId() => TEST_LOCATION_ID_1;

	/// <summary>
	/// Get test player credentials for login tests
	/// </summary>
	public static (string phone, string pin, string username) GetTestPlayer1Credentials()
	{
		return (TEST_PLAYER_1_PHONE, TEST_PLAYER_1_PIN, TEST_PLAYER_1_USERNAME);
	}

	/// <summary>
	/// Get test player credentials for multi-player tests
	/// </summary>
	public static (string phone, string pin, string username) GetTestPlayer2Credentials()
	{
		return (TEST_PLAYER_2_PHONE, TEST_PLAYER_2_PIN, TEST_PLAYER_2_USERNAME);
	}

	/// <summary>
	/// Get test player credentials for multi-player tests
	/// </summary>
	public static (string phone, string pin, string username) GetTestPlayer3Credentials()
	{
		return (TEST_PLAYER_3_PHONE, TEST_PLAYER_3_PIN, TEST_PLAYER_3_USERNAME);
	}
}
