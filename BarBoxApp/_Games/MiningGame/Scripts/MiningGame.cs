using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace BarBox.Games.MiningGame
{
	[GlobalClass]
	public partial class MiningGame : GameController
	{
		// ================================================================
		// CONSTANTS
		// ================================================================
		
		private const int DEBUG_STARTING_CREDITS = 5;
		private const int CREDITS_PER_PURCHASE = 1;
		
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
		
		[ExportCategory("Location Data")]
		[Export] public Godot.Collections.Array<MiningLocationData> LocationDataResources { get; set; } = new();
		
		// ================================================================
		// PRIVATE FIELDS
		// ================================================================

		private MiningGameUI _ui;
		private GameEngine _engine;
		private GameState _state;
		private MiningEventService _miningEventService;

		// Platform services
		private GameHost _gameHost;
		private UserManager _userManager;
		private LocationManager _locationManager;

		// Context detection
		private bool _isProductionContext;
		private Guid _playerId;
		
		// Race condition prevention
		private bool _isProcessingUserChange = false;

		// Location data management
		private Dictionary<string, MiningLocationData> _locationDataRegistry = new();
		
		// ================================================================
		// INITIALIZATION
		// ================================================================

		protected override void InitializeGame()
		{
			base.InitializeGame();

			// Initialize event service
			_miningEventService = new MiningEventService(EventService.GetInstance());

			DetectAndAdaptToContext();
			InitializeComponents();
			
			if (!_isProductionContext && AutoStartInEditor)
			{
				CallDeferred(MethodName.StartGame);
			}
			// Note: Production context UI state will be handled in OnUIContextSetup() after event handlers are connected
		}
		
		private void DetectAndAdaptToContext()
		{
			_gameHost = GameHost.GetInstance();
			_userManager = UserManager.GetAutoload();
			_locationManager = LocationManager.GetAutoload();

			// Check if we're loaded as a direct scene (root scene) vs overlay
			bool isDirectSceneLoad = GetTree().CurrentScene == this;

			if (_gameHost != null && IsInstanceValid(_gameHost))
			{
				_isProductionContext = true;
				var playerSession = _gameHost.GetPlayerSession("default");
				var playerIdString = playerSession?.PlayerId ?? _gameHost.GetCurrentPlayerId();
				_playerId = Guid.TryParse(playerIdString, out var parsedId) ? parsedId : Guid.Empty;
			}
			else
			{
				_isProductionContext = false;
				// No auto-login or dev_player - require actual user login for data persistence
				_playerId = Guid.Empty;
				EnableDebugMode = true;
			}

			// Ensure MainController exists for logout functionality when loading scene directly
			if (isDirectSceneLoad)
			{
				EnsureMainControllerExists();
			}

			// Context detected
		}
		
		
		private void EnsureMainControllerExists()
		{
			// Check if MainController already exists in scene tree by searching for the type
			var mainControllers = GetTree().GetNodesInGroup("_MainController");
			MainController existingMain = null;
			
			// Search through all nodes to find MainController type
			if (mainControllers.Count == 0)
			{
				foreach (Node node in GetTree().GetNodesInGroup("_all"))
				{
					if (node is MainController)
					{
						existingMain = (MainController)node;
						break;
					}
				}
				
				// If still not found, search the root children directly
				if (existingMain == null)
				{
					foreach (Node child in GetTree().Root.GetChildren())
					{
						if (child is MainController)
						{
							existingMain = (MainController)child;
							break;
						}
					}
				}
			}
			
			if (existingMain == null)
			{
				// Load Main.tscn as a background node
				var mainScene = GD.Load<PackedScene>("res://_Core/Scenes/Main.tscn");
				
				if (mainScene != null)
				{
					var mainInstance = mainScene.Instantiate();
					
					// Use CallDeferred for adding child during _Ready() to avoid scene tree setup conflicts
					GetTree().Root.CallDeferred("add_child", mainInstance);
					
					// Hide the MainController UI since we only need its logout logic (also deferred)
					if (mainInstance is CanvasItem canvasItem)
					{
						canvasItem.CallDeferred("hide");
					}
					else
					{
						GD.PrintErr($"MainController is not a CanvasItem: {mainInstance.GetType().Name}");
					}
					
					// Ensure UIManager signal connection is established after MainController is ready
					CallDeferred(nameof(EnsureMainControllerConnection), mainInstance);
				}
				else
				{
					GD.PrintErr("Could not load Main.tscn for MainController");
				}
			}
		}
		
		/// <summary>
		/// Ensures MainController is properly connected to UIManager signals after deferred loading
		/// </summary>
		private void EnsureMainControllerConnection(Node mainInstance)
		{
			if (mainInstance is MainController mainController)
			{
				// Force UIManager signal connection using MainController's public method
				mainController.ForceUIManagerConnection();
			}
			else
			{
				GD.PrintErr($"Failed to cast {mainInstance?.GetType().Name} to MainController");
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
		
		private void InitializeComponents()
		{
			// Initialize location data registry first
			InitializeLocationDataRegistry();
			
			// Setup configuration
			if (Config == null)
			{
				Config = new MiningGameConfig();
				
				// Try to load location-specific config
				if (_locationManager != null)
				{
					var locationId = _locationManager.CurrentLocationId ?? "default";
					var locationConfig = _locationManager.GetLocationData<MiningGameConfig>(locationId);
					if (locationConfig != null)
					{
						Config = locationConfig;
					}
				}
			}
			
			// Initialize game systems
			_engine = new GameEngine(this);
			_state = new GameState(this);
			
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
				if (!GodotObject.IsInstanceValid(_state) || !GodotObject.IsInstanceValid(_engine))
				{
					GD.PrintErr("[MiningGame] Cannot initialize session - missing essential components (state/engine)");
					return;
				}
				
				// In production context, we need a valid user to proceed with actual game functionality
				if (string.IsNullOrEmpty(GetCurrentUserPhoneNumber()) && _isProductionContext)
				{
					GD.PrintErr("[MiningGame] Cannot start: No user logged in");

					// Ensure UI shows disabled state for no-user scenario
					if (GodotObject.IsInstanceValid(_ui))
					{
						_ui.SetEnabled(false);
						_ui.UpdateAllUI(); // Show "login required" messages
					}
					return;
				}

				// CREATE BACKEND SESSION - CRITICAL FOR EVENT PERSISTENCE
				if (_isProductionContext && _miningEventService != null)
				{
					var locationManager = _locationManager;
					if (locationManager != null && GodotObject.IsInstanceValid(locationManager))
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

								var sessionResult = await _miningEventService.CreateSessionAsync(boxId, playerId);
								if (!sessionResult.IsSuccess)
								{
									GD.PrintErr($"[MiningGame] WARNING: Failed to create backend session: {sessionResult.Error}");
									GD.PrintErr($"[MiningGame] Game will continue but persistence may not work");
									// Continue anyway - game can work in offline mode
								}
								else
								{
									GD.Print($"[MiningGame] Backend session created successfully: {sessionResult.Value}");
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

					// Check if components are still valid after await
					if (!GodotObject.IsInstanceValid(this) || !GodotObject.IsInstanceValid(_engine))
						return;
				}
				
				// Start mining only after user data (including partial progress) is fully loaded
				_engine.StartMining();
				
				// Enable UI after all components are ready
				if (GodotObject.IsInstanceValid(_ui))
				{
					_ui.RefreshLocationData(); // Update cached location data after load
					_ui.SetEnabled(true);
					_ui.UpdateAllUI(); // Force immediate UI refresh
				}
			}
			catch (System.Exception ex)
			{
				GD.PrintErr($"[MiningGame] Error during session initialization: {ex.Message}");
				
				// Ensure UI shows error state
				if (GodotObject.IsInstanceValid(_ui))
				{
					_ui.SetEnabled(false);
					_ui.UpdateAllUI(); // Show error state
				}
			}
		}
		
		public override void StartGame()
		{
			base.StartGame();
		}
		
		public void StopGame()
		{
			OnGameEnded();
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
			
			if (GodotObject.IsInstanceValid(_engine))
				_engine.StopMining();
				
			if (GodotObject.IsInstanceValid(_state))
				
			if (GodotObject.IsInstanceValid(_ui))
				_ui.SetEnabled(false);
			
			EmitSignal(SignalName.GameEnded);
			GD.Print("[MiningGame] Game ended");
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
			
			// Now handle initial UI state based on current context (after event handlers are connected)
			if (_isProductionContext)
			{
				if (!string.IsNullOrEmpty(GetCurrentUserPhoneNumber()))
				{
					// User is already logged in, start the game
					CallDeferred(MethodName.StartGame);
				}
				else
				{
					// No user logged in, disable UI until login
					if (GodotObject.IsInstanceValid(_ui))
					{
						_ui.SetEnabled(false);
						_ui.UpdateAllUI(); // Show "login required" messages
					}
				}
			}
		}
		
		public override void OnUIContextTeardown()
		{
			base.OnUIContextTeardown();

			// Disconnect event handlers when UI context is torn down
			if (_userManager != null && GodotObject.IsInstanceValid(_userManager))
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
			if (_locationDataRegistry.TryGetValue(locationId ?? "default", out var exactMatch))
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
			if (!GodotObject.IsInstanceValid(_state))
				return;

			var locationTemplate = _state.GetLocationData();
			if (locationTemplate == null || !CanExtractGems()) return;

			int amount = _state.PendingGems;
			int maxCapacity = _state.GetMaxCapacity();
			bool wasAtCapacity = amount >= maxCapacity;
			GemType gemType = locationTemplate.PrimaryGemType;

			if (_state.ExtractGems())
			{

				if (GodotObject.IsInstanceValid(_ui))
					_ui.UpdateAllUI();
				EmitSignal(SignalName.GemsExtracted, amount, (int)gemType);
				GD.Print($"[MiningGame] Extracted {amount} {gemType} gems");
			}
		}
		
		public void PurchaseCredit()
		{
			if (GodotObject.IsInstanceValid(_state) && _state.PurchaseCredit())
			{
				if (GodotObject.IsInstanceValid(_ui))
					_ui.UpdateAllUI();
				EmitSignal(SignalName.CreditPurchased);
				GD.Print("[MiningGame] Credit purchased");
			}
		}
		
		public void PurchaseUpgrade(UpgradeType upgradeType)
		{
			if (!GodotObject.IsInstanceValid(_state))
				return;
				
			var currentLevel = _state.GetUpgradeLevel(upgradeType);
			
			if (_state.PurchaseUpgrade(upgradeType))
			{
				if (GodotObject.IsInstanceValid(_ui))
					_ui.UpdateAllUI();
				EmitSignal(SignalName.UpgradePurchased, (int)upgradeType, currentLevel + 1);
				GD.Print($"[MiningGame] {upgradeType} upgraded to level {currentLevel + 1}");
				
				// Handle immediate upgrade effects with unified approach
				if (GodotObject.IsInstanceValid(_engine))
				{
					// Timestamp-based system handles upgrade effects automatically
				}
			}
		}
		
		// Direct UI updates - no signal overhead
		public void UpdateUI()
		{
			if (GodotObject.IsInstanceValid(_ui))
				_ui.UpdateAllUI();
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
				if (GodotObject.IsInstanceValid(_state))
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

				StopGame();

				// Clear all state to ensure no previous user data remains
				if (GodotObject.IsInstanceValid(_state))
					_state.ClearAllState();

				// Ensure UI properly reflects no-user state
				if (GodotObject.IsInstanceValid(_ui))
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
				// Cleanup
				if (GodotObject.IsInstanceValid(_engine))
					_engine.StopMining();
					
				if (GodotObject.IsInstanceValid(_state))
				
				if (_userManager != null && GodotObject.IsInstanceValid(_userManager))
				{
					_userManager.UserLoggedIn -= OnUserLoggedIn;
					_userManager.UserLoggedOut -= OnUserLoggedOut;
				}
			}
		}
		
		// ================================================================
		// NESTED CLASSES - Core game systems
		// ================================================================
		
		public partial class GameEngine : Node
		{
			private MiningGame _game;
			private bool _isMiningActive = false;
			
			public GameEngine(MiningGame game)
			{
				_game = game;
				Name = "Engine";
			}
			
			public override void _Process(double delta)
			{
				if (!_isMiningActive || _game == null || !GodotObject.IsInstanceValid(_game._state)) return;
				
				_game._state.ProcessReadyMiningTicks();
				
				// Update UI if needed
				if (_game != null && GodotObject.IsInstanceValid(_game._ui))
				{
					_game._ui.UpdateMiningProgress();
				}
			}
			
			
			public void StartMining()
			{
				_isMiningActive = true;
				GD.Print("[GameEngine] Mining started");
			}
			
			public void StopMining()
			{
				_isMiningActive = false;
				GD.Print("[GameEngine] Mining stopped");
			}
			
			
			public void TriggerImmediateMiningTick()
			{
				if (_isMiningActive && _game != null && GodotObject.IsInstanceValid(_game._state))
				{
					_game._state.ProcessReadyMiningTicks();
				}
			}
			
			public float GetMiningProgress()
			{
				if (!_isMiningActive || _game == null || !GodotObject.IsInstanceValid(_game._state)) return 0.0f;
				
				var (_, progress) = _game._state.CalculateMiningProgress();
				return progress;
			}
			
			public float GetTimeUntilNextTick()
			{
				if (!_isMiningActive || _game == null || !GodotObject.IsInstanceValid(_game._state)) return 0.0f;
				
				var (_, progress) = _game._state.CalculateMiningProgress();
				var miningTickTime = _game._state.GetMiningTickTime();
				return (float)((1.0f - progress) * miningTickTime);
			}
			
		}
		
		public partial class GameState : Node
		{
			private MiningGame _game;
			private MiningLocationData _locationTemplate;
			private MiningGlobalDataStore _globalData;
			
			// Runtime state data (what gets saved/loaded)
			private int _pendingGems = 0;
			private Dictionary<UpgradeType, int> _upgradeLevels = new();
			private bool _firstTimeBonus = true;
			private DateTime _lastMiningTickTime = DateTime.UtcNow;
			
			// Cached calculated values
			private int _cachedMaxCapacity;
			private float _cachedMiningTickTime;
			private int _cachedGemsPerTick;
			private bool _cacheValid = false;
			
			// Game time management
			private double _gameTime = 0.0;
			private double _lastSaveTime = 0.0;
			private const double AUTO_SAVE_INTERVAL = 30.0;
			private const double SECONDS_PER_HOUR = 3600.0;
			
			
			private const string LOCATION_STATE_PREFIX = "mining_state_";
			private const string GLOBAL_DATA_KEY = "mining_global";
			
			public GameState(MiningGame game)
			{
				_game = game;
				Name = "State";
			}
			
			public override void _Process(double delta)
			{
				_gameTime += delta;
				
				// Auto-save every 30 seconds (replaces save timer)
				if (_gameTime - _lastSaveTime >= AUTO_SAVE_INTERVAL)
				{
				_lastSaveTime = _gameTime;

				// Emit mining tick event to backend
				if (_game._miningEventService != null)
				{
					var locationId = _game._locationManager?.CurrentLocationId ?? "default";
					_ = EmitMiningTickSafeAsync(locationId, _pendingGems);
				}

				GD.Print("[GameState] Auto-save: tick event emitted");
				}
			}
			
			// Public API for accessing runtime state
			public int PendingGems => _pendingGems;
			public bool IsExtractionReady => _pendingGems > 0;
			public bool FirstTimeBonus => _firstTimeBonus;
			public double GameTime => _gameTime;
			
			// Cached calculated values
			public int GetMaxCapacity()
			{
				RefreshCacheIfNeeded();
				return _cachedMaxCapacity;
			}
			
			public float GetMiningTickTime()
			{
				RefreshCacheIfNeeded();
				return _cachedMiningTickTime;
			}
			
			public int GetGemsPerTick()
			{
				RefreshCacheIfNeeded();
				return _cachedGemsPerTick;
			}

			public int GetUpgradeLevel(UpgradeType upgradeType)
			{
				return _upgradeLevels.ContainsKey(upgradeType) ? _upgradeLevels[upgradeType] : 0;
			}
			
			public void SetUpgradeLevel(UpgradeType upgradeType, int level)
			{
				_upgradeLevels[upgradeType] = level;
				InvalidateCache();
			}
			
			private void RefreshCacheIfNeeded()
			{
				if (!_cacheValid && _locationTemplate != null)
				{
					_cachedMaxCapacity = _locationTemplate.GetMaxCapacity(GetUpgradeLevel(UpgradeType.Capacity), _game.Config);
					_cachedMiningTickTime = _locationTemplate.GetMiningTickTime(GetUpgradeLevel(UpgradeType.MiningSpeed), _game.Config);
					_cachedGemsPerTick = _locationTemplate.GetGemsPerTick(GetUpgradeLevel(UpgradeType.MiningAmount), _game.Config);
					_cacheValid = true;
				}
			}
			
			private void InvalidateCache()
			{
				_cacheValid = false;
			}
			
			// ================================================================
			// Unified timestamp-based mining calculator
			// ================================================================
			
			/// <summary>
			/// Calculates mining progress using timestamps - works for both online and offline!
			/// </summary>
			/// <returns>Tuple of (ticksReady, progressToNextTick)</returns>
			public (int ticksReady, float progressToNextTick) CalculateMiningProgress()
			{
				// Check if we're at capacity - no progress when full
				var maxCapacity = GetMaxCapacity();
				if (_pendingGems >= maxCapacity)
				{
					return (0, 0.0f);
				}

				var elapsed = (DateTime.UtcNow - _lastMiningTickTime).TotalSeconds;

				// Handle negative time (clock went backward) - treat as no progress
				if (elapsed < 0)
				{
					_lastMiningTickTime = DateTime.UtcNow;
					return (0, 0.0f);
				}
				
				var miningInterval = GetMiningTickTime();
				if (miningInterval <= 0) return (0, 0.0f);
				
				var ticksReady = (int)(elapsed / miningInterval);
				var progressToNextTick = (float)((elapsed % miningInterval) / miningInterval);
				
				return (ticksReady, progressToNextTick);
			}
			
			/// <summary>
			/// Process ready mining ticks and update timestamp
			/// </summary>
			public void ProcessReadyMiningTicks()
			{
				var (ticksReady, _) = CalculateMiningProgress();
				
				if (ticksReady <= 0) return;
				
				var maxCapacity = GetMaxCapacity();
				var gemsPerTick = GetGemsPerTick();
				
				// Check if we're already at capacity - don't process any ticks
				if (_pendingGems >= maxCapacity) return;
				
				// Calculate how many ticks we can actually process (capacity limit)
				var availableCapacity = maxCapacity - _pendingGems;
				// Use float division to prevent truncation errors
				var maxPossibleTicks = (int)((float)availableCapacity / Math.Max(1.0f, gemsPerTick));
				var actualTicks = Math.Min(ticksReady, maxPossibleTicks);
				
				if (actualTicks > 0)
				{
					_pendingGems += actualTicks * gemsPerTick;
					
					GD.Print($"[GameState] Processed {actualTicks} mining ticks: +{actualTicks * gemsPerTick} gems, Total: {_pendingGems}/{maxCapacity}");
					
					// Only advance timestamp when we actually processed ticks
					var miningInterval = GetMiningTickTime();
					_lastMiningTickTime = _lastMiningTickTime.AddSeconds(actualTicks * miningInterval);
				}
			}
			
			/// <summary>
			/// Reset mining timestamp (called when starting fresh cycle - e.g. when mining was paused at capacity)
			/// </summary>
			public void ResetMiningTimer()
			{
				_lastMiningTickTime = DateTime.UtcNow;
				
				GD.Print("[GameState] Mining timer reset to current time");
			}
			
			
		public async Task LoadUserDataAsync()
		{
			GD.Print("[GameState] === LoadUserDataAsync START ===");

			// Get location template first
			string currentLocationId = _game._locationManager?.CurrentLocationId ?? "default";
			_locationTemplate = _game.GetLocationDataTemplate(currentLocationId);
			GD.Print($"[GameState] Location: {currentLocationId}");

			// Get current user phone number
			var phoneNumber = _game.GetCurrentUserPhoneNumber();
			GD.Print($"[GameState] Phone number: {(string.IsNullOrEmpty(phoneNumber) ? "NONE" : phoneNumber)}");

			if (!string.IsNullOrEmpty(phoneNumber))
			{
				GD.Print("[GameState] User is logged in, attempting backend data load...");

				// Get player ID from session manager
				var sessionManager = SessionManager.GetInstance();
				GD.Print($"[GameState] SessionManager instance: {(sessionManager != null ? "FOUND" : "NULL")}");

				var currentSession = sessionManager?.GetPrimaryUserSession();
				GD.Print($"[GameState] Current session: {(currentSession != null ? "FOUND" : "NULL")}");

				if (currentSession?.PlayerId != null && currentSession.PlayerId != Guid.Empty)
				{
					var playerId = currentSession.PlayerId;
					GD.Print($"[GameState] Valid Player ID: {playerId}");

					// Check backend health before attempting to load
					var backendManager = BackendManager.GetInstance();
					bool isBackendHealthy = backendManager?.IsBackendRunning() ?? false;
					GD.Print($"[GameState] Backend health check: {(isBackendHealthy ? "HEALTHY" : "UNAVAILABLE")}");

					if (!isBackendHealthy)
					{
						if (GameHost.IsProductionContext())
						{
							// Production: Backend is REQUIRED for logged-in users
							GD.PrintErr("[GameState] CRITICAL ERROR: Backend is unavailable in production mode!");
							GD.PrintErr("[GameState] Cannot load user data without backend connection.");
							GD.PrintErr("[GameState] Please ensure the backend service is running and healthy.");

							// Show error to user
							_game._ui?.ShowError(
								"Service Unavailable",
								"Unable to connect to game server.\nPlease try again later or contact support if the issue persists."
							);

							// Create minimal default state to prevent crashes
							CreateDefaultState();
							return; // Skip processing - user should see an error
						}
						else
						{
							// Development: Fallback gracefully with warning
							GD.PushWarning("[GameState] Backend unavailable in development mode - using default state");
							GD.Print("[GameState] To test with backend data, ensure backend is running (sh scripts/dev.sh)");
							CreateDefaultState();
						}
					}
					else
					{
						// Backend is healthy - try to load state
						GD.Print("[GameState] Calling TryLoadStateFromBackend...");
						bool stateLoaded = await TryLoadStateFromBackend(playerId, currentLocationId);
						GD.Print($"[GameState] TryLoadStateFromBackend result: {(stateLoaded ? "SUCCESS" : "FAILED")}");

						if (!stateLoaded)
						{
							// First-time player or backend error - create default state
							GD.Print("[GameState] Backend load failed - creating default state");
							CreateDefaultState();
						}
						else
						{
							GD.Print("[GameState] Backend data loaded successfully!");
						}
					}
				}
				else
				{
					// No valid player ID - create default state
					GD.Print("[GameState] No valid player ID - creating default state");
					CreateDefaultState();
				}
			}
			else
			{
				// No user logged in - create default state for development/testing
				GD.Print("[GameState] No user logged in - creating default state (dev/test mode)");
				CreateDefaultState();
			}

			// Process any mining ticks that are ready (handles offline progress automatically)
			GD.Print("[GameState] Processing ready mining ticks...");
			ProcessReadyMiningTicks();

			// Invalidate cache so it gets rebuilt with new data
			InvalidateCache();
			GD.Print("[GameState] === LoadUserDataAsync COMPLETE ===");
		}


			/// <summary>
			/// Attempt to load state from backend using event aggregation
			/// </summary>
			private async Task<bool> TryLoadStateFromBackend(Guid playerId, string locationId)
			{
					GD.Print($"[GameState] === TryLoadStateFromBackend START === Player: {playerId}, Location: {locationId}");

				try
				{
					// Load global inventory (gems extracted - gems spent)
					var inventoryResult = await _game._miningEventService.GetPlayerInventoryAsync(playerId);
					if (inventoryResult.IsSuccess)
					{
						_globalData = new MiningGlobalDataStore();
						foreach (var kvp in inventoryResult.Value.Gems)
						{
							if (Enum.TryParse<GemType>(kvp.Key, true, out var gemType))
							{
								_globalData.AddGems(gemType, kvp.Value);
							}
						}

						GD.Print($"[GameState] Loaded inventory: {string.Join(", ", inventoryResult.Value.Gems.Select(g => $"{g.Key}={g.Value}"))}");
					}
					else
					{
						// No inventory found - new player
						_globalData = new MiningGlobalDataStore();

						GD.Print($"[GameState] No inventory found (new player): {inventoryResult.Error}");
					}

					// Load upgrade levels
					var upgradesResult = await _game._miningEventService.GetPlayerUpgradesAsync(playerId);
					if (upgradesResult.IsSuccess)
					{
						_upgradeLevels = new Dictionary<UpgradeType, int>();
						foreach (var kvp in upgradesResult.Value.Upgrades)
						{
							if (Enum.TryParse<UpgradeType>(kvp.Key, true, out var upgradeType))
							{
								_upgradeLevels[upgradeType] = kvp.Value;
							}
						}

						_firstTimeBonus = false; // Has upgrades = not first time

						GD.Print($"[GameState] Loaded upgrades: {string.Join(", ", upgradesResult.Value.Upgrades.Select(u => $"{u.Key}={u.Value}"))}");
					}
					else
					{
						// No upgrades found - new player
						_upgradeLevels = new Dictionary<UpgradeType, int>();
						_firstTimeBonus = true;

						GD.Print($"[GameState] No upgrades found (new player): {upgradesResult.Error}");
					}

					// Load last mining timestamp for offline progress
					var timestampResult = await _game._miningEventService.GetPlayerMiningTimestampAsync(playerId, locationId);
					if (timestampResult.IsSuccess)
					{
						_lastMiningTickTime = timestampResult.Value.LastMiningTime;

						var elapsed = (DateTime.UtcNow - _lastMiningTickTime).TotalMinutes;
						GD.Print($"[GameState] Loaded mining timestamp: {_lastMiningTickTime:yyyy-MM-dd HH:mm:ss} ({elapsed:F1} minutes ago)");
					}
					else
					{
						// No timestamp found - start fresh
						_lastMiningTickTime = DateTime.UtcNow;

						GD.Print($"[GameState] No timestamp found (new location): {timestampResult.Error}");
					}

					// Check first-time bonus status from metadata
					var metadataResult = await _game._miningEventService.GetPlayerMetadataAsync(playerId);
					if (metadataResult.IsSuccess)
					{
						_firstTimeBonus = !metadataResult.Value.HasReceivedBonus;

						GD.Print($"[GameState] Loaded metadata: HasBonus={metadataResult.Value.HasReceivedBonus}, TotalEvents={metadataResult.Value.TotalEvents}");
					}

					// Initialize pending gems to 0 (location-specific state starts fresh)
					_pendingGems = 0;

					return true;
				}
				catch (System.Exception ex)
				{
					GD.PrintErr($"[GameState] Error loading state from backend: {ex.Message}");
					return false;
				}
			}
			
			private void CreateDefaultState()
			{
				GD.Print("[GameState] Creating minimal default state (dev/test mode only)");

				_pendingGems = 0;
				_upgradeLevels = new Dictionary<UpgradeType, int>();
				_globalData = new MiningGlobalDataStore();
				_firstTimeBonus = true;
				_lastMiningTickTime = DateTime.UtcNow;

				// Note: First-time bonuses should be granted via backend events
				// Note: Debug resources should be granted via backend events
			}
			
			public bool CanExtractGems() => IsExtractionReady;
			
			public bool CanPurchaseCredit()
			{
				if (_globalData == null || _locationTemplate == null) return false;

				var cost = new Dictionary<GemType, int>
				{
					{ _locationTemplate.PrimaryGemType, _game.Config.CreditCost }
				};

				return _globalData.HasSufficientGems(cost);
			}
			
			public bool CanPurchaseUpgrade(UpgradeType upgradeType)
			{
				if (_locationTemplate == null || _globalData == null) return false;
				
				int currentLevel = GetUpgradeLevel(upgradeType);
				if (currentLevel >= _game.Config.MaxUpgradeLevel)
					return false;
				
				var cost = _game.Config.GetUpgradeCost(upgradeType, currentLevel, _locationTemplate.PrimaryGemType);
				return _globalData.HasSufficientGems(cost);
			}
			
			public bool ExtractGems()
			{
				if (!CanExtractGems() || _locationTemplate == null) return false;

				// Check if mining was paused (at capacity) BEFORE extraction
				var maxCapacity = GetMaxCapacity();
				bool wasAtCapacity = _pendingGems >= maxCapacity;

				int extractedAmount = _pendingGems;
				GemType gemType = _locationTemplate.PrimaryGemType;

				// Update local state
				_globalData.AddGems(gemType, extractedAmount);
				_pendingGems = 0;

				// Only reset timer if mining was paused at capacity
				// If mining was actively progressing, preserve the timer progress
				if (wasAtCapacity)
				{
					ResetMiningTimer(); // Start fresh cycle since mining was paused
				}
				// else: keep current timer progress for continuous mining

				// Emit event to backend with error logging
				if (_game._miningEventService != null)
				{
					var emitTask = _game._miningEventService.EmitExtractCompleteAsync(gemType, extractedAmount);

					// Fire and forget, but log failures for debugging
					_ = Task.Run(async () =>
					{
						var result = await emitTask;
						if (!result.IsSuccess)
						{
							GD.PrintErr($"[GameState] Failed to emit extraction event: {result.Error}");
						}
					});
				}

				return true;
			}
			
			public bool PurchaseCredit()
			{
				if (!CanPurchaseCredit() || _locationTemplate == null) return false;

				var cost = new Dictionary<GemType, int>
				{
					{ _locationTemplate.PrimaryGemType, _game.Config.CreditCost }
				};

				// Update local state
				_globalData.SpendGems(cost);

				// Actually add credit to user account
				if (!string.IsNullOrEmpty(_game.GetCurrentUserPhoneNumber()))
				{
					_game._userManager?.AddCredits(CREDITS_PER_PURCHASE);
				}

				// Emit event to backend
				if (_game._miningEventService != null)
				{
					_ = _game._miningEventService.EmitCreditDepositAsync(_locationTemplate.PrimaryGemType, _game.Config.CreditCost, CREDITS_PER_PURCHASE);
				}

				return true;
			}
			
			public bool PurchaseUpgrade(UpgradeType upgradeType)
			{
				if (!CanPurchaseUpgrade(upgradeType) || _locationTemplate == null) return false;

				int currentLevel = GetUpgradeLevel(upgradeType);
				int newLevel = currentLevel + 1;
				var cost = _game.Config.GetUpgradeCost(upgradeType, currentLevel, _locationTemplate.PrimaryGemType);

				// Update local state
				_globalData.SpendGems(cost);
				SetUpgradeLevel(upgradeType, newLevel);

				// Emit event to backend
				if (_game._miningEventService != null)
				{
					var locationId = _game._locationManager?.CurrentLocationId ?? "default";
					_ = _game._miningEventService.EmitUpgradePurchaseAsync(upgradeType, newLevel, cost, locationId);
				}

				return true;
			}
			
			public MiningLocationData GetLocationData() => _locationTemplate;
			public MiningGlobalDataStore GetGlobalData() => _globalData;

			public void ClearAllState()
			{
				// Clear all user-specific state data
				_pendingGems = 0;
				_upgradeLevels.Clear();
				_globalData = null;
				_locationTemplate = null;
				// Don't reset _firstTimeBonus - it will be set correctly by LoadUserDataAsync based on database state

				// Reset calculated values cache
				InvalidateCache();

				GD.Print("[GameState] All state cleared - ready for new user");
			}

		/// <summary>
		/// Helper method to emit mining tick events with error handling
		/// </summary>
		private async Task EmitMiningTickSafeAsync(string locationId, int pendingGems)
		{
			try
			{
				var result = await _game._miningEventService.EmitMiningTickAsync(locationId, pendingGems);
				if (!result.IsSuccess)
				{
					GD.PrintErr($"[GameState] Failed to emit mining tick: {result.Error}");
				}
			}
			catch (System.Exception ex)
			{
				GD.PrintErr($"[GameState] Error emitting mining tick: {ex.Message}");
			}
		}

		}
	}
}
