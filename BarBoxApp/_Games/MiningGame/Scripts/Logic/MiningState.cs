using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace BarBox.Games.MiningGame.Logic;

public partial class MiningState : Node
{
#region Fields

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

	// Constants
	private const string DEFAULT_LOCATION_ID = "default";
	private const int CREDITS_PER_PURCHASE = 1;

#endregion

#region Constructor & Lifecycle

	public MiningState(MiningGame game)
	{
		_game = game;
		Name = "State";
	}

	public override void _Process(double delta)
	{
		_gameTime += delta;

		// Auto-save every 30 seconds (replaces save timer)
		if (!(_gameTime - _lastSaveTime >= AUTO_SAVE_INTERVAL))
			return;

		_lastSaveTime = _gameTime;

		// Emit mining tick event to backend
		if (_game.GetEventService() != null)
		{
			var locationId = _game.GetLocationManager()?.CurrentLocationId ?? DEFAULT_LOCATION_ID;
			_ = EmitMiningTickSafeAsync(locationId, _pendingGems);
		}

		if (_game.IsDebugMode())
			GD.Print("[GameState] Auto-save: tick event emitted");
	}

#endregion

#region Public API - Properties
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
		return _upgradeLevels.GetValueOrDefault(upgradeType, 0);
	}

	public void SetUpgradeLevel(UpgradeType upgradeType, int level)
	{
		_upgradeLevels[upgradeType] = level;
		InvalidateCache();
	}

#endregion

#region Cache Management

	private void RefreshCacheIfNeeded()
	{
		if (_cacheValid || _locationTemplate == null)
			return;

		_cachedMaxCapacity = _locationTemplate.GetMaxCapacity(GetUpgradeLevel(UpgradeType.Capacity), _game.GetConfig());
		_cachedMiningTickTime = _locationTemplate.GetMiningTickTime(GetUpgradeLevel(UpgradeType.MiningSpeed), _game.GetConfig());
		_cachedGemsPerTick = _locationTemplate.GetGemsPerTick(GetUpgradeLevel(UpgradeType.MiningAmount), _game.GetConfig());
		_cacheValid = true;
	}

	private void InvalidateCache()
	{
		_cacheValid = false;
	}

#endregion

#region Mining Calculations
		
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

			if (_game.IsDebugMode())
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

		if (_game.IsDebugMode())
			GD.Print("[GameState] Mining timer reset to current time");
	}

#endregion

#region Data Loading

	public async Task LoadUserDataAsync()
	{
		if (_game.IsDebugMode())
			GD.Print("[GameState] === LoadUserDataAsync START ===");

		// Get location template first
		string currentLocationId = _game.GetLocationManager()?.CurrentLocationId ?? DEFAULT_LOCATION_ID;
		_locationTemplate = _game.GetLocationDataTemplate(currentLocationId);

		// Validate location template
		if (_locationTemplate == null)
		{
			GD.PrintErr($"[GameState] CRITICAL: Could not load location template for '{currentLocationId}'");
			CreateDefaultState();
			return;
		}

		// Get current user phone number
		var phoneNumber = _game.GetCurrentUserPhoneNumber();

		if (!string.IsNullOrEmpty(phoneNumber))
		{
			// Get player ID from session manager
			var sessionManager = SessionManager.GetInstance();
			var currentSession = sessionManager?.GetPrimaryUserSession();

			if (currentSession?.PlayerId != null && currentSession.PlayerId != Guid.Empty)
			{
				var playerId = currentSession.PlayerId;

				// Check backend health before attempting to load
				var backendManager = BackendManager.GetInstance();
				bool isBackendHealthy = backendManager?.IsBackendRunning() ?? false;

				if (!isBackendHealthy)
				{
					if (GameHost.IsProductionContext())
					{
						// Production: Backend is REQUIRED for logged-in users
						GD.PrintErr("[GameState] CRITICAL ERROR: Backend is unavailable in production mode!");
						GD.PrintErr("[GameState] Cannot load user data without backend connection.");
						GD.PrintErr("[GameState] Please ensure the backend service is running and healthy.");

						// Show error to user
						_game.GetUI()?.ShowError(
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

		#endregion

#region Backend State Loading

	/// <summary>
	/// Attempt to load state from backend using event aggregation
	/// </summary>
	private async Task<bool> TryLoadStateFromBackend(Guid playerId, string locationId)
	{
		GD.Print($"[GameState] === TryLoadStateFromBackend START === Player: {playerId}, Location: {locationId}");

		try
		{
			// Load global inventory (gems extracted - gems spent)
			var inventoryResult = await _game.GetEventService().GetPlayerInventoryAsync(playerId);
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
			var upgradesResult = await _game.GetEventService().GetPlayerUpgradesAsync(playerId);
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
			var timestampResult = await _game.GetEventService().GetPlayerMiningTimestampAsync(playerId, locationId);
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
			var metadataResult = await _game.GetEventService().GetPlayerMetadataAsync(playerId);
			if (metadataResult.IsSuccess)
			{
				_firstTimeBonus = !metadataResult.Value.HasReceivedBonus;

				GD.Print($"[GameState] Loaded metadata: HasBonus={metadataResult.Value.HasReceivedBonus}, TotalEvents={metadataResult.Value.TotalEvents}");
			}

			// Initialize pending gems to 0 (location-specific state starts fresh)
			_pendingGems = 0;

			// Grant first-time bonus if eligible
			if (_firstTimeBonus)
			{
				var maxCapacity = GetMaxCapacity();
				_pendingGems = maxCapacity; // Grant full capacity of gems

				GD.Print($"[GameState] First-time bonus granted: {_pendingGems} gems");

				// Emit event to backend to mark bonus as received
				if (_game.GetEventService() != null)
				{
					var bonusResult = await _game.GetEventService().EmitFirstTimeBonusAsync(
						playerId,
						locationId,
						_pendingGems
					);

					if (bonusResult.IsSuccess)
					{
						_firstTimeBonus = false; // Mark as received locally
						GD.Print("[GameState] First-time bonus event emitted successfully");
					}
					else
					{
						GD.PrintErr($"[GameState] Failed to emit first-time bonus event: {bonusResult.Error}");
						// Keep _firstTimeBonus=true so we retry on next login
					}
				}
			}

			return true;
		}
		catch (Exception ex)
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

#endregion

#region Transaction Logic

	public bool CanExtractGems() => IsExtractionReady;
		
	public bool CanPurchaseCredit()
	{
		if (_globalData == null || _locationTemplate == null) return false;

		var cost = new Dictionary<GemType, int>
		{
			{ _locationTemplate.PrimaryGemType, _game.GetConfig().CreditCost }
		};

		return _globalData.HasSufficientGems(cost);
	}
		
	public bool CanPurchaseUpgrade(UpgradeType upgradeType)
	{
		if (_locationTemplate == null || _globalData == null) return false;
			
		int currentLevel = GetUpgradeLevel(upgradeType);
		if (currentLevel >= _game.GetConfig().MaxUpgradeLevel)
			return false;

		var cost = _game.GetConfig().GetUpgradeCost(upgradeType, currentLevel, _locationTemplate.PrimaryGemType);
		return _globalData.HasSufficientGems(cost);
	}
		
	public bool ExtractGems()
	{
		if (!CanExtractGems() || _locationTemplate == null) 
			return false;

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

		// Emit event to backend (fire and forget)
		if (_game.GetEventService() != null)
		{
			_ = _game.GetEventService().EmitExtractCompleteAsync(gemType, extractedAmount);
		}

		return true;
	}
		
	public bool PurchaseCredit()
	{
		if (!CanPurchaseCredit() || _locationTemplate == null) return false;

		var cost = new Dictionary<GemType, int>
		{
			{ _locationTemplate.PrimaryGemType, _game.GetConfig().CreditCost }
		};

		// Update local state
		_globalData.SpendGems(cost);

		// Actually add credit to user account
		if (!string.IsNullOrEmpty(_game.GetCurrentUserPhoneNumber()))
		{
			_game.GetUserManager()?.AddCredits(CREDITS_PER_PURCHASE);
		}

		// Emit event to backend
		if (_game.GetEventService() != null)
		{
			_ = _game.GetEventService().EmitCreditDepositAsync(_locationTemplate.PrimaryGemType, _game.GetConfig().CreditCost, CREDITS_PER_PURCHASE);
		}

		return true;
	}
		
	public bool PurchaseUpgrade(UpgradeType upgradeType)
	{
		if (!CanPurchaseUpgrade(upgradeType) || _locationTemplate == null) return false;

		int currentLevel = GetUpgradeLevel(upgradeType);
		int newLevel = currentLevel + 1;
		var cost = _game.GetConfig().GetUpgradeCost(upgradeType, currentLevel, _locationTemplate.PrimaryGemType);

		// Update local state
		_globalData.SpendGems(cost);
		SetUpgradeLevel(upgradeType, newLevel);

		// Emit event to backend
		if (_game.GetEventService() != null)
		{
			var locationId = _game.GetLocationManager()?.CurrentLocationId ?? "default";
			_ = _game.GetEventService().EmitUpgradePurchaseAsync(upgradeType, newLevel, cost, locationId);
		}

		return true;
	}

		#endregion

#region State Management

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

#endregion

#region Helper Methods

	/// <summary>
	/// Helper method to emit mining tick events with error handling
	/// </summary>
	private async Task EmitMiningTickSafeAsync(string locationId, int pendingGems)
	{
		try
		{
			var result = await _game.GetEventService().EmitMiningTickAsync(locationId, pendingGems);
			if (!result.IsSuccess)
			{
				GD.PrintErr($"[GameState] Failed to emit mining tick: {result.Error}");
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[GameState] Error emitting mining tick: {ex.Message}");
		}
	}

#endregion
}