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
	private Dictionary<string, UserData> _loggedInUsers = new();
	private Dictionary<string, UserData> _cachedUsers = new();

	protected override void OnServiceReady()
	{
		EnsureUserDataDirectory();
		LoadUserCache();
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
			EmitSignal(SignalName.UserLoggedIn, userData);
			return true;
		}

		return false;
	}

	public void LogoutUser(string userId)
	{
		if (_loggedInUsers.ContainsKey(userId))
		{
			var userData = _loggedInUsers[userId];
			SaveUserData(userData);
			_loggedInUsers.Remove(userId);
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
}