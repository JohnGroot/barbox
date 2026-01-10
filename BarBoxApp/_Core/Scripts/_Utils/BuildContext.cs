using Godot;

namespace BarBox.Core.Gameplay;

/// <summary>
/// Cached build context detection. Values are immutable at runtime.
/// </summary>
public static class BuildContext
{
	private const string FEATURE_EDITOR = "editor";
	private const string FEATURE_STANDALONE = "standalone";

	// Lazy initialization - cached on first access
	private static bool? _isLaunchedFromEditor;
	private static bool? _isExportedBuild;
	private static bool? _isEditorToolContext;

	/// <summary>
	/// True when launched from Godot editor (F5 play).
	/// Includes both editor play mode and debug builds launched from editor.
	/// </summary>
	public static bool IsLaunchedFromEditor =>
		_isLaunchedFromEditor ??= OS.HasFeature(FEATURE_EDITOR);

	/// <summary>
	/// True for all exported builds (debug or release).
	/// </summary>
	public static bool IsExportedBuild =>
		_isExportedBuild ??= OS.HasFeature(FEATURE_STANDALONE);

	/// <summary>
	/// True during @tool script execution in editor.
	/// </summary>
	public static bool IsEditorToolContext =>
		_isEditorToolContext ??= Engine.IsEditorHint();

	/// <summary>
	/// True for any development context (editor play or tool scripts).
	/// </summary>
	public static bool IsDevelopment => IsLaunchedFromEditor || IsEditorToolContext;

	/// <summary>
	/// True for production (exported standalone build).
	/// </summary>
	public static bool IsProduction => IsExportedBuild;

	/// <summary>
	/// True when credit system should be bypassed (any dev context).
	/// </summary>
	public static bool ShouldBypassCredits => IsDevelopment;

	/// <summary>
	/// Get a human-readable description of the current build context.
	/// </summary>
	public static string GetContextDescription()
	{
		if (IsEditorToolContext)
			return "Development: Editor tool script context";

		if (IsLaunchedFromEditor)
			return "Development: Launched from Godot editor";

		if (IsExportedBuild)
			return "Production: Exported standalone build";

		return "Unknown: Unable to determine build context";
	}
}
