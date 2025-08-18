using Godot;
using System.Collections.Generic;

/// <summary>
/// Real-time score display UI for Carrom competitive mode
/// Shows round information, player scores, and current turn indicator
/// </summary>
[GlobalClass]
public partial class CarromScoreDisplay : CanvasLayer
{
	// UI Components
	private Panel _mainPanel;
	private Label _roundLabel;
	private VBoxContainer _playersContainer;
	private Dictionary<string, PlayerScoreEntry> _playerEntries = new Dictionary<string, PlayerScoreEntry>();
	
	// Styling constants
	private const float PANEL_WIDTH = 300.0f;
	private const float PANEL_MARGIN = 20.0f;
	private readonly Color CURRENT_PLAYER_COLOR = new Color(0.2f, 0.8f, 0.2f, 0.8f); // Green
	private readonly Color INACTIVE_PLAYER_COLOR = new Color(0.1f, 0.1f, 0.1f, 0.7f); // Dark gray
	private readonly Color PANEL_BACKGROUND_COLOR = new Color(0.0f, 0.0f, 0.0f, 0.9f); // Semi-transparent black
	
	/// <summary>
	/// Individual player score entry UI
	/// </summary>
	private partial class PlayerScoreEntry : VBoxContainer
	{
		public Label PlayerNameLabel { get; private set; }
		public Label PiecesLabel { get; private set; }
		public Label QueenLabel { get; private set; }
		public Panel BackgroundPanel { get; private set; }
		
		public PlayerScoreEntry(string playerId, PieceType pieceType)
		{
			// Create background panel
			BackgroundPanel = new Panel();
			BackgroundPanel.AnchorLeft = 0.0f;
			BackgroundPanel.AnchorRight = 1.0f;
			BackgroundPanel.AnchorTop = 0.0f;
			BackgroundPanel.AnchorBottom = 1.0f;
			AddChild(BackgroundPanel);
			
			// Create labels
			PlayerNameLabel = new Label();
			PlayerNameLabel.Text = $"▶ {playerId.ToUpper()} ({pieceType})";
			PlayerNameLabel.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
			AddChild(PlayerNameLabel);
			
			PiecesLabel = new Label();
			PiecesLabel.Text = "Pieces: 0/9";
			PiecesLabel.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
			AddChild(PiecesLabel);
			
			QueenLabel = new Label();
			QueenLabel.Text = "Queen: Not Pocketed";
			QueenLabel.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
			AddChild(QueenLabel);
			
			// Style labels - remove explicit size settings
			foreach (Label label in new[] { PlayerNameLabel, PiecesLabel, QueenLabel })
			{
				label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			}
		}
		
		public void UpdateCurrentPlayerStatus(bool isCurrent)
		{
			if (isCurrent)
			{
				PlayerNameLabel.Text = PlayerNameLabel.Text.Replace("  ", "▶ ");
				BackgroundPanel.Modulate = new Color(0.2f, 0.8f, 0.2f, 0.3f); // Light green
			}
			else
			{
				PlayerNameLabel.Text = PlayerNameLabel.Text.Replace("▶ ", "  ");
				BackgroundPanel.Modulate = new Color(0.1f, 0.1f, 0.1f, 0.2f); // Dark
			}
		}
		
		public void UpdateScore(int piecesPocketed)
		{
			PiecesLabel.Text = $"Pieces: {piecesPocketed}/9";
		}
		
		public void UpdateQueenStatus(bool hasQueen, bool covered)
		{
			if (hasQueen)
			{
				QueenLabel.Text = covered ? "Queen: ✓ Covered" : "Queen: ✗ Uncovered";
			}
			else
			{
				QueenLabel.Text = "Queen: Not Pocketed";
			}
		}
	}
	
	public override void _Ready()
	{
		// Set layer to appear on top
		Layer = 100;
		
		SetupUI();
	}
	
	/// <summary>
	/// Setup the UI components
	/// </summary>
	private void SetupUI()
	{
		// Create main panel - positioned at bottom left
		_mainPanel = new Panel();
		
		// Set anchoring to bottom left using proper offsets
		_mainPanel.AnchorLeft = 0.0f;   // Left edge
		_mainPanel.AnchorRight = 0.0f;  // Left edge  
		_mainPanel.AnchorTop = 1.0f;    // Bottom edge
		_mainPanel.AnchorBottom = 1.0f; // Bottom edge
		
		// Set initial position using offsets (size will be set by AdjustPanelHeight)
		_mainPanel.OffsetLeft = PANEL_MARGIN;
		_mainPanel.OffsetRight = PANEL_WIDTH;
		_mainPanel.OffsetBottom = -PANEL_MARGIN;
		
		// Style the main panel
		var styleBox = new StyleBoxFlat();
		styleBox.BgColor = PANEL_BACKGROUND_COLOR;
		styleBox.BorderWidthTop = 2;
		styleBox.BorderWidthBottom = 2;
		styleBox.BorderWidthLeft = 2;
		styleBox.BorderWidthRight = 2;
		styleBox.BorderColor = Colors.White;
		styleBox.CornerRadiusTopLeft = 8;
		styleBox.CornerRadiusTopRight = 8;
		styleBox.CornerRadiusBottomLeft = 8;
		styleBox.CornerRadiusBottomRight = 8;
		_mainPanel.AddThemeStyleboxOverride("panel", styleBox);
		
		AddChild(_mainPanel);
		
		// Create content container with margins
		var contentContainer = new VBoxContainer();
		contentContainer.AnchorLeft = 0.0f;
		contentContainer.AnchorRight = 1.0f;
		contentContainer.AnchorTop = 0.0f;
		contentContainer.AnchorBottom = 1.0f;
		contentContainer.OffsetLeft = 10;
		contentContainer.OffsetTop = 10;
		contentContainer.OffsetRight = -10;
		contentContainer.OffsetBottom = -10;
		
		_mainPanel.AddChild(contentContainer);
		
		// Create round label
		_roundLabel = new Label();
		_roundLabel.Text = "Round: 1 / 50";
		_roundLabel.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
		_roundLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		contentContainer.AddChild(_roundLabel);
		
		// Add separator
		var separator = new HSeparator();
		separator.Modulate = Colors.White;
		contentContainer.AddChild(separator);
		
		// Create players container
		_playersContainer = new VBoxContainer();
		contentContainer.AddChild(_playersContainer);
		
		// Set initial panel height
		AdjustPanelHeight();
	}
	
	/// <summary>
	/// Update the round display
	/// </summary>
	public void UpdateRound(int currentRound, int maxRounds)
	{
		_roundLabel.Text = $"Round: {currentRound} / {maxRounds}";
	}
	
	/// <summary>
	/// Add a player to the score display
	/// </summary>
	public void AddPlayer(string playerId, PieceType pieceType)
	{
		if (_playerEntries.ContainsKey(playerId))
		{
			return; // Player already added
		}
		
		var playerEntry = new PlayerScoreEntry(playerId, pieceType);
		_playerEntries[playerId] = playerEntry;
		_playersContainer.AddChild(playerEntry);
		
		// Adjust panel height
		AdjustPanelHeight();
	}
	
	/// <summary>
	/// Update a player's score
	/// </summary>
	public void UpdatePlayerScore(string playerId, int piecesPocketed)
	{
		if (_playerEntries.TryGetValue(playerId, out var entry))
		{
			entry.UpdateScore(piecesPocketed);
		}
	}
	
	/// <summary>
	/// Update a player's queen status
	/// </summary>
	public void UpdateQueenStatus(string playerId, bool hasQueen, bool covered)
	{
		if (_playerEntries.TryGetValue(playerId, out var entry))
		{
			entry.UpdateQueenStatus(hasQueen, covered);
		}
	}
	
	/// <summary>
	/// Highlight the current player
	/// </summary>
	public void SetCurrentPlayer(string playerId)
	{
		foreach (var kvp in _playerEntries)
		{
			bool isCurrent = kvp.Key == playerId;
			kvp.Value.UpdateCurrentPlayerStatus(isCurrent);
		}
	}
	
	/// <summary>
	/// Clear all players (for game reset)
	/// </summary>
	public void ClearPlayers()
	{
		foreach (var entry in _playerEntries.Values)
		{
			entry.QueueFree();
		}
		_playerEntries.Clear();
		
		AdjustPanelHeight();
	}
	
	/// <summary>
	/// Show or hide the score display
	/// </summary>
	public new void SetVisible(bool visible)
	{
		Visible = visible;
	}
	
	/// <summary>
	/// Adjust panel height based on number of players
	/// </summary>
	private void AdjustPanelHeight()
	{
		float baseHeight = 60.0f; // Round label + separator
		float playerHeight = 75.0f; // Height per player entry (reduced due to fewer lines)
		float padding = 20.0f;
		
		float totalHeight = baseHeight + (_playerEntries.Count * playerHeight) + padding;
		
		// Update offsets to maintain bottom-left anchoring with new height
		_mainPanel.OffsetLeft = PANEL_MARGIN;
		_mainPanel.OffsetTop = -totalHeight - PANEL_MARGIN; // Negative to go up from bottom
		_mainPanel.OffsetRight = PANEL_WIDTH;
		_mainPanel.OffsetBottom = -PANEL_MARGIN;
	}
	
	/// <summary>
	/// Update all player scores from a list of players
	/// </summary>
	public void UpdateAllPlayerScores(System.Collections.Generic.List<CarromPlayer> players)
	{
		foreach (var player in players)
		{
			UpdatePlayerScore(player.PlayerId, player.PiecesPocketed);
			UpdateQueenStatus(player.PlayerId, player.HasQueen, player.QueenCovered);
		}
	}
}