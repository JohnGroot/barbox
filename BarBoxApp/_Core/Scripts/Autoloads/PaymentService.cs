using Godot;
using System;
using System.Threading.Tasks;

/// <summary>
/// Payment service autoload for managing payment providers
/// Handles provider selection and payment processing coordination
/// </summary>
public partial class PaymentService : AutoloadBase
{
	public static PaymentService Instance { get; private set; }

	public static PaymentService GetInstance()
	{
		return GetAutoload<PaymentService>();
	}

	private IPaymentService _currentProvider;
	private CreditService _creditService;

	protected override void OnServiceReady()
	{
		Instance = this;
	}

	protected override void OnServiceInitialize()
	{
		_creditService = CreditService.Instance;
		if (_creditService == null)
		{
			LogError("CreditService not found - PaymentService requires CreditService");
			return;
		}

		// Initialize payment provider based on context
		InitializePaymentProvider();

		LogInfo($"PaymentService initialized with provider: {_currentProvider?.GetProviderName()}");
	}

	/// <summary>
	/// Get available credit packs for purchase
	/// </summary>
	public CreditPack[] GetAvailableCreditPacks()
	{
		return _currentProvider?.GetAvailableCreditPacks() ?? new CreditPack[0];
	}

	/// <summary>
	/// Process a credit pack purchase and add credits to user account
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
		var eventService = EventService.GetInstance();
		if (eventService == null || !eventService.IsReady)
		{
			var diagnostics = $"EventService exists: {eventService != null}";
			if (eventService != null)
			{
				diagnostics += $", EventService ready: {eventService.IsReady}";
			}

			LogError($"Cannot process credit purchase - EventService not ready");
			LogError($"Diagnostics: {diagnostics}");

			return PaymentResult.Failure(
				$"Credit system temporarily unavailable (EventService not ready). {diagnostics}"
			);
		}

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

			if (!addResult.IsSuccess)
			{
				// This should be VERY rare now - we already checked EventService before payment
				// But we log diagnostics in case of race conditions
				var diagnostics = $"EventService exists: {eventService != null}";
				if (eventService != null)
				{
					diagnostics += $", EventService ready: {eventService.IsReady}";
				}

				LogError($"CRITICAL: Failed to add credits for player {playerId} after successful payment {paymentResult.TransactionId}");
				LogError($"Diagnostics: {diagnostics}");
				LogError($"Error: {addResult.Error}");
				LogError("This requires manual intervention for refund");

				// TODO: In production, queue for automatic refund processing
				return PaymentResult.Failure(
					$"Payment processed but credit addition failed. Transaction ID: {paymentResult.TransactionId}. " +
					$"Please contact support for resolution."
				);
			}

			LogInfo($"Purchase completed for player {playerId}: {creditPack.Credits} credits added, new balance: {addResult.Value} (Transaction: {paymentResult.TransactionId})");
			return paymentResult;
		}
		catch (System.Exception ex)
		{
			LogError($"Error processing purchase for player {playerId}: {ex.Message}");
			return PaymentResult.Failure($"Purchase processing error: {ex.Message}");
		}
	}

	/// <summary>
	/// Check if payment service is available and ready
	/// </summary>
	public async Task<bool> IsServiceAvailableAsync()
	{
		if (_currentProvider == null)
			return false;

		return await _currentProvider.IsServiceAvailableAsync();
	}

	/// <summary>
	/// Get the current payment provider name
	/// </summary>
	public string GetProviderName()
	{
		return _currentProvider?.GetProviderName() ?? "No Provider";
	}

	private void InitializePaymentProvider()
	{
		// For now, always use debug provider
		// In production, this would select based on configuration
		var debugProvider = new DebugPaymentService();
		AddChild(debugProvider);
		_currentProvider = debugProvider;

		LogInfo("Initialized with DebugPaymentService");
	}

	protected override void OnServiceDestroyed()
	{
		Instance = null;
	}
}