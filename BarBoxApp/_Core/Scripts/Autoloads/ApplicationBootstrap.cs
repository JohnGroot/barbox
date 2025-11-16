using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Orchestrates phased initialization of all application services
/// Ensures services are initialized in correct dependency order before MainController starts
/// </summary>
public partial class ApplicationBootstrap : AutoloadBase
{
	[Signal] public delegate void AllServicesReadyEventHandler();

	private bool _servicesInitialized = false;
	private readonly List<string> _failedServices = new();

	private enum ServiceCriticality
	{
		Required,    // Abort if fails
		Optional     // Warn and continue
	}

	protected override void OnServiceReady()
	{
		// All autoloads have completed OnServiceEnterTree() by this point
		// Safe to call async initialization directly (no CallDeferred needed)
		InitializeAllServices();
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
			var phase1Success = await InitializePhase1Async(cancellationToken);
			if (!phase1Success)
			{
				LogError("BackendManager failed to initialize - aborting service initialization");
				LogError("App will continue with degraded functionality");
				initCancellation.Cancel(); // Cancel remaining initialization
				_servicesInitialized = false;
				EmitSignal(SignalName.AllServicesReady); // Still emit so UI knows to proceed
				return;
			}

			// Phase 2: Event Service (depends on BackendManager)
			await InitializePhase2Async(cancellationToken);

			// Phase 3: Services that depend on EventService (can run in parallel)
			await InitializePhase3Async(cancellationToken);

			// Phase 4: Game Services (can run in parallel)
			await InitializePhase4Async(cancellationToken);

			// Phase 5: Input/UI Services (can run in parallel)
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

	/// <summary>
	/// Initialize a single service with async support and error handling
	/// </summary>
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

	/// <summary>
	/// Check if all services have been initialized
	/// </summary>
	public bool AreServicesReady() => _servicesInitialized;

	public static ApplicationBootstrap GetAutoload()
	{
		return AutoloadBase.GetAutoload<ApplicationBootstrap>();
	}
}
