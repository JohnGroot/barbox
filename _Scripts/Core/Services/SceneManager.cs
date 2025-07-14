using Godot;

public partial class SceneManager : AutoloadBase
{
	[Signal] public delegate void SceneChangedEventHandler(string scenePath);
	[Signal] public delegate void SceneChangeStartedEventHandler(string scenePath);

	private SceneTree _sceneTree;
	private string _currentScenePath = string.Empty;

	protected override void OnServiceReady()
	{
		_sceneTree = GetTree();
		_currentScenePath = _sceneTree.CurrentScene?.SceneFilePath ?? string.Empty;
	}

	public static SceneManager GetAutoload()
	{
		return AutoloadBase.GetAutoload<SceneManager>();
	}

	public void ChangeScene(string scenePath)
	{
		if (string.IsNullOrEmpty(scenePath) || scenePath == _currentScenePath)
			return;

		EmitSignal(SignalName.SceneChangeStarted, scenePath);
		
		CallDeferred(nameof(DeferredChangeScene), scenePath);
	}

	private void DeferredChangeScene(string scenePath)
	{
		var error = _sceneTree.ChangeSceneToFile(scenePath);
		
		if (error == Error.Ok)
		{
			_currentScenePath = scenePath;
			EmitSignal(SignalName.SceneChanged, scenePath);
		}
		else
		{
			GD.PrintErr($"Failed to change scene to {scenePath}: {error}");
		}
	}

	public void ReturnToMainMenu()
	{
		// Clear current game
		GameRegistry.GetAutoload()?.SetCurrentGame(string.Empty);
		
		// Return to main menu
		ChangeScene("res://_Scenes/Main.tscn");
	}

	public string GetCurrentScenePath()
	{
		return _currentScenePath;
	}

	public bool IsInGame()
	{
		return !string.IsNullOrEmpty(GameRegistry.GetAutoload()?.GetCurrentGameId());
	}
}