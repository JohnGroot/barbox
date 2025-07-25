using Godot;

/// <summary>
/// Racing car player that extends BasePlayer with racing-specific functionality
/// </summary>
[GlobalClass]
public partial class RacingPlayer : BasePlayer
{
	[Signal] public delegate void CarMovedEventHandler(Vector2 position, Vector2 velocity);
	[Signal] public delegate void CarRotatedEventHandler(float rotation);

	[ExportCategory("Car Settings")]
	[Export] public Vector2 CarSize { get; set; } = new Vector2(80, 160);
	[Export] public float MaxSpeed { get; set; } = 400.0f;
	[Export] public float MinSpeed { get; set; } = 50.0f;
	[Export] public float AccelerationRate { get; set; } = 800.0f;
	[Export] public float DecelerationRate { get; set; } = 600.0f;
	[Export] public float RotationLerpSpeed { get; set; } = 5.0f;

	[ExportCategory("Input Settings")]
	[Export] public float MaxInputDistance { get; set; } = 300.0f;

	// Car physics components
	protected CharacterBody2D _carBody;
	protected CollisionShape2D _carCollision;
	protected ColorRect _carVisual;

	// Car state
	protected Vector2 _targetPosition;
	protected Vector2 _lastTargetPosition;
	protected Vector2 _velocity = Vector2.Zero;
	protected float _currentSpeed = 0.0f;
	protected bool _hasInput = false;

	// Input management (will be replaced with InputComponent)
	protected InputManager _inputManager;

	protected override void InitializePlayer()
	{
		base.InitializePlayer();
		SetupCarBody();
		SetupInputManager();
	}

	/// <summary>
	/// Setup the car's physical body and visuals
	/// </summary>
	protected virtual void SetupCarBody()
	{
		_carBody = new CharacterBody2D();
		AddChild(_carBody);

		// Car visual
		_carVisual = new ColorRect();
		_carVisual.Size = CarSize;
		_carVisual.Color = Colors.Red;
		_carVisual.Position = new Vector2(-CarSize.X * 0.125f, -CarSize.Y * 0.125f);
		_carBody.AddChild(_carVisual);

		// Car collision
		_carCollision = new CollisionShape2D();
		var carShape = new RectangleShape2D();
		carShape.Size = CarSize;
		_carCollision.Shape = carShape;
		_carBody.AddChild(_carCollision);

		// Position car at player position
		_carBody.GlobalPosition = GlobalPosition;
	}

	/// <summary>
	/// Setup input manager connection
	/// </summary>
	protected virtual void SetupInputManager()
	{
		_inputManager = InputManager.GetAutoload();
		if (_inputManager != null)
		{
			_inputManager.TouchStarted += OnTouchStarted;
			_inputManager.TouchEnded += OnTouchEnded;
			_inputManager.ClickStarted += OnClickStarted;
			_inputManager.ClickEnded += OnClickEnded;
		}
	}

	public override void _Process(double delta)
	{
		if (!IsActive()) return;

		float deltaF = (float)delta;

		// Poll for current input position
		if (_inputManager != null && _hasInput)
		{
			if (_inputManager.IsTouchActive())
			{
				Vector2 screenPosition = _inputManager.GetTouchPosition();
				Vector2 worldPosition = TransformScreenToWorldPosition(screenPosition);
				_targetPosition = worldPosition;
				_lastTargetPosition = worldPosition;
			}
			else
			{
				_hasInput = false;
			}
		}

		UpdateCarPhysics(deltaF);
	}

	/// <summary>
	/// Transform screen coordinates to world coordinates
	/// </summary>
	protected virtual Vector2 TransformScreenToWorldPosition(Vector2 screenPosition)
	{
		// Basic screen to world transformation - subclasses can override for camera-aware transformation
		return screenPosition;
	}

	/// <summary>
	/// Update car physics and movement
	/// </summary>
	protected virtual void UpdateCarPhysics(float delta)
	{
		if (_carBody == null) return;

		Vector2 inputDirection = Vector2.Zero;
		float targetSpeed = 0.0f;

		if (_hasInput)
		{
			// Calculate direction and distance to target
			var directionToTarget = (_targetPosition - _carBody.GlobalPosition);
			var distanceToTarget = directionToTarget.Length();
			
			if (distanceToTarget > 1.0f)
			{
				inputDirection = directionToTarget.Normalized();
				
				// Calculate speed based on distance (closer = slower, farther = faster)
				var normalizedDistance = Mathf.Clamp(distanceToTarget / MaxInputDistance, 0.0f, 1.0f);
				targetSpeed = Mathf.Lerp(MinSpeed, MaxSpeed, normalizedDistance);
			}
		}
		else if (_lastTargetPosition != Vector2.Zero)
		{
			// Decelerate in last known direction
			var directionToLastTarget = (_lastTargetPosition - _carBody.GlobalPosition);
			if (directionToLastTarget.Length() > 1.0f)
			{
				inputDirection = directionToLastTarget.Normalized();
			}
			targetSpeed = 0.0f;
		}

		// Apply track penalties (can be overridden by subclasses)
		targetSpeed *= GetSpeedModifier();
		
		// Update velocity
		if (targetSpeed > _currentSpeed)
		{
			_currentSpeed = Mathf.MoveToward(_currentSpeed, targetSpeed, AccelerationRate * GetAccelerationModifier() * delta);
		}
		else
		{
			_currentSpeed = Mathf.MoveToward(_currentSpeed, targetSpeed, DecelerationRate * delta);
		}

		if (_currentSpeed > 0.1f && inputDirection != Vector2.Zero)
		{
			// Apply turn penalty to rotation speed
			var effectiveRotationSpeed = RotationLerpSpeed * GetTurnModifier() * delta;
			
			// Lerp car rotation towards target direction
			if (_hasInput)
			{
				var targetDirection = (_targetPosition - _carBody.GlobalPosition).Normalized();
				var targetAngle = targetDirection.Angle() + Mathf.Pi / 2;
				_carBody.Rotation = Mathf.LerpAngle(_carBody.Rotation, targetAngle, effectiveRotationSpeed);
			}
			else
			{
				// When not actively steering, rotate towards movement direction
				_carBody.Rotation = Mathf.LerpAngle(_carBody.Rotation, _velocity.Angle() + Mathf.Pi / 2, effectiveRotationSpeed);
			}
			
			// Calculate velocity relative to car's forward direction
			var carForward = new Vector2(Mathf.Sin(_carBody.Rotation), -Mathf.Cos(_carBody.Rotation));
			_velocity = carForward * _currentSpeed * GetTurnModifier();
		}
		else
		{
			_velocity = Vector2.Zero;
		}

		// Apply movement
		_carBody.Velocity = _velocity;
		_carBody.MoveAndSlide();

		// Emit movement signals
		EmitSignal(SignalName.CarMoved, _carBody.GlobalPosition, _velocity);
		EmitSignal(SignalName.CarRotated, _carBody.Rotation);
	}

	// Input event handlers
	protected virtual void OnTouchStarted(Vector2 position, int fingerId)
	{
		if (!IsActive()) return;
		_hasInput = true;
	}

	protected virtual void OnTouchEnded(Vector2 position, int fingerId)
	{
		if (!IsActive()) return;
		_hasInput = false;
	}

	protected virtual void OnClickStarted(Vector2 position)
	{
		if (!IsActive()) return;
		_hasInput = true;
	}

	protected virtual void OnClickEnded(Vector2 position)
	{
		if (!IsActive()) return;
		_hasInput = false;
	}

	// Override methods for track penalties - subclasses can implement track-specific logic
	protected virtual float GetSpeedModifier() => 1.0f;
	protected virtual float GetAccelerationModifier() => 1.0f;
	protected virtual float GetTurnModifier() => 1.0f;

	// Cleanup
	public override void _Notification(int what)
	{
		if (what == NotificationExitTree)
		{
			if (_inputManager != null && GodotObject.IsInstanceValid(_inputManager))
			{
				_inputManager.TouchStarted -= OnTouchStarted;
				_inputManager.TouchEnded -= OnTouchEnded;
				_inputManager.ClickStarted -= OnClickStarted;
				_inputManager.ClickEnded -= OnClickEnded;
			}
		}
		base._Notification(what);
	}

	// Public accessors
	public CharacterBody2D GetCarBody() => _carBody;
	public Vector2 GetVelocity() => _velocity;
	public float GetCurrentSpeed() => _currentSpeed;
	public bool HasInput() => _hasInput;
	public Vector2 GetTargetPosition() => _targetPosition;
	public void SetTargetPosition(Vector2 target) => _targetPosition = target;
}