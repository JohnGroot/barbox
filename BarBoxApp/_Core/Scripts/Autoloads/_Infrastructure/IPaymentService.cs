using System.Threading.Tasks;

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
/// Interface for payment processing services
/// Prepares for future integration with payment APIs while providing debug implementation
/// </summary>
public interface IPaymentService
{
	/// <summary>
	/// Get available credit packs for purchase
	/// </summary>
	CreditPack[] GetAvailableCreditPacks();

	/// <summary>
	/// Process a credit pack purchase
	/// </summary>
	/// <param name="userId">User making the purchase</param>
	/// <param name="creditPack">Credit pack to purchase</param>
	/// <returns>Payment result with transaction details</returns>
	Task<PaymentResult> ProcessPurchaseAsync(string userId, CreditPack creditPack);

	/// <summary>
	/// Validate if a payment service is available and ready
	/// </summary>
	Task<bool> IsServiceAvailableAsync();

	/// <summary>
	/// Get the display name of the payment provider
	/// </summary>
	string GetProviderName();
}