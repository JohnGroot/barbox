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
		
		// 2D time trial racing game with daily seeded race tracks
		RegisterGame(new GameMetadata
		{
			GameId = "racing_game",
			DisplayName = "Racing Game",
			Description = "2D time trial racing game with multiple tracks and competitive timing",
			ScenePath = "res://_Scenes/Games/RacingGame/RacingGame.tscn",
			ThumbnailPath = "res://_Scenes/Games/RacingGame/Assets/thumbnail.png",
			MaxPlayers = 1,
			CreditCost = 1,
			IsActive = true,
			Category = "Racing",
			DifficultyRating = 2.0f
		});

		// Traditional board game with physics-based gameplay
		RegisterGame(new GameMetadata
		{
			GameId = "carrom_game",
			DisplayName = "Carrom",
			Description = "Traditional board game featuring physics-based striking and strategic pocket play",
			ScenePath = "res://_Scenes/Games/CarromGame/CarromGame.tscn",
			ThumbnailPath = "res://_Scenes/Games/CarromGame/Assets/thumbnail.png",
			MaxPlayers = 4,
			CreditCost = 1,
			IsActive = true,
			Category = "Board",
			DifficultyRating = 3.0f
		});
	}
}