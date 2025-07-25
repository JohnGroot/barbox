using Godot;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public partial class UserManager : AutoloadBase
{
	[Signal] public delegate void UserLoggedInEventHandler(UserData userData);
	[Signal] public delegate void UserLoggedOutEventHandler();
	[Signal] public delegate void UserDataUpdatedEventHandler(UserData userData);

	private const string USER_DATA_PATH = "user://user_profiles/";
	private const float IDLE_TIMEOUT_SECONDS = 600.0f; // 10 minutes
	
	private Dictionary<string, UserData> _loggedInUsers = new();
	private Dictionary<string, UserData> _cachedUsers = new();
	private Dictionary<string, float> _userIdleTimers = new();
	private Timer _idleCheckTimer;

	protected override void OnServiceReady()
	{
		EnsureUserDataDirectory();
		LoadUserCache();
		SetupIdleTimer();
	}

	public static UserManager GetAutoload()
	{
		return AutoloadBase.GetAutoload<UserManager>();
	}

	public bool LoginUser(string userId, string pin)
	{
		if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(pin))
			return false;

		// Don't allow same user to be logged in multiple times
		if (_loggedInUsers.ContainsKey(userId))
			return false;

		var userData = LoadUserData(userId);
		if (userData != null && userData.Pin == pin)
		{
			_loggedInUsers[userId] = userData;
			_userIdleTimers[userId] = 0.0f; // Initialize idle timer
			EmitSignal(SignalName.UserLoggedIn, userData);
			return true;
		}

		return false;
	}

	/// <summary>
	/// Development context login - creates debug account if doesn't exist
	/// Only works in development context (editor mode)
	/// </summary>
	public bool LoginUserDevelopment(string userId)
	{
		if (string.IsNullOrEmpty(userId))
			return false;

		// Only allow in development context
		if (!GameHost.IsDevelopmentContext())
		{
			LogError("LoginUserDevelopment can only be used in development context");
			return false;
		}

		// Don't allow same user to be logged in multiple times
		if (_loggedInUsers.ContainsKey(userId))
			return false;

		// Try to load existing user data
		var userData = LoadUserData(userId);
		
		// If user doesn't exist, create a debug account
		if (userData == null)
		{
			userData = new UserData
			{
				UserId = userId,
				Pin = "debug", // Default PIN for debug accounts
				Credits = 1000, // Generous credits for development
				HighScores = new Godot.Collections.Dictionary<string, int>()
			};
			
			// Save the debug account for future use
			SaveUserData(userData);
			_cachedUsers[userId] = userData;
			
			LogInfo($"Created debug account for user '{userId}' with 1000 credits");
		}

		// Log in the user
		_loggedInUsers[userId] = userData;
		_userIdleTimers[userId] = 0.0f; // Initialize idle timer
		EmitSignal(SignalName.UserLoggedIn, userData);
		
		LogInfo($"Debug login successful for user '{userId}' - Credits: {userData.Credits}");
		return true;
	}

	public void LogoutUser(string userId)
	{
		if (_loggedInUsers.ContainsKey(userId))
		{
			var userData = _loggedInUsers[userId];
			SaveUserData(userData);
			_loggedInUsers.Remove(userId);
			_userIdleTimers.Remove(userId);
			EmitSignal(SignalName.UserLoggedOut);
		}
	}

	public void LogoutAllUsers()
	{
		foreach (var userData in _loggedInUsers.Values)
		{
			SaveUserData(userData);
		}
		_loggedInUsers.Clear();
		EmitSignal(SignalName.UserLoggedOut);
	}

	public UserData GetUserData(string userId)
	{
		return _loggedInUsers.GetValueOrDefault(userId);
	}

	public List<UserData> GetLoggedInUsers()
	{
		return new List<UserData>(_loggedInUsers.Values);
	}

	public bool IsUserLoggedIn(string userId)
	{
		return _loggedInUsers.ContainsKey(userId);
	}

	public int GetLoggedInUserCount()
	{
		return _loggedInUsers.Count;
	}

	// Backward compatibility methods - return first logged in user or null
	public UserData GetCurrentUser()
	{
		return _loggedInUsers.Values.FirstOrDefault();
	}

	public void LogoutUser()
	{
		var firstUser = GetCurrentUser();
		if (firstUser != null)
		{
			LogoutUser(firstUser.UserId);
		}
	}

	public bool IsUserLoggedIn()
	{
		return _loggedInUsers.Count > 0;
	}

	public void AddCredits(int amount)
	{
		var currentUser = GetCurrentUser();
		if (currentUser != null)
		{
			AddCredits(currentUser.UserId, amount);
		}
	}

	public bool SpendCredits(int amount)
	{
		var currentUser = GetCurrentUser();
		if (currentUser != null)
		{
			return SpendCredits(currentUser.UserId, amount);
		}
		return false;
	}

	public UserData CreateUser(string userId, string pin)
	{
		if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(pin) || 
		    userId.Length > 5 || pin.Length > 5)
			return null;

		if (_cachedUsers.ContainsKey(userId))
			return null; // User already exists

		var newUser = new UserData
		{
			UserId = userId,
			Pin = pin,
			Credits = 0,
			CreatedAt = System.DateTime.Now,
			LastLoginAt = System.DateTime.Now
		};

		SaveUserData(newUser);
		_cachedUsers[userId] = newUser;
		return newUser;
	}

	public void AddCredits(string userId, int amount)
	{
		var userData = GetUserData(userId);
		if (userData != null && amount > 0)
		{
			userData.Credits += amount;
			SaveUserData(userData);
			EmitSignal(SignalName.UserDataUpdated, userData);
		}
	}

	public bool SpendCredits(string userId, int amount)
	{
		var userData = GetUserData(userId);
		if (userData != null && userData.Credits >= amount)
		{
			userData.Credits -= amount;
			SaveUserData(userData);
			EmitSignal(SignalName.UserDataUpdated, userData);
			return true;
		}
		return false;
	}

	public void UpdateHighScore(string gameId, int score)
	{
		// For backward compatibility - update all logged in users' high scores
		foreach (var userData in _loggedInUsers.Values)
		{
			if (!userData.HighScores.ContainsKey(gameId) || 
			    userData.HighScores[gameId] < score)
			{
				userData.HighScores[gameId] = score;
				SaveUserData(userData);
				EmitSignal(SignalName.UserDataUpdated, userData);
			}
		}
	}

	public void UpdateHighScore(string userId, string gameId, int score)
	{
		var userData = GetUserData(userId);
		if (userData != null)
		{
			if (!userData.HighScores.ContainsKey(gameId) || 
			    userData.HighScores[gameId] < score)
			{
				userData.HighScores[gameId] = score;
				SaveUserData(userData);
				EmitSignal(SignalName.UserDataUpdated, userData);
			}
		}
	}

	private void EnsureUserDataDirectory()
	{
		var dir = DirAccess.Open("user://");
		if (!dir.DirExists("user_profiles"))
		{
			dir.MakeDir("user_profiles");
		}
	}

	private void LoadUserCache()
	{
		var dir = DirAccess.Open(USER_DATA_PATH);
		if (dir != null)
		{
			dir.ListDirBegin();
			string fileName = dir.GetNext();
			
			while (fileName != "")
			{
				if (fileName.EndsWith(".json"))
				{
					string userId = fileName.Replace(".json", "");
					var userData = LoadUserData(userId);
					if (userData != null)
					{
						_cachedUsers[userId] = userData;
					}
				}
				fileName = dir.GetNext();
			}
		}
	}

	private UserData LoadUserData(string userId)
	{
		string filePath = USER_DATA_PATH + userId + ".json";
		
		if (Godot.FileAccess.FileExists(filePath))
		{
			using var file = Godot.FileAccess.Open(filePath, Godot.FileAccess.ModeFlags.Read);
			if (file != null)
			{
				string jsonText = file.GetAsText();
				var json = new Json();
				if (json.Parse(jsonText) == Error.Ok)
				{
					var result = json.Data.AsGodotDictionary();
					return UserData.FromDictionary(result);
				}
			}
		}

		return null;
	}

	private void SaveUserData(UserData userData)
	{
		string filePath = USER_DATA_PATH + userData.UserId + ".json";
		
		using var file = Godot.FileAccess.Open(filePath, Godot.FileAccess.ModeFlags.Write);
		if (file != null)
		{
			var json = Json.Stringify(userData.ToDictionary());
			file.StoreString(json);
		}
	}
	
	// ============================================================================
	// Idle Timeout Management
	// ============================================================================
	
	private void SetupIdleTimer()
	{
		_idleCheckTimer = new Timer();
		_idleCheckTimer.WaitTime = 1.0f; // Check every second
		_idleCheckTimer.Timeout += OnIdleTimerTick;
		AddChild(_idleCheckTimer);
		_idleCheckTimer.Start();
	}
	
	private void OnIdleTimerTick()
	{
		var usersToLogout = new List<string>();
		
		// Update idle timers
		var keys = _userIdleTimers.Keys.ToList();
		foreach (var userId in keys)
		{
			_userIdleTimers[userId] += (float)_idleCheckTimer.WaitTime;
			
			if (_userIdleTimers[userId] >= IDLE_TIMEOUT_SECONDS)
			{
				usersToLogout.Add(userId);
			}
		}
		
		// Logout idle users
		foreach (var userId in usersToLogout)
		{
			LogInfo($"Auto-logout user {userId} due to idle timeout");
			LogoutUser(userId);
		}
	}
	
	/// <summary>
	/// Reset idle timer for a user when they interact with the system
	/// </summary>
	public void ResetUserIdleTimer(string userId = null)
	{
		if (userId == null)
		{
			// Reset all logged in users if no specific user provided
			foreach (var loggedInUserId in _loggedInUsers.Keys)
			{
				_userIdleTimers[loggedInUserId] = 0.0f;
			}
		}
		else if (_userIdleTimers.ContainsKey(userId))
		{
			_userIdleTimers[userId] = 0.0f;
		}
	}
	
	/// <summary>
	/// Add credits for debug/testing purposes
	/// </summary>
	public void AddDebugCredits(string userId, int amount)
	{
		if (!GameHost.IsDevelopmentContext())
		{
			LogError("AddDebugCredits can only be used in development context");
			return;
		}
		
		AddCredits(userId, amount);
		LogInfo($"Added {amount} debug credits to user {userId}");
	}
	
	protected override void OnServiceDestroyed()
	{
		if (_idleCheckTimer != null)
		{
			_idleCheckTimer.Stop();
			_idleCheckTimer.QueueFree();
		}
		
		// Save all user data before shutdown
		foreach (var userData in _loggedInUsers.Values)
		{
			SaveUserData(userData);
		}
	}
}