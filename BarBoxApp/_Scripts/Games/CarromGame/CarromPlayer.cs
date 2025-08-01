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
			GD.PrintErr($"[CarromPlayer] Cannot assign {pieceType} as player piece type");
			return;
		}

		AssignedPieceType = pieceType;
		EmitSignal(SignalName.PieceAssignmentChanged);
		GD.Print($"[CarromPlayer] Player {PlayerId} assigned {pieceType} pieces");
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
			GD.Print($"[CarromPlayer] Player {PlayerId} pocketed the Queen");
		}
		else if (pieceType == AssignedPieceType)
		{
			PiecesPocketed++;
			_validPockets++;
			
			// Check if queen is covered (pocketed an assigned piece after the queen)
			if (HasQueen && !QueenCovered)
			{
				QueenCovered = true;
				GD.Print($"[CarromPlayer] Player {PlayerId} covered the Queen");
			}
			
			EmitSignal(SignalName.ScoreUpdated, PiecesPocketed);
		}
		else if (pieceType != PieceType.Striker)
		{
			// Pocketed opponent's piece (might be a penalty)
			GD.Print($"[CarromPlayer] Player {PlayerId} pocketed opponent's piece: {pieceType}");
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
		
		GD.Print($"[CarromPlayer] Player {PlayerId} committed foul #{_fouls}");
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
	/// Get count of specific piece type pocketed
	/// </summary>
	public virtual int GetPocketedCount(PieceType pieceType)
	{
		int count = 0;
		foreach (var piece in _pocketedPieces)
		{
			if (piece == pieceType) count++;
		}
		return count;
	}

	/// <summary>
	/// Get player status summary for UI display
	/// </summary>
	public virtual string GetStatusSummary()
	{
		string status = $"{PlayerName}: {PiecesPocketed}/{GetRequiredPieces()} pieces";
		
		if (HasQueen)
		{
			status += QueenCovered ? " (Queen✓)" : " (Queen!)";
		}
		
		return status;
	}

	/// <summary>
	/// Get detailed statistics for end-game display
	/// </summary>
	public virtual Dictionary<string, object> GetGameStatistics()
	{
		return new Dictionary<string, object>
		{
			["TotalShots"] = _totalShots,
			["ValidPockets"] = _validPockets,
			["Fouls"] = _fouls,
			["Accuracy"] = GetAccuracy(),
			["FoulRate"] = GetFoulRate(),
			["HasQueen"] = HasQueen,
			["QueenCovered"] = QueenCovered,
			["PiecesPocketed"] = PiecesPocketed,
			["RemainingPieces"] = GetRemainingPieces()
		};
	}

	// Public accessors for game statistics
	public int GetTotalShots() => _totalShots;
	public int GetValidPockets() => _validPockets;
	public int GetFouls() => _fouls;
	public bool IsAssignedPiece(PieceType pieceType) => pieceType == AssignedPieceType;
}