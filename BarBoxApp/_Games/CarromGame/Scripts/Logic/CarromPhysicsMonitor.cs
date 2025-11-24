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

		// Check all pieces every physics frame
		if (AreAllPiecesStopped())
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

		foreach (var piece in allPieces)
		{
			if (!GodotObject.IsInstanceValid(piece))
				continue;

			if (piece.LinearVelocity.Length() > StoppedVelocityThreshold)
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
