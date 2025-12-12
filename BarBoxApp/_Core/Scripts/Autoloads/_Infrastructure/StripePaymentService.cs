using Godot;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Payment result with additional Stripe-specific data
/// </summary>
public struct StripePaymentData
{
	public ImageTexture QRCodeTexture { get; init; }
	public string SessionUrl { get; init; }
	public string SessionId { get; init; }
}

/// <summary>
/// Stripe payment service for real money credit purchases via QR code.
///
/// Flow:
/// 1. Create Checkout Session via backend
/// 2. Generate QR code for session URL (using ZXing library)
/// 3. Display QR code for mobile payment
/// 4. Poll for balance increase (1s intervals)
/// 5. Return success when credits appear
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

	private readonly QRCodeCache _qrCache = new();
	private EventService _eventService;
	private int _initialBalance;
	private int _targetCredits;
	private CancellationTokenSource _pollCancellation;
	private string _currentSessionUrl;
	private bool _isProcessingPurchase;

	/// <summary>
	/// Event fired when QR code is ready for display
	/// </summary>
	public event Action<StripePaymentData> OnQRCodeReady;

	/// <summary>
	/// Event fired to update payment progress (seconds remaining)
	/// </summary>
	public event Action<float> OnProgressUpdate;

	/// <summary>
	/// Event fired when payment times out
	/// </summary>
	public event Action OnPaymentTimeout;

	/// <summary>
	/// Event fired when polling encounters a recoverable error
	/// </summary>
	public event Action<string> OnPollingError;

	public CreditPack[] GetAvailableCreditPacks()
	{
		return CreditPack.AvailablePacks;
	}

	public async Task<PaymentResult> ProcessPurchaseAsync(string userId, CreditPack creditPack)
	{
		// Prevent concurrent purchases
		if (_isProcessingPurchase)
		{
			return PaymentResult.Failure("Purchase already in progress");
		}

		_isProcessingPurchase = true;
		try
		{
			// Cancel and dispose any previous polling operation
			_pollCancellation?.Cancel();
			_pollCancellation?.Dispose();
			_pollCancellation = new CancellationTokenSource();

			_eventService = EventService.GetInstance();
			if (_eventService == null || !_eventService.IsReady)
			{
				return PaymentResult.Failure("Payment service not available");
			}

			if (!Guid.TryParse(userId, out var playerId))
			{
				return PaymentResult.Failure("Invalid user ID format");
			}

			// Get initial balance for comparison
			var initialBalanceResult = await _eventService.GetPlayerCreditsAsync(playerId);
			if (initialBalanceResult.IsFailure(out var balanceError))
			{
				return PaymentResult.Failure($"Failed to get current balance: {balanceError.Message}");
			}

			initialBalanceResult.IsSuccess(out var initialBalance);
			_initialBalance = initialBalance;
			_targetCredits = creditPack.TotalCredits;

			GD.Print($"[StripePaymentService] Starting purchase: {creditPack.DisplayName}");
			GD.Print($"[StripePaymentService] Initial balance: {_initialBalance}, expected credits: {_targetCredits}");

			// Create checkout session
			var sessionResult = await _eventService.CreateCheckoutSessionAsync(creditPack.PackId, playerId);
			if (sessionResult.IsFailure(out var sessionError))
			{
				return PaymentResult.Failure($"Failed to create checkout session: {sessionError.Message}");
			}

			sessionResult.IsSuccess(out var session);
			GD.Print($"[StripePaymentService] Checkout session created: {session.SessionId}");

			// Validate session URL domain for security
			if (!session.SessionUrl.StartsWith(STRIPE_CHECKOUT_DOMAIN, StringComparison.OrdinalIgnoreCase))
			{
				GD.PrintErr($"[StripePaymentService] Invalid session URL domain: {session.SessionUrl}");
				return PaymentResult.Failure("Invalid payment session");
			}

			// Track current session for cancellation cleanup
			_currentSessionUrl = session.SessionUrl;

			// Generate QR code
			var qrTexture = _qrCache.GetOrCreateQRCode(session.SessionUrl);
			if (qrTexture == null)
			{
				_currentSessionUrl = null;
				return PaymentResult.Failure("Failed to generate QR code");
			}

			// Notify UI that QR code is ready
			var paymentData = new StripePaymentData
			{
				QRCodeTexture = qrTexture,
				SessionUrl = session.SessionUrl,
				SessionId = session.SessionId
			};
			OnQRCodeReady?.Invoke(paymentData);

			// Poll for payment completion with cancellation support
			var paymentResult = await PollForPaymentAsync(playerId, creditPack, _pollCancellation.Token);

			// Clear QR cache after payment attempt
			_qrCache.ClearUrl(session.SessionUrl);
			_currentSessionUrl = null;

			return paymentResult;
		}
		finally
		{
			_isProcessingPurchase = false;
		}
	}

	private async Task<PaymentResult> PollForPaymentAsync(
		Guid playerId,
		CreditPack creditPack,
		CancellationToken cancellationToken)
	{
		var startTime = Time.GetTicksMsec();
		var pollInterval = INITIAL_POLL_INTERVAL;

		while (!cancellationToken.IsCancellationRequested)
		{
			var elapsedSeconds = (Time.GetTicksMsec() - startTime) / 1000.0f;
			var remainingSeconds = POLL_TIMEOUT - elapsedSeconds;

			if (remainingSeconds <= 0)
			{
				GD.Print("[StripePaymentService] Payment timed out");
				OnPaymentTimeout?.Invoke();
				return PaymentResult.Failure("Payment timed out. Please try again.");
			}

			OnProgressUpdate?.Invoke(remainingSeconds);

			// Validate EventService is still available after potential async operations
			if (_eventService == null || !GodotObject.IsInstanceValid(_eventService) || !_eventService.IsReady)
			{
				GD.PrintErr("[StripePaymentService] EventService became unavailable during polling");
				return PaymentResult.Failure("Payment service lost connection. Please try again.");
			}

			// Check balance
			var balanceResult = await _eventService.GetPlayerCreditsAsync(playerId);
			if (balanceResult.IsSuccess(out var currentBalance))
			{
				var creditIncrease = currentBalance - _initialBalance;
				if (creditIncrease >= _targetCredits)
				{
					GD.Print($"[StripePaymentService] Payment confirmed! Balance: {_initialBalance} -> {currentBalance} (+{creditIncrease})");
					return PaymentResult.Success(
						$"stripe_{DateTime.UtcNow:yyyyMMddHHmmss}",
						creditPack
					);
				}
			}
			else
			{
				// Notify UI of recoverable polling error but continue polling
				OnPollingError?.Invoke("Checking payment status...");
			}

			// Wait before next poll (frame-aware delay)
			await DelayAsync(pollInterval);

			// Check cancellation and EventService validity after async delay
			if (cancellationToken.IsCancellationRequested)
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

	public async Task<bool> IsServiceAvailableAsync()
	{
		var eventService = EventService.GetInstance();
		if (eventService == null || !eventService.IsReady)
		{
			return false;
		}

		// Could add a ping endpoint check here if needed
		await Task.CompletedTask;
		return true;
	}

	public string GetProviderName()
	{
		return "Stripe";
	}

	/// <summary>
	/// Cancel any ongoing payment and clear state
	/// </summary>
	public void CancelPayment()
	{
		_pollCancellation?.Cancel();

		if (!string.IsNullOrEmpty(_currentSessionUrl))
		{
			_qrCache.ClearUrl(_currentSessionUrl);
			_currentSessionUrl = null;
		}

		GD.Print("[StripePaymentService] Payment cancelled");
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
			// Cancel any pending operations
			_pollCancellation?.Cancel();
			_pollCancellation?.Dispose();
			_pollCancellation = null;

			// Clear QR code cache
			_qrCache.ClearCache();

			GD.Print("[StripePaymentService] Disposed");
		}

		_disposed = true;
	}
}
