using System;

namespace BarBox.Tests.Fixtures;

/// <summary>
/// Test configuration and environment settings for the test suite.
/// </summary>
public static class TestConfig
{
	/// <summary>
	/// Test backend URL (separate from production backend on port 8001)
	/// </summary>
	public static string BackendUrl =>
		Environment.GetEnvironmentVariable("TEST_BACKEND_URL") ?? "http://127.0.0.1:8001";

	/// <summary>
	/// Whether we're running in test mode
	/// </summary>
	public static bool IsTestMode =>
		Environment.GetEnvironmentVariable("BARBOX_TEST_MODE") == "1";

	/// <summary>
	/// Test timeout in seconds for async operations
	/// </summary>
	public const int DefaultTimeoutSeconds = 30;

	/// <summary>
	/// Delay between test backend polls in seconds
	/// </summary>
	public const float BackendPollDelaySeconds = 0.1f;

	/// <summary>
	/// Maximum attempts to check backend readiness
	/// </summary>
	public const int MaxBackendReadinessAttempts = 30;

	/// <summary>
	/// Test database path (should match test-backend.sh)
	/// </summary>
	public const string TestDatabasePath = "/tmp/barbox-test.db";

	/// <summary>
	/// Test backend port (should match test-backend.sh)
	/// </summary>
	public const int TestBackendPort = 8001;
}
