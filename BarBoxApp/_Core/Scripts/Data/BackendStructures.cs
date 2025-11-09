using System;
using System.Text.Json.Serialization;

/// <summary>
/// Core backend response structures for player-related queries
/// Game-specific response structures are defined in their respective EventService files
/// </summary>

/// <summary>
/// Response for username availability check
/// </summary>
public partial class UsernameAvailabilityResponse
{
	[JsonPropertyName("username")]
	public string Username { get; set; }

	[JsonPropertyName("is_available")]
	public bool IsAvailable { get; set; }
}

/// <summary>
/// Response for player credits query
/// </summary>
public partial class PlayerCreditsResponse
{
	[JsonPropertyName("player_id")]
	public string PlayerId { get; set; }

	[JsonPropertyName("location_id")]
	public string LocationId { get; set; }

	[JsonPropertyName("credits")]
	public int Credits { get; set; }
}

/// <summary>
/// Request for creating a new player account
/// </summary>
public partial class PlayerCreateRequest
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("tag")]
	public string Tag { get; set; }

	[JsonPropertyName("origin_id")]
	public string OriginId { get; set; }
}

/// <summary>
/// Response for player creation
/// </summary>
public partial class PlayerDetailResponse
{
	[JsonPropertyName("id")]
	public string Id { get; set; }
}

/// <summary>
/// Request for registering a new box (physical terminal)
/// </summary>
public partial class BoxCreateRequest
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("tag")]
	public string Tag { get; set; }
}

/// <summary>
/// Response for box registration
/// </summary>
public partial class BoxDetailResponse
{
	[JsonPropertyName("id")]
	public string Id { get; set; }
}

/// <summary>
/// Validation error detail for a specific field
/// </summary>
public partial class ValidationErrorDetail
{
	[JsonPropertyName("field")]
	public string Field { get; set; }

	[JsonPropertyName("message")]
	public string Message { get; set; }

	[JsonPropertyName("value")]
	public string Value { get; set; }
}

/// <summary>
/// Response for player creation validation (pre-flight check)
/// </summary>
public partial class PlayerValidationResponse
{
	[JsonPropertyName("valid")]
	public bool Valid { get; set; }

	[JsonPropertyName("errors")]
	public ValidationErrorDetail[] Errors { get; set; } = Array.Empty<ValidationErrorDetail>();
}

/// <summary>
/// Individual player contribution to machine credit pot
/// </summary>
public partial class MachinePlayerContribution
{
	[JsonPropertyName("player_id")]
	public string PlayerId { get; set; }

	[JsonPropertyName("amount")]
	public int Amount { get; set; }
}

/// <summary>
/// Response for machine credit pot query (per box+game)
/// </summary>
public partial class MachineCreditsResponse
{
	[JsonPropertyName("box_id")]
	public string BoxId { get; set; }

	[JsonPropertyName("game_tag")]
	public string GameTag { get; set; }

	[JsonPropertyName("balance")]
	public int Balance { get; set; }

	[JsonPropertyName("contributions")]
	public MachinePlayerContribution[] Contributions { get; set; } = Array.Empty<MachinePlayerContribution>();
}
