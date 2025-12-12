using Godot;
using System;
using System.Threading.Tasks;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Payment service autoload for managing payment providers.
/// Handles provider selection and payment processing coordination.
///
/// Provider Selection:
/// - Production: Always uses StripePaymentService
/// - Development: Uses StripePaymentService when FORCE_STRIPE_PROVIDER=true,
///   otherwise uses DebugPaymentService for instant purchases
/// </summary>
public partial class PaymentService : AutoloadBase
{
	/// <summary>
	/// Toggle Stripe provider in development mode.
	/// Set to 'true' to test real Stripe integration during development.
	/// Set to 'false' to use DebugPaymentService for instant purchases (no QR code).
	///
	/// Hardcoded constant rationale: This is a compile-time developer setting,
	/// not a runtime configuration. Changing payment providers at runtime
	/// would require additional validation and could introduce bugs.
	/// For production deployment, GameHost.IsProductionContext() ensures
	/// Stripe is always used regardless of this setting.
	/// </summary>
	private const bool FORCE_STRIPE_PROVIDER = true;

	public static PaymentService GetInstance()
	{
		return GetAutoload<PaymentService>();
	}

	private IPaymentService _currentProvider;
	private StripePaymentService _stripeProvider;
	private CreditService _creditService;

	/// <summary>
	/// Get the Stripe payment service for QR code events (null if not using Stripe)
	/// </summary>
	public StripePaymentService StripeService => _stripeProvider;

	protected override void OnServiceInitialize()
	{
		_creditService = CreditService.GetInstance();
		if (_creditService == null)
		{
			LogError("CreditService not found - PaymentService requires CreditService");
			return;
		}

		// Initialize payment provider based on context
		InitializePaymentProvider();

		LogInfo($"PaymentService initialized with provider: {_currentProvider?.GetProviderName()}");
	}

	public CreditPack[] GetAvailableCreditPacks()
	{
		return _currentProvider?.GetAvailableCreditPacks() ?? [];
	}

	/// <summary>
	/// Charges payment provider, then adds credits to user account
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

		// CRITICAL: Check EventService readiness BEFORE processing payment
		// This prevents charging users when credits cannot be added
		var validation = ValidateEventServiceReady();
		if (validation.HasValue)
			return validation.Value;

		try
		{
			LogInfo($"Processing purchase for player {playerId}: {creditPack.DisplayName}");

			// NOW safe to process payment - we know credits can be added
			var paymentResult = await _currentProvider.ProcessPurchaseAsync(playerId.ToString(), creditPack);

			if (!paymentResult.IsSuccess)
			{
				LogWarning($"Payment failed for player {playerId}: {paymentResult.ErrorMessage}");
				return paymentResult;
			}

			// Stripe: Credits are added by backend webhook, StripePaymentService polls for balance change
			// Debug: Credits need to be added manually here
			if (!IsUsingStripe)
			{
				// Add credits to user account via CreditService (debug mode only)
				var addResult = await _creditService.AddAsync(playerId, creditPack.TotalCredits,
					$"Credit purchase - Transaction: {paymentResult.TransactionId}");

				if (addResult.IsFailure(out var addError))
				{
					LogError($"CRITICAL: Failed to add credits for player {playerId} after successful payment {paymentResult.TransactionId}");
					LogError($"Error: {addError.Message}");
					LogError("This requires manual intervention for refund");

					return PaymentResult.Failure(
						$"Payment processed but credit addition failed. Transaction ID: {paymentResult.TransactionId}. " +
						$"Please contact support for resolution."
					);
				}

				if (addResult.IsSuccess(out var newBalance))
				{
					LogInfo($"Purchase completed for player {playerId}: {creditPack.TotalCredits} credits added, new balance: {newBalance} (Transaction: {paymentResult.TransactionId})");
				}
			}
			else
			{
				// Stripe: Credits already added via webhook, just log completion
				LogInfo($"Stripe purchase completed for player {playerId}: {creditPack.TotalCredits} credits (Transaction: {paymentResult.TransactionId})");
			}

			return paymentResult;
		}
		catch (System.Exception ex)
		{
			LogError($"Error processing purchase for player {playerId}: {ex.Message}");
			return PaymentResult.Failure($"Purchase processing error: {ex.Message}");
		}
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
		var eventService = EventService.GetInstance();
		var eventServiceExists = eventService != null;
		var eventServiceReady = eventService?.IsReady ?? false;

		if (!eventServiceExists)
		{
			LogError("Cannot process payment - EventService not found");
			return PaymentResult.Failure(
				$"EventService not initialized - credits cannot be added. " +
				$"Diagnostics: EventService exists: {eventServiceExists}, EventService ready: {eventServiceReady}");
		}

		if (!eventServiceReady)
		{
			LogError("Cannot process payment - EventService not ready");
			return PaymentResult.Failure(
				$"EventService not ready - backend connection required. " +
				$"Diagnostics: EventService exists: {eventServiceExists}, EventService ready: {eventServiceReady}");
		}

		return null; // Validation passed
	}

	private void InitializePaymentProvider()
	{
		// Use Stripe in production, or when FORCE_STRIPE_PROVIDER is true
		if (FORCE_STRIPE_PROVIDER || GameHost.IsProductionContext())
		{
			_stripeProvider = new StripePaymentService();
			_currentProvider = _stripeProvider;
			var mode = GameHost.IsProductionContext() ? "production" : "development (forced)";
			LogInfo($"Initialized with StripePaymentService ({mode})");
		}
		else
		{
			_currentProvider = new DebugPaymentService();
			LogInfo("Initialized with DebugPaymentService (development mode)");
		}
	}

	/// <summary>
	/// Check if using Stripe payment provider
	/// </summary>
	public bool IsUsingStripe => _stripeProvider != null && _currentProvider == _stripeProvider;

	protected override void OnServiceDestroyed()
	{
		// Dispose Stripe provider if it was used
		_stripeProvider?.Dispose();
		_stripeProvider = null;
		_currentProvider = null;
	}
}