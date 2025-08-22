using Godot;
using System.Collections.Generic;

/// <summary>
/// Abstract base class for all autoload services in the project
/// Provides common functionality like service discovery, logging, and signal management
/// Reduces boilerplate code and ensures consistent patterns across autoloads
/// </summary>
[GlobalClass]
public abstract partial class AutoloadBase : Node
{
	/// <summary>
	/// The service name used for group registration and logging
	/// Defaults to the class name but can be overridden
	/// </summary>
	protected virtual string ServiceName => GetType().Name;

	private bool _isInitialized = false;

	/// <summary>
	/// Called during _Ready() to perform service-specific setup (minimal initialization only)
	/// Override this instead of _Ready() in derived classes
	/// </summary>
	protected abstract void OnServiceReady();

	/// <summary>
	/// Called explicitly by SceneManager to perform full service initialization
	/// Override this for services that need explicit initialization order
	/// </summary>
	protected virtual void OnServiceInitialize()
	{
		// Default implementation does nothing - override in derived classes
	}

	public override void _Ready()
	{
		// Automatic group registration using service name
		AddToGroup(ServiceName);
		
		// Log service construction
		LogInfo($"{ServiceName} autoload constructed");
		
		// Call derived class setup (minimal only)
		OnServiceReady();
	}

	/// <summary>
	/// Explicitly initialize the service - called by SceneManager in dependency order
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
		LogInfo($"{ServiceName} initialized");
	}

	/// <summary>
	/// Check if this service has been explicitly initialized
	/// </summary>
	public bool IsInitialized => _isInitialized;

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