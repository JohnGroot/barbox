namespace BarBox.Games.MiningGame
{
	/// <summary>
	/// Data class for tracking credit purchase timers in the mining game.
	/// Each timer represents a credit that was purchased and needs to recharge before another can be bought.
	/// </summary>
	public class CreditPurchaseTimer
	{
		/// <summary>
		/// Game time when the credit was purchased.
		/// </summary>
		public double PurchaseGameTime { get; set; }
		
		/// <summary>
		/// Game time when the credit will be fully recharged and available for purchase again.
		/// </summary>
		public double RechargeGameTime { get; set; }
		
		/// <summary>
		/// Checks if the credit has finished recharging and is available for purchase.
		/// </summary>
		/// <param name="currentGameTime">Current game time in seconds</param>
		/// <returns>True if the credit has recharged</returns>
		public bool IsRecharged(double currentGameTime) => currentGameTime >= RechargeGameTime;
		
		/// <summary>
		/// Gets the remaining time in seconds until the credit is recharged.
		/// </summary>
		/// <param name="currentGameTime">Current game time in seconds</param>
		/// <returns>Remaining seconds until recharge, or 0 if already recharged</returns>
		public double GetTimeRemainingSeconds(double currentGameTime) 
		{
			return System.Math.Max(0, RechargeGameTime - currentGameTime);
		}
		
		/// <summary>
		/// Gets the remaining time in hours until the credit is recharged.
		/// </summary>
		/// <param name="currentGameTime">Current game time in seconds</param>
		/// <returns>Remaining hours until recharge, or 0 if already recharged</returns>
		public float GetTimeRemainingHours(double currentGameTime)
		{
			return (float)(GetTimeRemainingSeconds(currentGameTime) / 3600.0);
		}
	}
}