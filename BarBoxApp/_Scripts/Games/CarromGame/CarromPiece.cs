using Godot;

/// <summary>
/// Carrom piece with realistic physics using RigidBody2D
/// </summary>
[GlobalClass]
public partial class CarromPiece : RigidBody2D
{
	[Signal] public delegate void PieceCollidedEventHandler(CarromPiece thisPiece, CarromPiece otherPiece, Vector2 impactForce);
	[Signal] public delegate void PieceStoppedEventHandler(CarromPiece piece);

	[ExportCategory("Piece Settings")]
	[Export] public PieceType Type { get; set; } = PieceType.White;
	[Export] public CarromPhysicsConfig PhysicsConfig { get; set; }

	[ExportCategory("Visual Settings")]
	[Export] public Color PieceColor { get; set; } = Colors.White;

	// Physics components
	private CircleShape2D _collisionShape;
	private CollisionShape2D _collisionShape2D;
	private Label _pieceLabel;

	// Movement tracking with hysteresis
	private Vector2 _lastVelocity = Vector2.Zero;
	private bool _wasMoving = false;
	private float _stoppedTimer = 0.0f;
	
	// Reset state management
	private bool _skipNextStoppedCheck = false;
	
	// Movement history tracking for meaningful velocity validation
	private bool _hasEverMoved = false;
	private float _maxVelocityAchieved = 0.0f;
	private double _creationTime = 0.0; // Track when piece was created for never-moved timeout
	
	// Powder-effect friction tracking
	private float _distanceTraveled = 0.0f; // Total distance traveled for powder friction calculation
	private Vector2 _lastPositionForFriction = Vector2.Zero; // Previous position for distance calculation
	private bool _powderFrictionActive = false; // Whether custom friction is currently being applied
	
	// Cached calculations for performance
	private float _cachedSpeed = 0.0f; // Cached current speed to avoid multiple Length() calls
	private float _cachedFrictionCoefficient = 0.0f; // Cached friction coefficient to avoid repeated calculations
	private bool _frictionCacheValid = false; // Whether cached friction values are valid

	// Physics limits - set by CarromGame.cs
	private float _minVelocityThreshold = 1.0f;
	private float _angularMinThreshold = 0.1f;
	private float _maxVelocityLimit = 2000.0f;
	private float _maxAngularVelocity = 50.0f;  
	private float _velocityAlertThreshold = 1800.0f;
	
	// Realistic physics constants - now using PhysicsConfig values
	
	// Velocity monitoring for tunneling protection validation (used for debugging)
	private float _maxSpeedAchieved = 0.0f;

	public override void _Ready()
	{
		ValidatePhysicsConfig();
		SetupPhysics();
		SetupCollisionShape();
		SetupVisual();
		SetupPhysicsMaterial();
		ConnectSignals();
		
		// Record creation time for never-moved timeout
		_creationTime = Time.GetUnixTimeFromSystem();
		
		// Initialize powder friction tracking
		_lastPositionForFriction = GlobalPosition;
	}

	/// <summary>
	/// Set physics limits from CarromGame
	/// </summary>
	public void SetPhysicsLimits(float minVelocityThreshold, float angularMinThreshold, 
		float maxVelocityLimit, float maxAngularVelocity, float velocityAlertThreshold)
	{
		_minVelocityThreshold = minVelocityThreshold;
		_angularMinThreshold = angularMinThreshold;
		_maxVelocityLimit = maxVelocityLimit;
		_maxAngularVelocity = maxAngularVelocity;
		_velocityAlertThreshold = velocityAlertThreshold;
	}

	/// <summary>
	/// Validate physics configuration
	/// </summary>
	private void ValidatePhysicsConfig()
	{
		if (PhysicsConfig == null)
		{
			GD.PrintErr($"[CarromPiece] No PhysicsConfig provided for piece {Type}. Physics behavior may be incorrect.");
		}
	}

	/// <summary>
	/// Setup physics properties
	/// </summary>
	private void SetupPhysics()
	{
		GravityScale = 0.0f; // No gravity for top-down view
		
		// Use either powder-effect friction OR built-in damping, not both
		if (PhysicsConfig.EnablePowderEffectFriction)
		{
			LinearDamp = 0.0f; // Disable built-in damping when using custom powder friction
		}
		else
		{
			LinearDamp = PhysicsConfig.LinearDamping; // Use built-in damping when powder friction disabled
		}
		
		AngularDamp = PhysicsConfig.AngularDamping;
		ContactMonitor = PhysicsConfig.ContactMonitor;
		MaxContactsReported = PhysicsConfig.MaxContactsReported;
		
		// Enable Continuous Collision Detection for high-speed collision accuracy
		// CastShape provides better collision detection for circular pieces than CastRay
		ContinuousCd = CcdMode.CastShape;
		
		// Set mass based on piece type
		Mass = PhysicsConfig.GetMassForPieceType(Type);
	}

	/// <summary>
	/// Setup collision shape (get existing from scene and update)
	/// </summary>
	private void SetupCollisionShape()
	{
		_collisionShape2D = GetNode<CollisionShape2D>("CollisionShape2D");
		
		if (_collisionShape2D?.Shape is CircleShape2D existingShape)
		{
			_collisionShape = existingShape;
			// Use collision radius with safety margin to prevent tunneling
			_collisionShape.Radius = PhysicsConfig.GetCollisionRadiusForPieceType(Type);
		}
		else
		{
			// Fallback: create new collision shape if not found
			_collisionShape = new CircleShape2D();
			_collisionShape.Radius = PhysicsConfig.GetCollisionRadiusForPieceType(Type);
			
			if (_collisionShape2D == null)
			{
				_collisionShape2D = new CollisionShape2D();
				AddChild(_collisionShape2D);
			}
			_collisionShape2D.Shape = _collisionShape;
		}
	}

	/// <summary>
	/// Setup visual representation (get existing nodes from scene)
	/// </summary>
	private void SetupVisual()
	{
		// Get visual nodes from scene (if they exist)
		_pieceLabel = GetNode<Label>("PieceLabel");

		if (_pieceLabel != null)
		{
			_pieceLabel.Text = GetPieceText();
			// Use visual radius for label sizing (not collision radius)
			float radius = PhysicsConfig.GetRadiusForPieceType(Type);
			float size = radius * 2;
			_pieceLabel.Size = new Vector2(size, size);
			_pieceLabel.Position = new Vector2(-radius, -radius);
			_pieceLabel.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
		}
	}

	/// <summary>
	/// Setup physics material
	/// </summary>
	private void SetupPhysicsMaterial()
	{
		PhysicsMaterialOverride = PhysicsConfig.CreatePieceMaterial();
	}

	/// <summary>
	/// Connect physics signals
	/// </summary>
	private void ConnectSignals()
	{
		BodyEntered += OnBodyEntered;
	}
	
	/// <summary>
	/// Get color for piece type
	/// </summary>
	private Color GetPieceColor()
	{
		return PieceColor;
	}

	/// <summary>
	/// Get text label for piece type
	/// </summary>
	private string GetPieceText()
	{
		return Type switch
		{
			PieceType.White => "W",
			PieceType.Black => "B",
			PieceType.Red => "Q", // Queen
			PieceType.Striker => "S",
			_ => "?"
		};
	}

	/// <summary>
	/// Handle collision with other bodies
	/// </summary>
	private void OnBodyEntered(Node body)
	{
		if (body is CarromPiece otherPiece)
		{
			// Calculate impact force for sound/visual effects
			Vector2 relativeVelocity = LinearVelocity - otherPiece.LinearVelocity;
			float impactSpeed = relativeVelocity.Length();
			Vector2 impactForce = relativeVelocity * Mass;

			EmitSignal(SignalName.PieceCollided, this, otherPiece, impactForce);
		}
	}

	/// <summary>
	/// Apply strike force to piece
	/// </summary>
	public void ApplyStrike(Vector2 force)
	{
		ApplyImpulse(force);
		_wasMoving = true;
		_stoppedTimer = 0.0f;
	}

	/// <summary>
	/// Check if piece has stopped moving using timer-based confirmation with hysteresis and movement validation
	/// </summary>
	public bool IsStopped()
	{
		// Prevent false positive during restoration - piece should not be considered stopped immediately after reset
		if (_skipNextStoppedCheck)
		{
			return false;
		}
		
		// Special case: Strikers can be considered stopped immediately after setup to allow input
		if (IsStrikerReadyForInput())
		{
			return true;
		}
		
		// Handle pieces that never moved significantly but are clearly settled
		if (!_hasEverMoved)
		{
			return CheckNeverMovedPieceSettlement();
		}
		
		// Use physics config to determine if piece is stopped (includes hysteresis)
		return PhysicsConfig.IsPieceStopped(LinearVelocity, AngularVelocity, _wasMoving);
	}
	
	/// <summary>
	/// Check if striker is ready for input (stopped after setup)
	/// </summary>
	private bool IsStrikerReadyForInput()
	{
		return Type == PieceType.Striker && !_hasEverMoved && LinearVelocity.Length() < _minVelocityThreshold;
	}
	
	/// <summary>
	/// Check if a piece that has never moved significantly should be considered settled
	/// </summary>
	private bool CheckNeverMovedPieceSettlement()
	{
		float currentVelocity = LinearVelocity.Length();
		float currentAngularVel = Mathf.Abs(AngularVelocity);
		
		// Enhanced never-moved logic with multiple fallback conditions
		bool isPhysicallyStationary = currentVelocity < _minVelocityThreshold && currentAngularVel < _angularMinThreshold;
		bool isCompletelyStill = currentVelocity < 0.01f && currentAngularVel < 0.001f;
		
		// Calculate time alive once for all checks
		double timeAlive = Time.GetUnixTimeFromSystem() - _creationTime;
		
		if (isPhysicallyStationary)
		{
			// Progressive timeout: stricter conditions for shorter times, more lenient for longer times
			if (isCompletelyStill && timeAlive > PhysicsConfig.CompletelyStillTimeout)
			{
				return true;
			}
			else if (timeAlive > PhysicsConfig.StationaryPieceTimeout)
			{
				return true;
			}
			else if (timeAlive > PhysicsConfig.AbsoluteTimeout)
			{
				return true;
			}
		}
		
		// Ultimate fallback - no piece should block settlement indefinitely
		if (timeAlive > PhysicsConfig.UltimateTimeout)
		{
			return true;
		}
		
		return false; // Cannot be considered "stopped" if it never moved
	}
	

	/// <summary>
	/// Minimal physics integration with only essential velocity clamping
	/// </summary>
	public override void _IntegrateForces(PhysicsDirectBodyState2D state)
	{
		// Only clamp velocities to prevent physics instability - let Godot handle everything else
		Vector2 velocity = state.LinearVelocity;
		if (velocity.Length() > _maxVelocityLimit) // Max velocity limit
		{
			state.LinearVelocity = velocity.Normalized() * _maxVelocityLimit;
		}
		
		if (Mathf.Abs(state.AngularVelocity) > _maxAngularVelocity) // Max angular velocity limit
		{
			state.AngularVelocity = Mathf.Sign(state.AngularVelocity) * _maxAngularVelocity;
		}
	}

	/// <summary>
	/// Apply powder-effect friction based on distance traveled
	/// 
	/// Mimics real carrom physics where board powder creates:
	/// - Initial "frictionless" feel: Low friction coefficient (μ ≈ 0.05) when powder acts like ball bearings
	/// - Gradual transition: Friction increases over distance as powder scatters and disperses
	/// - "Sudden" slowdown: Same constant friction force becomes larger percentage of smaller velocities
	/// - Physics accuracy: Applies constant deceleration (F = μ × m × g) rather than velocity-proportional damping
	/// 
	/// This eliminates the "floaty" feel by using realistic constant-force friction that matches 
	/// research on carrom board powder physics behavior.
	/// </summary>
	public override void _PhysicsProcess(double delta)
	{
		if (PhysicsConfig == null || !PhysicsConfig.EnablePowderEffectFriction)
		{
			return; // Use built-in damping when powder effect disabled
		}
		
		Vector2 currentPosition = GlobalPosition;
		_cachedSpeed = LinearVelocity.Length(); // Cache speed calculation
		
		// Only apply custom friction when piece is moving significantly
		if (_cachedSpeed > _minVelocityThreshold)
		{
			// Calculate distance traveled this frame
			float frameDeltaDistance = (currentPosition - _lastPositionForFriction).Length();
			_distanceTraveled += frameDeltaDistance;
			
			// Update friction coefficient cache if distance changed significantly
			if (!_frictionCacheValid || frameDeltaDistance > 1.0f)
			{
				_cachedFrictionCoefficient = PhysicsConfig.CalculatePowderFrictionCoefficient(_distanceTraveled);
				_frictionCacheValid = true;
			}
			
			// Apply realistic friction force: F = μ × m × g
			float frictionAcceleration = _cachedFrictionCoefficient * PhysicsConfig.GravityConstant;
			Vector2 frictionForceVector = -LinearVelocity.Normalized() * frictionAcceleration * (float)delta;
			
			// Prevent friction from reversing direction
			if (frictionForceVector.Length() > _cachedSpeed)
			{
				LinearVelocity = Vector2.Zero;
				_cachedSpeed = 0.0f; // Update cached speed
			}
			else
			{
				LinearVelocity += frictionForceVector;
				// Don't recalculate speed immediately as it will be updated next frame
			}
			
			_powderFrictionActive = true;
		}
		else
		{
			_powderFrictionActive = false;
		}
		
		// Update position tracking for next frame
		_lastPositionForFriction = currentPosition;
	}

	/// <summary>
	/// Process movement and stop detection with movement history tracking
	/// </summary>
	public override void _Process(double delta)
	{
		// Use cached speed if available from _PhysicsProcess, otherwise calculate
		float currentSpeed = _cachedSpeed > 0.0f ? _cachedSpeed : LinearVelocity.Length();
		
		// Track movement history for meaningful velocity validation
		if (currentSpeed > _maxVelocityAchieved)
		{
			_maxVelocityAchieved = currentSpeed;
		}
		
		// Mark as having moved if velocity exceeds minimum threshold
		if (currentSpeed > PhysicsConfig.MinimumMovementValidation && !_hasEverMoved)
		{
			_hasEverMoved = true;
		}
		
		// Track movement state changes (only after movement validation to prevent false settlement)
		bool isMoving = currentSpeed > _minVelocityThreshold || Mathf.Abs(AngularVelocity) > _angularMinThreshold;
		if (isMoving)
		{
			_wasMoving = true;
			_stoppedTimer = 0.0f;
		}
		else if (_wasMoving && _hasEverMoved) // Only allow stopping if piece has actually moved
		{
			float deltaF = (float)delta;
			_stoppedTimer += deltaF;
			
			// Confirm piece has stopped after a brief delay
			if (_stoppedTimer >= PhysicsConfig.StopConfirmationTime)
			{
				ForceStop();
				EmitSignal(SignalName.PieceStopped, this);
			}
		}

		_lastVelocity = LinearVelocity;
	}

	/// <summary>
	/// Force piece to stop immediately
	/// </summary>
	public void ForceStop()
	{
		LinearVelocity = Vector2.Zero;
		AngularVelocity = 0.0f;
		_wasMoving = false;
		_stoppedTimer = 0.0f;
	}

	/// <summary>
	/// Check if this piece can be placed at a given position without collision
	/// </summary>
	/// <param name="position">World position to check</param>
	/// <param name="board">Reference to the CarromBoard for collision checking</param>
	/// <returns>True if position is valid, false if obstructed</returns>
	public bool CanBePlacedAt(Vector2 position, CarromBoard board = null)
	{
		if (board == null)
		{
			// Try to find board in parent hierarchy
			var parent = GetParent();
			while (parent != null)
			{
				if (parent is CarromBoard foundBoard)
				{
					board = foundBoard;
					break;
				}
				parent = parent.GetParent();
			}
			
			if (board == null)
			{
				GD.PrintErr("[CarromPiece] Cannot validate placement: No board reference found");
				return true; // Assume valid if we can't check
			}
		}
		
		float pieceRadius = PhysicsConfig?.GetRadiusForPieceType(Type) ?? 15.0f;
		return !board.IsPositionObstructed(position, pieceRadius, this);
	}
	
	/// <summary>
	/// Find a valid position for this piece near the target location
	/// </summary>
	/// <param name="targetPosition">Preferred position</param>
	/// <param name="board">Reference to the CarromBoard</param>
	/// <returns>Valid position near target, or null if no valid position found</returns>
	public Vector2? FindValidPositionNear(Vector2 targetPosition, CarromBoard board = null)
	{
		if (board == null)
		{
			// Try to find board in parent hierarchy
			var parent = GetParent();
			while (parent != null)
			{
				if (parent is CarromBoard foundBoard)
				{
					board = foundBoard;
					break;
				}
				parent = parent.GetParent();
			}
			
			if (board == null)
			{
				GD.PrintErr("[CarromPiece] Cannot find valid position: No board reference found");
				return targetPosition; // Return original position if we can't check
			}
		}
		
		float pieceRadius = PhysicsConfig?.GetRadiusForPieceType(Type) ?? 15.0f;
		
		// Check if target position is already valid
		if (!board.IsPositionObstructed(targetPosition, pieceRadius, this))
		{
			return targetPosition;
		}
		
		// Use board's spiral search to find alternative position
		return board.FindValidPositionNearCenter(pieceRadius, this);
	}
	
	/// <summary>
	/// Reset piece to clean state with collision-aware positioning
	/// </summary>
	/// <param name="globalPosition">Optional position to set piece to (null = no position change)</param>
	/// <param name="immediate">If true, uses immediate physics sync to prevent interactions during reset</param>
	/// <param name="restoreVisualProperties">If true, restores scale and modulate to default values</param>
	/// <param name="validatePlacement">If true, checks for collisions and finds alternative position if needed</param>
	public void Reset(Vector2? globalPosition = null, bool immediate = false, bool restoreVisualProperties = true, bool validatePlacement = false)
	{
		bool originalContactMonitor = ContactMonitor;
		
		// Temporarily disable collision monitoring if using immediate mode
		if (immediate)
		{
			ContactMonitor = false;
		}
		
		// Stop all movement and clear tracking flags
		LinearVelocity = Vector2.Zero;
		AngularVelocity = 0.0f;
		_wasMoving = false;
		_stoppedTimer = 0.0f;
		
		// Reset movement history for fresh settlement detection
		_hasEverMoved = false;
		_maxVelocityAchieved = 0.0f;
		
		// Reset powder friction tracking
		_distanceTraveled = 0.0f;
		_powderFrictionActive = false;
		
		// Invalidate cached calculations
		_cachedSpeed = 0.0f;
		_frictionCacheValid = false;
		
		// Clear reset state flags
		_skipNextStoppedCheck = false;
		
		// Handle positioning with optional collision validation
		if (globalPosition.HasValue)
		{
			Vector2 finalPosition = globalPosition.Value;
			
			// Validate placement if requested
			if (validatePlacement)
			{
				var validPosition = FindValidPositionNear(finalPosition);
				if (validPosition.HasValue)
				{
					finalPosition = validPosition.Value;
				}
				else
				{
					GD.PrintErr($"[CarromPiece] Could not find valid position for piece {Type} near {finalPosition}, using original position");
				}
			}
			
			// Use physics server for immediate sync
			PhysicsServer2D.BodySetState(GetRid(), PhysicsServer2D.BodyState.Transform, 
				new Transform2D(0, finalPosition));
			GlobalPosition = finalPosition;
			_lastPositionForFriction = finalPosition;
			
			// Additional force stop after positioning
			LinearVelocity = Vector2.Zero;
			AngularVelocity = 0.0f;
		}
		else
		{
			// Update position tracking for powder friction
			_lastPositionForFriction = GlobalPosition;
		}
		
		// Restore physics and visual state
		Visible = true;
		Freeze = false;
		ContactMonitor = originalContactMonitor;
		
		// Restore visual properties if requested
		if (restoreVisualProperties)
		{
			Scale = Vector2.One;
			Modulate = new Color(1.0f, 1.0f, 1.0f, 1.0f);
		}
	}

	/// <summary>
	/// Pocket this piece (hide and disable physics)
	/// </summary>
	public void PocketPiece()
	{
		ForceStop();
		Visible = false;
		Freeze = true;
		ContactMonitor = false;
	}

	/// <summary>
	/// Get piece value for scoring
	/// </summary>
	public float GetValue()
	{
		return Type switch
		{
			PieceType.Red => 10.0f,  // Queen is worth more
			PieceType.White => 1.0f,
			PieceType.Black => 1.0f,
			PieceType.Striker => 0.0f, // Striker has no value (foul if pocketed)
			_ => 0.0f
		};
	}

	/// <summary>
	/// Check if this piece can be legally pocketed by a player
	/// </summary>
	public bool CanBePocketedBy(PieceType playerPieceType)
	{
		if (Type == PieceType.Striker)
		{
			return false; // Striker should never be pocketed
		}
		
		if (Type == PieceType.Red)
		{
			return true; // Queen can be pocketed by anyone (with conditions)
		}
		
		return Type == playerPieceType; // Can only pocket assigned pieces
	}

	/// <summary>
	/// Get movement direction
	/// </summary>
	public Vector2 GetMovementDirection()
	{
		if (LinearVelocity.Length() > _minVelocityThreshold)
		{
			return LinearVelocity.Normalized();
		}
		return Vector2.Zero;
	}

	/// <summary>
	/// Get current speed
	/// </summary>
	public float GetSpeed()
	{
		return LinearVelocity.Length();
	}

	/// <summary>
	/// Update visual appearance
	/// </summary>
	public void UpdateVisual()
	{
		if (_pieceLabel != null)
		{
			_pieceLabel.Text = GetPieceText();
		}
	}

	/// <summary>
	/// Custom drawing for circular pieces
	/// </summary>
	public override void _Draw()
	{
		// Use visual radius (not collision radius) for drawing
		float radius = PhysicsConfig.GetRadiusForPieceType(Type);

		// Draw piece as a circle with proper colors and highlights
		DrawCircle(Vector2.Zero, radius, GetPieceColor(), true, -1f, true);

		// Add highlight for 3D effect
		Vector2 highlightOffset = new Vector2(-3, -3);
		DrawCircle(highlightOffset, radius * 0.3f, GetPieceColor().Lightened(0.3f), true, -1f, true);

		// Add border
		DrawArc(Vector2.Zero, radius, 0, Mathf.Tau, 32, Colors.Black, 0.75f, true);
	}

	public override void _ExitTree()
	{
		base._ExitTree();
		
		// Disconnect signals - no need for IsInstanceValid check as _ExitTree means object is still valid
		BodyEntered -= OnBodyEntered;
	}
}