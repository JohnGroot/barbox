using System.Threading.Tasks;
using BarBox.Core.Autoloads;
using Godot;

namespace BarBox.Core.Utils;

/// <summary>
/// Utility class for credit confirmation UI logic
/// Extracted from SessionManager to maintain clean service boundaries
/// </summary>
public static class CreditConfirmationHelper
{
	/// <summary>
	/// Show credit spending confirmation dialog. Auto-confirms in development
	/// mode (bypass is derived from <c>BuildContext.IsExportedBuild</c>, so it
	/// cannot be true in a production build); routes through UIManager's
	/// confirmation modal otherwise.
	/// </summary>
	/// <param name="phoneNumber">User's phone number</param>
	/// <param name="amount">Amount of credits to spend</param>
	/// <param name="reason">Reason for spending credits</param>
	/// <param name="currentCredits">User's current credit balance</param>
	/// <returns>True if user confirms, false if cancelled</returns>
	public static async Task<bool> ShowCreditConfirmationAsync(string phoneNumber, int amount, string reason, int currentCredits)
	{
		GD.Print($"[CreditConfirmation] Spend {amount} credits for {reason}? (Balance: {currentCredits}, User: {phoneNumber})");

		if (GameHost.IsDevelopmentContext())
		{
			GD.Print("[CreditConfirmation] Auto-confirming in development mode");
			return true;
		}

		var uiManager = UIManager.GetInstance();
		if (uiManager == null)
		{
			GD.PushError("[CreditConfirmation] UIManager unavailable - refusing spend rather than silently confirming");
			return false;
		}

		return await uiManager.ShowConfirmationAsync(
			title: "Confirm Purchase",
			message: $"Spend {amount} credits on {reason}?\nBalance: {currentCredits}",
			confirmText: "Spend",
			cancelText: "Cancel");
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
