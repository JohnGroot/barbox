using Godot;
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
	private SessionManager _sessionManager;

	protected override void OnServiceReady()
	{
		Instance = this;
	}

	protected override void OnServiceInitialize()
	{
		_sessionManager = SessionManager.GetInstance();
		if (_sessionManager == null)
		{
			LogError("SessionManager not found - PaymentService requires SessionManager");
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
	public async Task<PaymentResult> PurchaseCreditsAsync(string userId, CreditPack creditPack)
	{
		if (_currentProvider == null)
		{
			LogError("No payment provider available");
			return PaymentResult.Failure("Payment service not available");
		}

		if (_sessionManager == null)
		{
			LogError("SessionManager not available for credit addition");
			return PaymentResult.Failure("Session service not available");
		}

		try
		{
			LogInfo($"Processing purchase for user {userId}: {creditPack.DisplayName}");

			// Process payment through provider
			var paymentResult = await _currentProvider.ProcessPurchaseAsync(userId, creditPack);

			if (!paymentResult.IsSuccess)
			{
				LogWarning($"Payment failed for user {userId}: {paymentResult.ErrorMessage}");
				return paymentResult;
			}

			// Add credits to user account
			var creditsAdded = await _sessionManager.AddGlobalCreditsAsync(userId, creditPack.Credits,
				$"Credit purchase - Transaction: {paymentResult.TransactionId}");

			if (!creditsAdded)
			{
				LogError($"Failed to add credits for user {userId} after successful payment {paymentResult.TransactionId}");
				// TODO: In production, this would trigger a refund or manual intervention
				return PaymentResult.Failure("Failed to add credits to account");
			}

			LogInfo($"Purchase completed for user {userId}: {creditPack.Credits} credits added (Transaction: {paymentResult.TransactionId})");
			return paymentResult;
		}
		catch (System.Exception ex)
		{
			LogError($"Error processing purchase for user {userId}: {ex.Message}");
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