using Godot;
using System.Collections.Generic;

/// <summary>
/// Traditional board game featuring physics-based striking and strategic pocket play
/// </summary>
[GlobalClass]
public partial class CarromGame : GameController
{
	// ================================================================
	// SIGNALS
	// ================================================================

	[Signal] public delegate void PiecePocketedEventHandler(string playerId, CarromPiece piece);
	[Signal] public delegate void StrikerFoulEventHandler(string playerId);
	[Signal] public delegate void TurnChangedEventHandler(string playerId, int turnNumber);

	// ================================================================
	// EXPORT PROPERTIES - CARROM SETTINGS
	// ================================================================

	[ExportCategory("Carrom Settings")]
	[Export] public bool ShowPracticeMode { get; set; } = true;
	[Export] public int CompetitiveCreditCost { get; set; } = 1;
	[Export] public float TurnTimeLimit { get; set; } = 30.0f;
	[Export] public CarromPhysicsConfig PhysicsConfig { get; set; }
	

	[ExportCategory("Physics Limits")]
	[Export] public float MaxVelocityLimit { get; set; } = 2000.0f;
	[Export] public float MaxAngularVelocity { get; set; } = 50.0f;
	[Export] public float VelocityAlertThreshold { get; set; } = 1800.0f;
	[Export] public float MinVelocityThreshold { get; set; } = 1.0f;
	[Export] public float AngularMinThreshold { get; set; } = 0.1f;

	[ExportCategory("Visual Feedback")]
	[Export] public float AimLineLength { get; set; } = 100.0f;
	[Export] public float PowerBarWidth { get; set; } = 60.0f;
	[Export] public float PowerBarHeight { get; set; } = 8.0f;

	// Board settings are now managed by the CarromBoard component itself

	[ExportCategory("Piece Templates")]
	[Export(PropertyHint.ResourceType, "PackedScene")] public PackedScene WhitePieceTemplate { get; set; }
	[Export(PropertyHint.ResourceType, nameof(PackedScene))] public PackedScene BlackPieceTemplate { get; set; }
	[Export(PropertyHint.ResourceType, nameof(PackedScene))] public PackedScene RedPieceTemplate { get; set; }
	[Export(PropertyHint.ResourceType, nameof(PackedScene))] public PackedScene StrikerTemplate { get; set; }

	// ================================================================
	// PRIVATE FIELDS - GAME LOGIC
	// ================================================================

	private CarromGameMode _carromGameMode = CarromGameMode.Practice;
	private CarromBoard _board;
	private CarromInputController _inputController;
	
	// Managers
	private CarromPracticeModeManager _practiceModeManager;
	private CarromCompetitiveModeManager _competitiveModeManager;
	private CarromModeManagerBase _currentModeManager;
	private CarromPieceFactory _pieceFactory;
	private CarromCameraController _cameraController;
	private CarromGameStateMachine _gameStateMachine;
	
	// Game state
	private bool _waitingForPiecesToStop = false;

	// ================================================================
	// INITIALIZATION
	// ================================================================

	public override void _Ready()
	{
		GameId = "carrom_game";
		SetGameMode(GameMode.Practice); // Start in practice mode

		// Initialize physics config
		if (PhysicsConfig == null)
		{
			PhysicsConfig = new CarromPhysicsConfig();
		}
		
		// Initialize systems in explicit order
		SetupBoard();
		SetupCameraController();
		SetupInputController();
		
		// Initialize game state machine for simplified state management
		InitializeGameStateMachine();
		
		// Initialize components explicitly after all nodes are found AND phase manager created
		InitializeComponents();
		
		// Initialize managers
		InitializeManagers();
		
		// Call base which triggers InitializeGame()
		base._Ready();
		
		// Context detection
		DetectAndAdaptToContext();
		
		// Start practice mode AFTER all managers are initialized
		// This prevents SetPhaseManager(null) errors during initialization
		StartPracticeMode();
	}

	protected override void InitializeGame()
	{
		base.InitializeGame();
	}

	/// <summary>
	/// Setup the carrom board
	/// </summary>
	private void SetupBoard()
	{
		try
		{
			// Get board from scene tree instead of creating it
			_board = GetNode<CarromBoard>("CarromBoard");
			
			if (!GodotObject.IsInstanceValid(_board))
			{
				GD.PrintErr("[CarromGame] Failed to find CarromBoard in scene tree");
				_board = null;
				return;
			}
			
			// Board should be self-contained with its own export properties
			
			// Connect board signals
			_board.PiecePocketed += OnPiecePocketed;
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[CarromGame] Exception setting up CarromBoard: {ex.Message}");
			_board = null;
		}
	}

	/// <summary>
	/// Setup camera controller to center on board at origin
	/// </summary>
	private void SetupCameraController()
	{
		if (_board == null) return;

		// Ensure board stays at origin (0,0)
		_board.GlobalPosition = Vector2.Zero;
		
		// Create and initialize camera controller
		_cameraController = new CarromCameraController();
		AddChild(_cameraController);
		_cameraController.Initialize(_board);
	}

	/// <summary>
	/// Setup input controller for striker control
	/// </summary>
	private void SetupInputController()
	{
		try
		{
			// Get input controller from scene tree instead of creating it
			_inputController = GetNode<CarromInputController>("CarromInputController");
			
			if (!IsInstanceValid(_inputController))
			{
				GD.PrintErr("[CarromGame] Failed to find CarromInputController in scene tree");
				_inputController = null;
				return;
			}
			
			// Connect input signals
			_inputController.StrikeExecuted += OnStrikeExecuted;
			_inputController.AimingStateChanged += OnAimingStateChanged;
			
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[CarromGame] Exception setting up CarromInputController: {ex.Message}");
			_inputController = null;
		}
	}

	/// <summary>
	/// Initialize components in explicit order after all nodes are discovered
	/// </summary>
	private void InitializeComponents()
	{
		// Initialize board first (it may need to setup internal state)
		if (_board != null)
		{
			// Board should be self-contained and fully initialized from its own exports
			_board.RefreshBoard(); // Ensure board is fully initialized
			
			// Configure physics config with official board scaling for proportional piece sizes
			if (PhysicsConfig != null)
			{
				PhysicsConfig.SetBoardScaling(
					_board.ScaleFactor,
					_board.PieceRadius,
					_board.OfficialStrikerRadius
				);
				
				// Pass physics config to board pockets for enhanced physics
				_board.SetPocketPhysicsConfig(PhysicsConfig);
			}
		}
		
		// Initialize input controller after board and camera are ready
		if (_inputController != null)
		{
			_inputController.InitializeWithBoard(_board);
			_inputController.SetCameraController(_cameraController);
			_inputController.SetGameState(_gameStateMachine);
			_inputController.SetPhysicsConfig(PhysicsConfig);
			
			// Pass visual parameters to input controller
			_inputController.SetVisualParameters(AimLineLength, PowerBarWidth, PowerBarHeight);
		}
	}

	
	/// <summary>
	/// Initialize game state machine - replaces complex distributed state management
	/// </summary>
	private void InitializeGameStateMachine()
	{
		_gameStateMachine = new CarromGameStateMachine();
		AddChild(_gameStateMachine);
		
		// Connect state machine signals
		_gameStateMachine.StateChanged += OnGameStateChanged;
		_gameStateMachine.SettlementCompleted += OnSettlementCompleted;
		
		GD.Print("[CarromGame] Game state machine initialized");
	}
	
	/// <summary>
	/// Setup state machine with current mode manager and pieces after mode initialization
	/// </summary>
	private void SetupStateMachineForCurrentMode()
	{
		if (_gameStateMachine == null || _currentModeManager == null)
		{
			GD.PrintErr("[CarromGame] Cannot setup state machine - missing components");
			return;
		}
		
		// Get all active pieces from current mode manager (includes striker)
		var pieces = _currentModeManager.GetActivePieces() ?? new System.Collections.Generic.List<CarromPiece>();
		
		// Initialize state machine with pieces and mode manager
		_gameStateMachine.Initialize(pieces, _currentModeManager);
		
		GD.Print($"[CarromGame] State machine setup complete for {_carromGameMode} mode with {pieces.Count} pieces");
	}

	/// <summary>
	/// Initialize manager components
	/// </summary>
	private void InitializeManagers()
	{
		// Initialize piece factory
		_pieceFactory = new CarromPieceFactory();
		AddChild(_pieceFactory);
		_pieceFactory.Initialize(_board, PhysicsConfig, WhitePieceTemplate, BlackPieceTemplate, RedPieceTemplate, StrikerTemplate);
		
		// Pass centralized physics limits to piece factory
		_pieceFactory.SetPhysicsLimits(MinVelocityThreshold, AngularMinThreshold, 
			MaxVelocityLimit, MaxAngularVelocity, VelocityAlertThreshold);
		
		// Initialize practice mode manager
		_practiceModeManager = new CarromPracticeModeManager();
		AddChild(_practiceModeManager);
		_practiceModeManager.Initialize(_board, _inputController, PhysicsConfig, BlackPieceTemplate, StrikerTemplate);
		// State machine manages phases now
		_practiceModeManager.SetPieceFactory(_pieceFactory);
		
		// Note: Board signals are handled through CarromGame.OnPiecePocketed() to avoid duplicate calls
		
		// Initialize competitive mode manager
		_competitiveModeManager = new CarromCompetitiveModeManager();
		AddChild(_competitiveModeManager);
		_competitiveModeManager.Initialize(_board, _inputController, PhysicsConfig, WhitePieceTemplate, BlackPieceTemplate, RedPieceTemplate, StrikerTemplate, CompetitiveCreditCost);

		_competitiveModeManager.SetPieceFactory(_pieceFactory);
		
		// Connect manager signals
		ConnectManagerSignals();
	}

	/// <summary>
	/// Connect signals from managers
	/// </summary>
	private void ConnectManagerSignals()
	{
		// Practice mode signals
		_practiceModeManager.PracticeResetRequested += OnPracticeResetRequested;
		_practiceModeManager.PracticeModeSetupComplete += OnPracticeModeSetupComplete;
		
		// Competitive mode signals
		_competitiveModeManager.TurnChanged += OnTurnChanged;
		_competitiveModeManager.PlayerWon += OnPlayerWon;
		_competitiveModeManager.FoulCommitted += OnFoulCommitted;
		_competitiveModeManager.CompetitiveModeSetupComplete += OnCompetitiveModeSetupComplete;
		
		// Piece factory signals
		_pieceFactory.PieceCreated += OnPieceCreated;
		_pieceFactory.PieceDestroyed += OnPieceDestroyed;
	}

	// ================================================================
	// CORE GAME LOOP
	// ================================================================

	/// <summary>
	/// Check if all pieces have stopped moving (delegated to current mode manager)
	/// </summary>
	private bool AreAllPiecesStopped()
	{
		// Delegate to current mode manager using polymorphism
		return _currentModeManager?.AreAllPiecesStopped() ?? true;
	}
	
	/// <summary>
	/// Get the current striker (delegated to current mode manager)
	/// </summary>
	private CarromPiece GetStriker()
	{
		return _currentModeManager?.GetStriker();
	}
	
	/// <summary>
	/// Update input controller with current striker from mode manager
	/// </summary>
	private void UpdateInputControllerStriker()
	{
		var striker = _currentModeManager?.GetStriker();
		_inputController?.SetStriker(striker);
	}

	// ================================================================
	// GAME MODES
	// ================================================================

	/// <summary>
	/// Start practice mode (single piece, free play)
	/// </summary>
	public virtual void StartPracticeMode()
	{
		// Clean up competitive mode before switching
		_competitiveModeManager?.CleanupMode();
		
		// Set current mode manager to practice
		_currentModeManager = _practiceModeManager;
		_carromGameMode = CarromGameMode.Practice;
		SetGameMode(GameMode.Practice);
		ResetGame();

		// Delegate to practice mode manager - it will emit PracticeModeSetupComplete when done
		_practiceModeManager?.SetupPracticeMode();
		
		// Setup state machine for practice mode
		SetupStateMachineForCurrentMode();
	}

	/// <summary>
	/// Start competitive mode (full carrom rules)
	/// </summary>
	public virtual async void StartCompetitiveMode()
	{
		if (_isGameActive) 
			return;
		
		// Clean up practice mode before switching
		_practiceModeManager?.CleanupMode();
		
		// Set current mode manager to competitive
		_currentModeManager = _competitiveModeManager;
		_carromGameMode = CarromGameMode.Competitive;
		SetGameMode(GameMode.TimeTrial); // Use TimeTrial for competitive tracking
		ResetGame();
		StartGame();
		
		// Delegate to competitive mode manager
		bool success = await _competitiveModeManager.StartCompetitiveMode();
		if (!success)
		{
			// Credits not spent or other failure
			return;
		}

		// Setup state machine for competitive mode
		SetupStateMachineForCurrentMode();
	}

	/// <summary>
	/// Reset game state - simplified without PhaseManager
	/// </summary>
	private void ResetGame()
	{
		_waitingForPiecesToStop = false;
		// Game state machine will handle state resets automatically
	}
	
	/// <summary>
	/// Request practice mode reset - relies on state machine guarantees for safe timing
	/// </summary>
	private void RequestPracticeReset()
	{
		if (_practiceModeManager == null)
		{
			GD.PrintErr("[CarromGame] Cannot request practice reset - practice manager not initialized");
			return;
		}
		
		// State machine guarantees safe timing for resets
		ExecutePracticeReset();
	}
	
	/// <summary>
	/// Execute the actual practice reset
	/// </summary>
	private void ExecutePracticeReset()
	{
		GD.Print("[CarromGame] Executing practice reset");
		
		try
		{
			_practiceModeManager?.ResetPracticeMode();
			
			// State machine will handle transitions automatically
			
			GD.Print("[CarromGame] Practice reset completed successfully");
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[CarromGame] Practice reset failed with exception: {ex.Message}");
		}
	}

	// ================================================================
	// INPUT HANDLING
	// ================================================================

	/// <summary>
	/// Handle strike execution from input controller - simplified without PhaseManager
	/// </summary>
	private void OnStrikeExecuted(Vector2 force)
	{
		// Get striker from current mode manager
		var striker = _currentModeManager?.GetStriker();
		if (striker != null)
		{
			striker.ApplyStrike(force);
		}
	}

	/// <summary>
	/// Handle aiming state changes
	/// </summary>
	private void OnAimingStateChanged(bool isAiming, Vector2 aimDirection, float power)
	{
		// Update UI or visual feedback based on aiming state
		QueueRedraw();
	}

	// ================================================================
	// GAME EVENTS
	// ================================================================

	/// <summary>
	/// Handle piece being pocketed with comprehensive logging
	/// </summary>
	private void OnPiecePocketed(CarromPiece piece)
	{
		if (piece == null || !GodotObject.IsInstanceValid(piece))
		{
			GD.PrintErr("[CarromGame] OnPiecePocketed called with invalid piece");
			return;
		}
		
		string pieceType = piece.Type.ToString();
		bool isStriker = piece.Type == PieceType.Striker;
		string playerId = GetCurrentPlayerId();

		// Delegate to current mode manager using polymorphism
		_currentModeManager?.OnPiecePocketed(piece);
		
		// Emit main game signal
		EmitSignal(SignalName.PiecePocketed, playerId, piece);
	}

	/// <summary>
	/// Handle piece collision for sound/effects
	/// </summary>
	private void OnPieceCollided(CarromPiece piece, CarromPiece otherPiece, Vector2 impactForce)
	{
		// Handle collision effects (sound, particles, etc.)
		float impactMagnitude = impactForce.Length();
		
		// Play collision sound based on impact force
		// Add particle effects, etc.
	}

	/// <summary>
	/// Handle piece stopped signal - simplified since CarromGameStateMachine handles settlement
	/// </summary>
	private void OnPieceStoppedSignal(CarromPiece stoppedPiece) { }

	/// <summary>
	/// Get point value for a piece type
	/// </summary>
	private float GetPieceValue(PieceType type)
	{
		return type switch
		{
			PieceType.Red => 10.0f,  // Queen is worth more
			PieceType.White => 1.0f,
			PieceType.Black => 1.0f,
			_ => 0.0f
		};
	}

	/// <summary>
	/// Reset a single piece to its starting position
	/// </summary>
	private void ResetPieceToStart(CarromPiece piece, Vector2 startPosition)
	{
		if (!GodotObject.IsInstanceValid(piece)) return;
		
		// Use the immediate synchronous reset method with global coordinates
		piece.Reset(ToGlobal(startPosition));
	}

	/// <summary>
	/// Get current player ID
	/// </summary>
	private string GetCurrentPlayerId()
	{
		if (_carromGameMode == CarromGameMode.Practice)
		{
			return "practice";
		}
		else
		{
			return _competitiveModeManager?.GetCurrentPlayer()?.PlayerId ?? "player1";
		}
	}
	
	/// <summary>
	/// Log comprehensive game state for debugging
	/// </summary>
	private void LogGameState(string context)
	{
		GD.Print($"[CarromGame] === GAME STATE: {context} ===");
		GD.Print($"Game State: {_gameStateMachine?.GetCurrentStateName() ?? "Unknown"}");
		GD.Print($"Game Mode: {_carromGameMode}");
		GD.Print($"State Machine: {_gameStateMachine?.GetStateDebugInfo() ?? "Not initialized"}");
		
		var striker = _currentModeManager?.GetStriker();
		if (striker != null && GodotObject.IsInstanceValid(striker))
		{
			GD.Print($"Striker - Visible: {striker.Visible}, Freeze: {striker.Freeze}, Position: {striker.GlobalPosition}, Stopped: {striker.IsStopped()}");
		}
		else
		{
			GD.Print("Striker: Invalid or null");
		}
		
		GD.Print($"All Pieces Stopped: {AreAllPiecesStopped()}");
		
		if (_inputController != null)
		{
			GD.Print($"Input - Enabled: {_inputController.IsInputEnabled()}, GameState: {_inputController.GetGameStateStatus()}, HasGameState: {_inputController.HasGameState()}");
		}
		else
		{
			GD.Print("Input Controller: null");
		}
		
		GD.Print($"=== END GAME STATE ===");
	}
	
	/// <summary>
	/// Centralized striker restoration method - replaces distributed logic across mode managers
	/// </summary>
	public bool RestoreStrikerToBaseline(int? playerIndexOverride = null)
	{
		var striker = _currentModeManager?.GetStriker();
		if (striker == null || !GodotObject.IsInstanceValid(striker))
		{
			GD.PrintErr("[CarromGame] RestoreStrikerToBaseline: No valid striker found");
			return false;
		}

		// Determine appropriate baseline position based on game mode
		Vector2 baselinePosition;
		int playerIndex;
		
		if (_carromGameMode == CarromGameMode.Practice)
		{
			// Practice mode: Always use player 0 baseline (bottom)
			playerIndex = 0;
			baselinePosition = _board?.GetBaselinePosition(playerIndex) ?? Vector2.Zero;
		}
		else
		{
			// Competitive mode: Use specified player index or current player's baseline
			if (playerIndexOverride.HasValue)
			{
				playerIndex = playerIndexOverride.Value;
			}
			else
			{
				// Default to current player
				playerIndex = _competitiveModeManager?.GetCurrentPlayer()?.PlayerId == "player1" ? 0 : 1;
			}
			
			baselinePosition = _board?.GetBaselinePosition(playerIndex) ?? Vector2.Zero;
		}
		
		// Convert to global coordinates
		Vector2 globalBaselinePosition = _board?.ToGlobal(baselinePosition) ?? Vector2.Zero;

		// Perform the restoration using CarromPiece's immediate reset method for predictable state
		try
		{
			// Use immediate physics reset to guarantee stopped state
			striker.Reset(globalBaselinePosition, immediate: true);
			
			// Mark this restoration in the mode manager's settlement context
			MarkRecentRestoration(striker);
			
			// Validate restoration succeeded (position and visibility)
			bool restorationSucceeded = ValidateStrikerRestoration(striker, globalBaselinePosition);
			return restorationSucceeded;
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[CarromGame] RestoreStrikerToBaseline: Exception during restoration: {ex.Message}");
			return false;
		}
	}
	
	/// <summary>
	/// Mark recent restoration in current mode manager's settlement context
	/// </summary>
	private void MarkRecentRestoration(CarromPiece piece)
	{
		// Cast current mode manager to base class to access settlement context
		if (_currentModeManager is CarromModeManagerBase modeManager)
		{
			modeManager.MarkRecentRestoration(piece);
			GD.Print($"[CarromGame] MarkRecentRestoration: Marked {piece.Type} as recently restored in settlement context");
		}
		else
		{
			GD.PrintErr($"[CarromGame] MarkRecentRestoration: Could not cast mode manager to base class");
		}
	}
	
	/// <summary>
	/// Validate that striker restoration was successful - focuses on position and visibility, not physics state
	/// </summary>
	private bool ValidateStrikerRestoration(CarromPiece striker, Vector2 expectedPosition)
	{
		if (striker == null || !IsInstanceValid(striker))
		{
			return false;
		}
		
		// Check visibility was restored
		if (!striker.Visible)
		{
			GD.PrintErr("[CarromGame] Striker restoration validation failed: Still invisible");
			return false;
		}
		
		// Check freeze state was cleared
		if (striker.Freeze)
		{
			GD.PrintErr("[CarromGame] Striker restoration validation failed: Still frozen");
			return false;
		}
		
		// Check position is reasonable (within tolerance)
		float positionTolerance = 100.0f; // Allow 100 pixel tolerance for validation
		float actualDistance = striker.GlobalPosition.DistanceTo(expectedPosition);
		if (actualDistance > positionTolerance)
		{
			GD.PrintErr($"[CarromGame] Striker restoration validation failed: Position too far from expected - Distance: {actualDistance:F2}, Expected: {expectedPosition}, Actual: {striker.GlobalPosition}");
			return false;
		}
		
		// NOTE: We don't check IsStopped() here because with deferred physics reset,
		// the striker may still have velocity that will be cleared later
		
		return true;
	}

	// ================================================================
	// CONTEXT DETECTION
	// ================================================================

	private void DetectAndAdaptToContext()
	{
		var gameHost = GameHost.GetInstance();
		
		if (gameHost != null && GodotObject.IsInstanceValid(gameHost))
		{
			// Production context - integrate with platform
			var playerSession = gameHost.GetPlayerSession("default");
			if (playerSession != null)
			{
				// Setup player integration
				var player = new CarromPlayer();
				player.PlayerId = playerSession.PlayerId;
				player.SetUserData(playerSession.UserData);
				AddPlayer(player);
			}
		}
		else
		{
			// Development context - create default player
			var player = new CarromPlayer();
			player.PlayerId = "dev_player";
			AddPlayer(player);
		}
	}

	// ================================================================
	// UI INTEGRATION OVERRIDES
	// ================================================================

	/// <summary>
	/// Override to provide carrom-specific context buttons
	/// </summary>
	public override ContextButtonData[] GetContextButtons()
	{
		var buttons = new List<ContextButtonData>();

		// Standard "Return to Menu" button
		buttons.Add(GameContextButton.CreateReturnToMenuButton(() => {
			var userManager = UserManager.GetAutoload();
			userManager?.ResetUserIdleTimer();
			ReturnToMainMenu();
		}));

		// Pause/Resume button
		if (CanPause)
		{
			if (_isGamePaused)
			{
				buttons.Add(GameContextButton.CreateResumeButton(() => {
					ResumeGame();
					RefreshUI();
				}));
			}
			else if (_isGameActive)
			{
				buttons.Add(GameContextButton.CreatePauseButton(() => {
					PauseGame();
					RefreshUI();
				}));
			}
		}

		// Mode-specific buttons
		if (_carromGameMode == CarromGameMode.Practice)
		{
			buttons.Add(new ContextButtonData("Reset", () => {
				var userManager = UserManager.GetAutoload();
				userManager?.ResetUserIdleTimer();
				RequestPracticeReset();
			}, "🔄", true, "Reset practice session"));
			
			buttons.Add(new ContextButtonData("Play Competitive", () => {
				var userManager = UserManager.GetAutoload();
				userManager?.ResetUserIdleTimer();
				StartCompetitiveMode();
			}, "🏆", true, "Start competitive match"));
		}

		return buttons.ToArray();
	}

	/// <summary>
	/// Override to provide carrom game title
	/// </summary>
	public override string GetGameTitle()
	{
		return _carromGameMode switch
		{
			CarromGameMode.Practice => "Carrom - Practice",
			CarromGameMode.Competitive => "Carrom - Competitive",
			_ => "Carrom"
		};
	}

	// ================================================================
	// SIGNAL HANDLERS FOR MANAGERS
	// ================================================================

	
	/// <summary>
	/// Handle game state changes from state machine
	/// </summary>
	private void OnGameStateChanged(CarromGameStateMachine.GameState oldState, CarromGameStateMachine.GameState newState)
	{
		GD.Print($"[CarromGame] Game state changed: {oldState} → {newState}");
	}
	
	/// <summary>
	/// Handle settlement completion from state machine
	/// </summary>
	private void OnSettlementCompleted()
	{
		GD.Print("[CarromGame] Settlement completed - game ready for next turn");
	}
	

	/// <summary>
	/// Handle practice reset request from practice mode manager
	/// </summary>
	private void OnPracticeResetRequested()
	{
		// Phase transition is handled by practice mode manager's DeferredPhaseTransition()
		// Additional game state reset if needed
	}

	/// <summary>
	/// Handle practice mode setup completion with comprehensive validation
	/// </summary>
	private void OnPracticeModeSetupComplete()
	{
		// Validate all critical components before enabling input
		bool validationPassed = ValidateInputControllerSynchronization();
		if (validationPassed)
		{
			GD.Print("[CarromGame] OnPracticeModeSetupComplete: Validation passed - starting game");
		}
		else
		{
			GD.PrintErr("[CarromGame] OnPracticeModeSetupComplete: Validation failed - attempting recovery");
			RecoverInputControllerSynchronization();
		}
	}
	
	/// <summary>
	/// Validate that input controller is properly synchronized with all dependencies
	/// </summary>
	private bool ValidateInputControllerSynchronization()
	{
		GD.Print("[CarromGame] ValidateInputControllerSynchronization: Starting validation");
		
		// Check input controller exists and is valid
		if (_inputController == null || !GodotObject.IsInstanceValid(_inputController))
		{
			GD.PrintErr("[CarromGame] Validation failed: Input controller is null or invalid");
			return false;
		}
		
		// Check phase manager exists and is valid
		if (_gameStateMachine == null || !GodotObject.IsInstanceValid(_gameStateMachine))
		{
			GD.PrintErr("[CarromGame] Validation failed: Game state machine is null or invalid");
			return false;
		}
		
		// Check current mode manager exists and is valid
		if (_currentModeManager == null)
		{
			GD.PrintErr("[CarromGame] Validation failed: Current mode manager is null");
			return false;
		}
		
		// Check striker exists and is valid
		var striker = _currentModeManager.GetStriker();
		if (striker == null || !GodotObject.IsInstanceValid(striker))
		{
			GD.PrintErr("[CarromGame] Validation failed: Striker is null or invalid");
			return false;
		}
		
		// Validate input controller has proper references
		if (!_inputController.HasGameState())
		{
			GD.PrintErr("[CarromGame] Validation failed: Input controller lacks game state reference");
			return false;
		}
		
		// Check that input is in a valid state
		if (!_inputController.IsInputEnabled())
		{
			GD.Print("[CarromGame] Input currently disabled - this may be normal during setup");
		}
		
		GD.Print("[CarromGame] ValidateInputControllerSynchronization: All validations passed");
		return true;
	}
	
	/// <summary>
	/// Attempt to recover input controller synchronization if validation fails
	/// </summary>
	private void RecoverInputControllerSynchronization()
	{
		GD.Print("[CarromGame] RecoverInputControllerSynchronization: Starting recovery");
		
		// Re-establish game state reference
		if (_inputController != null && _gameStateMachine != null)
		{
			_inputController.SetGameState(_gameStateMachine);
			GD.Print("[CarromGame] Recovery: Re-established game state reference");
		}
		
		// Update striker reference
		UpdateInputControllerStriker();
		GD.Print("[CarromGame] Recovery: Updated striker reference");
		
		// Re-validate after recovery
		bool recoverySuccessful = ValidateInputControllerSynchronization();
		
		if (recoverySuccessful)
		{
			GD.Print("[CarromGame] Recovery successful - starting game");
			// State machine handles game start automatically
		}
		else
		{
			GD.PrintErr("[CarromGame] Recovery failed - game may not function properly");
			// Force start anyway to prevent complete blockage
			// State machine handles game start automatically
		}
	}
	
	
	
	/// <summary>
	/// DEBUG METHOD: Force enable input for debugging stuck input states
	/// Call this method from debugger or add temporary UI button during development
	/// </summary>
	public void DEBUG_ForceEnableInput()
	{
		GD.Print("[CarromGame] DEBUG_ForceEnableInput: Force enabling input for debugging");
		
		// State machine handles transitions automatically
		
		// Force input controller to enabled state
		if (_inputController != null)
		{
			// This assumes SetInputState method exists - may need to adjust based on actual InputController API
			GD.Print("[CarromGame] DEBUG: Attempting to force input controller to enabled state");
			
			// Re-establish all references
			_inputController.SetGameState(_gameStateMachine);
			UpdateInputControllerStriker();
			
			GD.Print("[CarromGame] DEBUG: Re-established input controller references");
		}
		
		// Log current state for debugging
		LogGameState("DEBUG Force Enable Input");
	}

	/// <summary>
	/// Handle turn change in competitive mode
	/// </summary>
	private void OnTurnChanged(string playerId, int turnNumber)
	{
		EmitSignal(SignalName.TurnChanged, playerId, turnNumber);
	}

	/// <summary>
	/// Handle player winning in competitive mode
	/// </summary>
	private void OnPlayerWon(string playerId)
	{
		// Handle win condition
		EndGame();
	}

	/// <summary>
	/// Handle foul committed in competitive mode
	/// </summary>
	private void OnFoulCommitted(string playerId)
	{
		EmitSignal(SignalName.StrikerFoul, playerId);
	}

	/// <summary>
	/// Handle competitive mode setup completion
	/// </summary>
	private void OnCompetitiveModeSetupComplete()
	{
		// State machine handles game start automatically
	}

	/// <summary>
	/// Handle piece creation from factory (mode managers handle tracking)
	/// </summary>
	private void OnPieceCreated(CarromPiece piece)
	{
		// Connect piece signals for main game events
		piece.PieceCollided += OnPieceCollided;
		piece.PieceStopped += OnPieceStoppedSignal;
		
		// Update input controller if this is a striker
		if (piece.Type == PieceType.Striker)
		{
			UpdateInputControllerStriker();
		}
	}

	/// <summary>
	/// Handle piece destruction from factory (mode managers handle tracking)
	/// </summary>
	private void OnPieceDestroyed(CarromPiece piece)
	{
		// Clean up piece signals if needed
		if (GodotObject.IsInstanceValid(piece))
		{
			piece.PieceCollided -= OnPieceCollided;
			piece.PieceStopped -= OnPieceStoppedSignal;
		}
		
		// Update input controller if striker was destroyed
		if (piece?.Type == PieceType.Striker)
		{
			_inputController?.SetStriker(null);
		}
	}

	/// <summary>
	/// Disconnect signals from managers
	/// </summary>
	private void DisconnectManagerSignals()
	{
		// Practice mode signals
		if (_practiceModeManager != null && GodotObject.IsInstanceValid(_practiceModeManager))
		{
			_practiceModeManager.PracticeResetRequested -= OnPracticeResetRequested;
			_practiceModeManager.PracticeModeSetupComplete -= OnPracticeModeSetupComplete;
		}
		
		// Competitive mode signals
		if (_competitiveModeManager != null && GodotObject.IsInstanceValid(_competitiveModeManager))
		{
			_competitiveModeManager.TurnChanged -= OnTurnChanged;
			_competitiveModeManager.PlayerWon -= OnPlayerWon;
			_competitiveModeManager.FoulCommitted -= OnFoulCommitted;
			_competitiveModeManager.CompetitiveModeSetupComplete -= OnCompetitiveModeSetupComplete;
		}
		
		// Piece factory signals
		if (_pieceFactory != null && GodotObject.IsInstanceValid(_pieceFactory))
		{
			_pieceFactory.PieceCreated -= OnPieceCreated;
			_pieceFactory.PieceDestroyed -= OnPieceDestroyed;
		}
	}

	public override void _ExitTree()
	{
		base._ExitTree();

		// Clean up signals and references
		if (_board != null && GodotObject.IsInstanceValid(_board))
		{
			_board.PiecePocketed -= OnPiecePocketed;
		}
			
		if (_inputController != null && GodotObject.IsInstanceValid(_inputController))
		{
			_inputController.StrikeExecuted -= OnStrikeExecuted;
			_inputController.AimingStateChanged -= OnAimingStateChanged;
		}
			
			
		// Clean up manager signals
		DisconnectManagerSignals();
	}
}