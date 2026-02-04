using Godot;
using System.Collections.Generic;
using System.Linq;

namespace BarBox.Games.Carrom;

/// <summary>
/// Simplified pocket physics system using zone-based detection:
/// - Detection Zone: Area2D for entry/exit tracking
/// - Capture Zone: Position-based capture detection with approach validation  
/// - Simple physics simulation where pieces are captured when center crosses threshold
/// 
/// The system uses a simple two-zone approach for authentic carrom pocket behavior.
/// </summary>
[GlobalClass]
public partial class CarromPocket : Area2D
{
	[Signal] public delegate void PiecePocketedEventHandler(CarromPiece piece);
	[Signal] public delegate void PieceEnteredPocketAreaEventHandler(CarromPiece piece);
	[Signal] public delegate void PieceLeftPocketAreaEventHandler(CarromPiece piece);

	[ExportCategory("Pocket Settings")]
	[Export] public float PocketRadius { get; set; } = 30.0f;
	[Export] public int PocketIndex { get; set; } = 0;
	[Export] public float PocketDepth { get; set; } = 0.75f; // How deep pieces must go to be pocketed (0.0-1.0)

	[ExportCategory("Detection Settings")]

	// Detection components
	private CircleShape2D _detectionShape;
	private CollisionShape2D _collisionShape2D;
	
	// Simple piece presence tracking
	private System.Collections.Generic.HashSet<CarromPiece> _piecesInPocket = new();

	// GC OPTIMIZATION: Pre-allocated removal lists to avoid per-frame allocations
	private readonly List<CarromPiece> _piecesToRemoveCache = new(16);
	private readonly List<CarromPiece> _invalidPiecesCache = new(16);

	// Particle effect components
	private GpuParticles2D _pocketParticles;
	private float _particleTimer = 0.0f;
	private bool _particlesActive = false;
	private const float PARTICLE_DURATION = 1.5f;

	// Zone definitions
	public enum PocketZone
	{
		Outside,     // Not in any pocket zone
		Influence,   // In influence zone - gradual attraction
		Capture      // In capture zone - hole detection active
	}

	// PERFORMANCE OPTIMIZATION: Pre-calculated values for expensive operations
	private float _captureRadiusSquared;  // Cached for faster distance comparisons
	private float _holeZoneRadiusSquared; // Cached for faster zone calculations
	
	private const float INFLUENCE_ZONE_MULTIPLIER = 1.8f;
	private const float HOLE_ZONE_MULTIPLIER = 0.75f;

	public override void _Ready()
	{
		SetupPocketDetection();
		SetupParticles();
		ConnectSignals();
	}

	/// <summary>
	/// Setup pocket detection area
	/// PERFORMANCE: Pre-calculate squared radii for faster comparisons
	/// </summary>
	private void SetupPocketDetection()
	{
		Monitoring = true;
		
		// Create detection shape - use influence zone radius for initial detection
		float influenceRadius = GetInfluenceZoneRadius();
		_detectionShape = new CircleShape2D();
		_detectionShape.Radius = influenceRadius;
		
		_collisionShape2D = new CollisionShape2D();
		_collisionShape2D.Shape = _detectionShape;
		AddChild(_collisionShape2D);
		
		// Set collision layers for piece detection
		CollisionLayer = 0; // Pocket doesn't have collision
		CollisionMask = 1;  // Detects pieces on layer 1
		
		// PERFORMANCE: Pre-calculate squared radii to avoid expensive sqrt() in distance comparisons
		float captureRadius = PocketRadius * PocketDepth;
		_captureRadiusSquared = captureRadius * captureRadius;
		_holeZoneRadiusSquared = GetHoleZoneRadius() * GetHoleZoneRadius();
	}

	private void SetupParticles()
	{
		_pocketParticles = new GpuParticles2D();
		AddChild(_pocketParticles);
		
		// Configure particle emission
		_pocketParticles.Emitting = false;
		_pocketParticles.Amount = 20;
		_pocketParticles.Lifetime = 1.0f;
		_pocketParticles.OneShot = true;
		_pocketParticles.Explosiveness = 1.0f;
		
		// Create process material for particle behavior
		var processMaterial = new ParticleProcessMaterial();
		
		// Emission settings - circular burst from pocket center for top-down view
		processMaterial.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Point;
		processMaterial.Direction = Vector3.Zero; // No preferred direction - radial spread
		processMaterial.Spread = 45.0f; // Full 360-degree spread
		processMaterial.InitialVelocityMin = 80.0f;
		processMaterial.InitialVelocityMax = 150.0f;
		
		// No gravity for top-down board view
		processMaterial.Gravity = Vector3.Zero;
		
		// Use linear acceleration to push particles outward radially
		processMaterial.LinearAccelMin = -20.0f;
		processMaterial.LinearAccelMax = -50.0f; // Negative values slow particles over time
		
		// Damping to slow particles over time (simulates friction)
		processMaterial.DampingMin = 1.0f;
		processMaterial.DampingMax = 2.0f;
		
		// Size and scale settings
		processMaterial.ScaleMin = 0.3f;
		processMaterial.ScaleMax = 0.8f;
		
		_pocketParticles.ProcessMaterial = processMaterial;
	}

	private void ConnectSignals()
	{
		BodyEntered += OnBodyEntered;
		BodyExited += OnBodyExited;
	}

	/// <summary>
	/// Process enhanced pocket physics and zone-based interactions
	/// FIXED: Changed to _PhysicsProcess for proper synchronization with RigidBody2D pieces
	/// </summary>
	public override void _PhysicsProcess(double delta)
	{
		// Update zone tracking only for pieces that have moved
		UpdatePieceZones();
		
		// Update particle timer
		UpdateParticleTimer((float)delta);
		
		// Clean up invalid pieces
		CleanupInvalidPieces();
	}

	private void OnBodyEntered(Node2D body)
	{
		if (body is not CarromPiece piece)
			return;

		_piecesInPocket.Add(piece);

		EmitSignal(SignalName.PieceEnteredPocketArea, piece);
	}

	private void OnBodyExited(Node2D body)
	{
		if (body is CarromPiece piece)
		{
			_piecesInPocket.Remove(piece);
			EmitSignal(SignalName.PieceLeftPocketArea, piece);
		}
	}

	public bool ContainsPiece(CarromPiece piece)
	{
		return _piecesInPocket.Contains(piece);
	}

	public CarromPiece[] GetPiecesInPocket()
	{
		List<CarromPiece> pieces = [];
		foreach (var piece in _piecesInPocket)
		{
			if (GodotObject.IsInstanceValid(piece))
			{
				pieces.Add(piece);
			}
		}
		return pieces.ToArray();
	}

	public int GetPieceCount()
	{
		int count = 0;
		foreach (var piece in _piecesInPocket)
		{
			if (GodotObject.IsInstanceValid(piece))
			{
				count++;
			}
		}
		return count;
	}


	public void ClearPocket()
	{
		_piecesInPocket.Clear();
	}

	public Vector2 GetPocketPosition()
	{
		return GlobalPosition;
	}

	public bool IsActive()
	{
		return Monitoring;
	}

	public void SetActive(bool active)
	{
		Monitoring = active;
		
		if (!active)
		{
			ClearPocket();
		}
	}



	// ================================================================
	// ENHANCED PHYSICS METHODS
	// ================================================================

	private float GetInfluenceZoneRadius()
	{
		return PocketRadius * INFLUENCE_ZONE_MULTIPLIER;
	}

	private float GetHoleZoneRadius()
	{
		return PocketRadius * HOLE_ZONE_MULTIPLIER;
	}


	/// <summary>
	/// Update zone tracking - simplified to check capture opportunities with optimized calculations
	/// GC OPTIMIZATION: Iterate HashSet directly and reuse cached removal list
	/// </summary>
	private void UpdatePieceZones()
	{
		_piecesToRemoveCache.Clear();

		// GC OPTIMIZATION: Iterate HashSet directly instead of ToArray()
		foreach (var piece in _piecesInPocket)
		{
			if (!IsInstanceValid(piece))
			{
				_piecesToRemoveCache.Add(piece);
				continue;
			}

			// PERFORMANCE: Use squared distance to avoid expensive sqrt() operation
			Vector2 offsetVector = piece.GlobalPosition - GlobalPosition;
			float distanceSquared = offsetVector.LengthSquared();

			// Check if piece is in hole zone using pre-calculated squared radius
			if (distanceSquared > _holeZoneRadiusSquared)
				continue;

			bool shouldCapture = ShouldCapturePiece(piece, distanceSquared);
			if (!shouldCapture)
				continue;

			// Let CarromPiece handle its own pocketing
			piece.ForceStop();
			piece.PocketPiece();

			// Apply simple visual effect
			piece.Scale = Vector2.Zero;
			piece.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
			piece.GlobalPosition = GlobalPosition;

			// DEBUG: Log piece pocketing in pocket
			GD.Print($"[DEBUG] Pocket {PocketIndex} capturing piece {piece.Type} at {piece.GlobalPosition}");

			// FIXED: Use deferred signal emission to prevent race conditions
			CallDeferred(MethodName._EmitPiecePocketed, piece);
			_piecesToRemoveCache.Add(piece);

			// Trigger particle effect with piece color
			TriggerPocketParticles(piece.PieceColor);
		}

		// Clean up removed pieces using cached list
		foreach (var piece in _piecesToRemoveCache)
		{
			_piecesInPocket.Remove(piece);
		}
	}


	/// <summary>
	/// Check if piece should be captured - simplified overlap-based detection
	/// </summary>
	private bool ShouldCapturePiece(CarromPiece piece, float distanceSquared)
	{
		// Check if piece is in pocket area
		if (!_piecesInPocket.Contains(piece))
		{
			return false;
		}
		
		// Simple overlap check - capture if piece center is within capture radius
		return distanceSquared <= _captureRadiusSquared;
	}




	





	/// <summary>
	/// GC OPTIMIZATION: Reuse cached list instead of allocating new one each frame
	/// </summary>
	private void CleanupInvalidPieces()
	{
		_invalidPiecesCache.Clear();
		foreach (var piece in _piecesInPocket)
		{
			if (!IsInstanceValid(piece))
			{
				_invalidPiecesCache.Add(piece);
			}
		}

		foreach (var piece in _invalidPiecesCache)
		{
			_piecesInPocket.Remove(piece);
		}
	}

	private void TriggerPocketParticles(Color pieceColor)
	{
		if (_pocketParticles == null)
			return;

		// Set particle color to match the entering piece
		_pocketParticles.Modulate = pieceColor;
		
		// Start emitting particles
		_pocketParticles.Emitting = true;
		_pocketParticles.Restart();
		
		// Reset and start the timer
		_particleTimer = 0.0f;
		_particlesActive = true;
	}

	private void UpdateParticleTimer(float delta)
	{
		if (!_particlesActive)
			return;

		_particleTimer += delta;
		
		if (_particleTimer >= PARTICLE_DURATION)
		{
			// Deactivate particles after duration
			if (_pocketParticles != null)
			{
				_pocketParticles.Emitting = false;
			}
			_particlesActive = false;
			_particleTimer = 0.0f;
		}
	}

	/// <summary>
	/// Deferred signal emission method to prevent race conditions
	/// </summary>
	private void _EmitPiecePocketed(CarromPiece piece)
	{
		EmitSignal(SignalName.PiecePocketed, piece);
	}

	public override void _ExitTree()
	{
		base._ExitTree();
		
		if (IsInstanceValid(this))
		{
			BodyEntered -= OnBodyEntered;
			BodyExited -= OnBodyExited;
		}
		
		// Clean up particle system
		if (_pocketParticles != null)
		{
			_pocketParticles.Emitting = false;
			_particlesActive = false;
		}
	}
}