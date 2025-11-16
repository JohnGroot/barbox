using System.Threading.Tasks;

/// <summary>
/// Credit pack options for purchase
/// </summary>
public struct CreditPack
{
	public int Credits { get; init; }
	public decimal Price { get; init; }
	public string DisplayName { get; init; }

	public CreditPack(int credits, decimal price)
	{
		Credits = credits;
		Price = price;
		DisplayName = $"{credits} Credits - ${price:F2}";
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