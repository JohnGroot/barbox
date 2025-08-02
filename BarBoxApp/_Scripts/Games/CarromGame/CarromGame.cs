using Godot;
using System.Collections.Generic;
using System.Linq;

// CARROMS TODO:
// - Setup rounded corner board edge & collider
// - Setup actual Team-Based Rounds (1v1 or 2v2) 
//		- Login for "ranked" play?
// - Setup example of "pooled" credit usage
// - Add particle effect on enter pocket
// - 

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

	// Settlement state tracking to prevent infinite loops
	private bool _settlementDetected = false;

	[ExportCategory("Strike Controls")]
	[Export] public float StrikeDeadZone { get; set; } = 30.0f;
	[Export] public float MaxAimDistance { get; set; } = 200.0f;
	[Export] public float LateralSensitivity { get; set; } = 1.5f;
	[Export(PropertyHint.Range, "5.0,90.0,1.0")] public float LateralAngleThreshold { get; set; } = 45.0f;

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
	[Export(PropertyHint.ResourceType, "PackedScene")] public PackedScene BlackPieceTemplate { get; set; }
	[Export(PropertyHint.ResourceType, "PackedScene")] public PackedScene RedPieceTemplate { get; set; }
	[Export(PropertyHint.ResourceType, "PackedScene")] public PackedScene StrikerTemplate { get; set; }

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
	private CarromPhaseManager _phaseManager;
	
	// Game state
	private bool _waitingForPiecesToStop = false;

	public GamePhase CurrentPhase => _phaseManager?.CurrentPhase ?? GamePhase.Setup;

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
		
		// Initialize components explicitly after all nodes are found
		InitializeComponents();
		
		// Initialize phase manager first
		InitializePhaseManager();
		
		// Initialize managers
		InitializeManagers();
		
		// Call base which triggers InitializeGame()
		base._Ready();
		
		// Context detection
		DetectAndAdaptToContext();
	}

	protected override void InitializeGame()
	{
		base.InitializeGame();
		StartPracticeMode(); // Default to practice mode
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
				
				// Pass phase manager to board pockets for phase-aware detection
				if (_phaseManager != null)
				{
					_board.SetPocketPhaseManager(_phaseManager);
				}
			}
		}
		
		// Initialize input controller after board and camera are ready
		if (_inputController != null)
		{
			_inputController.InitializeWithBoard(_board);
			_inputController.SetCameraController(_cameraController);
			_inputController.SetPhaseManager(_phaseManager);
			_inputController.SetPhysicsConfig(PhysicsConfig);
			
			// Pass centralized parameters to input controller (strike power now from physics config)
			_inputController.SetStrikeParameters( StrikeDeadZone, 
				MaxAimDistance, LateralSensitivity, LateralAngleThreshold, PhysicsConfig.MaxStrikeAngle);
			_inputController.SetVisualParameters(AimLineLength, PowerBarWidth, PowerBarHeight);
		}
	}

	/// <summary>
	/// Initialize phase manager
	/// </summary>
	private void InitializePhaseManager()
	{
		_phaseManager = new CarromPhaseManager();
		AddChild(_phaseManager);
		
		// Connect phase manager signals
		_phaseManager.PiecesSettled += OnPiecesSettled;
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
		_practiceModeManager.SetPhaseManager(_phaseManager);
		_practiceModeManager.SetPieceFactory(_pieceFactory);
		
		// Note: Board signals are handled through CarromGame.OnPiecePocketed() to avoid duplicate calls
		
		// Initialize competitive mode manager
		_competitiveModeManager = new CarromCompetitiveModeManager();
		AddChild(_competitiveModeManager);
		_competitiveModeManager.Initialize(_board, _inputController, PhysicsConfig, WhitePieceTemplate, BlackPieceTemplate, RedPieceTemplate, StrikerTemplate, CompetitiveCreditCost);
		_competitiveModeManager.SetPhaseManager(_phaseManager);
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
		
		// Restore phase manager reference that was lost during cleanup
		_practiceModeManager?.SetPhaseManager(_phaseManager);
		
		_carromGameMode = CarromGameMode.Practice;
		SetGameMode(GameMode.Practice);
		ResetGame();

		// Delegate to practice mode manager - it will emit PracticeModeSetupComplete when done
		_practiceModeManager?.SetupPracticeMode();
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
		
		// Restore phase manager reference that was lost during cleanup
		_competitiveModeManager?.SetPhaseManager(_phaseManager);
		
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
		
		_phaseManager?.StartGame();
	}

	/// <summary>
	/// Reset game state
	/// </summary>
	private void ResetGame()
	{
		_phaseManager?.ResetGame();
		_waitingForPiecesToStop = false;
	}

	// ================================================================
	// INPUT HANDLING
	// ================================================================

	/// <summary>
	/// Handle strike execution from input controller (delegated to mode manager)
	/// </summary>
	private void OnStrikeExecuted(Vector2 force)
	{
		if (!_phaseManager.CanReceiveInput()) 
			return;

		// Get striker from current mode manager
		var striker = _currentModeManager?.GetStriker();
		if (striker != null)
		{
			striker.ApplyStrike(force);

			// Reset settlement state for new processing cycle
			_settlementDetected = false;
			_phaseManager?.BeginProcessing();
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
	/// Handle piece being pocketed
	/// </summary>
	private void OnPiecePocketed(CarromPiece piece)
	{
		// Delegate to current mode manager using polymorphism
		_currentModeManager?.OnPiecePocketed(piece);
		
		// Emit main game signal
		string playerId = GetCurrentPlayerId();
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
	/// Handle individual piece stopped signal to coordinate settlement detection
	/// </summary>
	private void OnPieceStoppedSignal(CarromPiece stoppedPiece)
	{
		// Only check settlement during processing phase and if not already detected
		if (_phaseManager != null && _phaseManager.IsProcessing() && !_settlementDetected)
		{
			// Check if all pieces have now stopped
			if (AreAllPiecesStopped())
			{
				_settlementDetected = true; // Prevent duplicate settlement calls
				_phaseManager.OnPiecesSettled();
			}
		}
	}

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
				_practiceModeManager?.ResetPracticeMode();
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
	/// Handle pieces settled from phase manager (centralized)
	/// </summary>
	private void OnPiecesSettled()
	{
		// Process settlement based on game mode
		ProcessPiecesSettlement();
	}
	
	/// <summary>
	/// Process pieces settlement using current mode manager (polymorphic)
	/// </summary>
	private void ProcessPiecesSettlement()
	{
		// Delegate to current mode manager using polymorphism
		_currentModeManager?.OnPiecesSettled();
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
	/// Handle practice mode setup completion
	/// </summary>
	private void OnPracticeModeSetupComplete()
	{
		_phaseManager?.StartGame();
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
		_phaseManager?.StartGame();
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
			
		if (_phaseManager != null && GodotObject.IsInstanceValid(_phaseManager))
		{
			_phaseManager.PiecesSettled -= OnPiecesSettled;
		}
			
		// Clean up manager signals
		DisconnectManagerSignals();
	}
}