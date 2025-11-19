using Godot;
using System;
using System.Threading.Tasks;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Payment service autoload for managing payment providers
/// Handles provider selection and payment processing coordination
/// </summary>
public partial class PaymentService : AutoloadBase
{
	public static PaymentService GetInstance()
	{
		return GetAutoload<PaymentService>();
	}

	private IPaymentService _currentProvider;
	private CreditService _creditService;

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

			// Add credits to user account via CreditService
			var addResult = await _creditService.AddAsync(playerId, creditPack.Credits,
				$"Credit purchase - Transaction: {paymentResult.TransactionId}");

			if (addResult.IsFailure(out var addError))
			{
				LogError($"CRITICAL: Failed to add credits for player {playerId} after successful payment {paymentResult.TransactionId}");
				LogError($"Error: {addError.Message}");
				LogError("This requires manual intervention for refund");

				// TODO: In production, queue for automatic refund processing
				return PaymentResult.Failure(
					$"Payment processed but credit addition failed. Transaction ID: {paymentResult.TransactionId}. " +
					$"Please contact support for resolution."
				);
			}

			if (addResult.IsSuccess(out var newBalance))
			{
				LogInfo($"Purchase completed for player {playerId}: {creditPack.Credits} credits added, new balance: {newBalance} (Transaction: {paymentResult.TransactionId})");
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
		if (eventService == null)
		{
			LogError("Cannot process payment - EventService not found");
			return PaymentResult.Failure("Credit system unavailable (service not found)");
		}

		if (!eventService.IsReady)
		{
			LogError("Cannot process payment - EventService not ready");
			return PaymentResult.Failure("Credit system temporarily unavailable (backend not ready)");
		}

		return null; // Validation passed
	}

	private void InitializePaymentProvider()
	{
		// For now, always use debug provider
		// In production, this would select based on configuration
		var debugProvider = new DebugPaymentService();
		_currentProvider = debugProvider;

		LogInfo("Initialized with DebugPaymentService");
	}

	protected override void OnServiceDestroyed()
	{
	}
}