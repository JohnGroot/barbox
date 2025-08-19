using Godot;

/// <summary>
/// Static constants for common Tween properties to reduce GC allocation.
/// Instead of creating new StringName instances each time, use these cached constants.
/// </summary>
public static class TweenConstants
{
	// Common property names for TweenProperty() calls
	public static readonly StringName Position = "position";
	public static readonly StringName GlobalPosition = "global_position";
	public static readonly StringName Rotation = "rotation";
	public static readonly StringName Scale = "scale";
	public static readonly StringName Modulate = "modulate";
	public static readonly StringName ModulateAlpha = "modulate:a";
	
	// Transform properties
	public static readonly StringName Transform = "transform";
	public static readonly StringName Basis = "basis";
	
	// Size and region properties
	public static readonly StringName Size = "size";
	public static readonly StringName CustomMinimumSize = "custom_minimum_size";
	
	// Anchor and margin properties (Control nodes)
	public static readonly StringName AnchorLeft = "anchor_left";
	public static readonly StringName AnchorRight = "anchor_right";
	public static readonly StringName AnchorTop = "anchor_top";
	public static readonly StringName AnchorBottom = "anchor_bottom";
	
	// Camera properties
	public static readonly StringName Zoom = "zoom";
	public static readonly StringName Offset = "offset";
	
	// Audio properties
	public static readonly StringName VolumeDb = "volume_db";
	public static readonly StringName PitchScale = "pitch_scale";
	
	// Animation player
	public static readonly StringName CurrentAnimationPosition = "current_animation_position";
	public static readonly StringName SpeedScale = "speed_scale";
	
	// CanvasItem properties
	public static readonly StringName Visible = "visible";
	public static readonly StringName ZIndex = "z_index";
	
	// Material properties
	public static readonly StringName AlbedoColor = "albedo_color";
	public static readonly StringName Metallic = "metallic";
	public static readonly StringName Roughness = "roughness";
	
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