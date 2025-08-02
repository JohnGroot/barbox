using Godot;
using System.Collections.Generic;
using System.Linq;

namespace BarBox.Games.Racing
{
	/// <summary>
	/// 2D time trial racing game with comprehensive racing mechanics, track system, and UI
	/// </summary>
	[GlobalClass]
	public partial class RacingGame : GameController
{
	// ================================================================
	// SIGNALS
	// ================================================================
	
	[Signal] public delegate void LapCompletedEventHandler(string playerId, int lapNumber, float lapTime);
	[Signal] public delegate void RaceCompletedEventHandler(string playerId, float totalTime);
	[Signal] public delegate void CheckpointCrossedEventHandler(string playerId, int checkpointIndex, float gapTime);

	// ================================================================
	// EXPORT PROPERTIES - RACING SETTINGS
	// ================================================================
	 
	[ExportCategory("Racing Settings")]
	[Export] public int TargetLaps 
	{ 
		get => _targetLaps; 
		set 
		{ 
			_targetLaps = value; 
			if (_timingSystem != null) 
			{
				_timingSystem.TargetLaps = value;
			}
		} 
	}
	private int _targetLaps = 3;
	[Export] public bool ShowCountdown { get; set; } = true;
	[Export] public float CountdownDuration { get; set; } = 4.0f; // 3-2-1-GO = 4 seconds
	[Export] public int TimeTrialCreditCost { get; set; } = 1; // Credit cost for time trial mode

	[ExportCategory("Track Settings")]
	[Export] public Godot.Collections.Array<PackedScene> TrackScenes { get; set; } = new Godot.Collections.Array<PackedScene>();





	// ================================================================
	// CONSTANTS
	// ================================================================
	
	// Total height reserved for TopMenuBar (100px) + ContextButtonBar (50px)
	private const float TOP_MENU_HEIGHT = 150.0f;

	// ================================================================
	// PRIVATE FIELDS - RACING LOGIC
	// ================================================================
	
	// Racing timing system
	private RacingTimingSystem _timingSystem;
	
	// Track validation system
	private RacingTrackValidationSystem _trackValidationSystem;
	
	// Camera controller
	private RacingCameraController _cameraController;
	
	// Racing car controller
	private RacingCarController _carController;

	// ================================================================
	// PRIVATE FIELDS - TRACK AND WORLD SYSTEMS
	// ================================================================
	
	// Track system
	private RacingTrackDefinition _trackDefinition;
	private Curve2D _trackCurve;
	private RacingCheckpointTrigger[] _checkpointTriggers;
	private bool[] _checkpointsCrossed;
	private int _nextCheckpointIndex = 0;
	private int _currentTrackIndex = 0;

	// ================================================================
	// PRIVATE Fields - UI SYSTEM
	// ================================================================
	
	// UI manager for all racing game UI elements
	private RacingUIManager _uiManager;

	// ================================================================
	// PRIVATE FIELDS - VISUAL FEEDBACK SYSTEM
	// ================================================================
	
	// Visual feedback renderer for tire trails, input lines, and mouse indicators
	private RacingVisualFeedbackRenderer _visualRenderer;
	
	// Direct input handling (restored for arc positioning fix)
	private Vector2 _directTargetPosition = Vector2.Zero;
	private bool _hasDirectInput = false;

	// ================================================================
	// INITIALIZATION
	// ================================================================

	public override void _Ready()
	{
		GD.Print("[RacingGame] _Ready() starting");
		GameId = "racing_game";
		SetGameMode(GameMode.Practice); // Start in practice mode
		
		// Initialize all systems FIRST before calling base._Ready()
		// This ensures timing system exists when InitializeGame() is called
		SetupTimingSystem();
		SetupTrackValidationSystem();
		SetupCameraController();
		SetupCarController();
		SetupUI();
		InitializeTrackSystem();
		
		// THEN call base which triggers InitializeGame() -> StartPractice()
		base._Ready();
		
		// Context detection can happen after everything is ready
		DetectAndAdaptToContext();
		
		GD.Print("[RacingGame] _Ready() completed");
	}

	protected override void InitializeGame()
	{
		base.InitializeGame();
		StartPractice(); // Default to practice mode
	}

	/// <summary>
	/// Setup the racing timing system
	/// </summary>
	private void SetupTimingSystem()
	{
		try
		{
			_timingSystem = new RacingTimingSystem();
			if (_timingSystem == null)
			{
				GD.PrintErr("[RacingGame] Failed to create RacingTimingSystem instance");
				return;
			}
			
			_timingSystem.TargetLaps = TargetLaps;
			_timingSystem.SetGameMode(_currentGameMode);
			AddChild(_timingSystem);
			
			// Verify the node was added successfully
			if (!GodotObject.IsInstanceValid(_timingSystem) || _timingSystem.GetParent() != this)
			{
				GD.PrintErr("[RacingGame] Failed to add RacingTimingSystem to scene tree");
				_timingSystem = null;
				return;
			}
			
			// Connect timing system signals to maintain existing behavior
			_timingSystem.LapCompleted += (playerId, lapNumber, lapTime) => 
			{
				// Update player score (best lap time or total time)
				if (_currentGameMode == GameMode.Practice)
				{
					UpdateScore(playerId, _timingSystem.GetPlayerBestLapTime(playerId));
				}
				else
				{
					// In time trial, score is total time
					float totalTime = _timingSystem.CalculatePlayerScore(playerId, _currentGameMode);
					UpdateScore(playerId, totalTime);
				}
				
				// Forward the signal to maintain existing external integrations
				EmitSignal(SignalName.LapCompleted, playerId, lapNumber, lapTime);
			};

			_timingSystem.RaceCompleted += (playerId, totalTime) => 
			{
				// Forward the signal to maintain existing external integrations
				EmitSignal(SignalName.RaceCompleted, playerId, totalTime);
			};

			_timingSystem.CheckpointCrossed += (playerId, checkpointIndex, gapTime) => 
			{
				// Forward the signal to maintain existing external integrations
				EmitSignal(SignalName.CheckpointCrossed, playerId, checkpointIndex, gapTime);
			};
			
			GD.Print("[RacingGame] RacingTimingSystem initialized successfully");
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[RacingGame] Exception creating RacingTimingSystem: {ex.Message}");
			_timingSystem = null;
		}
	}

	/// <summary>
	/// Setup the track validation system
	/// </summary>
	private void SetupTrackValidationSystem()
	{
		try
		{
			_trackValidationSystem = new RacingTrackValidationSystem();
			if (_trackValidationSystem == null)
			{
				GD.PrintErr("[RacingGame] Failed to create RacingTrackValidationSystem instance");
				return;
			}
			
			// Set default values (these would be configurable via exports on the system)
			_trackValidationSystem.CenterLineProximityRange = 40.0f;
			_trackValidationSystem.CenterLineAccelerationBonus = 1.5f;
			_trackValidationSystem.TrackProximityRange = 20.0f;
			_trackValidationSystem.OffTrackSpeedPenalty = 0.3f;
			_trackValidationSystem.OffTrackTurnPenalty = 0.3f;
			_trackValidationSystem.OffTrackAccelerationPenalty = 0.3f;
			_trackValidationSystem.OffTrackPenaltyLerpSpeed = 3.0f;
			
			AddChild(_trackValidationSystem);
			
			// Verify the node was added successfully
			if (!GodotObject.IsInstanceValid(_trackValidationSystem) || _trackValidationSystem.GetParent() != this)
			{
				GD.PrintErr("[RacingGame] Failed to add RacingTrackValidationSystem to scene tree");
				_trackValidationSystem = null;
				return;
			}
			
			GD.Print("[RacingGame] RacingTrackValidationSystem initialized successfully");
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[RacingGame] Exception creating RacingTrackValidationSystem: {ex.Message}");
			_trackValidationSystem = null;
		}
	}

	/// <summary>
	/// Setup the camera controller system
	/// </summary>
	private void SetupCameraController()
	{
		try
		{
			_cameraController = new RacingCameraController();
			if (_cameraController == null)
			{
				GD.PrintErr("[RacingGame] Failed to create RacingCameraController instance");
				return;
			}
			
			_cameraController.ScreenEdgeColliderThickness = 8.0f; // Default value
			AddChild(_cameraController);
			
			// Verify the node was added successfully
			if (!GodotObject.IsInstanceValid(_cameraController) || _cameraController.GetParent() != this)
			{
				GD.PrintErr("[RacingGame] Failed to add RacingCameraController to scene tree");
				_cameraController = null;
				return;
			}
			
			_cameraController.Initialize();
			
			// Verify initialization succeeded
			if (_cameraController.TrackCamera == null)
			{
				GD.PrintErr("[RacingGame] RacingCameraController initialization failed - no camera created");
				return;
			}
			
			GD.Print("[RacingGame] RacingCameraController initialized successfully");
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[RacingGame] Exception creating RacingCameraController: {ex.Message}");
			_cameraController = null;
		}
	}

	/// <summary>
	/// Setup the racing car controller system
	/// </summary>
	private void SetupCarController()
	{
		try
		{
			// Validate dependencies first
			if (!GodotObject.IsInstanceValid(_cameraController) || !GodotObject.IsInstanceValid(_trackValidationSystem))
			{
				GD.PrintErr("[RacingGame] Cannot setup car controller - camera or track validation system not ready");
				return;
			}
			
			if (_cameraController.TrackCamera == null)
			{
				GD.PrintErr("[RacingGame] Cannot setup car controller - camera not initialized");
				return;
			}
			
			_carController = new RacingCarController();
			if (_carController == null)
			{
				GD.PrintErr("[RacingGame] Failed to create RacingCarController instance");
				return;
			}
			
			// Set default car settings
			_carController.MaxSpeed = 1800.0f;
			_carController.MinSpeed = 100.0f;
			_carController.MaxInputDistance = 500.0f;
			_carController.AccelerationRate = 300.0f;
			_carController.DecelerationRate = 600.0f;
			_carController.RotationLerpSpeed = 5.0f;
			_carController.CarSize = new Vector2(40, 80);
			
			AddChild(_carController);
			
			// Verify the node was added successfully
			if (!GodotObject.IsInstanceValid(_carController) || _carController.GetParent() != this)
			{
				GD.PrintErr("[RacingGame] Failed to add RacingCarController to scene tree");
				_carController = null;
				return;
			}
			
			// Initialize with validated dependencies
			_carController.Initialize(_cameraController, _trackValidationSystem);
			
			// Verify initialization succeeded
			if (!_carController.IsInitialized)
			{
				GD.PrintErr("[RacingGame] RacingCarController initialization failed");
				return;
			}
			
			// Connect car movement events for visual feedback
			_carController.CarMoved += OnCarMoved;
			
			// Add player to game controller
			var player = _carController.GetPlayer();
			if (player != null)
			{
				AddPlayer(player);
			}
			else
			{
				GD.PrintErr("[RacingGame] Failed to get player from car controller");
				return;
			}
			
			// Initialize visual feedback renderer
			SetupVisualRenderer();
			
			GD.Print("[RacingGame] RacingCarController initialized successfully");
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[RacingGame] Exception creating RacingCarController: {ex.Message}");
			_carController = null;
		}
	}

	/// <summary>
	/// Setup visual feedback renderer
	/// </summary>
	private void SetupVisualRenderer()
	{
		try
		{
			// Validate car controller dependency
			if (!GodotObject.IsInstanceValid(_carController) || !_carController.IsInitialized)
			{
				GD.PrintErr("[RacingGame] Cannot setup visual renderer - car controller not ready");
				return;
			}
			
			_visualRenderer = new RacingVisualFeedbackRenderer();
			if (_visualRenderer == null)
			{
				GD.PrintErr("[RacingGame] Failed to create RacingVisualFeedbackRenderer instance");
				return;
			}
			
			_visualRenderer.Initialize(_carController);
			AddChild(_visualRenderer);
			
			// Verify the node was added successfully
			if (!GodotObject.IsInstanceValid(_visualRenderer) || _visualRenderer.GetParent() != this)
			{
				GD.PrintErr("[RacingGame] Failed to add RacingVisualFeedbackRenderer to scene tree");
				_visualRenderer = null;
				return;
			}
			
			GD.Print("[RacingGame] RacingVisualFeedbackRenderer initialized successfully");
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[RacingGame] Exception creating RacingVisualFeedbackRenderer: {ex.Message}");
			_visualRenderer = null;
		}
	}

	/// <summary>
	/// Override SetGameMode to update timing system
	/// </summary>
	public new void SetGameMode(GameMode mode)
	{
		base.SetGameMode(mode);
		if (_timingSystem != null)
		{
			_timingSystem.SetGameMode(mode);
		}
	}

	// ================================================================
	// DIRECT INPUT HANDLING (Arc Positioning Fix)
	// ================================================================
	
	/// <summary>
	/// Handle direct input polling for accurate arc positioning
	/// </summary>
	private void HandleDirectInput()
	{
		if (!_isGameActive || _isGamePaused) 
		{
			_hasDirectInput = false;
			return;
		}
		
		var inputManager = InputManager.GetAutoload();
		if (inputManager == null) 
		{
			_hasDirectInput = false;
			return;
		}
		
		// Get direct input state from InputManager
		_hasDirectInput = inputManager.IsTouchActive();
		
		if (_hasDirectInput)
		{
			// Get raw screen position from InputManager - direct 1:1 mapping like original
			_directTargetPosition = inputManager.GetTouchPosition();
			
			// Debug output for coordinate validation (remove in production)
			// GD.Print($"[RacingGame] Direct Input - Screen = World: {_directTargetPosition}");
		}
	}

	// ================================================================
	// CORE GAME LOOP
	// ================================================================

	public override void _Process(double delta)
	{
		base._Process(delta);
		
		// Handle direct input for arc positioning fix
		HandleDirectInput();
		
		if (GodotObject.IsInstanceValid(_timingSystem))
		{
			if (_timingSystem.IsInCountdown)
			{
				HandleCountdown((float)delta);
			}
			else if (_isGameActive && !_isGamePaused)
			{
				_timingSystem.UpdateRacingTimers((float)delta, _players);
			}
		}
		
		UpdateUI();
	}

	protected override void UpdateGame(float delta)
	{
		base.UpdateGame(delta);
		
		if (GodotObject.IsInstanceValid(_carController) && _carController.IsInitialized)
		{
			// Update track validation and penalties
			if (GodotObject.IsInstanceValid(_trackValidationSystem))
			{
				_trackValidationSystem.UpdateOffTrackPenalties(_carController.GetCarPosition(), delta);
			}
			
			// Update visual feedback renderer
			if (GodotObject.IsInstanceValid(_visualRenderer))
			{
				_visualRenderer.UpdateVisualFeedback(delta);
				_visualRenderer.UpdateTireTrails();
			}
		}
	}

	public override void _Draw()
	{
		// Visual feedback is now handled by VisualFeedbackRenderer
		// Update renderer visibility based on game state
		if (GodotObject.IsInstanceValid(_visualRenderer))
		{
			_visualRenderer.ShouldRender = IsGameActive() && !IsGamePaused() && 
				GodotObject.IsInstanceValid(_carController) && _carController.IsInitialized;
		}
	}

	// ================================================================
	// RACING MECHANICS - GAME MODES
	// ================================================================

	/// <summary>
	/// Start a time trial race with countdown
	/// </summary>
	public virtual async void StartTimeTrial()
	{
		if (_isGameActive) return;
		
		// Check if credits are required and handle credit spending
		if (TimeTrialCreditCost > 0)
		{
			var creditManager = CreditManager.GetInstance();
			if (creditManager != null && GodotObject.IsInstanceValid(creditManager))
			{
				// Reset idle timer when starting a premium feature
				var userManager = UserManager.GetAutoload();
				if (userManager != null && GodotObject.IsInstanceValid(userManager))
				{
					userManager.ResetUserIdleTimer();
				}
				
				bool creditsSpent = await creditManager.CheckAndSpendCredits(TimeTrialCreditCost, "Time Trial Race");
				if (!creditsSpent)
				{
					// Credits not spent - don't start the race
					GD.Print("Time trial cancelled - credits not spent");
					return;
				}
			}
		}
		
		SetGameMode(GameMode.TimeTrial);
		ResetRacingData();
		
		if (ShowCountdown)
		{
			StartCountdown();
		}
		else
		{
			StartGame();
		}
	}

	/// <summary>
	/// Start practice mode (no countdown, continuous play)
	/// </summary>
	public virtual void StartPractice()
	{
		SetGameMode(GameMode.Practice);
		ResetRacingData();
		StartGame();
		
		// In practice mode, immediately start first "lap" for timing
		foreach (var player in _players)
		{
			StartPlayerLap(player.PlayerId);
		}
	}

	// ================================================================
	// RACING MECHANICS - COUNTDOWN SYSTEM
	// ================================================================

	/// <summary>
	/// Start countdown sequence for time trials
	/// </summary>
	protected virtual void StartCountdown()
	{
		if (GodotObject.IsInstanceValid(_timingSystem))
		{
			_timingSystem.StartCountdown(CountdownDuration);
			OnCountdownStarted();
		}
		else
		{
			GD.PrintErr("[RacingGame] Cannot start countdown - timing system not initialized");
		}
	}

	/// <summary>
	/// Handle countdown timer and progression
	/// </summary>
	protected virtual void HandleCountdown(float delta)
	{
		if (_timingSystem == null)
		{
			GD.PrintErr("[RacingGame] Cannot handle countdown - timing system not initialized");
			return;
		}
		
		bool countdownCompleted = _timingSystem.UpdateCountdown(delta, CountdownDuration);
		
		// Check if countdown number changed
		int countdownNumber = _timingSystem.CountdownNumber;
		if (countdownNumber > 0)
		{
			OnCountdownNumber(countdownNumber);
		}
		else if (!countdownCompleted)
		{
			OnCountdownGo();
		}

		// Check if countdown completed
		if (countdownCompleted)
		{
			EndCountdown();
		}
	}

	/// <summary>
	/// End countdown and start the actual race
	/// </summary>
	protected virtual void EndCountdown()
	{
		if (GodotObject.IsInstanceValid(_timingSystem))
		{
			_timingSystem.EndCountdown();
			_timingSystem.StartRacing();
			StartGame();
			OnCountdownEnded();
			
			// Start first lap for all players in time trial mode
			foreach (var player in _players)
			{
				_timingSystem.StartPlayerLap(player.PlayerId);
			}
		}
		else
		{
			GD.PrintErr("[RacingGame] Cannot end countdown - timing system not initialized");
		}
	}

	// Countdown event handlers - override in RacingGame for custom display
	protected virtual void OnCountdownStarted()
	{
		_uiManager?.SetCountdownOverlayVisible(true);
	}

	protected virtual void OnCountdownNumber(int number)
	{
		_uiManager?.UpdateCountdownText(number.ToString());
	}

	protected virtual void OnCountdownGo()
	{
		_uiManager?.UpdateCountdownText("GO!");
	}

	protected virtual void OnCountdownEnded()
	{
		_uiManager?.SetCountdownOverlayVisible(false);
	}

	// ================================================================
	// RACING MECHANICS - LAP MANAGEMENT
	// ================================================================

	/// <summary>
	/// Start a new lap for a player - delegates to timing system
	/// </summary>
	public virtual void StartPlayerLap(string playerId)
	{
		if (GodotObject.IsInstanceValid(_timingSystem))
		{
			_timingSystem.StartPlayerLap(playerId);
		}
		else
		{
			GD.PrintErr("[RacingGame] Cannot start player lap - timing system not initialized");
		}
	}

	/// <summary>
	/// Complete a lap for a player - delegates to timing system
	/// </summary>
	public virtual void CompletePlayerLap(string playerId, float lapTime)
	{
		if (GodotObject.IsInstanceValid(_timingSystem))
		{
			_timingSystem.CompletePlayerLap(playerId, lapTime);
			
			// Check if race is complete and start next lap for practice mode
			if (_currentGameMode == GameMode.Practice || _timingSystem.GetPlayerCurrentLap(playerId) < TargetLaps)
			{
				// Start next lap (timing system will handle the logic)
				_timingSystem.StartPlayerLap(playerId);
			}
		}
		else
		{
			GD.PrintErr("[RacingGame] Cannot complete player lap - timing system not initialized");
		}
	}

	/// <summary>
	/// Complete the race for a player - delegates to timing system
	/// </summary>
	protected virtual void CompletePlayerRace(string playerId)
	{
		if (GodotObject.IsInstanceValid(_timingSystem))
		{
			_timingSystem.CompletePlayerRace(playerId, _players);
			
			// Check if race state changed to finished
			if (_timingSystem.CurrentRacingState == RacingTimingSystem.RacingState.Finished)
			{
				EndGame();
			}
		}
		else
		{
			GD.PrintErr("[RacingGame] Cannot complete player race - timing system not initialized");
		}
	}

	/// <summary>
	/// Player crossed a checkpoint - delegates to timing system
	/// </summary>
	public virtual void OnPlayerCheckpointCrossed(string playerId, int checkpointIndex)
	{
		if (GodotObject.IsInstanceValid(_timingSystem))
		{
			_timingSystem.OnPlayerCheckpointCrossed(playerId, checkpointIndex);
		}
		else
		{
			GD.PrintErr("[RacingGame] Cannot process checkpoint crossing - timing system not initialized");
		}
	}

	/// <summary>
	/// Reset all racing-specific data - delegates to timing system
	/// </summary>
	protected virtual void ResetRacingData()
	{
		if (GodotObject.IsInstanceValid(_timingSystem))
		{
			_timingSystem.ResetRacingData();
		}
		else
		{
			GD.PrintErr("[RacingGame] Cannot reset racing data - timing system not initialized");
		}
	}

	// ================================================================
	// CAR MANAGEMENT
	// ================================================================



	/// <summary>
	/// Handle car movement events for visual feedback
	/// </summary>
	private void OnCarMoved(Vector2 position, Vector2 velocity)
	{
		QueueRedraw(); // Trigger visual updates
	}

	// ================================================================
	// TRACK SYSTEM
	// ================================================================

	/// <summary>
	/// Initialize track system
	/// </summary>
	private void InitializeTrackSystem()
	{
		if (TrackScenes != null && TrackScenes.Count > 0)
		{
			_currentTrackIndex = 0;
			LoadTrack(0);
		}
		else
		{
			// Look for existing TrackDefinition child
			foreach (Node child in GetChildren())
			{
				if (child is RacingTrackDefinition trackDef)
				{
					_trackDefinition = trackDef;
					_currentTrackIndex = 0;
					SetupLoadedTrack();
					return;
				}
			}
			GD.PrintErr("RacingGame: No track scenes configured and no RacingTrackDefinition child node found.");
		}
	}

	/// <summary>
	/// Load a specific track by index
	/// </summary>
	private void LoadTrack(int trackIndex)
	{
		if (TrackScenes == null || trackIndex < 0 || trackIndex >= TrackScenes.Count)
		{
			GD.PrintErr($"Invalid track index: {trackIndex}");
			return;
		}

		// Clear existing track
		if (_trackDefinition != null)
		{
			DisconnectTrackSignals();
			_trackDefinition.QueueFree();
		}

		// Load new track
		var trackScene = TrackScenes[trackIndex];
		var trackInstance = trackScene.Instantiate();
		
		if (trackInstance is RacingTrackDefinition trackDef)
		{
			_trackDefinition = trackDef;
			AddChild(_trackDefinition);
			
			// Position track at screen center like original working implementation
			var viewport = GetViewport();
			if (viewport != null)
			{
				var screenSize = viewport.GetVisibleRect().Size;
				Vector2 screenCenter = screenSize / 2;
				_trackDefinition.GlobalPosition = screenCenter;
				GD.Print($"[RacingGame] Track positioned at screen center (like original): {screenCenter}");
			}
			
			SetupLoadedTrack();
		}
		else
		{
			GD.PrintErr("Track scene does not contain RacingTrackDefinition");
			trackInstance.QueueFree();
		}
	}

	/// <summary>
	/// Setup loaded track
	/// </summary>
	private void SetupLoadedTrack()
	{
		if (_trackDefinition == null) return;

		_trackDefinition.SetupTrack();
		_trackCurve = _trackDefinition.GetTrackCurve();
		
		// Initialize track validation system with track data
		if (_trackValidationSystem != null)
		{
			_trackValidationSystem.Initialize(_trackDefinition, _trackCurve);
		}
		
		// Initialize camera controller with track data
		if (_cameraController != null)
		{
			_cameraController.SetTrackDefinition(_trackDefinition);
			_cameraController.PositionCameraOverTrack();
			_cameraController.SetupScreenEdgeColliders();
		}
		TargetLaps = _trackDefinition.NumberOfLaps;
		InitializeCheckpointTracking();
		ConnectTrackSignals();
		PositionCarAtStart();
	}

	/// <summary>
	/// Position car at track start
	/// </summary>
	private void PositionCarAtStart()
	{
		if (_trackDefinition == null || !(_carController?.IsInitialized == true)) return;

		var startPoint = _trackDefinition.GetStartLinePosition();
		var startLineDirection = _trackDefinition.GetStartLineDirection();
		
		if (startPoint == Vector2.Zero)
		{
			var viewport = GetViewport();
			if (viewport != null)
			{
				var screenSize = viewport.GetVisibleRect().Size;
				Vector2 screenCenter = screenSize / 2;
				_carController.PositionCar(screenCenter);
			}
			return;
		}
		
		var carPosition = startPoint - startLineDirection * 50;
		var carRotation = startLineDirection.Angle() + Mathf.Pi / 2;
		_carController.PositionCar(carPosition, carRotation);
	}

	// Track utility methods - delegates to track validation system
	public bool IsOnTrack(Vector2 position)
	{
		return _trackValidationSystem?.IsOnTrack(position) ?? true;
	}

	public float GetDistanceToTrackCenterLine(Vector2 position)
	{
		return _trackValidationSystem?.GetDistanceToTrackCenterLine(position) ?? float.MaxValue;
	}



	// ================================================================
	// CHECKPOINT AND TRIGGER MANAGEMENT
	// ================================================================

	/// <summary>
	/// Initialize checkpoint tracking
	/// </summary>
	private void InitializeCheckpointTracking()
	{
		if (_trackDefinition == null) return;

		_checkpointTriggers = _trackDefinition.CheckpointTriggers;
		if (_checkpointTriggers.Length == 0) return;

		_checkpointsCrossed = new bool[_checkpointTriggers.Length];
		_nextCheckpointIndex = 0;
		UpdateCheckpointVisuals();
	}

	/// <summary>
	/// Connect track signals
	/// </summary>
	private void ConnectTrackSignals()
	{
		if (_trackDefinition == null) return;

		var startLineTrigger = _trackDefinition.StartLine;
		if (startLineTrigger != null)
		{
			startLineTrigger.StartLineCrossed += OnStartLineTriggered;
		}

		var finishLineTrigger = _trackDefinition.FinishLine;
		if (finishLineTrigger != null && finishLineTrigger != startLineTrigger)
		{
			finishLineTrigger.StartLineCrossed += OnFinishLineTriggered;
		}

		for (int i = 0; i < _checkpointTriggers.Length; i++)
		{
			var trigger = _checkpointTriggers[i];
			if (trigger != null)
			{
				trigger.CheckpointCrossed += OnCheckpointTriggered;
			}
		}
	}

	/// <summary>
	/// Disconnect track signals
	/// </summary>
	private void DisconnectTrackSignals()
	{
		if (_trackDefinition == null || !GodotObject.IsInstanceValid(_trackDefinition)) return;

		var startLineTrigger = _trackDefinition.StartLine;
		if (startLineTrigger != null && GodotObject.IsInstanceValid(startLineTrigger))
		{
			startLineTrigger.StartLineCrossed -= OnStartLineTriggered;
		}

		var finishLineTrigger = _trackDefinition.FinishLine;
		if (finishLineTrigger != null && GodotObject.IsInstanceValid(finishLineTrigger) && finishLineTrigger != startLineTrigger)
		{
			finishLineTrigger.StartLineCrossed -= OnFinishLineTriggered;
		}

		if (_checkpointTriggers != null)
		{
			for (int i = 0; i < _checkpointTriggers.Length; i++)
			{
				var trigger = _checkpointTriggers[i];
				if (trigger != null && GodotObject.IsInstanceValid(trigger))
				{
					trigger.CheckpointCrossed -= OnCheckpointTriggered;
				}
			}
		}
	}

	// Track signal handlers
	private void OnStartLineTriggered(Node2D body)
	{
		if (!GodotObject.IsInstanceValid(_carController) || !_carController.IsInitialized) return;
		if (body != _carController.GetCarBody()) return;
		
		var player = _carController.GetPlayer();
		if (player == null) return;
		var playerId = player.PlayerId ?? "player1";
		
		if (GetGameMode() == GameMode.TimeTrial && GetPlayerCurrentLap(playerId) > 0)
		{
			// Check if this should be treated as finish line crossing
			bool shouldTreatAsFinishLine = (_checkpointTriggers.Length == 0) ? 
				(GetPlayerCurrentLap(playerId) > 1) : 
				(_nextCheckpointIndex >= _checkpointTriggers.Length);
			
			if (shouldTreatAsFinishLine)
			{
				OnFinishLineTriggered(body);
				return;
			}
		}
		
		// Reset gap timer in practice mode
		if (GetGameMode() == GameMode.Practice)
		{
			OnPlayerCheckpointCrossed(playerId, -1); // -1 indicates start line
		}
	}

	private void OnFinishLineTriggered(Node2D body)
	{
		if (!GodotObject.IsInstanceValid(_carController) || !_carController.IsInitialized) return;
		if (body != _carController.GetCarBody()) return;
		
		var player = _carController.GetPlayer();
		if (player == null) return;
		var playerId = player.PlayerId ?? "player1";
		
		if (GetGameMode() == GameMode.TimeTrial)
		{
			// Complete the lap
			float lapTime = GetPlayerCurrentLapTime(playerId);
			CompletePlayerLap(playerId, lapTime);
			ResetCheckpoints();
		}
		else
		{
			// Practice mode - just reset gap timer
			OnPlayerCheckpointCrossed(playerId, -1);
		}
	}

	private void OnCheckpointTriggered(Node2D body, int checkpointIndex)
	{
		if (!GodotObject.IsInstanceValid(_carController) || !_carController.IsInitialized) return;
		if (body != _carController.GetCarBody()) return;
		
		var player = _carController.GetPlayer();
		if (player == null) return;
		var playerId = player.PlayerId ?? "player1";
		
		if (GetGameMode() == GameMode.TimeTrial)
		{
			if (checkpointIndex == _nextCheckpointIndex)
			{
				_checkpointsCrossed[checkpointIndex] = true;
				_nextCheckpointIndex++;
				
				if (_checkpointTriggers[checkpointIndex] != null)
				{
					_checkpointTriggers[checkpointIndex].MarkAsCrossed();
				}
				
				UpdateCheckpointVisuals();
			}
		}
		
		OnPlayerCheckpointCrossed(playerId, checkpointIndex);
	}

	private void ResetCheckpoints()
	{
		if (_checkpointsCrossed == null) return;
		
		for (int i = 0; i < _checkpointsCrossed.Length; i++)
		{
			_checkpointsCrossed[i] = false;
		}
		_nextCheckpointIndex = 0;
		
		_trackDefinition?.ResetAllCheckpoints();
		_trackDefinition?.ResetStartLineColor();
		UpdateCheckpointVisuals();
	}

	private void UpdateCheckpointVisuals()
	{
		if (_checkpointTriggers == null || _checkpointTriggers.Length == 0) return;

		for (int i = 0; i < _checkpointTriggers.Length; i++)
		{
			var checkpoint = _checkpointTriggers[i];
			if (checkpoint == null) continue;

			if (i < _nextCheckpointIndex)
			{
				checkpoint.MarkAsCrossed();
			}
			else if (i == _nextCheckpointIndex && GetGameMode() == GameMode.TimeTrial)
			{
				checkpoint.SetNextRequiredState();
			}
			else
			{
				checkpoint.SetFutureState();
			}
		}
	}


	// ================================================================
	// UI SYSTEM
	// ================================================================

	private void SetupUI()
	{
		_uiManager = new RacingUIManager();
		var trackScenesList = TrackScenes?.ToList();
		_uiManager.Initialize(trackScenesList, _currentTrackIndex, TimeTrialCreditCost);
		AddChild(_uiManager);

		// Connect UI manager signals
		_uiManager.TimeTrialRequested += () => { if (!IsGameActive()) StartTimeTrial(); };
		_uiManager.RestartRequested += () => { EndGame(); StartPractice(); };
		_uiManager.TrackSwitchRequested += OnTrackSwitchRequested;
		_uiManager.ResumeRequested += ResumeGame;
		_uiManager.MainMenuRequested += HandleMainMenuRequest;
		_uiManager.RaceAgainRequested += () => { EndGame(); StartTimeTrial(); };
		_uiManager.PracticeModeRequested += () => { EndGame(); StartPractice(); };
	}

	/// <summary>
	/// Handle track switch request from UI manager
	/// </summary>
	/// <param name="trackIndex">Index of track to switch to</param>
	private void OnTrackSwitchRequested(int trackIndex)
	{
		if (IsGameActive() && GetGameMode() == GameController.GameMode.TimeTrial) 
		{
			return; // Don't allow track switching during time trial
		}
		
		_currentTrackIndex = trackIndex;
		LoadTrack(trackIndex);
		_uiManager?.SetCurrentTrackIndex(trackIndex);
		
		// Restart practice mode with new track
		EndGame();
		StartPractice();
	}

	/// <summary>
	/// Handle main menu request from UI manager
	/// </summary>
	private void HandleMainMenuRequest()
	{
		// Return to main menu via GameHost
		var gameHost = GameHost.GetInstance();
		if (gameHost != null && GodotObject.IsInstanceValid(gameHost))
		{
			gameHost.ReturnToMainMenu();
		}
		else
		{
			// Fallback: just end the game
			EndGame();
		}
	}






	private void UpdateUI()
	{
		if (!GodotObject.IsInstanceValid(_carController) || !_carController.IsInitialized || 
			!GodotObject.IsInstanceValid(_uiManager)) return;

		var player = _carController.GetPlayer();
		if (player == null) return;
		
		var playerId = player.PlayerId ?? "player1";
		var gameMode = GetGameMode();
		
		// Prepare data for UI manager
		float carSpeed = _carController.GetCarSpeed();
		int currentLap = GetPlayerCurrentLap(playerId);
		int targetLaps = TargetLaps;
		
		// Determine time display based on game mode
		float timeDisplay;
		string timeLabel;
		if (gameMode == GameController.GameMode.Practice)
		{
			timeDisplay = GetPlayerGapTime(playerId);
			timeLabel = "Gap";
		}
		else
		{
			timeDisplay = GetPlayerCurrentLapTime(playerId);
			timeLabel = "Time";
		}

		// Update status labels
		_uiManager.UpdateStatusLabels(carSpeed, gameMode, currentLap, targetLaps, timeDisplay, timeLabel);

		// Update button states
		bool isGameActive = IsGameActive();
		bool isInCountdown = IsInCountdown();
		bool isTimeTrial = gameMode == GameController.GameMode.TimeTrial;
		_uiManager.UpdateButtonStates(isGameActive, isInCountdown, isTimeTrial);

		// Update pause overlay
		_uiManager.SetPauseOverlayVisible(IsGamePaused());
	}

	/// <summary>
	/// Override game end to show completion UI
	/// </summary>
	public override void EndGame()
	{
		base.EndGame();
		
		if (GetGameMode() == GameController.GameMode.TimeTrial && 
			GodotObject.IsInstanceValid(_carController) && _carController.IsInitialized &&
			GodotObject.IsInstanceValid(_uiManager))
		{
			var player = _carController.GetPlayer();
			if (player != null)
			{
				var playerId = player.PlayerId ?? "player1";
				var playerScore = GetPlayerScore(playerId);
				_uiManager.SetGameOverOverlayVisible(true, playerScore);
			}
		}
	}


	// ================================================================
	// CONTEXT DETECTION AND INTEGRATION
	// ================================================================

	private void DetectAndAdaptToContext()
	{
		GD.Print("[RacingGame] DetectAndAdaptToContext() called");
		var gameHost = GameHost.GetInstance();
		GD.Print($"[RacingGame] GameHost instance: {gameHost != null}");
		
		if (gameHost != null && GodotObject.IsInstanceValid(gameHost))
		{
			// Production context
			var playerSession = gameHost.GetPlayerSession("default");
			if (playerSession != null && GodotObject.IsInstanceValid(_carController) && _carController.IsInitialized)
			{
				var racingCar = _carController.RacingCar;
				if (racingCar != null)
				{
					racingCar.PlayerId = playerSession.PlayerId;
					_carController.SetUserData(playerSession.UserData);
				}
			}
		}
		else
		{
			// Development context - auto-start practice mode
			if (GodotObject.IsInstanceValid(_carController) && _carController.IsInitialized)
			{
				var racingCar = _carController.RacingCar;
				if (racingCar != null)
				{
					racingCar.PlayerId = "dev_player";
				}
			}
		}
	}

	// ================================================================
	// RACING DATA GETTERS
	// ================================================================

	public int GetPlayerCurrentLap(string playerId) => GodotObject.IsInstanceValid(_timingSystem) ? _timingSystem.GetPlayerCurrentLap(playerId) : 0;
	public float GetPlayerCurrentLapTime(string playerId) => GodotObject.IsInstanceValid(_timingSystem) ? _timingSystem.GetPlayerCurrentLapTime(playerId) : 0.0f;
	public float GetPlayerBestLapTime(string playerId) => GodotObject.IsInstanceValid(_timingSystem) ? _timingSystem.GetPlayerBestLapTime(playerId) : 0.0f;
	public float GetPlayerGapTime(string playerId) => GodotObject.IsInstanceValid(_timingSystem) ? _timingSystem.GetPlayerGapTime(playerId) : 0.0f;
	public List<float> GetPlayerLapTimes(string playerId) => GodotObject.IsInstanceValid(_timingSystem) ? _timingSystem.GetPlayerLapTimes(playerId) : new List<float>();
	public RacingTimingSystem.RacingState GetRacingState() => GodotObject.IsInstanceValid(_timingSystem) ? _timingSystem.CurrentRacingState : RacingTimingSystem.RacingState.Stopped;
	public bool IsInCountdown() => GodotObject.IsInstanceValid(_timingSystem) && _timingSystem.IsInCountdown;
	public int GetCountdownNumber() => GodotObject.IsInstanceValid(_timingSystem) ? _timingSystem.CountdownNumber : 0;
	
	// ================================================================
	// UI INTEGRATION OVERRIDES
	// ================================================================

	/// <summary>
	/// Override to provide racing-specific context buttons
	/// </summary>
	public override ContextButtonData[] GetContextButtons()
	{
		var buttons = new List<ContextButtonData>();

		// Standard "Return to Menu" button
		buttons.Add(GameContextButton.CreateReturnToMenuButton(() => {
			var userManager = UserManager.GetAutoload();
			if (userManager != null && GodotObject.IsInstanceValid(userManager))
			{
				userManager.ResetUserIdleTimer();
			}
			ReturnToMainMenu();
		}));

		// Racing-specific pause/resume button
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

		// Racing-specific restart button (only in practice mode)
		if (GetGameMode() == GameMode.Practice && !IsInCountdown())
		{
			buttons.Add(new ContextButtonData("Restart", () => {
				var userManager = UserManager.GetAutoload();
				if (userManager != null && GodotObject.IsInstanceValid(userManager))
				{
					userManager.ResetUserIdleTimer();
				}
				EndGame();
				StartPractice();
			}, "🔄", true, "Restart practice session"));
		}

		return buttons.ToArray();
	}

	/// <summary>
	/// Override to provide racing game title
	/// </summary>
	public override string GetGameTitle()
	{
		if (GetGameMode() == GameMode.Practice)
		{
			return "Racing Game - Practice";  
		}
		else if (GetGameMode() == GameMode.TimeTrial)
		{
			return "Racing Game - Time Trial";
		}
		return "Racing Game";
	}

	/// <summary>
	/// Handle viewport size changes (e.g., window resize, orientation change)
	/// </summary>
	public override void _Notification(int what)
	{
		base._Notification(what);
		
		if (what == NotificationWMSizeChanged)
		{
			// Recalculate camera positioning and zoom when viewport changes
			if (_cameraController != null)
			{
				_cameraController.OnViewportSizeChanged();
			}
		}
		else if (what == NotificationExitTree)
		{
			// Clean up signals and references
			DisconnectTrackSignals();
			
			if (_carController != null && GodotObject.IsInstanceValid(_carController))
			{
				_carController.CarMoved -= OnCarMoved;
			}
		}
	}
}
}