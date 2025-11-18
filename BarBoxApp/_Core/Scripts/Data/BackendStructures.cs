#nullable enable
using System;
using System.Text.Json.Serialization;
using LightResults;

/// <summary>
/// Core backend response structures for player-related queries
/// Game-specific response structures are defined in their respective EventService files
///
/// UUID Serialization Convention:
/// - Backend uses UUID type for IDs (player_id, box_id, origin_id, etc.)
/// - UUIDs are automatically serialized to string format in JSON responses
/// - C# uses string type for all ID fields to match JSON serialization
/// - Example: Python UUID("123e4567-e89b-12d3-a456-426614174000") -> JSON "123e4567-e89b-12d3-a456-426614174000" -> C# string
/// </summary>

/// <summary>
/// Response for username availability check
/// </summary>
public record UsernameAvailabilityResponse
{
	[JsonPropertyName("username")]
	public string Username { get; init; } = "";

	[JsonPropertyName("is_available")]
	public bool IsAvailable { get; init; }
}

/// <summary>
/// Response for player credits query
/// </summary>
public record PlayerCreditsResponse
{
	[JsonPropertyName("player_id")]
	public string PlayerId { get; init; } = "";

	[JsonPropertyName("location_id")]
	public string LocationId { get; init; } = "";

	[JsonPropertyName("credits")]
	public int Credits { get; init; } = 0;
}

/// <summary>
/// Request for creating a new player account
/// </summary>
public record PlayerCreateRequest
{
	[JsonPropertyName("id")]
	public string Id { get; init; } = "";

	[JsonPropertyName("tag")]
	public string Tag { get; init; } = "";

	[JsonPropertyName("origin_id")]
	public string OriginId { get; init; } = "";

	[JsonPropertyName("pin")]
	public string Pin { get; init; } = "";

	[JsonPropertyName("phone_number")]
	public string PhoneNumber { get; init; } = "";
}

/// <summary>
/// Response for player creation
/// Contains complete player details including username, origin box, and phone number
/// </summary>
public record PlayerDetailResponse
{
	[JsonPropertyName("id")]
	public string Id { get; init; } = "";

	[JsonPropertyName("tag")]
	public string Tag { get; init; } = "";

	[JsonPropertyName("origin_id")]
	public string OriginId { get; init; } = "";

	[JsonPropertyName("phone_number")]
	public string PhoneNumber { get; init; } = "";
}

/// <summary>
/// Request for registering a new box (physical terminal)
/// </summary>
public record BoxCreateRequest
{
	[JsonPropertyName("id")]
	public string Id { get; init; } = "";

	[JsonPropertyName("name")]
	public string Name { get; init; } = "";

	[JsonPropertyName("tag")]
	public string Tag { get; init; } = "";
}

/// <summary>
/// Response for box registration and verification
/// Fields are nullable because backend returns different responses:
/// - First registration: All fields populated (id, name, tag, api_key, warning)
/// - Verification: Only id field populated
/// WARNING: API key is only returned once on first registration - must be saved immediately!
/// </summary>
public record BoxDetailResponse
{
	[JsonPropertyName("id")]
	public string Id { get; init; } = "";

	/// <summary>
	/// Box name - only present on first-time registration
	/// </summary>
	[JsonPropertyName("name")]
	public string? Name { get; init; }

	/// <summary>
	/// Box tag - only present on first-time registration
	/// </summary>
	[JsonPropertyName("tag")]
	public string? Tag { get; init; }

	/// <summary>
	/// API key for authenticating box requests
	/// Only returned on first-time registration - save immediately!
	/// Will be null on subsequent verification calls
	/// </summary>
	[JsonPropertyName("api_key")]
	public string? ApiKey { get; init; }

	/// <summary>
	/// Warning message about saving API key - only present on first registration
	/// </summary>
	[JsonPropertyName("warning")]
	public string? Warning { get; init; }
}

/// <summary>
/// Validation error detail for a specific field
/// Note: Value can be any type (string, UUID, int, null) from backend
/// </summary>
public record ValidationErrorDetail
{
	[JsonPropertyName("field")]
	public string Field { get; init; } = "";

	[JsonPropertyName("message")]
	public string Message { get; init; } = "";

	[JsonPropertyName("value")]
	public object? Value { get; init; }
}

/// <summary>
/// Response for player creation validation (pre-flight check)
/// </summary>
public record PlayerValidationResponse
{
	[JsonPropertyName("valid")]
	public bool Valid { get; init; }

	[JsonPropertyName("errors")]
	public ValidationErrorDetail[] Errors { get; init; } = [];
}

/// <summary>
/// Individual player contribution to machine credit pot
/// </summary>
public record MachinePlayerContribution
{
	[JsonPropertyName("player_id")]
	public string PlayerId { get; init; } = "";

	[JsonPropertyName("amount")]
	public int Amount { get; init; }
}

/// <summary>
/// Response for machine credit pot query (per box+game)
/// </summary>
public record MachineCreditsResponse
{
	[JsonPropertyName("box_id")]
	public string BoxId { get; init; } = "";

	[JsonPropertyName("game_tag")]
	public string GameTag { get; init; } = "";

	[JsonPropertyName("balance")]
	public int Balance { get; init; }

	[JsonPropertyName("contributions")]
	public MachinePlayerContribution[] Contributions { get; init; } = [];
}

/// <summary>
/// Request for player authentication (login)
/// </summary>
public record PlayerLoginRequest
{
	[JsonPropertyName("phone_number")]
	public string PhoneNumber { get; init; } = "";

	[JsonPropertyName("pin")]
	public string Pin { get; init; } = "";

	[JsonPropertyName("box_id")]
	public string BoxId { get; init; } = "";
}

/// <summary>
/// Response for successful player authentication (arcade-optimized single token)
/// </summary>
public record PlayerLoginResponse
{
	/// <summary>
	/// JWT access token for authenticated API requests.
	/// Include in Authorization header as "Bearer {token}".
	/// Valid for duration specified in ExpiresAt field.
	/// </summary>
	[JsonPropertyName("access_token")]
	public string AccessToken { get; init; } = "";

	[JsonPropertyName("player_id")]
	public string PlayerId { get; init; } = "";

	[JsonPropertyName("username")]
	public string Username { get; init; } = "";

	/// <summary>
	/// Token expiration time in ISO 8601 format (UTC).
	/// Example: "2025-11-18T14:30:00Z"
	/// Check this value to determine if token refresh is needed.
	/// </summary>
	[JsonPropertyName("expires_at")]
	public string ExpiresAt { get; init; } = "";
}

/// <summary>
/// Validation extensions for backend response DTOs.
/// Use these to validate that required fields are present after deserialization.
/// </summary>
public static class ResponseValidationExtensions
{
	/// <summary>
	/// Validate that PlayerLoginResponse contains all required fields.
	/// Call this immediately after deserialization to catch malformed responses.
	/// </summary>
	public static Result<PlayerLoginResponse> ValidateRequired(this PlayerLoginResponse response)
	{
		if (response == null)
			return Result.Failure<PlayerLoginResponse>("Response is null");

		if (string.IsNullOrEmpty(response.AccessToken))
			return Result.Failure<PlayerLoginResponse>("Missing access_token in login response");

		if (string.IsNullOrEmpty(response.PlayerId))
			return Result.Failure<PlayerLoginResponse>("Missing player_id in login response");

		if (string.IsNullOrEmpty(response.Username))
			return Result.Failure<PlayerLoginResponse>("Missing username in login response");

		if (string.IsNullOrEmpty(response.ExpiresAt))
			return Result.Failure<PlayerLoginResponse>("Missing expires_at in login response");

		return Result.Success(response);
	}

	/// <summary>
	/// Validate that PlayerDetailResponse contains all required fields.
	/// Call this immediately after deserialization to catch malformed responses.
	/// </summary>
	public static Result<PlayerDetailResponse> ValidateRequired(this PlayerDetailResponse response)
	{
		if (response == null)
			return Result.Failure<PlayerDetailResponse>("Response is null");

		if (string.IsNullOrEmpty(response.Id))
			return Result.Failure<PlayerDetailResponse>("Missing id in player detail response");

		if (string.IsNullOrEmpty(response.Tag))
			return Result.Failure<PlayerDetailResponse>("Missing tag in player detail response");

		if (string.IsNullOrEmpty(response.OriginId))
			return Result.Failure<PlayerDetailResponse>("Missing origin_id in player detail response");

		if (string.IsNullOrEmpty(response.PhoneNumber))
			return Result.Failure<PlayerDetailResponse>("Missing phone_number in player detail response");

		return Result.Success(response);
	}

	/// <summary>
	/// Validate that BoxDetailResponse contains required fields.
	/// Only validates the id field, as other fields are optional (depend on registration vs verification)
	/// </summary>
	public static Result<BoxDetailResponse> ValidateRequired(this BoxDetailResponse response)
	{
		if (response == null)
			return Result.Failure<BoxDetailResponse>("Response is null");

		if (string.IsNullOrEmpty(response.Id))
			return Result.Failure<BoxDetailResponse>("Missing id in box response");

		return Result.Success(response);
	}

	/// <summary>
	/// Validate that BoxDetailResponse is a first-time registration response (has API key)
	/// Call this when you expect a first-time registration with API key
	/// </summary>
	public static Result<BoxDetailResponse> ValidateFirstRegistration(this BoxDetailResponse response)
	{
		var baseValidation = response.ValidateRequired();
		if (baseValidation.IsFailure(out var error))
			return Result.Failure<BoxDetailResponse>(error);

		if (string.IsNullOrEmpty(response.ApiKey))
			return Result.Failure<BoxDetailResponse>("Expected first-time registration but api_key is missing");

		if (string.IsNullOrEmpty(response.Name))
			return Result.Failure<BoxDetailResponse>("Expected first-time registration but name is missing");

		if (string.IsNullOrEmpty(response.Tag))
			return Result.Failure<BoxDetailResponse>("Expected first-time registration but tag is missing");

		return Result.Success(response);
	}

	/// <summary>
	/// Validate that UsernameAvailabilityResponse contains all required fields.
	/// Call this immediately after deserialization to catch malformed responses.
	/// </summary>
	public static Result<UsernameAvailabilityResponse> ValidateRequired(this UsernameAvailabilityResponse response)
	{
		if (response == null)
			return Result.Failure<UsernameAvailabilityResponse>("Response is null");

		if (string.IsNullOrEmpty(response.Username))
			return Result.Failure<UsernameAvailabilityResponse>("Missing username in availability response");

		return Result.Success(response);
	}

	/// <summary>
	/// Validate that PlayerCreditsResponse contains all required fields.
	/// Call this immediately after deserialization to catch malformed responses.
	/// </summary>
	public static Result<PlayerCreditsResponse> ValidateRequired(this PlayerCreditsResponse response)
	{
		if (response == null)
			return Result.Failure<PlayerCreditsResponse>("Response is null");

		if (string.IsNullOrEmpty(response.PlayerId))
			return Result.Failure<PlayerCreditsResponse>("Missing player_id in credits response");

		if (string.IsNullOrEmpty(response.LocationId))
			return Result.Failure<PlayerCreditsResponse>("Missing location_id in credits response");

		return Result.Success(response);
	}
}
