using Godot;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Payment service autoload for managing payment providers.
/// Handles provider selection, payment orchestration, and generic event firing.
///
/// Provider Selection:
/// - Production: Always uses StripePaymentService
/// - Development: Uses StripePaymentService when FORCE_STRIPE_PROVIDER=true,
///   otherwise uses DebugPaymentService for instant purchases
///
/// Events:
/// - OnPaymentUrlReady: Fired when a payment URL is available for QR display
/// - OnProgressUpdate: Fired during payment polling with remaining seconds
/// - OnPaymentTimeout: Fired when payment times out
/// </summary>
public partial class PaymentService : AutoloadBase
{
	/// <summary>
	/// Toggle Stripe provider in development mode.
	/// Set to 'true' to test real Stripe integration during development.
	/// Set to 'false' to use DebugPaymentService for instant purchases (no QR code).
	/// </summary>
	private const bool FORCE_STRIPE_PROVIDER = true;

	public static PaymentService GetInstance()
	{
		return GetAutoload<PaymentService>();
	}

	private IPaymentService _currentProvider;
	private CreditService _creditService;
	private CancellationTokenSource _paymentCancellation;

	/// <summary>
	/// Fired when a payment URL is ready for display (e.g., as QR code)
	/// </summary>
	public event Action<string> OnPaymentUrlReady;

	/// <summary>
	/// Fired during payment polling with remaining seconds
	/// </summary>
	public event Action<float> OnProgressUpdate;

	/// <summary>
	/// Fired when payment times out
	/// </summary>
	public event Action OnPaymentTimeout;

	/// <summary>
	/// Check if provider requires user action (e.g., Stripe with QR code)
	/// </summary>
	public bool RequiresUserActionForPayments => _currentProvider?.RequiresUserActionForPayments ?? false;

	protected override void OnServiceInitialize()
	{
		_creditService = CreditService.GetInstance();
		if (_creditService == null)
		{
			LogError("CreditService not found - PaymentService requires CreditService");
			return;
		}

		InitializePaymentProvider();

		LogInfo($"PaymentService initialized with provider: {_currentProvider?.GetProviderName()}");
	}

	public CreditPack[] GetAvailableCreditPacks()
	{
		return _currentProvider?.GetAvailableCreditPacks() ?? [];
	}

	/// <summary>
	/// Process a credit purchase through the payment provider.
	/// Handles two-phase payment: initiate → display URL → await completion.
	/// </summary>
	public async Task<PaymentResult> PurchaseCreditsAsync(Guid playerId, CreditPack creditPack)
	{
		if (_currentProvider == null)
		{
			LogError("No payment provider available");
			return PaymentResult.Failure("Payment service not available");
		}

		if (_creditService == null)
		{
			LogError("CreditService not available for credit addition");
			return PaymentResult.Failure("Credit service not available");
		}

		// Validate player has an active session
		var sessionManager = SessionManager.GetInstance();
		if (sessionManager == null)
		{
			LogError("SessionManager not available");
			return PaymentResult.Failure("Session service not available");
		}

		var session = sessionManager.GetSession(playerId);
		if (session == null)
		{
			LogError($"No active session for player {playerId}");
			return PaymentResult.Failure("No active session - please log in first");
		}

		// CRITICAL: Check SessionEventService readiness BEFORE processing payment
		var validation = ValidateEventServiceReady();
		if (validation.HasValue)
			return validation.Value;

		// Cancel any previous payment, create new cancellation token
		_paymentCancellation?.Cancel();
		_paymentCancellation?.Dispose();
		_paymentCancellation = new CancellationTokenSource();

		try
		{
			LogInfo($"Processing purchase for player {playerId}: {creditPack.DisplayName}");

			// Phase 1: Initiate payment
			var checkoutResult = await _currentProvider.InitiatePaymentAsync(playerId.ToString(), creditPack);

			if (checkoutResult.IsFailure(out var checkoutError))
			{
				LogWarning($"Payment initiation failed for player {playerId}: {checkoutError.Message}");
				return PaymentResult.Failure(checkoutError.Message);
			}

			checkoutResult.IsSuccess(out var checkout);

			if (checkout.RequiresUserAction)
			{
				// Notify UI to display payment URL as QR code
				OnPaymentUrlReady?.Invoke(checkout.PaymentUrl);

				// Phase 2: Wait for payment completion with progress updates
				var result = await _currentProvider.AwaitPaymentCompletionAsync(
					checkout,
					_paymentCancellation.Token,
					onProgressUpdate: (secondsRemaining) =>
					{
						OnProgressUpdate?.Invoke(secondsRemaining);
					});

				// Check if timeout occurred
				if (!result.IsSuccess && result.ErrorMessage?.Contains("timed out") == true)
				{
					OnPaymentTimeout?.Invoke();
				}

				if (result.IsSuccess)
				{
					LogInfo($"Stripe purchase completed for player {playerId}: {creditPack.TotalCredits} credits (Transaction: {result.TransactionId})");
				}
				else
				{
					LogWarning($"Payment failed for player {playerId}: {result.ErrorMessage}");
				}

				return result;
			}
			else
			{
				// Instant payment (debug mode) - add credits directly
				var addResult = await _creditService.AddAsync(playerId, creditPack.TotalCredits,
					$"Credit purchase - Transaction: {checkout.SessionId}");

				if (addResult.IsFailure(out var addError))
				{
					LogError($"Failed to add credits for player {playerId}: {addError.Message}");
					return PaymentResult.Failure($"Credit addition failed: {addError.Message}");
				}

				if (addResult.IsSuccess(out var newBalance))
				{
					LogInfo($"Purchase completed for player {playerId}: {creditPack.TotalCredits} credits added, new balance: {newBalance} (Transaction: {checkout.SessionId})");
				}

				return PaymentResult.Success(checkout.SessionId, creditPack);
			}
		}
		catch (Exception ex)
		{
			LogError($"Error processing purchase for player {playerId}: {ex.Message}");
			return PaymentResult.Failure($"Purchase processing error: {ex.Message}");
		}
	}

	/// <summary>
	/// Cancel any in-progress payment
	/// </summary>
	public void CancelPayment()
	{
		_paymentCancellation?.Cancel();
		_currentProvider?.CancelPayment();
		LogInfo("Payment cancelled");
	}

	public async Task<bool> IsServiceAvailableAsync()
	{
		if (_currentProvider == null)
			return false;

		return await _currentProvider.IsServiceAvailableAsync();
	}

	public string GetProviderName()
	{
		return _currentProvider?.GetProviderName() ?? "No Provider";
	}

	private PaymentResult? ValidateEventServiceReady()
	{
		var eventService = SessionEventService.GetInstance();
		var eventServiceExists = eventService != null;
		var eventServiceReady = eventService?.IsReady ?? false;

		if (!eventServiceExists)
		{
			LogError("Cannot process payment - SessionEventService not found");
			return PaymentResult.Failure(
				$"SessionEventService not initialized - credits cannot be added. " +
				$"Diagnostics: SessionEventService exists: {eventServiceExists}, SessionEventService ready: {eventServiceReady}");
		}

		if (!eventServiceReady)
		{
			LogError("Cannot process payment - SessionEventService not ready");
			return PaymentResult.Failure(
				$"SessionEventService not ready - backend connection required. " +
				$"Diagnostics: SessionEventService exists: {eventServiceExists}, SessionEventService ready: {eventServiceReady}");
		}

		return null;
	}

	private void InitializePaymentProvider()
	{
		if (FORCE_STRIPE_PROVIDER || GameHost.IsProductionContext())
		{
			_currentProvider = new StripePaymentService();
			var mode = GameHost.IsProductionContext() ? "production" : "development (forced)";
			LogInfo($"Initialized with StripePaymentService ({mode})");
		}
		else
		{
			_currentProvider = new DebugPaymentService();
			LogInfo("Initialized with DebugPaymentService (development mode)");
		}
	}

	protected override void OnServiceDestroyed()
	{
		_paymentCancellation?.Cancel();
		_paymentCancellation?.Dispose();
		_paymentCancellation = null;

		if (_currentProvider is IDisposable disposable)
		{
			disposable.Dispose();
		}
		_currentProvider = null;
	}
}
