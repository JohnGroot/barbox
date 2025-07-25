using Godot;
using System;

/// <summary>
/// Simple data structure for context button configuration
/// Replaces the complex GameContextButton class with lightweight data
/// </summary>
public struct ContextButtonData
{
	public string Text;
	public Action OnPressed;
	public string Icon;
	public bool Enabled;
	public string Tooltip;

	public ContextButtonData(string text, Action onPressed, string icon = "", bool enabled = true, string tooltip = "")
	{
		Text = text;
		OnPressed = onPressed;
		Icon = icon;
		Enabled = enabled;
		Tooltip = tooltip;
	}

	/// <summary>
	/// Gets the full display text including icon if present
	/// </summary>
	public readonly string GetDisplayText()
	{
		return string.IsNullOrEmpty(Icon) ? Text : $"{Icon} {Text}";
	}
}

/// <summary>
/// Factory methods for common game context buttons
/// Provides convenient creation of standard button configurations
/// </summary>
public static class GameContextButton
{
	/// <summary>
	/// Factory method for common "Return to Menu" button
	/// </summary>
	public static ContextButtonData CreateReturnToMenuButton(Action onReturnToMenu)
	{
		return new ContextButtonData("Return to Menu", onReturnToMenu, "🏠", true, "Return to the main menu");
	}

	/// <summary>
	/// Factory method for common "Pause Game" button
	/// </summary>
	public static ContextButtonData CreatePauseButton(Action onPause)
	{
		return new ContextButtonData("Pause", onPause, "⏸️", true, "Pause the game");
	}

	/// <summary>
	/// Factory method for common "Resume Game" button
	/// </summary>
	public static ContextButtonData CreateResumeButton(Action onResume)
	{
		return new ContextButtonData("Resume", onResume, "▶️", true, "Resume the game");
	}

	/// <summary>
	/// Factory method for common "Restart Game" button
	/// </summary>
	public static ContextButtonData CreateRestartButton(Action onRestart)
	{
		return new ContextButtonData("Restart", onRestart, "🔄", true, "Restart the current game");
	}
}