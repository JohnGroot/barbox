using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BarBox.Core.Autoloads;
using BarBox.Games.MiningGame.Logic;
using Godot;

namespace BarBox.Games.MiningGame;

[GlobalClass]
public partial class MiningGame : GameController
{
	protected override string GetGameId() => "mining";

	// ================================================================
	// CONSTANTS
	// ================================================================
	private const int CREDITS_PER_PURCHASE = 1000;
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
	[Signal]
	public delegate void MiningSessionStartedEventHandler();

	[Signal]
	public delegate void MiningSessionEndedEventHandler();

	// Game event signals
	[Signal]
	public delegate void GemsExtractedEventHandler(int amount, GemType gemType);

	[Signal]
	public delegate void CreditPurchasedEventHandler();

	[Signal]
	public delegate void UpgradePurchasedEventHandler(UpgradeType upgradeType, int newLevel);

	// ================================================================
	// EXPORT PROPERTIES
	// ================================================================
	[ExportCategory("Game Settings")]
	[Export]
	public MiningGameConfig Config { get; set; }

	[Export]
	public NodePath UIPath { get; set; }

	[Export]
	public bool EnableDebugMode { get; set; } = false;

	[Export]
	public bool AutoStartInEditor { get; set; } = true;

	/// <summary>
	/// Allow players to logout during active mining sessions
	/// </summary>
	public override bool CanLogout => true;

	// ================================================================
	// PRIVATE FIELDS
	// ================================================================
	private MiningGameUI _ui;
	private MiningEngine _engine;
	private MiningState _state;
	private SessionEventService _eventService;
	private MiningEventService _miningEventService;

	// Platform services
	private SessionManager _sessionManager;
	private LocationManager _locationManager;

	// Race condition prevention
	private bool _isProcessingUserChange = false;

	// Backend-derived location configuration
	private MiningLocationConfig _locationConfig;

	// ================================================================
	// INITIALIZATION
	// ================================================================

	/// <summary>
	/// PHASE 1: Service Discovery
	/// Discovers platform services and initializes event services
	/// POST-CONDITION GUARANTEES:
	/// - _eventService exists and is valid (REQUIRED)
	/// - _miningEventService exists (REQUIRED)
	/// - _sessionManager, _locationManager exist in production (REQUIRED)
	/// </summary>
	protected override void OnDiscoverServices()
	{
		// Initialize REQUIRED event services
		_eventService = SessionEventService.GetInstance();
		if (_eventService == null)
		{
			throw new InvalidOperationException("SessionEventService is required but not available");
		}

		_miningEventService = new MiningEventService(_eventService);

		// Cache platform services for convenience (Platform property provides these too)
		_sessionManager = Platform.Session;
		_locationManager = Platform.Location;

		// Context detection and validation
		if (Platform.IsProduction)
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

			if (Platform.Host == null)
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
		{
			return;
		}

		var mainScene = GD.Load<PackedScene>(MAIN_SCENE_PATH);
		if (mainScene != null)
		{
			var mainInstance = mainScene.Instantiate();
			GetTree().Root.CallDeferred(Node.MethodName.AddChild, mainInstance);

			if (mainInstance is CanvasItem canvasItem)
			{
				canvasItem.Visible = false;
			}
		}
	}

	/// <summary>
	/// PHASE 2: Component Initialization
	/// Creates game components (_engine, _state, _ui)
	/// NOTE: Location config obtained asynchronously in OnActivateGame()
	/// POST-CONDITION GUARANTEES:
	/// - _engine exists and is valid
	/// - _state exists and is valid
	/// - _ui exists and is valid
	/// - Config is not null
	/// </summary>
	protected override void OnInitializeComponents()
	{
		Config ??= new MiningGameConfig();

		_engine = new MiningEngine(this);
		_state = new MiningState(this);

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
		_engine.StopMining();
		_ui.SetEnabled(false);

		// Notify platform that game session ended
		Platform.Host?.NotifyGameEnded();

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
			// Create backend activity session if we have a valid player
			await TryCreateBackendSession();

			// Load user data if logged in
			if (!string.IsNullOrEmpty(GetCurrentUserPhoneNumber()))
			{
				await _state.LoadUserDataAsync();

				// Check if user logged out during async operation
				if (string.IsNullOrEmpty(GetCurrentUserPhoneNumber()))
				{
					GD.PrintErr("[MiningGame] User logged out during data load");
					return;
				}
			}

			_engine.StartMining();

			// Update UI based on login state
			bool isLoggedIn = !string.IsNullOrEmpty(GetCurrentUserPhoneNumber());
			_ui.SetEnabled(isLoggedIn);
			if (isLoggedIn)
			{
				_ui.UpdateAllUI();
			}

			// Notify platform that game session started
			Platform.Host?.NotifyGameStarted();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MiningGame] Error during session initialization: {ex.Message}");
			_ui.SetEnabled(false);
			_ui.UpdateAllUI();
		}
	}

	private async Task TryCreateBackendSession()
	{
		if (string.IsNullOrEmpty(GetCurrentUserPhoneNumber()))
		{
			return;
		}

		var currentSession = _sessionManager?.GetPrimarySession();
		if (currentSession?.PlayerId == null || currentSession.PlayerId == Guid.Empty)
		{
			GD.PrintErr("[MiningGame] Cannot create backend session - no valid player ID");
			return;
		}

		var playerId = currentSession.PlayerId;
		var boxId = _locationManager?.BoxId ?? Guid.Empty;

		GD.Print($"[MiningGame] Creating backend session for player {playerId} at box {boxId}");

		// Session create/close is owned by GameController (auto-closed on teardown).
		var sessionResult = await StartBackendSessionAsync(boxId, playerId);

		if (sessionResult.IsFailure(out var error))
		{
			GD.PrintErr($"[MiningGame] WARNING: Failed to create backend session: {error.Message}");
			GD.PrintErr("[MiningGame] Game will continue but persistence may not work");
		}
		else if (sessionResult.IsSuccess(out var sessionId))
		{
			GD.Print($"[MiningGame] Backend session created successfully: {sessionId}");
		}
	}

	/// <summary>
	/// PHASE 4: Activation Decision
	/// Registers location with backend, then starts mining session if user logged in
	/// </summary>
	protected override void OnActivateGame()
	{
		// Start async location registration
		RegisterLocationAndActivateAsync();
	}

	private async void RegisterLocationAndActivateAsync()
	{
		try
		{
			// Get venue name from environment
			var venueName = _locationManager?.VenueName ?? "dev_location";
			if (string.IsNullOrEmpty(venueName))
			{
				venueName = "dev_location";
				GD.PushWarning("[MiningGame] BARBOX_VENUE_NAME not set, using dev_location");
			}

			GD.Print($"[MiningGame] Registering location: {venueName}");

			// Register with backend (idempotent)
			var result = await _miningEventService.RegisterLocationAsync(venueName);

			// Check validity after await
			if (!IsInstanceValid(this))
			{
				return;
			}

			if (result.IsFailure(out var error))
			{
				GD.PrintErr($"[MiningGame] Location registration failed: {error.Message}");

				// Use dev fallback in non-production
				if (Platform.IsDevelopment)
				{
					GD.Print("[MiningGame] Using dev fallback config");
					_locationConfig = MiningLocationConfig.CreateDevDefault();
				}
				else
				{
					// Show error UI in production
					ShowRegistrationError(error.Message);
					return;
				}
			}
			else if (result.IsSuccess(out var config))
			{
				_locationConfig = config;
				GD.Print($"[MiningGame] Location registered: {config.VenueName} → {config.GemTypeString}");
			}

			// Check validity again
			if (!IsInstanceValid(this) || !IsInstanceValid(_ui))
			{
				return;
			}

			// Apply location config to UI
			_ui.ApplyLocationConfig(_locationConfig);

			// Now proceed with activation
			if (IsPlayerLoggedIn())
			{
				StartMiningSession();
			}
			else
			{
				_ui.SetEnabled(false);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MiningGame] Error during location registration: {ex.Message}");

			if (!IsInstanceValid(this))
			{
				return;
			}

			if (Platform.IsDevelopment)
			{
				_locationConfig = MiningLocationConfig.CreateDevDefault();
				if (IsInstanceValid(_ui))
				{
					_ui.ApplyLocationConfig(_locationConfig);
					if (IsPlayerLoggedIn())
					{
						StartMiningSession();
					}
					else
					{
						_ui.SetEnabled(false);
					}
				}
			}
			else
			{
				ShowRegistrationError(ex.Message);
			}
		}
	}

	private void ShowRegistrationError(string errorMessage)
	{
		GD.PrintErr($"[MiningGame] Registration error: {errorMessage}");

		if (IsInstanceValid(_ui))
		{
			_ui.SetEnabled(false);
			_ui.ShowError(ErrorMessages.BACKEND_UNAVAILABLE_TITLE, ErrorMessages.BACKEND_UNAVAILABLE_MESSAGE);
		}
	}

	protected override void OnGameTeardown()
	{
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

		// Note: User signal disconnection and backend session close are handled by the base class
	}

	protected override HelpContentData GetHelpContent()
	{
		var gemTypeName = _locationConfig?.GetGemType().ToString() ?? "Crystal";

		return new HelpContentData("MINING HOW-TO")
			.AddSection(
				"⛏️ WELCOME TO THE MINES ⛏️",
				"• Gems accumulate automatically even while not playing!",
				$"• This location mines {gemTypeName} gems",
				"• Each machine has a capacity limit - extract gems regularly!")

			.AddSection(
				"💎 EXTRACTING GEMS 💎",
				"• Gems are stored locally on each machine until you extract them:",
				"• Extracting clears out space in the mining capacity so you can Mine more Gems",
				"• Extracted Gems can be used at any BarBox location")

			.AddSection(
				"😮‍💨 USING GEMS 🤑",
				"• Trade your extracted gems for credits OR upgrade this machine's mine",
				"• Buy unlimited credits as long as you have enough gems",
				"• Credits cost the same gem type as the machine produces")

			.AddSection(
				"⬆️ THREE UPGRADE TYPES ⬆️",
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

	internal void ShowNotification(string message, NotificationSeverity severity) =>
		Platform.Notifications?.Show(message, severity);

	internal SessionManager GetSessionManager() => _sessionManager;

	internal bool IsDebugMode() => EnableDebugMode;

	internal MiningState GetState() => _state;

	internal MiningLocationConfig GetLocationConfig() => _locationConfig;

	// ================================================================
	// PUBLIC API
	// ================================================================
	public bool CanExtractGems() => _state.CanExtractGems();

	public bool CanPurchaseCredit() => _state.CanPurchaseCredit();

	public bool CanPurchaseUpgrade(UpgradeType upgradeType) => _state.CanPurchaseUpgrade(upgradeType);

	public MiningGlobalDataStore GetGlobalData() => _state.GetGlobalData();

	public float GetMiningProgress() => _engine.GetMiningProgress();

	public float GetTimeUntilNextTick() => _engine.GetTimeUntilNextTick();

	public int GetPendingGems() => _state.PendingGems;

	public int GetMaxCapacity() => _state.GetMaxCapacity();

	public int GetGemsPerTick() => _state.GetGemsPerTick();

	public float GetMiningTickTime() => _state.GetMiningTickTime();

	public int GetUpgradeLevel(UpgradeType upgradeType) => _state.GetUpgradeLevel(upgradeType);

	public GemType GetPrimaryGemType() => _locationConfig?.GetGemType() ?? GemType.Amethyst;

	public int GetCreditsPerPurchase() => CREDITS_PER_PURCHASE;

	public void ExtractGems()
	{
		if (_locationConfig == null)
		{
			return;
		}

		if (!CanExtractGems())
		{
			return;
		}

		int amount = _state.PendingGems;
		GemType gemType = _locationConfig.GetGemType();

		if (_state.ExtractGems())
		{
			_ui.UpdateAllUI();
			EmitSignal(SignalName.GemsExtracted, amount, (int)gemType);
			GD.Print($"[MiningGame] Extracted {amount} {gemType} gems");
		}
	}

	public async Task PurchaseCreditAsync()
	{
		var result = await _state.PurchaseCreditAsync();
		switch (result)
		{
			case MiningState.CreditPurchaseResult.Success:
				_ui.UpdateAllUI();
				EmitSignal(SignalName.CreditPurchased);
				GD.Print("[MiningGame] Credit purchased");
				break;
			case MiningState.CreditPurchaseResult.DepositFailed:
				_ui.UpdateAllUI();
				_ui.ShowError("Purchase Failed", "Could not add credits - gems were refunded.");
				break;
			case MiningState.CreditPurchaseResult.NotEligible:
				break;
		}
	}

	public void PurchaseUpgrade(UpgradeType upgradeType)
	{
		var currentLevel = _state.GetUpgradeLevel(upgradeType);

		if (_state.PurchaseUpgrade(upgradeType))
		{
			_ui.UpdateAllUI();
			EmitSignal(SignalName.UpgradePurchased, (int)upgradeType, currentLevel + 1);
			GD.Print($"[MiningGame] {upgradeType} upgraded to level {currentLevel + 1}");
		}
	}

	public void UpdateUI()
	{
		_ui.UpdateAllUI();
	}

	internal string GetCurrentUserPhoneNumber()
	{
		return _sessionManager?.GetPrimarySession()?.PhoneNumber;
	}

	// ================================================================
	// EVENT HANDLERS
	// ================================================================
	protected override void OnUserLoggedIn(UserSession session)
	{
		if (_isProcessingUserChange)
		{
			GD.PrintErr("[MiningGame] User change already in progress, ignoring login event");
			return;
		}

		_isProcessingUserChange = true;

		try
		{
			_state.ClearAllState();

			if (IsPlayerLoggedIn())
			{
				EndMiningSession();
			}

			StartMiningSession();

			var phoneNumber = session?.PhoneNumber ?? string.Empty;
			var userName = session?.UserName ?? string.Empty;

			GD.Print($"[MiningGame] User logged in: {userName} ({phoneNumber})");
		}
		finally
		{
			_isProcessingUserChange = false;
		}
	}

	protected override void OnUserLoggedOut(string phoneNumber)
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

			_state.ClearAllState();

			_ui.SetEnabled(false);
			_ui.UpdateAllUI();

			GD.Print("[MiningGame] User logged out and state cleared");
		}
		finally
		{
			_isProcessingUserChange = false;
		}
	}
}
