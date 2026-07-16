using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
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
	private const int MAX_CONSECUTIVE_FAILURES = 5;

	// Stripe Checkout Session URLs always start with this domain
	// See: https://docs.stripe.com/payments/checkout/how-checkout-works
	private const string STRIPE_CHECKOUT_DOMAIN = "https://checkout.stripe.com/";

	private BackendClient _backend;
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
		bool initiationSucceeded = false;
		try
		{
			_backend = BackendClient.GetInstance();
			if (!IsBackendClientValid())
			{
				return Result.Failure<PaymentCheckout>("Payment service not available");
			}

			if (!Guid.TryParse(userId, out var playerId))
			{
				return Result.Failure<PaymentCheckout>("Invalid user ID format");
			}

			GD.Print($"[StripePaymentService] Starting purchase: {creditPack.DisplayName}");

			// Create checkout session
			var sessionResult = await CreateCheckoutSessionAsync(creditPack.PackId, playerId);
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

			initiationSucceeded = true;
			return Result.Success(new PaymentCheckout
			{
				SessionId = session.SessionId,
				PaymentUrl = session.SessionUrl,
				CreditPack = creditPack,
				CreatedAtUtc = DateTime.UtcNow,
			});
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[StripePaymentService] Error initiating payment: {ex.Message}");
			return Result.Failure<PaymentCheckout>($"Failed to initiate payment: {ex.Message}");
		}
		finally
		{
			// Reset flag on failure - if initiation succeeded, AwaitPaymentCompletionAsync will reset it
			if (!initiationSucceeded)
			{
				_isProcessingPurchase = false;
			}
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
			var consecutiveFailures = 0;

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

				// Validate BackendClient is still available
				if (!IsBackendClientValid())
				{
					GD.PrintErr("[StripePaymentService] BackendClient became unavailable during polling");
					return PaymentResult.Failure("Payment service lost connection. Please try again.");
				}

				// Check payment status directly (more efficient than balance polling)
				var statusResult = await GetCheckoutStatusAsync(checkout.SessionId);
				if (statusResult.IsSuccess(out var status))
				{
					consecutiveFailures = 0; // Reset on success
					if (status.Status == "completed")
					{
						GD.Print($"[StripePaymentService] Payment confirmed! Credits: {status.CreditsGranted}");
						return PaymentResult.Success(checkout.SessionId, checkout.CreditPack);
					}

					if (status.Status == "failed")
					{
						GD.Print("[StripePaymentService] Payment failed");
						return PaymentResult.Failure("Payment failed. Please try again.");
					}

					// status is "pending" - continue polling
				}
				else
				{
					consecutiveFailures++;
					GD.PrintErr($"[StripePaymentService] Status check failed ({consecutiveFailures}/{MAX_CONSECUTIVE_FAILURES})");

					if (consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
					{
						GD.PrintErr("[StripePaymentService] Too many consecutive status check failures");
						return PaymentResult.Failure("Unable to verify payment status. Please check your account.");
					}
				}

				// Wait before next poll (frame-aware delay)
				await DelayAsync(pollInterval);

				// Check cancellation after async delay
				if (linkedCts.Token.IsCancellationRequested)
				{
					break;
				}

				if (!IsBackendClientValid())
				{
					GD.PrintErr("[StripePaymentService] BackendClient invalidated during delay");
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
		_backend = BackendClient.GetInstance();
		await Task.CompletedTask;
		return IsBackendClientValid();
	}

	public string GetProviderName()
	{
		return "Stripe";
	}

	public bool RequiresUserActionForPayments => true;

	/// <summary>
	/// Check if BackendClient is valid and ready for requests
	/// </summary>
	private bool IsBackendClientValid()
	{
		return _backend != null &&
			   GodotObject.IsInstanceValid(_backend) &&
			   _backend.IsReady;
	}

	/// <summary>
	/// Create a Stripe Checkout Session for credit purchase
	/// Returns session URL for QR code display
	/// </summary>
	private async Task<Result<CheckoutSessionResponse>> CreateCheckoutSessionAsync(string packId, Guid playerId)
	{
		var request = new CheckoutSessionRequest { PackId = packId };

		var result = await _backend.PostAsync<CheckoutSessionRequest, CheckoutSessionResponse>(
			"/payments/checkout/create",
			request,
			201,
			playerId: playerId);

		if (result.IsSuccess(out var response))
		{
			var validation = response.ValidateRequired();
			if (validation.IsFailure(out var validationError))
			{
				return Result.Failure<CheckoutSessionResponse>(validationError.Message);
			}

			GD.Print($"[StripePaymentService] Checkout session created: {response.SessionId}");
			return Result.Success(response);
		}

		if (result.IsFailure(out var error))
		{
			GD.PrintErr($"[StripePaymentService] Failed to create checkout session: {error.Message}");
			return Result.Failure<CheckoutSessionResponse>(error.Message);
		}

		return Result.Failure<CheckoutSessionResponse>("Unknown error creating checkout session");
	}

	/// <summary>
	/// Check payment completion status by Stripe session ID.
	/// More efficient than polling credit balance (single indexed lookup vs full aggregation).
	/// </summary>
	private async Task<Result<CheckoutStatusResponse>> GetCheckoutStatusAsync(string sessionId)
	{
		var result = await _backend.QueryAsync<CheckoutStatusResponse>($"/payments/checkout/{sessionId}/status");

		if (result.IsSuccess(out var response))
		{
			return Result.Success(response);
		}

		if (result.IsFailure(out var error))
		{
			GD.PrintErr($"[StripePaymentService] Failed to get checkout status: {error.Message}");
			return Result.Failure<CheckoutStatusResponse>(error.Message);
		}

		return Result.Failure<CheckoutStatusResponse>("Unknown error getting checkout status");
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
		{
			return;
		}

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
