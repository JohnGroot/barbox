using Godot;
using System.Collections.Generic;
using System.Linq;

namespace BarBox.Games.Racing;

/// <summary>
/// Manages racing zones, tracks which bodies are in which zones,
/// and calculates combined modifiers for car behavior
/// </summary>
[GlobalClass]
public partial class RacingZoneManager : Node
{
	// ================================================================
	// SIGNALS
	// ================================================================

	[Signal] public delegate void ZoneEnteredEventHandler(Node2D body, RacingZone zone);
	[Signal] public delegate void ZoneExitedEventHandler(Node2D body, RacingZone zone);
	[Signal] public delegate void FrictionlessStartedEventHandler(Node2D body, float duration);
	[Signal] public delegate void FrictionlessEndedEventHandler(Node2D body);

	// ================================================================
	// PRIVATE FIELDS
	// ================================================================

	private readonly List<RacingZone> _allZones = [];
	private readonly Dictionary<Node2D, HashSet<RacingZone>> _bodyZones = new();
	private readonly Dictionary<Node2D, FrictionlessState> _frictionlessStates = new();

	// Cached collision shape references (GC optimization: avoids string-based scene tree traversal per zone check)
	private readonly Dictionary<RacingZone, CollisionPolygon2D> _zoneCollisionPolygons = new();
	private readonly Dictionary<RacingZone, CollisionShape2D> _zoneCollisionShapes = new();

	// Reusable list for frictionless effect cleanup (GC optimization: avoids per-frame allocation)
	private readonly List<Node2D> _expiredBodies = [];

	// ================================================================
	// FRICTIONLESS STATE
	// ================================================================

	private class FrictionlessState
	{
		public bool IsActive { get; set; }
		public float TimeRemaining { get; set; }
		public Vector2 LockedVelocity { get; set; }
		public float LockedRotation { get; set; }
		public RacingZone SourceZone { get; set; }
	}

	// ================================================================
	// ZONE REGISTRATION
	// ================================================================

	public void RegisterZone(RacingZone zone)
	{
		if (zone == null || _allZones.Contains(zone))
			return;

		_allZones.Add(zone);

		// Cache collision shape references (avoids per-frame scene tree traversal)
		var collisionPolygon = zone.GetNodeOrNull<CollisionPolygon2D>("CollisionPolygon2D");
		if (collisionPolygon != null)
		{
			_zoneCollisionPolygons[zone] = collisionPolygon;
		}
		else
		{
			var collisionShape = zone.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
			if (collisionShape != null)
			{
				_zoneCollisionShapes[zone] = collisionShape;
			}
		}

		// Connect zone signals
		zone.BodyEntered += body => OnZoneBodyEntered(body, zone);
		zone.BodyExited += body => OnZoneBodyExited(body, zone);
	}

	public void UnregisterZone(RacingZone zone)
	{
		if (zone == null)
			return;

		_allZones.Remove(zone);
		_zoneCollisionPolygons.Remove(zone);
		_zoneCollisionShapes.Remove(zone);

		// Remove this zone from all body tracking
		foreach (var bodyZones in _bodyZones.Values)
		{
			bodyZones.Remove(zone);
		}
	}

	public void ClearAllZones()
	{
		_allZones.Clear();
		_bodyZones.Clear();
		_frictionlessStates.Clear();
		_zoneCollisionPolygons.Clear();
		_zoneCollisionShapes.Clear();
	}

	// ================================================================
	// ZONE EVENTS
	// ================================================================

	private void OnZoneBodyEntered(Node2D body, RacingZone zone)
	{
		if (!_bodyZones.TryGetValue(body, out var zones))
		{
			zones = [];
			_bodyZones[body] = zones;
		}

		zones.Add(zone);

		// Handle frictionless zone entry
		if (zone.Type == ZoneType.Frictionless && zone.BlocksInput)
		{
			StartFrictionlessEffect(body, zone);
		}

		EmitSignal(SignalName.ZoneEntered, body, zone);
	}

	private void OnZoneBodyExited(Node2D body, RacingZone zone)
	{
		if (_bodyZones.TryGetValue(body, out var zones))
		{
			zones.Remove(zone);

			if (zones.Count == 0)
			{
				_bodyZones.Remove(body);
			}
		}

		EmitSignal(SignalName.ZoneExited, body, zone);
	}

	// ================================================================
	// FRICTIONLESS EFFECT MANAGEMENT
	// ================================================================

	private void StartFrictionlessEffect(Node2D body, RacingZone zone)
	{
		// Get current velocity from the car body
		Vector2 velocity = Vector2.Zero;
		float rotation = 0f;

		if (body is CharacterBody2D charBody)
		{
			velocity = charBody.Velocity;
			rotation = charBody.Rotation;
		}
		else if (body.HasMethod("GetVelocity"))
		{
			velocity = (Vector2)body.Call("GetVelocity");
			rotation = body.Rotation;
		}

		_frictionlessStates[body] = new FrictionlessState
		{
			IsActive = true,
			TimeRemaining = zone.Duration > 0 ? zone.Duration : float.MaxValue,
			LockedVelocity = velocity,
			LockedRotation = rotation,
			SourceZone = zone
		};

		EmitSignal(SignalName.FrictionlessStarted, body, zone.Duration);
	}

	public void UpdateFrictionlessEffects(float delta)
	{
		_expiredBodies.Clear();

		foreach (var kvp in _frictionlessStates)
		{
			var body = kvp.Key;
			var state = kvp.Value;

			if (!state.IsActive)
				continue;

			// Check if body is still valid
			if (!GodotObject.IsInstanceValid(body))
			{
				_expiredBodies.Add(body);
				continue;
			}

			state.TimeRemaining -= delta;

			// Check if effect has expired
			if (state.TimeRemaining <= 0)
			{
				_expiredBodies.Add(body);
			}
		}

		// Clean up expired effects
		foreach (var body in _expiredBodies)
		{
			_frictionlessStates.Remove(body);
			if (GodotObject.IsInstanceValid(body))
			{
				EmitSignal(SignalName.FrictionlessEnded, body);
			}
		}
	}

	public (bool isActive, float timeRemaining, Vector2 lockedVelocity, float lockedRotation) GetFrictionlessState(Node2D body)
	{
		if (_frictionlessStates.TryGetValue(body, out var state) && state.IsActive)
		{
			return (true, state.TimeRemaining, state.LockedVelocity, state.LockedRotation);
		}
		return (false, 0f, Vector2.Zero, 0f);
	}

	public bool IsInFrictionlessState(Node2D body)
	{
		return _frictionlessStates.TryGetValue(body, out var state) && state.IsActive;
	}

	// ================================================================
	// MODIFIER CALCULATIONS
	// ================================================================

	/// <summary>
	/// Get combined speed modifier for all zones the body is in
	/// Modifiers are multiplicative (0.5 * 2.0 = 1.0)
	/// </summary>
	public float GetCombinedSpeedModifier(Node2D body)
	{
		if (!_bodyZones.TryGetValue(body, out var zones) || zones.Count == 0)
			return 1.0f;

		float modifier = 1.0f;
		foreach (var zone in zones)
		{
			modifier *= zone.SpeedModifier;
		}
		return modifier;
	}

	public float GetCombinedAccelerationModifier(Node2D body)
	{
		if (!_bodyZones.TryGetValue(body, out var zones) || zones.Count == 0)
			return 1.0f;

		float modifier = 1.0f;
		foreach (var zone in zones)
		{
			modifier *= zone.AccelerationModifier;
		}
		return modifier;
	}

	public float GetCombinedTurnModifier(Node2D body)
	{
		if (!_bodyZones.TryGetValue(body, out var zones) || zones.Count == 0)
			return 1.0f;

		float modifier = 1.0f;
		foreach (var zone in zones)
		{
			modifier *= zone.TurnModifier;
		}
		return modifier;
	}

	public bool IsInputBlocked(Node2D body)
	{
		// First check if in active frictionless state (with duration)
		if (IsInFrictionlessState(body))
			return true;

		// Then check if in any blocking zone
		if (!_bodyZones.TryGetValue(body, out var zones))
			return false;

		foreach (var zone in zones)
		{
			if (zone.BlocksInput)
				return true;
		}
		return false;
	}

	// ================================================================
	// POSITION-BASED QUERIES (for kerb detection)
	// ================================================================

	/// <summary>
	/// Check if a position is inside any kerb zone
	/// Used by track validation to extend on-track boundaries
	/// </summary>
	public bool IsInKerbZone(Vector2 position)
	{
		foreach (var zone in _allZones)
		{
			if (zone.Type != ZoneType.Kerb)
				continue;

			if (!GodotObject.IsInstanceValid(zone))
				continue;

			if (IsPointInZone(position, zone))
				return true;
		}
		return false;
	}

	/// <summary>
	/// Get all zones that contain the given position
	/// </summary>
	public List<RacingZone> GetZonesAtPosition(Vector2 position)
	{
		var result = new List<RacingZone>();

		foreach (var zone in _allZones)
		{
			if (!GodotObject.IsInstanceValid(zone))
				continue;

			if (IsPointInZone(position, zone))
				result.Add(zone);
		}

		return result;
	}

	private bool IsPointInZone(Vector2 globalPosition, RacingZone zone)
	{
		// Convert global position to zone's local space
		var localPosition = zone.ToLocal(globalPosition);

		// Use cached collision polygon reference (avoids string-based scene tree traversal)
		if (_zoneCollisionPolygons.TryGetValue(zone, out var collisionPolygon))
		{
			return Geometry2D.IsPointInPolygon(localPosition, collisionPolygon.Polygon);
		}

		// Use cached CollisionShape2D reference
		if (_zoneCollisionShapes.TryGetValue(zone, out var collisionShape2D))
		{
			if (collisionShape2D.Shape is RectangleShape2D rectShape)
			{
				var halfSize = rectShape.Size / 2;
				return Mathf.Abs(localPosition.X) <= halfSize.X &&
					   Mathf.Abs(localPosition.Y) <= halfSize.Y;
			}

			if (collisionShape2D.Shape is CircleShape2D circleShape)
			{
				return localPosition.Length() <= circleShape.Radius;
			}
		}

		return false;
	}

	// ================================================================
	// UTILITY METHODS
	// ================================================================

	/// <summary>
	/// Get all zones a body is currently in
	/// </summary>
	public IReadOnlyList<RacingZone> GetZonesForBody(Node2D body)
	{
		if (_bodyZones.TryGetValue(body, out var zones))
		{
			return zones.ToList();
		}
		return [];
	}

	/// <summary>
	/// Check if a body is in any zone
	/// </summary>
	public bool IsInAnyZone(Node2D body)
	{
		return _bodyZones.TryGetValue(body, out var zones) && zones.Count > 0;
	}

	/// <summary>
	/// Check if a body is in a specific type of zone
	/// </summary>
	public bool IsInZoneType(Node2D body, ZoneType type)
	{
		if (!_bodyZones.TryGetValue(body, out var zones))
			return false;

		foreach (var zone in zones)
		{
			if (zone.Type == type)
				return true;
		}
		return false;
	}

	/// <summary>
	/// Get count of registered zones
	/// </summary>
	public int ZoneCount => _allZones.Count;

	/// <summary>
	/// Get all registered zones (read-only)
	/// </summary>
	public IReadOnlyList<RacingZone> AllZones => _allZones;
}
