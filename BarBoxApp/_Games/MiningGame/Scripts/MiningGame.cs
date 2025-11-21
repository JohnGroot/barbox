using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BarBox.Games.MiningGame.Logic;
using Godot;

namespace BarBox.Games.MiningGame;

[GlobalClass]
public partial class MiningGame : GameController
{
	// ================================================================
	// CONSTANTS
	// ================================================================

	private const int CREDITS_PER_PURCHASE = 1;
	private const string MAIN_SCENE_PATH = "res://_Core/Scenes/Main.tscn";

	private static class ErrorMessages
	{
		public const string BACKEND_UNAVAILABLE_TITLE = "Service Unavailable";
		public const string BACKEND_UNAVAILABLE_MESSAGE = "Unable to connect to game server.\nPlease try again later or contact support if the issue persists.";
	}

	// ================================================================
	// SIGNALS - External integration only
	// ================================================================

	// Domain-specific lifecycle signals
	[Signal] public delegate void MiningSessionStartedEventHandler();
	[Signal] public delegate void MiningSessionEndedEventHandler();

	// Game event signals
	[Signal] public delegate void GemsExtractedEventHandler(int amount, GemType gemType);
	[Signal] public delegate void CreditPurchasedEventHandler();
	[Signal] public delegate void UpgradePurchasedEventHandler(UpgradeType upgradeType, int newLevel);
		
	// ================================================================
	// EXPORT PROPERTIES
	// ================================================================
		
	[ExportCategory("Game Settings")]
	[Export] public MiningGameConfig Config { get; set; }
	[Export] public NodePath UIPath { get; set; }
	[Export] public bool EnableDebugMode { get; set; } = false;
	[Export] public bool AutoStartInEditor { get; set; } = true;

	/// <summary>
	/// Allow players to logout during active mining sessions
	/// </summary>
	public override bool CanLogout => true;

	[ExportCategory("Location Data")]
	[Export] public Godot.Collections.Array<MiningLocationData> LocationDataResources { get; set; } = new();
		
	// ================================================================
	// PRIVATE FIELDS
	// ================================================================

	private MiningGameUI _ui;
	private MiningEngine _engine;
	private MiningState _state;
	private EventService _eventService;
	private Guid _activitySessionId;
	private MiningEventService _miningEventService;

	// Platform services (Note: _gameHost inherited from GameController)
	private SessionManager _sessionManager;
	private LocationManager _locationManager;

	// Race condition prevention
	private bool _isProcessingUserChange = false;

	// Location data management
	private Dictionary<string, MiningLocationData> _locationDataRegistry = new();
		
	// ================================================================
	// INITIALIZATION
	// ================================================================

	/// <summary>
	/// PHASE 1: Service Discovery
	/// Discovers platform services and initializes event services
	/// POST-CONDITION GUARANTEES:
	/// - _eventService exists and is valid (REQUIRED)
	/// - _miningEventService exists (REQUIRED)
	/// - _userManager, _locationManager, _gameHost exist in production (REQUIRED)
	/// </summary>
	protected override void DiscoverServices()
	{
		base.DiscoverServices();

		// Initialize REQUIRED event services
		_eventService = EventService.GetInstance();
		if (_eventService == null)
		{
			throw new InvalidOperationException("EventService is required but not available");
		}

		_miningEventService = new MiningEventService(_eventService);

		// Discover platform services
		_gameHost = GameHost.GetInstance();
		_sessionManager = SessionManager.GetInstance();
		_locationManager = LocationManager.GetAutoload();

		// Context detection and validation
		bool isProductionContext = GameHost.IsProductionContext();
		if (isProductionContext)
		{
			// Production: Validate all required services exist
			if (_sessionManager == null)
			{
				throw new InvalidOperationException("SessionManager is required in production but not available");
			}

			if (_locationManager == null)
			{
				throw new InvalidOperationException("LocationManager is required in production but not available");
			}

			if (_gameHost == null)
			{
				throw new InvalidOperationException("GameHost is required in production but not available");
			}
		}
		else
		{
			// Development: Enable debug mode explicitly
			EnableDebugMode = true;
			GD.Print("[MiningGame] Development context detected - debug mode enabled");
		}

		// Check if loaded as direct scene - ensure MainController exists
		bool isDirectSceneLoad = GetTree().CurrentScene == this;
		if (isDirectSceneLoad)
		{
			EnsureMainControllerExists();
		}
	}

	private void EnsureMainControllerExists()
	{
		if (GetTree().Root.GetNodeOrNull<MainController>("MainController") != null)
			return;

		var mainScene = GD.Load<PackedScene>(MAIN_SCENE_PATH);
		if (mainScene != null)
		{
			var mainInstance = mainScene.Instantiate();
			GetTree().Root.CallDeferred(Node.MethodName.AddChild, mainInstance);

			if (mainInstance is CanvasItem canvasItem)
				canvasItem.Visible = false;
		}
	}
		
	private void InitializeLocationDataRegistry()
	{
		_locationDataRegistry.Clear();

		if (LocationDataResources is { Count: > 0 })
		{
			foreach (var locationData in LocationDataResources)
			{
				if (locationData != null && !string.IsNullOrEmpty(locationData.LocationId))
				{
					_locationDataRegistry[locationData.LocationId] = locationData;
				}
			}
		}
		else
		{
			GD.PrintErr("[MiningGame] ERROR: No LocationDataResources configured! Please assign location data in the editor.");
		}
	}
		
	/// <summary>
	/// PHASE 2: Component Initialization
	/// Creates game components (_engine, _state, _ui)
	/// POST-CONDITION GUARANTEES:
	/// - _engine exists and is valid
	/// - _state exists and is valid
	/// - _ui exists and is valid
	/// - _locationDataRegistry contains at least one location
	/// - Config is not null
	/// </summary>
	protected override void InitializeComponents()
	{
		base.InitializeComponents();

		InitializeLocationDataRegistry();
		if (_locationDataRegistry.Count == 0)
		{
			throw new InvalidOperationException(
				"No LocationDataResources configured. Game cannot function without location data.");
		}

		if (Config == null)
		{
			if (_locationManager == null)
				throw new InvalidOperationException("LocationManager is required but not available");

			var locationId = _locationManager.VenueName;
			if (string.IsNullOrEmpty(locationId))
				throw new InvalidOperationException("BARBOX_VENUE_NAME environment variable is required but not set");

			// GetLocationData is deprecated - use location data registry instead
			var locationTemplate = GetLocationDataTemplate(locationId);
			if (locationTemplate == null)
			{
				var availableLocations = string.Join(", ", _locationDataRegistry.Keys);
				throw new InvalidOperationException(
					$"Location data for '{locationId}' not found in LocationDataRegistry. " +
					$"Available locations: {availableLocations}. " +
					$"Either set BARBOX_VENUE_NAME to an available location or add '{locationId}' to the editor's LocationDataResources.");
			}

			// Create config based on location template
			Config = new MiningGameConfig();
			GD.Print($"[MiningGame] Loaded location template: {locationId}");
		}

		_engine = new MiningEngine(this);
		_state = new MiningState(this);
		_state.Initialize();

		AddChild(_engine);
		AddChild(_state);

		var uiNode = GetNode<MiningGameUI>(UIPath);
		_ui = uiNode ?? throw new InvalidOperationException(
			$"UI node is required but not found at path: {UIPath}");
		_ui.Initialize(this, Config);
	}
		
	// ================================================================
	// DOMAIN-SPECIFIC STATE - Mining Game checks player login status
	// ================================================================

	/// <summary>
	/// MiningGame uses login status as its primary state check
	/// </summary>
	public bool IsPlayerLoggedIn()
	{
		return !string.IsNullOrEmpty(GetCurrentUserPhoneNumber());
	}

	// ================================================================
	// DOMAIN-SPECIFIC LIFECYCLE - Mining session management
	// ================================================================

	public void StartMiningSession()
	{
		if (!IsPlayerLoggedIn())
		{
			GD.PrintErr("[MiningGame] Cannot start mining session - no player logged in");
			return;
		}

		InitializeGameSession();

		EmitSignal(SignalName.MiningSessionStarted);
		GD.Print("[MiningGame] Mining session started");
	}

	public void EndMiningSession()
	{
		if (IsInstanceValid(_engine))
			_engine.StopMining();

		if (IsInstanceValid(_ui))
			_ui.SetEnabled(false);

		// Notify platform that game session ended
		_gameHost?.NotifyGameEnded();

		EmitSignal(SignalName.MiningSessionEnded);
		GD.Print("[MiningGame] Mining session ended");
	}

	// ================================================================
	// GAME SESSION INITIALIZATION
	// ================================================================

	private async void InitializeGameSession()
	{
		try
		{
			if (_eventService != null && !string.IsNullOrEmpty(GetCurrentUserPhoneNumber()))
			{
				var locationManager = _locationManager;
				if (locationManager != null && IsInstanceValid(locationManager))
				{
					var boxId = locationManager.BoxId;
					var phoneNumber = GetCurrentUserPhoneNumber();
					if (!string.IsNullOrEmpty(phoneNumber))
					{
						var sessionManager = SessionManager.GetInstance();
						var currentSession = sessionManager?.GetPrimaryUserSession();

						if (currentSession?.PlayerId != null && currentSession.PlayerId != Guid.Empty)
						{
							var playerId = currentSession.PlayerId;

							GD.Print($"[MiningGame] Creating backend session for player {playerId} at box {boxId}");

							var sessionResult = await _eventService.CreateActivitySessionAsync(
								boxId: boxId,
								playerId: playerId,
								gameTag: "mining",
								playerIds: null
							);
							if (sessionResult.IsFailure(out var error))
							{
								GD.PrintErr($"[MiningGame] WARNING: Failed to create backend session: {error.Message}");
								GD.PrintErr($"[MiningGame] Game will continue but persistence may not work");
							}
							else if (sessionResult.IsSuccess(out var sessionId))
							{
								_activitySessionId = sessionId;
								GD.Print($"[MiningGame] Backend session created successfully: {_activitySessionId}");
							}
						}
						else
						{
							GD.PrintErr("[MiningGame] WARNING: Cannot create backend session - no valid player ID");
						}
					}
				}
			}

			if (!string.IsNullOrEmpty(GetCurrentUserPhoneNumber()))
			{
				await _state.LoadUserDataAsync();

				if (!IsInstanceValid(this) || !IsInstanceValid(_engine) || !IsInstanceValid(_state))
					return;

				if (string.IsNullOrEmpty(GetCurrentUserPhoneNumber()))
				{
					GD.PrintErr("[MiningGame] User logged out during data load");
					return;
				}
			}

			_engine.StartMining();

			if (IsInstanceValid(_ui))
			{
				_ui.RefreshLocationData();

				if (!string.IsNullOrEmpty(GetCurrentUserPhoneNumber()))
				{
					_ui.SetEnabled(true);
					_ui.UpdateAllUI();
				}
				else
				{
					_ui.SetEnabled(false);
				}
			}

			// Notify platform that game session started - only after full initialization
			_gameHost?.NotifyGameStarted();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MiningGame] Error during session initialization: {ex.Message}");

			if (IsInstanceValid(_ui))
			{
				_ui.SetEnabled(false);
				_ui.UpdateAllUI();
			}
		}
	}

	/// <summary>
	/// PHASE 4: Activation Decision
	/// Starts mining session if user is logged in, otherwise waits for login
	/// </summary>
	protected override void ActivateGame()
	{
		base.ActivateGame();

		if (IsPlayerLoggedIn())
		{
			StartMiningSession();
		}
		else
		{
			if (IsInstanceValid(_ui))
			{
				_ui.SetEnabled(false);
			}
		}
	}

	public override void OnGameSetup()
	{
		base.OnGameSetup();

		if (_sessionManager != null)
		{
			_sessionManager.UserLoggedIn += OnUserLoggedIn;
			_sessionManager.UserLoggedOut += OnUserLoggedOut;
		}
	}

	public override void OnGameTeardown()
	{
		base.OnGameTeardown();

		if (_sessionManager != null && IsInstanceValid(_sessionManager))
		{
			_sessionManager.UserLoggedIn -= OnUserLoggedIn;
			_sessionManager.UserLoggedOut -= OnUserLoggedOut;
		}
	}

	protected override HelpContentData GetHelpContent()
	{
		var locationData = GetLocationData();
		var gemTypeName = locationData?.PrimaryGemType.ToString() ?? "Crystal";

		return new HelpContentData("MINING HOW-TO")
			.AddSection("⛏️ WELCOME TO THE MINES ⛏️",
				"• Gems accumulate automatically even while not playing!",
				$"• This location mines {gemTypeName} gems",
				"• Each machine has a capacity limit - extract gems regularly!")

			.AddSection("💎 EXTRACTING GEMS 💎",
				"• Gems are stored locally on each machine until you extract them:",
				"• Extracting clears out space in the mining capacity so you can Mine more Gems",
				"• Extracted Gems can be used at any BarBox location")

			.AddSection("😮‍💨 USING GEMS 🤑",
				"• Trade your extracted gems for credits OR upgrade this machine's mine",
				"• Buy unlimited credits as long as you have enough gems",
				"• Credits cost the same gem type as the machine produces")

			.AddSection("⬆️ THREE UPGRADE TYPES ⬆️",
				"• All upgrades are applied to the current machine you're on. Upgrade the mines at all your favorite spots!",
				"• 💼 Capacity - Increases maximum mined gem storage on this machine",
				"• ⚡ Mining Amount - Increases gems produced per 2-hour cycle",
				"• 🚀 Mining Speed - Decreases time between mining cycles");
	}

	// ================================================================
	// INTERNAL ACCESSORS - Encapsulated access to private fields
	// ================================================================

	internal MiningEventService GetEventService() => _miningEventService;
	internal LocationManager GetLocationManager() => _locationManager;
	internal MiningGameUI GetUI() => _ui;
	internal MiningGameConfig GetConfig() => Config;
	internal SessionManager GetSessionManager() => _sessionManager;
	internal bool IsDebugMode() => EnableDebugMode;
	internal MiningState GetState() => _state;

	// ================================================================
	// PUBLIC API
	// ================================================================

	public bool CanExtractGems() => _state.CanExtractGems();
	public bool CanPurchaseCredit() => _state.CanPurchaseCredit();
	public bool CanPurchaseUpgrade(UpgradeType upgradeType) => _state.CanPurchaseUpgrade(upgradeType);

	public MiningLocationData GetLocationData() => _state.GetLocationData();
	public MiningGlobalDataStore GetGlobalData() => _state.GetGlobalData();

	public float GetMiningProgress() => _engine.GetMiningProgress();
	public float GetTimeUntilNextTick() => _engine.GetTimeUntilNextTick();

	public int GetPendingGems() => _state.PendingGems;
	public int GetMaxCapacity() => _state.GetMaxCapacity();

	public int GetGemsPerTick() => _state.GetGemsPerTick();
	public float GetMiningTickTime() => _state.GetMiningTickTime();
	public int GetUpgradeLevel(UpgradeType upgradeType) => _state.GetUpgradeLevel(upgradeType);
		
	public MiningLocationData GetLocationDataTemplate(string locationId)
	{
		if (string.IsNullOrEmpty(locationId))
			throw new ArgumentException("Location ID cannot be null or empty", nameof(locationId));

		if (!_locationDataRegistry.TryGetValue(locationId, out var template))
		{
			var available = string.Join(", ", _locationDataRegistry.Keys);
			throw new InvalidOperationException(
				$"Location template '{locationId}' not found in registry. " +
				$"Available locations: {available}. " +
				$"Either set BARBOX_VENUE_NAME to an available location or add '{locationId}' to the editor's LocationDataResources.");
		}

		return template;
	}
		
	public void ExtractGems()
	{
		if (!IsInstanceValid(_state))
			return;

		var locationTemplate = _state.GetLocationData();
		if (locationTemplate == null || !CanExtractGems()) return;

		int amount = _state.PendingGems;
		int maxCapacity = _state.GetMaxCapacity();
		bool wasAtCapacity = amount >= maxCapacity;
		GemType gemType = locationTemplate.PrimaryGemType;

		if (_state.ExtractGems())
		{

			if (IsInstanceValid(_ui))
				_ui.UpdateAllUI();
			EmitSignal(SignalName.GemsExtracted, amount, (int)gemType);
			GD.Print($"[MiningGame] Extracted {amount} {gemType} gems");
		}
	}
		
	public void PurchaseCredit()
	{
		if (IsInstanceValid(_state) && _state.PurchaseCredit())
		{
			if (IsInstanceValid(_ui))
				_ui.UpdateAllUI();
			EmitSignal(SignalName.CreditPurchased);
			GD.Print("[MiningGame] Credit purchased");
		}
	}
		
	public void PurchaseUpgrade(UpgradeType upgradeType)
	{
		if (!IsInstanceValid(_state))
			return;

		var currentLevel = _state.GetUpgradeLevel(upgradeType);

		if (_state.PurchaseUpgrade(upgradeType))
		{
			if (IsInstanceValid(_ui))
				_ui.UpdateAllUI();
			EmitSignal(SignalName.UpgradePurchased, (int)upgradeType, currentLevel + 1);
			GD.Print($"[MiningGame] {upgradeType} upgraded to level {currentLevel + 1}");
		}
	}
		
	public void UpdateUI()
	{
		if (IsInstanceValid(_ui))
			_ui.UpdateAllUI();
	}

	private void LogDebug(string message)
	{
		if (EnableDebugMode)
			GD.Print($"[MiningGame] {message}");
	}

	private void LogError(string message)
	{
		GD.PrintErr($"[MiningGame] {message}");
	}

	private void LogAsyncError(string message)
	{
		GD.PrintErr($"[MiningGame] {message}");
	}

	internal string GetCurrentUserPhoneNumber()
	{
		try
		{
			var sessionManager = SessionManager.GetInstance();
			if (sessionManager != null && IsInstanceValid(sessionManager))
			{
				var currentSession = sessionManager.GetPrimaryUserSession();
				if (currentSession != null)
				{
					return currentSession.PhoneNumber;
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MiningGame] Error getting current user phone number: {ex.Message}");
		}

		return null;
	}
		
	// ================================================================
	// EVENT HANDLERS
	// ================================================================
		
	private void OnUserLoggedIn(string phoneNumber)
	{
		if (_isProcessingUserChange)
		{
			GD.PrintErr("[MiningGame] User change already in progress, ignoring login event");
			return;
		}

		_isProcessingUserChange = true;

		try
		{
			if (IsInstanceValid(_state))
				_state.ClearAllState();

			if (IsPlayerLoggedIn())
			{
				EndMiningSession();
			}

			StartMiningSession();

			// Fetch username from session for logging
			var session = _sessionManager?.GetUserSession(phoneNumber);
			var userName = session?.UserName ?? string.Empty;

			GD.Print($"[MiningGame] User logged in: {userName} ({phoneNumber})");
		}
		finally
		{
			_isProcessingUserChange = false;
		}
	}

	private void OnUserLoggedOut(string phoneNumber)
	{
		if (_isProcessingUserChange)
		{
			GD.PrintErr("[MiningGame] User change already in progress, ignoring logout event");
			return;
		}

		_isProcessingUserChange = true;

		try
		{
			EndMiningSession();

			if (IsInstanceValid(_state))
				_state.ClearAllState();

			if (IsInstanceValid(_ui))
			{
				_ui.SetEnabled(false);
				_ui.UpdateAllUI();
			}

			GD.Print("[MiningGame] User logged out and state cleared");
		}
		finally
		{
			_isProcessingUserChange = false;
		}
	}
		
	public override void _Notification(int what)
	{
		if (what == NotificationExitTree)
		{
			// Close activity session if active
			if (_activitySessionId != Guid.Empty && _eventService != null)
			{
				_ = _eventService.CloseActivitySessionAsync(_activitySessionId);
			}

			// Cleanup engine
			if (IsInstanceValid(_engine))
			{
				_engine.StopMining();
			}

			// Cleanup state
			if (IsInstanceValid(_state))
			{
				_state.ClearAllState();
			}

			// Disconnect event handlers
			if (_sessionManager != null && IsInstanceValid(_sessionManager))
			{
				_sessionManager.UserLoggedIn -= OnUserLoggedIn;
				_sessionManager.UserLoggedOut -= OnUserLoggedOut;
			}
		}
	}
}