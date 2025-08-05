using Godot;

/// <summary>
/// Enhanced pocket physics system using zone-based detection:
/// - Influence Zone: Gentle attraction forces guide pieces toward pocket
/// - Capture Zone: Position-based capture detection with speed/angle validation  
/// - Real physics simulation of piece falling through hole when center passes threshold
/// 
/// The system uses multiple detection zones to create realistic pocket behavior
/// where pieces are gradually drawn into pockets rather than instantly captured.
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
	[Export] public float PocketDepth { get; set; } = 0.7f; // How deep pieces must go to be pocketed (0.0-1.0)

	[ExportCategory("Detection Settings")]
	[Export] public bool RequireSlowEntry { get; set; } = true; // Pieces must be moving slowly to be pocketed
	[Export] public float MaxPocketingSpeed { get; set; } = 100.0f; // Legacy fallback - use physics config instead
	[Export] public float PocketingDelay { get; set; } = 0.1f; // Delay before confirming pocket
	[Export] public CarromPhysicsConfig PhysicsConfig { get; set; } // Physics configuration for enhanced pocket behavior

	// Detection components
	private CircleShape2D _detectionShape;
	private CollisionShape2D _collisionShape2D;
	
	// Consolidated piece state tracking
	private System.Collections.Generic.Dictionary<CarromPiece, PieceState> _pieceStates = new();
	
	// Piece state data structure
	private class PieceState
	{
		public PocketZone Zone { get; set; } = PocketZone.Outside;
		public Vector2 LastPosition { get; set; }
		public float EntryTime { get; set; }
		public bool HasMoved { get; set; } = true; // Start true to force initial zone calculation
	}

	// Zone definitions
	public enum PocketZone
	{
		Outside,     // Not in any pocket zone
		Influence,   // In influence zone - gradual attraction
		Capture      // In capture zone - hole detection active
	}

	public override void _Ready()
	{
		SetupPocketDetection();
		ConnectSignals();
	}

	/// <summary>
	/// Setup pocket detection area
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
	}

	/// <summary>
	/// Connect area signals
	/// </summary>
	private void ConnectSignals()
	{
		BodyEntered += OnBodyEntered;
		BodyExited += OnBodyExited;
	}

	/// <summary>
	/// Process enhanced pocket physics and zone-based interactions
	/// </summary>
	public override void _Process(double delta)
	{
		// Update zone tracking only for pieces that have moved
		UpdatePieceZones();
		
		// Clean up invalid pieces
		CleanupInvalidPieces();
	}

	/// <summary>
	/// Physics process for applying forces to pieces
	/// </summary>
	public override void _PhysicsProcess(double delta)
	{
		float deltaF = (float)delta;
		
		// Apply physics forces to pieces in zones
		foreach (var kvp in _pieceStates)
		{
			var piece = kvp.Key;
			if (!IsInstanceValid(piece)) 
				continue;

			var state = kvp.Value;
			ApplyZonePhysics(piece, state.Zone, deltaF);
		}
	}

	/// <summary>
	/// Handle piece entering pocket area
	/// </summary>
	private void OnBodyEntered(Node2D body)
	{
		if (body is CarromPiece piece)
		{
			// Initialize piece state
			_pieceStates[piece] = new PieceState
			{
				LastPosition = piece.GlobalPosition,
				EntryTime = (float)Time.GetUnixTimeFromSystem(),
				HasMoved = true // Force initial zone calculation
			};
			
			EmitSignal(SignalName.PieceEnteredPocketArea, piece);
		}
	}

	/// <summary>
	/// Handle piece leaving pocket area
	/// </summary>
	private void OnBodyExited(Node2D body)
	{
		if (body is CarromPiece piece)
		{
			_pieceStates.Remove(piece);
			EmitSignal(SignalName.PieceLeftPocketArea, piece);
		}
	}

	/// <summary>
	/// Check if piece can start the pocketing process
	/// </summary>
	private bool CanStartPocketing(CarromPiece piece)
	{
		// Don't pocket pieces that are moving too fast
		if (RequireSlowEntry && piece.GetSpeed() > GetMaxPocketingSpeed())
		{
			return false;
		}
		
		// Check if piece is deep enough in pocket
		return IsPieceInPocketDepth(piece);
	}

	/// <summary>
	/// Check if piece should be pocketed after timer expires
	/// </summary>
	private bool ShouldPocketPiece(CarromPiece piece)
	{
		// Verify piece is still in the pocket area
		if (!_pieceStates.ContainsKey(piece))
		{
			return false;
		}
		
		// Check piece is deep enough in pocket
		if (!IsPieceInPocketDepth(piece))
		{
			return false;
		}
		
		// Check speed requirement if enabled
		if (RequireSlowEntry && piece.GetSpeed() > GetMaxPocketingSpeed())
		{
			return false;
		}
		
		return true;
	}

	/// <summary>
	/// Check if piece is deep enough in the pocket
	/// </summary>
	private bool IsPieceInPocketDepth(CarromPiece piece)
	{
		float distanceToCenter = GlobalPosition.DistanceTo(piece.GlobalPosition);
		float requiredDepth = PocketRadius * PocketDepth;
		return distanceToCenter <= requiredDepth;
	}

	/// <summary>
	/// Pocket the piece
	/// </summary>
	private void PocketPiece(CarromPiece piece)
	{
		// Pocketing is always allowed when pieces are in capture zone
		
		// Stop piece physics
		piece.ForceStop();
		
		// Hide piece with animation effect
		CreatePocketingEffect(piece);
		
		// Disable piece
		piece.PocketPiece();
		
		// Emit pocketing signal
		EmitSignal(SignalName.PiecePocketed, piece);
		
	}

	/// <summary>
	/// Create visual effect for pocketing (immediate, no animation)
	/// </summary>
	private void CreatePocketingEffect(CarromPiece piece)
	{
		// Immediate pocketing effect - no animations to avoid race conditions with reset
		piece.Scale = Vector2.Zero;
		piece.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
		piece.GlobalPosition = GlobalPosition;
	}

	/// <summary>
	/// Check if pocket contains a specific piece
	/// </summary>
	public bool ContainsPiece(CarromPiece piece)
	{
		return _pieceStates.ContainsKey(piece);
	}

	/// <summary>
	/// Get all pieces currently in pocket area
	/// </summary>
	public CarromPiece[] GetPiecesInPocket()
	{
		var pieces = new System.Collections.Generic.List<CarromPiece>();
		foreach (var piece in _pieceStates.Keys)
		{
			if (GodotObject.IsInstanceValid(piece))
			{
				pieces.Add(piece);
			}
		}
		return pieces.ToArray();
	}

	/// <summary>
	/// Get count of pieces in pocket
	/// </summary>
	public int GetPieceCount()
	{
		int count = 0;
		foreach (var piece in _pieceStates.Keys)
		{
			if (GodotObject.IsInstanceValid(piece))
			{
				count++;
			}
		}
		return count;
	}


	/// <summary>
	/// Clear all pieces from pocket (for game reset)
	/// </summary>
	public void ClearPocket()
	{
		_pieceStates.Clear();
	}

	/// <summary>
	/// Get pocket position for external use
	/// </summary>
	public Vector2 GetPocketPosition()
	{
		return GlobalPosition;
	}

	/// <summary>
	/// Check if pocket is active
	/// </summary>
	public bool IsActive()
	{
		return Monitoring;
	}

	/// <summary>
	/// Enable or disable pocket detection
	/// </summary>
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

	/// <summary>
	/// Get influence zone radius
	/// </summary>
	private float GetInfluenceZoneRadius()
	{
		if (PhysicsConfig != null)
			return PocketRadius * PhysicsConfig.PocketInfluenceZoneMultiplier;
		return PocketRadius * 1.8f; // Fallback
	}

	/// <summary>
	/// Get hole zone radius for capture detection
	/// </summary>
	private float GetHoleZoneRadius()
	{
		if (PhysicsConfig != null)
			return PocketRadius * PhysicsConfig.PocketHoleZoneMultiplier;
		return PocketRadius * 0.75f; // Fallback
	}

	/// <summary>
	/// Determine which zone a piece is in based on distance to pocket center
	/// </summary>
	private PocketZone DeterminePieceZone(CarromPiece piece)
	{
		float distanceToCenter = GlobalPosition.DistanceTo(piece.GlobalPosition);
		
		if (distanceToCenter <= GetHoleZoneRadius())
			return PocketZone.Capture;
			
		return distanceToCenter <= GetInfluenceZoneRadius() ? PocketZone.Influence : PocketZone.Outside;
	}

	/// <summary>
	/// Update zone tracking only for pieces that have moved
	/// </summary>
	private void UpdatePieceZones()
	{
		var keysToRemove = new System.Collections.Generic.List<CarromPiece>();
		
		foreach (var kvp in _pieceStates)
		{
			var piece = kvp.Key;
			var state = kvp.Value;
			if (!IsInstanceValid(piece))
			{
				keysToRemove.Add(piece);
				continue;
			}
			
			// Check if piece has moved significantly
			var currentPosition = piece.GlobalPosition;
			var movementThreshold = 1.0f; // Minimum movement to trigger zone recalculation
			bool hasMoved = state.HasMoved || currentPosition.DistanceTo(state.LastPosition) > movementThreshold;
			
			if (hasMoved)
			{
				var newZone = DeterminePieceZone(piece);
				var oldZone = state.Zone;
				
				if (newZone != oldZone)
				{
					OnPieceZoneChanged(piece, oldZone, newZone);
				}
				
				// Update state
				state.Zone = newZone;
				state.LastPosition = currentPosition;
				state.HasMoved = false; // Reset movement flag
			}
		}
		
		// Clean up removed pieces
		foreach (var piece in keysToRemove)
		{
			_pieceStates.Remove(piece);
		}
	}

	/// <summary>
	/// Handle piece zone transitions
	/// </summary>
	private void OnPieceZoneChanged(CarromPiece piece, PocketZone oldZone, PocketZone newZone)
	{
		// Handle entry to capture zone
		if (newZone == PocketZone.Capture)
		{
			bool shouldCapture = ShouldCaptureImmediately(piece);
			
			if (shouldCapture)
			{
				PocketPiece(piece);
			}
		}
	}

	/// <summary>
	/// Check if piece should be captured immediately when entering capture zone
	/// </summary>
	private bool ShouldCaptureImmediately(CarromPiece piece)
	{
		float pieceSpeed = piece.GetSpeed();
		
		if (PhysicsConfig != null)
		{
			float captureChance = PhysicsConfig.CalculatePocketCaptureChance(pieceSpeed);
			Vector2 pocketDirection = (GlobalPosition - piece.GlobalPosition).Normalized();
			bool validAngle = PhysicsConfig.IsValidPocketApproachAngle(piece.LinearVelocity, pocketDirection);
			
			return validAngle && (captureChance >= 1.0f || GD.Randf() < captureChance);
		}
		
		// Fallback logic
		return pieceSpeed < GetMaxPocketingSpeed();
	}

	/// <summary>
	/// Apply physics forces based on piece zone
	/// </summary>
	private void ApplyZonePhysics(CarromPiece piece, PocketZone zone, float delta)
	{
		if (PhysicsConfig == null) return;
		
		Vector2 toPocketCenter = GlobalPosition - piece.GlobalPosition;
		float distanceToCenter = toPocketCenter.Length();
		
		switch (zone)
		{
			case PocketZone.Influence:
				ApplyInfluenceForces(piece, toPocketCenter, distanceToCenter, delta);
				break;
				
			case PocketZone.Capture:
				ApplyHolePhysics(piece, toPocketCenter, distanceToCenter, delta);
				break;
		}
	}

	/// <summary>
	/// Apply gentle attraction forces in influence zone
	/// </summary>
	private void ApplyInfluenceForces(CarromPiece piece, Vector2 toPocketCenter, float distanceToCenter, float delta)
	{
		if (distanceToCenter < 1.0f) 
			return;
		
		float forceStrength = PhysicsConfig.CalculatePocketRadialForce(distanceToCenter, PocketRadius);
		Vector2 attractionForce = toPocketCenter.Normalized() * forceStrength;
		
		piece.ApplyForce(attractionForce * delta);
		
		// Apply slight friction to slow pieces down
		Vector2 frictionForce = -piece.LinearVelocity * PhysicsConfig.PocketFrictionMultiplier * 0.5f;
		piece.ApplyForce(frictionForce * delta);
	}


	/// <summary>
	/// Apply hole zone physics - position-based capture detection
	/// </summary>
	private void ApplyHolePhysics(CarromPiece piece, Vector2 toPocketCenter, float distanceToCenter, float delta)
	{
		// Check if piece should be captured in the hole
		if (ShouldCaptureInHole(piece))
		{
			PocketPiece(piece);
			return;
		}
		
		// Apply attraction forces to guide piece toward hole center
		if (distanceToCenter >= 1.0f)
		{
			float forceStrength = PhysicsConfig.CalculatePocketRadialForce(distanceToCenter, PocketRadius) * 3.0f;
			Vector2 attractionForce = toPocketCenter.Normalized() * forceStrength;
			piece.ApplyForce(attractionForce * delta);
			
			// High friction to help pieces settle
			Vector2 frictionForce = -piece.LinearVelocity * PhysicsConfig.PocketFrictionMultiplier * 2.0f;
			piece.ApplyForce(frictionForce * delta);
		}
	}
	
	/// <summary>
	/// Check if piece should be captured in hole based on position and physics
	/// </summary>
	private bool ShouldCaptureInHole(CarromPiece piece)
	{
		float distanceToCenter = GlobalPosition.DistanceTo(piece.GlobalPosition);
		float pieceRadius = PhysicsConfig?.GetRadiusForPieceType(piece.Type) ?? 12.0f;
		
		// Real carrom physics: piece falls through when its center gets close enough to hole edge
		float holeRadius = PocketRadius; // 4.45cm diameter from real carrom specs
		float captureThreshold = holeRadius - pieceRadius;
		
		// Basic position check
		if (distanceToCenter > captureThreshold)
			return false;
			
		// Validate approach angle - pieces moving away shouldn't fall in
		if (PhysicsConfig != null)
		{
			Vector2 pocketDirection = (GlobalPosition - piece.GlobalPosition).Normalized();
			bool validAngle = PhysicsConfig.IsValidPocketApproachAngle(piece.LinearVelocity, pocketDirection);
			if (!validAngle)
				return false;
		}
		
		// Check speed requirements
		float pieceSpeed = piece.GetSpeed();
		return pieceSpeed <= GetMaxPocketingSpeed();
	}




	/// <summary>
	/// Get max pocketing speed from physics config or fallback
	/// </summary>
	private float GetMaxPocketingSpeed()
	{
		if (PhysicsConfig != null)
			return PhysicsConfig.PocketSlowCaptureSpeed;
		return MaxPocketingSpeed; // Legacy fallback
	}

	/// <summary>
	/// Clean up invalid piece references
	/// </summary>
	private void CleanupInvalidPieces()
	{
		var keysToRemove = new System.Collections.Generic.List<CarromPiece>();
		foreach (var piece in _pieceStates.Keys)
		{
			if (!IsInstanceValid(piece))
			{
				keysToRemove.Add(piece);
			}
		}
		
		foreach (var piece in keysToRemove)
		{
			_pieceStates.Remove(piece);
		}
	}

	public override void _ExitTree()
	{
		base._ExitTree();
		
		if (IsInstanceValid(this))
		{
			BodyEntered -= OnBodyEntered;
			BodyExited -= OnBodyExited;
		}
	}
}