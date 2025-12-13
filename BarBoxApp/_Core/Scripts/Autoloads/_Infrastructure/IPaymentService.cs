using System;
using System.Threading;
using System.Threading.Tasks;
using LightResults;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Credit pack options for purchase
/// </summary>
public struct CreditPack
{
	public string PackId { get; init; }
	public int Credits { get; init; }
	public int BonusCredits { get; init; }
	public decimal Price { get; init; }
	public int TotalCredits => Credits + BonusCredits;

	public string DisplayName => BonusCredits > 0
		? $"{Credits:N0} + {BonusCredits:N0} Bonus Credits - ${Price:F2}"
		: $"{Credits:N0} Credits - ${Price:F2}";

	/// <summary>
	/// Available credit packs matching backend CREDIT_PACKS configuration
	/// </summary>
	public static readonly CreditPack[] AvailablePacks =
	[
		new() { PackId = "pack_5", Credits = 5000, BonusCredits = 0, Price = 5.00m },
		new() { PackId = "pack_10", Credits = 10000, BonusCredits = 0, Price = 10.00m },
		new() { PackId = "pack_25", Credits = 25000, BonusCredits = 3000, Price = 25.00m },
		new() { PackId = "pack_50", Credits = 50000, BonusCredits = 10000, Price = 50.00m },
		new() { PackId = "pack_100", Credits = 100000, BonusCredits = 25000, Price = 100.00m },
	];

	/// <summary>
	/// Create a credit pack for testing purposes (allows custom values)
	/// </summary>
	public static CreditPack CreateForTest(int credits, decimal price)
	{
		return new CreditPack
		{
			PackId = $"test_{credits}",
			Credits = credits,
			BonusCredits = 0,
			Price = price
		};
	}
}

/// <summary>
/// Payment transaction result
/// </summary>
public struct PaymentResult
{
	public bool IsSuccess { get; init; }
	public string TransactionId { get; init; }
	public string ErrorMessage { get; init; }
	public CreditPack CreditPack { get; init; }

	public static PaymentResult Success(string transactionId, CreditPack creditPack)
	{
		return new PaymentResult
		{
			IsSuccess = true,
			TransactionId = transactionId,
			ErrorMessage = string.Empty,
			CreditPack = creditPack
		};
	}

	public static PaymentResult Failure(string errorMessage)
	{
		return new PaymentResult
		{
			IsSuccess = false,
			TransactionId = string.Empty,
			ErrorMessage = errorMessage,
			CreditPack = default
		};
	}
}

/// <summary>
/// Result of initiating a payment - may or may not require user action.
/// If PaymentUrl is null, the payment completes immediately (e.g., debug mode).
/// If PaymentUrl is set, the user must complete payment via the URL (e.g., Stripe checkout).
/// </summary>
public struct PaymentCheckout
{
	public string SessionId { get; init; }
	public string PaymentUrl { get; init; }
	public bool RequiresUserAction => !string.IsNullOrEmpty(PaymentUrl);
	public CreditPack CreditPack { get; init; }
	public DateTime CreatedAtUtc { get; init; }
}

/// <summary>
/// Interface for payment processing services.
/// Uses a two-phase approach: InitiatePaymentAsync creates the payment session,
/// AwaitPaymentCompletionAsync waits for user action (if required).
/// </summary>
public interface IPaymentService
{
	/// <summary>
	/// Get available credit packs for purchase
	/// </summary>
	CreditPack[] GetAvailableCreditPacks();

	/// <summary>
	/// Phase 1: Initiate a payment. Returns checkout info.
	/// If PaymentUrl is null, payment completes immediately (no user action needed).
	/// </summary>
	/// <param name="userId">User making the purchase</param>
	/// <param name="creditPack">Credit pack to purchase</param>
	/// <returns>Checkout info with optional payment URL</returns>
	Task<Result<PaymentCheckout>> InitiatePaymentAsync(string userId, CreditPack creditPack);

	/// <summary>
	/// Phase 2: Wait for payment completion.
	/// Only call if PaymentCheckout.RequiresUserAction is true.
	/// If called when RequiresUserAction is false, returns immediate success.
	/// </summary>
	/// <param name="checkout">Checkout from InitiatePaymentAsync</param>
	/// <param name="cancellationToken">Token to cancel the wait</param>
	/// <param name="onProgressUpdate">Optional callback for progress updates (seconds remaining)</param>
	/// <returns>Final payment result</returns>
	Task<PaymentResult> AwaitPaymentCompletionAsync(
		PaymentCheckout checkout,
		CancellationToken cancellationToken,
		Action<float> onProgressUpdate = null);

	/// <summary>
	/// Cancel any in-progress payment operation
	/// </summary>
	void CancelPayment();

	/// <summary>
	/// Validate if a payment service is available and ready
	/// </summary>
	Task<bool> IsServiceAvailableAsync();

	/// <summary>
	/// Get the display name of the payment provider
	/// </summary>
	string GetProviderName();

	/// <summary>
	/// Indicates if this provider typically requires user action (e.g., QR code scan).
	/// Use to determine UI flow before initiating payment.
	/// </summary>
	bool RequiresUserActionForPayments { get; }
}