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
					_userManager.AddCredits(5);
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
		
		private void InitializeGameSession()
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
			
			// Load user data and start mining - data should already be loaded by OnUserLoggedIn
			// This is a safety check for development mode or edge cases
			if (_currentUser != null)
			{
				_state.LoadUserData();
			}
			_engine.StartMining();
			
			// Enable UI after all components are ready
			if (GodotObject.IsInstanceValid(_ui))
			{
				_ui.SetEnabled(true);
				_ui.UpdateAllUI(); // Force immediate UI refresh
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
				_state.SaveData();
				
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
		public MiningGlobalData GetGlobalData() => _state?.GetGlobalData();
		
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
				// If we extracted from full capacity, reset the mining timer
				if (wasAtCapacity && GodotObject.IsInstanceValid(_engine))
				{
					_engine.ResetTimerAfterExtraction();
				}
				
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
					switch (upgradeType)
					{
						case UpgradeType.MiningSpeed:
							// Speed changed - just update the mining interval without triggering tick
							_engine.UpdateMiningInterval();
							break;
							
						case UpgradeType.Capacity:
							// Capacity increased - resume mining if we were at max capacity before
							var currentGems = _state.PendingGems;
							var locationTemplate = _state.GetLocationData();
							var oldMaxCapacity = currentLevel > 0 ? 
								locationTemplate?.GetMaxCapacity(currentLevel - 1, Config) ?? Config.BaseCapacity : 
								Config.BaseCapacity;
							
							// Only trigger tick if we were previously at max capacity (mining was stopped)
							if (currentGems >= oldMaxCapacity && currentGems < _state.GetMaxCapacity())
							{
								// Resume mining without adding extra gems - just reset the timer
								_engine.ResumeMining();
							}
							break;
							
						// MiningAmount and CreditCharges don't need immediate effects
					}
				}
			}
		}
		
		// Direct UI updates - no signal overhead
		public void UpdateUI()
		{
			if (GodotObject.IsInstanceValid(_ui))
				_ui.UpdateAllUI();
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
					_state.SaveData();
					
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
					_state.SaveData();
				
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
			private double _miningAccumulator = 0.0;
			private double _miningInterval;
			private bool _isMiningActive = false;
			
			public GameEngine(MiningGame game)
			{
				_game = game;
				Name = "Engine";
			}
			
			public override void _Process(double delta)
			{
				if (!_isMiningActive) return;
				
				// Check if we're at full capacity - if so, don't increment timer
				if (GodotObject.IsInstanceValid(_game._state))
				{
					int currentGems = _game._state.PendingGems;
					int maxCapacity = _game._state.GetMaxCapacity();
					
					if (currentGems >= maxCapacity)
					{
						// At capacity - timer should not progress
						return;
					}
				}
				
				_miningAccumulator += delta;
				
				if (_miningAccumulator >= _miningInterval)
				{
					ProcessMiningTick();
					_miningAccumulator = 0.0;
					UpdateMiningInterval(); // Refresh in case upgrades changed it
				}
			}
			
			private void ProcessMiningTick()
			{
				if (GodotObject.IsInstanceValid(_game._state))
					_game._state.ProcessMiningTick();
					
				if (GodotObject.IsInstanceValid(_game._ui))
					_game._ui.UpdateMiningProgress();
					
				if (_game.EnableDebugMode)
				{
					var state = _game._state;
					if (state != null && GodotObject.IsInstanceValid(state))
					{
						GD.Print($"[GameEngine] Mining tick: +{state.GetGemsPerTick()} gems, " +
								$"Total: {state.PendingGems}/{state.GetMaxCapacity()}");
					}
				}
			}
			
			public void StartMining()
			{
				UpdateMiningInterval();
				_miningAccumulator = 0.0;
				_isMiningActive = true;
				GD.Print("[GameEngine] Mining started");
			}
			
			public void StopMining()
			{
				_isMiningActive = false;
				GD.Print("[GameEngine] Mining stopped");
			}
			
			public void UpdateMiningInterval()
			{
				_miningInterval = _game._state?.GetMiningTickTime() ?? 7200.0;
				
				if (_game.EnableDebugMode)
				{
					GD.Print($"[GameEngine] Mining interval updated: {_miningInterval:F1} seconds");
				}
			}
			
			public void ResumeMining()
			{
				if (_isMiningActive)
				{
					// Reset the accumulator to restart the mining interval
					_miningAccumulator = 0.0;
					UpdateMiningInterval(); // Update interval in case upgrades changed it
					
					if (_game.EnableDebugMode)
					{
						GD.Print("[GameEngine] Mining resumed with reset timer");
					}
				}
			}
			
			public void ResetTimerAfterExtraction()
			{
				// Reset timer when extracting gems from full capacity
				_miningAccumulator = 0.0;
				UpdateMiningInterval();
				
				if (_game.EnableDebugMode)
				{
					GD.Print("[GameEngine] Timer reset after gem extraction");
				}
			}
			
			public void TriggerImmediateMiningTick()
			{
				if (_isMiningActive)
				{
					ProcessMiningTick();
					_miningAccumulator = 0.0; // Reset for next interval
				}
			}
			
			public float GetMiningProgress()
			{
				if (!_isMiningActive || _miningInterval <= 0) return 0.0f;
				return (float)(_miningAccumulator / _miningInterval);
			}
			
			public float GetTimeUntilNextTick()
			{
				if (!_isMiningActive) return 0.0f;
				return (float)(_miningInterval - _miningAccumulator);
			}
			
			public double GetCurrentAccumulator()
			{
				return _miningAccumulator;
			}
			
			public double GetCurrentInterval()
			{
				return _miningInterval;
			}
			
			public void SetAccumulator(double value)
			{
				_miningAccumulator = Math.Max(0.0, Math.Min(value, _miningInterval));
			}
		}
		
		public partial class GameState : Node
		{
			private MiningGame _game;
			private MiningLocationData _locationTemplate;
			private MiningGlobalData _globalData;
			
			// Runtime state data (what gets saved/loaded)
			private int _pendingGems = 0;
			private Dictionary<UpgradeType, int> _upgradeLevels = new();
			private bool _firstTimeBonus = true;
			
			// Cached calculated values
			private int _cachedMaxCapacity;
			private float _cachedMiningTickTime;
			private int _cachedGemsPerTick;
			private int _cachedMaxCreditCharges;
			private bool _cacheValid = false;
			
			// Game time management
			private double _gameTime = 0.0;
			private double _lastSaveTime = 0.0;
			private const double AUTO_SAVE_INTERVAL = 1.0;
			
			// Offline progress tracking
			private DateTime _lastSaveTimestamp = DateTime.UtcNow;
			
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
					SaveData();
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
			
			private DateTime GetLastSaveTime()
			{
				return _lastSaveTimestamp;
			}
			
			private void UpdateLastSaveTime()
			{
				_lastSaveTimestamp = DateTime.UtcNow;
			}
			
			public async void LoadUserData()
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
				
				// Load location-specific runtime state using DataStore extensions
				string locationKey = LOCATION_STATE_PREFIX + currentLocationId;
				var savedState = await dataStore.GetGameValueAsync<MiningLocationState>(userId, locationKey);
				
				if (savedState != null && !string.IsNullOrEmpty(savedState.LocationId))
				{
					_pendingGems = savedState.PendingGems;
					_upgradeLevels = savedState.UpgradeLevels ?? new();
					_firstTimeBonus = savedState.FirstTimeBonus;
					_lastSaveTimestamp = savedState.LastSaveTime;
				}
				else
				{
					CreateDefaultState();
					await SaveLocationStateAsync();
				}
				
				// Load global mining data using DataStore extensions
				_globalData = await dataStore.GetGameValueAsync<MiningGlobalData>(userId, GLOBAL_DATA_KEY);
				if (_globalData == null)
				{
					_globalData = new MiningGlobalData();
					await dataStore.SetGameValueAsync(userId, GLOBAL_DATA_KEY, _globalData);
				}
				
				// Process offline progress
				ProcessOfflineProgress();
				
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
				_lastSaveTimestamp = DateTime.UtcNow;
				
				// Check if we have a valid user - only give bonuses for actual users
				bool hasValidUser = _game._currentUser != null && !string.IsNullOrEmpty(_game._currentUser.UserId);
				
				// First-time bonus: only for actual logged-in users, not "no user" state
				if (_locationTemplate != null && _firstTimeBonus && hasValidUser)
				{
					_pendingGems = _locationTemplate.GetMaxCapacity(0, _game.Config); // Start at level 0
					_firstTimeBonus = false;
				}
				
				_globalData = new MiningGlobalData();
				
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
					await Task.CompletedTask;
					return;
				}
				
				var dataStore = DataStore.GetInstance();
				if (dataStore == null) 
				{
					await Task.CompletedTask;
					return;
				}
				
				var state = new MiningLocationState
				{
					LocationId = _game._locationManager?.CurrentLocationId ?? "default",
					PendingGems = _pendingGems,
					UpgradeLevels = _upgradeLevels,
					FirstTimeBonus = _firstTimeBonus,
					LastSaveTime = DateTime.UtcNow
				};
				
				string locationKey = LOCATION_STATE_PREFIX + state.LocationId;
				await dataStore.SetGameValueAsync(userId, locationKey, state);
			}

			private void ProcessOfflineProgress()
			{
				if (_locationTemplate == null) return;
				
				// No offline progress calculation if this is a new save or missing timestamp
				DateTime lastSaveTime = GetLastSaveTime();
				if (lastSaveTime == default(DateTime)) 
				{
					UpdateLastSaveTime();
					return;
				}
				
				DateTime currentTime = DateTime.UtcNow;
				double offlineSeconds = (currentTime - lastSaveTime).TotalSeconds;
				
				// Skip if no time passed or negative time (clock issues)
				if (offlineSeconds <= 0) 
				{
					UpdateLastSaveTime();
					return;
				}
				
				// Cap offline time to reasonable limit (7 days)
				const double maxOfflineSeconds = 7 * 24 * 3600.0;
				offlineSeconds = Math.Min(offlineSeconds, maxOfflineSeconds);
				
				// Calculate how many mining ticks would have occurred
				float miningTickTime = GetMiningTickTime();
				if (miningTickTime <= 0) return;
				
				// Include current timer accumulator for accurate offline calculation
				double totalOfflineTime = offlineSeconds;
				if (GodotObject.IsInstanceValid(_game._engine))
				{
					double currentAccumulator = _game._engine.GetCurrentAccumulator();
					totalOfflineTime += currentAccumulator; // Add progress toward next tick
				}
				
				int possibleTicks = (int)(totalOfflineTime / miningTickTime);
				if (possibleTicks <= 0) 
				{
					UpdateLastSaveTime();
					return;
				}
				
				// Simulate mining ticks up to capacity limit
				int maxCapacity = GetMaxCapacity();
				int gemsPerTick = GetGemsPerTick();
				int offlineGems;
				
				// For performance: if we can fit all gems, calculate directly
				int totalPossibleGems = possibleTicks * gemsPerTick;
				int availableCapacity = maxCapacity - _pendingGems;
				
				if (totalPossibleGems <= availableCapacity)
				{
					// All ticks fit within capacity
					_pendingGems += totalPossibleGems;
					offlineGems = totalPossibleGems;
				}
				else
				{
					// Calculate how many ticks until capacity is reached
					int ticksUntilCapacity = Math.Max(0, (availableCapacity + gemsPerTick - 1) / gemsPerTick);
					int actualTicks = Math.Min(possibleTicks, ticksUntilCapacity);
					
					offlineGems = actualTicks * gemsPerTick;
					_pendingGems = Math.Min(maxCapacity, _pendingGems + offlineGems);
				}
				
				UpdateLastSaveTime();
				
				// Reset engine accumulator to prevent double-counting after offline processing
				if (GodotObject.IsInstanceValid(_game._engine))
				{
					double remainingTime = totalOfflineTime - (possibleTicks * miningTickTime);
					// Set accumulator to remaining partial tick time
					_game._engine.SetAccumulator(Math.Max(0.0, remainingTime));
				}
				
				if (offlineGems > 0)
				{
					double hours = offlineSeconds / 3600.0;
					GD.Print($"[GameState] Offline mining: +{offlineGems} {_locationTemplate.PrimaryGemType} gems from {possibleTicks} ticks over {hours:F1} hours");
				}
			}
			
			public void ProcessMiningTick()
			{
				int maxCapacity = GetMaxCapacity();
				if (_pendingGems >= maxCapacity)
				{
					GD.Print("[GameState] Mining tick skipped - at max capacity");
					return;
				}
				
				int gemsToAdd = Math.Min(
					GetGemsPerTick(),
					maxCapacity - _pendingGems
				);
				
				_pendingGems += gemsToAdd;
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
				
				_globalData.AddGems(_locationTemplate.PrimaryGemType, _pendingGems);
				_pendingGems = 0;
				SaveData();
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
				var timer = new CreditPurchaseTimer
				{
					PurchaseGameTime = _gameTime,
					RechargeGameTime = _gameTime + (_game.Config.CreditRechargeHours * 3600.0)
				};
				_globalData.CreditTimers.Add(timer);
				
				// Actually add credit to user account
				if (_game._currentUser != null)
				{
					_game._userManager?.AddCredits(1);
				}
				
				SaveData();
				return true;
			}
			
			public bool PurchaseUpgrade(UpgradeType upgradeType)
			{
				if (!CanPurchaseUpgrade(upgradeType) || _locationTemplate == null) return false;
				
				int currentLevel = GetUpgradeLevel(upgradeType);
				var cost = _game.Config.GetUpgradeCost(upgradeType, currentLevel, _locationTemplate.PrimaryGemType);
				
				_globalData.SpendGems(cost);
				SetUpgradeLevel(upgradeType, currentLevel + 1);
				
				SaveData();
				return true;
			}
			
			public MiningLocationData GetLocationData() => _locationTemplate;
			public MiningGlobalData GetGlobalData() => _globalData;

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
				_lastSaveTimestamp = DateTime.UtcNow;
				
				if (_game.EnableDebugMode)
				{
					GD.Print("[GameState] All state cleared - ready for new user or no-user state");
				}
			}

			public async void SaveData()
			{
				string userId = _game._currentUser?.UserId;
				if (string.IsNullOrEmpty(userId)) 
					return;
				
				var dataStore = DataStore.GetInstance();
				if (dataStore == null) 
					return;
				
				await SaveLocationStateAsync();
				await dataStore.SetGameValueAsync(userId, GLOBAL_DATA_KEY, _globalData);
				
				if (_game.EnableDebugMode)
				{
					GD.Print("[GameState] Data saved");
				}
			}
		}
	}
}