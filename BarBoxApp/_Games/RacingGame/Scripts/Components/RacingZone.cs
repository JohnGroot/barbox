using Godot;

namespace BarBox.Games.Racing;

/// <summary>
/// Base Area2D component for racing zones that affect car behavior.
/// Place in track scenes with a CollisionPolygon2D child to define the zone area.
/// </summary>
[GlobalClass]
public partial class RacingZone : Area2D, IRacingZone
{
	// ================================================================
	// EXPORT PROPERTIES - ZONE TYPE
	// ================================================================
	[ExportCategory("Zone Type")]
	[Export]
	public ZoneType Type { get; set; } = ZoneType.Slowdown;

	// ================================================================
	// EXPORT PROPERTIES - MODIFIERS
	// ================================================================
	[ExportCategory("Modifiers")]
	[Export(PropertyHint.Range, "0.1,3.0,0.1")]
	public float SpeedModifier { get; set; } = 1.0f;

	[Export(PropertyHint.Range, "0.1,3.0,0.1")]
	public float AccelerationModifier { get; set; } = 1.0f;

	[Export(PropertyHint.Range, "0.1,3.0,0.1")]
	public float TurnModifier { get; set; } = 1.0f;

	// ================================================================
	// EXPORT PROPERTIES - FRICTIONLESS SETTINGS
	// ================================================================
	[ExportCategory("Frictionless Settings")]
	[Export]
	public bool BlocksInput { get; set; } = false;

	[Export(PropertyHint.Range, "0.0,10.0,0.1")]
	public float Duration { get; set; } = 0.0f;

	// ================================================================
	// EXPORT PROPERTIES - VISUAL
	// ================================================================
	[ExportCategory("Visual")]
	[Export]
	public Color ZoneColor { get; set; } = RacingPalette.ZoneDefault;

	[Export]
	public Polygon2D VisualPolygon { get; set; }

	[Export]
	public bool ShowVisual { get; set; } = true;

	// ================================================================
	// LIFECYCLE
	// ================================================================
	public override void _Ready()
	{
		// Set up collision monitoring
		Monitorable = true;
		Monitoring = true;

		// Apply visual settings
		UpdateVisual();

		// Auto-register with zone manager if we can find it
		TryAutoRegister();
	}

	public override void _ExitTree()
	{
		// Unregister from zone manager
		TryAutoUnregister();
	}

	// ================================================================
	// VISUAL UPDATES
	// ================================================================
	private void UpdateVisual()
	{
		// Try to find visual polygon child
		VisualPolygon ??= GetNodeOrNull<Polygon2D>("VisualPolygon");

		if (VisualPolygon != null)
		{
			VisualPolygon.Color = ZoneColor;
			VisualPolygon.Visible = ShowVisual;

			// Sync with collision polygon if not set
			SyncVisualWithCollision();
		}
	}

	private void SyncVisualWithCollision()
	{
		if (VisualPolygon == null)
		{
			return;
		}

		// If visual has no polygon, copy from collision
		if (VisualPolygon.Polygon.Length == 0)
		{
			var collisionPolygon = GetNodeOrNull<CollisionPolygon2D>("CollisionPolygon2D");
			if (collisionPolygon != null && collisionPolygon.Polygon.Length > 0)
			{
				VisualPolygon.Polygon = collisionPolygon.Polygon;
			}
		}
	}

	// ================================================================
	// ZONE MANAGER REGISTRATION
	// ================================================================
	private void TryAutoRegister()
	{
		// Look for zone manager in parent hierarchy
		var parent = GetParent();
		while (parent != null)
		{
			var zoneManager = parent.GetNodeOrNull<RacingZoneManager>("RacingZoneManager");
			if (zoneManager != null)
			{
				zoneManager.RegisterZone(this);
				return;
			}

			parent = parent.GetParent();
		}
	}

	private void TryAutoUnregister()
	{
		var parent = GetParent();
		while (parent != null)
		{
			var zoneManager = parent.GetNodeOrNull<RacingZoneManager>("RacingZoneManager");
			if (zoneManager != null)
			{
				zoneManager.UnregisterZone(this);
				return;
			}

			parent = parent.GetParent();
		}
	}

	/// <summary>Hides the legacy Polygon2D visual once RacingTrackRenderer supplies its own.</summary>
	public void HideVisual()
	{
		if (VisualPolygon != null)
		{
			VisualPolygon.Visible = false;
		}
	}

	/// <summary>The zone's collision polygon in this node's own local space.</summary>
	public Vector2[] GetLocalPolygon()
	{
		var collisionPolygon = GetNodeOrNull<CollisionPolygon2D>("CollisionPolygon2D");
		return collisionPolygon?.Polygon ?? [];
	}

	// ================================================================
	// HELPER METHODS
	// ================================================================

	/// <summary>
	/// Check if a point is inside this zone
	/// </summary>
	public bool ContainsPoint(Vector2 globalPosition)
	{
		var localPosition = ToLocal(globalPosition);

		var collisionPolygon = GetNodeOrNull<CollisionPolygon2D>("CollisionPolygon2D");
		if (collisionPolygon != null)
		{
			return Geometry2D.IsPointInPolygon(localPosition, collisionPolygon.Polygon);
		}

		var collisionShape = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		if (collisionShape?.Shape is RectangleShape2D rectShape)
		{
			var halfSize = rectShape.Size / 2;
			return Mathf.Abs(localPosition.X) <= halfSize.X &&
				   Mathf.Abs(localPosition.Y) <= halfSize.Y;
		}

		if (collisionShape?.Shape is CircleShape2D circleShape)
		{
			return localPosition.Length() <= circleShape.Radius;
		}

		return false;
	}

	/// <summary>
	/// Set the collision polygon points
	/// </summary>
	public void SetPolygon(Vector2[] points)
	{
		var collisionPolygon = GetNodeOrNull<CollisionPolygon2D>("CollisionPolygon2D");
		if (collisionPolygon != null)
		{
			collisionPolygon.Polygon = points;
		}

		if (VisualPolygon != null)
		{
			VisualPolygon.Polygon = points;
		}
	}

	/// <summary>
	/// Create default zone configurations
	/// </summary>
	public static RacingZone CreateSlowdownZone()
	{
		var zone = new RacingZone
		{
			Type = ZoneType.Slowdown,
			SpeedModifier = 0.5f,
			AccelerationModifier = 0.7f,
			TurnModifier = 1.0f,
			BlocksInput = false,
			ZoneColor = RacingPalette.ZoneSlowdown,
		};
		return zone;
	}

	public static RacingZone CreateBoostZone()
	{
		var zone = new RacingZone
		{
			Type = ZoneType.Boost,
			SpeedModifier = 1.5f,
			AccelerationModifier = 1.5f,
			TurnModifier = 1.0f,
			BlocksInput = false,
			ZoneColor = RacingPalette.ZoneBoost,
		};
		return zone;
	}

	public static RacingZone CreateFrictionlessZone(float duration = 2.0f)
	{
		var zone = new RacingZone
		{
			Type = ZoneType.Frictionless,
			SpeedModifier = 1.0f,
			AccelerationModifier = 1.0f,
			TurnModifier = 1.0f,
			BlocksInput = true,
			Duration = duration,
			ZoneColor = RacingPalette.ZoneFrictionless,
		};
		return zone;
	}

	public static RacingZone CreateKerbZone()
	{
		var zone = new RacingZone
		{
			Type = ZoneType.Kerb,
			SpeedModifier = 1.0f,
			AccelerationModifier = 1.0f,
			TurnModifier = 1.0f,
			BlocksInput = false,
			ZoneColor = RacingPalette.ZoneKerb,
		};
		return zone;
	}

	// ================================================================
	// DEBUG
	// ================================================================
	public override string ToString()
	{
		return $"RacingZone({Type}, Speed:{SpeedModifier:F2}, Accel:{AccelerationModifier:F2}, Turn:{TurnModifier:F2})";
	}
}
