using System;
using System.Text.Json.Serialization;

/// <summary>
/// Core backend response structures for player-related queries
/// Game-specific response structures are defined in their respective EventService files
/// </summary>

/// <summary>
/// Response for username availability check
/// </summary>
public record class UsernameAvailabilityResponse
{
	[JsonPropertyName("username")]
	public string Username { get; init; }

	[JsonPropertyName("is_available")]
	public bool IsAvailable { get; init; }
}

/// <summary>
/// Response for player credits query
/// </summary>
public record class PlayerCreditsResponse
{
	[JsonPropertyName("player_id")]
	public string PlayerId { get; init; }

	[JsonPropertyName("location_id")]
	public string LocationId { get; init; }

	[JsonPropertyName("credits")]
	public int Credits { get; init; }
}

/// <summary>
/// Request for creating a new player account
/// </summary>
public record class PlayerCreateRequest
{
	[JsonPropertyName("id")]
	public string Id { get; init; }

	[JsonPropertyName("tag")]
	public string Tag { get; init; }

	[JsonPropertyName("origin_id")]
	public string OriginId { get; init; }
}

/// <summary>
/// Response for player creation
/// </summary>
public record class PlayerDetailResponse
{
	[JsonPropertyName("id")]
	public string Id { get; init; }
}

/// <summary>
/// Request for registering a new box (physical terminal)
/// </summary>
public record class BoxCreateRequest
{
	[JsonPropertyName("id")]
	public string Id { get; init; }

	[JsonPropertyName("name")]
	public string Name { get; init; }

	[JsonPropertyName("tag")]
	public string Tag { get; init; }
}

/// <summary>
/// Response for box registration
/// </summary>
public record class BoxDetailResponse
{
	[JsonPropertyName("id")]
	public string Id { get; init; }
}

/// <summary>
/// Validation error detail for a specific field
/// </summary>
public record class ValidationErrorDetail
{
	[JsonPropertyName("field")]
	public string Field { get; init; }

	[JsonPropertyName("message")]
	public string Message { get; init; }

	[JsonPropertyName("value")]
	public string Value { get; init; }
}

/// <summary>
/// Response for player creation validation (pre-flight check)
/// </summary>
public record class PlayerValidationResponse
{
	[JsonPropertyName("valid")]
	public bool Valid { get; init; }

	[JsonPropertyName("errors")]
	public ValidationErrorDetail[] Errors { get; init; } = [];
}

/// <summary>
/// Individual player contribution to machine credit pot
/// </summary>
public record class MachinePlayerContribution
{
	[JsonPropertyName("player_id")]
	public string PlayerId { get; init; }

	[JsonPropertyName("amount")]
	public int Amount { get; init; }
}

/// <summary>
/// Response for machine credit pot query (per box+game)
/// </summary>
public record class MachineCreditsResponse
{
	[JsonPropertyName("box_id")]
	public string BoxId { get; init; }

	[JsonPropertyName("game_tag")]
	public string GameTag { get; init; }

	[JsonPropertyName("balance")]
	public int Balance { get; init; }

	[JsonPropertyName("contributions")]
	public MachinePlayerContribution[] Contributions { get; init; } = [];
}
