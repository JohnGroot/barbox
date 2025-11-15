using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Base class for game-specific event services
/// Provides common validation, error handling, and HTTP query patterns
/// Consolidates patterns from successful SessionManager/EventService flows
/// </summary>
public class GameEventServiceBase
{
	protected EventService _eventService;

	public GameEventServiceBase(EventService eventService = null)
	{
		_eventService = eventService ?? EventService.GetInstance();
		if (_eventService == null)
		{
			GD.PrintErr($"{GetType().Name}: EventService not found");
		}
	}

	/// <summary>
	/// Emit event with validation and error mapping
	/// </summary>
	protected async Task<Result<bool>> EmitEventSafeAsync(string eventType, object payload)
	{
		// Service availability and validity check
		if (_eventService == null || !GodotObject.IsInstanceValid(_eventService))
			return Result<bool>.Failure("Event service not available");

		// Backend session check
		if (_eventService.GetCurrentSessionId() == Guid.Empty)
			return Result<bool>.Failure("No active backend session");

		// Emit event
		var result = await _eventService.EmitEventAsync(eventType, payload);

		// Map backend errors to user-friendly messages
		return MapBackendError(result);
	}

	/// <summary>
	/// Query backend with proper connection lifecycle and error handling
	/// Returns raw JSON string for manual parsing by derived classes
	/// Delegates to EventService for HTTP operations (reuses persistent connection)
	/// </summary>
	protected async Task<Result<string>> QueryBackendRawAsync(
		string endpoint,
		Dictionary<string, string> queryParams = null
	)
	{
		if (_eventService == null || !GodotObject.IsInstanceValid(_eventService))
			return Result<string>.Failure("Event service not available");

		// Delegate to EventService which maintains persistent HttpClient
		return await _eventService.QueryRawAsync(endpoint, queryParams);
	}

	/// <summary>
	/// Parse JSON response body into Godot Dictionary
	/// Eliminates duplication of JSON parsing logic across game services
	/// </summary>
	protected Result<Godot.Collections.Dictionary> ParseJsonResponse(string responseBody)
	{
		if (string.IsNullOrEmpty(responseBody))
			return Result<Godot.Collections.Dictionary>.Failure("Empty response from server");

		var json = Json.ParseString(responseBody);
		if (json.Obj == null)
			return Result<Godot.Collections.Dictionary>.Failure("Failed to parse JSON response");

		var jsonDict = json.AsGodotDictionary();
		return Result<Godot.Collections.Dictionary>.Success(jsonDict);
	}

	/// <summary>
	/// Map backend errors to user-friendly messages
	/// Follows SessionManager error mapping patterns
	/// </summary>
	protected Result<T> MapBackendError<T>(Result<T> backendResult)
	{
		if (backendResult.IsSuccess)
			return backendResult;

		var error = backendResult.Error;

		// Map common backend errors to user-friendly messages
		if (error.Contains("not initialized") || error.Contains("not ready") || error.Contains("not available"))
			return Result<T>.Failure("Game service temporarily unavailable");

		if (error.Contains("timeout") || error.Contains("Timeout"))
			return Result<T>.Failure("Connection lost - your progress may not be saved");

		if (error.Contains("No active session") || error.Contains("No active backend session"))
			return Result<T>.Failure("Session expired - please restart the game");

		if (error.Contains("Connection failed") || error.Contains("Connection timeout"))
			return Result<T>.Failure("Unable to connect to game server");

		// Generic fallback
		return Result<T>.Failure("Unable to save game data");
	}

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
		public const string Upgrades = "upgrades";
		public const string LastMiningTime = "last_mining_time";
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
			if (!responseResult.IsSuccess)
				return Result<TData>.Failure(responseResult.Error);

			var parseResult = ParseJsonResponse(responseResult.Value);
			if (!parseResult.IsSuccess)
				return Result<TData>.Failure(parseResult.Error);

			// Delegate to specific parsing function
			return parseFunc(parseResult.Value);
		}
		catch (FormatException ex)
		{
			LogError($"Invalid data format in response: {ex.Message}");
			return Result<TData>.Failure("Invalid server response format");
		}
		catch (System.Exception ex)
		{
			LogError($"Unexpected error in query: {ex.Message}");
			return Result<TData>.Failure("Unable to retrieve data");
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
		/// Parse GUID from dictionary with validation
		/// </summary>
		public static Result<Guid> ParseGuid(Godot.Collections.Dictionary dict, string key)
		{
			if (!dict.ContainsKey(key))
				return Result<Guid>.Failure($"Missing required field: {key}");

			try
			{
				return Result<Guid>.Success(Guid.Parse(dict[key].AsString()));
			}
			catch (FormatException)
			{
				return Result<Guid>.Failure($"Invalid GUID format for field: {key}");
			}
		}

		/// <summary>
		/// Parse DateTime from dictionary with UTC validation
		/// </summary>
		public static Result<DateTime> ParseDateTime(Godot.Collections.Dictionary dict, string key)
		{
			if (!dict.ContainsKey(key))
				return Result<DateTime>.Failure($"Missing required field: {key}");

			try
			{
				var dateStr = dict[key].AsString();
				var parsed = DateTime.Parse(dateStr, null, System.Globalization.DateTimeStyles.RoundtripKind);

				// Ensure UTC for consistency
				if (parsed.Kind != DateTimeKind.Utc)
				{
					parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
				}

				return Result<DateTime>.Success(parsed);
			}
			catch (FormatException)
			{
				return Result<DateTime>.Failure($"Invalid DateTime format for field: {key}");
			}
		}

		/// <summary>
		/// Parse nullable DateTime from dictionary
		/// </summary>
		public static Result<DateTime?> ParseNullableDateTime(Godot.Collections.Dictionary dict, string key)
		{
			if (IsNullOrMissing(dict, key))
				return Result<DateTime?>.Success(null);

			try
			{
				var dateStr = dict[key].AsString();
				var parsed = DateTime.Parse(dateStr, null, System.Globalization.DateTimeStyles.RoundtripKind);

				// Ensure UTC for consistency
				if (parsed.Kind != DateTimeKind.Utc)
				{
					parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
				}

				return Result<DateTime?>.Success(parsed);
			}
			catch (FormatException)
			{
				return Result<DateTime?>.Failure($"Invalid DateTime format for field: {key}");
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
				return Result<Dictionary<string, int>>.Failure($"Missing required field: {key}");

			try
			{
				var result = new Dictionary<string, int>();
				var godotDict = dict[key].AsGodotDictionary();

				foreach (var godotKey in godotDict.Keys)
				{
					result[godotKey.AsString()] = godotDict[godotKey].AsInt32();
				}

				return Result<Dictionary<string, int>>.Success(result);
			}
			catch (System.Exception ex)
			{
				return Result<Dictionary<string, int>>.Failure(
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
			return Result<bool>.Failure(ValidationMessages.Required(parameterName));
		return Result<bool>.Success(true);
	}

	/// <summary>
	/// Validate leaderboard limit is within acceptable range
	/// </summary>
	protected Result<bool> ValidateLeaderboardLimit(int limit)
	{
		if (limit < MIN_LEADERBOARD_LIMIT || limit > MAX_LEADERBOARD_LIMIT)
			return Result<bool>.Failure(
				ValidationMessages.RangeError("Limit", MIN_LEADERBOARD_LIMIT, MAX_LEADERBOARD_LIMIT));
		return Result<bool>.Success(true);
	}

	#endregion
}
