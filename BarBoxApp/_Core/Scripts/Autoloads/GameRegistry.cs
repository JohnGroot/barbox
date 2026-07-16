using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BarBox.Core.Autoloads;

public partial class GameRegistry : AutoloadBase
{
	[Signal]
	public delegate void GameLoadedEventHandler(string gameId);

	[Signal]
	public delegate void GameUnloadedEventHandler(string gameId);

	private Dictionary<string, GameMetadata> _availableGames = new();
	private string _currentGameId = string.Empty;

	protected override void OnServiceEnterTree()
	{
		// Synchronous initialization - guaranteed before any scene loads
		LoadGameConfigurations();
		ValidateGameConfigurations();
	}

	public static GameRegistry GetAutoload()
	{
		return AutoloadBase.GetAutoload<GameRegistry>();
	}

	public void RegisterGame(GameMetadata gameData)
	{
		if (gameData == null || string.IsNullOrEmpty(gameData.GameId))
		{
			throw new InvalidOperationException("[GameRegistry] Cannot register game: invalid metadata");
		}

		if (_availableGames.ContainsKey(gameData.GameId))
		{
			throw new InvalidOperationException($"[GameRegistry] Duplicate game id: {gameData.GameId}");
		}

		_availableGames[gameData.GameId] = gameData;
	}

	/// <summary>
	/// Boot-time check that every registered game's scene actually exists.
	/// Catches a bad ScenePath at startup instead of a silent no-op the next
	/// time someone tries to load that game.
	/// </summary>
	private void ValidateGameConfigurations()
	{
		foreach (var game in _availableGames.Values)
		{
			if (!ResourceLoader.Exists(game.ScenePath))
			{
				throw new InvalidOperationException(
					$"[GameRegistry] Game '{game.GameId}' has a missing ScenePath: {game.ScenePath}");
			}
		}
	}

	public GameMetadata[] GetAvailableGames()
	{
		return [.. _availableGames.Values];
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
		// 2D time trial racing game with daily seeded race tracks
		RegisterGame(new GameMetadata
		{
			GameId = "racing",
			DisplayName = "Racing Game",
			Description = "2D time trial racing game with multiple tracks and competitive timing",
			ScenePath = "res://_Games/RacingGame/Scenes/RacingGame.tscn",
			ThumbnailPath = "res://_Games/RacingGame/Assets/thumbnail.png",
			MaxPlayers = 1,
			CreditCost = 1000,
			IsActive = true,
			Category = "Racing",
			DifficultyRating = 2.0f,
		});

		// Traditional board game with physics-based gameplay
		RegisterGame(new GameMetadata
		{
			GameId = "carrom",
			DisplayName = "Carrom",
			Description = "Traditional board game featuring physics-based striking and strategic pocket play",
			ScenePath = "res://_Games/CarromGame/Scenes/CarromGame.tscn",
			ThumbnailPath = "res://_Games/CarromGame/Assets/thumbnail.png",
			MaxPlayers = 4,
			CreditCost = 1000,
			IsActive = true,
			Category = "Board",
			DifficultyRating = 3.0f,
		});

		// Idle mining game with upgrades and gem collection
		RegisterGame(new GameMetadata
		{
			GameId = "mining",
			DisplayName = "Mining Game",
			Description = "Idle mining game where you collect gems, upgrade your equipment, and extract resources across different locations",
			ScenePath = "res://_Games/MiningGame/Scenes/MiningGame.tscn",
			ThumbnailPath = "res://_Games/MiningGame/Assets/thumbnail.png",
			MaxPlayers = 1,
			CreditCost = 1000,
			IsActive = true,
			Category = "Idle",
			DifficultyRating = 1.5f,
		});

		// Card prediction game with time-based jackpot
		RegisterGame(new GameMetadata
		{
			GameId = "nines",
			DisplayName = "Nines",
			Description = "Card prediction game where players guess higher, lower, or same. Survive the deck to win the jackpot!",
			ScenePath = "res://_Games/Nines/Nines.tscn",
			ThumbnailPath = "res://_Games/Nines/Assets/thumbnail.png",
			MaxPlayers = 8,
			CreditCost = 100,
			IsActive = true,
			Category = "Card",
			DifficultyRating = 2.0f,
		});
	}
}
