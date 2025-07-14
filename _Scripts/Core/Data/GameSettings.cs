using Godot;

[GlobalClass]
public partial class GameSettings : Resource
{
	[ExportCategory("Display")]
	[Export] public bool Fullscreen { get; set; } = true;
	[Export] public Vector2I Resolution { get; set; } = new Vector2I(1920, 1080);
	[Export] public float MasterVolume { get; set; } = 1.0f;

	[ExportCategory("Input")]
	[Export] public bool TouchEnabled { get; set; } = true;
	[Export] public bool MouseEnabled { get; set; } = true;
	[Export] public float TouchSensitivity { get; set; } = 1.0f;

	[ExportCategory("Arcade")]
	[Export] public int DefaultCredits { get; set; } = 5;
	[Export] public float IdleTimeoutSeconds { get; set; } = 120.0f;
	[Export] public bool ShowFPS { get; set; } = false;
	[Export] public bool DebugMode { get; set; } = false;
}