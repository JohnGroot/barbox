using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
	[Signal] public delegate void PenaltyPiecesTweenCompletedEventHandler();

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

	[ExportCategory("Debug Options")]
	[Export] public bool EnableTrails { get; set; } = false;

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
	private CarromEventService _carromEventService;

	// Managers
	private CarromPracticeModeManager _practiceModeManager;
	private CarromCompetitiveModeManager _competitiveModeManager;
	private CarromModeManagerBase _currentModeManager;
	private CarromPieceFactory _pieceFactory;
	private CarromCameraController _cameraController;
	private CarromGameStateMachine _gameStateMachine;
	
	// UI Components
	private CarromScoreDisplay _scoreDisplay;
	private CarromNotificationSystem _notificationSystem;
	private CarromPlayerSetupMenu _playerSetupMenu;
	private int _pendingPlayerCount = 2; // Track player count for menu → game transition
	
	// Game state is now managed entirely by CarromGameStateMachine

	// Animation components
	private Tween _strikerTween;
	private Tween _penaltyPiecesTween;
	private int _penaltyPieceIndex = 0;

	// Turn transition tracking for highlight timing
	private bool _waitingForTurnTransition = false;

	// ================================================================
	// INITIALIZATION
	// ================================================================

	public override void _Ready()
	{
		GameId = "carrom_game";
		SetGameMode(GameMode.Practice); // Start in practice mode

		// Initialize event service
		_carromEventService = new CarromEventService(EventService.GetInstance());

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
		
		// Initialize UI components
		InitializeScoreDisplay();

		// Call base which triggers InitializeGame()
		base._Ready();
		
		// Context detection
		DetectAndAdaptToContext();
		
		// Load user data after context detection
		LoadUserDataAsync();
		
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
			
			var pockets = _board.GetPockets();
			foreach (var pocket in pockets)
			{
				if (pocket != null)
				{
					pocket.PiecePocketed += OnPiecePocketed;
				}
			}
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
		
		// Connect camera signals
		_cameraController.CameraTransitionCompleted += OnCameraTransitionCompleted;
		
		// Connect penalty tween signals
		PenaltyPiecesTweenCompleted += OnPenaltyPiecesTweenCompleted;
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
		
		// Initialize trail system based on debug setting
		InitializeTrailSystem();
	}

	/// <summary>
	/// Initialize trail system for debug visualization
	/// </summary>
	private void InitializeTrailSystem()
	{
		// Enable trails globally based on debug setting
		CarromPiece.SetTrailsEnabled(EnableTrails);
		
		// Trail system initialized
	}
	
	/// <summary>
	/// Clear all piece trails
	/// </summary>
	private void ClearAllTrails()
	{
		// Find all CarromPiece instances in the scene tree
		var allPieces = GetTree().GetNodesInGroup("pieces");
		if (allPieces.Count == 0)
		{
			// Fallback: search for CarromPiece nodes in the board
			if (_board != null)
			{
				foreach (Node child in _board.GetChildren())
				{
					if (child is CarromPiece piece)
					{
						piece.ClearTrail();
					}
				}
			}
		}
		else
		{
			// Use group-based approach
			foreach (Node node in allPieces)
			{
				if (node is CarromPiece piece && GodotObject.IsInstanceValid(piece))
				{
					piece.ClearTrail();
				}
			}
		}
		
		// All piece trails cleared
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
		_gameStateMachine.InputAvailabilityChanged += OnInputAvailabilityChanged;
		_gameStateMachine.ReadyForInput += OnReadyForInput;
		
		// Game state machine initialized
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
		
		// State machine setup complete
	}

	/// <summary>
	/// Initialize score display UI component
	/// </summary>
	private void InitializeScoreDisplay()
	{
		_scoreDisplay = new CarromScoreDisplay();
		AddChild(_scoreDisplay);
		_scoreDisplay.SetVisible(false); // Hidden by default, shown in competitive mode

		// Connect to Pass Turn signal for manual turn advancement
		_scoreDisplay.PassTurnRequested += OnPassTurnRequested;

		// Initialize notification system (positioned below top menu bar)
		_notificationSystem = new CarromNotificationSystem();
		AddChild(_notificationSystem);
		_notificationSystem.Hide(); // Hidden by default, shown in competitive mode

		// Initialize player setup menu
		_playerSetupMenu = new CarromPlayerSetupMenu();
		AddChild(_playerSetupMenu);
		_playerSetupMenu.GameStartRequested += OnPlayerSetupMenuGameStartRequested;
		_playerSetupMenu.MenuCancelled += OnPlayerSetupMenuCancelled;
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
		_competitiveModeManager.TurnReadyForPass += OnTurnReadyForPass;
		_competitiveModeManager.PlayerWon += OnPlayerWon;
		_competitiveModeManager.FoulCommitted += OnFoulCommitted;
		_competitiveModeManager.CompetitiveModeSetupComplete += OnCompetitiveModeSetupComplete;
		_competitiveModeManager.NotificationRequested += OnNotificationRequested;
		
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
	/// Provides comprehensive help content for the Carrom Game
	/// Explains traditional Carrom rules with ICF scoring, gameplay mechanics, and competitive modes
	/// </summary>
	protected override HelpContentData GetHelpContent()
	{
		return new HelpContentData("CARROM HOW-TO - Traditional Rules with ICF Scoring")
			.AddSection("🎯 WELCOME TO CARROM 🎯",
				"• Pocket all your assigned pieces (⚪ White or ⚫ Black) to win the game",
				"• Use the striker to hit pieces into corner pockets",
				"• The red piece (👑 Queen) is worth 3 points and must be 'covered' by your piece",
				"• Practice Mode lets you play freely - Competitive Mode follows official ICF rules!")

			.AddSection("🎱 HOW TO PLAY 🎱",
				"• Drag DOWN from the striker to aim, then release to shoot",
				"• Drag LEFT or RIGHT on the Striker to reposition it on the baseline",
				"• Pocket your color pieces while avoiding opponent pieces and fouls",
				"• Valid shots require hitting your own pieces first or pocketing any piece")

			.AddSection("⚠️ RULES & FOULS ⚠️",
				"• CONTINUE TURN: Pocket your piece(s) legally = shoot again!",
				"• FOUL CONDITIONS: Striker pocketed, opponent piece hit first, or leaves the board",
				"• FOUL PENALTY: Lose your turn + return one of your pocketed pieces to center",
				"• QUEEN COVERING: Must pocket your own piece immediately after Queen in same turn",
				"• BREAKING RULE: 3 attempts maximum to disturb pieces, then turn passes")

			.AddSection("🏆 COMPETITIVE MODES 🏆",
				"• 2-Player Singles: Each player owns one color (White vs Black pieces)",
				"• 4-Player Doubles: Teams of 2, partners sit opposite (White team vs Black team)",
				"• WIN CONDITION: Pocket all 9 of your pieces + cover Queen if pocketed",
				"• Queen worth 3 points when properly covered");
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

		// Hide score display and notification system
		_scoreDisplay?.SetVisible(false);
		_notificationSystem?.Hide();

		// Reset camera to default rotation (player 0 position)
		_cameraController?.ResetRotation();

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
	/// Start competitive mode (full carrom rules) - now shows player setup menu first
	/// </summary>
	public virtual void StartCompetitiveMode(int playerCount = 2)
	{
		if (_isGameActive)
			return;

		// Store player count for when menu signals game start
		_pendingPlayerCount = playerCount;

		// Show player setup menu instead of immediately starting
		if (_playerSetupMenu != null)
		{
			_playerSetupMenu.ShowMenu(playerCount, CompetitiveCreditCost);
		}
	}

	/// <summary>
	/// Actually start the competitive mode after player setup is complete
	/// </summary>
	private void StartCompetitiveModeInternal(int playerCount)
	{
		if (_isGameActive)
			return;

		// Clean up practice mode before switching
		_practiceModeManager?.CleanupMode();

		// Configure player count
		_competitiveModeManager?.SetPlayerCount(playerCount);

		// Set current mode manager to competitive
		_currentModeManager = _competitiveModeManager;
		_carromGameMode = CarromGameMode.Competitive;
		SetGameMode(GameMode.TimeTrial); // Use TimeTrial for competitive tracking
		ResetGame();
		StartGame();

		// Delegate to competitive mode manager
		bool success = _competitiveModeManager.StartCompetitiveMode();
		if (!success)
		{
			// Failed to setup competitive mode
			return;
		}

		// Setup state machine for competitive mode
		SetupStateMachineForCurrentMode();
	}

	/// <summary>
	/// Reset game state - state machine handles all state management
	/// </summary>
	private void ResetGame()
	{
		// Game state machine will handle state resets automatically
		// No manual state flags needed
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
		// Executing practice reset
		
		try
		{
			_practiceModeManager?.ResetPracticeMode();
			
			// State machine will handle transitions automatically
			
			// Practice reset completed successfully
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
		// Stop highlights on all pieces when strike is executed
		StopAllPieceHighlights();

		// Clear any sticky notifications when striker is hit
		_notificationSystem?.ClearStickyNotification();

		// Get striker from current mode manager
		var striker = _currentModeManager?.GetStriker();
		if (striker != null)
		{
			striker.ApplyStrike(force);

			// Record shot in competitive mode for statistics
			if (_carromGameMode == CarromGameMode.Competitive && _competitiveModeManager != null)
			{
				var currentPlayer = _competitiveModeManager.GetCurrentPlayer();
				currentPlayer?.RecordShot();
			}
		}
	}

	/// <summary>
	/// Stop highlights on all pieces
	/// </summary>
	private void StopAllPieceHighlights()
	{
		// Get all active pieces from the current mode (includes striker)
		var activePieces = _currentModeManager?.GetActivePieces();
		if (activePieces == null)
		{
			return;
		}

		// Stop highlight on all pieces
		foreach (var piece in activePieces)
		{
			if (GodotObject.IsInstanceValid(piece))
			{
				piece.StopHighlight();
			}
		}
	}

	/// <summary>
	/// Start piece highlights for a specific player by player index
	/// </summary>
	/// <param name="playerIndex">0=player1(White), 1=player2(Black), etc.</param>
	private void StartPieceHighlightsForPlayer(int playerIndex)
	{
		// Only highlight in competitive mode
		if (_carromGameMode != CarromGameMode.Competitive || _competitiveModeManager == null)
		{
			return;
		}

		// Map player index to piece type (player1=White, player2=Black)
		PieceType playerPieceType = playerIndex == 0 ? PieceType.White : PieceType.Black;

		// Get all active pieces from the current mode
		var activePieces = _competitiveModeManager.GetActivePieces();
		if (activePieces == null)
		{
			return;
		}

		// Start highlight on the specified player's pieces (exclude striker)
		foreach (var piece in activePieces)
		{
			if (GodotObject.IsInstanceValid(piece) && piece.Type == playerPieceType)
			{
				piece.StartHighlight();
			}
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
		
		// Update score display in competitive mode
		if (_carromGameMode == CarromGameMode.Competitive && _scoreDisplay != null && _competitiveModeManager != null)
		{
			_scoreDisplay.UpdateAllPlayerScores(_competitiveModeManager.GetPlayers());
		}
		
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
	/// Get point value for a piece type (Official ICF Rules)
	/// </summary>
	private float GetPieceValue(PieceType type)
	{
		return type switch
		{
			PieceType.Red => 3.0f,  // Queen worth 3 points per ICF rules
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
		// Debug state logging removed for production
	}
	
	/// <summary>
	/// Centralized striker restoration method - replaces distributed logic across mode managers
	/// Now includes collision detection to prevent overlapping with other pieces
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

		// COLLISION VALIDATION: Check if baseline position is obstructed by other pieces
		float strikerRadius = striker.PhysicsConfig?.GetRadiusForPieceType(striker.Type) ?? 15.0f;
		bool isObstructed = _board?.IsPositionObstructed(globalBaselinePosition, strikerRadius, striker) ?? false;

		if (isObstructed)
		{
			GD.Print($"[CarromGame] Baseline center obstructed by other pieces, searching for alternative position");

			// Try to find valid position along baseline first
			var validPositionOnBaseline = _board?.FindNearestValidPositionOnBaseline(
				globalBaselinePosition, playerIndex, strikerRadius, striker);

			if (validPositionOnBaseline.HasValue)
			{
				globalBaselinePosition = validPositionOnBaseline.Value;
				GD.Print($"[CarromGame] Found alternative position on baseline: {globalBaselinePosition}");
			}
			else
			{
				// Baseline completely blocked - try center area as fallback
				GD.PrintErr($"[CarromGame] Baseline completely obstructed, using center fallback");
				globalBaselinePosition = _board?.FindValidPositionNearCenter(strikerRadius, striker) ?? globalBaselinePosition;
			}
		}

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
			// Marked piece as recently restored in settlement context
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
	
	/// <summary>
	/// Smoothly tween striker to baseline position for cinematic camera transitions
	/// Now includes collision detection to prevent tweening into other pieces
	/// </summary>
	public void TweenStrikerToBaseline(int? playerIndexOverride = null, float duration = 0.6f)
	{
		var striker = _currentModeManager?.GetStriker();
		if (striker == null || !GodotObject.IsInstanceValid(striker))
		{
			GD.PrintErr("[CarromGame] TweenStrikerToBaseline: No valid striker found");
			return;
		}

		// Determine appropriate baseline position based on game mode (same logic as RestoreStrikerToBaseline)
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

		// COLLISION VALIDATION: Check if baseline position is obstructed by other pieces
		float strikerRadius = striker.PhysicsConfig?.GetRadiusForPieceType(striker.Type) ?? 15.0f;
		bool isObstructed = _board?.IsPositionObstructed(globalBaselinePosition, strikerRadius, striker) ?? false;

		if (isObstructed)
		{
			GD.Print($"[CarromGame] Tween target obstructed by other pieces, searching for alternative position");

			// Try to find valid position along baseline first
			var validPositionOnBaseline = _board?.FindNearestValidPositionOnBaseline(
				globalBaselinePosition, playerIndex, strikerRadius, striker);

			if (validPositionOnBaseline.HasValue)
			{
				globalBaselinePosition = validPositionOnBaseline.Value;
				GD.Print($"[CarromGame] Found alternative tween target on baseline: {globalBaselinePosition}");
			}
			else
			{
				// Baseline completely blocked - try center area as fallback
				GD.PrintErr($"[CarromGame] Baseline completely obstructed for tween, using center fallback");
				globalBaselinePosition = _board?.FindValidPositionNearCenter(strikerRadius, striker) ?? globalBaselinePosition;
			}
		}

		try
		{
			// Ensure striker is in proper state for tweening (re-enable if pocketed)
			striker.LinearVelocity = Vector2.Zero;
			striker.AngularVelocity = 0.0f;
			striker.Visible = true;
			striker.Freeze = false;
			striker.ContactMonitor = true; // Re-enable collision detection
			
			// Restore visual properties (pocket may have made it invisible/scaled to zero)
			striker.Scale = Vector2.One;
			striker.Modulate = new Color(1.0f, 1.0f, 1.0f, 1.0f); // Fully opaque
			
			// Mark this restoration in the mode manager's settlement context
			MarkRecentRestoration(striker);

			// Stop any existing striker tween with proper cleanup
			if (_strikerTween != null && _strikerTween.IsValid())
			{
				_strikerTween.Kill();
			}
			_strikerTween = null;
			
			// Create and configure tween
			_strikerTween = CreateTween();
			if (_strikerTween == null)
			{
				GD.PrintErr("[CarromGame] Failed to create striker tween - falling back to immediate restoration");
				RestoreStrikerToBaseline(playerIndexOverride);
				return;
			}

			// Configure tween properties for smooth movement
			_strikerTween.SetEase(Tween.EaseType.Out);
			_strikerTween.SetTrans(Tween.TransitionType.Cubic);

			// Clear turn transition flag and start piece highlights
			// This happens when striker begins tweening to baseline (perfect timing for highlights)
			_waitingForTurnTransition = false;
			StartPieceHighlightsForPlayer(playerIndex);

			// Show turn notification at same time as highlights for visual consistency
			if (_notificationSystem != null && _competitiveModeManager != null && _carromGameMode == CarromGameMode.Competitive)
			{
				var currentPlayer = _competitiveModeManager.GetCurrentPlayer();
				if (currentPlayer != null)
				{
					string pieceIcon = currentPlayer.AssignedPieceType == PieceType.White ? "⚪" : "⚫";
					// Get turn number from current player index + 1 for display
					int turnNumber = _competitiveModeManager.GetPlayers().Count > 0 ?
						(_competitiveModeManager.GetPlayers().IndexOf(currentPlayer) + 1) : 1;
					string message = $"🎯 {currentPlayer.PlayerId.ToUpper()}'S TURN {pieceIcon}";
					_notificationSystem.ShowNotification(NotificationType.TurnStart, message);
				}
			}

			// Animate striker position
			_strikerTween.TweenProperty(striker, TweenConstants.GlobalPosition, globalBaselinePosition, duration);
			
			// Validate final position after tween completes
			_strikerTween.TweenCallback(Callable.From(() => {
				// Validate final position (don't interfere with physics - let tween handle positioning)
				bool tweenSucceeded = ValidateStrikerRestoration(striker, globalBaselinePosition);
				if (tweenSucceeded)
				{
					// Striker tween to baseline completed
				}
				else
				{
					GD.PrintErr("[CarromGame] Striker tween validation failed after completion");
				}
				
				// Complete the turn flow by transitioning game state to Ready (re-enables input)
				ForceGameStateToReady();
			}));
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[CarromGame] TweenStrikerToBaseline: Exception during tween setup: {ex.Message}");
			// Fallback to immediate restoration
			RestoreStrikerToBaseline(playerIndexOverride);
		}
	}

	/// <summary>
	/// Force the game state machine to Ready state (called by mode manager for continued turns)
	/// </summary>
	public void ForceGameStateToReady()
	{
		_gameStateMachine?.ForceToReady();
	}

	/// <summary>
	/// Tween penalty pieces to board sequentially before turn transition
	/// </summary>
	public void TweenPenaltyPiecesToBoard()
	{
		if (_carromGameMode != CarromGameMode.Competitive || _competitiveModeManager == null)
		{
			// No penalty pieces in practice mode, proceed directly to camera transition
			EmitSignal(SignalName.PenaltyPiecesTweenCompleted);
			return;
		}

		var penaltyPieces = ((CarromCompetitiveModeManager)_competitiveModeManager).GetPiecesNeedingTweenReturn();
		if (penaltyPieces.Count == 0)
		{
			// No penalty pieces to animate, proceed directly to camera transition
			EmitSignal(SignalName.PenaltyPiecesTweenCompleted);
			return;
		}

		// Starting penalty pieces tween animation

		// Stop any existing penalty tween with proper cleanup
		if (_penaltyPiecesTween != null && _penaltyPiecesTween.IsValid())
		{
			_penaltyPiecesTween.Kill();
		}
		_penaltyPiecesTween = null;

		// Create and configure tween for sequential animation
		_penaltyPiecesTween = CreateTween();
		if (_penaltyPiecesTween == null)
		{
			GD.PrintErr("[PENALTY TWEEN] Failed to create penalty pieces tween - falling back to immediate completion");
			EmitSignal(SignalName.PenaltyPiecesTweenCompleted);
			return;
		}

		// Configure tween properties for smooth movement
		_penaltyPiecesTween.SetEase(Tween.EaseType.Out);
		_penaltyPiecesTween.SetTrans(Tween.TransitionType.Cubic);

		// Build linear tween chain - no recursion, no callback violations
		BuildLinearTweenChain(penaltyPieces);
	}

	/// <summary>
	/// Build a linear tween chain for all penalty pieces - avoids recursive callback issues
	/// </summary>
	private void BuildLinearTweenChain(List<CarromPiece> penaltyPieces)
	{
		const float pieceAnimationDuration = 0.4f;
		const float delayBetweenPieces = 0.1f;
		
		// Build the complete animation chain upfront
		for (int i = 0; i < penaltyPieces.Count; i++)
		{
			var piece = penaltyPieces[i];
			if (!GodotObject.IsInstanceValid(piece))
			{
				continue; // Skip invalid pieces
			}

			// Get target position from metadata
			var targetPosition = piece.GetMeta("tween_target_position", Vector2.Zero).AsVector2();
			
			// Queuing penalty piece animation

			// First, reveal the piece (restore visual properties from pocket state)
			_penaltyPiecesTween.TweenCallback(Callable.From(() => RevealPieceForAnimation(piece)));
			
			// Then animate the piece to its target position
			_penaltyPiecesTween.TweenProperty(piece, TweenConstants.GlobalPosition, targetPosition, pieceAnimationDuration);
			
			// Add delay between pieces (except after the last piece)
			if (i < penaltyPieces.Count - 1)
			{
				_penaltyPiecesTween.TweenInterval(delayBetweenPieces);
			}
		}

		// Single completion callback at the end of the entire chain
		_penaltyPiecesTween.TweenCallback(Callable.From(OnAllPenaltyPiecesTweenCompleted));
	}

	/// <summary>
	/// Reveal a penalty piece for animation by restoring its visual properties
	/// </summary>
	private void RevealPieceForAnimation(CarromPiece piece)
	{
		if (!GodotObject.IsInstanceValid(piece)) return;
		
		// Restore piece visibility and visual properties from pocket state
		piece.Visible = true;
		piece.Scale = Vector2.One;
		piece.Modulate = new Color(1.0f, 1.0f, 1.0f, 1.0f);
		
		// Revealed penalty piece for animation
	}

	/// <summary>
	/// Called when all penalty pieces have finished tweening
	/// </summary>
	private void OnAllPenaltyPiecesTweenCompleted()
	{
		// All penalty pieces tween animation completed
		
		// Clear the competitive mode manager's tween list
		if (_competitiveModeManager is CarromCompetitiveModeManager competitiveModeManager)
		{
			competitiveModeManager.ClearTweenReturnList();
		}
		
		// Reset animation state
		_penaltyPieceIndex = 0;
		
		// Clean up tween
		if (_penaltyPiecesTween != null && _penaltyPiecesTween.IsValid())
		{
			_penaltyPiecesTween.Kill();
		}
		_penaltyPiecesTween = null;
		
		// Signal completion to trigger camera transition
		EmitSignal(SignalName.PenaltyPiecesTweenCompleted);
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
				player.SetUserSession(playerSession.UserSession);
				AddPlayer(player);
			}
		}
		// Development context: No auto-player creation
		// Require actual login for data persistence and game functionality
	}

	// ================================================================
	// SAVE/LOAD SYSTEM
	// ================================================================

	/// <summary>
	/// Load user data - event-sourced persistence
	/// Backend rebuilds state from events
	/// </summary>
	private async void LoadUserDataAsync()
	{
		// Event-sourced persistence - no DataStore loading needed
		// Backend will rebuild state from events when queried
		await Task.CompletedTask;
	}

	/// <summary>
	/// Save game result via event-sourced persistence
	/// </summary>
	private async void SaveGameResultAsync(string playerId, bool isWin)
	{
		string phoneNumber = GetCurrentUserPhoneNumber();
		if (string.IsNullOrEmpty(phoneNumber)) return;

		try
		{
			// Emit event to backend (event-sourced persistence)
			if (_carromEventService != null)
			{
				// Build scores dictionary with player scores
				// For single-player competitive mode, we only have one player's score
				var scores = new Dictionary<string, int>();
				if (_competitiveModeManager != null)
				{
					// Get player score from competitive mode manager
					// Assuming a method exists to get current score
					scores[playerId] = 0; // TODO: Get actual score from competitive mode
				}

				var mode = _carromGameMode.ToString().ToLowerInvariant();
				var winnerId = isWin ? playerId : "";

				_ = _carromEventService.EmitRoundFinishAsync(mode, winnerId, scores);

				// Thread-safe logging via CallDeferred
				CallDeferred(MethodName.LogSavedData, $"Emitted round_finish event - Win: {isWin}");
			}
		}
		catch (System.Exception ex)
		{
			// Thread-safe error logging via CallDeferred
			CallDeferred(MethodName.LogAsyncError, $"Exception emitting game result event: {ex.Message}");
		}

		await Task.CompletedTask;
	}

	/// <summary>
	/// Get current win streak for a player from competitive mode manager
	/// </summary>
	private int GetCurrentWinStreak(string playerId)
	{
		if (_competitiveModeManager == null) return 0;
		
		var player = _competitiveModeManager.GetPlayers()?.FirstOrDefault(p => p.PlayerId == playerId);
		return player != null ? 1 : 0; // Simple implementation - just return 1 for existing players
	}
	
	/// <summary>
	/// Thread-safe logging method for loaded data
	/// </summary>
	private void LogLoadedData(string message)
	{
		GD.Print($"[CarromGame] {message}");
	}
	
	/// <summary>
	/// Thread-safe logging method for saved data
	/// </summary>
	private void LogSavedData(string message)
	{
		GD.Print($"[CarromGame] {message}");
	}
	
	/// <summary>
	/// Thread-safe logging method for async errors
	/// </summary>
	private void LogAsyncError(string message)
	{
		GD.PrintErr($"[CarromGame] {message}");
	}

	/// <summary>
	/// Get the current user's phone number from SessionManager
	/// Returns null if no user is logged in
	/// </summary>
	private string GetCurrentUserPhoneNumber()
	{
		try
		{
			var sessionManager = SessionManager.GetInstance();
			if (sessionManager != null && GodotObject.IsInstanceValid(sessionManager))
			{
				var currentSession = sessionManager.GetPrimaryUserSession();
				if (currentSession != null)
				{
					return currentSession.PhoneNumber;
				}
			}
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[CarromGame] Error getting current user phone number: {ex.Message}");
		}
		// Return null if no user is logged in - no fallbacks
		return null;
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

		// Mode-specific buttons
		if (_carromGameMode == CarromGameMode.Practice)
		{
			buttons.Add(new ContextButtonData("Reset", () => {
				var userManager = UserManager.GetAutoload();
				userManager?.ResetUserIdleTimer();
				RequestPracticeReset();
			}, "🔄", true, "Reset practice session"));
			
			buttons.Add(new ContextButtonData("2-Player Match", () => {
				var userManager = UserManager.GetAutoload();
				userManager?.ResetUserIdleTimer();
				StartCompetitiveMode(2);
			}, "👥", true, "Start 2-player competitive match"));
			
			buttons.Add(new ContextButtonData("4-Player Match", () => {
				var userManager = UserManager.GetAutoload();
				userManager?.ResetUserIdleTimer();
				StartCompetitiveMode(4);
			}, "👨‍👩‍👧‍👦", true, "Start 4-player doubles match"));
		}
		else if (_carromGameMode == CarromGameMode.Competitive)
		{
			// Show scores
			buttons.Add(new ContextButtonData("Scores", () => {
				ShowScoreboard();
			}, "📊", true, "View current scores"));
			
			// Return to practice mode
			buttons.Add(new ContextButtonData("Return to Practice", () => {
				var userManager = UserManager.GetAutoload();
				userManager?.ResetUserIdleTimer();
				StartPracticeMode();
			}, "🔄", true, "Return to practice mode"));
		}

		return buttons.ToArray();
	}

	/// <summary>
	/// Show scoreboard with current game statistics
	/// </summary>
	private void ShowScoreboard()
	{
		if (_competitiveModeManager == null)
		{
			return;
		}

		var players = _competitiveModeManager.GetPlayers();
		var scoreText = new System.Text.StringBuilder("=== CARROM SCOREBOARD ===\n\n");

		foreach (var player in players)
		{
			scoreText.AppendLine($"{player.PlayerId} ({player.AssignedPieceType})");
			scoreText.AppendLine($"Pieces: {player.PiecesPocketed}/9");
			scoreText.AppendLine($"Queen: {(player.HasQueen ? (player.QueenCovered ? "Covered" : "Uncovered") : "No")}");
			scoreText.AppendLine($"Accuracy: {player.GetAccuracy():P1}");
			scoreText.AppendLine($"Fouls: {player.GetFouls()}");
			scoreText.AppendLine(); // Empty line between players
		}

		// Show scoreboard in a proper dialog instead of debug print
		ShowScoreboardDialog(scoreText.ToString());
	}

	/// <summary>
	/// Display scoreboard in a dialog window
	/// </summary>
	private void ShowScoreboardDialog(string scoreText)
	{
		var dialog = new AcceptDialog();
		dialog.Title = "📊 Current Scores";
		dialog.DialogText = scoreText;
		dialog.Size = new Vector2I(400, 350);
		
		// Center the dialog
		var viewport = GetViewport();
		if (viewport != null)
		{
			var screenSize = viewport.GetVisibleRect().Size;
			dialog.Position = new Vector2I(
				(int)(screenSize.X / 2 - 200), 
				(int)(screenSize.Y / 2 - 175)
			);
		}

		// Add dialog to scene tree and show
		GetTree().CurrentScene.AddChild(dialog);
		dialog.PopupCentered();
		
		// Auto-remove dialog when closed
		dialog.Confirmed += () => dialog.QueueFree();
		dialog.Canceled += () => dialog.QueueFree();
	}

	/// <summary>
	/// Show turn transition with player information and current scores
	/// Camera rotation happens when pass turn is actually executed, not immediately
	/// </summary>
	private void ShowTurnTransition(string playerId, int turnNumber)
	{
		if (_carromGameMode != CarromGameMode.Competitive || _competitiveModeManager == null)
			return;

		var currentPlayer = _competitiveModeManager.GetCurrentPlayer();
		if (currentPlayer == null) 
			return;

		// Build compact transition message for the score bar
		string pieceIcon = currentPlayer.AssignedPieceType == PieceType.White ? "⚪" : "⚫";
		string transitionMessage = $"🎯 {currentPlayer.PlayerId.ToUpper()}'S TURN {pieceIcon} - Turn {turnNumber}";
		
		// Show transition in the score display bar - this shows the pass turn button
		if (_scoreDisplay != null)
		{
			_scoreDisplay.ShowTurnTransition(transitionMessage, 3.0f);
			
			// Update all player scores during transition
			var players = _competitiveModeManager.GetPlayers();
			_scoreDisplay.UpdateAllPlayerScores(players);
		}
		
		// Camera rotation now happens in OnPassTurnRequested when turn is actually advanced
	}
	
	/// <summary>
	/// Handle manual pass turn request from score display
	/// Now includes proper camera rotation timing after turn switch
	/// </summary>
	private void OnPassTurnRequested()
	{
		// Only process pass turn requests in competitive mode when all pieces have settled
		if (_carromGameMode != CarromGameMode.Competitive || _competitiveModeManager == null)
			return;
		
		// Check if we're in a state where turn can be passed (all pieces stopped)
		bool allPiecesStopped = AreAllPiecesStopped();
		if (!allPiecesStopped)
		{
			return;
		}
		
		// Transition state machine to Ready before switching players
		_gameStateMachine?.ForceToReady();
		
		_competitiveModeManager?.SwitchToNextPlayer();
		
		// Start penalty pieces tween animation sequence (if any penalty pieces need to be returned)
		// This will either animate penalty pieces sequentially or proceed directly to camera transition
		TweenPenaltyPiecesToBoard();
		
		// Note: Camera transition now happens via PenaltyPiecesTweenCompleted signal
		// Striker restoration happens after camera transition via CameraTransitionCompleted signal
	}


	/// <summary>
	/// Show comprehensive game over screen with final statistics
	/// </summary>
	private void ShowGameOverScreen(string winnerPlayerId)
	{
		if (_competitiveModeManager == null) return;

		var players = _competitiveModeManager.GetPlayers();
		var winner = players.FirstOrDefault(p => p.PlayerId == winnerPlayerId);
		if (winner == null) return;

		int playerCount = players.Count;
		string modeText = playerCount == 4 ? "Doubles" : "Singles";
		
		var gameOverMessage = new System.Text.StringBuilder($"🏆 GAME OVER - {modeText.ToUpper()} 🏆\n\n");
		gameOverMessage.AppendLine($"WINNER: {winner.PlayerId.ToUpper()}");
		gameOverMessage.AppendLine($"Team: {winner.AssignedPieceType} Pieces");
		gameOverMessage.AppendLine();

		// Winner's performance
		gameOverMessage.AppendLine("=== WINNER STATS ===");
		gameOverMessage.AppendLine($"Pieces Pocketed: {winner.PiecesPocketed}/9");
		gameOverMessage.AppendLine($"Queen Status: {(winner.HasQueen ? (winner.QueenCovered ? "Covered ✓" : "Not Covered ✗") : "Not Pocketed")}");
		gameOverMessage.AppendLine($"Accuracy: {winner.GetAccuracy():P1}");
		gameOverMessage.AppendLine($"Fouls: {winner.GetFouls()}");
		gameOverMessage.AppendLine();

		// Final standings for all players
		gameOverMessage.AppendLine("=== FINAL STANDINGS ===");
		var sortedPlayers = players.OrderByDescending(p => p.PiecesPocketed).ToList();
		for (int i = 0; i < sortedPlayers.Count; i++)
		{
			var player = sortedPlayers[i];
			string position = (i + 1) switch
			{
				1 => "1st",
				2 => "2nd", 
				3 => "3rd",
				4 => "4th",
				_ => $"{i + 1}th"
			};
			
			gameOverMessage.Append($"{position}: {player.PlayerId} - {player.PiecesPocketed}/9 pieces");
			if (player.HasQueen)
			{
				gameOverMessage.Append(player.QueenCovered ? " + Queen ✓" : " + Queen ✗");
			}
			gameOverMessage.AppendLine($" (Accuracy: {player.GetAccuracy():P1})");
		}

		// Show the game over dialog
		ShowGameOverDialog(gameOverMessage.ToString());
	}

	/// <summary>
	/// Display game over dialog with final statistics
	/// </summary>
	private void ShowGameOverDialog(string message)
	{
		var dialog = new AcceptDialog();
		dialog.Title = "🏆 COMPETITIVE CARROM - GAME COMPLETE 🏆";
		dialog.DialogText = message;
		dialog.Size = new Vector2I(500, 400);
		
		// Center the dialog
		var viewport = GetViewport();
		if (viewport != null)
		{
			var screenSize = viewport.GetVisibleRect().Size;
			dialog.Position = new Vector2I(
				(int)(screenSize.X / 2 - 250), 
				(int)(screenSize.Y / 2 - 200)
			);
		}

		// Add dialog to scene tree and show
		GetTree().CurrentScene.AddChild(dialog);
		dialog.PopupCentered();
		
		// Auto-remove dialog when closed
		dialog.Confirmed += () => dialog.QueueFree();
		dialog.Canceled += () => dialog.QueueFree();
		
		// Also add a "Play Again" button if possible
		dialog.AddButton("Return to Practice", false, "practice");
		dialog.CustomAction += (action) => {
			if (action.ToString() == "practice")
			{
				dialog.QueueFree();
				StartPracticeMode();
			}
		};
	}
	
	

	/// <summary>
	/// Override to provide carrom game title (simplified without player info)
	/// </summary>
	public override string GetGameTitle()
	{
		if (_carromGameMode == CarromGameMode.Practice)
		{
			return "Carrom - Practice";
		}
		else if (_carromGameMode == CarromGameMode.Competitive && _competitiveModeManager != null)
		{
			var playerCount = _competitiveModeManager.GetPlayers()?.Count ?? 0;
			string modeText = playerCount == 4 ? "Doubles" : "Singles";
			return $"Carrom - {modeText}";
		}

		return "Carrom";
	}

	// ================================================================
	// SIGNAL HANDLERS FOR MANAGERS
	// ================================================================

	
	/// <summary>
	/// Handle game state changes from state machine
	/// Game state machine is the single source of truth for input state
	/// </summary>
	private void OnGameStateChanged(CarromGameStateMachine.GameState oldState, CarromGameStateMachine.GameState newState)
	{
		// Game state changed
		
		// Update score display with current state
		if (_scoreDisplay != null)
		{
			_scoreDisplay.UpdateGameState(newState.ToString());
			
			// Game state machine is the single source of truth for input blocking
			bool inputBlocked = newState != CarromGameStateMachine.GameState.Ready;
			_scoreDisplay.SetInputBlockedVisual(inputBlocked);
		}
		
		// Update title to reflect current state for debugging
		RefreshUI();
	}
	
	/// <summary>
	/// Handle settlement completion from state machine
	/// State machine automatically transitions to Ready, no manual control needed
	/// </summary>
	private void OnSettlementCompleted()
	{
		// Settlement completed - state machine has already transitioned to Ready automatically
		// Mode managers have processed their logic, UI can react to state changes
		// No manual state control needed here
	}
	
	/// <summary>
	/// Handle camera transition completion - smoothly tween striker to new player's baseline
	/// </summary>
	private void OnCameraTransitionCompleted()
	{
		// Smoothly animate striker to new player's baseline position after cinematic camera transition
		TweenStrikerToBaseline(duration: 0.6f);
	}

	/// <summary>
	/// Handle penalty pieces tween completion - proceed to camera transition
	/// </summary>
	private void OnPenaltyPiecesTweenCompleted()
	{
		// Penalty pieces tween completed, proceeding to camera transition
		
		// Start cinematic camera transition to the new player's perspective
		var newCurrentPlayer = _competitiveModeManager?.GetCurrentPlayer();
		if (newCurrentPlayer != null && _cameraController != null)
		{
			// Map playerId to player index for camera transition
			int playerIndex = newCurrentPlayer.PlayerId switch
			{
				"player1" => 0, // Bottom
				"player2" => 1, // Top  
				"player3" => 2, // Left
				"player4" => 3, // Right
				_ => 0
			};
			
			// Start cinematic zoom transition (striker restoration happens when transition completes)
			_cameraController.TransitionToPlayerWithZoom(playerIndex, 1.2f);
		}
	}

	/// <summary>
	/// Handle input availability changes from state machine
	/// </summary>
	private void OnInputAvailabilityChanged(bool canAcceptInput)
	{
		// Input availability changed
		
		// Update score display visual state
		if (_scoreDisplay != null && GodotObject.IsInstanceValid(_scoreDisplay))
		{
			_scoreDisplay.SetInputBlockedVisual(!canAcceptInput);
		}
		
		// Input controller automatically checks _gameState.CanAcceptInput, so no direct update needed
	}

	/// <summary>
	/// Handle ready for input state - start highlights for turn continuation (not turn change)
	/// </summary>
	private void OnReadyForInput()
	{
		// Only highlight for turn continuation, not turn change
		// Turn change highlights happen in TweenStrikerToBaseline when camera/striker animation starts
		if (_waitingForTurnTransition)
		{
			// Turn is changing, wait for tween to handle highlights
			return;
		}

		// Only highlight pieces in competitive mode
		if (_carromGameMode != CarromGameMode.Competitive || _competitiveModeManager == null)
		{
			return;
		}

		// Get current player's piece type
		var currentPlayer = _competitiveModeManager.GetCurrentPlayer();
		if (currentPlayer == null)
		{
			return;
		}

		// Map current player to player index for consistency
		int playerIndex = currentPlayer.PlayerId == "player1" ? 0 : 1;

		// Start highlights for turn continuation
		StartPieceHighlightsForPlayer(playerIndex);
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
			// Practice mode setup complete - validation passed
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
		// Starting input controller validation
		
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
			// Input currently disabled - this may be normal during setup
		}
		
		// Input controller validation: All validations passed
		return true;
	}
	
	/// <summary>
	/// Attempt to recover input controller synchronization if validation fails
	/// </summary>
	private void RecoverInputControllerSynchronization()
	{
		// Starting input controller recovery
		
		// Re-establish game state reference
		if (_inputController != null && _gameStateMachine != null)
		{
			_inputController.SetGameState(_gameStateMachine);
			// Recovery: Re-established game state reference
		}
		
		// Update striker reference
		UpdateInputControllerStriker();
		// Recovery: Updated striker reference
		
		// Re-validate after recovery
		bool recoverySuccessful = ValidateInputControllerSynchronization();
		
		if (recoverySuccessful)
		{
			// Recovery successful - starting game
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
		// DEBUG: Force enabling input for debugging
		
		// State machine handles transitions automatically
		
		// Force input controller to enabled state
		if (_inputController != null)
		{
			// This assumes SetInputState method exists - may need to adjust based on actual InputController API
			// DEBUG: Attempting to force input controller to enabled state
			
			// Re-establish all references
			_inputController.SetGameState(_gameStateMachine);
			UpdateInputControllerStriker();
			
			// DEBUG: Re-established input controller references
		}
		
		// Log current state for debugging
		LogGameState("DEBUG Force Enable Input");
	}

	/// <summary>
	/// DEBUG METHOD: Test camera rotation manually
	/// </summary>
	public void DEBUG_TestCameraRotation(int playerIndex = 1, float duration = 1.0f)
	{
		_cameraController?.RotateToPlayer(playerIndex, duration);
	}

	/// <summary>
	/// Handle turn change in competitive mode - called AFTER turn has been advanced
	/// </summary>
	private void OnTurnChanged(string playerId, int turnNumber)
	{
		EmitSignal(SignalName.TurnChanged, playerId, turnNumber);

		// Clear all piece trails when turn changes for clean visual reset
		ClearAllTrails();

		// Update score display to reflect new current player
		if (_scoreDisplay != null && _competitiveModeManager != null)
		{
			_scoreDisplay.SetCurrentPlayer(playerId);
			_scoreDisplay.UpdateAllPlayerScores(_competitiveModeManager.GetPlayers());
		}

		// No need to show turn transition here - that's handled by OnTurnReadyForPass
		// This method is called AFTER the turn has been advanced via pass turn button

		// Refresh UI to show updated player turn and title
		RefreshUI();
	}
	
	/// <summary>
	/// Handle turn ready for pass - shows pass turn button without changing players
	/// </summary>
	private void OnTurnReadyForPass(string playerId, int turnNumber)
	{
		// Show only the pass turn button, no camera rotation or player switching
		if (_carromGameMode != CarromGameMode.Competitive || _competitiveModeManager == null)
			return;

		var currentPlayer = _competitiveModeManager.GetCurrentPlayer();
		if (currentPlayer == null)
			return;

		// Set flag to indicate we're waiting for turn transition animation
		// This prevents highlights from appearing prematurely in OnReadyForInput
		_waitingForTurnTransition = true;

		// Build message for the pass turn button display
		string pieceIcon = currentPlayer.AssignedPieceType == PieceType.White ? "⚪" : "⚫";
		string transitionMessage = $"🎯 {currentPlayer.PlayerId.ToUpper()}'S TURN {pieceIcon} - Turn {turnNumber}";

		// Show ONLY the pass turn button, no other UI updates
		if (_scoreDisplay != null)
		{
			_scoreDisplay.ShowTurnTransition(transitionMessage, 3.0f);
		}
	}

	/// <summary>
	/// Handle player winning in competitive mode
	/// </summary>
	private void OnPlayerWon(string playerId)
	{
		// Save win result to DataStore
		SaveGameResultAsync(playerId, true);

		// Handle win condition
		EndGame();

		// Hide notification system on game end
		_notificationSystem?.Hide();

		// Show comprehensive game over screen
		ShowGameOverScreen(playerId);

		RefreshUI();
	}

	/// <summary>
	/// Handle foul committed in competitive mode
	/// </summary>
	private void OnFoulCommitted(string playerId)
	{
		EmitSignal(SignalName.StrikerFoul, playerId);

		// Show foul notification (timed)
		if (_notificationSystem != null)
		{
			_notificationSystem.ShowNotification(NotificationType.Foul, $"⚠️ Foul by {playerId.ToUpper()}!");
		}

		// Also show floating text above player's score for emphasis
		if (_scoreDisplay != null)
		{
			_scoreDisplay.ShowFloatingText(playerId, "Foul! ⚠️", Colors.Red);
		}
	}

	/// <summary>
	/// Handle notification request from competitive mode manager
	/// </summary>
	private void OnNotificationRequested(int notificationType, string message)
	{
		if (_notificationSystem != null)
		{
			_notificationSystem.ShowNotification((NotificationType)notificationType, message);
		}
	}

	/// <summary>
	/// Handle competitive mode setup completion
	/// </summary>
	private void OnCompetitiveModeSetupComplete()
	{
		// Show score display and setup players
		if (_scoreDisplay != null && _competitiveModeManager != null)
		{
			_scoreDisplay.SetVisible(true);
			_scoreDisplay.ClearPlayers();

			// Provide score display to competitive mode manager for direct floating text calls
			_competitiveModeManager.SetScoreDisplay(_scoreDisplay);

			// Add all players to score display
			var players = _competitiveModeManager.GetPlayers();
			foreach (var player in players)
			{
				_scoreDisplay.AddPlayer(player.PlayerId, player.AssignedPieceType);
			}

			// Set current player
			var currentPlayer = _competitiveModeManager.GetCurrentPlayer();
			if (currentPlayer != null)
			{
				_scoreDisplay.SetCurrentPlayer(currentPlayer.PlayerId);
			}
		}

		// Show notification system with initial turn notification
		if (_notificationSystem != null && _competitiveModeManager != null)
		{
			var currentPlayer = _competitiveModeManager.GetCurrentPlayer();
			if (currentPlayer != null)
			{
				string pieceIcon = currentPlayer.AssignedPieceType == PieceType.White ? "⚪" : "⚫";
				string message = $"🎯 {currentPlayer.PlayerId.ToUpper()}'S TURN {pieceIcon} - Turn 1";
				_notificationSystem.Show();
				_notificationSystem.ShowNotification(NotificationType.TurnStart, message);
			}
		}

		// State machine handles game start automatically
	}
	

	/// <summary>
	/// Handle piece creation from factory (mode managers handle tracking)
	/// </summary>
	private void OnPieceCreated(CarromPiece piece)
	{
		// Connect piece signals for main game events
		piece.PieceCollided += OnPieceCollided;
		
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
			_competitiveModeManager.NotificationRequested -= OnNotificationRequested;
		}
		
		// Piece factory signals
		if (_pieceFactory != null && GodotObject.IsInstanceValid(_pieceFactory))
		{
			_pieceFactory.PieceCreated -= OnPieceCreated;
			_pieceFactory.PieceDestroyed -= OnPieceDestroyed;
		}
	}

	/// <summary>
	/// Handle player setup menu game start request
	/// </summary>
	private void OnPlayerSetupMenuGameStartRequested()
	{
		// Use the stored player count from when the menu was shown
		StartCompetitiveModeInternal(_pendingPlayerCount);
	}

	/// <summary>
	/// Handle player setup menu cancellation
	/// </summary>
	private void OnPlayerSetupMenuCancelled()
	{
		// Return to practice mode
		StartPracticeMode();
	}

	public override void _ExitTree()
	{
		base._ExitTree();

		// Clean up signals and references
		if (_board != null && GodotObject.IsInstanceValid(_board))
		{
			var pockets = _board.GetPockets();
			foreach (var pocket in pockets)
			{
				if (pocket != null && GodotObject.IsInstanceValid(pocket))
				{
					pocket.PiecePocketed -= OnPiecePocketed;
				}
			}
		}

		if (_inputController != null && GodotObject.IsInstanceValid(_inputController))
		{
			_inputController.StrikeExecuted -= OnStrikeExecuted;
			_inputController.AimingStateChanged -= OnAimingStateChanged;
		}

		if (_cameraController != null && GodotObject.IsInstanceValid(_cameraController))
		{
			_cameraController.CameraTransitionCompleted -= OnCameraTransitionCompleted;
		}

		// Clean up penalty tween signal
		PenaltyPiecesTweenCompleted -= OnPenaltyPiecesTweenCompleted;

		// Clean up game state machine signals
		if (_gameStateMachine != null && GodotObject.IsInstanceValid(_gameStateMachine))
		{
			_gameStateMachine.InputAvailabilityChanged -= OnInputAvailabilityChanged;
			_gameStateMachine.ReadyForInput -= OnReadyForInput;
		}

		// Clean up score display signals
		if (_scoreDisplay != null && GodotObject.IsInstanceValid(_scoreDisplay))
		{
			_scoreDisplay.PassTurnRequested -= OnPassTurnRequested;
		}

		// Clean up notification system
		if (_notificationSystem != null && GodotObject.IsInstanceValid(_notificationSystem))
		{
			_notificationSystem.QueueFree();
		}

		// Clean up player setup menu signals
		if (_playerSetupMenu != null && GodotObject.IsInstanceValid(_playerSetupMenu))
		{
			_playerSetupMenu.GameStartRequested -= OnPlayerSetupMenuGameStartRequested;
			_playerSetupMenu.MenuCancelled -= OnPlayerSetupMenuCancelled;
		}

		// Clean up animation components
		_strikerTween?.Kill();
		_penaltyPiecesTween?.Kill();

		// Clean up manager signals
		DisconnectManagerSignals();
	}
	
}