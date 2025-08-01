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
			GD.PrintErr($"[CarromPiece] ERROR: No PhysicsConfig provided on {Name}");
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
	/// Check if piece has stopped moving using timer-based confirmation with hysteresis
	/// </summary>
	public bool IsStopped()
	{
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
	/// Process movement and stop detection
	/// </summary>
	public override void _Process(double delta)
	{
		// Track movement state changes
		bool isMoving = !IsStopped();
		if (isMoving)
		{
			_wasMoving = true;
			_stoppedTimer = 0.0f;
		}
		else if (_wasMoving)
		{
			float deltaF = (float)delta;
			_stoppedTimer += deltaF;
			
			// Confirm piece has stopped after a brief delay
			if (_stoppedTimer >= PhysicsConfig.StopConfirmationTime)
			{
				_wasMoving = false;
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
		
		// Step 2: Set position
		GlobalPosition = globalPosition;
		
		// Step 3: Final force stop to ensure clean state after position change
		ForceStop();
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