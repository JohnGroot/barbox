using Godot;
using System.Collections.Generic;

/// <summary>
/// Carrom player that extends BasePlayer with carrom-specific functionality
/// </summary>
[GlobalClass]
public partial class CarromPlayer : BasePlayer
{
	[Signal] public delegate void PieceAssignmentChangedEventHandler();
	[Signal] public delegate void ScoreUpdatedEventHandler(int newScore);

	[ExportCategory("Carrom Settings")]
	[Export] public PieceType AssignedPieceType { get; set; } = PieceType.White;
	[Export] public int PiecesPocketed { get; set; } = 0;
	[Export] public bool HasQueen { get; set; } = false; // Whether player has pocketed the queen
	[Export] public bool QueenCovered { get; set; } = false; // Whether queen is properly covered

	// Player statistics
	private int _totalShots = 0;
	private int _validPockets = 0;
	private int _fouls = 0;
	private List<PieceType> _pocketedPieces = new List<PieceType>();

	protected override void InitializePlayer()
	{
		base.InitializePlayer();
		ResetGameStats();
	}

	/// <summary>
	/// Reset game-specific statistics
	/// </summary>
	public virtual void ResetGameStats()
	{
		PiecesPocketed = 0;
		HasQueen = false;
		QueenCovered = false;
		_totalShots = 0;
		_validPockets = 0;
		_fouls = 0;
		_pocketedPieces.Clear();
	}

	/// <summary>
	/// Assign piece type to player (for competitive mode)
	/// </summary>
	public virtual void AssignPieceType(PieceType pieceType)
	{
		if (pieceType == PieceType.Striker || pieceType == PieceType.Red)
		{
			return;
		}

		AssignedPieceType = pieceType;
		EmitSignal(SignalName.PieceAssignmentChanged);
	}

	/// <summary>
	/// Record a shot taken by the player
	/// </summary>
	public virtual void RecordShot()
	{
		_totalShots++;
	}

	/// <summary>
	/// Record a piece being pocketed by this player
	/// </summary>
	public virtual void RecordPocketedPiece(PieceType pieceType)
	{
		_pocketedPieces.Add(pieceType);

		if (pieceType == PieceType.Red)
		{
			HasQueen = true;
		}
		else if (pieceType == AssignedPieceType)
		{
			PiecesPocketed++;
			_validPockets++;
			
			// Check if queen is covered (pocketed an assigned piece after the queen)
			if (HasQueen && !QueenCovered)
			{
				QueenCovered = true;
			}
			
			EmitSignal(SignalName.ScoreUpdated, PiecesPocketed);
		}
	}

	/// <summary>
	/// Record a foul committed by this player
	/// </summary>
	public virtual void RecordFoul()
	{
		_fouls++;
		
		// In carrom, fouls might result in piece penalties
		if (PiecesPocketed > 0)
		{
			PiecesPocketed--;
			EmitSignal(SignalName.ScoreUpdated, PiecesPocketed);
		}
	}

	/// <summary>
	/// Check if player has won the game
	/// </summary>
	public virtual bool HasWon()
	{
		// In standard carrom, player wins by pocketing all assigned pieces
		// and covering the queen if they pocketed it
		return PiecesPocketed >= GetRequiredPieces() && (!HasQueen || QueenCovered);
	}

	/// <summary>
	/// Get required number of pieces to win (typically 9 in standard carrom)
	/// </summary>
	public virtual int GetRequiredPieces()
	{
		return 9; // Standard carrom has 9 pieces per player
	}

	/// <summary>
	/// Get remaining pieces needed to win
	/// </summary>
	public virtual int GetRemainingPieces()
	{
		return Mathf.Max(0, GetRequiredPieces() - PiecesPocketed);
	}

	/// <summary>
	/// Check if player can pocket the queen
	/// </summary>
	public virtual bool CanPocketQueen()
	{
		// Typically can only pocket queen after pocketing at least one assigned piece
		return PiecesPocketed > 0 && !HasQueen;
	}

	/// <summary>
	/// Get player's shooting accuracy
	/// </summary>
	public virtual float GetAccuracy()
	{
		if (_totalShots == 0) return 0.0f;
		return (float)_validPockets / _totalShots;
	}

	/// <summary>
	/// Get player's foul rate
	/// </summary>
	public virtual float GetFoulRate()
	{
		if (_totalShots == 0) return 0.0f;
		return (float)_fouls / _totalShots;
	}

	/// <summary>
	/// Get list of all pieces pocketed by this player
	/// </summary>
	public virtual List<PieceType> GetPocketedPieces()
	{
		return new List<PieceType>(_pocketedPieces);
	}

	/// <summary>
	/// Return a previously pocketed piece (for foul penalties)
	/// Returns the piece type that was returned, or null if no pieces to return
	/// </summary>
	public virtual PieceType? ReturnPocketedPiece()
	{
		// Return the most recently pocketed assigned piece (not queen)
		// This follows standard carrom penalty rules
		for (int i = _pocketedPieces.Count - 1; i >= 0; i--)
		{
			var pieceType = _pocketedPieces[i];
			if (pieceType == AssignedPieceType)
			{
				_pocketedPieces.RemoveAt(i);
				PiecesPocketed--;
				return pieceType;
			}
		}

		// If no assigned pieces to return, check if we can return the queen
		// (though this is rare in official rules)
		if (HasQueen && !QueenCovered)
		{
			for (int i = _pocketedPieces.Count - 1; i >= 0; i--)
			{
				if (_pocketedPieces[i] == PieceType.Red)
				{
					_pocketedPieces.RemoveAt(i);
					HasQueen = false;
					return PieceType.Red;
				}
			}
		}

		// No pieces available to return
		return null;
	}

	/// <summary>
	/// Check if player has any pieces that can be returned for foul penalty
	/// </summary>
	public virtual bool CanReturnPiece()
	{
		// Can return if player has pocketed assigned pieces or uncovered queen
		return PiecesPocketed > 0 || (HasQueen && !QueenCovered);
	}


	// Public accessors for game statistics
	public int GetTotalShots() => _totalShots;
	public int GetValidPockets() => _validPockets;
	public int GetFouls() => _fouls;
	public bool IsAssignedPiece(PieceType pieceType) => pieceType == AssignedPieceType;
}