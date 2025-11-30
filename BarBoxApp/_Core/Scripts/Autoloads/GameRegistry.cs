using Godot;
using System.Collections.Generic;
using System.Linq;

namespace BarBox.Core.Autoloads;

public partial class GameRegistry : AutoloadBase
{
	[Signal] public delegate void GameLoadedEventHandler(string gameId);
	[Signal] public delegate void GameUnloadedEventHandler(string gameId);

	private Dictionary<string, GameMetadata> _availableGames = new();
	private string _currentGameId = string.Empty;

	protected override void OnServiceEnterTree()
	{
		// Synchronous initialization - guaranteed before any scene loads
		LoadGameConfigurations();
	}

	public static GameRegistry GetAutoload()
	{
		return AutoloadBase.GetAutoload<GameRegistry>();
	}

	public void RegisterGame(GameMetadata gameData)
	{
		if (gameData == null || string.IsNullOrEmpty(gameData.GameId))
		{
			LogError("[GameRegistry] Cannot register game: invalid metadata");
			return;
		}

		if (_availableGames.ContainsKey(gameData.GameId))
		{
			LogWarning($"[GameRegistry] Overwriting existing game: {gameData.GameId}");
		}

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
			ScenePath = "res://_Games/RacingGame/Scenes/RacingGame.tscn",
			ThumbnailPath = "res://_Games/RacingGame/Assets/thumbnail.png",
			MaxPlayers = 1,
			CreditCost = 1000,
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
			ScenePath = "res://_Games/CarromGame/Scenes/CarromGame.tscn",
			ThumbnailPath = "res://_Games/CarromGame/Assets/thumbnail.png",
			MaxPlayers = 4,
			CreditCost = 1000,
			IsActive = true,
			Category = "Board",
			DifficultyRating = 3.0f
		});

		// Idle mining game with upgrades and gem collection
		RegisterGame(new GameMetadata
		{
			GameId = "mining_game",
			DisplayName = "Mining Game",
			Description = "Idle mining game where you collect gems, upgrade your equipment, and extract resources across different locations",
			ScenePath = "res://_Games/MiningGame/Scenes/MiningGame.tscn",
			ThumbnailPath = "res://_Games/MiningGame/Assets/thumbnail.png",
			MaxPlayers = 1,
			CreditCost = 1000,
			IsActive = true,
			Category = "Idle",
			DifficultyRating = 1.5f
		});
	}
}