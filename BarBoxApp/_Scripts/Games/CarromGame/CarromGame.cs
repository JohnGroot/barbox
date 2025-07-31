using Godot;
using System.Collections.Generic;
using System.Linq;

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

	// Board settings are now managed by the CarromBoard component itself

	[ExportCategory("Piece Templates")]
	[Export(PropertyHint.ResourceType, "PackedScene")] public PackedScene WhitePieceTemplate { get; set; }
	[Export(PropertyHint.ResourceType, "PackedScene")] public PackedScene BlackPieceTemplate { get; set; }
	[Export(PropertyHint.ResourceType, "PackedScene")] public PackedScene RedPieceTemplate { get; set; }
	[Export(PropertyHint.ResourceType, "PackedScene")] public PackedScene StrikerTemplate { get; set; }

	// ================================================================
	// CONSTANTS
	// ================================================================


	// ================================================================
	// PRIVATE FIELDS - GAME LOGIC
	// ================================================================

	private CarromGameMode _carromGameMode = CarromGameMode.Practice;
	private CarromBoard _board;
	private CarromInputController _inputController;
	
	// Managers
	private CarromPracticeModeManager _practiceModeManager;
	private CarromCompetitiveModeManager _competitiveModeManager;
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
			
			GD.Print("[CarromGame] CarromBoard found and configured successfully");
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
		
		GD.Print($"[CarromGame] Camera controller setup with board at origin");
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
			
			if (!GodotObject.IsInstanceValid(_inputController))
			{
				GD.PrintErr("[CarromGame] Failed to find CarromInputController in scene tree");
				_inputController = null;
				return;
			}
			
			// Connect input signals
			_inputController.StrikeExecuted += OnStrikeExecuted;
			_inputController.AimingStateChanged += OnAimingStateChanged;
			
			GD.Print("[CarromGame] CarromInputController found and configured successfully");
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
			}
			
			GD.Print($"[CarromGame] Board initialized with size: {_board.BoardSize}, border: {_board.BorderWidth}");
		}
		
		// Initialize input controller after board and camera are ready
		if (_inputController != null)
		{
			_inputController.InitializeWithBoard(_board);
			_inputController.SetCameraController(_cameraController);
			_inputController.SetPhaseManager(_phaseManager);
		}
		
		GD.Print("[CarromGame] Components initialized in order");
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
		
		GD.Print("[CarromGame] Phase manager initialized");
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
		
		// Initialize practice mode manager
		_practiceModeManager = new CarromPracticeModeManager();
		AddChild(_practiceModeManager);
		_practiceModeManager.Initialize(_board, _inputController, PhysicsConfig, BlackPieceTemplate, StrikerTemplate);
		_practiceModeManager.SetPhaseManager(_phaseManager);
		
		// Initialize competitive mode manager
		_competitiveModeManager = new CarromCompetitiveModeManager();
		AddChild(_competitiveModeManager);
		_competitiveModeManager.Initialize(_board, _inputController, PhysicsConfig, WhitePieceTemplate, BlackPieceTemplate, RedPieceTemplate, StrikerTemplate, CompetitiveCreditCost);
		_competitiveModeManager.SetPhaseManager(_phaseManager);
		
		// Connect manager signals
		ConnectManagerSignals();
		
		GD.Print("[CarromGame] Managers initialized");
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

	public override void _Process(double delta)
	{
		base._Process(delta);
		
		// Direct piece monitoring - no timer delays
		if (_phaseManager != null && _phaseManager.IsProcessing())
		{
			if (AreAllPiecesStopped())
			{
				// Pieces have stopped - notify phase manager
				_phaseManager.OnPiecesSettled();
			}
		}
	}

	protected override void UpdateGame(float delta)
	{
		base.UpdateGame(delta);
		// Additional game-specific updates can go here
	}


	/// <summary>
	/// Check if all pieces have stopped moving
	/// </summary>
	private bool AreAllPiecesStopped()
	{
		// Delegate to appropriate manager based on game mode
		if (_carromGameMode == CarromGameMode.Practice)
		{
			return _practiceModeManager?.AreAllPiecesStopped() ?? true;
		}
		else
		{
			return _competitiveModeManager?.AreAllPiecesStopped() ?? true;
		}
	}

	// ================================================================
	// GAME MODES
	// ================================================================

	/// <summary>
	/// Start practice mode (single piece, free play)
	/// </summary>
	public virtual void StartPracticeMode()
	{
		_carromGameMode = CarromGameMode.Practice;
		SetGameMode(GameMode.Practice);
		ResetGame();
		
		GD.Print("[CarromGame] Phase manager in Setup, starting practice setup");
		
		// Delegate to practice mode manager - it will emit PracticeModeSetupComplete when done
		_practiceModeManager?.SetupPracticeMode();
		
		// Phase transition to Active will happen in OnPracticeModeSetupComplete()
		
		GD.Print("[CarromGame] Practice mode started, waiting for setup completion");
	}

	/// <summary>
	/// Start competitive mode (full carrom rules)
	/// </summary>
	public virtual async void StartCompetitiveMode()
	{
		if (_isGameActive) return;
		
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
		GD.Print("[CarromGame] Competitive mode started");
	}








	/// <summary>
	/// Clear all pieces from the board
	/// </summary>
	private void ClearAllPieces()
	{
		// Delegate to piece factory
		_pieceFactory?.ClearAllPieces();
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
	/// Handle strike execution from input controller
	/// </summary>
	private void OnStrikeExecuted(Vector2 force)
	{
		if (!_phaseManager.CanReceiveInput()) return;
		
		// Get striker from appropriate manager
		CarromPiece striker = null;
		if (_carromGameMode == CarromGameMode.Practice)
		{
			striker = _practiceModeManager?.GetStriker();
		}
		else
		{
			striker = _competitiveModeManager?.GetStriker();
		}
		
		if (striker != null)
		{
			striker.ApplyStrike(force);
			_phaseManager?.BeginProcessing();
			
			GD.Print($"[CarromGame] Strike executed with force: {force}");
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
		// Delegate to appropriate manager
		if (_carromGameMode == CarromGameMode.Practice)
		{
			_practiceModeManager?.OnPiecePocketed(piece);
		}
		else
		{
			_competitiveModeManager?.OnPiecePocketed(piece);
		}
		
		// Emit main game signal
		string playerId = _carromGameMode == CarromGameMode.Practice ? "practice" : 
						 _competitiveModeManager?.GetCurrentPlayer()?.PlayerId ?? "player1";
		EmitSignal(SignalName.PiecePocketed, playerId, piece);
		
		GD.Print($"[CarromGame] Piece pocketed: {piece.Type}");
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
		
		GD.Print($"[CarromGame] Resetting {piece.Type} to starting position {startPosition}");
		
		// Use the optimized physics-safe reset method
		piece.ResetToPosition(startPosition);
		
		GD.Print($"[CarromGame] Reset {piece.Type} using optimized method");
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
		GD.Print("[CarromGame] DetectAndAdaptToContext() called");
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
				_practiceModeManager?.SchedulePracticeReset();
			}, "🔄", true, "Reset practice session"));
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

	/// <summary>
	/// Cleanup on exit
	/// </summary>
	// ================================================================
	// SIGNAL HANDLERS FOR MANAGERS
	// ================================================================

	/// <summary>
	/// Handle pieces settled from phase manager
	/// </summary>
	private void OnPiecesSettled()
	{
		// Delegate to appropriate mode manager based on game mode
		if (_carromGameMode == CarromGameMode.Practice)
		{
			_practiceModeManager?.OnPiecesSettled();
		}
		else
		{
			_competitiveModeManager?.OnPiecesSettled();
		}
	}

	/// <summary>
	/// Handle practice reset request from practice mode manager
	/// </summary>
	private void OnPracticeResetRequested()
	{
		GD.Print("[CarromGame] OnPracticeResetRequested() called - practice mode handling its own reset");
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
	/// Handle piece creation from factory
	/// </summary>
	private void OnPieceCreated(CarromPiece piece)
	{
		// Connect piece signals if needed
		piece.PieceCollided += OnPieceCollided;
	}

	/// <summary>
	/// Handle piece destruction from factory
	/// </summary>
	private void OnPieceDestroyed(CarromPiece piece)
	{
		// Clean up piece signals if needed
		if (GodotObject.IsInstanceValid(piece))
		{
			piece.PieceCollided -= OnPieceCollided;
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

	public override void _Notification(int what)
	{
		base._Notification(what);
		
		if (what == NotificationExitTree)
		{
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
			
			// Managers will clean up their own resources automatically
		}
	}
}