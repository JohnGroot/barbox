using Godot;

/// <summary>
/// Static constants for common Tween properties to reduce GC allocation.
/// Instead of creating new NodePath instances each time, use these cached constants.
/// </summary>
public static class TweenConstants
{
	// Common property names for TweenProperty() calls
	public static readonly NodePath Position = new("position");
	public static readonly NodePath GlobalPosition = new("global_position");
	public static readonly NodePath Rotation = new("rotation");
	public static readonly NodePath Scale = new("scale");
	public static readonly NodePath Modulate = new("modulate");
	public static readonly NodePath ModulateAlpha = new("modulate:a");

	// Transform properties
	public static readonly NodePath Transform = new("transform");
	public static readonly NodePath Basis = new("basis");

	// Size and region properties
	public static readonly NodePath Size = new("size");
	public static readonly NodePath CustomMinimumSize = new("custom_minimum_size");

	// Anchor and margin properties (Control nodes)
	public static readonly NodePath AnchorLeft = new("anchor_left");
	public static readonly NodePath AnchorRight = new("anchor_right");
	public static readonly NodePath AnchorTop = new("anchor_top");
	public static readonly NodePath AnchorBottom = new("anchor_bottom");

	// Camera properties
	public static readonly NodePath Zoom = new("zoom");
	public static readonly NodePath Offset = new("offset");

	// Audio properties
	public static readonly NodePath VolumeDb = new("volume_db");
	public static readonly NodePath PitchScale = new("pitch_scale");

	// Animation player
	public static readonly NodePath CurrentAnimationPosition = new("current_animation_position");
	public static readonly NodePath SpeedScale = new("speed_scale");

	// CanvasItem properties
	public static readonly NodePath Visible = new("visible");
	public static readonly NodePath ZIndex = new("z_index");

	// Material properties
	public static readonly NodePath AlbedoColor = new("albedo_color");
	public static readonly NodePath Metallic = new("metallic");
	public static readonly NodePath Roughness = new("roughness");

	// Common transition types (cached to avoid enum boxing)
	public const Tween.TransitionType TransSine = Tween.TransitionType.Sine;
	public const Tween.TransitionType TransQuint = Tween.TransitionType.Quint;
	public const Tween.TransitionType TransQuart = Tween.TransitionType.Quart;
	public const Tween.TransitionType TransQuad = Tween.TransitionType.Quad;
	public const Tween.TransitionType TransExpo = Tween.TransitionType.Expo;
	public const Tween.TransitionType TransElastic = Tween.TransitionType.Elastic;
	public const Tween.TransitionType TransCubic = Tween.TransitionType.Cubic;
	public const Tween.TransitionType TransCirc = Tween.TransitionType.Circ;
	public const Tween.TransitionType TransBounce = Tween.TransitionType.Bounce;
	public const Tween.TransitionType TransBack = Tween.TransitionType.Back;
	public const Tween.TransitionType TransLinear = Tween.TransitionType.Linear;

	// Common ease types (cached to avoid enum boxing)
	public const Tween.EaseType EaseIn = Tween.EaseType.In;
	public const Tween.EaseType EaseOut = Tween.EaseType.Out;
	public const Tween.EaseType EaseInOut = Tween.EaseType.InOut;
	public const Tween.EaseType EaseOutIn = Tween.EaseType.OutIn;

	// Common durations for consistency
	public const float QuickDuration = 0.1f;
	public const float FastDuration = 0.2f;
	public const float MediumDuration = 0.3f;
	public const float SlowDuration = 0.5f;
	public const float VerySlowDuration = 1.0f;
}
