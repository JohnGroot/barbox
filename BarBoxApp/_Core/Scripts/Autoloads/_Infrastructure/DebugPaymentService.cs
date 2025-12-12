using Godot;
using System;
using System.Threading;
using System.Threading.Tasks;
using LightResults;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Debug implementation of IPaymentService for development and testing.
/// Returns PaymentUrl = null indicating instant payment (no user action required).
/// Credits are added directly by PaymentService orchestrator when RequiresUserAction is false.
/// </summary>
public class DebugPaymentService : IPaymentService
{
	private const string TXN_PREFIX = "DEBUG_TXN";

	public CreditPack[] GetAvailableCreditPacks()
	{
		return CreditPack.AvailablePacks;
	}

	public Task<Result<PaymentCheckout>> InitiatePaymentAsync(string userId, CreditPack creditPack)
	{
		GD.Print($"[DebugPaymentService] Initiating purchase for user {userId}: {creditPack.DisplayName}");

		// No payment URL = instant payment (no user action required)
		var checkout = new PaymentCheckout
		{
			SessionId = $"{TXN_PREFIX}_{DateTime.UtcNow:yyyyMMddHHmmss}_{GD.Randi() % 10000:D4}",
			PaymentUrl = null,
			CreditPack = creditPack,
			CreatedAtUtc = DateTime.UtcNow
		};

		GD.Print($"[DebugPaymentService] Checkout created - Session ID: {checkout.SessionId}, RequiresUserAction: {checkout.RequiresUserAction}");

		return Task.FromResult(Result.Success(checkout));
	}

	public Task<PaymentResult> AwaitPaymentCompletionAsync(
		PaymentCheckout checkout,
		CancellationToken cancellationToken,
		Action<float> onProgressUpdate = null)
	{
		// Should never be called for debug (RequiresUserAction = false)
		// If called, return immediate success
		GD.Print($"[DebugPaymentService] AwaitPaymentCompletionAsync called (returning immediate success)");
		return Task.FromResult(PaymentResult.Success(checkout.SessionId, checkout.CreditPack));
	}

	public void CancelPayment()
	{
		// No-op for debug - instant payments can't be cancelled
		GD.Print("[DebugPaymentService] CancelPayment called (no-op)");
	}

	public async Task<bool> IsServiceAvailableAsync()
	{
		await Task.CompletedTask;
		return true;
	}

	public string GetProviderName()
	{
		return "Debug Payment Provider";
	}
}
