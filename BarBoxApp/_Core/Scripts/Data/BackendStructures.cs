using System;

/// <summary>
/// Core backend response structures for player-related queries
/// Game-specific response structures are defined in their respective EventService files
/// </summary>

/// <summary>
/// Response for username availability check
/// </summary>
public partial class UsernameAvailabilityResponse
{
	public string Username { get; set; }
	public bool IsAvailable { get; set; }
}

/// <summary>
/// Response for player credits query
/// </summary>
public partial class PlayerCreditsResponse
{
	public string PlayerId { get; set; }
	public string LocationId { get; set; }
	public int Credits { get; set; }
}
