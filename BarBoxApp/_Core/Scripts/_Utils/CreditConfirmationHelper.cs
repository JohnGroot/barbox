using Godot;
using System.Threading.Tasks;

namespace BarBox.Core.Utils;

/// <summary>
/// Utility class for credit confirmation UI logic
/// Extracted from SessionManager to maintain clean service boundaries
/// </summary>
public static class CreditConfirmationHelper
{
	/// <summary>
	/// Show credit spending confirmation dialog
	/// Currently a simplified implementation that auto-confirms in development mode
	/// TODO: Implement proper UI dialog for production
	/// </summary>
	/// <param name="phoneNumber">User's phone number</param>
	/// <param name="amount">Amount of credits to spend</param>
	/// <param name="reason">Reason for spending credits</param>
	/// <param name="currentCredits">User's current credit balance</param>
	/// <returns>True if user confirms, false if cancelled</returns>
	public static Task<bool> ShowCreditConfirmationAsync(string phoneNumber, int amount, string reason, int currentCredits)
	{
		// Log confirmation for debugging
		GD.Print($"[CreditConfirmation] Spend {amount} credits for {reason}? (Balance: {currentCredits}, User: {phoneNumber})");

		// Auto-confirm in development mode
		if (GameHost.IsDevelopmentContext())
		{
			GD.Print("[CreditConfirmation] Auto-confirming in development mode");
			return Task.FromResult(true);
		}

		// TODO: Implement proper UI dialog for production
		// For now, always confirm
		GD.Print("[CreditConfirmation] Auto-confirming (TODO: implement UI dialog)");
		return Task.FromResult(true);
	}

	/// <summary>
	/// Check if user has sufficient credits without showing confirmation
	/// Useful for pre-flight validation
	/// </summary>
	public static bool HasSufficientCredits(int currentCredits, int requiredAmount)
	{
		return currentCredits >= requiredAmount;
	}
}
