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
	private int _physicsFramesAfterStrike = 0;

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
		
		GD.Print($"[CarromPiece] {Type} piece initialized");
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
		
		GD.Print($"[CarromPiece] {Type} physics limits set: MaxVel {_maxVelocityLimit}, Alert {_velocityAlertThreshold}");
	}

	/// <summary>
	/// Validate export values and apply type-specific defaults
	/// </summary>
	private void ValidateExports()
	{
		// Create default physics config if not provided
		if (PhysicsConfig == null)
		{
			PhysicsConfig = new CarromPhysicsConfig();
			GD.PrintErr("[CarromPiece] No PhysicsConfig provided - using fallback values without board scaling. This may result in incorrect piece proportions.");
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
		
		GD.Print($"[CarromPiece] {Type} piece physics setup with CCD enabled");
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
			
			GD.Print($"[CarromPiece] {Type} collided with {otherPiece.Type}, impact: {impactSpeed:F1}");
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
		_physicsFramesAfterStrike = 3; // Prevent stopped detection for 3 physics frames
		
		GD.Print($"[CarromPiece] {Type} struck with force: {force}");
	}

	/// <summary>
	/// Check if piece has stopped moving (with hysteresis to prevent fluttering)
	/// </summary>
	public bool IsStopped()
	{
		// Don't report stopped immediately after strike to allow physics to propagate
		if (_physicsFramesAfterStrike > 0)
			return false;
		
		return PhysicsConfig.IsPieceStopped(LinearVelocity, AngularVelocity, _wasMoving);
	}

	/// <summary>
	/// Process physics frame counting and enhanced collision detection
	/// </summary>
	public override void _PhysicsProcess(double delta)
	{
		// Decrement physics frame counter
		if (_physicsFramesAfterStrike > 0)
		{
			_physicsFramesAfterStrike--;
		}

		// Update collision detection based on velocity
		UpdateCollisionDetection();

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
	/// Update collision detection state tracking (CCD is always enabled now)
	/// </summary>
	private void UpdateCollisionDetection()
	{
		float currentSpeed = LinearVelocity.Length();
		bool isHighSpeed = PhysicsConfig.IsHighSpeedVelocity(currentSpeed);
		
		// Track maximum speed achieved for tunneling protection validation
		if (currentSpeed > _maxSpeedAchieved)
		{
			_maxSpeedAchieved = currentSpeed;
			if (_maxSpeedAchieved > _velocityAlertThreshold) // Alert when approaching velocity limit
			{
				GD.Print($"[CarromPiece] {Type} approaching max velocity: {_maxSpeedAchieved:F1} (limit: {_maxVelocityLimit})");
			}
		}
		
		// Update state tracking for debug display (CCD is always enabled)
		if (isHighSpeed != _useCcdCollision)
		{
			_useCcdCollision = isHighSpeed;
			
			if (_useCcdCollision)
			{
				GD.Print($"[CarromPiece] {Type} high-speed collision at speed: {currentSpeed:F1}");
			}
		}
	}
	
	
	/// <summary>
	/// Process movement and stop detection
	/// </summary>
	public override void _Process(double delta)
	{
		float deltaF = (float)delta;
		
		// Track movement state changes
		bool isMoving = !IsStopped();
		
		if (isMoving)
		{
			_wasMoving = true;
			_stoppedTimer = 0.0f;
		}
		else if (_wasMoving)
		{
			_stoppedTimer += deltaF;
			
			// Confirm piece has stopped after a brief delay
			if (_stoppedTimer >= PhysicsConfig.StopConfirmationTime)
			{
				_wasMoving = false;
				EmitSignal(SignalName.PieceStopped, this);
				GD.Print($"[CarromPiece] {Type} stopped at position: {GlobalPosition}");
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
	/// Reset piece to initial state
	/// </summary>
	public void ResetPiece()
	{
		GD.Print($"[CarromPiece] Resetting {Type} piece - Freeze state before: {Freeze}");
		
		// First unfreeze to allow physics operations
		Freeze = false;
		
		// Reset collision detection
		ContactMonitor = true;
		
		// Stop movement after unfreezing
		ForceStop();
		
		// Make visible
		Visible = true;
		
		GD.Print($"[CarromPiece] Reset {Type} piece complete - Freeze state after: {Freeze}");
	}

	/// <summary>
	/// Reset piece to a specific position using physics-safe methods
	/// Since the board is at origin, targetPosition is already in the correct coordinate space
	/// </summary>
	public void ResetToPosition(Vector2 targetPosition)
	{
		GD.Print($"[CarromPiece] Resetting {Type} piece to position {targetPosition}");
		
		// Step 1: Freeze the body to prevent physics interference
		Freeze = true;
		GD.Print($"[CarromPiece] {Type} piece FROZEN for reset");
		
		// Step 2: Reset physics state while frozen
		ForceStop();
		ContactMonitor = true;
		Visible = true;
		
		// Step 3: Use PhysicsServer2D to set position directly - this bypasses RigidBody2D conflicts
		// Since board is at origin, targetPosition is already in correct global coordinates
		var rid = GetRid();
		var transform = Transform2D.Identity;
		transform.Origin = targetPosition;
		PhysicsServer2D.BodySetState(rid, PhysicsServer2D.BodyState.Transform, transform);
		
		// Step 4: Unfreeze after position is set (deferred to avoid same-frame conflicts)
		CallDeferred(GodotObject.MethodName.SetDeferred, RigidBody2D.PropertyName.Freeze, false);
		CallDeferred(MethodName.LogUnfreezeComplete);
		
		GD.Print($"[CarromPiece] {Type} piece position reset complete, unfreezing deferred");
	}
	
	/// <summary>
	/// Log when deferred unfreeze completes
	/// </summary>
	private void LogUnfreezeComplete()
	{
		GD.Print($"[CarromPiece] {Type} piece UNFROZEN - ready for strikes");
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
		
		GD.Print($"[CarromPiece] {Type} piece pocketed");
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
		DrawCircle(Vector2.Zero, radius, GetPieceColor());
		
		// Add highlight for 3D effect
		Vector2 highlightOffset = new Vector2(-3, -3);
		DrawCircle(highlightOffset, radius * 0.3f, GetPieceColor().Lightened(0.3f));
		
		// Add border
		DrawArc(Vector2.Zero, radius, 0, Mathf.Tau, 32, Colors.Black, 2.0f);
		
		// Debug: Show CCD collision detection state
		if (_useCcdCollision && !Engine.IsEditorHint())
		{
			DrawArc(Vector2.Zero, radius * 1.1f, 0, Mathf.Tau, 16, Colors.Blue, 1.0f);
		}
	}

	/// <summary>
	/// Cleanup on exit
	/// </summary>
	public override void _Notification(int what)
	{
		if (what == NotificationExitTree)
		{
			// Clean up signals
			if (GodotObject.IsInstanceValid(this))
			{
				BodyEntered -= OnBodyEntered;
			}
			
		}
	}

}