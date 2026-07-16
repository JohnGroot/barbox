using System.Collections.Generic;
using Godot;

namespace BarBox.Games.Carrom;

/// <summary>
/// Manages practice mode functionality for Carrom game
///
/// ARCHITECTURE NOTES:
/// - Striker positioning: Uses base class PositionStrikerAtBaseline() for consistency
/// - Phase transitions: Relies on base class ExecuteSettlement() for proper phase management
/// - Reset behavior: Combines stored initial positions with standardized positioning methods
/// </summary>
[GlobalClass]
public partial class CarromPracticeModeManager : CarromModeManagerBase
{
	[Signal]
	public delegate void PracticeResetRequestedEventHandler();

	[Signal]
	public delegate void PracticeModeSetupCompleteEventHandler();

	// Practice mode state
	private Dictionary<CarromPiece, Vector2> _practiceInitialPositions = [];
	private List<CarromPiece> _practicePieces = [];

	// Piece templates
	private PackedScene _blackPieceTemplate;
	private PackedScene _strikerTemplate;

	public void Initialize(CarromBoard board, CarromInputController inputController,
		CarromPhysicsConfig physicsConfig, PackedScene blackTemplate,
		PackedScene strikerTemplate)
	{
		Initialize(board, inputController, physicsConfig);
		_blackPieceTemplate = blackTemplate;
		_strikerTemplate = strikerTemplate;
	}

	// ================================================================
	// ABSTRACT METHOD IMPLEMENTATIONS
	// ================================================================
	protected override void CreateModeSpecificPieces()
	{
		_practicePieces.Clear();
		_practiceInitialPositions.Clear();

		// Get initial positions using global coordinates for consistency
		var strikerInitialPosition = _board.ToGlobal(_board.GetBaselinePosition(0));
		var centerPieceInitialPosition = _board.ToGlobal(Vector2.Zero); // Center of board in global coords

		// Create single practice piece in center
		var centerPiece = CreatePiece(PieceType.Black, Vector2.Zero);
		if (centerPiece != null)
		{
			_practicePieces.Add(centerPiece);
			_practiceInitialPositions[centerPiece] = centerPieceInitialPosition;
		}

		// Store striker initial position (striker is created by base class)
		if (_striker != null)
		{
			_practiceInitialPositions[_striker] = strikerInitialPosition;
		}

		// Position pieces at their initial positions
		PositionPiecesAtInitialPositions();
	}

	protected override List<CarromPiece> GetManagedPieces()
	{
		return [.. _practicePieces];
	}

	protected override void ClearModeSpecificPieces()
	{
		foreach (var piece in _practicePieces)
		{
			if (GodotObject.IsInstanceValid(piece))
			{
				piece.QueueFree();
			}
		}

		_practicePieces.Clear();
	}

	/// <summary>
	/// Handle practice mode settlement - only restore striker, pieces stay where they land
	/// Full board reset only happens via explicit RequestExplicitReset() call
	/// </summary>
	protected override void ExecuteModeSpecificSettlement()
	{
		// Only restore striker to baseline after settlement
		// Do NOT reset all pieces - that should only happen on explicit reset request
		var carromGame = GetParent<CarromGame>();
		if (carromGame != null)
		{
			carromGame.RestoreStrikerToBaseline();
		}
		else
		{
			// Fallback to local striker restoration
			EnsureStrikerRestored();
			PositionStrikerAtBaseline();
		}
	}

	/// <summary>
	/// Check if win condition is met (practice mode never "wins")
	/// </summary>
	public override bool CheckWinCondition(string playerId)
	{
		// Practice mode doesn't have win conditions
		return false;
	}

	/// <summary>
	/// Determine if current turn should continue (practice mode always continues)
	/// </summary>
	public override bool ShouldContinueTurn(string playerId)
	{
		// Practice mode continues indefinitely
		return true;
	}

	// ================================================================
	// PRACTICE MODE SPECIFIC METHODS
	// ================================================================

	/// <summary>
	/// Setup practice mode pieces (striker + single piece) - public interface
	/// </summary>
	public void SetupPracticeMode()
	{
		// Use base class template method
		SetupMode();
	}

	/// <summary>
	/// Finalize practice mode setup - emit the expected signal for CarromGame
	/// </summary>
	protected override void FinalizeSetup()
	{
		// Emit the practice-specific signal that CarromGame expects
		EmitSignal(SignalName.PracticeModeSetupComplete);
	}

	/// <summary>
	/// Handle practice-specific piece pocketing logic
	/// </summary>
	protected override void HandlePiecePocketed(CarromPiece piece)
	{
		// In practice mode, let the settlement system handle the reset
		// The actual reset will happen in ExecuteModeSpecificSettlement()
	}

	/// <summary>
	/// Explicit reset method for manual reset button - resets all pieces to initial positions
	/// This is the ONLY method that should reset the entire board in practice mode
	/// </summary>
	public void RequestExplicitReset()
	{
		if (!IsInstanceValid(this))
		{
			GD.PrintErr("[CarromPracticeModeManager] Cannot reset - manager is invalid");
			return;
		}

		// Reset all pieces to their stored initial positions
		ResetPracticeMode();

		GD.Print("[CarromPracticeModeManager] Explicit reset requested and executed");
	}

	/// <summary>
	/// Internal method to reset all pieces to initial positions
	/// Should only be called from RequestExplicitReset() or initial setup
	/// </summary>
	private void ResetPracticeMode()
	{
		// Reset all pieces to their stored initial positions using immediate method
		foreach (var kvp in _practiceInitialPositions)
		{
			var piece = kvp.Key;
			var initialGlobalPosition = kvp.Value;

			if (GodotObject.IsInstanceValid(piece))
			{
				ResetPieceToStartImmediate(piece, initialGlobalPosition);
			}
		}

		EmitSignal(SignalName.PracticeResetRequested);
	}

	private void PositionPiecesAtInitialPositions()
	{
		foreach (var kvp in _practiceInitialPositions)
		{
			var piece = kvp.Key;
			var globalPosition = kvp.Value;

			if (IsInstanceValid(piece))
			{
				piece.GlobalPosition = globalPosition;
				piece.LinearVelocity = Vector2.Zero;
				piece.AngularVelocity = 0.0f;
			}
		}
	}

	/// <summary>
	/// Reset a single piece to its starting position using immediate synchronous method
	/// </summary>
	private void ResetPieceToStartImmediate(CarromPiece piece, Vector2 globalPosition)
	{
		if (!IsInstanceValid(piece))
		{
			return;
		}

		// First restore from pocketed state if the piece was pocketed (invisible)
		if (!piece.Visible)
		{
			piece.Reset();
		}

		// Then use the immediate synchronous reset method for positioning
		piece.Reset(globalPosition);

		// CRITICAL FIX: Mark this piece in settlement context to prevent false "still moving" detection
		MarkRecentRestoration(piece);
	}

	protected override void CancelModeSpecificOperations()
	{
		// No additional operations to cancel in practice mode
	}

	/// <summary>
	/// Position striker at baseline using stored practice position (override base class)
	/// </summary>
	protected override void PositionStrikerAtBaseline()
	{
		if (!IsStrikerValid())
		{
			return;
		}

		// Use stored initial position instead of calculated baseline
		if (_practiceInitialPositions.ContainsKey(_striker))
		{
			var storedPosition = _practiceInitialPositions[_striker];

			_striker.GlobalPosition = storedPosition;
			_striker.LinearVelocity = Vector2.Zero;
			_striker.AngularVelocity = 0.0f;
		}
		else
		{
			// Fallback to base class behavior if no stored position
			base.PositionStrikerAtBaseline();
		}
	}

	private bool IsStrikerValid()
	{
		return _striker != null && GodotObject.IsInstanceValid(_striker);
	}

	protected override void ClearModeSpecificState()
	{
		_practiceInitialPositions.Clear();
	}
}
