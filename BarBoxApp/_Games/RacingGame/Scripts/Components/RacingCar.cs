using Godot;

namespace BarBox.Games.Racing
{
	/// <summary>
	/// Unified racing car that combines physics, input handling, and racing system integration
	/// Simplified from the previous RacingPlayer → RacingCarController inheritance chain
	/// </summary>
	[GlobalClass]
	public partial class RacingCar : BasePlayer
	{
		// ================================================================
		// SIGNALS
		// ================================================================
		
		[Signal] public delegate void CarMovedEventHandler(Vector2 position, Vector2 velocity);
		[Signal] public delegate void CarRotatedEventHandler(float rotation);

		// ================================================================
		// EXPORT PROPERTIES - CAR SETTINGS
		// ================================================================
		
		[ExportCategory("Car Settings")]
		[Export] public Vector2 CarSize { get; set; } = new Vector2(80, 160);
		[Export] public float MaxSpeed { get; set; } = 400.0f;
		[Export] public float MinSpeed { get; set; } = 50.0f;
		[Export] public float AccelerationRate { get; set; } = 800.0f;
		[Export] public float DecelerationRate { get; set; } = 600.0f;
		[Export] public float RotationLerpSpeed { get; set; } = 5.0f;

		[ExportCategory("Input Settings")]
		[Export] public float MaxInputDistance { get; set; } = 300.0f;

		// ================================================================
		// SYSTEM DEPENDENCIES - RACING SYSTEMS
		// ================================================================

		// References to racing game systems for modifiers and transformations
		private RacingCameraController _cameraController;
		private RacingTrackValidationSystem _trackValidationSystem;

		// ================================================================
		// CAR PHYSICS COMPONENTS
		// ================================================================
		
		protected CharacterBody2D _carBody;
		protected CollisionShape2D _carCollision;
		protected ColorRect _carVisual;

		// ================================================================
		// CAR PHYSICS STATE
		// ================================================================
		
		protected Vector2 _targetPosition;
		protected Vector2 _lastTargetPosition;
		protected Vector2 _velocity = Vector2.Zero;
		protected float _currentSpeed = 0.0f;
		protected bool _hasInput = false;

		// ================================================================
		// INPUT MANAGEMENT
		// ================================================================
		
		protected InputManager _inputManager;

		// ================================================================
		// PUBLIC PROPERTIES
		// ================================================================
		
		public bool IsInitialized => _cameraController != null && _trackValidationSystem != null;

		// ================================================================
		// INITIALIZATION
		// ================================================================

		protected override void InitializePlayer()
		{
			base.InitializePlayer();

			SetupCarBody();
			SetupInputManager();
		}

		/// <summary>
		/// Initialize the racing car with system dependencies
		/// </summary>
		/// <param name="cameraController">Camera controller for coordinate transformation</param>
		/// <param name="trackValidationSystem">Track validation system for penalties/bonuses</param>
		/// <param name="playerId">Player ID for the car</param>
		/// <param name="playerName">Player name for the car</param>
		public void Initialize(RacingCameraController cameraController, RacingTrackValidationSystem trackValidationSystem, string playerId = "player1", string playerName = "Player")
		{
			// Validate required dependencies
			if (string.IsNullOrEmpty(playerId))
				throw new System.ArgumentException("Player ID cannot be null or empty", nameof(playerId));
			if (string.IsNullOrEmpty(playerName))
				throw new System.ArgumentException("Player name cannot be null or empty", nameof(playerName));

			_cameraController = cameraController ?? throw new System.ArgumentNullException(nameof(cameraController), "Camera controller is required for car initialization");
			_trackValidationSystem = trackValidationSystem ?? throw new System.ArgumentNullException(nameof(trackValidationSystem), "Track validation system is required for car initialization");

			// Set player properties
			PlayerId = playerId;
			PlayerName = playerName;

			// Set default car properties (can be overridden via UpdateSettings)
			MaxSpeed = 1800.0f;
			MinSpeed = 100.0f;
			MaxInputDistance = 500.0f;
			AccelerationRate = 300.0f;
			DecelerationRate = 600.0f;
			RotationLerpSpeed = 5.0f;
			CarSize = new Vector2(40, 80);
		}

		/// <summary>
		/// Update car settings from external configuration
		/// </summary>
		public void UpdateSettings(float maxSpeed, float minSpeed, float maxInputDistance, float accelerationRate, float decelerationRate, float rotationLerpSpeed, Vector2 carSize)
		{
			MaxSpeed = maxSpeed;
			MinSpeed = minSpeed;
			MaxInputDistance = maxInputDistance;
			AccelerationRate = accelerationRate;
			DecelerationRate = decelerationRate;
			RotationLerpSpeed = rotationLerpSpeed;
			CarSize = carSize;
		}

		// ================================================================
		// CAR BODY SETUP
		// ================================================================

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

		// ================================================================
		// INPUT SYSTEM SETUP
		// ================================================================

		/// <summary>
		/// Setup input manager connection
		/// </summary>
		protected virtual void SetupInputManager()
		{
			_inputManager = InputManager.GetAutoload();
			if (_inputManager == null || !IsInstanceValid(_inputManager))
				return;

			_inputManager.TouchStarted += OnTouchStarted;
			_inputManager.TouchEnded += OnTouchEnded;
			_inputManager.ClickStarted += OnClickStarted;
			_inputManager.ClickEnded += OnClickEnded;
		}

		// ================================================================
		// CORE GAME LOOP
		// ================================================================

		public override void _Process(double delta)
		{
			if (!IsActive()) 
				return;

			// Check racing state before processing any input
			if (!IsRacingInputEnabled())
			{
				// Clear any existing input when state doesn't allow it
				_hasInput = false;
			}

			float deltaF = (float)delta;

			// Poll for current input position
			if (_inputManager != null && IsInstanceValid(_inputManager) && _hasInput)
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

		// ================================================================
		// CAR PHYSICS AND MOVEMENT
		// ================================================================

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

			// Apply track penalties
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

		// ================================================================
		// RACING-SPECIFIC INTEGRATIONS
		// ================================================================

		/// <summary>
		/// Transform screen coordinates to world coordinates using racing game camera
		/// </summary>
		protected virtual Vector2 TransformScreenToWorldPosition(Vector2 screenPosition)
		{
			return _cameraController?.TransformScreenToWorldPosition(screenPosition) ?? screenPosition;
		}

		/// <summary>
		/// Get speed modifier based on track validation (off-track penalties)
		/// </summary>
		protected virtual float GetSpeedModifier()
		{
			var carPosition = GetCarBody()?.GlobalPosition ?? Vector2.Zero;
			return _trackValidationSystem?.GetSpeedModifier(carPosition) ?? 1.0f;
		}

		/// <summary>
		/// Get acceleration modifier based on track validation (center line bonus, off-track penalties)
		/// </summary>
		protected virtual float GetAccelerationModifier()
		{
			var carPosition = GetCarBody()?.GlobalPosition ?? Vector2.Zero;
			return _trackValidationSystem?.GetAccelerationModifier(carPosition) ?? 1.0f;
		}

		/// <summary>
		/// Get turn modifier based on track validation (off-track penalties)
		/// </summary>
		protected virtual float GetTurnModifier()
		{
			var carPosition = GetCarBody()?.GlobalPosition ?? Vector2.Zero;
			return _trackValidationSystem?.GetTurnModifier(carPosition) ?? 1.0f;
		}

		// ================================================================
		// INPUT HANDLERS
		// ================================================================

		/// <summary>
		/// Check if racing input should be enabled based on current racing state
		/// </summary>
		private bool IsRacingInputEnabled()
		{
			var racingGame = GetParent<RacingGame>();
			return racingGame?.IsInputEnabled() ?? true;
		}

		protected virtual void OnTouchStarted(Vector2 position, int fingerId)
		{
			if (!IsActive() || !IsRacingInputEnabled()) 
				return;

			_hasInput = true;
		}

		protected virtual void OnTouchEnded(Vector2 position, int fingerId)
		{
			if (!IsActive() || !IsRacingInputEnabled()) 
				return;

			_hasInput = false;
		}

		protected virtual void OnClickStarted(Vector2 position)
		{
			if (!IsActive() || !IsRacingInputEnabled())
				return;

			_hasInput = true;
		}

		protected virtual void OnClickEnded(Vector2 position)
		{
			if (!IsActive() || !IsRacingInputEnabled()) 
				return;

			_hasInput = false;
		}

		// ================================================================
		// CAR MANAGEMENT
		// ================================================================

		/// <summary>
		/// Position car at a specific world position and rotation
		/// Also resets all physics state for clean repositioning
		/// </summary>
		public void PositionCar(Vector2 position, float rotation = 0.0f)
		{
			var carBody = GetCarBody();
			if (carBody != null)
			{
				carBody.GlobalPosition = position;
				carBody.Rotation = rotation;
			}
			
			// Reset all internal physics state to ensure clean positioning
			ResetCarState();
		}

		/// <summary>
		/// Get current car position
		/// </summary>
		public Vector2 GetCarPosition()
		{
			return GetCarBody()?.GlobalPosition ?? Vector2.Zero;
		}

		/// <summary>
		/// Get current car speed
		/// </summary>
		public float GetCarSpeed()
		{
			return GetCurrentSpeed();
		}

		/// <summary>
		/// Reset all car physics state to initial values
		/// Called when repositioning car to ensure clean state
		/// </summary>
		public void ResetCarState()
		{
			_velocity = Vector2.Zero;
			_currentSpeed = 0.0f;
			_targetPosition = Vector2.Zero;
			_lastTargetPosition = Vector2.Zero;
			_hasInput = false;
			
			// Also reset the car body's physics velocity
			if (_carBody != null)
			{
				_carBody.Velocity = Vector2.Zero;
			}
		}

		// ================================================================
		// DEPENDENCY MANAGEMENT
		// ================================================================

		/// <summary>
		/// Update system dependencies (useful when systems are recreated)
		/// </summary>
		public void UpdateDependencies(RacingCameraController cameraController, RacingTrackValidationSystem trackValidationSystem)
		{
			_cameraController = cameraController;
			_trackValidationSystem = trackValidationSystem;
		}

		/// <summary>
		/// Check if dependencies are properly initialized
		/// </summary>
		public bool HasValidDependencies()
		{
			return _cameraController != null && _trackValidationSystem != null;
		}

		// ================================================================
		// PLAYER INTEGRATION
		// ================================================================

		/// <summary>
		/// Set user data for the car player (for GameHost integration)
		/// </summary>
		public new void SetUserData(UserData userData)
		{
			base.SetUserData(userData);
		}

		/// <summary>
		/// Get the underlying BasePlayer instance for game controller integration
		/// </summary>
		public BasePlayer GetPlayer()
		{
			return this;
		}

		// ================================================================
		// PUBLIC ACCESSORS
		// ================================================================
		
		public CharacterBody2D GetCarBody() => _carBody;
		public Vector2 GetVelocity() => _velocity;
		public float GetCurrentSpeed() => _currentSpeed;
		public bool HasInput() => _hasInput;
		public Vector2 GetTargetPosition() => _targetPosition;
		public void SetTargetPosition(Vector2 target) => _targetPosition = target;

		// ================================================================
		// CLEANUP
		// ================================================================

		public override void _ExitTree()
		{
			if (_inputManager != null && GodotObject.IsInstanceValid(_inputManager))
			{
				_inputManager.TouchStarted -= OnTouchStarted;
				_inputManager.TouchEnded -= OnTouchEnded;
				_inputManager.ClickStarted -= OnClickStarted;
				_inputManager.ClickEnded -= OnClickEnded;
			}
			base._ExitTree();
		}
	}
}