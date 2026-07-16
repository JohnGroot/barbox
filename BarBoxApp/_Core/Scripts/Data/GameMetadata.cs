using Godot;

[GlobalClass]
public partial class GameMetadata : Resource
{
	[Export]
	public string GameId { get; set; }

	[Export]
	public string DisplayName { get; set; }

	[Export]
	public string Description { get; set; }

	[Export]
	public string ScenePath { get; set; }

	[Export]
	public string ThumbnailPath { get; set; }

	[Export]
	public int MaxPlayers { get; set; }

	[Export]
	public int CreditCost { get; set; }

	[Export]
	public bool IsActive { get; set; }

	[Export]
	public string Category { get; set; }

	[Export]
	public float DifficultyRating { get; set; }
}
