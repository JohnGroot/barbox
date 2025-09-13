using Godot;
using System;

/// <summary>
/// Compatibility class for existing code that references UserData
/// Minimal implementation to prevent compilation errors
/// New code should use GlobalUserData and LocalUserData from DataStore
/// </summary>
[GlobalClass]
public partial class UserData : Resource
{
	[Export] public string UserId { get; set; } = string.Empty;
	[Export] public string Pin { get; set; } = string.Empty;
	[Export] public int Credits { get; set; } = 0;
	public DateTime CreatedAt { get; set; } = DateTime.Now;
	public DateTime LastLoginAt { get; set; } = DateTime.Now;
	[Export] public Godot.Collections.Dictionary<string, int> HighScores { get; set; } = new();

	// Simple constructor
	public UserData()
	{
	}

	public UserData(string userId)
	{
		UserId = userId;
	}
}