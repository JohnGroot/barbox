using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Abstract base class for all autoload services in the project
/// Provides common functionality like service discovery, logging, and signal management
/// Reduces boilerplate code and ensures consistent patterns across autoloads
/// </summary>
[GlobalClass]
public abstract partial class AutoloadBase : Node
{
	/// <summary>
	/// Service lifecycle states for staged initialization
	/// </summary>
	public enum ServiceState
	{
		Constructed,	// _Ready() called, basic setup done
		Initializing,	// InitializeAsync() called, async work in progress
		Ready,			// Fully operational, can service requests
		Failed			// Initialization failed, degraded mode
	}

	/// <summary>
	/// The service name used for group registration and logging
	/// Defaults to the class name but can be overridden
	/// </summary>
	protected virtual string ServiceName => GetType().Name;

	private bool _isInitialized = false;
	private ServiceState _state = ServiceState.Constructed;
	private string _failureReason = string.Empty;
	private TaskCompletionSource<bool> _readySignal = new();

	/// <summary>
	/// Called during _EnterTree() to perform synchronous service initialization
	/// This fires BEFORE any scene _Ready() methods, guaranteeing service availability
	/// Override this for critical service setup (NO async/await, NO CallDeferred allowed)
	/// All autoloads will have completed OnServiceEnterTree() before any scene loads
	/// </summary>
	protected virtual void OnServiceEnterTree()
	{
		// Default implementation does nothing - override in derived classes for sync initialization
	}

	/// <summary>
	/// Called during _Ready() to perform service-specific setup (async work allowed)
	/// By this point, all autoloads have completed OnServiceEnterTree() synchronously
	/// Override this for async initialization or signal connections
	/// </summary>
	protected virtual void OnServiceReady()
	{
		// Default implementation does nothing - override in derived classes
	}

	/// <summary>
	/// Called explicitly by SceneManager to perform full service initialization
	/// Override this for services that need explicit initialization order
	/// </summary>
	protected virtual void OnServiceInitialize()
	{
		// Default implementation does nothing - override in derived classes
	}

	/// <summary>
	/// Called explicitly by SceneManager to perform async service initialization
	/// Override this in services that need async operations (backend health checks, etc.)
	/// </summary>
	protected virtual async Task OnServiceInitializeAsync(CancellationToken cancellationToken = default)
	{
		// Default: synchronous services call OnServiceInitialize()
		OnServiceInitialize();
		await Task.CompletedTask;
	}

	/// <summary>
	/// Godot lifecycle: _EnterTree() fires first (top-down, autoloads before scenes)
	/// This is where we guarantee synchronous initialization completes
	/// </summary>
	public override void _EnterTree()
	{
		// Automatic group registration using service name
		AddToGroup(ServiceName);

		// Log service entering tree
		LogInfo($"{ServiceName} entering tree");

		// Call derived class synchronous initialization
		// All autoloads complete this phase BEFORE any scene loads
		OnServiceEnterTree();
	}

	/// <summary>
	/// Godot lifecycle: _Ready() fires after _EnterTree() (bottom-up traversal)
	/// All autoloads have completed OnServiceEnterTree() by this point
	/// Safe to perform async operations or connect signals
	/// </summary>
	public override void _Ready()
	{
		// Log service ready
		LogInfo($"{ServiceName} ready");

		// Call derived class async/deferred setup
		OnServiceReady();
	}

	/// <summary>
	/// Explicitly initialize the service - called by SceneManager in dependency order
	/// Legacy synchronous method maintained for backward compatibility
	/// </summary>
	public void Initialize()
	{
		if (_isInitialized)
		{
			LogWarning($"{ServiceName} already initialized, skipping");
			return;
		}

		LogInfo($"{ServiceName} initializing...");
		OnServiceInitialize();
		_isInitialized = true;
		_state = ServiceState.Ready;
		_readySignal.TrySetResult(true);
		LogInfo($"{ServiceName} initialized");
	}

	/// <summary>
	/// Async initialization with proper error handling and cancellation support
	/// Called by SceneManager for staged initialization
	/// </summary>
	public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
	{
		if (_state >= ServiceState.Initializing)
		{
			LogWarning($"{ServiceName} already initializing/ready, waiting for completion");
			return await WaitForReadyAsync(30.0f, cancellationToken);
		}

		_state = ServiceState.Initializing;
		LogInfo($"{ServiceName} initializing...");

		try
		{
			await OnServiceInitializeAsync(cancellationToken);
			_state = ServiceState.Ready;
			_isInitialized = true;
			_readySignal.TrySetResult(true);
			LogInfo($"{ServiceName} ready");
			return true;
		}
		catch (OperationCanceledException)
		{
			_state = ServiceState.Failed;
			_failureReason = "Initialization cancelled";
			_readySignal.TrySetResult(false);
			LogWarning($"{ServiceName} initialization cancelled");
			return false;
		}
		catch (Exception ex)
		{
			_state = ServiceState.Failed;
			_failureReason = ex.Message;
			_readySignal.TrySetResult(false);
			LogError($"{ServiceName} initialization failed: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Wait for service to become ready with timeout
	/// Used by dependent services to wait for initialization to complete
	/// </summary>
	public async Task<bool> WaitForReadyAsync(float timeoutSeconds = 30.0f, CancellationToken cancellationToken = default)
	{
		if (_state == ServiceState.Ready)
			return true;

		if (_state == ServiceState.Failed)
		{
			LogWarning($"Cannot wait for {ServiceName} - service failed: {_failureReason}");
			return false;
		}

		using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

		try
		{
			return await _readySignal.Task.WaitAsync(cts.Token);
		}
		catch (OperationCanceledException)
		{
			LogWarning($"{ServiceName} ready wait timed out after {timeoutSeconds}s");
			return false;
		}
	}

	/// <summary>
	/// Check if this service has been explicitly initialized (legacy property)
	/// </summary>
	public bool IsInitialized => _isInitialized;

	/// <summary>
	/// Check if this service is ready to handle requests
	/// </summary>
	public bool IsReady => _state == ServiceState.Ready;

	/// <summary>
	/// Get the current lifecycle state of the service
	/// </summary>
	public ServiceState State => _state;

	/// <summary>
	/// Get the failure reason if service is in Failed state
	/// </summary>
	public string FailureReason => _failureReason;

	/// <summary>
	/// Generic service discovery method for finding other autoloads
	/// Provides fallback mechanisms for robust service location
	/// </summary>
	/// <typeparam name="T">The autoload service type to find</typeparam>
	/// <returns>The autoload instance or null if not found</returns>
	public static T GetAutoload<T>() where T : AutoloadBase
	{
		if (Engine.GetMainLoop() is not SceneTree tree) 
			return null;

		string serviceName = typeof(T).Name;

		// Primary: Group-based discovery
		if (tree.GetFirstNodeInGroup(serviceName) is T serviceFromGroup) 
			return serviceFromGroup;

		// Fallback: Direct node path discovery
		var serviceFromPath = tree.Root?.GetNode($"/root/{serviceName}") as T;
		return serviceFromPath;
	}


	/// <summary>
	/// Safely connects a signal from a node if the signal exists
	/// Uses nameof for compile-time method validation and runtime signal validation
	/// </summary>
	protected bool TryConnectSignal(Node node, string signalName, string methodName, Callable callable)
	{
		if (node?.HasSignal(signalName) == true)
		{
			node.Connect(signalName, callable);
			LogInfo($"Connected signal '{signalName}' to method '{methodName}' on {node.GetType().Name}");
			return true;
		}
		else
		{
			LogInfo($"Signal '{signalName}' not found on {node?.GetType().Name ?? "null"}, skipping connection to '{methodName}'");
			return false;
		}
	}

	/// <summary>
	/// Safely disconnects a signal from a node if connected
	/// </summary>
	protected bool TryDisconnectSignal(Node node, string signalName, Callable callable)
	{
		if (GodotObject.IsInstanceValid(node) && node.HasSignal(signalName) && node.IsConnected(signalName, callable))
		{
			node.Disconnect(signalName, callable);
			return true;
		}
		return false;
	}

	/// <summary>
	/// Standardized logging with service name prefix
	/// </summary>
	protected void LogInfo(string message)
	{
		GD.Print($"[{ServiceName}] {message}");
	}

	/// <summary>
	/// Standardized error logging with service name prefix
	/// </summary>
	protected void LogError(string message)
	{
		GD.PrintErr($"[{ServiceName}] ERROR: {message}");
	}

	/// <summary>
	/// Standardized warning logging with service name prefix
	/// </summary>
	protected void LogWarning(string message)
	{
		GD.Print($"[{ServiceName}] WARNING: {message}");
	}

	/// <summary>
	/// Frame-aware delay using Godot's timer system instead of Task.Delay()
	/// This ensures timing respects game pause states and frame drops
	/// Use this instead of Task.Delay() for Godot-aware timing
	/// </summary>
	/// <param name="seconds">Delay duration in seconds</param>
	/// <returns>Task that completes after the specified delay</returns>
	protected async System.Threading.Tasks.Task DelayAsync(float seconds)
	{
		if (seconds <= 0.0f) return;
		
		var timer = GetTree().CreateTimer(seconds);
		await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
	}

	/// <summary>
	/// Static frame-aware delay for non-Node classes using Godot's timer system
	/// This ensures timing respects game pause states and frame drops
	/// Use this instead of Task.Delay() for Godot-aware timing in utility classes
	/// </summary>
	/// <param name="seconds">Delay duration in seconds</param>
	/// <returns>Task that completes after the specified delay</returns>
	public static async System.Threading.Tasks.Task StaticDelayAsync(float seconds)
	{
		if (seconds <= 0.0f) return;
		
		var mainLoop = Engine.GetMainLoop();
		if (mainLoop is not SceneTree tree)
		{
			// Fallback to Task.Delay if no scene tree available
			await System.Threading.Tasks.Task.Delay((int)(seconds * 1000));
			return;
		}
		
		var timer = tree.CreateTimer(seconds);
		await tree.ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
	}

	/// <summary>
	/// Cleanup method called when the service is being destroyed
	/// Override this for service-specific cleanup
	/// </summary>
	protected virtual void OnServiceDestroyed()
	{
		// Default implementation does nothing
	}

	public override void _ExitTree()
	{
		LogInfo($"{ServiceName} autoload shutting down");
		OnServiceDestroyed();
		base._ExitTree();
	}
}