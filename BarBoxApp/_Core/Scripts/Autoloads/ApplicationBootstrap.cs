using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BarBox.Core.Debug;
using Godot;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Orchestrates phased initialization of all application services
/// Ensures services are initialized in correct dependency order before MainController starts
/// </summary>
public partial class ApplicationBootstrap : AutoloadBase
{
	[Signal]
	public delegate void AllServicesReadyEventHandler();

	private bool _servicesInitialized = false;
	private readonly List<string> _failedServices = new();
	private PosixSignalRegistration _sigtermRegistration;

	private enum ServiceCriticality
	{
		Required,    // Abort if fails
		Optional, // Warn and continue
	}

	protected override void OnServiceEnterTree()
	{
		// Load .env FIRST - before any _Ready() or async initialization runs
		// This ensures environment variables are set before any service reads them
		LoadDotEnvFile();
	}

	protected override void OnServiceReady()
	{
		RegisterSignalHandlers();
		InitializeAllServices();
	}

	/// <summary>
	/// Register POSIX signal handlers for graceful shutdown on Linux.
	/// SIGTERM is sent by systemd when stopping the service.
	/// Without this, Godot ignores SIGTERM and must be force-killed.
	/// </summary>
	private void RegisterSignalHandlers()
	{
		if (!OperatingSystem.IsLinux())
		{
			return;
		}

		try
		{
			_sigtermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSigtermReceived);
			LogInfo("SIGTERM handler registered for graceful shutdown");
		}
		catch (Exception ex)
		{
			LogWarning($"Failed to register SIGTERM handler: {ex.Message}");
		}
	}

	/// <summary>
	/// Handle SIGTERM signal (sent by systemd during service stop).
	/// Triggers graceful shutdown via SessionManager, same as clicking window close.
	/// </summary>
	private void OnSigtermReceived(PosixSignalContext context)
	{
		LogInfo("SIGTERM received - initiating graceful shutdown...");

		// Prevent default termination so we can shutdown gracefully
		context.Cancel = true;

		// Use CallDeferred to run on main thread (signal handlers run on signal thread)
		CallDeferred(MethodName.TriggerGracefulShutdown);
	}

	/// <summary>
	/// Trigger graceful shutdown on the main thread.
	/// This simulates window close request which SessionManager handles.
	/// </summary>
	private void TriggerGracefulShutdown()
	{
		LogInfo("Triggering graceful shutdown from main thread...");

		// Trigger the same notification that clicking X button does
		// SessionManager listens for this and does graceful logout
		GetTree().Root.PropagateNotification((int)NotificationWMCloseRequest);
	}

	/// <summary>
	/// Load environment variables from .env file.
	/// Called in OnServiceEnterTree to ensure env vars are set before any service initialization.
	/// </summary>
	private void LoadDotEnvFile()
	{
		string[] envPaths = ["res://.env.local", "res://.env", "user://.env.local"];

		foreach (var path in envPaths)
		{
			if (FileAccess.FileExists(path))
			{
				LoadEnvFileContents(path);
				GD.Print($"[ApplicationBootstrap] Loaded environment from: {path}");
				return;
			}
		}

		GD.Print("[ApplicationBootstrap] No .env file found, using system environment variables");
	}

	/// <summary>
	/// Parse and load environment variables from a file.
	/// Does NOT overwrite existing environment variables (allows test runners to override .env values).
	/// </summary>
	private void LoadEnvFileContents(string path)
	{
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			GD.PrintErr($"[ApplicationBootstrap] Failed to open .env file at: {path}");
			return;
		}

		while (!file.EofReached())
		{
			var line = file.GetLine().Trim();

			// Skip comments and empty lines
			if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
			{
				continue;
			}

			// Parse KEY=VALUE format
			var parts = line.Split('=', 2);
			if (parts.Length == 2)
			{
				var key = parts[0].Trim();
				var value = parts[1].Trim().Trim('"', '\'');

				// Only set if not already defined (allows test runners to override)
				if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(key)))
				{
					System.Environment.SetEnvironmentVariable(key, value);
				}
			}
		}
	}

	private async void InitializeAllServices()
	{
		LogInfo("Initializing all services in staged phases...");

		using var initCancellation = new CancellationTokenSource();
		var cancellationToken = initCancellation.Token;

		try
		{
			var phase1Success = await InitializePhase1Async(cancellationToken);
			if (!phase1Success)
			{
				LogError("BackendManager failed to initialize - aborting service initialization");
				LogError("App will continue with degraded functionality");
				initCancellation.Cancel();
				_servicesInitialized = false;
				EmitSignal(SignalName.AllServicesReady);
				return;
			}

			await InitializePhase2Async(cancellationToken);
			await InitializePhase3Async(cancellationToken);
			await InitializePhase4Async(cancellationToken);
			await InitializePhase5Async(cancellationToken);

			_servicesInitialized = true;
			LogInfo("All services initialized successfully");

			if (OS.IsDebugBuild())
			{
				var monitor = new DebugPerformanceMonitor();
				GetTree().Root.AddChild(monitor);
			}
		}
		catch (Exception ex)
		{
			LogError($"Critical error during service initialization: {ex.Message}");
			LogError($"Stack trace: {ex.StackTrace}");
			_servicesInitialized = false;
		}
		finally
		{
			if (_failedServices.Count > 0)
			{
				LogWarning($"Services that failed: {string.Join(", ", _failedServices)}");
			}

			// Always emit signal so app can continue
			EmitSignal(SignalName.AllServicesReady);
		}
	}

	private async Task<bool> InitializePhase1Async(CancellationToken cancellationToken)
	{
		// Phase 1a: Configuration service (LocationManager MUST complete first)
		// LocationManager reads all config values and exposes them via properties
		LogInfo("Phase 1: Foundation services (LocationManager, then BackendManager)");
		var locationReady = await InitializeServiceAsync(
			"LocationManager",
			LocationManager.GetAutoload(), ServiceCriticality.Required, cancellationToken);

		if (!locationReady)
		{
			LogError("LocationManager failed - cannot continue (configuration unavailable)");
			return false;
		}

		// Phase 1b: Backend service (depends on LocationManager for config)
		var backendReady = await InitializeServiceAsync(
			"BackendManager",
			BackendManager.GetInstance(), ServiceCriticality.Required, cancellationToken);

		return backendReady;
	}

	private async Task InitializePhase2Async(CancellationToken cancellationToken)
	{
		LogInfo("Phase 2: Transport + Event Service (depends on BackendManager)");

		var backendClientReady = await InitializeServiceAsync("BackendClient", BackendClient.GetInstance(), ServiceCriticality.Optional, cancellationToken);
		if (!backendClientReady)
		{
			LogError("BackendClient failed to initialize - all HTTP-backed systems (credits/auth/emit/stripe) will be unavailable");
		}

		var eventServiceReady = await InitializeServiceAsync("SessionEventService", SessionEventService.GetInstance(), ServiceCriticality.Optional, cancellationToken);
		if (!eventServiceReady)
		{
			LogError("SessionEventService failed to initialize - credit system will be unavailable");
		}
	}

	private async Task InitializePhase3Async(CancellationToken cancellationToken)
	{
		LogInfo("Phase 3: Session and Payment services (depend on SessionEventService)");
		await InitializeServiceAsync("SessionManager", SessionManager.GetInstance(), ServiceCriticality.Optional, cancellationToken);
		await InitializeServiceAsync("PaymentService", PaymentService.GetInstance(), ServiceCriticality.Optional, cancellationToken);
	}

	private async Task InitializePhase4Async(CancellationToken cancellationToken)
	{
		// GameRegistry before GameHost: GameHost resolves GameRegistry on init.
		LogInfo("Phase 4: Game services");
		await InitializeServiceAsync("GameRegistry", GameRegistry.GetAutoload(), ServiceCriticality.Optional, cancellationToken);
		await InitializeServiceAsync("GameHost", GameHost.GetInstance(), ServiceCriticality.Optional, cancellationToken);
	}

	private async Task InitializePhase5Async(CancellationToken cancellationToken)
	{
		LogInfo("Phase 5: Input and UI services");
		await InitializeServiceAsync("InputManager", GetAutoload<InputManager>(), ServiceCriticality.Optional, cancellationToken);
		await InitializeServiceAsync("UIManager", UIManager.GetInstance(), ServiceCriticality.Optional, cancellationToken);
		await InitializeServiceAsync("NotificationService", NotificationService.GetInstance(), ServiceCriticality.Optional, cancellationToken);
	}

	private async Task<bool> InitializeServiceAsync(string serviceName, AutoloadBase service, ServiceCriticality criticality, CancellationToken cancellationToken)
	{
		if (service == null)
		{
			LogWarning($"  [SKIP] {serviceName} not available");
			if (criticality == ServiceCriticality.Required)
			{
				_failedServices.Add(serviceName);
			}

			return false;
		}

		try
		{
			var success = await service.InitializeAsync(cancellationToken);
			if (success)
			{
				LogInfo($"  [OK] {serviceName} ready");
			}
			else
			{
				LogError($"  [FAIL] {serviceName} failed: {service.FailureReason}");
				if (criticality == ServiceCriticality.Required)
				{
					_failedServices.Add(serviceName);
				}
			}

			return success;
		}
		catch (Exception ex)
		{
			LogError($"  [EXCEPTION] {serviceName}: {ex.Message}");
			if (criticality == ServiceCriticality.Required)
			{
				_failedServices.Add(serviceName);
			}

			return false;
		}
	}

	public bool AreServicesReady() => _servicesInitialized;

	public override void _ExitTree()
	{
		_sigtermRegistration?.Dispose();
		_sigtermRegistration = null;
	}

	public static ApplicationBootstrap GetAutoload()
	{
		return AutoloadBase.GetAutoload<ApplicationBootstrap>();
	}
}
