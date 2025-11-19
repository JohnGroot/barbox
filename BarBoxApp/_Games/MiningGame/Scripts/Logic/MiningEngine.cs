using System;
using Godot;

namespace BarBox.Games.MiningGame.Logic;

public partial class MiningEngine : Node
{
	private MiningGame _game;
	private bool _isMiningActive;

	public MiningEngine(MiningGame game)
	{
		if (game == null)
		{
			throw new ArgumentNullException(nameof(game), "MiningEngine requires valid game reference");
		}

		_game = game;
		Name = "Engine";
	}

	public override void _Process(double delta)
	{
		// Components guaranteed valid by game lifecycle - no defensive checks needed
		if (!_isMiningActive) return;

		_game.GetState().ProcessReadyMiningTicks();

		// Update UI - direct call, guaranteed valid
		_game.GetUI()?.UpdateMiningProgress();
	}

	public void StartMining()
	{
		_isMiningActive = true;
		GD.Print("[GameEngine] Mining started");
	}
			
	public void StopMining()
	{
		_isMiningActive = false;
		GD.Print("[GameEngine] Mining stopped");
	}

	public float GetMiningProgress()
	{
		// State guaranteed valid by game lifecycle
		if (!_isMiningActive)
			return 0.0f;

		var (_, progress) = _game.GetState().CalculateMiningProgress();
		return progress;
	}

	public float GetTimeUntilNextTick()
	{
		// State guaranteed valid by game lifecycle
		if (!_isMiningActive)
			return 0.0f;

		var (_, progress) = _game.GetState().CalculateMiningProgress();
		var miningTickTime = _game.GetState().GetMiningTickTime();
		return (1.0f - progress) * miningTickTime;
	}
			
}