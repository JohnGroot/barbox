using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace BarBox.Games.Carrom;

/// <summary>
/// Traditional board game featuring physics-based striking and strategic pocket play
/// </summary>
[GlobalClass]
public partial class CarromGame : GameController
{
	protected override string GetGameId() => "carrom";

	#region Signals

	// Domain-specific lifecycle signals
	[Signal]
	public delegate void RoundStartedEventHandler();

	[Signal]
	public delegate void RoundEndedEventHandler();

	// Game event signals
	[Signal]
	public delegate void PiecePocketedEventHandler(string playerId, CarromPiece piece);

	[Signal]
	public delegate void StrikerFoulEventHandler(string playerId);

	[Signal]
	public delegate void TurnChangedEventHandler(string playerId, int turnNumber);

	[Signal]
	public delegate void PenaltyPiecesTweenCompletedEventHandler();

	#endregion

	#region Export Properties

	[ExportCategory("Carrom Settings")]
	[Export]
	public bool ShowPracticeMode { get; set; } = true;

	[Export]
	public int CompetitiveCreditCost { get; set; } = 1;

	[Export]
	public float TurnTimeLimit { get; set; } = 30.0f;

	[Export]
	public CarromPhysicsConfig PhysicsConfig { get; set; }

	[ExportCategory("Physics Limits")]
	[Export]
	public float MaxVelocityLimit { get; set; } = 2000.0f;

	[Export]
	public float MaxAngularVelocity { get; set; } = 50.0f;

	[Export]
	public float VelocityAlertThreshold { get; set; } = 1800.0f;

	[Export]
	public float MinVelocityThreshold { get; set; } = 1.0f;

	[Export]
	public float AngularMinThreshold { get; set; } = 0.1f;

	[ExportCategory("Visual Feedback")]
	[Export]
	public float AimLineLength { get; set; } = 100.0f;

	[Export]
	public float PowerBarWidth { get; set; } = 60.0f;

	[Export]
	public float PowerBarHeight { get; set; } = 8.0f;

	[ExportCategory("Debug Options")]
	[Export]
	public bool EnableTrails { get; set; } = false;

	/// <summary>
	/// Disallow logout during active rounds, allow when in menu/idle states
	/// </summary>
	public override bool CanLogout => !IsRoundActive();

	[ExportCategory("Piece Templates")]
	[Export(PropertyHint.ResourceType, "PackedScene")]
	public PackedScene WhitePieceTemplate { get; set; }

	[Export(PropertyHint.ResourceType, nameof(PackedScene))]
	public PackedScene BlackPieceTemplate { get; set; }

	[Export(PropertyHint.ResourceType, nameof(PackedScene))]
	public PackedScene RedPieceTemplate { get; set; }

	[Export(PropertyHint.ResourceType, nameof(PackedScene))]
	public PackedScene StrikerTemplate { get; set; }

	#endregion

	#region Private Fields

	private CarromGameMode _carromGameMode = CarromGameMode.Practice;
	private CarromBoard _board;
	private CarromInputController _inputController;
	private SessionEventService _eventService;
	private CarromEventService _carromEventService;

	// Managers
	private CarromPieceFactory _pieceFactory;
	private CarromCameraController _cameraController;
	private GameStateManager _gameStateManager;

	private CarromPracticeModeManager _practiceModeManager;
	private CarromCompetitiveModeManager _competitiveModeManager;
	private CarromModeManagerBase _currentModeManager;
	private List<CarromPlayer> _players = new();

	// Optional GameController components (minimal usage - Carrom has own player system)
	private PlayerManagementComponent _playerMgmt;

	// UI Components
	private CarromScoreDisplay _scoreDisplay;
	private CarromPlayerSetupMenu _playerSetupMenu;
	private int _pendingPlayerCount = 2; // Track player count for menu → game transition

	// Animation components
	private Tween _strikerTween;
	private Tween _penaltyPiecesTween;
	private int _penaltyPieceIndex = 0;

	// Turn transition tracking for highlight timing
	private bool _waitingForTurnTransition = false;

	#endregion

	#region Initialization

	/// <summary>
	/// Handle debug input commands
	/// </summary>
	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			// F11: Force show pass turn button for debugging
			if (keyEvent.Keycode == Key.F11)
			{
				GD.Print("[DEBUG] F11 pressed - Force showing pass button");
				if (_scoreDisplay != null && GodotObject.IsInstanceValid(_scoreDisplay))
				{
					_scoreDisplay.ShowTurnTransition("DEBUG: Manual Show (F11)", 10.0f);
					GD.Print("[DEBUG] Pass button force shown via ShowTurnTransition");
				}
				else
				{
					GD.PrintErr("[DEBUG] Cannot show button - _scoreDisplay is null or invalid");
				}
			}
		}
	}

	/// <summary>
	/// Discovers platform services and detects production vs development context
	/// </summary>
	protected override void OnDiscoverServices()
	{
		_eventService = Platform.Events;
		_carromEventService = new CarromEventService(_eventService);
	}

	/// <summary>
	/// Creates and configures all game components (board, camera, input, managers, UI)
	/// </summary>
	protected override void OnInitializeComponents()
	{
		// Initialize physics config early (needed for exports)
		PhysicsConfig ??= new CarromPhysicsConfig();

		// Create minimal GameController components for compatibility
		_playerMgmt = new PlayerManagementComponent();
		AddChild(_playerMgmt);

		SetupBoard();
		SetupCameraController();
		SetupInputController();
		InitializeComponentsInternal();
		CreateManagers();
		InitializeManagers();

		// GameStateManager requires a valid PieceFactory reference, which InitializeManagers() just set up
		InitializeGameStateMachine();

		InitializeScoreDisplay();

		// Player integration requires the components created above
		SetupPlayerIntegration();

		// Load user data after context detection and UI setup
		LoadUserDataAsync();
	}

	/// <summary>
	/// Automatically starts practice mode after all initialization is complete
	/// </summary>
	protected override void OnActivateGame()
	{
		StartPracticeMode();
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
		if (_board == null)
		{
			return;
		}

		// Ensure board stays at origin (0,0)
		_board.GlobalPosition = Vector2.Zero;

		_cameraController = new CarromCameraController();
		AddChild(_cameraController);
		_cameraController.Initialize(_board);

		_cameraController.CameraTransitionCompleted += OnCameraTransitionCompleted;
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
	private void InitializeComponentsInternal()
	{
		// Initialize board first (it may need to setup internal state)
		if (_board != null)
		{
			_board.RefreshBoard();

			// Configure physics config with official board scaling for proportional piece sizes
			if (PhysicsConfig != null)
			{
				PhysicsConfig.SetBoardScaling(
					_board.ScaleFactor,
					_board.PieceRadius,
					_board.OfficialStrikerRadius);

				_board.SetPocketPhysicsConfig(PhysicsConfig);
			}
		}

		// Initialize input controller after board and camera are ready
		if (_inputController != null)
		{
			_inputController.InitializeWithBoard(_board);
			_inputController.SetCameraController(_cameraController);
			_inputController.SetPhysicsConfig(PhysicsConfig);
			_inputController.SetVisualParameters(AimLineLength, PowerBarWidth, PowerBarHeight);
		}

		InitializeTrailSystem();
	}

	/// <summary>
	/// Initialize trail system for debug visualization
	/// </summary>
	private void InitializeTrailSystem()
	{
		CarromPiece.SetTrailsEnabled(EnableTrails);
	}

	/// <summary>
	/// Clear all piece trails
	/// </summary>
	private void ClearAllTrails()
	{
		var allPieces = GetTree().GetNodesInGroup("pieces");
		if (allPieces.Count == 0)
		{
			// Fallback: pieces group may be empty before it's populated, so walk the board's children directly
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
			foreach (Node node in allPieces)
			{
				if (node is CarromPiece piece && GodotObject.IsInstanceValid(piece))
				{
					piece.ClearTrail();
				}
			}
		}
	}

	/// <summary>
	/// Initialize game state manager (must run after PieceFactory is initialized)
	/// </summary>
	private void InitializeGameStateMachine()
	{
		_gameStateManager.Initialize(_board, _inputController, _carromGameMode, _pieceFactory);
		_inputController.SetGameState(_gameStateManager);
		_gameStateManager.PhaseChanged += OnPhaseChanged;
		_gameStateManager.SettlementCompleted += OnSettlementCompleted;
		_gameStateManager.TurnChanged += OnTurnChangedFromStateManager;
		_gameStateManager.TurnReadyForPass += OnTurnReadyForPass;
		_gameStateManager.ContinueTurnRequested += OnContinueTurnRequested;
		_gameStateManager.PlayerWon += OnPlayerWon;
		_gameStateManager.FoulCommitted += OnFoulCommitted;

		// Connect to PenaltyPiecesTweenCompleted for turn advancement flow
		PenaltyPiecesTweenCompleted += OnPenaltyPiecesTweenCompleted;

		GD.Print("[CarromGame] GameStateManager initialized");
	}

	/// <summary>
	/// Setup state manager with current pieces after mode initialization
	/// </summary>
	private void SetupStateMachineForCurrentMode()
	{
	}

	/// <summary>
	/// Initialize score display UI component
	/// </summary>
	private void InitializeScoreDisplay()
	{
		_scoreDisplay = new CarromScoreDisplay();
		AddChild(_scoreDisplay);
		_scoreDisplay.SetVisible(false); // Hidden by default, shown in competitive mode
		_scoreDisplay.PassTurnRequested += OnPassTurnRequested;

		_playerSetupMenu = new CarromPlayerSetupMenu();
		AddChild(_playerSetupMenu);
		_playerSetupMenu.GameStartRequested += OnPlayerSetupMenuGameStartRequested;
		_playerSetupMenu.MenuCancelled += OnPlayerSetupMenuCancelled;
	}

	/// <summary>
	/// Create all manager objects and add them to scene tree.
	/// Does NOT initialize them - initialization happens separately in <see cref="InitializeManagers"/>.
	/// </summary>
	private void CreateManagers()
	{
		_pieceFactory = new CarromPieceFactory();
		AddChild(_pieceFactory);

		_gameStateManager = new GameStateManager();
		AddChild(_gameStateManager);

		_practiceModeManager = new CarromPracticeModeManager();
		AddChild(_practiceModeManager);

		_competitiveModeManager = new CarromCompetitiveModeManager();
		AddChild(_competitiveModeManager);

		GD.Print("[CarromGame] All managers created");
	}

	/// <summary>
	/// Initializes in dependency order: PieceFactory first, then mode managers
	/// </summary>
	private void InitializeManagers()
	{
		_pieceFactory.Initialize(_board, PhysicsConfig, WhitePieceTemplate, BlackPieceTemplate, RedPieceTemplate, StrikerTemplate);
		_pieceFactory.SetPhysicsLimits(MinVelocityThreshold, AngularMinThreshold,
			MaxVelocityLimit, MaxAngularVelocity, VelocityAlertThreshold);

		_practiceModeManager.Initialize(_board, _inputController, PhysicsConfig, BlackPieceTemplate, StrikerTemplate);
		_practiceModeManager.SetPieceFactory(_pieceFactory);

		// Competitive mode manager requires GameStateManager to already be initialized
		_competitiveModeManager.Initialize(_gameStateManager, _board, _inputController, PhysicsConfig, WhitePieceTemplate, BlackPieceTemplate, RedPieceTemplate, StrikerTemplate, CompetitiveCreditCost);
		_competitiveModeManager.SetPieceFactory(_pieceFactory);

		ConnectManagerSignals();

		GD.Print("[CarromGame] All managers initialized");
	}

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

	#endregion

	#region Core Game Loop

	/// <summary>
	/// Check if all pieces have stopped moving (delegated to current mode manager)
	/// </summary>
	private bool AreAllPiecesStopped()
	{
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
			.AddSection(
				"🎯 WELCOME TO CARROM 🎯",
				"• Pocket all your assigned pieces (⚪ White or ⚫ Black) to win the game",
				"• Use the striker to hit pieces into corner pockets",
				"• The red piece (👑 Queen) is worth 3 points and must be 'covered' by your piece",
				"• Practice Mode lets you play freely - Competitive Mode follows official ICF rules!")

			.AddSection(
				"🎱 HOW TO PLAY 🎱",
				"• Drag DOWN from the striker to aim, then release to shoot",
				"• Drag LEFT or RIGHT on the Striker to reposition it on the baseline",
				"• Pocket your color pieces while avoiding opponent pieces and fouls",
				"• Valid shots require hitting your own pieces first or pocketing any piece")

			.AddSection(
				"⚠️ RULES & FOULS ⚠️",
				"• CONTINUE TURN: Pocket your piece(s) legally = shoot again!",
				"• FOUL CONDITIONS: Striker pocketed, opponent piece hit first, or leaves the board",
				"• FOUL PENALTY: Lose your turn + return one of your pocketed pieces to center",
				"• QUEEN COVERING: Must pocket your own piece immediately after Queen in same turn",
				"• BREAKING RULE: 3 attempts maximum to disturb pieces, then turn passes")

			.AddSection(
				"🏆 COMPETITIVE MODES 🏆",
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

	#endregion

	#region Round Lifecycle

	/// <summary>
	/// Check if a carrom round is currently active
	/// A round is active when the state machine is in any state except Initializing
	/// </summary>
	public bool IsRoundActive()
	{
		// Only competitive rounds are considered "active rounds"
		// Practice mode is free-play and shouldn't block competitive mode start
		return _gameStateManager != null &&
			   _gameStateManager.CurrentState != GameStateManager.GamePhase.Initializing &&
			   _carromGameMode == CarromGameMode.Competitive;
	}

	/// <summary>
	/// Start a new carrom round
	/// </summary>
	public void StartRound()
	{
		// Transition to ready phase - settlement will poll piece factory directly
		_gameStateManager?.TransitionToPhase(GameStateManager.GamePhase.Ready);

		Platform.Host?.NotifyGameStarted();

		EmitSignal(SignalName.RoundStarted);
		GD.Print("[CarromGame] Round started");
	}

	/// <summary>
	/// End the current carrom round
	/// </summary>
	public void EndRound()
	{
		// Game state is managed by state machine - no need to manually transition
		Platform.Host?.NotifyGameEnded();

		EmitSignal(SignalName.RoundEnded);
		GD.Print("[CarromGame] Round ended");
	}

	#endregion

	#region Game Modes

	/// <summary>
	/// Start practice mode (single piece, free play)
	/// </summary>
	public virtual void StartPracticeMode()
	{
		// Cancel current round if somehow still active (safety check)
		if (IsRoundActive())
		{
			EndRound();
		}

		_competitiveModeManager?.CleanupMode();
		_scoreDisplay?.SetVisible(false);
		Platform.Notifications.ClearAll();

		// Reset camera to default rotation (player 0 position)
		_cameraController?.ResetRotation();

		_currentModeManager = _practiceModeManager;
		_carromGameMode = CarromGameMode.Practice;
		ResetGame();

		// Delegate to practice mode manager - it will emit PracticeModeSetupComplete when done
		_practiceModeManager?.SetupPracticeMode();
		SetupStateMachineForCurrentMode();
	}

	/// <summary>
	/// Return to practice mode with confirmation dialog
	/// Follows same pattern as ReturnToMainMenu() for consistency
	/// </summary>
	protected virtual async void ReturnToPractice()
	{
		var uiManager = Platform.UI;
		if (uiManager != null)
		{
			bool confirmed = await uiManager.ShowConfirmationAsync(
				"Return to Practice",
				"Are you sure you want to return to practice mode?\n\nThe current competitive match will be cancelled.",
				"Return to Practice",
				"Cancel");

			if (!confirmed)
			{
				return; // User cancelled
			}
		}

		if (IsRoundActive())
		{
			EndRound();
		}

		StartPracticeMode();

		// Refresh UI to update button states (re-enable logout button, etc.)
		RefreshUI();
	}

	/// <summary>
	/// Start competitive mode (full carrom rules); shows the player setup menu first
	/// </summary>
	public virtual void StartCompetitiveMode(int playerCount = 2)
	{
		if (IsRoundActive())
		{
			return;
		}

		// Store player count for when menu signals game start
		_pendingPlayerCount = playerCount;

		// Show player setup menu instead of immediately starting
		_playerSetupMenu?.ShowMenu(playerCount, CompetitiveCreditCost);
	}

	/// <summary>
	/// Actually start the competitive mode after player setup is complete
	/// </summary>
	private void StartCompetitiveModeInternal(int playerCount)
	{
		if (IsRoundActive())
		{
			return;
		}

		_practiceModeManager?.CleanupMode();
		_competitiveModeManager?.SetPlayerCount(playerCount);

		_currentModeManager = _competitiveModeManager;
		_carromGameMode = CarromGameMode.Competitive;
		_gameStateManager.SetGameMode(CarromGameMode.Competitive);
		ResetGame();
		StartRound();

		bool success = _competitiveModeManager.StartCompetitiveMode();
		if (!success)
		{
			return;
		}

		SetupStateMachineForCurrentMode();
	}

	/// <summary>
	/// Reset game state - state machine handles all state management
	/// </summary>
	private void ResetGame()
	{
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

		ExecutePracticeReset();
	}

	private void ExecutePracticeReset()
	{
		try
		{
			_practiceModeManager?.RequestExplicitReset();
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[CarromGame] Practice reset failed with exception: {ex.Message}");
		}
	}

	#endregion

	#region Input Handling

	/// <summary>
	/// Handle strike execution from input controller
	/// </summary>
	private void OnStrikeExecuted(Vector2 force)
	{
		StopAllPieceHighlights();
		Platform.Notifications.ClearSticky();

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

	private void StopAllPieceHighlights()
	{
		// Get all active pieces from the current mode (includes striker)
		var activePieces = _currentModeManager?.GetActivePieces();
		if (activePieces == null)
		{
			return;
		}

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
		if (_carromGameMode != CarromGameMode.Competitive || _competitiveModeManager == null)
		{
			return;
		}

		// Map player index to piece type (player1=White, player2=Black)
		PieceType playerPieceType = playerIndex == 0 ? PieceType.White : PieceType.Black;

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

	private void OnAimingStateChanged(bool isAiming, Vector2 aimDirection, float power)
	{
		QueueRedraw();
	}

	#endregion

	#region Game Events

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

		_currentModeManager?.OnPiecePocketed(piece);

		if (_carromGameMode == CarromGameMode.Competitive && _scoreDisplay != null && _competitiveModeManager != null)
		{
			_scoreDisplay.UpdateAllPlayerScores(_competitiveModeManager.GetPlayers());
		}

		EmitSignal(SignalName.PiecePocketed, playerId, piece);
	}

	/// <summary>
	/// Handle piece collision for sound/effects
	/// </summary>
	private void OnPieceCollided(CarromPiece piece, CarromPiece otherPiece, Vector2 impactForce)
	{
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
			_ => 0.0f,
		};
	}

	private void ResetPieceToStart(CarromPiece piece, Vector2 startPosition)
	{
		if (!GodotObject.IsInstanceValid(piece))
		{
			return;
		}

		// Use the immediate synchronous reset method with global coordinates
		piece.Reset(ToGlobal(startPosition));
	}

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
	/// Restores the striker to its baseline position, avoiding overlap with other pieces.
	/// </summary>
	public bool RestoreStrikerToBaseline(int? playerIndexOverride = null)
	{
		var striker = _currentModeManager?.GetStriker();
		if (striker == null || !GodotObject.IsInstanceValid(striker))
		{
			GD.PrintErr("[CarromGame] RestoreStrikerToBaseline: No valid striker found");
			return false;
		}

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
			if (playerIndexOverride.HasValue)
			{
				playerIndex = playerIndexOverride.Value;
			}
			else
			{
				playerIndex = _competitiveModeManager?.GetCurrentPlayer()?.PlayerId == "player1" ? 0 : 1;
			}

			baselinePosition = _board?.GetBaselinePosition(playerIndex) ?? Vector2.Zero;
		}

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
			striker.Reset(globalBaselinePosition, immediate: true);
			MarkRecentRestoration(striker);

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
	/// Smoothly tweens the striker to its baseline position for cinematic camera transitions, avoiding overlap with other pieces.
	/// </summary>
	public void TweenStrikerToBaseline(int? playerIndexOverride = null, float duration = 0.6f)
	{
		var striker = _currentModeManager?.GetStriker();
		if (striker == null || !GodotObject.IsInstanceValid(striker))
		{
			GD.PrintErr("[CarromGame] TweenStrikerToBaseline: No valid striker found");
			return;
		}

		// Same baseline-position logic as RestoreStrikerToBaseline
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
			if (playerIndexOverride.HasValue)
			{
				playerIndex = playerIndexOverride.Value;
			}
			else
			{
				playerIndex = _competitiveModeManager?.GetCurrentPlayer()?.PlayerId == "player1" ? 0 : 1;
			}

			baselinePosition = _board?.GetBaselinePosition(playerIndex) ?? Vector2.Zero;
		}

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
			striker.Modulate = CarromPalette.ModulateOpaque;

			MarkRecentRestoration(striker);

			if (_strikerTween != null && _strikerTween.IsValid())
			{
				_strikerTween.Kill();
			}

			_strikerTween = null;

			_strikerTween = CreateTween();
			if (_strikerTween == null)
			{
				GD.PrintErr("[CarromGame] Failed to create striker tween - falling back to immediate restoration");
				RestoreStrikerToBaseline(playerIndexOverride);
				return;
			}

			_strikerTween.SetEase(Tween.EaseType.Out);
			_strikerTween.SetTrans(Tween.TransitionType.Cubic);

			// This happens when striker begins tweening to baseline (perfect timing for highlights)
			_waitingForTurnTransition = false;
			StartPieceHighlightsForPlayer(playerIndex);

			// Show turn notification at same time as highlights for visual consistency
			if (_competitiveModeManager != null && _carromGameMode == CarromGameMode.Competitive)
			{
				var currentPlayer = _competitiveModeManager.GetCurrentPlayer();
				if (currentPlayer != null)
				{
					string pieceIcon = currentPlayer.AssignedPieceType == PieceType.White ? "⚪" : "⚫";

					// Get turn number from current player index + 1 for display
					int turnNumber = _competitiveModeManager.GetPlayers().Count > 0 ?
						(_competitiveModeManager.GetPlayers().IndexOf(currentPlayer) + 1) : 1;
					string message = $"🎯 {currentPlayer.PlayerId.ToUpper()}'S TURN {pieceIcon}";
					ShowCarromNotification(NotificationType.TurnStart, message);
				}
			}

			// Validate striker before animating to avoid "Tween started with no Tweeners" error
			if (!GodotObject.IsInstanceValid(striker))
			{
				GD.PrintErr("[CarromGame] Striker became invalid before tween animation - falling back to immediate restoration");
				_strikerTween?.Kill();
				_strikerTween = null;
				RestoreStrikerToBaseline(playerIndexOverride);
				return;
			}

			var propertyTweener = _strikerTween.TweenProperty(striker, TweenConstants.GlobalPosition, globalBaselinePosition, duration);

			// Only add callback if property tweener was successfully created
			if (propertyTweener != null)
			{
				_strikerTween.TweenCallback(Callable.From(() =>
				{
					// Validate final position (don't interfere with physics - let tween handle positioning)
					if (GodotObject.IsInstanceValid(striker))
					{
						bool tweenSucceeded = ValidateStrikerRestoration(striker, globalBaselinePosition);
						if (!tweenSucceeded)
						{
							GD.PrintErr("[CarromGame] Striker tween validation failed after completion");
						}
					}

					// Complete the turn flow by transitioning game state to Ready (re-enables input)
					ForceGameStateToReady();
				}));
			}
			else
			{
				GD.PrintErr("[CarromGame] Failed to create striker property tweener - falling back to immediate restoration");
				_strikerTween?.Kill();
				_strikerTween = null;
				RestoreStrikerToBaseline(playerIndexOverride);
			}
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[CarromGame] TweenStrikerToBaseline: Exception during tween setup: {ex.Message}");
			RestoreStrikerToBaseline(playerIndexOverride);
		}
	}

	/// <summary>
	/// Force the game state machine to Ready state (called by mode manager for continued turns)
	/// </summary>
	public void ForceGameStateToReady()
	{
		_gameStateManager?.ForceToReady();
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
		var validPieces = penaltyPieces.Where(p => GodotObject.IsInstanceValid(p)).ToList();

		if (validPieces.Count == 0)
		{
			// No valid penalty pieces to animate, proceed directly to camera transition
			if (penaltyPieces.Count > 0)
			{
				GD.PrintErr($"[PENALTY TWEEN] {penaltyPieces.Count} penalty pieces were invalid - skipping animation");
			}

			EmitSignal(SignalName.PenaltyPiecesTweenCompleted);
			return;
		}

		penaltyPieces = validPieces;

		// Reveal all valid pieces BEFORE creating tween (fixes invisible piece bug)
		foreach (var piece in penaltyPieces)
		{
			RevealPieceForAnimation(piece);
		}

		if (_penaltyPiecesTween != null && _penaltyPiecesTween.IsValid())
		{
			_penaltyPiecesTween.Kill();
		}

		_penaltyPiecesTween = null;

		_penaltyPiecesTween = CreateTween();
		if (_penaltyPiecesTween == null)
		{
			GD.PrintErr("[PENALTY TWEEN] Failed to create penalty pieces tween - falling back to immediate completion");
			EmitSignal(SignalName.PenaltyPiecesTweenCompleted);
			return;
		}

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

		bool anyTweenersAdded = false;

		for (int i = 0; i < penaltyPieces.Count; i++)
		{
			var piece = penaltyPieces[i];
			if (!GodotObject.IsInstanceValid(piece))
			{
				continue;
			}

			// Get target position from metadata
			var targetPosition = piece.GetMeta("tween_target_position", Vector2.Zero).AsVector2();

			// Note: Piece already revealed in TweenPenaltyPiecesToBoard() before tween creation
			_penaltyPiecesTween.TweenProperty(piece, TweenConstants.GlobalPosition, targetPosition, pieceAnimationDuration);
			anyTweenersAdded = true;

			// Add delay between pieces (except after the last piece)
			if (i < penaltyPieces.Count - 1)
			{
				_penaltyPiecesTween.TweenInterval(delayBetweenPieces);
			}
		}

		// Only add callback if tweeners were actually added
		// Otherwise, complete immediately to avoid "Tween started with no Tweeners" error
		if (anyTweenersAdded)
		{
			_penaltyPiecesTween.TweenCallback(Callable.From(OnAllPenaltyPiecesTweenCompleted));
		}
		else
		{
			GD.Print("[PENALTY TWEEN] No valid pieces to animate - completing immediately");
			_penaltyPiecesTween?.Kill();
			_penaltyPiecesTween = null;
			OnAllPenaltyPiecesTweenCompleted();
		}
	}

	/// <summary>
	/// Reveal a penalty piece for animation by restoring its visual properties
	/// </summary>
	private void RevealPieceForAnimation(CarromPiece piece)
	{
		if (!GodotObject.IsInstanceValid(piece))
		{
			return;
		}

		// Restore piece visibility and visual properties from pocket state
		piece.Visible = true;
		piece.Scale = Vector2.One;
		piece.Modulate = CarromPalette.ModulateOpaque;
	}

	/// <summary>
	/// Called when all penalty pieces have finished tweening
	/// </summary>
	private void OnAllPenaltyPiecesTweenCompleted()
	{
		if (_competitiveModeManager is CarromCompetitiveModeManager competitiveModeManager)
		{
			competitiveModeManager.ClearTweenReturnList();
		}

		_penaltyPieceIndex = 0;

		if (_penaltyPiecesTween != null && _penaltyPiecesTween.IsValid())
		{
			_penaltyPiecesTween.Kill();
		}

		_penaltyPiecesTween = null;

		// Signal completion to trigger camera transition
		EmitSignal(SignalName.PenaltyPiecesTweenCompleted);
	}

	#endregion

	#region Player Integration

	/// <summary>
	/// Sets up player integration with GameHost when available
	/// Called during OnGameSetup phase when _playerMgmt is guaranteed to exist
	/// </summary>
	private void SetupPlayerIntegration()
	{
		var gameHost = Platform.Host;

		if (gameHost != null && GodotObject.IsInstanceValid(gameHost))
		{
			var userSession = gameHost.GetUserSession("default");
			if (userSession != null)
			{
				var player = new CarromPlayer();
				player.PlayerId = userSession.PhoneNumber;
				player.SetUserSession(userSession);
				_playerMgmt.AddPlayer(player);
			}
		}

		// Development context: No auto-player creation
		// Require actual login for data persistence and game functionality
	}

	#endregion

	#region Save/Load System

	/// <summary>
	/// Load user data - event-sourced persistence
	/// Backend rebuilds state from events
	/// </summary>
	private async void LoadUserDataAsync()
	{
		await Task.CompletedTask;
	}

	/// <summary>
	/// Save game result via event-sourced persistence
	/// </summary>
	private async void SaveGameResultAsync(string playerId, bool isWin)
	{
		string phoneNumber = GetCurrentUserPhoneNumber();
		if (string.IsNullOrEmpty(phoneNumber))
		{
			return;
		}

		try
		{
			// For single-player competitive mode, we only have one player's score
			var scores = new Dictionary<string, int>();
			if (_competitiveModeManager != null)
			{
				scores[playerId] = 0; // TODO: Get actual score from competitive mode
			}

			var mode = _carromGameMode.ToString().ToLowerInvariant();
			var winnerId = isWin ? playerId : string.Empty;

			// Emit result through the shared guarded path (event-sourced persistence)
			var result = await _carromEventService.EmitRoundFinishAsync(mode, winnerId, scores);

			// Thread-safe logging via CallDeferred
			if (result.IsFailure(out var error))
			{
				CallDeferred(MethodName.LogAsyncError, $"Failed to emit round_finish event: {error.Message}");
			}
			else
			{
				CallDeferred(MethodName.LogSavedData, $"Emitted round_finish event - Win: {isWin}");
			}
		}
		catch (System.Exception ex)
		{
			// Thread-safe error logging via CallDeferred
			CallDeferred(MethodName.LogAsyncError, $"Exception emitting game result event: {ex.Message}");
		}
	}

	/// <summary>
	/// Get current win streak for a player from competitive mode manager
	/// </summary>
	private int GetCurrentWinStreak(string playerId)
	{
		if (_competitiveModeManager == null)
		{
			return 0;
		}

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
			var sessionManager = Platform.Session;
			if (sessionManager != null && GodotObject.IsInstanceValid(sessionManager))
			{
				var currentSession = sessionManager.GetPrimarySession();
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

	#endregion

	#region UI Integration Overrides

	/// <summary>
	/// Override to provide carrom-specific context buttons
	/// </summary>
	public override ContextButtonData[] GetContextButtons()
	{
		var buttons = new List<ContextButtonData>();

		buttons.Add(GameContextButton.CreateReturnToMenuButton(() =>
		{
			var sessionManager = Platform.Session;
			sessionManager?.ResetAllIdleTimers();
			ReturnToMainMenu();
		}));

		// Mode-specific buttons
		if (_carromGameMode == CarromGameMode.Practice)
		{
			buttons.Add(new ContextButtonData("Reset", () =>
			{
				var sessionManager = Platform.Session;
				sessionManager?.ResetAllIdleTimers();
				RequestPracticeReset();
			}, "🔄", true, "Reset practice session"));

			buttons.Add(new ContextButtonData("2-Player Match", () =>
			{
				var sessionManager = Platform.Session;
				sessionManager?.ResetAllIdleTimers();
				StartCompetitiveMode(2);
			}, "👥", true, "Start 2-player competitive match"));

			buttons.Add(new ContextButtonData("4-Player Match", () =>
			{
				var sessionManager = Platform.Session;
				sessionManager?.ResetAllIdleTimers();
				StartCompetitiveMode(4);
			}, "👨‍👩‍👧‍👦", true, "Start 4-player doubles match"));
		}
		else if (_carromGameMode == CarromGameMode.Competitive)
		{
			buttons.Add(new ContextButtonData("Scores", () =>
			{
				ShowScoreboard();
			}, "📊", true, "View current scores"));

			buttons.Add(new ContextButtonData("Return to Practice", () =>
			{
				var sessionManager = Platform.Session;
				sessionManager?.ResetAllIdleTimers();
				ReturnToPractice();
			}, "🔄", true, "Return to practice mode"));
		}

		return [.. buttons];
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

		var viewport = GetViewport();
		if (viewport != null)
		{
			var screenSize = viewport.GetVisibleRect().Size;
			dialog.Position = new Vector2I(
				(int)((screenSize.X / 2) - 200),
				(int)((screenSize.Y / 2) - 175));
		}

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
		{
			return;
		}

		var currentPlayer = _competitiveModeManager.GetCurrentPlayer();
		if (currentPlayer == null)
		{
			return;
		}

		string pieceIcon = currentPlayer.AssignedPieceType == PieceType.White ? "⚪" : "⚫";
		string transitionMessage = $"🎯 {currentPlayer.PlayerId.ToUpper()}'S TURN {pieceIcon} - Turn {turnNumber}";

		// Show transition in the score display bar - this shows the pass turn button
		if (_scoreDisplay != null)
		{
			_scoreDisplay.ShowTurnTransition(transitionMessage, 3.0f);

			var players = _competitiveModeManager.GetPlayers();
			_scoreDisplay.UpdateAllPlayerScores(players);
		}

		// Camera rotation happens in OnPassTurnRequested when the turn is actually advanced
	}

	/// <summary>
	/// Handle manual pass turn request from score display, including camera rotation timing after the turn switch.
	/// </summary>
	private void OnPassTurnRequested()
	{
		if (_carromGameMode != CarromGameMode.Competitive || _gameStateManager == null)
		{
			return;
		}

		bool allPiecesStopped = AreAllPiecesStopped();
		if (!allPiecesStopped)
		{
			return;
		}

		// Execute pass turn via GameStateManager (handles turn advancement and phase transition)
		_gameStateManager.ExecutePassTurn();

		// This will either animate penalty pieces sequentially or proceed directly to camera transition
		TweenPenaltyPiecesToBoard();

		// Camera transition happens via PenaltyPiecesTweenCompleted signal;
		// striker restoration happens after that, via CameraTransitionCompleted signal
	}

	/// <summary>
	/// Show comprehensive game over screen with final statistics
	/// </summary>
	private void ShowGameOverScreen(string winnerPlayerId)
	{
		if (_competitiveModeManager == null)
		{
			return;
		}

		var players = _competitiveModeManager.GetPlayers();
		var winner = players.FirstOrDefault(p => p.PlayerId == winnerPlayerId);
		if (winner == null)
		{
			return;
		}

		int playerCount = players.Count;
		string modeText = playerCount == 4 ? "Doubles" : "Singles";

		var gameOverMessage = new System.Text.StringBuilder($"🏆 GAME OVER - {modeText.ToUpper()} 🏆\n\n");
		gameOverMessage.AppendLine($"WINNER: {winner.PlayerId.ToUpper()}");
		gameOverMessage.AppendLine($"Team: {winner.AssignedPieceType} Pieces");
		gameOverMessage.AppendLine();

		gameOverMessage.AppendLine("=== WINNER STATS ===");
		gameOverMessage.AppendLine($"Pieces Pocketed: {winner.PiecesPocketed}/9");
		gameOverMessage.AppendLine($"Queen Status: {(winner.HasQueen ? (winner.QueenCovered ? "Covered ✓" : "Not Covered ✗") : "Not Pocketed")}");
		gameOverMessage.AppendLine($"Accuracy: {winner.GetAccuracy():P1}");
		gameOverMessage.AppendLine($"Fouls: {winner.GetFouls()}");
		gameOverMessage.AppendLine();

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
				_ => $"{i + 1}th",
			};

			gameOverMessage.Append($"{position}: {player.PlayerId} - {player.PiecesPocketed}/9 pieces");
			if (player.HasQueen)
			{
				gameOverMessage.Append(player.QueenCovered ? " + Queen ✓" : " + Queen ✗");
			}

			gameOverMessage.AppendLine($" (Accuracy: {player.GetAccuracy():P1})");
		}

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

		var viewport = GetViewport();
		if (viewport != null)
		{
			var screenSize = viewport.GetVisibleRect().Size;
			dialog.Position = new Vector2I(
				(int)((screenSize.X / 2) - 250),
				(int)((screenSize.Y / 2) - 200));
		}

		GetTree().CurrentScene.AddChild(dialog);
		dialog.PopupCentered();

		// Auto-remove dialog when closed
		dialog.Confirmed += () => dialog.QueueFree();
		dialog.Canceled += () => dialog.QueueFree();

		dialog.AddButton("Return to Practice", false, "practice");
		dialog.CustomAction += (action) =>
		{
			if (action.ToString() == "practice")
			{
				dialog.QueueFree();
				StartPracticeMode();
			}
		};
	}

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

	#endregion

	#region Signal Handlers For Managers

	private static readonly bool _isDebugBuild = OS.IsDebugBuild();

	/// <summary>
	/// Handle phase changes from GameStateManager
	/// </summary>
	private void OnPhaseChanged(string oldPhase, string newPhase)
	{
		if (_isDebugBuild)
		{
			PushDebugMetrics();
		}

		if (_scoreDisplay != null)
		{
			_scoreDisplay.UpdateGameState(newPhase);

			// Input is blocked when not in Ready phase
			bool inputBlocked = newPhase != "Ready";
			_scoreDisplay.SetInputBlockedVisual(inputBlocked);

			// Keep the score display's own phase-driven UI in sync
			_scoreDisplay.OnPhaseChanged(oldPhase, newPhase);
		}

		// Update title to reflect current state for debugging
		RefreshUI();

		// Reset striker position when transitioning from Settlement to Ready
		if (newPhase == "Ready" && oldPhase == "Settlement")
		{
			if (_carromGameMode == CarromGameMode.Practice)
			{
				// Practice mode: Direct striker reset (no camera movement)
				RestoreStrikerToBaseline();
			}

			// Competitive mode: Uses TweenStrikerToBaseline via OnCameraTransitionCompleted
		}
	}

	/// <summary>
	/// Handle turn changes from GameStateManager
	/// </summary>
	private void OnTurnChangedFromStateManager(string playerId, int turnNumber)
	{
		GD.Print($"[CarromGame] Turn changed to {playerId} (turn {turnNumber})");

		var playerIndex = playerId switch
		{
			"player1" => 0,
			"player2" => 1,
			"player3" => 2,
			"player4" => 3,
			_ => 0,
		};

		_cameraController?.TransitionToPlayerWithZoom(playerIndex, 1.2f);
	}

	// NOTE: OnPlayerWon and OnFoulCommitted handlers exist later in the file with full implementation

	/// <summary>
	/// Handle settlement completion from state machine
	/// State machine automatically transitions to Ready, no manual control needed
	/// </summary>
	private void OnSettlementCompleted()
	{
	}

	/// <summary>
	/// Handle camera transition completion - smoothly tween striker to new player's baseline
	/// </summary>
	private void OnCameraTransitionCompleted()
	{
		TweenStrikerToBaseline(duration: 0.6f);
	}

	/// <summary>
	/// Handle penalty pieces tween completion - proceed to camera transition
	/// </summary>
	private void OnPenaltyPiecesTweenCompleted()
	{
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
				_ => 0,
			};

			// Start cinematic zoom transition (striker restoration happens when transition completes)
			_cameraController.TransitionToPlayerWithZoom(playerIndex, 1.2f);
		}
	}

	/// <summary>
	/// Handle practice reset request from practice mode manager
	/// </summary>
	private void OnPracticeResetRequested()
	{
		// Phase transition is handled by practice mode manager's DeferredPhaseTransition()
	}

	/// <summary>
	/// Handle practice mode setup completion with comprehensive validation
	/// </summary>
	private void OnPracticeModeSetupComplete()
	{
		bool validationPassed = ValidateInputControllerSynchronization();
		if (validationPassed)
		{
			_gameStateManager?.TransitionToPhase(GameStateManager.GamePhase.Ready);
			GD.Print("[CarromGame] Practice mode setup complete - transitioned to Ready phase");
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
		if (_inputController == null || !GodotObject.IsInstanceValid(_inputController))
		{
			GD.PrintErr("[CarromGame] Validation failed: Input controller is null or invalid");
			return false;
		}

		if (_gameStateManager == null || !GodotObject.IsInstanceValid(_gameStateManager))
		{
			GD.PrintErr("[CarromGame] Validation failed: Game state machine is null or invalid");
			return false;
		}

		if (_currentModeManager == null)
		{
			GD.PrintErr("[CarromGame] Validation failed: Current mode manager is null");
			return false;
		}

		var striker = _currentModeManager.GetStriker();
		if (striker == null || !GodotObject.IsInstanceValid(striker))
		{
			GD.PrintErr("[CarromGame] Validation failed: Striker is null or invalid");
			return false;
		}

		if (!_inputController.HasGameState())
		{
			GD.PrintErr("[CarromGame] Validation failed: Input controller lacks game state reference");
			return false;
		}

		if (!_inputController.IsInputEnabled())
		{
			// Input currently disabled - this may be normal during setup
		}

		return true;
	}

	/// <summary>
	/// Attempt to recover input controller synchronization if validation fails
	/// </summary>
	private void RecoverInputControllerSynchronization()
	{
		if (_inputController != null && _gameStateManager != null)
		{
			_inputController.SetGameState(_gameStateManager);
		}

		UpdateInputControllerStriker();

		bool recoverySuccessful = ValidateInputControllerSynchronization();

		if (recoverySuccessful)
		{
			// State machine handles game start automatically
		}
		else
		{
			GD.PrintErr("[CarromGame] Recovery failed - game may not function properly");

			// Force start anyway to prevent complete blockage
		}
	}

	/// <summary>
	/// Handle turn change in competitive mode - called AFTER turn has been advanced
	/// </summary>
	private void OnTurnChanged(string playerId, int turnNumber)
	{
		EmitSignal(SignalName.TurnChanged, playerId, turnNumber);

		// Clear all piece trails when turn changes for clean visual reset
		ClearAllTrails();

		if (_scoreDisplay != null && _competitiveModeManager != null)
		{
			_scoreDisplay.SetCurrentPlayer(playerId);
			_scoreDisplay.UpdateAllPlayerScores(_competitiveModeManager.GetPlayers());
		}

		// No need to show turn transition here - that's handled by OnTurnReadyForPass
		// This method is called AFTER the turn has been advanced via pass turn button
		RefreshUI();
	}

	/// <summary>
	/// Handle turn ready for pass - shows pass turn button without changing players.
	/// Trusts GameStateManager's signal parameters instead of querying CompetitiveModeManager,
	/// avoiding state synchronization issues between the two managers.
	/// </summary>
	private void OnTurnReadyForPass(string playerId, int turnNumber)
	{
		if (_carromGameMode != CarromGameMode.Competitive)
		{
			return;
		}

		// This prevents highlights from appearing prematurely in OnReadyForInput
		_waitingForTurnTransition = true;

		// Build message using signal parameters - GameStateManager guarantees validity
		// No need to query _competitiveModeManager which might be out of sync
		string transitionMessage = $"🎯 {playerId.ToUpper()}'S TURN - Turn {turnNumber}";

		// Show ONLY the pass turn button, no other UI updates
		_scoreDisplay?.ShowTurnTransition(transitionMessage, 3.0f);
	}

	/// <summary>
	/// Handle turn continuation after valid pocket - restore striker to baseline
	/// </summary>
	private void OnContinueTurnRequested(string playerId)
	{
		GD.Print($"[CarromGame] Turn continues for {playerId} - restoring striker");
		TweenStrikerToBaseline(duration: 0.4f);
	}

	/// <summary>
	/// Handle player winning in competitive mode
	/// </summary>
	private void OnPlayerWon(string playerId)
	{
		SaveGameResultAsync(playerId, true);
		EndRound();
		Platform.Notifications.ClearAll();
		ShowGameOverScreen(playerId);
		RefreshUI();
	}

	/// <summary>
	/// Handle foul committed in competitive mode
	/// </summary>
	private void OnFoulCommitted(string playerId)
	{
		EmitSignal(SignalName.StrikerFoul, playerId);
		ShowCarromNotification(NotificationType.Foul, $"⚠️ Foul by {playerId.ToUpper()}!");

		// Also show floating text above player's score for emphasis
		_scoreDisplay?.ShowFloatingText(playerId, "Foul! ⚠️", Colors.Red);
	}

	/// <summary>
	/// Handle notification request from competitive mode manager
	/// </summary>
	private void OnNotificationRequested(int notificationType, string message)
	{
		ShowCarromNotification((NotificationType)notificationType, message);
	}

	/// <summary>
	/// Show a notification styled per Carrom's notification taxonomy
	/// (<see cref="CarromNotificationStyle"/>) via the shared platform overlay.
	/// </summary>
	private void ShowCarromNotification(NotificationType type, string message, float? durationOverride = null)
	{
		var style = CarromNotificationStyle.For(type);
		Platform.Notifications.Show(message, color: style.Color, sticky: style.Sticky, duration: durationOverride ?? style.Duration);
	}

	/// <summary>
	/// Handle competitive mode setup completion
	/// </summary>
	private void OnCompetitiveModeSetupComplete()
	{
		if (_scoreDisplay != null && _competitiveModeManager != null)
		{
			_scoreDisplay.SetVisible(true);
			_scoreDisplay.ClearPlayers();

			// Provide score display to competitive mode manager for direct floating text calls
			_competitiveModeManager.SetScoreDisplay(_scoreDisplay);

			var players = _competitiveModeManager.GetPlayers();
			foreach (var player in players)
			{
				_scoreDisplay.AddPlayer(player.PlayerId, player.AssignedPieceType);
			}

			var currentPlayer = _competitiveModeManager.GetCurrentPlayer();
			if (currentPlayer != null)
			{
				_scoreDisplay.SetCurrentPlayer(currentPlayer.PlayerId);
			}
		}

		if (_competitiveModeManager != null)
		{
			var currentPlayer = _competitiveModeManager.GetCurrentPlayer();
			if (currentPlayer != null)
			{
				string pieceIcon = currentPlayer.AssignedPieceType == PieceType.White ? "⚪" : "⚫";
				string message = $"🎯 {currentPlayer.PlayerId.ToUpper()}'S TURN {pieceIcon} - Turn 1";
				ShowCarromNotification(NotificationType.TurnStart, message);
			}
		}

		// Transition GameStateManager to Ready phase to enable input
		_gameStateManager?.TransitionToPhase(GameStateManager.GamePhase.Ready);
		GD.Print("[CarromGame] Competitive mode setup complete - transitioned to Ready phase");
	}

	/// <summary>
	/// Handle piece creation from factory (mode managers handle tracking)
	/// </summary>
	private void OnPieceCreated(CarromPiece piece)
	{
		piece.PieceCollided += OnPieceCollided;

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
		if (GodotObject.IsInstanceValid(piece))
		{
			piece.PieceCollided -= OnPieceCollided;
		}

		if (piece?.Type == PieceType.Striker)
		{
			_inputController?.SetStriker(null);
		}
	}

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
	private async void OnPlayerSetupMenuGameStartRequested()
	{
		var playerIds = _playerSetupMenu?.GetLoggedInPlayerIds();

		if (playerIds != null && playerIds.Length >= 2)
		{
			var locationManager = Platform.Location;
			if (locationManager != null && locationManager.IsConfigLoaded)
			{
				var boxId = locationManager.BoxId;

				// Create multiplayer activity session (owned by GameController, auto-closed on teardown)
				var playerIdStrings = playerIds.Select(id => id.ToString()).ToList();
				var sessionResult = await StartBackendSessionAsync(boxId, playerIds[0], playerIdStrings);

				// Validate objects still exist after async boundary
				if (!IsInstanceValid(_eventService))
				{
					GD.PrintErr("[CarromGame] Services became invalid during async operation");
					return;
				}

				if (sessionResult.IsFailure(out var sessionError))
				{
					GD.PrintErr($"[CarromGame] Failed to create multiplayer session: {sessionError.Message}");
					ShowCarromNotification(NotificationType.Foul, $"Session creation failed: {sessionError.Message}");
					return;
				}

				GD.Print("[CarromGame] Multiplayer session created successfully");
			}
			else
			{
				GD.PrintErr("[CarromGame] LocationManager not available - cannot create backend session");
			}
		}
		else
		{
			GD.PrintErr($"[CarromGame] Invalid player count for multiplayer session: {playerIds?.Length ?? 0}");
		}

		// Use the stored player count from when the menu was shown
		StartCompetitiveModeInternal(_pendingPlayerCount);
	}

	/// <summary>
	/// Handle player setup menu cancellation
	/// </summary>
	private void OnPlayerSetupMenuCancelled()
	{
		StartPracticeMode();
	}

	/// <summary>
	/// Game-specific cleanup on exit.
	/// Called by base class during _ExitTree after UI cleanup and signal disconnection.
	/// </summary>
	protected override void OnGameTeardown()
	{
		if (_isDebugBuild)
		{
			ClearDebugMetrics();
		}

		// Backend session close is handled automatically by the base class.
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

		PenaltyPiecesTweenCompleted -= OnPenaltyPiecesTweenCompleted;

		if (_gameStateManager != null && GodotObject.IsInstanceValid(_gameStateManager))
		{
			_gameStateManager.PhaseChanged -= OnPhaseChanged;
			_gameStateManager.SettlementCompleted -= OnSettlementCompleted;
			_gameStateManager.TurnChanged -= OnTurnChangedFromStateManager;
			_gameStateManager.TurnReadyForPass -= OnTurnReadyForPass;
			_gameStateManager.ContinueTurnRequested -= OnContinueTurnRequested;
			_gameStateManager.PlayerWon -= OnPlayerWon;
			_gameStateManager.FoulCommitted -= OnFoulCommitted;
		}

		if (_scoreDisplay != null && GodotObject.IsInstanceValid(_scoreDisplay))
		{
			_scoreDisplay.PassTurnRequested -= OnPassTurnRequested;
		}

		Platform.Notifications.ClearAll();

		if (_playerSetupMenu != null && GodotObject.IsInstanceValid(_playerSetupMenu))
		{
			_playerSetupMenu.GameStartRequested -= OnPlayerSetupMenuGameStartRequested;
			_playerSetupMenu.MenuCancelled -= OnPlayerSetupMenuCancelled;
		}

		_strikerTween?.Kill();
		_penaltyPiecesTween?.Kill();

		DisconnectManagerSignals();
	}

	#endregion
}
