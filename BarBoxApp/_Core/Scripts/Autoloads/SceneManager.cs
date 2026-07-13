using Godot;

namespace BarBox.Core.Autoloads;

public partial class SceneManager : AutoloadBase
{
	private const string MAIN_MENU_SCENE = "res://_Core/Scenes/Main.tscn";

	private SceneTree _sceneTree;
	private string _currentScenePath = string.Empty;

	protected override void OnServiceReady()
	{
		_sceneTree = GetTree();

		if (_sceneTree == null)
		{
			LogError("SceneTree not available - cannot initialize SceneManager");
			return;
		}

		_currentScenePath = _sceneTree.CurrentScene?.SceneFilePath ?? string.Empty;
		LogInfo($"SceneManager initialized with current scene: {_currentScenePath}");
	}

	public static SceneManager GetAutoload()
	{
		return AutoloadBase.GetAutoload<SceneManager>();
	}

	public void ChangeScene(string scenePath)
	{
		if (string.IsNullOrEmpty(scenePath) || scenePath == _currentScenePath)
			return;

		CallDeferred(nameof(DeferredChangeScene), scenePath);
	}

	private void DeferredChangeScene(string scenePath)
	{
		var error = _sceneTree.ChangeSceneToFile(scenePath);

		if (error == Error.Ok)
		{
			_currentScenePath = scenePath;
			LogInfo($"Scene changed to: {scenePath}");
		}
		else
		{
			LogError($"Failed to change scene to {scenePath}: {error}");
		}
	}

	public void ReturnToMainMenu()
	{
		// Clear current game
		GameRegistry.GetAutoload()?.SetCurrentGame(string.Empty);

		// Return to main menu
		ChangeScene(MAIN_MENU_SCENE);
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