using Godot;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Orchestrates phased initialization of all application services
/// Ensures services are initialized in correct dependency order before MainController starts
/// </summary>
public partial class ApplicationBootstrap : AutoloadBase
{
	[Signal] public delegate void AllServicesReadyEventHandler();

	private bool _servicesInitialized = false;
	private readonly List<string> _failedServices = new();
	private PosixSignalRegistration _sigtermRegistration;

	private enum ServiceCriticality
	{
		Required,    // Abort if fails
		Optional     // Warn and continue
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
		LogInfo("Phase 1: Foundation services (LocationManager, BackendManager)");
		var phase1Tasks = new[]
		{
			InitializeServiceAsync("LocationManager", LocationManager.GetAutoload(), ServiceCriticality.Optional, cancellationToken),
			InitializeServiceAsync("BackendManager", BackendManager.GetInstance(), ServiceCriticality.Required, cancellationToken)
		};
		var phase1Results = await Task.WhenAll(phase1Tasks);

		// If any REQUIRED service failed, return false
		return !_failedServices.Contains("BackendManager");
	}

	private async Task InitializePhase2Async(CancellationToken cancellationToken)
	{
		LogInfo("Phase 2: Event Service (depends on BackendManager)");
		var eventServiceReady = await InitializeServiceAsync("EventService", EventService.GetInstance(), ServiceCriticality.Optional, cancellationToken);

		if (!eventServiceReady)
		{
			LogError("EventService failed to initialize - credit system will be unavailable");
		}
	}

	private async Task InitializePhase3Async(CancellationToken cancellationToken)
	{
		LogInfo("Phase 3: Session and Payment services (depend on EventService)");
		var phase3Tasks = new[]
		{
			InitializeServiceAsync("SessionManager", SessionManager.GetInstance(), ServiceCriticality.Optional, cancellationToken),
			InitializeServiceAsync("PaymentService", PaymentService.GetInstance(), ServiceCriticality.Optional, cancellationToken)
		};
		await Task.WhenAll(phase3Tasks);
	}

	private async Task InitializePhase4Async(CancellationToken cancellationToken)
	{
		LogInfo("Phase 4: Game services");
		var phase4Tasks = new[]
		{
			InitializeServiceAsync("GameRegistry", GameRegistry.GetAutoload(), ServiceCriticality.Optional, cancellationToken),
			InitializeServiceAsync("GameHost", GameHost.GetInstance(), ServiceCriticality.Optional, cancellationToken)
		};
		await Task.WhenAll(phase4Tasks);
	}

	private async Task InitializePhase5Async(CancellationToken cancellationToken)
	{
		LogInfo("Phase 5: Input and UI services");
		var phase5Tasks = new[]
		{
			InitializeServiceAsync("InputManager", GetAutoload<InputManager>(), ServiceCriticality.Optional, cancellationToken),
			InitializeServiceAsync("UIManager", UIManager.GetInstance(), ServiceCriticality.Optional, cancellationToken)
		};
		await Task.WhenAll(phase5Tasks);
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
