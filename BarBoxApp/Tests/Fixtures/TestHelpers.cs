using System;
using System.Threading.Tasks;
using Godot;

namespace BarBox.Tests.Fixtures;

/// <summary>
/// Shared test utilities and helper methods for the test suite.
/// </summary>
public static class TestHelpers
{
	/// <summary>
	/// Generate a unique test identifier for isolation
	/// </summary>
	public static string GenerateTestId(string prefix = "test")
	{
		return $"{prefix}_{Guid.NewGuid():N}".Substring(0, 20);
	}

	/// <summary>
	/// Create a unique box ID for testing
	/// </summary>
	public static Guid GenerateTestBoxId()
	{
		return Guid.NewGuid();
	}

	/// <summary>
	/// Create a unique player ID for testing
	/// </summary>
	public static Guid GenerateTestPlayerId()
	{
		return Guid.NewGuid();
	}

	/// <summary>
	/// Create a unique session ID for testing
	/// </summary>
	public static Guid GenerateTestSessionId()
	{
		return Guid.NewGuid();
	}

	/// <summary>
	/// Wait for a condition to be true with timeout
	/// </summary>
	/// <param name="condition">Condition to check</param>
	/// <param name="timeoutSeconds">Timeout in seconds</param>
	/// <param name="pollDelaySeconds">Delay between checks in seconds</param>
	/// <returns>True if condition became true, false if timeout</returns>
	public static async Task<bool> WaitForConditionAsync(
		Func<bool> condition,
		float timeoutSeconds = TestConfig.DefaultTimeoutSeconds,
		float pollDelaySeconds = TestConfig.BackendPollDelaySeconds)
	{
		var startTime = Time.GetTicksMsec();
		var timeoutMs = timeoutSeconds * 1000;

		while (!condition())
		{
			var elapsed = Time.GetTicksMsec() - startTime;
			if (elapsed > timeoutMs)
			{
				return false;
			}

			await Task.Delay((int)(pollDelaySeconds * 1000));
		}

		return true;
	}

	/// <summary>
	/// Check if the test backend is healthy and responding
	/// </summary>
	public static async Task<bool> IsTestBackendHealthyAsync()
	{
		try
		{
			var httpClient = new HttpClient();
			var error = httpClient.ConnectToHost("127.0.0.1", TestConfig.TestBackendPort);
			if (error != Error.Ok)
			{
				httpClient.Close();
				return false;
			}

			// Poll for connection
			for (int i = 0; i < 50; i++)
			{
				httpClient.Poll();
				var status = httpClient.GetStatus();

				if (status == HttpClient.Status.Connected)
				{
					// Connection successful, now try a request
					var headers = new string[] { "Accept: application/json" };
					var requestError = httpClient.Request(Godot.HttpClient.Method.Get, "/alive", headers);
					if (requestError != Error.Ok)
					{
						httpClient.Close();
						return false;
					}

					// Wait for response
					while (httpClient.GetStatus() == HttpClient.Status.Requesting)
					{
						httpClient.Poll();
						await Task.Delay(50);
					}

					var responseCode = httpClient.GetResponseCode();
					httpClient.Close();

					return responseCode == 200;
				}

				if (status == HttpClient.Status.CantConnect ||
				    status == HttpClient.Status.CantResolve ||
				    status == HttpClient.Status.ConnectionError)
				{
					httpClient.Close();
					return false;
				}

				await Task.Delay(100);
			}

			httpClient.Close();
			return false;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[TestHelpers] Error checking backend health: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Create a unique username for testing (max 7 chars as per requirements)
	/// </summary>
	public static string GenerateTestUsername()
	{
		return $"tst{Guid.NewGuid():N}".Substring(0, 7);
	}

	/// <summary>
	/// Create a test phone number
	/// </summary>
	public static string GenerateTestPhoneNumber()
	{
		var random = new Random();
		return $"555{random.Next(1000000, 9999999)}"; // 555-XXX-XXXX format
	}

	/// <summary>
	/// Log test information with proper formatting
	/// </summary>
	public static void LogTestInfo(string message)
	{
		GD.Print($"[TEST INFO] {message}");
	}

	/// <summary>
	/// Log test error with proper formatting
	/// </summary>
	public static void LogTestError(string message)
	{
		GD.PrintErr($"[TEST ERROR] {message}");
	}

	/// <summary>
	/// Log test warning with proper formatting
	/// </summary>
	public static void LogTestWarning(string message)
	{
		GD.Print($"[TEST WARNING] {message}");
	}
}
