using Godot;
using System;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// Local file-based backend implementation for DataStore
/// Stores data as JSON files in user://barbox_data/ directory
/// Provides development fallback when REST API is unavailable
/// </summary>
public class LocalFileBackend : IDataStoreBackend
{
	private const string BASE_DATA_PATH = "user://barbox_data";
	private const string GLOBAL_DATA_DIR = "global";
	private const string LOCAL_DATA_DIR = "local";
	private const string MACHINE_DATA_DIR = "machine";

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions 
	{ 
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true // Make files human-readable for development
	};

	private string _locationId;
	private string _dataPath;

	public Task<bool> InitializeAsync(string locationId, string apiBaseUrl = null)
	{
		_locationId = locationId;
		_dataPath = System.Environment.GetEnvironmentVariable("BARBOX_LOCAL_DATA_PATH") ?? BASE_DATA_PATH;

		// Create directory structure
		EnsureDirectoryExists(_dataPath);
		EnsureDirectoryExists($"{_dataPath}/{GLOBAL_DATA_DIR}");
		EnsureDirectoryExists($"{_dataPath}/{LOCAL_DATA_DIR}");
		EnsureDirectoryExists($"{_dataPath}/{LOCAL_DATA_DIR}/{_locationId}");
		EnsureDirectoryExists($"{_dataPath}/{MACHINE_DATA_DIR}");
		EnsureDirectoryExists($"{_dataPath}/{MACHINE_DATA_DIR}/{_locationId}");

		// Backend initialization successful (no logging needed for normal operation)
		return Task.FromResult(true);
	}

	public Task<bool> IsServiceAvailableAsync()
	{
		// Local backend is always available if directories can be created
		try
		{
			EnsureDirectoryExists(_dataPath);
			return Task.FromResult(true);
		}
		catch
		{
			return Task.FromResult(false);
		}
	}

	public Task<Result<DataStore.GlobalUserData>> GetGlobalDataAsync(string phoneNumber)
	{
		if (string.IsNullOrEmpty(phoneNumber))
			return Task.FromResult(Result<DataStore.GlobalUserData>.Failure("Phone number cannot be null or empty"));

		try
		{
			var filePath = $"{_dataPath}/{GLOBAL_DATA_DIR}/{phoneNumber}.json";

			if (!FileAccess.FileExists(filePath))
			{
				// Return new instance if file doesn't exist
				return Task.FromResult(Result<DataStore.GlobalUserData>.Success(new DataStore.GlobalUserData { PhoneNumber = phoneNumber }));
			}

			var fileAccess = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
			if (fileAccess == null)
			{
				return Task.FromResult(Result<DataStore.GlobalUserData>.Failure($"Failed to open global data file for phone {phoneNumber}"));
			}

			var jsonContent = fileAccess.GetAsText();
			fileAccess.Close();

			if (string.IsNullOrEmpty(jsonContent))
			{
				return Task.FromResult(Result<DataStore.GlobalUserData>.Success(new DataStore.GlobalUserData { PhoneNumber = phoneNumber }));
			}

			var data = JsonSerializer.Deserialize<DataStore.GlobalUserData>(jsonContent, JsonOptions);
			return Task.FromResult(Result<DataStore.GlobalUserData>.Success(data ?? new DataStore.GlobalUserData { PhoneNumber = phoneNumber }));
		}
		catch (Exception ex)
		{
			return Task.FromResult(Result<DataStore.GlobalUserData>.Failure($"Exception getting global data for phone {phoneNumber}: {ex.Message}"));
		}
	}

	public Task<Result<bool>> SetGlobalDataAsync(string phoneNumber, DataStore.GlobalUserData data)
	{
		if (string.IsNullOrEmpty(phoneNumber))
			return Task.FromResult(Result<bool>.Failure("User ID cannot be null or empty"));
		if (data == null)
			return Task.FromResult(Result<bool>.Failure("Data cannot be null"));

		try
		{
			var filePath = $"{_dataPath}/{GLOBAL_DATA_DIR}/{phoneNumber}.json";
			var json = JsonSerializer.Serialize(data, JsonOptions);

			var fileAccess = FileAccess.Open(filePath, FileAccess.ModeFlags.Write);
			if (fileAccess == null)
			{
				return Task.FromResult(Result<bool>.Failure($"Failed to open global data file for writing: {phoneNumber}"));
			}

			fileAccess.StoreString(json);
			fileAccess.Close();

			return Task.FromResult(Result<bool>.Success(true));
		}
		catch (Exception ex)
		{
			return Task.FromResult(Result<bool>.Failure($"Exception setting global data for user {phoneNumber}: {ex.Message}"));
		}
	}

	public Task<Result<DataStore.LocalUserData>> GetLocalDataAsync(string phoneNumber)
	{
		if (string.IsNullOrEmpty(phoneNumber))
			return Task.FromResult(Result<DataStore.LocalUserData>.Failure("User ID cannot be null or empty"));

		try
		{
			var filePath = $"{_dataPath}/{LOCAL_DATA_DIR}/{_locationId}/{phoneNumber}.json";
			
			if (!FileAccess.FileExists(filePath))
			{
				// Return new instance if file doesn't exist
				return Task.FromResult(Result<DataStore.LocalUserData>.Success(new DataStore.LocalUserData { PhoneNumber = phoneNumber, LocationId = _locationId }));
			}

			var fileAccess = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
			if (fileAccess == null)
			{
				return Task.FromResult(Result<DataStore.LocalUserData>.Failure($"Failed to open local data file for user {phoneNumber}"));
			}

			var jsonContent = fileAccess.GetAsText();
			fileAccess.Close();

			if (string.IsNullOrEmpty(jsonContent))
			{
				return Task.FromResult(Result<DataStore.LocalUserData>.Success(new DataStore.LocalUserData { PhoneNumber = phoneNumber, LocationId = _locationId }));
			}

			var data = JsonSerializer.Deserialize<DataStore.LocalUserData>(jsonContent, JsonOptions);
			return Task.FromResult(Result<DataStore.LocalUserData>.Success(data ?? new DataStore.LocalUserData { PhoneNumber = phoneNumber, LocationId = _locationId }));
		}
		catch (Exception ex)
		{
			return Task.FromResult(Result<DataStore.LocalUserData>.Failure($"Exception getting local data for user {phoneNumber}: {ex.Message}"));
		}
	}

	public Task<Result<bool>> SetLocalDataAsync(string phoneNumber, DataStore.LocalUserData data)
	{
		if (string.IsNullOrEmpty(phoneNumber))
			return Task.FromResult(Result<bool>.Failure("User ID cannot be null or empty"));
		if (data == null)
			return Task.FromResult(Result<bool>.Failure("Data cannot be null"));

		try
		{
			data.LocationId = _locationId; // Ensure correct location
			var filePath = $"{_dataPath}/{LOCAL_DATA_DIR}/{_locationId}/{phoneNumber}.json";
			var json = JsonSerializer.Serialize(data, JsonOptions);

			var fileAccess = FileAccess.Open(filePath, FileAccess.ModeFlags.Write);
			if (fileAccess == null)
			{
				return Task.FromResult(Result<bool>.Failure($"Failed to open local data file for writing: {phoneNumber}"));
			}

			fileAccess.StoreString(json);
			fileAccess.Close();

			return Task.FromResult(Result<bool>.Success(true));
		}
		catch (Exception ex)
		{
			return Task.FromResult(Result<bool>.Failure($"Exception setting local data for user {phoneNumber}: {ex.Message}"));
		}
	}

	public Task<DataStore.MachineGameData> GetMachineGameDataAsync(string gameId)
	{
		if (string.IsNullOrEmpty(gameId))
			throw new ArgumentException("Game ID cannot be null or empty", nameof(gameId));

		try
		{
			var filePath = $"{_dataPath}/{MACHINE_DATA_DIR}/{_locationId}/{gameId}.json";
			
			if (!FileAccess.FileExists(filePath))
			{
				// Return new instance if file doesn't exist
				return Task.FromResult(new DataStore.MachineGameData { LocationId = _locationId, GameId = gameId });
			}

			var fileAccess = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
			if (fileAccess == null)
			{
				GD.PrintErr($"Failed to open machine-game data file for game {gameId}");
				return Task.FromResult(new DataStore.MachineGameData { LocationId = _locationId, GameId = gameId });
			}

			var jsonContent = fileAccess.GetAsText();
			fileAccess.Close();

			if (string.IsNullOrEmpty(jsonContent))
			{
				return Task.FromResult(new DataStore.MachineGameData { LocationId = _locationId, GameId = gameId });
			}

			var data = JsonSerializer.Deserialize<DataStore.MachineGameData>(jsonContent, JsonOptions);
			return Task.FromResult(data ?? new DataStore.MachineGameData { LocationId = _locationId, GameId = gameId });
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Exception getting machine-game data for game {gameId}: {ex.Message}");
			return Task.FromResult(new DataStore.MachineGameData { LocationId = _locationId, GameId = gameId });
		}
	}

	public Task<bool> SetMachineGameDataAsync(string gameId, DataStore.MachineGameData data)
	{
		if (string.IsNullOrEmpty(gameId) || data == null)
			return Task.FromResult(false);

		try
		{
			data.LocationId = _locationId; // Ensure correct location
			data.GameId = gameId; // Ensure correct game
			data.LastUpdated = DateTime.UtcNow;

			var filePath = $"{_dataPath}/{MACHINE_DATA_DIR}/{_locationId}/{gameId}.json";
			var json = JsonSerializer.Serialize(data, JsonOptions);

			var fileAccess = FileAccess.Open(filePath, FileAccess.ModeFlags.Write);
			if (fileAccess == null)
			{
				GD.PrintErr($"Failed to open machine-game data file for writing: {gameId}");
				return Task.FromResult(false);
			}

			fileAccess.StoreString(json);
			fileAccess.Close();

			GD.Print($"Updated machine-game data for game {gameId} at location {_locationId}");
			return Task.FromResult(true);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Exception setting machine-game data for game {gameId}: {ex.Message}");
			return Task.FromResult(false);
		}
	}

	public async Task<Result<bool>> SpendGlobalCreditsAsync(string phoneNumber, int amount, string reason = "")
	{
		var globalResult = await GetGlobalDataAsync(phoneNumber);
		if (!globalResult.IsSuccess)
			return Result<bool>.Failure($"Failed to get global data: {globalResult.Error}");

		if (globalResult.Value.GlobalCredits < amount)
			return Result<bool>.Failure($"Insufficient credits: has {globalResult.Value.GlobalCredits}, needs {amount}");

		globalResult.Value.GlobalCredits -= amount;
		var setResult = await SetGlobalDataAsync(phoneNumber, globalResult.Value);
		
		if (!setResult.IsSuccess)
			return Result<bool>.Failure($"Failed to update global data: {setResult.Error}");
		
		GD.Print($"User {phoneNumber} spent {amount} global credits for: {reason}");
		return Result<bool>.Success(true);
	}

	public async Task<Result<bool>> AddGlobalCreditsAsync(string phoneNumber, int amount, string reason = "")
	{
		var globalResult = await GetGlobalDataAsync(phoneNumber);
		if (!globalResult.IsSuccess)
			return Result<bool>.Failure($"Failed to get global data: {globalResult.Error}");

		globalResult.Value.GlobalCredits += amount;
		var setResult = await SetGlobalDataAsync(phoneNumber, globalResult.Value);
		
		if (!setResult.IsSuccess)
			return Result<bool>.Failure($"Failed to update global data: {setResult.Error}");
		
		GD.Print($"User {phoneNumber} gained {amount} global credits from: {reason}");
		return Result<bool>.Success(true);
	}

	public async Task<bool> TransferCreditsToMachineAsync(string phoneNumber, string gameId, int amount, string reason = "")
	{
		if (amount <= 0)
			return false;

		// Check if user has enough global credits
		var globalData = await GetGlobalDataAsync(phoneNumber);
		if (globalData.Value.GlobalCredits < amount)
			return false;

		try
		{
			// Spend user's global credits
			var globalSpent = await SpendGlobalCreditsAsync(phoneNumber, amount, $"Transfer to machine for {gameId}: {reason}");
			if (!globalSpent.IsSuccess)
				return false;

			// Add to machine credits
			var machineData = await GetMachineGameDataAsync(gameId);
			machineData.MachineCredits += amount;
			
			bool machineUpdated = await SetMachineGameDataAsync(gameId, machineData);
			if (machineUpdated)
			{
				GD.Print($"User {phoneNumber} transferred {amount} credits to machine for game {gameId}: {reason}");
				return true;
			}
			else
			{
				// Rollback: add credits back to user (best effort)
				await AddGlobalCreditsAsync(phoneNumber, amount, $"Rollback failed machine transfer for {gameId}");
				return false;
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Exception transferring credits to machine for game {gameId}: {ex.Message}");
			return false;
		}
	}

	public async Task<bool> SpendMachineGameCreditsAsync(string gameId, int amount, string reason = "")
	{
		var machineData = await GetMachineGameDataAsync(gameId);
		if (machineData.MachineCredits < amount)
			return false;

		machineData.MachineCredits -= amount;
		var success = await SetMachineGameDataAsync(gameId, machineData);
		
		if (success)
		{
			GD.Print($"Spent {amount} machine credits for game {gameId}: {reason}");
		}
		
		return success;
	}

	public async Task<bool> AddMachineGameCreditsAsync(string gameId, int amount, string reason = "")
	{
		var machineData = await GetMachineGameDataAsync(gameId);
		machineData.MachineCredits += amount;
		var success = await SetMachineGameDataAsync(gameId, machineData);
		
		if (success)
		{
			GD.Print($"Added {amount} machine credits for game {gameId}: {reason}");
		}
		
		return success;
	}

	public Task<bool> SyncUserDataAsync(string phoneNumber)
	{
		// For local backend, sync is essentially a no-op since data is already saved locally
		// We could implement backup/validation logic here if needed
		GD.Print($"Local backend sync completed for user {phoneNumber} (files already persisted)");
		return Task.FromResult(true);
	}

	public string GetCurrentLocationId() => _locationId;

	public Task<Result<bool>> IsPhoneNumberRegisteredAsync(string phoneNumber)
	{
		try
		{
			// Search through all global user files to see if phone number exists
			var globalDir = $"{_dataPath}/{GLOBAL_DATA_DIR}";
			if (!DirAccess.DirExistsAbsolute(globalDir))
				return Task.FromResult(Result<bool>.Success(false));

			var dirAccess = DirAccess.Open(globalDir);
			if (dirAccess == null)
				return Task.FromResult(Result<bool>.Failure("Cannot access global data directory"));

			dirAccess.ListDirBegin();
			string fileName = dirAccess.GetNext();

			while (!string.IsNullOrEmpty(fileName))
			{
				if (fileName.EndsWith(".json"))
				{
					var filePath = $"{globalDir}/{fileName}";
					var fileAccess = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
					if (fileAccess != null)
					{
						var content = fileAccess.GetAsText();
						fileAccess.Close();

						var userData = JsonSerializer.Deserialize<DataStore.GlobalUserData>(content, JsonOptions);
						if (userData != null && userData.PhoneNumber == phoneNumber)
						{
							return Task.FromResult(Result<bool>.Success(true));
						}
					}
				}
				fileName = dirAccess.GetNext();
			}

			return Task.FromResult(Result<bool>.Success(false));
		}
		catch (Exception ex)
		{
			return Task.FromResult(Result<bool>.Failure($"Error checking phone number: {ex.Message}"));
		}
	}

	public Task<Result<bool>> IsUsernameTakenAsync(string username)
	{
		try
		{
			// Search through all global user files to see if username exists
			var globalDir = $"{_dataPath}/{GLOBAL_DATA_DIR}";
			if (!DirAccess.DirExistsAbsolute(globalDir))
				return Task.FromResult(Result<bool>.Success(false));

			var dirAccess = DirAccess.Open(globalDir);
			if (dirAccess == null)
				return Task.FromResult(Result<bool>.Failure("Cannot access global data directory"));

			dirAccess.ListDirBegin();
			string fileName = dirAccess.GetNext();

			while (!string.IsNullOrEmpty(fileName))
			{
				if (fileName.EndsWith(".json"))
				{
					var filePath = $"{globalDir}/{fileName}";
					var fileAccess = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
					if (fileAccess != null)
					{
						var content = fileAccess.GetAsText();
						fileAccess.Close();

						var userData = JsonSerializer.Deserialize<DataStore.GlobalUserData>(content, JsonOptions);
						if (userData != null && userData.UserName.Equals(username, StringComparison.OrdinalIgnoreCase))
						{
							return Task.FromResult(Result<bool>.Success(true));
						}
					}
				}
				fileName = dirAccess.GetNext();
			}

			return Task.FromResult(Result<bool>.Success(false));
		}
		catch (Exception ex)
		{
			return Task.FromResult(Result<bool>.Failure($"Error checking username: {ex.Message}"));
		}
	}

	public async Task<Result<DataStore.GlobalUserData>> CreateUserAccountAsync(string phoneNumber, string pin, string username, string locationId)
	{
		try
		{
			// Check if phone number is already registered
			var phoneResult = await IsPhoneNumberRegisteredAsync(phoneNumber);
			if (!phoneResult.IsSuccess)
				return Result<DataStore.GlobalUserData>.Failure($"Error checking phone number: {phoneResult.Error}");

			if (phoneResult.Value)
				return Result<DataStore.GlobalUserData>.Failure("Phone number is already registered");

			// Check if username is already taken
			var usernameResult = await IsUsernameTakenAsync(username);
			if (!usernameResult.IsSuccess)
				return Result<DataStore.GlobalUserData>.Failure($"Error checking username: {usernameResult.Error}");

			if (usernameResult.Value)
				return Result<DataStore.GlobalUserData>.Failure("Username is already taken");

			// Create new user data
			var userData = new DataStore.GlobalUserData
			{
				PhoneNumber = phoneNumber,
				UserName = username,
				CreatedAtLocation = locationId,
				GlobalCredits = 0, // New users start with 0 credits
				LastSyncAt = DateTime.UtcNow,
				CreatedAt = DateTime.UtcNow
			};

			// Save global data
			var globalResult = await SetGlobalDataAsync(phoneNumber, userData);
			if (!globalResult.IsSuccess)
				return Result<DataStore.GlobalUserData>.Failure($"Failed to save global data: {globalResult.Error}");

			// Create initial local data
			var localData = new DataStore.LocalUserData
			{
				PhoneNumber = phoneNumber,
				LocationId = locationId,
				LastLoginTime = DateTime.UtcNow
			};

			var localResult = await SetLocalDataAsync(phoneNumber, localData);
			if (!localResult.IsSuccess)
				GD.PrintErr($"Warning: Failed to save initial local data for user {phoneNumber}: {localResult.Error}");

			return Result<DataStore.GlobalUserData>.Success(userData);
		}
		catch (Exception ex)
		{
			return Result<DataStore.GlobalUserData>.Failure($"Error creating user account: {ex.Message}");
		}
	}

	public Task<Result<DataStore.GlobalUserData>> AuthenticateByPhoneAsync(string phoneNumber, string pin)
	{
		try
		{
			// Search through all global user files to find the phone number
			var globalDir = $"{_dataPath}/{GLOBAL_DATA_DIR}";
			if (!DirAccess.DirExistsAbsolute(globalDir))
				return Task.FromResult(Result<DataStore.GlobalUserData>.Failure("Global data directory does not exist"));

			var dirAccess = DirAccess.Open(globalDir);
			if (dirAccess == null)
				return Task.FromResult(Result<DataStore.GlobalUserData>.Failure("Cannot access global data directory"));

			dirAccess.ListDirBegin();
			string fileName = dirAccess.GetNext();

			while (!string.IsNullOrEmpty(fileName))
			{
				if (fileName.EndsWith(".json"))
				{
					var filePath = $"{globalDir}/{fileName}";
					var fileAccess = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
					if (fileAccess != null)
					{
						var content = fileAccess.GetAsText();
						fileAccess.Close();

						var userData = JsonSerializer.Deserialize<DataStore.GlobalUserData>(content, JsonOptions);
						if (userData != null && userData.PhoneNumber == phoneNumber)
						{
							// For the prototype, simplified PIN validation
							// In production, PIN should be hashed and salted
							if (GameHost.IsDevelopmentContext() || pin.Length == 4)
							{
								return Task.FromResult(Result<DataStore.GlobalUserData>.Success(userData));
							}
							else
							{
								return Task.FromResult(Result<DataStore.GlobalUserData>.Failure("Invalid PIN"));
							}
						}
					}
				}
				fileName = dirAccess.GetNext();
			}

			return Task.FromResult(Result<DataStore.GlobalUserData>.Failure("Phone number not found or invalid credentials"));
		}
		catch (Exception ex)
		{
			return Task.FromResult(Result<DataStore.GlobalUserData>.Failure($"Error authenticating user: {ex.Message}"));
		}
	}

	// Private helper methods

	private void EnsureDirectoryExists(string path)
	{
		if (!DirAccess.DirExistsAbsolute(path))
		{
			var dirAccess = DirAccess.Open("user://");
			if (dirAccess != null)
			{
				var error = dirAccess.MakeDirRecursive(path);
				if (error != Error.Ok)
				{
					GD.PrintErr($"Failed to create directory {path}: {error}");
				}
			}
		}
	}
}