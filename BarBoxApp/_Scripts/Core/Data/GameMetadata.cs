using Godot;

[GlobalClass]
public partial class GameMetadata : Resource
{
	[Export] public string GameId { get; set; } = string.Empty;
	[Export] public string DisplayName { get; set; } = string.Empty;
	[Export] public string Description { get; set; } = string.Empty;
	[Export] public string ScenePath { get; set; } = string.Empty;
	[Export] public string ThumbnailPath { get; set; } = string.Empty;
	[Export] public int MaxPlayers { get; set; } = 1;
	[Export] public int CreditCost { get; set; } = 1;
	[Export] public bool IsActive { get; set; } = true;
	[Export] public string Category { get; set; } = "Arcade";
	[Export] public float DifficultyRating { get; set; } = 1.0f;
}