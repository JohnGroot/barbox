using Godot;
using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// REST API backend implementation for DataStore
/// Communicates with SQLite backend via HTTP REST API
/// </summary>
public class RestApiBackend : IDataStoreBackend
{
	// HTTP constants
	private const string USER_AGENT = "BarBox-DataStore/1.0";
	private const string CONTENT_TYPE_JSON = "application/json";
	private const string RESPONSE_NOT_FOUND = "NOT_FOUND";
	private const int CONNECTION_MS = 5000;
	private const int POLLING_DELAY_MS = 10;
	private const int CONNECTION_POLL_DELAY_MS = 100;
	private const int MAX_RETRY_ATTEMPTS = 50;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions 
	{ 
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
	};

	private HttpClient _httpClient;
	private string _locationId;
	private string _apiBaseUrl;
	private string _apiHost;
	private int _apiPort;
	private CancellationTokenSource _connectionCancellation;
	private CancellationTokenSource _requestCancellation;

	public Task<bool> InitializeAsync(string locationId, string apiBaseUrl = null)
	{
		_locationId = locationId;
		_apiBaseUrl = apiBaseUrl ?? "http://localhost:5000";
		
		ParseApiUrl(_apiBaseUrl);
		_httpClient = new HttpClient();

		// Backend initialization successful (no logging needed for normal operation)
		return Task.FromResult(true);
	}

	public async Task<bool> IsServiceAvailableAsync()
	{
		try
		{
			var (success, _) = await MakeRequestAsync(HttpClient.Method.Get, "/api/health");
			return success;
		}
		catch
		{
			return false;
		}
	}

	public async Task<Result<DataStore.GlobalUserData>> GetGlobalDataAsync(string phoneNumber)
	{
		if (string.IsNullOrEmpty(phoneNumber))
			return Result<DataStore.GlobalUserData>.Failure("User ID cannot be null or empty");

		try
		{
			var (success, response) = await MakeRequestAsync(HttpClient.Method.Get, $"/api/users/{phoneNumber}/global");
			if (success && !string.IsNullOrEmpty(response))
			{
				var data = JsonSerializer.Deserialize<DataStore.GlobalUserData>(response, JsonOptions);
				return Result<DataStore.GlobalUserData>.Success(data ?? new DataStore.GlobalUserData { PhoneNumber = phoneNumber });
			}
			
			// If user doesn't exist, return new instance
			if (response == RESPONSE_NOT_FOUND)
			{
				return Result<DataStore.GlobalUserData>.Success(new DataStore.GlobalUserData { PhoneNumber = phoneNumber });
			}
			
			return Result<DataStore.GlobalUserData>.Failure($"Failed to get global data for user {phoneNumber}: HTTP error");
		}
		catch (Exception ex)
		{
			return Result<DataStore.GlobalUserData>.Failure($"Exception getting global data for user {phoneNumber}: {ex.Message}");
		}
	}

	public async Task<Result<bool>> SetGlobalDataAsync(string phoneNumber, DataStore.GlobalUserData data)
	{
		if (string.IsNullOrEmpty(phoneNumber))
			return Result<bool>.Failure("User ID cannot be null or empty");
		if (data == null)
			return Result<bool>.Failure("Data cannot be null");

		try
		{
			var json = JsonSerializer.Serialize(data, JsonOptions);
			
			var (success, response) = await MakeRequestAsync(HttpClient.Method.Post, $"/api/users/{phoneNumber}/global", json);
			
			if (success)
			{
				return Result<bool>.Success(true);
			}
			
			return Result<bool>.Failure($"Failed to set global data for user {phoneNumber}: HTTP error - {response}");
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Exception setting global data for user {phoneNumber}: {ex.Message}");
		}
	}

	public async Task<Result<DataStore.LocalUserData>> GetLocalDataAsync(string phoneNumber)
	{
		if (string.IsNullOrEmpty(phoneNumber))
			return Result<DataStore.LocalUserData>.Failure("User ID cannot be null or empty");

		try
		{
			var (success, response) = await MakeRequestAsync(HttpClient.Method.Get, $"/api/locations/{_locationId}/local/{phoneNumber}");
			if (success && !string.IsNullOrEmpty(response))
			{
				var data = JsonSerializer.Deserialize<DataStore.LocalUserData>(response, JsonOptions);
				return Result<DataStore.LocalUserData>.Success(data ?? new DataStore.LocalUserData { PhoneNumber = phoneNumber, LocationId = _locationId });
			}
			
			// If user doesn't exist locally, return new instance
			if (response == RESPONSE_NOT_FOUND)
			{
				return Result<DataStore.LocalUserData>.Success(new DataStore.LocalUserData { PhoneNumber = phoneNumber, LocationId = _locationId });
			}
			
			return Result<DataStore.LocalUserData>.Failure($"Failed to get local data for user {phoneNumber}: HTTP error");
		}
		catch (Exception ex)
		{
			return Result<DataStore.LocalUserData>.Failure($"Exception getting local data for user {phoneNumber}: {ex.Message}");
		}
	}

	public async Task<Result<bool>> SetLocalDataAsync(string phoneNumber, DataStore.LocalUserData data)
	{
		if (string.IsNullOrEmpty(phoneNumber))
			return Result<bool>.Failure("User ID cannot be null or empty");
		if (data == null)
			return Result<bool>.Failure("Data cannot be null");

		try
		{
			data.LocationId = _locationId; // Ensure correct location
			var json = JsonSerializer.Serialize(data, JsonOptions);
			
			var (success, response) = await MakeRequestAsync(HttpClient.Method.Post, $"/api/locations/{_locationId}/local/{phoneNumber}", json);
			
			if (success)
			{
				return Result<bool>.Success(true);
			}
			
			return Result<bool>.Failure($"Failed to set local data for user {phoneNumber}: HTTP error - {response}");
		}
		catch (Exception ex)
		{
			return Result<bool>.Failure($"Exception setting local data for user {phoneNumber}: {ex.Message}");
		}
	}

	public async Task<DataStore.MachineGameData> GetMachineGameDataAsync(string gameId)
	{
		if (string.IsNullOrEmpty(gameId))
			throw new ArgumentException("Game ID cannot be null or empty", nameof(gameId));

		try
		{
			var (success, response) = await MakeRequestAsync(HttpClient.Method.Get, $"/api/locations/{_locationId}/games/{gameId}/machine-data");
			if (success && !string.IsNullOrEmpty(response))
			{
				var data = JsonSerializer.Deserialize<DataStore.MachineGameData>(response, JsonOptions);
				return data ?? new DataStore.MachineGameData { LocationId = _locationId, GameId = gameId };
			}
			
			// If machine-game data doesn't exist, return new instance
			if (response == RESPONSE_NOT_FOUND)
			{
				return new DataStore.MachineGameData { LocationId = _locationId, GameId = gameId };
			}
			
			GD.PrintErr($"Failed to get machine-game data for game {gameId} at location {_locationId}");
			return new DataStore.MachineGameData { LocationId = _locationId, GameId = gameId };
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Exception getting machine-game data for game {gameId}: {ex.Message}");
			return new DataStore.MachineGameData { LocationId = _locationId, GameId = gameId };
		}
	}

	public async Task<bool> SetMachineGameDataAsync(string gameId, DataStore.MachineGameData data)
	{
		if (string.IsNullOrEmpty(gameId) || data == null)
			return false;

		try
		{
			data.LocationId = _locationId; // Ensure correct location
			data.GameId = gameId; // Ensure correct game
			data.LastUpdated = DateTime.UtcNow;
			
			var json = JsonSerializer.Serialize(data, JsonOptions);
			
			var (success, _) = await MakeRequestAsync(HttpClient.Method.Post, $"/api/locations/{_locationId}/games/{gameId}/machine-data", json);
			
			if (success)
			{
				GD.Print($"Updated machine-game data for game {gameId} at location {_locationId}");
				return true;
			}
			
			GD.PrintErr($"Failed to set machine-game data for game {gameId}");
			return false;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Exception setting machine-game data for game {gameId}: {ex.Message}");
			return false;
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

	public async Task<bool> SyncUserDataAsync(string phoneNumber)
	{
		try
		{
			var (success, _) = await MakeRequestAsync(HttpClient.Method.Post, $"/api/sync/{phoneNumber}");
			
			if (success)
			{
				GD.Print($"Successfully synced data for user {phoneNumber}");
				return true;
			}
			
			GD.PrintErr($"Failed to sync data for user {phoneNumber}");
			return false;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Exception syncing data for user {phoneNumber}: {ex.Message}");
			return false;
		}
	}

	public string GetCurrentLocationId() => _locationId;

	// ============= GENERIC GAME QUERY MECHANISM =============

	public async Task<Result<TResult>> ExecuteGameQueryAsync<TResult>(
		string gameId,
		string queryType,
		System.Collections.Generic.Dictionary<string, object> parameters)
	{
		if (string.IsNullOrEmpty(gameId))
			return Result<TResult>.Failure("Game ID cannot be null or empty");
		if (string.IsNullOrEmpty(queryType))
			return Result<TResult>.Failure("Query type cannot be null or empty");

		try
		{
			// Build API path for game-specific queries
			string path;
			var method = HttpClient.Method.Get;
			string requestBody = "";

			if (parameters != null && parameters.Count > 0)
			{
				// For GET requests, convert to query string
				if (IsGetQuery(queryType))
				{
					var queryParams = new System.Collections.Generic.List<string>();
					foreach (var param in parameters)
					{
						queryParams.Add($"{param.Key}={Uri.EscapeDataString(param.Value?.ToString() ?? "")}");
					}
					var queryString = string.Join("&", queryParams);
					path = $"/api/{gameId}/{queryType}?{queryString}";
				}
				else
				{
					// For POST requests, serialize parameters as JSON body
					method = HttpClient.Method.Post;
					requestBody = JsonSerializer.Serialize(parameters, JsonOptions);
					path = $"/api/{gameId}/{queryType}";
				}
			}
			else
			{
				path = $"/api/{gameId}/{queryType}";
			}

			// Use the existing MakeRequestAsync method
			var (success, response) = await MakeRequestAsync(method, path, requestBody);

			if (!success)
			{
				return Result<TResult>.Failure($"API request failed for game query '{queryType}'");
			}

			if (string.IsNullOrEmpty(response))
			{
				return Result<TResult>.Failure($"Query '{queryType}' not found for game '{gameId}'");
			}

			// Deserialize response to requested type
			var result = JsonSerializer.Deserialize<TResult>(response, JsonOptions);
			return Result<TResult>.Success(result);
		}
		catch (Exception ex)
		{
			return Result<TResult>.Failure($"Exception executing game query: {ex.Message}");
		}
	}

	/// <summary>
	/// Determine if a query type should use GET or POST method
	/// </summary>
	private bool IsGetQuery(string queryType)
	{
		// Most queries are GET except for submissions
		return !queryType.ToLowerInvariant().Contains("submit");
	}

	// Private helper methods (extracted from original DataStore)

	private string[] GetRequestHeaders(bool includeJsonContentType = false)
	{
		if (!includeJsonContentType)
		{
			return new[]
			{
				$"User-Agent: {USER_AGENT}",
				$"Accept: {CONTENT_TYPE_JSON}"
			};
		}
		
		return new[]
		{
			$"User-Agent: {USER_AGENT}",
			$"Accept: {CONTENT_TYPE_JSON}",
			$"Content-Type: {CONTENT_TYPE_JSON}"
		};
	}

	private bool IsSuccessStatusCode(long responseCode)
	{
		return responseCode >= 200 && responseCode < 300;
	}

	private (bool isError, string errorResponse) ProcessResponseCode(long responseCode)
	{
		if (IsSuccessStatusCode(responseCode))
			return (false, string.Empty);
			
		return (true, responseCode == 404 ? RESPONSE_NOT_FOUND : string.Empty);
	}

	private bool RequiresTls(string baseUrl)
	{
		return baseUrl?.StartsWith("https", StringComparison.OrdinalIgnoreCase) == true;
	}

	private (string host, int port) ParseHostAndPort(Uri uri)
	{
		if (uri == null)
			throw new ArgumentNullException(nameof(uri));
			
		return (uri.Host, uri.Port);
	}

	private void ParseApiUrl(string url)
	{
		var uri = new Uri(url);
		var (host, port) = ParseHostAndPort(uri);
		_apiHost = host;
		_apiPort = port;
	}

	private async Task<bool> ConnectToApiAsync()
	{
		if (_httpClient.GetStatus() == HttpClient.Status.Connected)
			return true;

		var tlsOptions = RequiresTls(_apiBaseUrl) ? TlsOptions.Client() : null;
		var error = _httpClient.ConnectToHost(_apiHost, _apiPort, tlsOptions);
		if (error != Error.Ok)
		{
			GD.PrintErr($"Failed to connect to API host {_apiHost}:{_apiPort} - {error}");
			return false;
		}

		// Cancel any previous connection attempt
		_connectionCancellation?.Cancel();
		_connectionCancellation = new CancellationTokenSource();
		var cancellationToken = _connectionCancellation.Token;
		
		// Wait for connection to be established
		var attempts = 0;
		while (_httpClient.GetStatus() == HttpClient.Status.Connecting && 
			   attempts < MAX_RETRY_ATTEMPTS && 
			   !cancellationToken.IsCancellationRequested)
		{
			await AutoloadBase.StaticDelayAsync(CONNECTION_POLL_DELAY_MS / 1000.0f);
			attempts++;
		}
		
		// Check if cancelled
		if (cancellationToken.IsCancellationRequested)
		{
			GD.Print($"Connection attempt to {_apiHost}:{_apiPort} was cancelled");
			return false;
		}

		if (_httpClient.GetStatus() != HttpClient.Status.Connected)
		{
			GD.PrintErr($"Connection timeout to API host {_apiHost}:{_apiPort}");
			return false;
		}

		return true;
	}

	private async Task<(bool success, string response)> MakeRequestAsync(HttpClient.Method method, string path, string body = "")
	{
		if (!await ConnectToApiAsync())
			return (false, "");

		var headers = GetRequestHeaders(!string.IsNullOrEmpty(body));

		var error = _httpClient.Request(method, path, headers, body);
		if (error != Error.Ok)
		{
			GD.PrintErr($"Failed to make {method} request to {path} - {error}");
			return (false, "");
		}

		// Cancel any previous request polling
		_requestCancellation?.Cancel();
		_requestCancellation = new CancellationTokenSource();
		var requestCancellationToken = _requestCancellation.Token;
		
		// Poll for response with timeout and cancellation
		var pollAttempts = 0;
		const int MAX_POLL_ATTEMPTS = 5000; // 50 seconds timeout (10ms * 5000)
		
		while (_httpClient.GetStatus() == HttpClient.Status.Requesting && 
			   pollAttempts < MAX_POLL_ATTEMPTS && 
			   !requestCancellationToken.IsCancellationRequested)
		{
			await AutoloadBase.StaticDelayAsync(POLLING_DELAY_MS / 1000.0f);
			pollAttempts++;
		}
		
		// Check if cancelled or timed out
		if (requestCancellationToken.IsCancellationRequested)
		{
			GD.Print($"Request to {path} was cancelled");
			return (false, "");
		}
		
		if (pollAttempts >= MAX_POLL_ATTEMPTS)
		{
			GD.PrintErr($"Request to {path} timed out after {MAX_POLL_ATTEMPTS * POLLING_DELAY_MS}ms");
			return (false, "");
		}

		if (_httpClient.GetStatus() != HttpClient.Status.Body && _httpClient.GetStatus() != HttpClient.Status.Connected)
		{
			GD.PrintErr($"Request failed with status: {_httpClient.GetStatus()}");
			return (false, "");
		}

		var responseCode = _httpClient.GetResponseCode();
		var (isError, errorResponse) = ProcessResponseCode(responseCode);
		
		if (isError)
		{
			GD.PrintErr($"HTTP error {responseCode} for {method} {path}");
			return (false, errorResponse);
		}

		var responseBody = "";
		if (_httpClient.HasResponse())
		{
			var responseBytes = _httpClient.ReadResponseBodyChunk();
			responseBody = Encoding.UTF8.GetString(responseBytes);
		}

		return (true, responseBody);
	}

	public async Task<Result<bool>> IsPhoneNumberRegisteredAsync(string phoneNumber)
	{
		// TODO: Implement REST API call to check phone number registration
		// For now, return false (phone number not registered)
		await Task.Delay(1); // Simulate async operation
		return Result<bool>.Success(false);
	}

	public async Task<Result<bool>> IsUsernameTakenAsync(string username)
	{
		// TODO: Implement REST API call to check username availability
		// For now, return false (username not taken)
		await Task.Delay(1); // Simulate async operation
		return Result<bool>.Success(false);
	}

	public async Task<Result<DataStore.GlobalUserData>> CreateUserAccountAsync(string phoneNumber, string pin, string username, string locationId)
	{
		// TODO: Implement REST API call to create user account
		// For now, create a basic user account structure
		await Task.Delay(1); // Simulate async operation

		var userData = new DataStore.GlobalUserData
		{
			PhoneNumber = phoneNumber,
			UserName = username,
			CreatedAtLocation = locationId,
			GlobalCredits = 0,
			LastSyncAt = DateTime.UtcNow,
			CreatedAt = DateTime.UtcNow
		};

		return Result<DataStore.GlobalUserData>.Success(userData);
	}

	public async Task<Result<DataStore.GlobalUserData>> AuthenticateByPhoneAsync(string phoneNumber, string pin)
	{
		// TODO: Implement REST API call to authenticate by phone number
		// For now, fall back to the basic authentication
		await Task.Delay(1); // Simulate async operation

		// In development context, allow simplified authentication
		if (GameHost.IsDevelopmentContext())
		{
			var userData = new DataStore.GlobalUserData
			{
				PhoneNumber = phoneNumber,
				UserName = "DevUser",
				CreatedAtLocation = _locationId,
				GlobalCredits = 100, // Development users get starter credits
				LastSyncAt = DateTime.UtcNow,
				CreatedAt = DateTime.UtcNow
			};

			return Result<DataStore.GlobalUserData>.Success(userData);
		}

		return Result<DataStore.GlobalUserData>.Failure("Phone number authentication not yet implemented for production REST API");
	}

	public void Dispose()
	{
		// Cancel any ongoing operations
		_connectionCancellation?.Cancel();
		_requestCancellation?.Cancel();

		// Dispose cancellation tokens
		_connectionCancellation?.Dispose();
		_requestCancellation?.Dispose();

		// Clean up HTTP client
		_httpClient?.Close();
		_httpClient = null;
	}
}