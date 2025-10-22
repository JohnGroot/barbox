using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BarBox.Games.MiningGame;

/// <summary>
/// Mining game-specific event service
/// Provides type-safe methods for emitting mining events
/// </summary>
public partial class MiningEventService : Node
{
	private EventService _eventService;

	public override void _Ready()
	{
		_eventService = EventService.GetInstance();
		if (_eventService == null)
		{
			GD.PrintErr("MiningEventService: EventService not found");
		}
	}

	/// <summary>
	/// Emit extraction complete event
	/// </summary>
	public async Task<Result<bool>> EmitExtractCompleteAsync(GemType gemType, int quantity)
	{
		if (_eventService == null)
			return Result<bool>.Failure("EventService not initialized");

		var payload = new
		{
			gem_type = gemType.ToString().ToLowerInvariant(),
			quantity = quantity,
			location_id = _eventService.GetCurrentLocationId()
		};

		return await _eventService.EmitEventAsync("mining/extract_complete", payload);
	}

	/// <summary>
	/// Emit upgrade purchase event
	/// </summary>
	public async Task<Result<bool>> EmitUpgradePurchaseAsync(
		UpgradeType upgradeType,
		int level,
		Dictionary<GemType, int> cost)
	{
		if (_eventService == null)
			return Result<bool>.Failure("EventService not initialized");

		// Convert cost dictionary to string keys
		var costDict = new Dictionary<string, int>();
		foreach (var kvp in cost)
		{
			costDict[kvp.Key.ToString().ToLowerInvariant()] = kvp.Value;
		}

		var payload = new
		{
			upgrade_type = upgradeType.ToString().ToLowerInvariant(),
			level = level,
			cost = costDict
		};

		return await _eventService.EmitEventAsync("mining/upgrade_purchase", payload);
	}

	/// <summary>
	/// Emit credit deposit event when gems are spent for credits
	/// </summary>
	public async Task<Result<bool>> EmitCreditDepositAsync(
		GemType gemType,
		int gemsSpent,
		int creditsEarned)
	{
		if (_eventService == null)
			return Result<bool>.Failure("EventService not initialized");

		var payload = new
		{
			gem_type = gemType.ToString().ToLowerInvariant(),
			gems_spent = gemsSpent,
			credits_earned = creditsEarned
		};

		return await _eventService.EmitEventAsync("mining/credit_deposit", payload);
	}

	/// <summary>
	/// Get player's global mining inventory from backend
	/// </summary>
	public async Task<Result<MiningInventoryData>> GetPlayerInventoryAsync(Guid playerId)
	{
		if (_eventService == null)
			return Result<MiningInventoryData>.Failure("EventService not initialized");

		try
		{
			var httpClient = new HttpClient();

			// Connect to backend
			var error = httpClient.ConnectToHost("localhost", 8000);
			if (error != Error.Ok)
				return Result<MiningInventoryData>.Failure($"Connection failed: {error}");

			// Wait for connection
			var attempts = 0;
			while (httpClient.GetStatus() == HttpClient.Status.Connecting && attempts < 50)
			{
				await AutoloadBase.StaticDelayAsync(0.1f);
				attempts++;
			}

			if (httpClient.GetStatus() != HttpClient.Status.Connected)
				return Result<MiningInventoryData>.Failure("Connection timeout");

			// Make HTTP GET request
			var headers = new[]
			{
				"User-Agent: BarBox-Client/1.0",
				"Accept: application/json"
			};

			error = httpClient.Request(
				HttpClient.Method.Get,
				$"/game/mining/player/{playerId}/inventory",
				headers
			);

			if (error != Error.Ok)
			{
				httpClient.Close();
				return Result<MiningInventoryData>.Failure($"Request failed: {error}");
			}

			// Wait for response
			attempts = 0;
			while (httpClient.GetStatus() == HttpClient.Status.Requesting && attempts < 500)
			{
				await AutoloadBase.StaticDelayAsync(0.01f);
				attempts++;
			}

			if (httpClient.GetStatus() == HttpClient.Status.Requesting)
			{
				httpClient.Close();
				return Result<MiningInventoryData>.Failure("Request timeout");
			}

			var responseCode = httpClient.GetResponseCode();
			if (responseCode != 200)
			{
				httpClient.Close();
				return Result<MiningInventoryData>.Failure($"Server returned {responseCode}");
			}

			var responseBody = httpClient.GetResponseBodyAsString();
			httpClient.Close();

			// Parse JSON response
			var json = Json.ParseString(responseBody);
			if (json == null)
				return Result<MiningInventoryData>.Failure("Failed to parse response");

			var jsonDict = json.AsGodotDictionary();

			var inventoryData = new MiningInventoryData
			{
				PlayerId = Guid.Parse(jsonDict["player_id"].AsString()),
				LastUpdated = DateTime.Parse(jsonDict["last_updated"].AsString()),
				Gems = new Dictionary<string, int>()
			};

			var gemsDict = jsonDict["gems"].AsGodotDictionary();
			foreach (var key in gemsDict.Keys)
			{
				inventoryData.Gems[key.AsString()] = gemsDict[key].AsInt32();
			}

			return Result<MiningInventoryData>.Success(inventoryData);
		}
		catch (Exception ex)
		{
			return Result<MiningInventoryData>.Failure($"Exception: {ex.Message}");
		}
	}
}

/// <summary>
/// Mining inventory data structure
/// </summary>
public class MiningInventoryData
{
	public Guid PlayerId { get; set; }
	public Dictionary<string, int> Gems { get; set; }
	public DateTime LastUpdated { get; set; }
}
