using System;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Compatibility stub for UserManager
/// Existing code will reference this but functionality is moved to SessionManager
/// </summary>
public partial class UserManager : AutoloadBase
{
	[Signal] public delegate void UserLoggedInEventHandler(UserData userData);
	[Signal] public delegate void UserLoggedOutEventHandler();
	[Signal] public delegate void UserDataUpdatedEventHandler(UserData userData);

	public static UserManager GetAutoload()
	{
		return AutoloadBase.GetAutoload<UserManager>();
	}

	// Redirect to SessionManager for basic compatibility
	public UserData GetCurrentUser()
	{
		var sessionManager = SessionManager.GetInstance();
		var session = sessionManager?.GetCurrentUserSession();
		if (session != null)
		{
			var userData = new UserData(session.PhoneNumber);
			if (session.GlobalData != null)
			{
				userData.Credits = session.GlobalData.GlobalCredits;
			}
			return userData;
		}
		return null;
	}

	public bool IsUserLoggedIn()
	{
		var sessionManager = SessionManager.GetInstance();
		return sessionManager?.GetCurrentUserSession() != null;
	}

	public async Task LogoutUserAsync()
	{
		var sessionManager = SessionManager.GetInstance();
		var session = sessionManager?.GetCurrentUserSession();
		if (session != null)
		{
			await sessionManager.LogoutUserAsync(session.PhoneNumber);
		}
	}

	// Compatibility method - synchronous wrapper for async logout
	public void LogoutUser()
	{
		// For compatibility, start the async task but don't wait
		// This ensures the logout completes properly
		_ = LogoutUserAsync();
	}

	// Phone number authentication methods
	public async Task<bool> LoginUserByPhoneAsync(string phoneNumber, string pin)
	{
		var sessionManager = SessionManager.GetInstance();
		if (sessionManager != null)
		{
			return await sessionManager.LoginUserByPhoneAsync(phoneNumber, pin);
		}
		return false;
	}

	public async Task<Result<string>> CreateUserAccountAsync(string phoneNumber, string pin, string username)
	{
		var sessionManager = SessionManager.GetInstance();
		if (sessionManager != null)
		{
			return await sessionManager.CreateUserAccountAsync(phoneNumber, pin, username);
		}
		return Result<string>.Failure("SessionManager not available");
	}

	public async Task<Result<bool>> IsUsernameAvailableAsync(string username)
	{
		var sessionManager = SessionManager.GetInstance();
		if (sessionManager != null)
		{
			return await sessionManager.IsUsernameAvailableAsync(username);
		}
		return Result<bool>.Failure("SessionManager not available");
	}


	public void AddCredits(string userId, int amount)
	{
		var sessionManager = SessionManager.GetInstance();
		if (sessionManager != null)
		{
			_ = sessionManager.AddGlobalCreditsAsync(userId, amount, "Compatibility method");
		}
	}

	public void AddCredits(int amount)
	{
		var sessionManager = SessionManager.GetInstance();
		var session = sessionManager?.GetCurrentUserSession();
		if (session != null && sessionManager != null)
		{
			_ = sessionManager.AddGlobalCreditsAsync(session.PhoneNumber, amount, "Compatibility method");
		}
	}

	public void ResetUserIdleTimer(string userId = null)
	{
		var sessionManager = SessionManager.GetInstance();
		if (userId == null)
		{
			sessionManager?.ResetAllIdleTimers();
		}
		else
		{
			sessionManager?.ResetUserIdleTimer(userId);
		}
	}

	public void UpdateHighScore(string gameId, int score)
	{
		// Compatibility stub - high scores are now handled by individual game contexts
		LogInfo($"UpdateHighScore called for {gameId} with score {score} (handled by game contexts)");
	}

	protected override void OnServiceReady()
	{
		// Minimal initialization - most functionality moved to SessionManager
		LogInfo("UserManager compatibility stub initialized");
		
		// Bridge SessionManager signals to maintain compatibility
		var sessionManager = SessionManager.GetInstance();
		if (sessionManager != null)
		{
			sessionManager.UserLoggedIn += OnSessionUserLoggedIn;
			sessionManager.UserLoggedOut += OnSessionUserLoggedOut;
			LogInfo("UserManager signal bridging connected to SessionManager");
		}
		else
		{
			LogWarning("SessionManager not available - signals will not be bridged");
		}
	}

	// Signal bridging methods to forward SessionManager events with correct signatures
	private void OnSessionUserLoggedIn(string userId)
	{
		// Convert SessionManager's UserLoggedIn(string) to UserManager's UserLoggedIn(UserData)
		var userData = GetCurrentUser();
		if (userData != null)
		{
			EmitSignal(SignalName.UserLoggedIn, userData);
			LogInfo($"UserLoggedIn signal emitted for user: {userId}");
		}
		else
		{
			LogWarning($"Could not get UserData for logged in user: {userId}");
		}
	}

	private void OnSessionUserLoggedOut(string userId)
	{
		// Convert SessionManager's UserLoggedOut(string) to UserManager's UserLoggedOut() 
		EmitSignal(SignalName.UserLoggedOut);
		LogInfo($"UserLoggedOut signal emitted for user: {userId}");
	}

	public override void _ExitTree()
	{
		// Clean up signal subscriptions to prevent memory leaks
		var sessionManager = SessionManager.GetInstance();
		if (sessionManager != null && GodotObject.IsInstanceValid(sessionManager))
		{
			sessionManager.UserLoggedIn -= OnSessionUserLoggedIn;
			sessionManager.UserLoggedOut -= OnSessionUserLoggedOut;
			LogInfo("UserManager signal bridging disconnected from SessionManager");
		}
		
		base._ExitTree();
	}
}