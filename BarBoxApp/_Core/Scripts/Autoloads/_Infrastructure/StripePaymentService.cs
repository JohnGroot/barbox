using Godot;
using System;
using System.Threading;
using System.Threading.Tasks;
using LightResults;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Stripe payment service for real money credit purchases via QR code.
///
/// Flow:
/// 1. InitiatePaymentAsync creates Checkout Session via backend, returns PaymentUrl
/// 2. Caller displays QR code for the payment URL
/// 3. AwaitPaymentCompletionAsync polls for balance increase
/// 4. Returns success when credits appear
///
/// Stripe Checkout: https://docs.stripe.com/payments/checkout
/// How Checkout Works: https://docs.stripe.com/payments/checkout/how-checkout-works
///
/// Implements IDisposable for proper CancellationTokenSource cleanup.
/// </summary>
public class StripePaymentService : IPaymentService, IDisposable
{
	private const float INITIAL_POLL_INTERVAL = 1.0f;
	private const float MAX_POLL_INTERVAL = 3.0f;
	private const float POLL_TIMEOUT = 120.0f;

	// Stripe Checkout Session URLs always start with this domain
	// See: https://docs.stripe.com/payments/checkout/how-checkout-works
	private const string STRIPE_CHECKOUT_DOMAIN = "https://checkout.stripe.com/";

	private EventService _eventService;
	private Guid _currentPlayerId;
	private int _initialBalance;
	private CancellationTokenSource _pollCancellation;
	private bool _isProcessingPurchase;

	public CreditPack[] GetAvailableCreditPacks()
	{
		return CreditPack.AvailablePacks;
	}

	public async Task<Result<PaymentCheckout>> InitiatePaymentAsync(string userId, CreditPack creditPack)
	{
		// Prevent concurrent purchases
		if (_isProcessingPurchase)
		{
			return Result.Failure<PaymentCheckout>("Purchase already in progress");
		}

		_isProcessingPurchase = true;
		try
		{
			_eventService = EventService.GetInstance();
			if (_eventService == null || !_eventService.IsReady)
			{
				return Result.Failure<PaymentCheckout>("Payment service not available");
			}

			if (!Guid.TryParse(userId, out var playerId))
			{
				return Result.Failure<PaymentCheckout>("Invalid user ID format");
			}

			_currentPlayerId = playerId;

			// Get initial balance for comparison during polling
			var initialBalanceResult = await _eventService.GetPlayerCreditsAsync(playerId);
			if (initialBalanceResult.IsFailure(out var balanceError))
			{
				return Result.Failure<PaymentCheckout>($"Failed to get current balance: {balanceError.Message}");
			}

			initialBalanceResult.IsSuccess(out var initialBalance);
			_initialBalance = initialBalance;

			GD.Print($"[StripePaymentService] Starting purchase: {creditPack.DisplayName}");
			GD.Print($"[StripePaymentService] Initial balance: {_initialBalance}, expected credits: {creditPack.TotalCredits}");

			// Create checkout session
			var sessionResult = await _eventService.CreateCheckoutSessionAsync(creditPack.PackId, playerId);
			if (sessionResult.IsFailure(out var sessionError))
			{
				return Result.Failure<PaymentCheckout>($"Failed to create checkout session: {sessionError.Message}");
			}

			sessionResult.IsSuccess(out var session);
			GD.Print($"[StripePaymentService] Checkout session created: {session.SessionId}");

			// Validate session URL domain for security
			if (!session.SessionUrl.StartsWith(STRIPE_CHECKOUT_DOMAIN, StringComparison.OrdinalIgnoreCase))
			{
				GD.PrintErr($"[StripePaymentService] Invalid session URL domain: {session.SessionUrl}");
				return Result.Failure<PaymentCheckout>("Invalid payment session URL");
			}

			return Result.Success(new PaymentCheckout
			{
				SessionId = session.SessionId,
				PaymentUrl = session.SessionUrl,
				CreditPack = creditPack,
				CreatedAtUtc = DateTime.UtcNow
			});
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[StripePaymentService] Error initiating payment: {ex.Message}");
			return Result.Failure<PaymentCheckout>($"Failed to initiate payment: {ex.Message}");
		}
	}

	public async Task<PaymentResult> AwaitPaymentCompletionAsync(
		PaymentCheckout checkout,
		CancellationToken cancellationToken,
		Action<float> onProgressUpdate = null)
	{
		// If no user action required, return immediate success
		if (!checkout.RequiresUserAction)
		{
			return PaymentResult.Success(checkout.SessionId, checkout.CreditPack);
		}

		// Cancel any previous polling operation
		_pollCancellation?.Cancel();
		_pollCancellation?.Dispose();
		_pollCancellation = new CancellationTokenSource();

		// Link with external cancellation token
		using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
			cancellationToken, _pollCancellation.Token);

		try
		{
			var startTime = Time.GetTicksMsec();
			var pollInterval = INITIAL_POLL_INTERVAL;

			while (!linkedCts.Token.IsCancellationRequested)
			{
				var elapsedSeconds = (Time.GetTicksMsec() - startTime) / 1000.0f;
				var remainingSeconds = POLL_TIMEOUT - elapsedSeconds;

				if (remainingSeconds <= 0)
				{
					GD.Print("[StripePaymentService] Payment timed out");
					return PaymentResult.Failure("Payment timed out. Please try again.");
				}

				// Notify caller of progress
				onProgressUpdate?.Invoke(remainingSeconds);

				// Validate EventService is still available
				if (_eventService == null || !GodotObject.IsInstanceValid(_eventService) || !_eventService.IsReady)
				{
					GD.PrintErr("[StripePaymentService] EventService became unavailable during polling");
					return PaymentResult.Failure("Payment service lost connection. Please try again.");
				}

				// Check balance
				var balanceResult = await _eventService.GetPlayerCreditsAsync(_currentPlayerId);
				if (balanceResult.IsSuccess(out var currentBalance))
				{
					if (currentBalance >= _initialBalance + checkout.CreditPack.TotalCredits)
					{
						GD.Print($"[StripePaymentService] Payment confirmed! Balance: {_initialBalance} -> {currentBalance}");
						return PaymentResult.Success(
							$"stripe_{DateTime.UtcNow:yyyyMMddHHmmss}",
							checkout.CreditPack
						);
					}
				}
				// Balance check failures are silent - continue polling

				// Wait before next poll (frame-aware delay)
				await DelayAsync(pollInterval);

				// Check cancellation after async delay
				if (linkedCts.Token.IsCancellationRequested)
					break;

				if (_eventService == null || !GodotObject.IsInstanceValid(_eventService))
				{
					GD.PrintErr("[StripePaymentService] EventService invalidated during delay");
					return PaymentResult.Failure("Payment service connection lost. Please try again.");
				}

				// Gradually increase poll interval
				pollInterval = Math.Min(pollInterval + 0.5f, MAX_POLL_INTERVAL);
			}

			GD.Print("[StripePaymentService] Payment cancelled");
			return PaymentResult.Failure("Payment cancelled");
		}
		finally
		{
			_isProcessingPurchase = false;
		}
	}

	public void CancelPayment()
	{
		_pollCancellation?.Cancel();
		_isProcessingPurchase = false;
		GD.Print("[StripePaymentService] Payment cancelled");
	}

	public async Task<bool> IsServiceAvailableAsync()
	{
		var eventService = EventService.GetInstance();
		if (eventService == null || !eventService.IsReady)
		{
			return false;
		}

		await Task.CompletedTask;
		return true;
	}

	public string GetProviderName()
	{
		return "Stripe";
	}

	private static async Task DelayAsync(float seconds)
	{
		await AutoloadBase.StaticDelayAsync(seconds);
	}

	private bool _disposed;

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (_disposed)
			return;

		if (disposing)
		{
			_pollCancellation?.Cancel();
			_pollCancellation?.Dispose();
			_pollCancellation = null;

			GD.Print("[StripePaymentService] Disposed");
		}

		_disposed = true;
	}
}
