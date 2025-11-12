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
	private const string DEFAULT_LOCATION_ID = "default";

	private static class ErrorMessages
	{
		public const string BACKEND_UNAVAILABLE_TITLE = "Service Unavailable";
		public const string BACKEND_UNAVAILABLE_MESSAGE = "Unable to connect to game server.\nPlease try again later or contact support if the issue persists.";
	}

	// ================================================================
	// SIGNALS - External integration only
	// ================================================================
		
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
	public override bool AllowLogoutDuringPlay => true;

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

	// Platform services
	private GameHost _gameHost;
	private UserManager _userManager;
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
	/// </summary>
	protected override void DiscoverServices()
	{
		base.DiscoverServices();

		// Initialize event services
		_eventService = EventService.GetInstance();
		_miningEventService = new MiningEventService(_eventService);

		// Service discovery
		_gameHost = GameHost.GetInstance();
		_userManager = UserManager.GetAutoload();
		_locationManager = LocationManager.GetAutoload();

		// Enable debug mode if no GameHost (development context)
		if (_gameHost == null || !IsInstanceValid(_gameHost))
		{
			EnableDebugMode = true;
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
		// Simple check - is MainController already in the scene tree?
		if (GetTree().Root.GetNodeOrNull<MainController>("MainController") != null)
			return; // Already exists

		// Load Main.tscn if missing (only for direct scene load scenario)
		var mainScene = GD.Load<PackedScene>(MAIN_SCENE_PATH);
		if (mainScene != null)
		{
			var mainInstance = mainScene.Instantiate();
			GetTree().Root.CallDeferred(Node.MethodName.AddChild, mainInstance);

			// Hide UI - we only need the logout functionality
			if (mainInstance is CanvasItem canvasItem)
				canvasItem.Visible = false;
		}
	}
		
	private void InitializeLocationDataRegistry()
	{
		_locationDataRegistry.Clear();
			
		// Populate registry from exported resources only
		if (LocationDataResources != null && LocationDataResources.Count > 0)
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
	/// </summary>
	protected override void InitializeComponents()
	{
		base.InitializeComponents();

		// Initialize location data registry first
		InitializeLocationDataRegistry();
			
		// Setup configuration
		if (Config == null)
		{
			Config = new MiningGameConfig();
				
			// Try to load location-specific config
			if (_locationManager != null)
			{
				var locationId = _locationManager.CurrentLocationId ?? DEFAULT_LOCATION_ID;
				var locationConfig = _locationManager.GetLocationData<MiningGameConfig>(locationId);
				if (locationConfig != null)
				{
					Config = locationConfig;
				}
			}
		}
			
		// Initialize game systems
		_engine = new MiningEngine(this);
		_state = new MiningState(this);
			
		// Add systems to scene tree
		AddChild(_engine);
		AddChild(_state);
			
		// Setup UI if present
		var uiNode = GetNode<MiningGameUI>(UIPath);
		if (uiNode is not null)
		{
			_ui = uiNode;
			_ui.Initialize(this, Config);
		}
		else
		{
			GD.PrintErr("[MiningGame] UI node not found or wrong type");
		}
	}
		
	// ================================================================
	// GAME LIFECYCLE
	// ================================================================
		
	private async void InitializeGameSession()
	{
		try
		{
			// Validate essential components first
			if (!IsInstanceValid(_state) || !IsInstanceValid(_engine))
			{
				GD.PrintErr("[MiningGame] Cannot initialize session - missing essential components (state/engine)");
				return;
			}
				
			// Note: If no user is logged in in production, game will run with default state
			// The UI will show appropriate messages and backend persistence will be skipped

			// CREATE BACKEND SESSION - CRITICAL FOR EVENT PERSISTENCE
			if (_eventService != null && !string.IsNullOrEmpty(GetCurrentUserPhoneNumber()))
			{
				var locationManager = _locationManager;
				if (locationManager != null && IsInstanceValid(locationManager))
				{
					var boxId = locationManager.BoxId;
					var phoneNumber = GetCurrentUserPhoneNumber();

					if (!string.IsNullOrEmpty(phoneNumber))
					{
						// Get player ID from session manager
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
								playerIds: null  // Single-player game
							);
							if (!sessionResult.IsSuccess)
							{
								GD.PrintErr($"[MiningGame] WARNING: Failed to create backend session: {sessionResult.Error}");
								GD.PrintErr($"[MiningGame] Game will continue but persistence may not work");
								// Continue anyway - game can work in offline mode
							}
							else
							{
								_activitySessionId = sessionResult.Value;
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

			// Load user data first, then start mining after data is ready
			if (!string.IsNullOrEmpty(GetCurrentUserPhoneNumber()))
			{
				await _state.LoadUserDataAsync();

				// Check if components are still valid after await and user hasn't logged out
				if (!IsInstanceValid(this) || !IsInstanceValid(_engine) || !IsInstanceValid(_state))
					return;

				// Verify user still logged in after async operation
				if (string.IsNullOrEmpty(GetCurrentUserPhoneNumber()))
				{
					GD.PrintErr("[MiningGame] User logged out during data load");
					return;
				}
			}
				
			// Start mining only after user data (including partial progress) is fully loaded
			_engine.StartMining();

			// Enable UI only if user is logged in
			if (IsInstanceValid(_ui))
			{
				_ui.RefreshLocationData(); // Update cached location data after load

				// Only enable UI if we have a valid user session
				if (!string.IsNullOrEmpty(GetCurrentUserPhoneNumber()))
				{
					_ui.SetEnabled(true);
					_ui.UpdateAllUI(); // Force immediate UI refresh
				}
				else
				{
					// No user logged in - keep UI disabled with "login required" state
					_ui.SetEnabled(false);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MiningGame] Error during session initialization: {ex.Message}");
				
			// Ensure UI shows error state
			if (IsInstanceValid(_ui))
			{
				_ui.SetEnabled(false);
				_ui.UpdateAllUI(); // Show error state
			}
		}
	}

	protected override void OnGameStarted()
	{
		base.OnGameStarted();
		InitializeGameSession();
		EmitSignal(SignalName.GameStarted);
	}
		
	protected override void OnGameEnded()
	{
		base.OnGameEnded();
			
		if (IsInstanceValid(_engine))
			_engine.StopMining();
				
		if (IsInstanceValid(_ui))
			_ui.SetEnabled(false);
			
		EmitSignal(SignalName.GameEnded);
		GD.Print("[MiningGame] Game ended");
	}
		
	/// <summary>
	/// PHASE 4: Activation Decision
	/// Game requires a logged-in user to start - UI remains disabled until login
	/// </summary>
	protected override void ActivateGame()
	{
		base.ActivateGame();

		// Only start game if user is logged in
		if (!string.IsNullOrEmpty(GetCurrentUserPhoneNumber()))
		{
			StartGame();
		}
		else
		{
			// No user logged in, keep UI disabled until login
			if (IsInstanceValid(_ui))
			{
				_ui.SetEnabled(false);
			}
		}
	}

	public override void OnUIContextSetup()
	{
		base.OnUIContextSetup();

		// Connect to user changes after UI context is set up
		if (_userManager != null)
		{
			_userManager.UserLoggedIn += OnUserLoggedIn;
			_userManager.UserLoggedOut += OnUserLoggedOut;
		}

	}
		
	public override void OnUIContextTeardown()
	{
		base.OnUIContextTeardown();

		// Disconnect event handlers when UI context is torn down
		if (_userManager != null && IsInstanceValid(_userManager))
		{
			_userManager.UserLoggedIn -= OnUserLoggedIn;
			_userManager.UserLoggedOut -= OnUserLoggedOut;
		}
	}

	/// <summary>
	/// Provides comprehensive help content for the Mining Game
	/// Explains all game mechanics, upgrades, and progression systems
	/// </summary>
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
	internal UserManager GetUserManager() => _userManager;
	internal bool IsDebugMode() => EnableDebugMode;
	internal MiningState GetState() => _state;

	// ================================================================
	// PUBLIC API - Called directly by UI (no signals)
	// ================================================================
		
	public bool CanExtractGems() => _state?.CanExtractGems() ?? false;
	public bool CanPurchaseCredit() => _state?.CanPurchaseCredit() ?? false;
	public bool CanPurchaseUpgrade(UpgradeType upgradeType) => _state?.CanPurchaseUpgrade(upgradeType) ?? false;
		
	public MiningLocationData GetLocationData() => _state?.GetLocationData();
	public MiningGlobalDataStore GetGlobalData() => _state?.GetGlobalData();
		
	// Encapsulated access to engine properties
	public float GetMiningProgress() => _engine?.GetMiningProgress() ?? 0.0f;
	public float GetTimeUntilNextTick() => _engine?.GetTimeUntilNextTick() ?? 0.0f;
		
	// Encapsulated access to state properties  
	public int GetPendingGems() => _state?.PendingGems ?? 0;
	public int GetMaxCapacity() => _state?.GetMaxCapacity() ?? 0;

	// Specifically gets the game time cached on the state, not the Game Time getter inherited from game controller
	public double GetStateTime() => _state?.GameTime ?? 0.0;
	public int GetGemsPerTick() => _state?.GetGemsPerTick() ?? 0;
	public float GetMiningTickTime() => _state?.GetMiningTickTime() ?? 0.0f;
	public int GetUpgradeLevel(UpgradeType upgradeType) => _state?.GetUpgradeLevel(upgradeType) ?? 0;
		
	public MiningLocationData GetLocationDataTemplate(string locationId)
	{
		// Try to get exact match first
		if (_locationDataRegistry.TryGetValue(locationId ?? DEFAULT_LOCATION_ID, out var exactMatch))
		{
			return exactMatch;
		}

		// Log debug info for missing location (fallback to default is expected)
		if (EnableDebugMode)
		{
			GD.Print($"[MiningGame] No location data found for '{locationId}'. Available locations: {string.Join(", ", _locationDataRegistry.Keys)}");
		}

		// Fall back to first available location
		if (_locationDataRegistry.Count > 0)
		{
			// Use foreach for cold path initialization
			MiningLocationData fallback = null;
			foreach (var location in _locationDataRegistry.Values)
			{
				fallback = location;
				break;
			}
				
			if (EnableDebugMode)
			{
				GD.Print($"[MiningGame] Using fallback location '{fallback.LocationId}' instead of '{locationId}'");
			}
				
			return fallback;
		}
			
		// Complete failure - no location data at all
		GD.PrintErr("[MiningGame] CRITICAL ERROR: No location data configured in registry!");
		return null;
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
				
			// Handle immediate upgrade effects with unified approach
			if (IsInstanceValid(_engine))
			{
				// Timestamp-based system handles upgrade effects automatically
			}
		}
	}
		
	// Direct UI updates - no signal overhead
	public void UpdateUI()
	{
		if (IsInstanceValid(_ui))
			_ui.UpdateAllUI();
	}
		
	/// <summary>
	/// Debug logging - only outputs when EnableDebugMode is true
	/// </summary>
	private void LogDebug(string message)
	{
		if (EnableDebugMode)
			GD.Print($"[MiningGame] {message}");
	}

	/// <summary>
	/// Error logging - always outputs
	/// </summary>
	private void LogError(string message)
	{
		GD.PrintErr($"[MiningGame] {message}");
	}

	/// <summary>
	/// Thread-safe logging method for async errors
	/// </summary>
	private void LogAsyncError(string message)
	{
		GD.PrintErr($"[MiningGame] {message}");
	}

	/// <summary>
	/// Get the current user's phone number from SessionManager
	/// Returns null if no user is logged in
	/// </summary>
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

		// Return null if no user is logged in - no fallbacks
		return null;
	}
		
	// ================================================================
	// EVENT HANDLERS
	// ================================================================
		
	private void OnUserLoggedIn(string phoneNumber, string userName)
	{
		if (_isProcessingUserChange)
		{
			GD.PrintErr("[MiningGame] User change already in progress, ignoring login event");
			return;
		}

		_isProcessingUserChange = true;

		try
		{
			// Clear any previous user state FIRST to prevent data mixing
			if (IsInstanceValid(_state))
				_state.ClearAllState();

			// User data will be loaded by InitializeGameSession() after StartGame()
			// No need to load it here to avoid race conditions

			// Ensure game is properly reset before starting
			if (IsGameActive())
			{
				EndGame();
			}

			StartGame();

			// Game session is automatically initialized via OnGameStarted() -> InitializeGameSession()
			// No need to call InitializeGameSession() again as it would cause double initialization

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
			// Event-sourced persistence - no explicit save needed

			EndGame();

			// Clear all state to ensure no previous user data remains
			if (IsInstanceValid(_state))
				_state.ClearAllState();

			// Ensure UI properly reflects no-user state
			if (IsInstanceValid(_ui))
			{
				_ui.SetEnabled(false);
				_ui.UpdateAllUI(); // Force UI refresh to show "login required" messages
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
			if (_userManager != null && IsInstanceValid(_userManager))
			{
				_userManager.UserLoggedIn -= OnUserLoggedIn;
				_userManager.UserLoggedOut -= OnUserLoggedOut;
			}
		}
	}
}