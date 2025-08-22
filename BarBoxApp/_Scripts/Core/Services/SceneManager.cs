using Godot;
using System;

public partial class SceneManager : AutoloadBase
{
	[Signal] public delegate void SceneChangedEventHandler(string scenePath);
	[Signal] public delegate void SceneChangeStartedEventHandler(string scenePath);
	[Signal] public delegate void AllServicesReadyEventHandler();

	private SceneTree _sceneTree;
	private string _currentScenePath = string.Empty;
	private bool _servicesInitialized = false;

	protected override void OnServiceReady()
	{
		_sceneTree = GetTree();
		_currentScenePath = _sceneTree.CurrentScene?.SceneFilePath ?? string.Empty;
		
		// Initialize all services in dependency order
		CallDeferred(nameof(InitializeAllServices));
	}

	/// <summary>
	/// Initialize all autoload services in explicit dependency order
	/// </summary>
	private void InitializeAllServices()
	{
		LogInfo("Initializing all services in dependency order...");

		// Phase 1: Core Data Services (no dependencies)
		InitializeService("DataStore", () => DataStore.GetInstance()?.Initialize());

		// Phase 2: Services that depend on DataStore
		InitializeService("SessionManager", () => SessionManager.GetInstance()?.Initialize());
		InitializeService("LocationManager", () => LocationManager.GetAutoload()?.Initialize());

		// Phase 3: Game Services
		InitializeService("GameRegistry", () => GameRegistry.GetAutoload()?.Initialize());
		InitializeService("GameHost", () => GameHost.GetInstance()?.Initialize());

		// Phase 4: Input/UI Services
		InitializeService("InputManager", () => GetAutoload<InputManager>()?.Initialize());
		InitializeService("UIManager", () => UIManager.GetInstance()?.Initialize());

		// Phase 5: Compatibility stubs (can fail gracefully)
		InitializeService("UserManager", () => UserManager.GetAutoload()?.Initialize());

		_servicesInitialized = true;
		LogInfo("All services initialized successfully");
		
		// Emit signal that services are ready
		EmitSignal(SignalName.AllServicesReady);
	}

	/// <summary>
	/// Initialize a single service with error handling
	/// </summary>
	private void InitializeService(string serviceName, Action initializer)
	{
		try
		{
			initializer?.Invoke();
			LogInfo($"  ✓ {serviceName} initialized");
		}
		catch (Exception ex)
		{
			LogError($"  ✗ {serviceName} failed to initialize: {ex.Message}");
		}
	}

	/// <summary>
	/// Check if all services have been initialized
	/// </summary>
	public bool AreServicesReady() => _servicesInitialized;

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