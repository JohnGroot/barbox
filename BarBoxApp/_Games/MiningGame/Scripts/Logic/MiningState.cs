using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using BarBox.Core.Autoloads;

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

	// Constants
	private const int CREDITS_PER_PURCHASE = 1;

#endregion

#region Constructor & Lifecycle

	public MiningState(MiningGame game)
	{
		_game = game;
		Name = "State";
	}

	/// <summary>
	/// Initialize state with location data and configuration
	/// Must be called after construction before using the state
	/// </summary>
	public void Initialize()
	{
		// FAIL FAST: Require LocationManager
		var locationManager = _game.GetLocationManager();
		if (locationManager == null)
			throw new InvalidOperationException("LocationManager is required but not available");

		string currentLocationId = locationManager.VenueName;
		if (string.IsNullOrEmpty(currentLocationId))
			throw new InvalidOperationException("Venue name not configured - BARBOX_VENUE_NAME required");

		_locationTemplate = _game.GetLocationDataTemplate(currentLocationId);
		if (_locationTemplate == null)
			throw new InvalidOperationException(
				$"Location template for '{currentLocationId}' not found. Check MiningGame.tscn LocationDataResources configuration.");
	}

#endregion

#region Public API - Properties
	public int PendingGems => _pendingGems;
	public bool IsExtractionReady => _pendingGems > 0;
	public bool FirstTimeBonus => _firstTimeBonus;

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
		if (_cacheValid)
			return;

		var capacityLevel = GetUpgradeLevel(UpgradeType.Capacity);
		var speedLevel = GetUpgradeLevel(UpgradeType.MiningSpeed);
		var amountLevel = GetUpgradeLevel(UpgradeType.MiningAmount);

		_cachedMaxCapacity = _locationTemplate.GetMaxCapacity(capacityLevel, _game.GetConfig());
		_cachedMiningTickTime = _locationTemplate.GetMiningTickTime(speedLevel, _game.GetConfig());
		_cachedGemsPerTick = _locationTemplate.GetGemsPerTick(amountLevel, _game.GetConfig());
		_cacheValid = true;

		if (_game.IsDebugMode())
		{
			GD.Print($"[GameState] Cache refreshed from upgrade levels: Capacity={capacityLevel}, Speed={speedLevel}, Amount={amountLevel}");
			GD.Print($"[GameState] Calculated values: MaxCapacity={_cachedMaxCapacity}, TickTime={_cachedMiningTickTime}s ({_cachedMiningTickTime/60:F1}min), GemsPerTick={_cachedGemsPerTick}");
		}
	}

	private void InvalidateCache()
	{
		_cacheValid = false;
	}

#endregion

#region Mining Calculations

	/// <summary>
	/// Calculates mining progress using timestamp-based calculation.
	/// </summary>
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
			RecoverFromClockDrift();
			return (0, 0.0f);
		}
			
		var miningInterval = GetMiningTickTime();
		if (miningInterval <= 0) return (0, 0.0f);
			
		var ticksReady = (int)(elapsed / miningInterval);
		var progressToNextTick = (float)((elapsed % miningInterval) / miningInterval);
			
		return (ticksReady, progressToNextTick);
	}
		
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
	/// Start a new mining cycle from current time.
	/// Called after extraction to establish baseline for next offline calculation.
	/// </summary>
	public void StartNewMiningCycle()
	{
		_lastMiningTickTime = DateTime.UtcNow;

		if (_game.IsDebugMode())
			GD.Print("[GameState] New mining cycle started");
	}

	/// <summary>
	/// Recover from clock anomalies (system time went backward).
	/// This should be rare - if it happens frequently, investigate root cause.
	/// </summary>
	private void RecoverFromClockDrift()
	{
		_lastMiningTickTime = DateTime.UtcNow;

		if (_game.IsDebugMode())
			GD.PrintErr("[GameState] Clock drift detected - timer recovered");
	}

#endregion

#region Data Loading

	public async Task LoadUserDataAsync()
	{
		if (_game.IsDebugMode())
			GD.Print("[GameState] === LoadUserDataAsync START ===");

		// Early return: no user logged in
		var phoneNumber = _game.GetCurrentUserPhoneNumber();
		if (string.IsNullOrEmpty(phoneNumber))
		{
			GD.Print("[GameState] No user logged in - creating default state");
			CreateDefaultState();
			InvalidateCache();
			ProcessReadyMiningTicks();
			GD.Print("[GameState] === LoadUserDataAsync COMPLETE ===");
			return;
		}

		// Early return: no valid player session
		var sessionManager = SessionManager.GetInstance();
		var currentSession = sessionManager?.GetPrimaryUserSession();
		if (currentSession?.PlayerId == null || currentSession.PlayerId == Guid.Empty)
		{
			GD.Print("[GameState] No valid player ID - creating default state");
			CreateDefaultState();
			InvalidateCache();
			ProcessReadyMiningTicks();
			GD.Print("[GameState] === LoadUserDataAsync COMPLETE ===");
			return;
		}

		// Happy path: load from backend
		var playerId = currentSession.PlayerId;
		// Get venue name from EventService for venue-scoped mining progress
		// Mining progress follows player across machines at same venue
		var eventService = _game.GetEventService();
		var venueName = eventService.GetVenueName() ?? "default";

		GD.Print("[GameState] Loading state from backend using unified endpoint...");
		bool stateLoaded = await TryLoadStateFromBackend(playerId, venueName);

		if (!stateLoaded)
		{
			GD.Print("[GameState] Backend load failed - creating default state");
			CreateDefaultState();
		}
		else
		{
			GD.Print("[GameState] Backend data loaded successfully!");
		}

		// Common finalization
		InvalidateCache();
		GD.Print("[GameState] Processing ready mining ticks...");
		ProcessReadyMiningTicks();
		GD.Print("[GameState] === LoadUserDataAsync COMPLETE ===");
	}

		#endregion

#region Backend State Loading

	private async Task<bool> TryLoadStateFromBackend(Guid playerId, string venueName)
	{
		GD.Print($"[GameState] === TryLoadStateFromBackend START === Player: {playerId}, Venue: {venueName}");

		try
		{
			// Use unified endpoint to load all state in single API call
			var result = await _game.GetEventService().GetPlayerStateAsync(playerId, venueName);

			if (!result.IsSuccess(out var state))
			{
				var errorMsg = result.IsFailure(out var error)
					? $"Failed to load state: {error.Message}"
					: "Failed to extract state data";
				GD.Print($"[GameState] {errorMsg}");
				return false;
			}

			_globalData = new MiningGlobalDataStore();
			foreach (var kvp in state.Inventory)
			{
				if (Enum.TryParse<GemType>(kvp.Key, true, out var gemType))
				{
					_globalData.AddGems(gemType, kvp.Value);
				}
			}
			GD.Print($"[GameState] Loaded inventory: {string.Join(", ", state.Inventory.Select(g => $"{g.Key}={g.Value}"))}");

			_upgradeLevels = new();
			foreach (var kvp in state.Upgrades)
			{
				if (Enum.TryParse<UpgradeType>(kvp.Key, true, out var upgradeType))
				{
					_upgradeLevels[upgradeType] = kvp.Value;
				}
			}
			GD.Print($"[GameState] Loaded upgrades: {string.Join(", ", state.Upgrades.Select(u => $"{u.Key}={u.Value}"))}");

			_lastMiningTickTime = ValidateAndNormalizeTimestamp(state.LastExtractionTime);

			_firstTimeBonus = !state.Metadata.HasReceivedBonusAtLocation;
			GD.Print($"[GameState] Loaded metadata: HasBonus={state.Metadata.HasReceivedBonusAtLocation}, TotalEvents={state.Metadata.TotalEvents}");

			// Handle first-time bonus if applicable
			if (_firstTimeBonus)
			{
				// Invalidate cache to ensure bonus uses fresh capacity calculation
				InvalidateCache();
				var maxCapacity = GetMaxCapacity();
				_pendingGems = maxCapacity;

				// Don't emit bonus event yet - wait for extraction to ensure gems are actually persisted
				// This prevents losing bonus gems if user logs out before extracting
				GD.Print($"[GameState] First-time bonus prepared: {_pendingGems} gems (will emit event on extraction)");
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
		GD.Print("[GameState] Creating minimal default state");

		_pendingGems = 0;
		_upgradeLevels = new Dictionary<UpgradeType, int>();
		_globalData = new MiningGlobalDataStore();
		_firstTimeBonus = true;
		_lastMiningTickTime = DateTime.UtcNow;
	}

#endregion

#region Transaction Logic

	public bool CanExtractGems() => IsExtractionReady;
		
	public bool CanPurchaseCredit()
	{
		if (_globalData == null || _locationTemplate == null) 
			return false;

		var cost = new Dictionary<GemType, int>
		{
			{ _locationTemplate.PrimaryGemType, _game.GetConfig().CreditCost }
		};

		return _globalData.HasSufficientGems(cost);
	}
		
	public bool CanPurchaseUpgrade(UpgradeType upgradeType)
	{
		if (_locationTemplate == null || _globalData == null) 
			return false;
			
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

		var maxCapacity = GetMaxCapacity();
		bool wasAtCapacity = _pendingGems >= maxCapacity;

		int extractedAmount = _pendingGems;
		GemType gemType = _locationTemplate.PrimaryGemType;

		// Capture timestamp BEFORE reset - this is the actual last mining time
		DateTime actualLastMiningTime = _lastMiningTickTime;

		_globalData.AddGems(gemType, extractedAmount);
		_pendingGems = 0;

		// Emit event with ACTUAL last mining time (before reset)
		if (_game.GetEventService() != null)
		{
			var eventService = _game.GetEventService();
			_ = eventService.EmitExtractCompleteAsync(gemType, extractedAmount, actualLastMiningTime);

			// If this is first-time bonus extraction, emit bonus event
		if (_firstTimeBonus)
		{
			var playerId = _game.GetSessionManager()?.GetPrimaryUserSession()?.PlayerId ?? Guid.Empty;
			var venueName = eventService.GetVenueName() ?? "default";

			_ = eventService.EmitFirstTimeBonusAsync(playerId, venueName, extractedAmount);
			_firstTimeBonus = false;

			GD.Print($"[GameState] First-time bonus event emitted: {extractedAmount} gems");
		}
		}

		// NOW reset timer to start new mining cycle
		StartNewMiningCycle();

		return true;
	}
		
	public bool PurchaseCredit()
	{
		if (!CanPurchaseCredit() || _locationTemplate == null) 
			return false;

		var cost = new Dictionary<GemType, int>
		{
			{ _locationTemplate.PrimaryGemType, _game.GetConfig().CreditCost }
		};

		// Update local state
		_globalData.SpendGems(cost);

		// Actually add credit to user account
		var phoneNumber = _game.GetCurrentUserPhoneNumber();
		if (!string.IsNullOrEmpty(phoneNumber))
		{
			var sessionManager = _game.GetSessionManager();
			if (sessionManager != null)
			{
				var session = sessionManager.GetUserSession(phoneNumber);
				var creditService = CreditService.GetInstance();
				if (session != null && creditService != null)
				{
					_ = creditService.AddAsync(session.PlayerId, CREDITS_PER_PURCHASE, "Mining game credit purchase");
				}
			}
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
		if (!CanPurchaseUpgrade(upgradeType) || _locationTemplate == null) 
			return false;

		int currentLevel = GetUpgradeLevel(upgradeType);
		int newLevel = currentLevel + 1;
		var cost = _game.GetConfig().GetUpgradeCost(upgradeType, currentLevel, _locationTemplate.PrimaryGemType);

		// Update local state
		_globalData.SpendGems(cost);
		SetUpgradeLevel(upgradeType, newLevel);

		// Emit event to backend (venue-scoped for upgrade tracking)
		var eventService = _game.GetEventService();
		var venueName = eventService.GetVenueName();
		_ = eventService.EmitUpgradePurchaseAsync(upgradeType, newLevel, cost, venueName);

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
		_lastMiningTickTime = DateTime.UtcNow;

		// Reset calculated values cache
		InvalidateCache();

		GD.Print("[GameState] All state cleared - ready for new user");
	}

#endregion

#region Helper Methods

	private DateTime ValidateAndNormalizeTimestamp(DateTime? backendTimestamp)
	{
		if (backendTimestamp == null)
		{
			GD.Print("[GameState] No extraction history - starting fresh mining session");
			return DateTime.UtcNow;
		}

		var timestamp = backendTimestamp.Value;
		var elapsed = (DateTime.UtcNow - timestamp).TotalMinutes;

		// Validate timestamp sanity
		const int ONE_YEAR_IN_MINUTES = 365 * 24 * 60;

		if (elapsed < 0 || elapsed > ONE_YEAR_IN_MINUTES)
		{
			var reason = elapsed < 0 ? "is in the future" : "is over 1 year old";
			GD.PrintErr($"[GameState] Backend timestamp {reason}! Resetting to now.");
			return DateTime.UtcNow;
		}

		GD.Print($"[GameState] Loaded extraction timestamp: {timestamp:yyyy-MM-dd HH:mm:ss} ({elapsed:F1} minutes ago)");
		return timestamp;
	}

#endregion
}