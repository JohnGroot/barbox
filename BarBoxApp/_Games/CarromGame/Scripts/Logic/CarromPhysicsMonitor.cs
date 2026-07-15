using Godot;
using System.Collections.Generic;

namespace BarBox.Games.Carrom;

/// <summary>
/// Monitors physics bodies to detect when all pieces have stopped moving
/// Extracted from GameStateManager to separate physics polling from state management
/// </summary>
public partial class CarromPhysicsMonitor : Node
{
	// ================================================================
	// SIGNALS
	// ================================================================

	[Signal] public delegate void AllPiecesStoppedEventHandler();

	// ================================================================
	// CONFIGURATION
	// ================================================================

	[Export] public float StoppedVelocityThreshold { get; set; } = 1.0f;
	[Export] public int MinimumSettlementFrames { get; set; } = 3;

	// ================================================================
	// PRIVATE FIELDS
	// ================================================================

	private bool _isMonitoringActive = false;
	private int _physicsFramesSinceStart = 0;
	private CarromPieceFactory _pieceFactory;

	// Debug timing
	private static readonly bool _isDebugBuild = OS.IsDebugBuild();
	private System.Diagnostics.Stopwatch _debugStopwatch;
	public static double DebugElapsedMs { get; private set; }

	// ================================================================
	// INITIALIZATION
	// ================================================================

	public void Initialize(CarromPieceFactory pieceFactory)
	{
		_pieceFactory = pieceFactory;
		GD.Print("[CarromPhysicsMonitor] Initialized");
	}

	// ================================================================
	// MONITORING CONTROL
	// ================================================================

	/// <summary>
	/// Start monitoring piece velocities
	/// </summary>
	public void StartMonitoring()
	{
		if (_isMonitoringActive)
			return;

		_isMonitoringActive = true;
		_physicsFramesSinceStart = 0;
		SetPhysicsProcess(true);
		GD.Print("[CarromPhysicsMonitor] Started monitoring");
	}

	/// <summary>
	/// Stop monitoring piece velocities
	/// </summary>
	public void StopMonitoring()
	{
		if (!_isMonitoringActive)
			return;

		_isMonitoringActive = false;
		_physicsFramesSinceStart = 0;
		SetPhysicsProcess(false);
		GD.Print("[CarromPhysicsMonitor] Stopped monitoring");
	}

	// ================================================================
	// PHYSICS MONITORING
	// ================================================================

	public override void _PhysicsProcess(double delta)
	{
		if (!_isMonitoringActive)
			return;

		_physicsFramesSinceStart++;

		// Wait minimum frames before checking settlement
		if (_physicsFramesSinceStart < MinimumSettlementFrames)
			return;

		if (_isDebugBuild)
		{
			_debugStopwatch ??= new System.Diagnostics.Stopwatch();
			_debugStopwatch.Restart();
		}

		// Check all pieces every physics frame
		bool allStopped = AreAllPiecesStopped();

		if (_isDebugBuild)
		{
			_debugStopwatch.Stop();
			DebugElapsedMs = _debugStopwatch.Elapsed.TotalMilliseconds;
		}

		if (allStopped)
		{
			StopMonitoring();
			EmitSignal(SignalName.AllPiecesStopped);
			GD.Print("[CarromPhysicsMonitor] All pieces stopped - signal emitted");
		}
	}

	private bool AreAllPiecesStopped()
	{
		// Validate piece factory exists before checking pieces
		if (_pieceFactory == null || !GodotObject.IsInstanceValid(_pieceFactory))
		{
			GD.PrintErr("[CarromPhysicsMonitor] Piece factory not available - cannot determine if pieces are stopped");
			return false;
		}

		// Poll piece factory every frame - always includes striker (no timing dependency)
		var allPieces = _pieceFactory.GetAllPieces();

		// If no pieces exist, we cannot settle (likely initialization issue)
		if (allPieces == null || allPieces.Count == 0)
		{
			GD.PrintErr("[CarromPhysicsMonitor] No pieces available - cannot determine settlement");
			return false;
		}

		// OPTIMIZATION: Single-pass check using LengthSquared for fast rejection
		float thresholdSq = StoppedVelocityThreshold * StoppedVelocityThreshold;
		const float FAST_MULTIPLIER = 2.0f;
		float fastThresholdSq = thresholdSq * FAST_MULTIPLIER * FAST_MULTIPLIER;

		foreach (var piece in allPieces)
		{
			if (!GodotObject.IsInstanceValid(piece))
				continue;

			float velSq = piece.LinearVelocity.LengthSquared();

			// Fast rejection: clearly still moving
			if (velSq > fastThresholdSq)
				return false;

			// Borderline: precise check needed
			if (velSq > thresholdSq && piece.LinearVelocity.Length() > StoppedVelocityThreshold)
				return false;

			if (Mathf.Abs(piece.AngularVelocity) > StoppedVelocityThreshold)
				return false;
		}

		return true;
	}

	// ================================================================
	// ACCESSORS
	// ================================================================

	public bool IsMonitoring() => _isMonitoringActive;

	// ================================================================
	// CLEANUP
	// ================================================================

	public override void _Notification(int what)
	{
		if (what == NotificationExitTree)
		{
			StopMonitoring();
		}
	}
}
