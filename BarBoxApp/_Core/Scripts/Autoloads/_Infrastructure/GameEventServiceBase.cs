using Godot;
using LightResults;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Base class for game-specific event services
/// Provides common validation, error handling, and HTTP query patterns
/// Consolidates patterns from successful SessionManager/SessionEventService flows
/// </summary>
public class GameEventServiceBase
{
	protected SessionEventService _eventService;

	public GameEventServiceBase(SessionEventService eventService)
	{
		_eventService = eventService;
	}

	public string GetVenueName() => _eventService.GetVenueName();
	public string GetBoxName() => _eventService.GetBoxName();
	public Guid GetBoxId() => _eventService.GetBoxId();
	public Guid GetCurrentSessionId() => _eventService.GetCurrentSessionId();

	/// <summary>
	/// Emit event with validation and error mapping
	/// </summary>
	protected async Task<Result<bool>> EmitEventSafeAsync(string eventType, object payload)
	{
		// Service availability and validity check
		if (_eventService == null || !GodotObject.IsInstanceValid(_eventService))
			return Result.Failure<bool>("Event service not available");

		// Backend session check
		if (_eventService.GetCurrentSessionId() == Guid.Empty)
			return Result.Failure<bool>("No active backend session");

		// Emit event
		var result = await _eventService.EmitEventAsync(eventType, payload);

		// Map backend errors to user-friendly messages
		return MapBackendError(result);
	}

	/// <summary>
	/// Submit a game result/score event. Single guarded entry point so every
	/// game routes results through the same validated path (delegates to
	/// EmitEventSafeAsync).
	/// </summary>
	protected Task<Result<bool>> SubmitResultAsync(string eventType, object payload)
		=> EmitEventSafeAsync(eventType, payload);

	/// <summary>
	/// Query backend with proper connection lifecycle and error handling
	/// Returns raw JSON string for manual parsing by derived classes
	/// Delegates to SessionEventService for HTTP operations (reuses persistent connection)
	/// </summary>
	protected async Task<Result<string>> QueryBackendRawAsync(
		string endpoint,
		Dictionary<string, string> queryParams = null
	)
	{
		if (_eventService == null || !GodotObject.IsInstanceValid(_eventService))
			return Result.Failure<string>("Event service not available");

		// Delegate to SessionEventService which maintains persistent HttpClient
		return await _eventService.QueryRawAsync(endpoint, queryParams);
	}

	/// <summary>
	/// Parse JSON response body into Godot Dictionary
	/// Eliminates duplication of JSON parsing logic across game services
	/// </summary>
	protected Result<Godot.Collections.Dictionary> ParseJsonResponse(string responseBody)
	{
		if (string.IsNullOrEmpty(responseBody))
			return Result.Failure<Godot.Collections.Dictionary>("Empty response from server");

		var json = Json.ParseString(responseBody);
		if (json.Obj == null)
			return Result.Failure<Godot.Collections.Dictionary>("Failed to parse JSON response");

		var jsonDict = json.AsGodotDictionary();
		return Result.Success(jsonDict);
	}

	/// <summary>
	/// Map backend errors to user-friendly messages
	/// Follows SessionManager error mapping patterns
	/// </summary>
	protected Result<T> MapBackendError<T>(Result<T> backendResult)
	{
		if (backendResult.IsSuccess(out var value))
			return Result.Success(value);

		if (backendResult.IsFailure(out var error))
		{
			var userMessage = MapErrorMessage(error.Message);
			return Result.Failure<T>(userMessage);
		}

		return Result.Failure<T>("Unknown error occurred");
	}

	private static string MapErrorMessage(string backendError) => backendError switch
	{
		_ when backendError.Contains("not initialized") => "Game service temporarily unavailable",
		_ when backendError.Contains("not ready") => "Game service temporarily unavailable",
		_ when backendError.Contains("not available") => "Game service temporarily unavailable",
		_ when backendError.Contains("timeout", StringComparison.OrdinalIgnoreCase) => "Connection lost - your progress may not be saved",
		_ when backendError.Contains("No active session") => "Session expired - please restart the game",
		_ when backendError.Contains("No active backend session") => "Session expired - please restart the game",
		_ when backendError.Contains("Connection failed") => "Unable to connect to game server",
		_ when backendError.Contains("Connection timeout") => "Unable to connect to game server",
		_ => "Unable to save game data"
	};

	/// <summary>
	/// Log info message with service name prefix
	/// </summary>
	protected void LogInfo(string message)
	{
		GD.Print($"[{GetType().Name}] {message}");
	}

	/// <summary>
	/// Log warning message with service name prefix
	/// </summary>
	protected void LogWarning(string message)
	{
		GD.PushWarning($"[{GetType().Name}] {message}");
	}

	/// <summary>
	/// Log error message with service name prefix
	/// </summary>
	protected void LogError(string message)
	{
		GD.PrintErr($"[{GetType().Name}] {message}");
	}

	#region Shared Constants

	protected const int MIN_LEADERBOARD_LIMIT = 1;
	protected const int MAX_LEADERBOARD_LIMIT = 100;
	protected const int DEFAULT_LEADERBOARD_LIMIT = 10;

	/// <summary>
	/// Cached JSON field names to reduce GC allocations
	/// </summary>
	protected static class JsonFieldNames
	{
		// Common fields
		public const string PlayerId = "player_id";
		public const string Username = "username";
		public const string LastUpdated = "last_updated";
		public const string TrackId = "track_id";
		public const string LocationId = "location_id";
		public const string Metric = "metric";
		public const string MetricValue = "metric_value";
		public const string EntryDate = "entry_date";
		public const string Leaderboard = "leaderboard";

		// Mining-specific fields
		public const string Gems = "gems";
		public const string Inventory = "inventory";
		public const string Upgrades = "upgrades";
		public const string LastMiningTime = "last_mining_time";
		public const string PendingGems = "pending_gems";
		public const string HasReceivedBonus = "has_received_bonus";
		public const string TotalEvents = "total_events";
		public const string FirstEventTime = "first_event_time";
		public const string LastEventTime = "last_event_time";

		// Racing-specific fields
		public const string LapTimes = "lap_times";

		// Carrom-specific fields
		public const string TotalScore = "total_score";
		public const string TotalWins = "total_wins";
	}

	#endregion

	#region Query Template Method

	/// <summary>
	/// Execute backend query with automatic error handling and JSON parsing
	/// Reduces boilerplate by consolidating try-catch-parse patterns
	/// </summary>
	/// <typeparam name="TData">Type of data to parse from response</typeparam>
	/// <param name="endpoint">Backend endpoint to query</param>
	/// <param name="queryParams">Optional query parameters</param>
	/// <param name="parseFunc">Function to parse JSON dictionary into TData</param>
	/// <returns>Result containing parsed data or error message</returns>
	protected async Task<Result<TData>> QueryBackendAsync<TData>(
		string endpoint,
		Dictionary<string, string> queryParams,
		Func<Godot.Collections.Dictionary, Result<TData>> parseFunc
	)
	{
		try
		{
			var responseResult = await QueryBackendRawAsync(endpoint, queryParams);
			if (responseResult.IsFailure(out var responseError))
				return Result.Failure<TData>(responseError.Message);

			if (responseResult.IsSuccess(out var responseBody))
			{
				var parseResult = ParseJsonResponse(responseBody);
				if (parseResult.IsFailure(out var parseError))
					return Result.Failure<TData>(parseError.Message);

				if (parseResult.IsSuccess(out var jsonDict))
				{
					// Delegate to specific parsing function
					return parseFunc(jsonDict);
				}
			}

			return Result.Failure<TData>("Unable to retrieve data");
		}
		catch (FormatException ex)
		{
			LogError($"Invalid data format in response: {ex.Message}");
			return Result.Failure<TData>("Invalid server response format");
		}
		catch (System.Exception ex)
		{
			LogError($"Unexpected error in query: {ex.Message}");
			return Result.Failure<TData>("Unable to retrieve data");
		}
	}

	#endregion

	#region JSON Parsing Helpers

	/// <summary>
	/// JSON parsing utilities for game event services
	/// Provides safe, consistent parsing with proper error handling
	/// </summary>
	protected static class JsonParsingHelpers
	{
		/// <summary>
		/// Check if a key is missing or has null value in dictionary
		/// </summary>
		public static bool IsNullOrMissing(Godot.Collections.Dictionary dict, string key)
		{
			return !dict.ContainsKey(key) || dict[key].VariantType == Godot.Variant.Type.Nil;
		}

		/// <summary>
		/// Parse string from dictionary with validation
		/// </summary>
		public static Result<string> ParseString(Godot.Collections.Dictionary dict, string key)
		{
			if (!dict.ContainsKey(key))
				return Result.Failure<string>($"Missing required field: {key}");

			try
			{
				var value = dict[key].AsString();
				if (string.IsNullOrEmpty(value))
					return Result.Failure<string>($"Field {key} is null or empty");

				return Result.Success(value);
			}
			catch (System.Exception ex)
			{
				return Result.Failure<string>($"Failed to parse string for {key}: {ex.Message}");
			}
		}

		/// <summary>
		/// Parse GUID from dictionary with validation
		/// </summary>
		public static Result<Guid> ParseGuid(Godot.Collections.Dictionary dict, string key)
		{
			if (!dict.ContainsKey(key))
				return Result.Failure<Guid>($"Missing required field: {key}");

			try
			{
				return Result.Success(Guid.Parse(dict[key].AsString()));
			}
			catch (FormatException)
			{
				return Result.Failure<Guid>($"Invalid GUID format for field: {key}");
			}
		}

		/// <summary>
		/// Parse DateTime from dictionary with UTC validation
		/// </summary>
		public static Result<DateTime> ParseDateTime(Godot.Collections.Dictionary dict, string key)
		{
			if (!dict.ContainsKey(key))
				return Result.Failure<DateTime>($"Missing required field: {key}");

			try
			{
				var dateStr = dict[key].AsString();
				var parsed = DateTime.Parse(dateStr, null, System.Globalization.DateTimeStyles.RoundtripKind);

				// Ensure UTC for consistency
				if (parsed.Kind != DateTimeKind.Utc)
				{
					parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
				}

				return Result.Success(parsed);
			}
			catch (FormatException)
			{
				return Result.Failure<DateTime>($"Invalid DateTime format for field: {key}");
			}
		}

		/// <summary>
		/// Parse nullable DateTime from dictionary
		/// </summary>
		public static Result<DateTime?> ParseNullableDateTime(Godot.Collections.Dictionary dict, string key)
		{
			if (IsNullOrMissing(dict, key))
				return Result.Success<DateTime?>(null);

			try
			{
				var dateStr = dict[key].AsString();
				var parsed = DateTime.Parse(dateStr, null, System.Globalization.DateTimeStyles.RoundtripKind);

				// Ensure UTC for consistency
				if (parsed.Kind != DateTimeKind.Utc)
				{
					parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
				}

				return Result.Success<DateTime?>(parsed);
			}
			catch (FormatException)
			{
				return Result.Failure<DateTime?>($"Invalid DateTime format for field: {key}");
			}
		}

		/// <summary>
		/// Parse dictionary of string keys to int values
		/// Used for gem inventories, upgrade levels, etc.
		/// </summary>
		public static Result<Dictionary<string, int>> ParseIntDictionary(
			Godot.Collections.Dictionary dict,
			string key)
		{
			if (!dict.ContainsKey(key))
				return Result.Failure<Dictionary<string, int>>($"Missing required field: {key}");

			try
			{
				var result = new Dictionary<string, int>();
				var godotDict = dict[key].AsGodotDictionary();

				foreach (var godotKey in godotDict.Keys)
				{
					result[godotKey.AsString()] = godotDict[godotKey].AsInt32();
				}

				return Result.Success(result);
			}
			catch (System.Exception ex)
			{
				return Result.Failure<Dictionary<string, int>>(
					$"Failed to parse dictionary for {key}: {ex.Message}");
			}
		}
	}

	#endregion

	#region Validation Helpers

	/// <summary>
	/// Standardized validation error messages
	/// </summary>
	protected static class ValidationMessages
	{
		public static string Required(string fieldName) => $"{fieldName} is required";
		public static string MinValue(string fieldName, int minValue) =>
			$"{fieldName} must be at least {minValue}";
		public static string MaxValue(string fieldName, int maxValue) =>
			$"{fieldName} must not exceed {maxValue}";
		public static string InvalidFormat(string fieldName) =>
			$"Invalid format for {fieldName}";
		public static string RangeError(string fieldName, int minValue, int maxValue) =>
			$"{fieldName} must be between {minValue} and {maxValue}";
	}

	/// <summary>
	/// Validate GUID is not empty
	/// </summary>
	protected Result<bool> ValidateGuid(Guid value, string parameterName)
	{
		if (value == Guid.Empty)
			return Result.Failure<bool>(ValidationMessages.Required(parameterName));
		return Result.Success(true);
	}

	/// <summary>
	/// Validate leaderboard limit is within acceptable range
	/// </summary>
	protected Result<bool> ValidateLeaderboardLimit(int limit)
	{
		if (limit < MIN_LEADERBOARD_LIMIT || limit > MAX_LEADERBOARD_LIMIT)
			return Result.Failure<bool>(
				ValidationMessages.RangeError("Limit", MIN_LEADERBOARD_LIMIT, MAX_LEADERBOARD_LIMIT));
		return Result.Success(true);
	}

	#endregion
}
