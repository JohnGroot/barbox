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

	public static CreditService Instance { get; private set; }

	private EventService _eventService;
	private System.Collections.Generic.Dictionary<Guid, CachedBalance> _balanceCache = new();

	private class CachedBalance
	{
		public int Amount { get; set; }
		public DateTime LastUpdated { get; set; }
		public bool IsStale => (DateTime.UtcNow - LastUpdated).TotalSeconds > 30;
	}

	protected override void OnServiceReady()
	{
		Instance = this;
	}

	protected override void OnServiceInitialize()
	{
		_eventService = EventService.GetInstance();
		if (_eventService == null)
		{
			LogWarning("EventService not found - credit operations will not be persisted");
		}

		LogInfo("CreditService initialized with smart caching (30s TTL)");
	}

	/// <summary>
	/// Get player's credit balance
	/// Uses cache if fresh (< 30 seconds), otherwise queries backend
	/// </summary>
	public async Task<Result<int>> GetBalanceAsync(Guid playerId, bool forceRefresh = false)
	{
		if (_eventService == null)
		{
			LogError("EventService not available");
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
		var result = await _eventService.GetPlayerCreditsAsync(playerId);
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
		if (_eventService == null)
		{
			LogError("EventService not available");
			return Result<int>.Failure("Credit service unavailable");
		}

		if (amount <= 0)
		{
			return Result<int>.Failure("Amount must be positive");
		}

		// Backend validates balance and performs spend atomically
		var spendResult = await _eventService.SpendCreditsAsync(playerId, amount, reason);
		if (!spendResult.IsSuccess)
		{
			LogWarning($"Credit spend failed: {spendResult.Error}");
			return Result<int>.Failure(spendResult.Error);
		}

		// Get updated balance after spend (event-sourced system)
		var balanceResult = await _eventService.GetPlayerCreditsAsync(playerId);
		if (balanceResult.IsSuccess)
		{
			UpdateCache(playerId, balanceResult.Value);
			EmitSignal(SignalName.CreditsChanged, playerId.ToString(), balanceResult.Value);
			LogInfo($"Credits spent: {amount} ({reason}) - New balance: {balanceResult.Value}");
			return balanceResult;
		}

		return Result<int>.Failure($"Spend succeeded but balance query failed: {balanceResult.Error}");
	}

	/// <summary>
	/// Add credits with immediate backend sync
	/// </summary>
	public async Task<Result<int>> AddAsync(Guid playerId, int amount, string reason)
	{
		if (_eventService == null)
		{
			LogError("EventService not available");
			return Result<int>.Failure("Credit service unavailable");
		}

		if (amount <= 0)
		{
			return Result<int>.Failure("Amount must be positive");
		}

		var addResult = await _eventService.AddCreditsAsync(playerId, amount, reason);
		if (!addResult.IsSuccess)
		{
			LogWarning($"Credit add failed: {addResult.Error}");
			return Result<int>.Failure(addResult.Error);
		}

		// Get updated balance after add (event-sourced system)
		var balanceResult = await _eventService.GetPlayerCreditsAsync(playerId);
		if (balanceResult.IsSuccess)
		{
			UpdateCache(playerId, balanceResult.Value);
			EmitSignal(SignalName.CreditsChanged, playerId.ToString(), balanceResult.Value);
			LogInfo($"Credits added: {amount} ({reason}) - New balance: {balanceResult.Value}");
			return balanceResult;
		}

		return Result<int>.Failure($"Add succeeded but balance query failed: {balanceResult.Error}");
	}

	/// <summary>
	/// Force reconciliation with backend
	/// Call before critical operations (game start, credit-dependent decisions)
	/// </summary>
	public async Task ReconcileBalanceAsync(Guid playerId)
	{
		if (_eventService == null)
		{
			LogWarning("EventService not available for reconciliation");
			return;
		}

		var backendResult = await _eventService.GetPlayerCreditsAsync(playerId);
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
