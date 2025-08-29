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

		// Platform services
		private GameHost _gameHost;
		private UserManager _userManager;
		private LocationManager _locationManager;

		// Context detection
		private bool _isProductionContext;
		private string _playerId;
		private UserData _currentUser;
		
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
				_playerId = playerSession?.PlayerId ?? _gameHost.GetCurrentPlayerId();
			}
			else
			{
				_isProductionContext = false;
				_playerId = "dev_player";
				SetupDevelopmentMode();
			}
			
			// Ensure MainController exists for logout functionality when loading scene directly
			if (isDirectSceneLoad)
			{
				EnsureMainControllerExists();
			}
			
			// Context detected
		}
		
		private void SetupDevelopmentMode()
		{
			EnableDebugMode = true;
			
			// Auto-login debug user
			if (_userManager != null)
			{
				_userManager.LoginUserDevelopment("dev_player");
				_currentUser = _userManager.GetCurrentUser();
				
				// Give debug user some starting gems for testing
				if (_currentUser != null)
				{
					_userManager.AddCredits(DEBUG_STARTING_CREDITS);
				}
			}
			
			// Development mode initialized
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
				var mainScene = GD.Load<PackedScene>("res://_Scenes/Main.tscn");
				
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
				
				// Get current user state - but don't block initialization if temporarily null
				_currentUser = _userManager?.GetCurrentUser();
				
				// In production context, we need a valid user to proceed with actual game functionality
				if (_currentUser == null && _isProductionContext)
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
				
				// Load user data first, then start mining after data is ready
				if (_currentUser != null)
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
				// Use discard pattern for intentional fire-and-forget async call to avoid CS4014 warning
				// Game end should not block on save completion - errors are handled within SaveDataAsync
				_ = _state.SaveDataAsync(); // Fire-and-forget
				
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
				_currentUser = _userManager?.GetCurrentUser();
				if (_currentUser != null)
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
		public int GetMaxCreditCharges() => _state?.GetMaxCreditCharges() ?? 0;
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
				var fallback = _locationDataRegistry.Values.First();
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
		
		// ================================================================
		// EVENT HANDLERS
		// ================================================================
		
		private void OnUserLoggedIn(UserData userData)
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
					
				_currentUser = userData;
				
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
				
				GD.Print($"[MiningGame] User logged in: {userData.UserId}");
			}
			finally
			{
				_isProcessingUserChange = false;
			}
		}
		
		private void OnUserLoggedOut()
		{
			if (_isProcessingUserChange)
			{
				GD.PrintErr("[MiningGame] User change already in progress, ignoring logout event");
				return;
			}
			
			_isProcessingUserChange = true;
			
			try
			{
				// Save current data before stopping game
				if (GodotObject.IsInstanceValid(_state))
					// Use discard pattern for intentional fire-and-forget async call to avoid CS4014 warning
					// User switching should not block on save completion - errors are handled within SaveDataAsync
					_ = _state.SaveDataAsync(); // Fire-and-forget
					
				StopGame();
				
				// Clear all state to ensure no previous user data remains
				if (GodotObject.IsInstanceValid(_state))
					_state.ClearAllState();
					
				_currentUser = null;
				
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
					// Use discard pattern for intentional fire-and-forget async call to avoid CS4014 warning
					// Node cleanup should not block on save completion - errors are handled within SaveDataAsync
					_ = _state.SaveDataAsync(); // Fire-and-forget
				
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
			private int _cachedMaxCreditCharges;
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
					_ = SaveDataAsync(); // Fire-and-forget
					_lastSaveTime = _gameTime;
					
					if (_game.EnableDebugMode)
					{
						GD.Print("[GameState] Auto-save completed");
					}
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
			
			public int GetMaxCreditCharges()
			{
				RefreshCacheIfNeeded();
				return _cachedMaxCreditCharges;
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
					_cachedMaxCreditCharges = _game.Config.GetMaxCreditCharges(GetUpgradeLevel(UpgradeType.CreditCharges));
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
				
				// Initialize timestamp if not set (first run or after first-time bonus)
				if (_lastMiningTickTime == default(DateTime))
				{
					_lastMiningTickTime = DateTime.UtcNow;
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
					
					if (_game.EnableDebugMode)
					{
						GD.Print($"[GameState] Processed {actualTicks} mining ticks: +{actualTicks * gemsPerTick} gems, Total: {_pendingGems}/{maxCapacity}");
					}
					
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
				
				if (_game.EnableDebugMode)
				{
					GD.Print("[GameState] Mining timer reset to current time");
				}
			}
			
			
			public async Task LoadUserDataAsync()
			{
				// Get location template first
				string currentLocationId = _game._locationManager?.CurrentLocationId ?? "default";
				_locationTemplate = _game.GetLocationDataTemplate(currentLocationId);
				
				string userId = _game._currentUser?.UserId;
				if (string.IsNullOrEmpty(userId))
				{
					CreateDefaultState();
					return;
				}
				
				var dataStore = DataStore.GetInstance();
				if (dataStore == null)
				{
					GD.PrintErr("[MiningGame] DataStore not available, using default state");
					CreateDefaultState();
					return;
				}
				
				try
				{
					// Load location-specific runtime state using DataStore extensions
					var miningData = await DataStoreExtensions.GetMiningDataAsync(dataStore, userId);
					
					// Create state from loaded data
					var savedState = new MiningLocationState
					{
						LocationId = currentLocationId,
						PendingGems = miningData.LocalGemsReady,
						LastMiningTickTime = miningData.LastMiningTick,
						// FirstTimeBonus will be determined below based on actual usage
					};
					
					// Store upgrade levels directly as strings (already in correct format)
					foreach (var upgrade in miningData.Upgrades)
					{
						savedState.UpgradeLevels[upgrade.Key] = upgrade.Value;
					}
					
					if (!string.IsNullOrEmpty(savedState.LocationId))
					{
						_pendingGems = savedState.PendingGems;
						_lastMiningTickTime = savedState.LastMiningTickTime;
						
						// Convert string upgrade levels back to enum dictionary
						_upgradeLevels.Clear();
						foreach (var upgrade in savedState.UpgradeLevels)
						{
							if (Enum.TryParse<UpgradeType>(upgrade.Key, out var upgradeType))
							{
								_upgradeLevels[upgradeType] = upgrade.Value;
							}
						}
						
						// Determine if this is truly first time based on actual game state
						// Check if user has ever actually played (has gems or upgrades)
						bool hasPlayedBefore = savedState.PendingGems > 0 || savedState.UpgradeLevels.Count > 0;
						_firstTimeBonus = !hasPlayedBefore;
						
						// Apply first-time bonus if this is truly the first time
						if (_firstTimeBonus && _locationTemplate != null)
						{
							bool hasValidUser = _game._currentUser != null && !string.IsNullOrEmpty(_game._currentUser.UserId);
							if (hasValidUser)
							{
								_pendingGems = _locationTemplate.GetMaxCapacity(0, _game.Config); // Start at level 0
								_firstTimeBonus = false; // Mark as used
								
								// Don't start mining timer when at full capacity from first-time bonus
								// Timer will start after first extraction
								_lastMiningTickTime = default(DateTime);
								
								if (_game.EnableDebugMode)
								{
									GD.Print($"[GameState] Applied first-time bonus: {_pendingGems} gems at location '{currentLocationId}' - mining timer paused");
								}
							}
						}
					}
					else
					{
						CreateDefaultState();
						try
						{
							await SaveLocationStateAsync();
						}
						catch (System.Exception ex)
						{
							// Thread-safe error logging via CallDeferred to main game class
							_game.CallDeferred(MiningGame.MethodName.LogAsyncError, $"Failed to save initial location state: {ex.Message}");
						}
					}
					
					// Load global mining data using DataStore
					var globalResult = await dataStore.GetGlobalDataAsync(userId);
					if (globalResult.IsSuccess)
					{
						_globalData = new BarBox.Games.MiningGame.MiningGlobalDataStore();
						// Copy gems from DataStore format (already string-based)
						foreach (var gem in globalResult.Value.Mining.Gems)
						{
							_globalData.Gems[gem.Key] = gem.Value;
						}
					}
					else
					{
						_globalData = new BarBox.Games.MiningGame.MiningGlobalDataStore();
						// Save initial global data structure
						try
						{
							var newGlobalData = new DataStore.GlobalUserData
							{
								UserId = userId,
								Mining = new MiningGlobalDataStore() // DataStore version
							};
							await dataStore.SetGlobalDataAsync(userId, newGlobalData);
						}
						catch (Exception ex)
						{
							// Thread-safe error logging via CallDeferred to main game class
							_game.CallDeferred(MiningGame.MethodName.LogAsyncError, $"Failed to save initial global data: {ex.Message}");
						}
					}
				}
				catch (System.Exception ex)
				{
					// Thread-safe error logging via CallDeferred to main game class
					_game.CallDeferred(MiningGame.MethodName.LogAsyncError, $"Failed to load user data: {ex.Message}");
					_game.CallDeferred(MiningGame.MethodName.LogAsyncError, "Falling back to default state");
					CreateDefaultState();
				}
				
				// Process any mining ticks that are ready (handles offline progress automatically)
				ProcessReadyMiningTicks();
				
				// Cleanup expired credit timers
				_globalData.CleanupExpiredTimers(_gameTime);
				
				// Invalidate cache so it gets rebuilt with new data
				InvalidateCache();
			}
			
			private void CreateDefaultState()
			{
				_pendingGems = 0;
				_upgradeLevels = new Dictionary<UpgradeType, int>();
				_firstTimeBonus = true;
				
				// Check if we have a valid user - only give bonuses for actual users
				bool hasValidUser = _game._currentUser != null && !string.IsNullOrEmpty(_game._currentUser.UserId);
				
				// First-time bonus: only for actual logged-in users, not "no user" state
				if (_locationTemplate != null && _firstTimeBonus && hasValidUser)
				{
					_pendingGems = _locationTemplate.GetMaxCapacity(0, _game.Config); // Start at level 0
					_firstTimeBonus = false;
					
					// Don't start mining timer when at full capacity from first-time bonus
					_lastMiningTickTime = default(DateTime);
				}
				
				_globalData = new MiningGlobalDataStore();
				
				// Debug resources: only for actual users in debug mode, not "no user" state
				if (_game.EnableDebugMode && hasValidUser)
				{
					// Give some debug resources
					_globalData.AddGems(GemType.Amethyst, 500);
					_globalData.AddGems(GemType.Diamond, 200);
				}
				
				if (_game.EnableDebugMode)
				{
					GD.Print($"[GameState] Default state created - User: {(hasValidUser ? _game._currentUser.UserId : "None")}, " +
						$"StartingGems: {_pendingGems}, DebugResources: {(hasValidUser ? "Added" : "Skipped")}");
				}
			}
			
			private async Task SaveLocationStateAsync()
			{
				string userId = _game._currentUser?.UserId;
				if (string.IsNullOrEmpty(userId)) 
				{
					return;
				}
				
				var dataStore = DataStore.GetInstance();
				if (dataStore == null) 
				{
					return;
				}
				
				// Create DataStore-compatible local data using modern C# enum conversion
				var miningLocalData = new MiningLocalData
				{
					LocalGemsReady = _pendingGems,
					LastMiningTick = _lastMiningTickTime,
					LocationGemType = _locationTemplate?.PrimaryGemType.ToString() ?? nameof(GemType.Amethyst),
					GemCapacity = GetMaxCapacity(),
					Upgrades = _upgradeLevels.ToDictionary(
						kvp => kvp.Key.ToString(), // Modern enum to string
						kvp => kvp.Value
					),
					CreditTimers = new List<CreditPurchaseTimer>() // Simplified - no complex conversion needed
				};
				
				try
				{
					// Use DataStore extension for clean save operation
					await DataStoreExtensions.SetMiningDataAsync(dataStore, userId, miningLocalData);
				}
				catch (System.Exception ex)
				{
					// Thread-safe error logging via CallDeferred to main game class
					_game.CallDeferred(MiningGame.MethodName.LogAsyncError, $"Failed to save location state: {ex.Message}");
					throw; // Re-throw to let caller handle
				}
			}

			public bool CanExtractGems() => IsExtractionReady;
			
			public bool CanPurchaseCredit()
			{
				if (_globalData == null || _locationTemplate == null) return false;
				
				var cost = new Dictionary<GemType, int>
				{
					{ _locationTemplate.PrimaryGemType, _game.Config.CreditCost }
				};
				
				// Check if we haven't exceeded max credit charges
				int maxCharges = GetMaxCreditCharges();
				int activeCharges = 0;
				
				// Count non-recharged timers
				foreach (var timer in _globalData.CreditTimers)
				{
					if (!timer.IsRecharged(_gameTime))
						activeCharges++;
				}
				
				if (activeCharges >= maxCharges)
				{
					GD.Print($"[GameState] Max credit charges reached: {activeCharges}/{maxCharges}");
					return false;
				}
				
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
				
				_globalData.AddGems(_locationTemplate.PrimaryGemType, _pendingGems);
				_pendingGems = 0;
				
				// Only reset timer if mining was paused at capacity
				// If mining was actively progressing, preserve the timer progress
				if (wasAtCapacity)
				{
					ResetMiningTimer(); // Start fresh cycle since mining was paused
				}
				// else: keep current timer progress for continuous mining
				
				_ = SaveDataAsync(); // Fire-and-forget
				return true;
			}
			
			public bool PurchaseCredit()
			{
				if (!CanPurchaseCredit() || _locationTemplate == null) return false;
				
				var cost = new Dictionary<GemType, int>
				{
					{ _locationTemplate.PrimaryGemType, _game.Config.CreditCost }
				};
				_globalData.SpendGems(cost);
				
				// Add credit timer using game time
				var timer = new GameTimeCreditTimer
				{
					PurchaseGameTime = _gameTime,
					RechargeGameTime = _gameTime + (_game.Config.CreditRechargeHours * SECONDS_PER_HOUR)
				};
				_globalData.CreditTimers.Add(timer);
				
				// Actually add credit to user account
				if (_game._currentUser != null)
				{
					_game._userManager?.AddCredits(CREDITS_PER_PURCHASE);
				}
				
				_ = SaveDataAsync(); // Fire-and-forget
				return true;
			}
			
			public bool PurchaseUpgrade(UpgradeType upgradeType)
			{
				if (!CanPurchaseUpgrade(upgradeType) || _locationTemplate == null) return false;
				
				int currentLevel = GetUpgradeLevel(upgradeType);
				var cost = _game.Config.GetUpgradeCost(upgradeType, currentLevel, _locationTemplate.PrimaryGemType);
				
				_globalData.SpendGems(cost);
				SetUpgradeLevel(upgradeType, currentLevel + 1);
				
				_ = SaveDataAsync(); // Fire-and-forget
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
				_firstTimeBonus = true;
				
				// Reset calculated values cache
				InvalidateCache();
				
				// Reset timestamps
				
				if (_game.EnableDebugMode)
				{
					GD.Print("[GameState] All state cleared - ready for new user or no-user state");
				}
			}

			public async Task SaveDataAsync()
			{
				string userId = _game._currentUser?.UserId;
				if (string.IsNullOrEmpty(userId)) 
					return;
				
				var dataStore = DataStore.GetInstance();
				if (dataStore == null) 
					return;
				
				try
				{
					// Save local data (location-specific)
					await SaveLocationStateAsync();
					
					// Save global data directly using DataStore
					// For now, we'll create a minimal global data save (gems will be saved when extracted)
					var globalResult = await dataStore.GetGlobalDataAsync(userId);
					if (globalResult.IsSuccess)
					{
						// Global data already exists, just ensure Mining section is initialized
						if (globalResult.Value.Mining == null)
						{
							globalResult.Value.Mining = new MiningGlobalDataStore();
						}
						await dataStore.SetGlobalDataAsync(userId, globalResult.Value);
					}
					else
					{
						// Create new global data structure
						var newGlobalData = new DataStore.GlobalUserData
						{
							UserId = userId,
							Mining = new MiningGlobalDataStore()
						};
						await dataStore.SetGlobalDataAsync(userId, newGlobalData);
					}
					
					if (_game.EnableDebugMode)
					{
						GD.Print("[GameState] Data saved");
					}
				}
				catch (System.Exception ex)
				{
					// Thread-safe error logging via CallDeferred to main game class
					_game.CallDeferred(MiningGame.MethodName.LogAsyncError, $"Failed to save data: {ex.Message}");
				}
			}
		}
	}
}