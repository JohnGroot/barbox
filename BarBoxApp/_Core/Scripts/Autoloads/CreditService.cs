using Godot;
using System;
using System.Threading.Tasks;

/// <summary>
/// Credit management service with smart caching and event-driven invalidation
///
/// ARCHITECTURE:
/// - 30-second cache TTL for balance queries (balances UX and accuracy)
/// - Event-driven invalidation on spend/add operations
/// - Backend is source of truth for all credit operations
/// - Reconciliation on critical paths (pre-game credit checks)
///
/// USAGE:
/// - GetBalanceAsync() - Query balance (uses cache if fresh)
/// - SpendAsync() - Spend credits (immediate backend call + cache update)
/// - AddAsync() - Add credits (immediate backend call + cache update)
/// - ReconcileBalanceAsync() - Force sync with backend
/// </summary>
public partial class CreditService : AutoloadBase
{
	[Signal] public delegate void CreditsChangedEventHandler(string playerId, int newBalance);

	// Cache and polling configuration constants
	private const float CACHE_TTL_SECONDS = 30.0f;
	private const float BALANCE_POLL_TIMEOUT_SECONDS = 1.5f;
	private const float BALANCE_POLL_INTERVAL_SECONDS = 0.05f;  // Reduced from 0.1f for 2x faster UX responsiveness

	public static CreditService Instance { get; private set; }

	private System.Collections.Generic.Dictionary<Guid, CachedBalance> _balanceCache = new();

	private class CachedBalance
	{
		public int Amount { get; set; }
		public DateTime LastUpdated { get; set; }
		public bool IsStale => (DateTime.UtcNow - LastUpdated).TotalSeconds > CACHE_TTL_SECONDS;
	}

	protected override void OnServiceReady()
	{
		Instance = this;
	}

	protected override void OnServiceInitialize()
	{
		LogInfo("CreditService initialized with smart caching (30s TTL)");
	}

	/// <summary>
	/// Get player's credit balance
	/// Uses cache if fresh (< 30 seconds), otherwise queries backend
	/// </summary>
	public async Task<Result<int>> GetBalanceAsync(Guid playerId, bool forceRefresh = false)
	{
		var eventService = EventService.GetInstance();
		if (eventService == null || !eventService.IsReady)
		{
			LogError($"EventService not available (exists: {eventService != null}, ready: {eventService?.IsReady ?? false})");
			return Result<int>.Failure("Credit service unavailable");
		}

		// Check cache if not forcing refresh
		if (!forceRefresh && _balanceCache.TryGetValue(playerId, out var cached))
		{
			if (!cached.IsStale)
			{
				return Result<int>.Success(cached.Amount);
			}
		}

		// Query backend
		var result = await eventService.GetPlayerCreditsAsync(playerId);
		if (result.IsSuccess)
		{
			UpdateCache(playerId, result.Value);
			EmitSignal(SignalName.CreditsChanged, playerId.ToString(), result.Value);
		}

		return result;
	}

	/// <summary>
	/// Spend credits with immediate backend sync
	/// </summary>
	public async Task<Result<int>> SpendAsync(Guid playerId, int amount, string reason)
	{
		var eventService = EventService.GetInstance();
		if (eventService == null || !eventService.IsReady)
		{
			LogError($"EventService not available (exists: {eventService != null}, ready: {eventService?.IsReady ?? false})");
			return Result<int>.Failure("Credit service unavailable");
		}

		if (amount <= 0)
		{
			return Result<int>.Failure("Amount must be positive");
		}

		// Get initial balance to detect when credits are spent
		var initialBalanceResult = await eventService.GetPlayerCreditsAsync(playerId);
		if (!initialBalanceResult.IsSuccess)
		{
			LogWarning($"Could not get initial balance: {initialBalanceResult.Error}");
			// Continue anyway - backend will validate balance on spend
		}
		var initialBalance = initialBalanceResult.IsSuccess ? initialBalanceResult.Value : 0;

		// Backend validates balance and performs spend atomically
		var spendResult = await eventService.SpendCreditsAsync(playerId, amount, reason);
		if (!spendResult.IsSuccess)
		{
			LogWarning($"Credit spend failed: {spendResult.Error}");
			return Result<int>.Failure(spendResult.Error);
		}

		// Poll for updated balance after spend (event-sourced system requires waiting for event processing)
		// Optimized for game responsiveness: 100ms polls, 1.5s max wait = 15 attempts
		var startTime = System.DateTime.UtcNow;
		var expectedMaxBalance = initialBalance - amount;

		while ((System.DateTime.UtcNow - startTime).TotalSeconds < BALANCE_POLL_TIMEOUT_SECONDS)
		{
			var balanceResult = await eventService.GetPlayerCreditsAsync(playerId);
			if (balanceResult.IsSuccess && balanceResult.Value <= expectedMaxBalance)
			{
				UpdateCache(playerId, balanceResult.Value);
				EmitSignal(SignalName.CreditsChanged, playerId.ToString(), balanceResult.Value);
				LogInfo($"Credits spent: {amount} ({reason}) - New balance: {balanceResult.Value}");
				return balanceResult;
			}

			// Wait before next poll attempt
			await DelayAsync(BALANCE_POLL_INTERVAL_SECONDS);
		}

		// Timeout - get final balance for diagnostic purposes
		var finalBalanceResult = await eventService.GetPlayerCreditsAsync(playerId);
		var finalBalance = finalBalanceResult.IsSuccess ? finalBalanceResult.Value : -1;

		LogError($"Credit spend timeout: Event succeeded but balance did not update within {BALANCE_POLL_TIMEOUT_SECONDS}s. " +
			$"Initial: {initialBalance}, Expected: <={expectedMaxBalance}, Final: {finalBalance}");

		return Result<int>.Failure($"Spend succeeded but balance update timed out (expected: {expectedMaxBalance}, got: {finalBalance})");
	}

	/// <summary>
	/// Add credits with immediate backend sync
	/// </summary>
	public async Task<Result<int>> AddAsync(Guid playerId, int amount, string reason)
	{
		var eventService = EventService.GetInstance();
		if (eventService == null || !eventService.IsReady)
		{
			LogError($"EventService not available (exists: {eventService != null}, ready: {eventService?.IsReady ?? false})");
			return Result<int>.Failure("Credit service unavailable");
		}

		if (amount <= 0)
		{
			return Result<int>.Failure("Amount must be positive");
		}

		// Get initial balance to detect when credits are added
		var initialBalanceResult = await eventService.GetPlayerCreditsAsync(playerId);
		if (!initialBalanceResult.IsSuccess)
		{
			LogWarning($"Could not get initial balance: {initialBalanceResult.Error}");
			// Continue anyway - backend might not have player record yet
		}
		var initialBalance = initialBalanceResult.IsSuccess ? initialBalanceResult.Value : 0;

		var addResult = await eventService.AddCreditsAsync(playerId, amount, reason);
		if (!addResult.IsSuccess)
		{
			LogWarning($"Credit add failed: {addResult.Error}");
			return Result<int>.Failure(addResult.Error);
		}

		// Poll for updated balance after add (event-sourced system requires waiting for event processing)
		// Optimized for game responsiveness: 100ms polls, 1.5s max wait = 15 attempts
		var startTime = System.DateTime.UtcNow;
		var expectedMinBalance = initialBalance + amount;

		while ((System.DateTime.UtcNow - startTime).TotalSeconds < BALANCE_POLL_TIMEOUT_SECONDS)
		{
			var balanceResult = await eventService.GetPlayerCreditsAsync(playerId);
			if (balanceResult.IsSuccess && balanceResult.Value >= expectedMinBalance)
			{
				UpdateCache(playerId, balanceResult.Value);
				EmitSignal(SignalName.CreditsChanged, playerId.ToString(), balanceResult.Value);
				LogInfo($"Credits added: {amount} ({reason}) - New balance: {balanceResult.Value}");
				return balanceResult;
			}

			// Wait before next poll attempt
			await DelayAsync(BALANCE_POLL_INTERVAL_SECONDS);
		}

		// Timeout - get final balance for diagnostic purposes
		var finalBalanceResult = await eventService.GetPlayerCreditsAsync(playerId);
		var finalBalance = finalBalanceResult.IsSuccess ? finalBalanceResult.Value : -1;

		LogError($"Credit add timeout: Event succeeded but balance did not update within {BALANCE_POLL_TIMEOUT_SECONDS}s. " +
			$"Initial: {initialBalance}, Expected: >={expectedMinBalance}, Final: {finalBalance}");

		return Result<int>.Failure($"Add succeeded but balance update timed out (expected: {expectedMinBalance}, got: {finalBalance})");
	}

	/// <summary>
	/// Force reconciliation with backend
	/// Call before critical operations (game start, credit-dependent decisions)
	/// </summary>
	public async Task ReconcileBalanceAsync(Guid playerId)
	{
		var eventService = EventService.GetInstance();
		if (eventService == null || !eventService.IsReady)
		{
			LogWarning($"EventService not available for reconciliation (exists: {eventService != null}, ready: {eventService?.IsReady ?? false})");
			return;
		}

		var backendResult = await eventService.GetPlayerCreditsAsync(playerId);
		if (!backendResult.IsSuccess)
		{
			LogWarning($"Reconciliation failed: {backendResult.Error}");
			return;
		}

		if (_balanceCache.TryGetValue(playerId, out var cached))
		{
			if (cached.Amount != backendResult.Value)
			{
				LogWarning($"Credit drift detected: Local={cached.Amount}, Backend={backendResult.Value}");
				UpdateCache(playerId, backendResult.Value);
				EmitSignal(SignalName.CreditsChanged, playerId.ToString(), backendResult.Value);
			}
		}
		else
		{
			// First time seeing this player - initialize cache
			UpdateCache(playerId, backendResult.Value);
		}
	}

	private void UpdateCache(Guid playerId, int amount)
	{
		if (_balanceCache.ContainsKey(playerId))
		{
			_balanceCache[playerId].Amount = amount;
			_balanceCache[playerId].LastUpdated = DateTime.UtcNow;
		}
		else
		{
			_balanceCache[playerId] = new CachedBalance
			{
				Amount = amount,
				LastUpdated = DateTime.UtcNow
			};
		}
	}

	protected override void OnServiceDestroyed()
	{
		_balanceCache.Clear();
		Instance = null;
	}
}
