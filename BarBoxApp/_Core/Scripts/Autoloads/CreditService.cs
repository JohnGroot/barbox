using BarBox.Core.Utils;
using Godot;
using LightResults;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BarBox.Core.Autoloads;

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

	private readonly ConcurrentDictionary<Guid, CachedBalance> _balanceCache = new();

	private class CachedBalance(int amount, DateTime lastUpdated)
	{
		public int Amount { get; set; } = amount;
		public DateTime LastUpdated { get; set; } = lastUpdated;
		public bool IsStale => (DateTime.UtcNow - LastUpdated).TotalSeconds > CACHE_TTL_SECONDS;
	}

	protected override void OnServiceInitialize()
	{
		LogInfo("CreditService initialized with smart caching (30s TTL)");
	}

	public static CreditService GetInstance()
	{
		return GetAutoload<CreditService>();
	}

	/// <summary>
	/// Returns cached balance if fresh (< 30s TTL), otherwise queries backend
	/// </summary>
	public async Task<Result<int>> GetBalanceAsync(Guid playerId, bool forceRefresh = false)
	{
		// Check cache first (before waiting for BackendClient)
		if (!forceRefresh && _balanceCache.TryGetValue(playerId, out var cached))
		{
			if (!cached.IsStale)
			{
				return Result.Success(cached.Amount);
			}
		}

		var result = await QueryPlayerCreditsAsync(playerId);
		if (result.IsSuccess(out var credits))
		{
			UpdateCache(playerId, credits);
			EmitSignal(SignalName.CreditsChanged, playerId.ToString(), credits);
		}

		return result;
	}

	/// <summary>
	/// Spends credits via backend, polls up to 1.5s for event-sourced balance update
	/// </summary>
	public async Task<Result<int>> SpendAsync(Guid playerId, int amount, string reason)
	{
		if (amount <= 0)
			return Result.Failure<int>("Amount must be positive");

		// Ensure SessionEventService is ready (with retry for race condition) - credit/spend is a
		// session-scoped emit, which SessionEventService still owns
		var eventServiceResult = await EnsureEventServiceReadyAsync();
		if (!eventServiceResult.IsSuccess(out var eventService))
			return Result.Failure<int>("Credit service unavailable");

		// Ensure lobby session exists (lazy creation via SessionManager)
		var sessionManager = SessionManager.GetInstance();
		if (sessionManager == null)
		{
			return Result.Failure<int>("SessionManager not available");
		}

		var lobbyResult = await sessionManager.EnsureLobbySessionAsync(playerId);
		if (lobbyResult.IsFailure(out var lobbyError))
		{
			return Result.Failure<int>($"Cannot spend credits: {lobbyError.Message}");
		}

		if (!lobbyResult.IsSuccess(out var lobbySessionId))
		{
			return Result.Failure<int>("Failed to obtain lobby session");
		}

		var initialBalance = await GetInitialBalanceAsync(playerId);

		// Backend validates balance and performs spend atomically
		var payload = new
		{
			location_id = GetVenueName(),
			amount = amount,
			reason = reason
		};
		var spendResult = await eventService.EmitUserEventAsync(playerId, "credit/spend", payload, lobbySessionId);
		if (spendResult.IsFailure(out var spendError))
		{
			LogWarning($"Credit spend failed: {spendError.Message}");
			return Result.Failure<int>(spendError.Message);
		}

		// Poll for updated balance after spend (event-sourced system requires waiting for event processing)
		// Optimized for game responsiveness: 100ms polls, 1.5s max wait = 15 attempts
		var startTime = System.DateTime.UtcNow;
		var expectedMaxBalance = initialBalance - amount;

		while ((System.DateTime.UtcNow - startTime).TotalSeconds < BALANCE_POLL_TIMEOUT_SECONDS)
		{
			var balanceResult = await QueryPlayerCreditsAsync(playerId);
			if (balanceResult.IsSuccess(out var newBalance) && newBalance <= expectedMaxBalance)
			{
				UpdateCache(playerId, newBalance);
				EmitSignal(SignalName.CreditsChanged, playerId.ToString(), newBalance);
				LogInfo($"Credits spent: {amount} ({reason}) - New balance: {newBalance}");
				return balanceResult;
			}

			// Wait before next poll attempt
			await DelayAsync(BALANCE_POLL_INTERVAL_SECONDS);
		}

		// Timeout - get final balance for diagnostic purposes
		var finalBalanceResult = await QueryPlayerCreditsAsync(playerId);
		var finalBalance = finalBalanceResult.IsSuccess(out var finalValue) ? finalValue : -1;

		LogWarning($"Credit spend timeout: Event succeeded but balance did not update within {BALANCE_POLL_TIMEOUT_SECONDS}s. " +
			$"Initial: {initialBalance}, Expected: <={expectedMaxBalance}, Final: {finalBalance}");

		// CRITICAL FIX: Backend operation succeeded, so we must return success
		// The timeout is only for the event-sourced balance update, not the spend operation
		// Return the best available balance (final query result or expected balance)
		if (finalBalanceResult.IsSuccess(out var finalBalanceValue))
		{
			UpdateCache(playerId, finalBalanceValue);
			EmitSignal(SignalName.CreditsChanged, playerId.ToString(), finalBalanceValue);
			return Result.Success(finalBalanceValue);
		}

		// Fallback: Use expected balance if we can't query final balance
		// This is conservative (assumes spend worked) to prevent double-spending
		var expectedBalance = Math.Max(0, initialBalance - amount);
		UpdateCache(playerId, expectedBalance);
		EmitSignal(SignalName.CreditsChanged, playerId.ToString(), expectedBalance);
		return Result.Success(expectedBalance);
	}

	/// <summary>
	/// Single-spend flow with confirmation: fetch balance, show the spend
	/// dialog, then spend on confirm. One entry point so games stop hand-rolling
	/// balance -> confirm -> spend. Does NOT cover multi-player rollback (Nines)
	/// or spend-then-deposit (Carrom) shapes.
	/// </summary>
	public async Task<Result<int>> SpendWithConfirmationAsync(Guid playerId, string phoneNumber, int amount, string reason)
	{
		if (amount <= 0)
			return Result.Failure<int>("Amount must be positive");

		var balanceResult = await GetBalanceAsync(playerId);
		int currentBalance = balanceResult.IsSuccess(out var balance) ? balance : 0;

		bool confirmed = await CreditConfirmationHelper.ShowCreditConfirmationAsync(phoneNumber, amount, reason, currentBalance);
		if (!confirmed)
			return Result.Failure<int>("Credit spend cancelled");

		return await SpendAsync(playerId, amount, reason);
	}

	/// <summary>
	/// Adds credits via backend, polls up to 1.5s for event-sourced balance update
	/// </summary>
	public async Task<Result<int>> AddAsync(Guid playerId, int amount, string reason)
	{
		if (amount <= 0)
			return Result.Failure<int>("Amount must be positive");

		// Ensure SessionEventService is ready (with retry for race condition) - credit/earn is a
		// session-scoped emit, which SessionEventService still owns
		var eventServiceResult = await EnsureEventServiceReadyAsync();
		if (!eventServiceResult.IsSuccess(out var eventService))
			return Result.Failure<int>("Credit service unavailable");

		// Ensure lobby session exists (lazy creation via SessionManager)
		var sessionManager = SessionManager.GetInstance();
		if (sessionManager == null)
		{
			return Result.Failure<int>("SessionManager not available");
		}

		var lobbyResult = await sessionManager.EnsureLobbySessionAsync(playerId);
		if (lobbyResult.IsFailure(out var lobbyError))
		{
			return Result.Failure<int>($"Cannot add credits: {lobbyError.Message}");
		}

		if (!lobbyResult.IsSuccess(out var lobbySessionId))
		{
			return Result.Failure<int>("Failed to obtain lobby session");
		}

		var initialBalance = await GetInitialBalanceAsync(playerId);

		var payload = new
		{
			location_id = GetVenueName(),
			amount = amount,
			reason = reason
		};
		var addResult = await eventService.EmitUserEventAsync(playerId, "credit/earn", payload, lobbySessionId);
		if (addResult.IsFailure(out var addError))
		{
			LogWarning($"Credit add failed: {addError.Message}");
			return Result.Failure<int>(addError.Message);
		}

		// Poll for updated balance after add (event-sourced system requires waiting for event processing)
		// Optimized for game responsiveness: 100ms polls, 1.5s max wait = 15 attempts
		var startTime = System.DateTime.UtcNow;
		var expectedMinBalance = initialBalance + amount;

		while ((System.DateTime.UtcNow - startTime).TotalSeconds < BALANCE_POLL_TIMEOUT_SECONDS)
		{
			var balanceResult = await QueryPlayerCreditsAsync(playerId);
			if (balanceResult.IsSuccess(out var newBalance) && newBalance >= expectedMinBalance)
			{
				UpdateCache(playerId, newBalance);
				EmitSignal(SignalName.CreditsChanged, playerId.ToString(), newBalance);
				LogInfo($"Credits added: {amount} ({reason}) - New balance: {newBalance}");
				return balanceResult;
			}

			// Wait before next poll attempt
			await DelayAsync(BALANCE_POLL_INTERVAL_SECONDS);
		}

		// Timeout - get final balance for diagnostic purposes
		var finalBalanceResult = await QueryPlayerCreditsAsync(playerId);
		var finalBalance = finalBalanceResult.IsSuccess(out var finalValue) ? finalValue : -1;

		LogWarning($"Credit add timeout: Event succeeded but balance did not update within {BALANCE_POLL_TIMEOUT_SECONDS}s. " +
			$"Initial: {initialBalance}, Expected: >={expectedMinBalance}, Final: {finalBalance}");

		// CRITICAL FIX: Backend operation succeeded, so we must return success
		// The timeout is only for the event-sourced balance update, not the add operation
		// Return the best available balance (final query result or expected balance)
		if (finalBalanceResult.IsSuccess(out var finalBalanceValue))
		{
			UpdateCache(playerId, finalBalanceValue);
			EmitSignal(SignalName.CreditsChanged, playerId.ToString(), finalBalanceValue);
			return Result.Success(finalBalanceValue);
		}

		// Fallback: Use expected balance if we can't query final balance
		// This is optimistic (assumes add worked) since credits were added on backend
		var expectedBalance = initialBalance + amount;
		UpdateCache(playerId, expectedBalance);
		EmitSignal(SignalName.CreditsChanged, playerId.ToString(), expectedBalance);
		return Result.Success(expectedBalance);
	}

	/// <summary>
	/// Call before critical operations (game start, purchases) to ensure fresh balance
	/// </summary>
	public async Task ReconcileBalanceAsync(Guid playerId)
	{
		var backendResult = await QueryPlayerCreditsAsync(playerId);
		if (backendResult.IsFailure(out var error))
		{
			LogWarning($"Reconciliation failed: {error.Message}");
			return;
		}

		if (!backendResult.IsSuccess(out var backendBalance))
			return;

		if (_balanceCache.TryGetValue(playerId, out var cached))
		{
			if (cached.Amount != backendBalance)
			{
				LogWarning($"Credit drift detected: Local={cached.Amount}, Backend={backendBalance}");
				UpdateCache(playerId, backendBalance);
				EmitSignal(SignalName.CreditsChanged, playerId.ToString(), backendBalance);
			}
		}
		else
		{
			// First time seeing this player - initialize cache
			UpdateCache(playerId, backendBalance);
		}
	}

	/// <summary>
	/// Get machine credit pot balance and player contributions
	/// </summary>
	public async Task<Result<MachineCreditsResponse>> GetMachineCreditsAsync(string gameTag, Guid boxId)
	{
		var backendResult = await EnsureBackendClientReadyAsync();
		if (!backendResult.IsSuccess(out var backend))
			return Result.Failure<MachineCreditsResponse>("Credit service unavailable");

		var queryParams = new Dictionary<string, string>
		{
			{ "box_id", boxId.ToString() }
		};

		return await backend.QueryAsync<MachineCreditsResponse>($"/machine-credits/{gameTag}", queryParams);
	}

	/// <summary>
	/// Deposit credits to machine pot (from player account)
	/// </summary>
	public async Task<Result<MachineCreditsResponse>> DepositMachineCreditsAsync(
		string gameTag,
		Guid boxId,
		Guid playerId,
		int amount,
		Guid lobbySessionId)
	{
		var backendResult = await EnsureBackendClientReadyAsync();
		if (!backendResult.IsSuccess(out var backend))
			return Result.Failure<MachineCreditsResponse>("Credit service unavailable");

		var request = new MachineCreditsDepositRequest
		{
			BoxId = boxId,
			PlayerId = playerId,
			Amount = amount,
			LobbySessionId = lobbySessionId
		};
		return await backend.PostAsync<MachineCreditsDepositRequest, MachineCreditsResponse>(
			$"/machine-credits/{gameTag}/deposit", request, 201, playerId: playerId);
	}

	/// <summary>
	/// Consume credits from machine pot (for game start)
	/// </summary>
	public async Task<Result<MachineCreditsResponse>> ConsumeMachineCreditsAsync(
		string gameTag,
		Guid boxId,
		int amount,
		Guid gameSessionId)
	{
		var backendResult = await EnsureBackendClientReadyAsync();
		if (!backendResult.IsSuccess(out var backend))
			return Result.Failure<MachineCreditsResponse>("Credit service unavailable");

		var request = new MachineCreditsConsumeRequest
		{
			BoxId = boxId,
			Amount = amount,
			GameSessionId = gameSessionId
		};
		return await backend.PostAsync<MachineCreditsConsumeRequest, MachineCreditsResponse>(
			$"/machine-credits/{gameTag}/consume", request, 200);
	}

	private void UpdateCache(Guid playerId, int amount)
	{
		// Thread-safe: ConcurrentDictionary indexer atomically adds or updates
		_balanceCache[playerId] = new CachedBalance(amount, DateTime.UtcNow);
	}

	private static string GetVenueName() => LocationManager.GetAutoload()?.VenueName ?? "";

	/// <summary>
	/// Get player's credit balance for current location, over BackendClient directly
	/// </summary>
	private async Task<Result<int>> QueryPlayerCreditsAsync(Guid playerId)
	{
		var backendResult = await EnsureBackendClientReadyAsync();
		if (!backendResult.IsSuccess(out var backend))
			return Result.Failure<int>("Credit service unavailable");

		var result = await backend.QueryAsync<PlayerCreditsResponse>(
			$"/player/{playerId}/credits",
			new Dictionary<string, string> { { "location_id", GetVenueName() } }
		);

		if (result.IsSuccess(out var creditsResponse))
			return Result.Success(creditsResponse.Credits);

		if (result.IsFailure(out var error))
			return Result.Failure<int>(error.Message);

		return Result.Failure<int>("Unknown error getting player credits");
	}

	/// <summary>
	/// Validates SessionEventService is ready, with retry logic to handle initialization race conditions.
	/// Waits up to 1 second for SessionEventService to become ready before failing.
	/// </summary>
	private async Task<Result<SessionEventService>> EnsureEventServiceReadyAsync()
	{
		var eventService = SessionEventService.GetInstance();
		if (eventService != null && eventService.IsReady)
			return Result.Success(eventService);

		// Wait for SessionEventService to become ready (handles initialization race condition)
		const int MAX_RETRIES = 10;
		const float RETRY_DELAY_SECONDS = 0.1f;

		for (int i = 0; i < MAX_RETRIES; i++)
		{
			await DelayAsync(RETRY_DELAY_SECONDS);
			eventService = SessionEventService.GetInstance();
			if (eventService != null && eventService.IsReady)
			{
				LogInfo($"SessionEventService became ready after {(i + 1) * RETRY_DELAY_SECONDS:F1}s");
				return Result.Success(eventService);
			}
		}

		var exists = SessionEventService.GetInstance() != null;
		var ready = SessionEventService.GetInstance()?.IsReady ?? false;
		LogError($"SessionEventService not available (exists: {exists}, ready: {ready})");
		return Result.Failure<SessionEventService>("Credit service unavailable");
	}

	/// <summary>
	/// Validates BackendClient is ready, with retry logic to handle initialization race conditions.
	/// Waits up to 1 second for BackendClient to become ready before failing.
	/// </summary>
	private async Task<Result<BackendClient>> EnsureBackendClientReadyAsync()
	{
		var backend = BackendClient.GetInstance();
		if (backend != null && backend.IsReady)
			return Result.Success(backend);

		// Wait for BackendClient to become ready (handles initialization race condition)
		const int MAX_RETRIES = 10;
		const float RETRY_DELAY_SECONDS = 0.1f;

		for (int i = 0; i < MAX_RETRIES; i++)
		{
			await DelayAsync(RETRY_DELAY_SECONDS);
			backend = BackendClient.GetInstance();
			if (backend != null && backend.IsReady)
			{
				LogInfo($"BackendClient became ready after {(i + 1) * RETRY_DELAY_SECONDS:F1}s");
				return Result.Success(backend);
			}
		}

		var exists = BackendClient.GetInstance() != null;
		var ready = BackendClient.GetInstance()?.IsReady ?? false;
		LogError($"BackendClient not available (exists: {exists}, ready: {ready})");
		return Result.Failure<BackendClient>("Credit service unavailable");
	}

	private async Task<int> GetInitialBalanceAsync(Guid playerId)
	{
		var result = await QueryPlayerCreditsAsync(playerId);
		if (result.IsFailure(out var error))
		{
			LogWarning($"Could not get initial balance: {error.Message}");
		}
		return result.IsSuccess(out var balance) ? balance : 0;
	}

	protected override void OnServiceDestroyed()
	{
		_balanceCache.Clear();
	}
}
