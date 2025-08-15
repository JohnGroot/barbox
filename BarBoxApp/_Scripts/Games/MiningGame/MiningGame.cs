using System;
using System.Collections.Generic;
using System.Linq;
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
			_locationManager = LocationManager.GetAutoload<LocationManager>();
			
			if (_gameHost != null && GodotObject.IsInstanceValid(_gameHost))
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
			
			GD.Print($"[MiningGame] Context: {(_isProductionContext ? "Production" : "Development")}");
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
			
			GD.Print("[MiningGame] Development mode initialized");
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
						GD.Print($"[MiningGame] Registered location data for: {locationData.LocationId}");
					}
				}
				
				GD.Print($"[MiningGame] Location data registry initialized with {_locationDataRegistry.Count} locations");
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
						GD.Print($"[MiningGame] Loaded config for location: {locationId}");
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
			_currentUser = _userManager?.GetCurrentUser();
			if (_currentUser == null && _isProductionContext)
			{
				GD.PrintErr("[MiningGame] Cannot start: No user logged in");
				return;
			}
			
			if (GodotObject.IsInstanceValid(_state))
				_state.LoadUserData();
				
			if (GodotObject.IsInstanceValid(_engine))
				_engine.StartMining();
			
			// Enable UI LAST, after all components are ready
			if (GodotObject.IsInstanceValid(_ui))
			{
				_ui.SetEnabled(true);
				_ui.UpdateAllUI(); // Force immediate UI refresh
			}
			
			GD.Print($"[MiningGame] Game initialized for player: {_playerId}");
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
						_ui.SetEnabled(false);
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
			
			// Log error for missing location
			GD.PrintErr($"[MiningGame] ERROR: No location data found for '{locationId}'. Available locations: {string.Join(", ", _locationDataRegistry.Keys)}");
			
			// Fall back to first available location
			if (_locationDataRegistry.Count > 0)
			{
				var fallback = _locationDataRegistry.Values.First();
				GD.PrintErr($"[MiningGame] Using fallback location '{fallback.LocationId}' instead of '{locationId}'");
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
				_currentUser = userData;
				
				// Load user data first
				if (GodotObject.IsInstanceValid(_state))
					_state.LoadUserData();
				
				// Ensure game is properly reset before starting
				if (IsGameActive())
				{
					EndGame();
				}
				
				StartGame();
				
				// Ensure game session is initialized
				if (GodotObject.IsInstanceValid(_engine))
				{
					InitializeGameSession();
				}
				
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
				_currentUser = null;
				GD.Print("[MiningGame] User logged out");
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
			private const double AUTO_SAVE_INTERVAL = 30.0;
			
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
			
			public void LoadUserData()
			{
				// Get location template first
				string currentLocationId = _game._locationManager?.CurrentLocationId ?? "default";
				_locationTemplate = _game.GetLocationDataTemplate(currentLocationId);
				
				if (_game._currentUser == null)
				{
					CreateDefaultState();
					return;
				}
				
				// Load location-specific runtime state
				string locationKey = LOCATION_STATE_PREFIX + currentLocationId;
				var savedState = LoadFromUserMeta<MiningLocationState>(locationKey);
				
				if (savedState != null)
				{
					_pendingGems = savedState.PendingGems;
					_upgradeLevels = savedState.UpgradeLevels ?? new();
					_firstTimeBonus = savedState.FirstTimeBonus;
				}
				else
				{
					CreateDefaultState();
					SaveLocationState();
				}
				
				// Load global mining data
				_globalData = LoadFromUserMeta<MiningGlobalData>(GLOBAL_DATA_KEY);
				if (_globalData == null)
				{
					_globalData = new MiningGlobalData();
					SaveToUserMeta(GLOBAL_DATA_KEY, _globalData);
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
				
				// First-time bonus: max out capacity
				if (_locationTemplate != null && _firstTimeBonus)
				{
					_pendingGems = _locationTemplate.GetMaxCapacity(0, _game.Config); // Start at level 0
					_firstTimeBonus = false;
				}
				
				_globalData = new MiningGlobalData();
				
				if (_game.EnableDebugMode)
				{
					// Give some debug resources
					_globalData.AddGems(GemType.Amethyst, 500);
					_globalData.AddGems(GemType.Diamond, 200);
				}
			}
			
			private void SaveLocationState()
			{
				if (_game._currentUser == null) return;
				
				var state = new MiningLocationState
				{
					LocationId = _game._locationManager?.CurrentLocationId ?? "default",
					PendingGems = _pendingGems,
					UpgradeLevels = _upgradeLevels,
					FirstTimeBonus = _firstTimeBonus
				};
				
				string locationKey = LOCATION_STATE_PREFIX + state.LocationId;
				SaveToUserMeta(locationKey, state);
			}
			
			
			private void ProcessOfflineProgress()
			{
				if (_locationTemplate == null) return;
				
				// For prototyping: Simple offline progress without DateTime
				// Just give a fixed bonus when loading
				int offlineBonus = GetMaxCapacity() / 2; // Half capacity as offline bonus
				_pendingGems = Math.Min(_pendingGems + offlineBonus, GetMaxCapacity());
				
				if (offlineBonus > 0)
				{
					GD.Print($"[GameState] Offline bonus: +{offlineBonus} {_locationTemplate.PrimaryGemType} gems");
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
			
			public void SaveData()
			{
				if (_game._currentUser == null) return;
				
				SaveLocationState();
				SaveToUserMeta(GLOBAL_DATA_KEY, _globalData);
				
				if (_game.EnableDebugMode)
				{
					GD.Print("[GameState] Data saved");
				}
			}
			
			private T LoadFromUserMeta<[MustBeVariant] T>(string key) where T : Resource
			{
				if (_game._currentUser?.HasMeta(key) == true)
				{
					var variant = _game._currentUser.GetMeta(key);
					if (variant.VariantType == Variant.Type.Object)
					{
						return variant.As<T>();
					}
				}
				return null;
			}
			
			private void SaveToUserMeta<[MustBeVariant] T>(string key, T data) where T : Resource
			{
				_game._currentUser?.SetMeta(key, data);
			}
		}
	}
}