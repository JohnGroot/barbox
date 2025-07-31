using Godot;
using System.Collections.Generic;

/// <summary>
/// Common interface for Carrom mode managers (Practice and Competitive)
/// </summary>
public interface ICarromModeManager
{
	/// <summary>
	/// Initialize the mode manager with required dependencies
	/// </summary>
	void Initialize(CarromBoard board, CarromInputController inputController, CarromPhysicsConfig physicsConfig);
	
	/// <summary>
	/// Set the phase manager for phase-aware operations
	/// </summary>
	void SetPhaseManager(CarromPhaseManager phaseManager);

	/// <summary>
	/// Set the piece factory for centralized piece creation
	/// </summary>
	void SetPieceFactory(CarromPieceFactory pieceFactory);

	/// <summary>
	/// Setup the game mode (pieces, players, etc.)
	/// </summary>
	void SetupMode();

	/// <summary>
	/// Get the current striker piece
	/// </summary>
	CarromPiece GetStriker();

	/// <summary>
	/// Check if all pieces have stopped moving
	/// </summary>
	bool AreAllPiecesStopped();

	/// <summary>
	/// Handle piece being pocketed
	/// </summary>
	void OnPiecePocketed(CarromPiece piece);

	/// <summary>
	/// Handle pieces settled (called when all pieces have stopped)
	/// </summary>
	void OnPiecesSettled();

	/// <summary>
	/// Get all active pieces in this mode
	/// </summary>
	List<CarromPiece> GetActivePieces();

	/// <summary>
	/// Clean up mode resources (pieces, references, etc.)
	/// </summary>
	void CleanupMode();

	/// <summary>
	/// Check if the mode is currently active/initialized
	/// </summary>
	bool IsActive { get; }
}