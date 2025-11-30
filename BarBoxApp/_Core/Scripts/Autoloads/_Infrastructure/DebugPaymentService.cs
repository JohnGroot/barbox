using Godot;
using System;
using System.Threading.Tasks;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Debug implementation of IPaymentService for development and testing
/// Simulates payment processing with instant approval and logging
/// </summary>
public class DebugPaymentService : IPaymentService
{
	private const string TXN_PREFIX = "DEBUG_TXN";

	private static readonly CreditPack[] _availablePacks =
	[
		new CreditPack(1000, 1.00m),
		new CreditPack(5000, 5.00m),
		new CreditPack(10000, 10.00m),
		new CreditPack(25000, 25.00m),
		new CreditPack(50000, 50.00m),
		new CreditPack(100000, 100.00m)
	];

	public CreditPack[] GetAvailableCreditPacks()
	{
		return _availablePacks;
	}

	public async Task<PaymentResult> ProcessPurchaseAsync(string userId, CreditPack creditPack)
	{
		GD.Print($"[DebugPaymentService] Processing purchase for user {userId}: {creditPack.DisplayName}");

		await Task.Delay(1);
		
		// Generate mock transaction ID
		var transactionId = $"{TXN_PREFIX}_{DateTime.UtcNow:yyyyMMddHHmmss}_{GD.Randi() % 10000:D4}";

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