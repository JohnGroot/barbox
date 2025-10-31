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
}
