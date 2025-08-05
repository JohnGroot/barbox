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
	
	// Deferred physics reset to prevent false settlement detection
	private bool _skipNextStoppedCheck = false;
	private bool _deferredResetPending = false;
	
	// Movement history tracking for meaningful velocity validation
	private bool _hasEverMoved = false;
	private float _maxVelocityAchieved = 0.0f;
	private float _minimumMovementValidation = 50.0f; // Minimum velocity to count as "real movement"
	private double _creationTime = 0.0; // Track when piece was created for never-moved timeout

	// Physics limits - set by CarromGame.cs
	private float _minVelocityThreshold = 1.0f;
	private float _angularMinThreshold = 0.1f;
	private float _maxVelocityLimit = 2000.0f;
	private float _maxAngularVelocity = 50.0f;  
	private float _velocityAlertThreshold = 1800.0f;
	
	// Realistic physics constants
	private const float ANGULAR_TORQUE_SCALE = 50.0f;
	
	// Collision detection state tracking
	private bool _useCcdCollision = false;
	
	// Velocity monitoring for tunneling protection validation
	private float _maxSpeedAchieved = 0.0f;

	public override void _Ready()
	{
		ValidateExports();
		SetupPhysics();
		SetupCollisionShape();
		SetupVisual();
		SetupPhysicsMaterial();
		ConnectSignals();
		
		// Record creation time for never-moved timeout
		_creationTime = Time.GetUnixTimeFromSystem();
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
	/// Validate export values and apply type-specific defaults
	/// </summary>
	private void ValidateExports()
	{
		// Create default physics config if not provided
		if (PhysicsConfig == null)
		{
			return;
		}
	}

	/// <summary>
	/// Setup physics properties
	/// </summary>
	private void SetupPhysics()
	{
		GravityScale = 0.0f; // No gravity for top-down view
		LinearDamp = PhysicsConfig.LinearDamping;
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
		if (Type == PieceType.Striker && !_hasEverMoved && LinearVelocity.Length() < _minVelocityThreshold)
		{
			return true;
		}
		
		// CRITICAL FIX: Handle pieces that never moved significantly but are clearly settled
		// In competitive mode, some pieces may never reach the movement threshold but are visually stopped
		if (!_hasEverMoved)
		{
			// Check if piece is truly stationary (zero velocity and not in a transient state)
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
				if (isCompletelyStill && timeAlive > 2.0) // Completely still after 2 seconds
				{
					return true;
				}
				else if (timeAlive > 5.0) // Any stationary piece after 5 seconds
				{
					return true;
				}
				else if (timeAlive > 10.0) // Absolute timeout - any piece after 10 seconds
				{
					return true;
				}
			}
			
			// Check if piece has been in this state too long (stuck detection)
			if (timeAlive > 15.0) // Ultimate fallback - no piece should block settlement for 15+ seconds
			{
				return true;
			}
			
			return false; // Cannot be considered "stopped" if it never moved
		}
		
		// Use physics config to determine if piece is stopped (includes hysteresis)
		return PhysicsConfig.IsPieceStopped(LinearVelocity, AngularVelocity, _wasMoving);
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
	/// Process movement and stop detection with movement history tracking
	/// </summary>
	public override void _Process(double delta)
	{
		// Track movement history for meaningful velocity validation
		float currentSpeed = LinearVelocity.Length();
		if (currentSpeed > _maxVelocityAchieved)
		{
			_maxVelocityAchieved = currentSpeed;
		}
		
		// Mark as having moved if velocity exceeds minimum threshold
		if (currentSpeed > _minimumMovementValidation && !_hasEverMoved)
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
	/// Internal method to restore piece to a clean physics and visual state
	/// </summary>
	private void RestorePieceState(bool restoreVisualProperties = true, bool conditionalUnfreeze = true)
	{
		// Stop all movement
		ForceStop();
		
		// Reset movement history for fresh settlement detection
		_hasEverMoved = false;
		_maxVelocityAchieved = 0.0f;
		
		// Handle freeze state
		if (conditionalUnfreeze)
		{
			if (Freeze)
			{
				Freeze = false;
			}
		}
		else
		{
			Freeze = false;
		}
		
		// Restore physics state
		ContactMonitor = true;
		Visible = true;
		
		// Restore visual properties if requested
		if (restoreVisualProperties)
		{
			Scale = Vector2.One;
			Modulate = new Color(1.0f, 1.0f, 1.0f, 1.0f);
		}
	}

	/// <summary>
	/// Reset piece to clean state (stop movement, restore physics, make visible)
	/// </summary>
	public void Reset()
	{
		RestorePieceState(restoreVisualProperties: true, conditionalUnfreeze: true);
	}

	/// <summary>
	/// Reset piece and position it at the specified global coordinates
	/// </summary>
	public void Reset(Vector2 globalPosition)
	{
		// Step 1: Restore state with visual properties
		RestorePieceState(restoreVisualProperties: true, conditionalUnfreeze: true);
		
		// Step 2: Force physics engine to sync position immediately
		// This ensures the physics body's internal state is updated synchronously
		PhysicsServer2D.BodySetState(GetRid(), PhysicsServer2D.BodyState.Transform, 
			new Transform2D(0, globalPosition));
		
		// Step 3: Set GlobalPosition as backup
		GlobalPosition = globalPosition;
		
		// Step 4: Final force stop after physics sync
		ForceStop();
	}
	
	/// <summary>
	/// Reset piece with immediate, synchronous physics - guarantees stopped state without physics interactions
	/// </summary>
	public void ResetWithImmediatePhysics(Vector2 globalPosition)
	{
		// Step 1: Temporarily disable collision monitoring to prevent physics interactions during reset
		bool originalContactMonitor = ContactMonitor;
		ContactMonitor = false;
		
		// Step 2: Stop all movement immediately and clear tracking flags
		LinearVelocity = Vector2.Zero;
		AngularVelocity = 0.0f;
		_wasMoving = false;
		_stoppedTimer = 0.0f;
		
		// Step 3: Reset movement history for fresh settlement detection
		_hasEverMoved = false;
		_maxVelocityAchieved = 0.0f;
		
		// Step 4: Use synchronous positioning
		SetPhysicsSafePositionImmediate(globalPosition, originalContactMonitor);
		
		// Step 5: Clear any deferred operation flags
		_skipNextStoppedCheck = false;
		_deferredResetPending = false;
	}
	
	/// <summary>
	/// Set position in a physics-safe manner that avoids triggering movement
	/// </summary>
	private void SetPhysicsSafePosition(Vector2 globalPosition, bool originalContactMonitor)
	{
		// Position the piece using physics server for immediate sync
		PhysicsServer2D.BodySetState(GetRid(), PhysicsServer2D.BodyState.Transform, 
			new Transform2D(0, globalPosition));
		GlobalPosition = globalPosition;
		
		// Final velocity clearing after positioning
		LinearVelocity = Vector2.Zero;
		AngularVelocity = 0.0f;
		
		// Restore visual state
		Visible = true;
		Freeze = false;
		Scale = Vector2.One;
		Modulate = new Color(1.0f, 1.0f, 1.0f, 1.0f);
		
		// Re-enable collision monitoring after positioning is complete
		ContactMonitor = originalContactMonitor;
	}
	
	/// <summary>
	/// Set position immediately and synchronously - no deferred operations
	/// </summary>
	private void SetPhysicsSafePositionImmediate(Vector2 globalPosition, bool originalContactMonitor)
	{
		// Position the piece using physics server for immediate sync
		PhysicsServer2D.BodySetState(GetRid(), PhysicsServer2D.BodyState.Transform, 
			new Transform2D(0, globalPosition));
		GlobalPosition = globalPosition;
		
		// Final velocity clearing after positioning
		LinearVelocity = Vector2.Zero;
		AngularVelocity = 0.0f;
		
		// Restore visual state
		Visible = true;
		Freeze = false;
		Scale = Vector2.One;
		Modulate = new Color(1.0f, 1.0f, 1.0f, 1.0f);
		
		// Re-enable collision monitoring after positioning is complete
		ContactMonitor = originalContactMonitor;
	}
	
	/// <summary>
	/// Apply deferred velocity reset after settlement processing
	/// </summary>
	private void ApplyDeferredVelocityReset()
	{
		if (!_deferredResetPending)
		{
			return; // Reset was cancelled or already applied
		}
		
		// Now safe to reset velocities without triggering false settlement
		LinearVelocity = Vector2.Zero;
		AngularVelocity = 0.0f;
		_wasMoving = false;
		_stoppedTimer = 0.0f;
		
		// Clear deferred reset state
		_skipNextStoppedCheck = false;
		_deferredResetPending = false;
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

		// Debug: Show CCD collision detection state
		if (_useCcdCollision && !Engine.IsEditorHint())
		{
			DrawArc(Vector2.Zero, radius * 1.1f, 0, Mathf.Tau, 16, Colors.Blue, 1.0f, true);
		}
	}

	public override void _ExitTree()
	{
		base._ExitTree();

		if (IsInstanceValid(this))
		{
			BodyEntered -= OnBodyEntered;
		}
	}
}