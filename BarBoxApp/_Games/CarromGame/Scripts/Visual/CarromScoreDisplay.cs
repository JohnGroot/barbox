using Godot;
using System.Collections.Generic;

/// <summary>
/// Helper class for queuing floating text requests
/// </summary>
public class FloatingTextRequest
{
	public string Message { get; set; }
	public Color? Color { get; set; }
	
	public FloatingTextRequest(string message, Color? color = null)
	{
		Message = message;
		Color = color;
	}
}

/// <summary>
/// Real-time score display UI for Carrom competitive mode
/// Horizontal bar at bottom showing scores, turn info, and transitions
/// </summary>
[GlobalClass]
public partial class CarromScoreDisplay : CanvasLayer
{
	[Signal] public delegate void PassTurnRequestedEventHandler();
	// UI Components - Main structure
	private Panel _mainPanel;
	private HBoxContainer _mainContainer;
	
	// UI Components - Three sections
	private VBoxContainer _leftSection;    // Round info and game status
	private VBoxContainer _centerSection;  // Turn transitions and messages
	private HBoxContainer _rightSection;   // Player scores (horizontal)
	
	// UI Components - Specific elements
	private Label _roundLabel;
	private Label _gameStatusLabel;
	private Label _inputStateLabel;
	private Button _passTurnButton;
	private Dictionary<string, PlayerScoreEntry> _playerEntries = new Dictionary<string, PlayerScoreEntry>();
	
	// Transition system
	private bool _isShowingTransition = false;
	
	// Floating text queuing system
	private Dictionary<string, Queue<FloatingTextRequest>> _pendingTextRequests = new Dictionary<string, Queue<FloatingTextRequest>>();
	private Dictionary<string, bool> _playerTextActive = new Dictionary<string, bool>();
	
	// Styling constants
	private const float PANEL_HEIGHT = 120.0f;
	private const float PANEL_MARGIN = 10.0f;
	private readonly Color CURRENT_PLAYER_COLOR = new Color(0.2f, 0.9f, 0.3f, 0.95f); // Bright green
	private readonly Color INACTIVE_PLAYER_COLOR = new Color(0.15f, 0.15f, 0.15f, 0.8f); // Dark gray
	private readonly Color PANEL_BACKGROUND_COLOR = new Color(0.05f, 0.05f, 0.1f, 0.95f); // Dark blue-black
	private readonly Color TRANSITION_COLOR = new Color(1.0f, 0.8f, 0.2f, 0.95f); // Amber/yellow
	private readonly Color INPUT_BLOCKED_COLOR = new Color(0.9f, 0.3f, 0.2f, 0.9f); // Red
	private readonly Color INPUT_READY_COLOR = new Color(0.2f, 0.9f, 0.3f, 0.9f); // Green
	
	/// <summary>
	/// Individual player score entry UI - now horizontal layout
	/// </summary>
	private partial class PlayerScoreEntry : VBoxContainer
	{
		public Label PlayerNameLabel { get; private set; }
		public Label PiecesLabel { get; private set; }
		public Label QueenLabel { get; private set; }
		public Panel BackgroundPanel { get; private set; }
		public ProgressBar PiecesProgressBar { get; private set; }
		
		public PlayerScoreEntry(string playerId, PieceType pieceType)
		{
			// Create background panel
			BackgroundPanel = new Panel();
			BackgroundPanel.AnchorLeft = 0.0f;
			BackgroundPanel.AnchorRight = 1.0f;
			BackgroundPanel.AnchorTop = 0.0f;
			BackgroundPanel.AnchorBottom = 1.0f;
			AddChild(BackgroundPanel);
			
			// Get piece icon based on type
			string pieceIcon = pieceType == PieceType.White ? "⚪" : "⚫";
			
			// Player name with piece icon
			PlayerNameLabel = new Label();
			PlayerNameLabel.Text = $"{pieceIcon} {playerId.ToUpper()}";
			PlayerNameLabel.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
			PlayerNameLabel.AddThemeFontSizeOverride("font_size", 14);
			AddChild(PlayerNameLabel);
			
			// Pieces count with progress bar
			var piecesContainer = new HBoxContainer();
			AddChild(piecesContainer);
			
			PiecesLabel = new Label();
			PiecesLabel.Text = "0/9";
			PiecesLabel.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
			PiecesLabel.CustomMinimumSize = new Vector2(40, 0);
			piecesContainer.AddChild(PiecesLabel);
			
			PiecesProgressBar = new ProgressBar();
			PiecesProgressBar.MinValue = 0;
			PiecesProgressBar.MaxValue = 9;
			PiecesProgressBar.Value = 0;
			PiecesProgressBar.CustomMinimumSize = new Vector2(80, 16);
			PiecesProgressBar.ShowPercentage = false;
			piecesContainer.AddChild(PiecesProgressBar);
			
			// Queen status with icon
			QueenLabel = new Label();
			QueenLabel.Text = "👑 -";
			QueenLabel.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
			QueenLabel.AddThemeFontSizeOverride("font_size", 12);
			AddChild(QueenLabel);
		}
		
		public void UpdateCurrentPlayerStatus(bool isCurrent)
		{
			if (isCurrent)
			{
				// Add arrow indicator and bright green background
				if (!PlayerNameLabel.Text.Contains("▶"))
				{
					PlayerNameLabel.Text = "▶ " + PlayerNameLabel.Text;
				}
				BackgroundPanel.Modulate = new Color(0.2f, 0.9f, 0.3f, 0.4f); // Bright green
				
				// Add pulsing animation effect
				var tween = GetTree().CreateTween();
				tween.SetLoops();
				tween.TweenProperty(BackgroundPanel, TweenConstants.ModulateAlpha, 0.6f, 0.5f);
				tween.TweenProperty(BackgroundPanel, TweenConstants.ModulateAlpha, 0.4f, 0.5f);
			}
			else
			{
				// Remove arrow and set dark background
				PlayerNameLabel.Text = PlayerNameLabel.Text.Replace("▶ ", "");
				BackgroundPanel.Modulate = new Color(0.15f, 0.15f, 0.15f, 0.3f); // Dark
				
				// Stop any active tweens
				foreach (var tween in GetTree().GetProcessedTweens())
				{
					if (tween.IsValid())
						tween.Kill();
				}
			}
		}
		
		public void UpdateScore(int piecesPocketed)
		{
			PiecesLabel.Text = $"{piecesPocketed}/9";
			PiecesProgressBar.Value = piecesPocketed;
			
			// Color progress bar based on completion
			if (piecesPocketed >= 9)
			{
				PiecesProgressBar.Modulate = Colors.Gold;
			}
			else if (piecesPocketed >= 6)
			{
				PiecesProgressBar.Modulate = Colors.Yellow;
			}
			else
			{
				PiecesProgressBar.Modulate = Colors.White;
			}
		}
		
		public void UpdateQueenStatus(bool hasQueen, bool covered)
		{
			if (hasQueen)
			{
				QueenLabel.Text = covered ? "👑 ✓" : "👑 ✗";
				QueenLabel.Modulate = covered ? Colors.Gold : Colors.Orange;
			}
			else
			{
				QueenLabel.Text = "👑 -";
				QueenLabel.Modulate = Colors.Gray;
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
		// Create main panel - horizontal bar at bottom of screen
		_mainPanel = new Panel();
		
		// Anchor to bottom of screen, full width
		_mainPanel.AnchorLeft = 0.0f;
		_mainPanel.AnchorRight = 1.0f;
		_mainPanel.AnchorTop = 1.0f;
		_mainPanel.AnchorBottom = 1.0f;
		
		// Set size and position
		_mainPanel.OffsetLeft = PANEL_MARGIN;
		_mainPanel.OffsetRight = -PANEL_MARGIN;
		_mainPanel.OffsetTop = -PANEL_HEIGHT - PANEL_MARGIN;
		_mainPanel.OffsetBottom = -PANEL_MARGIN;
		
		// Style the main panel
		var styleBox = new StyleBoxFlat();
		styleBox.BgColor = PANEL_BACKGROUND_COLOR;
		styleBox.BorderWidthTop = 3;
		styleBox.BorderWidthBottom = 0;
		styleBox.BorderWidthLeft = 0;
		styleBox.BorderWidthRight = 0;
		styleBox.BorderColor = new Color(0.3f, 0.3f, 0.4f, 1.0f);
		styleBox.CornerRadiusTopLeft = 12;
		styleBox.CornerRadiusTopRight = 12;
		styleBox.CornerRadiusBottomLeft = 0;
		styleBox.CornerRadiusBottomRight = 0;
		_mainPanel.AddThemeStyleboxOverride("panel", styleBox);
		
		AddChild(_mainPanel);
		
		// Create main horizontal container
		_mainContainer = new HBoxContainer();
		_mainContainer.AnchorLeft = 0.0f;
		_mainContainer.AnchorRight = 1.0f;
		_mainContainer.AnchorTop = 0.0f;
		_mainContainer.AnchorBottom = 1.0f;
		_mainContainer.OffsetLeft = 15;
		_mainContainer.OffsetTop = 10;
		_mainContainer.OffsetRight = -15;
		_mainContainer.OffsetBottom = -10;
		_mainContainer.AddThemeConstantOverride("separation", 20);
		
		_mainPanel.AddChild(_mainContainer);
		
		// Create left section (round info and game status)
		_leftSection = new VBoxContainer();
		_leftSection.CustomMinimumSize = new Vector2(200, 0);
		_mainContainer.AddChild(_leftSection);
		
		_roundLabel = new Label();
		_roundLabel.Text = "Round: 1 / 50";
		_roundLabel.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
		_roundLabel.AddThemeFontSizeOverride("font_size", 16);
		_leftSection.AddChild(_roundLabel);
		
		_gameStatusLabel = new Label();
		_gameStatusLabel.Text = "Game Status: Ready";
		_gameStatusLabel.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
		_gameStatusLabel.AddThemeFontSizeOverride("font_size", 14);
		_gameStatusLabel.Modulate = INPUT_READY_COLOR;
		_leftSection.AddChild(_gameStatusLabel);
		
		_inputStateLabel = new Label();
		_inputStateLabel.Text = "";
		_inputStateLabel.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
		_inputStateLabel.AddThemeFontSizeOverride("font_size", 12);
		_leftSection.AddChild(_inputStateLabel);
		
		// Create center section (Pass Turn button)
		_centerSection = new VBoxContainer();
		_centerSection.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_centerSection.AddThemeConstantOverride("separation", 5);
		_mainContainer.AddChild(_centerSection);
		
		// Create Pass Turn button with yellow styling to replace transition panel
		_passTurnButton = new Button();
		_passTurnButton.Text = "Pass Turn";
		_passTurnButton.CustomMinimumSize = new Vector2(150, 80);
		_passTurnButton.AddThemeFontSizeOverride("font_size", 16);
		_passTurnButton.Visible = false; // Initially hidden
		_passTurnButton.Pressed += OnPassTurnPressed;
		
		// Style the button with yellow background like the old transition panel
		var buttonStyle = new StyleBoxFlat();
		buttonStyle.BgColor = TRANSITION_COLOR; // Same yellow as before
		buttonStyle.CornerRadiusTopLeft = 8;
		buttonStyle.CornerRadiusTopRight = 8;
		buttonStyle.CornerRadiusBottomLeft = 8;
		buttonStyle.CornerRadiusBottomRight = 8;
		_passTurnButton.AddThemeStyleboxOverride("normal", buttonStyle);
		_passTurnButton.AddThemeStyleboxOverride("hover", buttonStyle);
		_passTurnButton.AddThemeStyleboxOverride("pressed", buttonStyle);
		_passTurnButton.AddThemeColorOverride("font_color", Colors.Black);
		
		_centerSection.AddChild(_passTurnButton);
		
		// Create right section (player scores) - anchored to right
		_rightSection = new HBoxContainer();
		_rightSection.CustomMinimumSize = new Vector2(400, 0);
		_rightSection.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
		_rightSection.AddThemeConstantOverride("separation", 15);
		_mainContainer.AddChild(_rightSection);
		
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
		playerEntry.CustomMinimumSize = new Vector2(150, 0);
		_playerEntries[playerId] = playerEntry;
		_rightSection.AddChild(playerEntry);
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
	}
	
	/// <summary>
	/// Show or hide the score display
	/// </summary>
	public new void SetVisible(bool visible)
	{
		Visible = visible;
	}
	
	/// <summary>
	/// Show turn transition message
	/// Input state is now controlled by game state machine, not here
	/// </summary>
	public void ShowTurnTransition(string message, float duration = 3.0f)
	{
		_isShowingTransition = true;
		
		// Show the Pass Turn button with fade-in animation
		_passTurnButton.Visible = true;
		_passTurnButton.Modulate = new Color(1, 1, 1, 0);
		
		var tween = GetTree().CreateTween();
		tween.TweenProperty(_passTurnButton, TweenConstants.ModulateAlpha, 1.0f, 0.3f);
		
		// Ensure input is visually blocked while waiting for pass turn
		SetInputBlockedVisual(true);
	}
	
	/// <summary>
	/// Hide turn transition
	/// </summary>
	private void HideTransition()
	{
		_isShowingTransition = false;
		
		// Fade out and hide the Pass Turn button
		var tween = GetTree().CreateTween();
		tween.TweenProperty(_passTurnButton, TweenConstants.ModulateAlpha, 0.0f, 0.3f);
		tween.Finished += () => _passTurnButton.Visible = false;
		
		// Input state is controlled by game state machine, not by UI transitions
	}
	
	
	/// <summary>
	/// Set visual feedback for input blocked state
	/// </summary>
	public void SetInputBlockedVisual(bool blocked)
	{
		if (blocked)
		{
			_gameStatusLabel.Text = "Waiting...";
			_gameStatusLabel.Modulate = INPUT_BLOCKED_COLOR;
			_inputStateLabel.Text = "Input Disabled";
			_inputStateLabel.Modulate = INPUT_BLOCKED_COLOR;
		}
		else
		{
			_gameStatusLabel.Text = "Ready";
			_gameStatusLabel.Modulate = INPUT_READY_COLOR;
			_inputStateLabel.Text = "";
		}
	}
	
	/// <summary>
	/// Update game state display
	/// </summary>
	public void UpdateGameState(string state)
	{
		_gameStatusLabel.Text = $"State: {state}";
		
		// Color based on state
		switch (state.ToLower())
		{
			case "ready":
				_gameStatusLabel.Modulate = INPUT_READY_COLOR;
				break;
			case "processing":
			case "settlement":
				_gameStatusLabel.Modulate = TRANSITION_COLOR;
				break;
			default:
				_gameStatusLabel.Modulate = Colors.White;
				break;
		}
	}
	
	
	/// <summary>
	/// Update all player scores from a list of players
	/// </summary>
	public void UpdateAllPlayerScores(List<CarromPlayer> players)
	{
		foreach (var player in players)
		{
			UpdatePlayerScore(player.PlayerId, player.PiecesPocketed);
			UpdateQueenStatus(player.PlayerId, player.HasQueen, player.QueenCovered);
		}
	}
	
	/// <summary>
	/// Show simple message in transition area
	/// </summary>
	public void ShowMessage(string message, float duration = 2.0f)
	{
		ShowTurnTransition(message, duration);
	}
	
	/// <summary>
	/// Handle Pass Turn button press
	/// </summary>
	private void OnPassTurnPressed()
	{
		// Hide the button immediately when pressed to prevent double-clicks
		_passTurnButton.Visible = false;
		_isShowingTransition = false;

		// Emit signal to notify game that player wants to pass turn
		EmitSignal(SignalName.PassTurnRequested);
	}
	
	/// <summary>
	/// Show floating text effect above the current player's name
	/// Creates a temporary label that rises and fades out with scaling animation
	/// Implements queuing to prevent overlapping when called multiple times in same frame
	/// </summary>
	public void ShowFloatingText(string playerId, string message, Color? color = null)
	{
		// Create request object
		var request = new FloatingTextRequest(message, color);
		
		// Initialize queues if needed
		if (!_pendingTextRequests.ContainsKey(playerId))
		{
			_pendingTextRequests[playerId] = new Queue<FloatingTextRequest>();
			_playerTextActive[playerId] = false;
		}
		
		// If player has active animation, queue this request
		if (_playerTextActive[playerId])
		{
			_pendingTextRequests[playerId].Enqueue(request);
			return;
		}
		
		// Start the animation immediately
		StartFloatingTextAnimation(playerId, request);
	}
	
	/// <summary>
	/// Start the actual floating text animation
	/// </summary>
	private void StartFloatingTextAnimation(string playerId, FloatingTextRequest request)
	{
		// Find the player's entry to position the text above it
		if (!_playerEntries.TryGetValue(playerId, out var playerEntry))
		{
			GD.PrintErr($"Cannot show floating text - player {playerId} not found");
			return;
		}
		
		// Mark player as having active animation
		_playerTextActive[playerId] = true;
		
		// Create the floating text label with proper initialization
		var floatingLabel = new Label();
		floatingLabel.Text = request.Message;
		floatingLabel.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
		floatingLabel.AddThemeFontSizeOverride("font_size", 18);
		floatingLabel.Modulate = request.Color ?? Colors.White;
		floatingLabel.HorizontalAlignment = HorizontalAlignment.Center;
		floatingLabel.AutowrapMode = TextServer.AutowrapMode.Off;
		
		// Add to the main panel first to ensure proper sizing and coordinate space
		_mainPanel.AddChild(floatingLabel);
		
		// Convert from playerEntry's global position to _mainPanel's local coordinate space
		var playerEntryGlobalPos = playerEntry.GlobalPosition;
		var mainPanelGlobalPos = _mainPanel.GlobalPosition;
		var localPos = playerEntryGlobalPos - mainPanelGlobalPos;
		
		// Calculate proper text centering using actual text dimensions
		var font = floatingLabel.GetThemeFont("font");
		var fontSize = floatingLabel.GetThemeFontSize("font_size");
		var textSize = font.GetStringSize(request.Message, HorizontalAlignment.Left, -1, fontSize);
		
		// Position the label above the player's entry with proper centering
		floatingLabel.Position = new Vector2(
			localPos.X + (playerEntry.Size.X - textSize.X) / 2, // True horizontal center
			localPos.Y - textSize.Y - 10 // Position above with proper clearance
		);
		
		// Create the animation tween
		var tween = GetTree().CreateTween();
		
		// Scaling animation - 25% scale-up then back to normal
		tween.Parallel().TweenProperty(floatingLabel, TweenConstants.Scale, Vector2.One * 1.25f, 0.2f)
			.SetTrans(Tween.TransitionType.Back)
			.SetEase(Tween.EaseType.Out);
		// tween.Parallel().TweenProperty(floatingLabel, TweenConstants.Scale, Vector2.One, 0.3f)
		// 	.SetDelay(0.2f)
		// 	.SetTrans(Tween.TransitionType.Quart)
		// 	.SetEase(Tween.EaseType.Out);
		
		// Rise animation - move up by 60 pixels over 1.5 seconds
		tween.Parallel().TweenProperty(floatingLabel, TweenConstants.Position, 
			floatingLabel.Position + Vector2.Up * 60.0f, 1.5f)
			.SetTrans(Tween.TransitionType.Quart)
			.SetEase(Tween.EaseType.Out);
		
		// Fade animation - start fading after 0.3 seconds, complete in 1.2 seconds
		tween.Parallel().TweenProperty(floatingLabel, TweenConstants.ModulateAlpha, 
			0.0f, 1.2f)
			.SetDelay(0.3f);
		
		// Queue next text at 30% completion (0.45 seconds)
		tween.TweenCallback(Callable.From(() => ProcessNextQueuedText(playerId)))
			.SetDelay(0.45f);
		
		// Clean up the label when animation completes
		tween.Finished += () => 
		{
			floatingLabel.QueueFree();
			// Mark player as no longer having active animation
			_playerTextActive[playerId] = false;
		};
	}
	
	/// <summary>
	/// Process the next queued text for a player
	/// </summary>
	private void ProcessNextQueuedText(string playerId)
	{
		if (!_pendingTextRequests.ContainsKey(playerId) || _pendingTextRequests[playerId].Count == 0)
		{
			return;
		}
		
		var nextRequest = _pendingTextRequests[playerId].Dequeue();
		StartFloatingTextAnimation(playerId, nextRequest);
	}
}