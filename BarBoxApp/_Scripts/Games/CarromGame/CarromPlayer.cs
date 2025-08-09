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
	
	// Queen covering state (CRITICAL FIX for same-turn covering rule)
	private bool _pocketedQueenThisTurn = false;
	private bool _needsQueenCovering = false;
	
	// Tournament scoring (ICF Official Rules)
	[Export] public int TournamentPoints { get; set; } = 0;
	[Export] public int GamesWon { get; set; } = 0;
	[Export] public int GamesPlayed { get; set; } = 0;

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
		
		// Reset queen covering state
		_pocketedQueenThisTurn = false;
		_needsQueenCovering = false;
		
		// Note: Tournament points are NOT reset - they accumulate across games
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
	/// OFFICIAL CARROM RULE: Queen must be covered by pocketing assigned piece immediately after in same turn
	/// </summary>
	public virtual void RecordPocketedPiece(PieceType pieceType)
	{
		// CRITICAL FIX: Validate piece type BEFORE modifying any state
		if (AssignedPieceType == PieceType.Striker)
		{
			GD.PrintErr($"[CarromPlayer] Cannot pocket pieces - no assigned piece type for player {PlayerId}");
			return;
		}
		
		// Validate that this is a legal piece type to pocket
		if (pieceType == PieceType.Striker)
		{
			GD.PrintErr($"[CarromPlayer] Striker should never be recorded as pocketed for player {PlayerId}");
			return;
		}

		// NOW safe to modify state after validation
		_pocketedPieces.Add(pieceType);

		if (pieceType == PieceType.Red)
		{
			HasQueen = true;
			_pocketedQueenThisTurn = true;
			_needsQueenCovering = true;
			GD.Print($"[CarromPlayer] {PlayerId} pocketed queen - needs covering this turn");
		}
		else if (pieceType == AssignedPieceType)
		{
			PiecesPocketed++;
			_validPockets++;
			
			// OFFICIAL RULE: Queen covering must happen immediately after queen in same turn
			if (_needsQueenCovering)
			{
				QueenCovered = true;
				_needsQueenCovering = false;
				GD.Print($"[CarromPlayer] {PlayerId} covered queen with {pieceType} piece");
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
	/// OFFICIAL CARROM RULE: Queen can be pocketed at any time, covering eligibility checked separately
	/// </summary>
	public virtual bool CanPocketQueen()
	{
		// OFFICIAL RULE FIX: Player can pocket queen at any time during the game
		// The covering eligibility (having at least one assigned piece) is checked separately
		return !HasQueen;
	}
	
	/// <summary>
	/// Check if player is eligible to cover the queen (has at least one assigned piece pocketed)
	/// </summary>
	public virtual bool CanCoverQueen()
	{
		// Must have pocketed at least one assigned piece to be eligible to cover queen
		return PiecesPocketed > 0;
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
	/// <summary>
	/// Handle end of turn - check if queen needs to be returned to center
	/// OFFICIAL CARROM RULE: If queen was pocketed but not covered in same turn, return to center
	/// </summary>
	public virtual bool HandleEndOfTurn()
	{
		bool queenNeedsReturning = false;
		
		// Check if queen was pocketed this turn but not covered
		if (_pocketedQueenThisTurn && _needsQueenCovering)
		{
			// Queen was not covered in this turn - must be returned to center
			HasQueen = false;
			QueenCovered = false;
			queenNeedsReturning = true;
			
			// Remove queen from pocketed pieces list
			for (int i = _pocketedPieces.Count - 1; i >= 0; i--)
			{
				if (_pocketedPieces[i] == PieceType.Red)
				{
					_pocketedPieces.RemoveAt(i);
					break;
				}
			}
			
			GD.Print($"[CarromPlayer] {PlayerId} did not cover queen - returning to center");
		}
		
		// Reset turn state
		_pocketedQueenThisTurn = false;
		_needsQueenCovering = false;
		
		return queenNeedsReturning;
	}
	
	/// <summary>
	/// Start new turn - reset turn-specific state
	/// </summary>
	public virtual void StartNewTurn()
	{
		_pocketedQueenThisTurn = false;
		_needsQueenCovering = false;
	}
	
	// Public accessors for game statistics
	public int GetTotalShots() => _totalShots;
	public int GetValidPockets() => _validPockets;
	public int GetFouls() => _fouls;
	public bool IsAssignedPiece(PieceType pieceType) => pieceType == AssignedPieceType;
	public bool NeedsQueenCovering() => _needsQueenCovering;
	public bool PocketedQueenThisTurn() => _pocketedQueenThisTurn;
	
	/// <summary>
	/// Calculate tournament points for winning a game
	/// ICF OFFICIAL RULES: 1 point per opponent piece remaining + queen covering bonus
	/// </summary>
	public virtual int CalculateTournamentPoints(List<CarromPlayer> allPlayers)
	{
		if (allPlayers == null)
		{
			GD.PrintErr($"[CarromPlayer] Cannot calculate tournament points - allPlayers list is null");
			return 0;
		}

		if (allPlayers.Count == 0)
		{
			GD.PrintErr($"[CarromPlayer] Cannot calculate tournament points - allPlayers list is empty");
			return 0;
		}

		int points = 0;
		
		// Count opponent pieces remaining (1 point each)
		foreach (var opponent in allPlayers)
		{
			if (opponent == null || !GodotObject.IsInstanceValid(opponent))
			{
				GD.PrintErr($"[CarromPlayer] Skipping null or invalid opponent in tournament points calculation");
				continue;
			}

			if (opponent.PlayerId == PlayerId || opponent.AssignedPieceType == AssignedPieceType)
				continue; // Skip self and team members
				
			int opponentPiecesRemaining = opponent.GetRequiredPieces() - opponent.PiecesPocketed;
			points += opponentPiecesRemaining;
		}
		
		// Queen covering bonus: +5 points if total under 24
		if (HasQueen && QueenCovered && points < 24)
		{
			points += 5;
		}
		
		return points;
	}
	
	/// <summary>
	/// Award tournament points and update tournament statistics
	/// </summary>
	public virtual void AwardTournamentPoints(int points)
	{
		TournamentPoints += points;
		GamesWon++;
		GamesPlayed++;
		
		GD.Print($"[Tournament] {PlayerId} awarded {points} points (Total: {TournamentPoints})");
		EmitSignal(SignalName.ScoreUpdated, TournamentPoints);
	}
	
	/// <summary>
	/// Record a game loss (no points awarded)
	/// </summary>
	public virtual void RecordGameLoss()
	{
		GamesPlayed++;
		// No points awarded for losing
	}
	
	/// <summary>
	/// Reset tournament statistics (new tournament)
	/// </summary>
	public virtual void ResetTournamentStats()
	{
		TournamentPoints = 0;
		GamesWon = 0;
		GamesPlayed = 0;
	}
	
	/// <summary>
	/// Get tournament win percentage
	/// </summary>
	public virtual float GetWinPercentage()
	{
		if (GamesPlayed == 0) return 0.0f;
		return (float)GamesWon / GamesPlayed;
	}
	
	/// <summary>
	/// Check if player has won the tournament (typically 29 points)
	/// </summary>
	public virtual bool HasWonTournament(int targetPoints = 29)
	{
		return TournamentPoints >= targetPoints;
	}
}