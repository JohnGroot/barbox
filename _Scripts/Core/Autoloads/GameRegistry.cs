using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class GameRegistry : AutoloadBase
{
	[Signal] public delegate void GameLoadedEventHandler(string gameId);
	[Signal] public delegate void GameUnloadedEventHandler(string gameId);

	private Dictionary<string, GameMetadata> _availableGames = new();
	private string _currentGameId = string.Empty;

	protected override void OnServiceReady()
	{
		LoadGameConfigurations();
	}

	public static GameRegistry GetAutoload()
	{
		return AutoloadBase.GetAutoload<GameRegistry>();
	}

	public void RegisterGame(GameMetadata gameData)
	{
		_availableGames[gameData.GameId] = gameData;
	}

	public GameMetadata[] GetAvailableGames()
	{
		return _availableGames.Values.ToArray();
	}

	public GameMetadata GetGameData(string gameId)
	{
		return _availableGames.TryGetValue(gameId, out var gameData) ? gameData : null;
	}

	public bool IsGameAvailable(string gameId)
	{
		return _availableGames.ContainsKey(gameId);
	}

	public string GetCurrentGameId()
	{
		return _currentGameId;
	}

	public void SetCurrentGame(string gameId)
	{
		if (_currentGameId != gameId)
		{
			if (!string.IsNullOrEmpty(_currentGameId))
			{
				EmitSignal(SignalName.GameUnloaded, _currentGameId);
			}
			
			_currentGameId = gameId;
			
			if (!string.IsNullOrEmpty(_currentGameId))
			{
				EmitSignal(SignalName.GameLoaded, _currentGameId);
			}
		}
	}

	private void LoadGameConfigurations()
	{
		// Load from _Data/GameRegistry.json when available
		// For now, register sample games manually
		
		// Rapid prototype game demonstrating new GameHost architecture
		RegisterGame(new GameMetadata
		{
			GameId = "simple_sample_game",
			DisplayName = "Simple Sample Game",
			Description = "Rapid prototype game demonstrating single-file development with GameHost",
			ScenePath = "res://_Scenes/Games/SampleGame/SimpleSampleGame.tscn",
			ThumbnailPath = "res://_Scenes/Games/SampleGame/Assets/Sprites/thumbnail.png",
			MaxPlayers = 1,
			IsActive = true
		});
	}
}