using Godot;
using System;
using System.Threading.Tasks;

/// <summary>
/// Debug implementation of IPaymentService for development and testing
/// Simulates payment processing with instant approval and logging
/// </summary>
public partial class DebugPaymentService : Node, IPaymentService
{
	private static readonly CreditPack[] _availablePacks = new[]
	{
		new CreditPack(1, 1.00m),
		new CreditPack(5, 5.00m),
		new CreditPack(10, 10.00m),
		new CreditPack(25, 25.00m),
		new CreditPack(50, 50.00m),
		new CreditPack(100, 100.00m)
	};

	public CreditPack[] GetAvailableCreditPacks()
	{
		return _availablePacks;
	}

	public async Task<PaymentResult> ProcessPurchaseAsync(string userId, CreditPack creditPack)
	{
		GD.Print($"[DebugPaymentService] Processing purchase for user {userId}: {creditPack.DisplayName}");

		// Simulate API call delay
		await Task.Delay(1000);

		// Generate mock transaction ID
		var transactionId = $"DEBUG_TXN_{DateTime.UtcNow:yyyyMMddHHmmss}_{GD.Randi() % 10000:D4}";

		// In debug mode, always approve the transaction
		var result = PaymentResult.Success(transactionId, creditPack);

		GD.Print($"[DebugPaymentService] Purchase approved - Transaction ID: {transactionId}");

		return result;
	}

	public async Task<bool> IsServiceAvailableAsync()
	{
		// Simulate service check delay
		await Task.Delay(100);

		// Debug service is always available
		return true;
	}

	public string GetProviderName()
	{
		return "Debug Payment Provider";
	}
}