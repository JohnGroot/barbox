using Godot;
using System;
using System.Threading;
using System.Threading.Tasks;

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
	/// Initialize all autoload services in explicit dependency order with async support
	/// </summary>
	private async void InitializeAllServices()
	{
		LogInfo("Initializing all services in staged phases...");

		using var initCancellation = new CancellationTokenSource();
		var cancellationToken = initCancellation.Token;

		try
		{
			// Phase 1: Foundation services (parallel initialization where possible)
			LogInfo("Phase 1: Foundation services (LocationManager, BackendManager)");
			var phase1Tasks = new[]
			{
				InitializeServiceAsync("LocationManager", LocationManager.GetAutoload(), cancellationToken),
				InitializeServiceAsync("BackendManager", BackendManager.GetInstance(), cancellationToken)
			};
			var phase1Results = await Task.WhenAll(phase1Tasks);

			if (!phase1Results[1]) // BackendManager critical
			{
				LogError("BackendManager failed to initialize - aborting service initialization");
				LogError("App will continue with degraded functionality");
				_servicesInitialized = false;
				EmitSignal(SignalName.AllServicesReady); // Still emit so UI knows to proceed
				return;
			}

			// Phase 2: Event Service (depends on BackendManager)
			LogInfo("Phase 2: Event Service (depends on BackendManager)");
			var eventServiceReady = await InitializeServiceAsync("EventService", EventService.GetInstance(), cancellationToken);

			if (!eventServiceReady)
			{
				LogError("EventService failed to initialize - credit system will be unavailable");
			}

			// Phase 3: Services that depend on EventService (can run in parallel)
			LogInfo("Phase 3: Session and Payment services (depend on EventService)");
			var phase3Tasks = new[]
			{
				InitializeServiceAsync("SessionManager", SessionManager.GetInstance(), cancellationToken),
				InitializeServiceAsync("PaymentService", PaymentService.GetInstance(), cancellationToken)
			};
			await Task.WhenAll(phase3Tasks);

			// Phase 4: Game Services (can run in parallel)
			LogInfo("Phase 4: Game services");
			var phase4Tasks = new[]
			{
				InitializeServiceAsync("GameRegistry", GameRegistry.GetAutoload(), cancellationToken),
				InitializeServiceAsync("GameHost", GameHost.GetInstance(), cancellationToken)
			};
			await Task.WhenAll(phase4Tasks);

			// Phase 5: Input/UI Services (can run in parallel)
			LogInfo("Phase 5: Input and UI services");
			var phase5Tasks = new[]
			{
				InitializeServiceAsync("InputManager", GetAutoload<InputManager>(), cancellationToken),
				InitializeServiceAsync("UIManager", UIManager.GetInstance(), cancellationToken)
			};
			await Task.WhenAll(phase5Tasks);

			// Phase 6: Compatibility stubs (can fail gracefully)
			LogInfo("Phase 6: Compatibility services");
			await InitializeServiceAsync("UserManager", UserManager.GetAutoload(), cancellationToken);

			_servicesInitialized = true;
			LogInfo("All services initialized successfully");
		}
		catch (Exception ex)
		{
			LogError($"Critical error during service initialization: {ex.Message}");
			_servicesInitialized = false;
		}
		finally
		{
			// Always emit signal so app can continue
			EmitSignal(SignalName.AllServicesReady);
		}
	}

	/// <summary>
	/// Initialize a single service with async support and error handling
	/// </summary>
	private async Task<bool> InitializeServiceAsync(string serviceName, AutoloadBase service, CancellationToken cancellationToken)
	{
		if (service == null)
		{
			LogWarning($"  ⚠ {serviceName} not available - skipping");
			return false;
		}

		try
		{
			var success = await service.InitializeAsync(cancellationToken);
			if (success)
			{
				LogInfo($"  ✓ {serviceName} ready");
			}
			else
			{
				LogError($"  ✗ {serviceName} failed: {service.FailureReason}");
			}
			return success;
		}
		catch (Exception ex)
		{
			LogError($"  ✗ {serviceName} exception: {ex.Message}");
			return false;
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
		ChangeScene("res://_Core/Scenes/Main.tscn");
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