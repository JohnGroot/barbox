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

		string currentLocationId = _game.GetLocationManager()?.CurrentLocationId ?? DEFAULT_LOCATION_ID;
		_locationTemplate = _game.GetLocationDataTemplate(currentLocationId);
	}

	public override void _Process(double delta)
	{
		_gameTime += delta;

		// Auto-save every 30 seconds (replaces save timer)
		if (_gameTime - _lastSaveTime < AUTO_SAVE_INTERVAL)
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
			_lastMiningTickTime = DateTime.UtcNow;
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
	/// Called when starting fresh cycle (e.g. when mining was paused at capacity)
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

		var phoneNumber = _game.GetCurrentUserPhoneNumber();
		if (!string.IsNullOrEmpty(phoneNumber))
		{
			var sessionManager = SessionManager.GetInstance();
			var currentSession = sessionManager?.GetPrimaryUserSession();
			if (currentSession?.PlayerId != null && currentSession.PlayerId != Guid.Empty)
			{
				var playerId = currentSession.PlayerId;

				GD.Print("[GameState] Loading state from backend...");
				bool stateLoaded = await TryLoadStateFromBackend(playerId, _locationTemplate.LocationId);

				if (stateLoaded)
				{
					GD.Print("[GameState] Backend data loaded successfully!");
				}
				else
				{
					GD.Print("[GameState] Backend load failed - creating default state");
					CreateDefaultState();
				}
			}
			else
			{
				GD.Print("[GameState] No valid player ID - creating default state");
				CreateDefaultState();
			}
		}
		else
		{
			GD.Print("[GameState] No user logged in - creating default state");
			CreateDefaultState();
		}

		GD.Print("[GameState] Processing ready mining ticks...");
		ProcessReadyMiningTicks();

		InvalidateCache();
		GD.Print("[GameState] === LoadUserDataAsync COMPLETE ===");
	}

		#endregion

#region Backend State Loading

	private async Task<bool> TryLoadStateFromBackend(Guid playerId, string locationId)
	{
		GD.Print($"[GameState] === TryLoadStateFromBackend START === Player: {playerId}, Location: {locationId}");

		try
		{
			var inventoryResult = await _game.GetEventService().GetPlayerInventoryAsync(playerId);
			if (inventoryResult.IsSuccess(out var inventory))
			{
				_globalData = new MiningGlobalDataStore();
				foreach (var kvp in inventory.Gems)
				{
					if (Enum.TryParse<GemType>(kvp.Key, true, out var gemType))
					{
						_globalData.AddGems(gemType, kvp.Value);
					}
				}

				GD.Print($"[GameState] Loaded inventory: {string.Join(", ", inventory.Gems.Select(g => $"{g.Key}={g.Value}"))}");
			}
			else if (inventoryResult.IsFailure(out var invError))
			{
				_globalData = new MiningGlobalDataStore();
				GD.Print($"[GameState] No inventory found: {invError.Message}");
			}

			var upgradesResult = await _game.GetEventService().GetPlayerUpgradesAsync(playerId);
			if (upgradesResult.IsSuccess(out var upgrades))
			{
				_upgradeLevels = new();
				foreach (var kvp in upgrades.Upgrades)
				{
					if (Enum.TryParse<UpgradeType>(kvp.Key, true, out var upgradeType))
					{
						_upgradeLevels[upgradeType] = kvp.Value;
					}
				}

				_firstTimeBonus = false;

				GD.Print($"[GameState] Loaded upgrades: {string.Join(", ", upgrades.Upgrades.Select(u => $"{u.Key}={u.Value}"))}");
			}
			else if (upgradesResult.IsFailure(out var upgError))
			{
				_upgradeLevels = new();
				_firstTimeBonus = true;

				GD.Print($"[GameState] No upgrades found: {upgError.Message}");
			}

			var timestampResult = await _game.GetEventService().GetPlayerMiningTimestampAsync(playerId, locationId);
			if (timestampResult.IsSuccess(out var timestamp))
			{
				_lastMiningTickTime = timestamp.LastMiningTime;

				var elapsed = (DateTime.UtcNow - _lastMiningTickTime).TotalMinutes;
				GD.Print($"[GameState] Loaded mining timestamp: {_lastMiningTickTime:yyyy-MM-dd HH:mm:ss} ({elapsed:F1} minutes ago)");
			}
			else if (timestampResult.IsFailure(out var tsError))
			{
				_lastMiningTickTime = DateTime.UtcNow;
				GD.Print($"[GameState] No timestamp found: {tsError.Message}");
			}

			var metadataResult = await _game.GetEventService().GetPlayerMetadataAsync(playerId);
			if (metadataResult.IsSuccess(out var metadata))
			{
				_firstTimeBonus = !metadata.HasReceivedBonus;

				GD.Print($"[GameState] Loaded metadata: HasBonus={metadata.HasReceivedBonus}, TotalEvents={metadata.TotalEvents}");
			}

			_pendingGems = 0;

			if (_firstTimeBonus)
			{
				var maxCapacity = GetMaxCapacity();
				_pendingGems = maxCapacity;

				GD.Print($"[GameState] First-time bonus granted: {_pendingGems} gems");

				if (_game.GetEventService() != null)
				{
					var bonusResult = await _game.GetEventService().EmitFirstTimeBonusAsync(
						playerId,
						locationId,
						_pendingGems
					);

					if (bonusResult.IsSuccess(out var _))
					{
						_firstTimeBonus = false;
						GD.Print("[GameState] First-time bonus event emitted successfully");
					}
					else if (bonusResult.IsFailure(out var bonusError))
					{
						GD.PrintErr($"[GameState] Failed to emit first-time bonus event: {bonusError.Message}");
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

		var maxCapacity = GetMaxCapacity();
		bool wasAtCapacity = _pendingGems >= maxCapacity;

		int extractedAmount = _pendingGems;
		GemType gemType = _locationTemplate.PrimaryGemType;

		_globalData.AddGems(gemType, extractedAmount);
		_pendingGems = 0;

		if (wasAtCapacity)
		{
			ResetMiningTimer();
		}

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
		var phoneNumber = _game.GetCurrentUserPhoneNumber();
		if (!string.IsNullOrEmpty(phoneNumber))
		{
			var sessionManager = _game.GetSessionManager();
			if (sessionManager != null)
			{
				_ = sessionManager.AddGlobalCreditsAsync(phoneNumber, CREDITS_PER_PURCHASE, "Mining game credit purchase");
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

		// Reset calculated values cache
		InvalidateCache();

		GD.Print("[GameState] All state cleared - ready for new user");
	}

#endregion

#region Helper Methods

	private async Task EmitMiningTickSafeAsync(string locationId, int pendingGems)
	{
		try
		{
			var result = await _game.GetEventService().EmitMiningTickAsync(locationId, pendingGems);
			if (result.IsFailure(out var error))
			{
				GD.PrintErr($"[GameState] Failed to emit mining tick: {error.Message}");
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[GameState] Error emitting mining tick: {ex.Message}");
		}
	}

#endregion
}