using Godot;
using System;

namespace BarBox.Games.Carrom;

/// <summary>
/// In-game debug overlay for testing Carrom game scenarios
/// Only visible in editor/debug builds - auto-hidden in release
/// Press F12 to toggle visibility during gameplay
/// </summary>
public partial class CarromDebugOverlay : CanvasLayer
{
	private CarromGame _currentGame;
	private Control _debugPanel;
	private Label _stateLabel;
	private Timer _refreshTimer;
	private bool _isVisible = false;

	public override void _Ready()
	{
		// Only show in development mode
		if (!ShouldShowDebugOverlay())
		{
			QueueFree();
			return;
		}

		// Get parent game reference
		_currentGame = GetParent<CarromGame>();
		if (_currentGame == null)
		{
			GD.PrintErr("[CarromDebugOverlay] Must be child of CarromGame node");
			QueueFree();
			return;
		}

		// Set to top layer
		Layer = 1000;

		// Create the debug overlay UI
		_debugPanel = CreateDebugPanel();
		AddChild(_debugPanel);

		// Debug panel positioning
		GD.Print($"[CarromDebugOverlay] Panel Position: {_debugPanel.Position}");
		GD.Print($"[CarromDebugOverlay] Panel GlobalPosition: {_debugPanel.GlobalPosition}");
		GD.Print($"[CarromDebugOverlay] Panel Offsets: L={_debugPanel.OffsetLeft}, R={_debugPanel.OffsetRight}, T={_debugPanel.OffsetTop}, B={_debugPanel.OffsetBottom}");
		GD.Print($"[CarromDebugOverlay] Viewport Size: {GetViewport().GetVisibleRect().Size}");

		// Setup refresh timer for real-time state updates
		_refreshTimer = new Timer();
		_refreshTimer.WaitTime = 0.5f; // Update twice per second
		_refreshTimer.Timeout += UpdateStateDisplay;
		AddChild(_refreshTimer);
		_refreshTimer.Start();

		// Start hidden - press F12 to show
		_debugPanel.Visible = false;
		_isVisible = false;

		GD.Print("[CarromDebugOverlay] Debug overlay loaded - Press F12 to toggle visibility");
	}

	public override void _Notification(int what)
	{
		if (what == NotificationExitTree)
		{
			// Clean up timer
			if (_refreshTimer != null && IsInstanceValid(_refreshTimer))
			{
				_refreshTimer.Stop();
				_refreshTimer.QueueFree();
			}
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent &&
			keyEvent.Pressed &&
			!keyEvent.Echo &&
			keyEvent.Keycode == Key.F12)
		{
			_isVisible = !_isVisible;
			if (_debugPanel != null)
			{
				_debugPanel.Visible = _isVisible;
			}
			GetViewport().SetInputAsHandled();

			GD.Print($"[CarromDebugOverlay] Debug overlay {(_isVisible ? "shown" : "hidden")}");
		}
	}

	private bool ShouldShowDebugOverlay()
	{
		// Show in editor or debug builds, hide in release builds
		return Engine.IsEditorHint() || OS.IsDebugBuild();
	}

	private Control CreateDebugPanel()
	{
		var panel = new PanelContainer();

		// Position in top-right corner with 10px margins
		panel.SetAnchorsPreset(Control.LayoutPreset.TopRight, false);
		panel.CustomMinimumSize = new Vector2(280, 500);
		panel.OffsetLeft = -290;  // -(width + 10px margin)
		panel.OffsetRight = -10;  // 10px margin from right edge
		panel.OffsetTop = 10;     // 10px margin from top
		panel.OffsetBottom = 510; // top margin + height

		// Add background styling
		var styleBox = new StyleBoxFlat();
		styleBox.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
		styleBox.BorderColor = new Color(0.3f, 0.5f, 0.7f);
		styleBox.SetBorderWidthAll(2);
		styleBox.SetCornerRadiusAll(8);
		styleBox.SetContentMarginAll(10);
		panel.AddThemeStyleboxOverride("panel", styleBox);

		var vbox = new VBoxContainer();
		panel.AddChild(vbox);

		// Add title
		var title = new Label();
		title.Text = "🎯 Carrom Debug Tools";
		title.AddThemeFontSizeOverride("font_size", 18);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		var titleBg = new StyleBoxFlat();
		titleBg.BgColor = new Color(0.2f, 0.3f, 0.5f);
		titleBg.SetContentMarginAll(8);
		titleBg.SetCornerRadiusAll(4);
		title.AddThemeStyleboxOverride("normal", titleBg);
		vbox.AddChild(title);

		// Add spacing
		vbox.AddChild(CreateSpacer(10));

		// Add F12 hint
		var hint = new Label();
		hint.Text = "Press F12 to toggle";
		hint.AddThemeFontSizeOverride("font_size", 10);
		hint.HorizontalAlignment = HorizontalAlignment.Center;
		hint.Modulate = new Color(0.7f, 0.7f, 0.7f);
		vbox.AddChild(hint);
		vbox.AddChild(CreateSpacer(5));

		// Add scenario buttons
		vbox.AddChild(CreateScenarioSection());

		// Add player state controls
		vbox.AddChild(CreatePlayerStateSection());

		// Add utility buttons
		vbox.AddChild(CreateUtilitySection());

		// Add state display
		_stateLabel = CreateStateLabel();
		vbox.AddChild(_stateLabel);

		return panel;
	}

	private Control CreateScenarioSection()
	{
		var section = new VBoxContainer();

		var sectionLabel = new Label();
		sectionLabel.Text = "Test Scenarios";
		sectionLabel.AddThemeFontSizeOverride("font_size", 14);
		section.AddChild(sectionLabel);

		// Foul scenarios
		section.AddChild(CreateButton("⚠️ Striker Foul", () => TestScenario_StrikerFoul()));
		section.AddChild(CreateButton("⚠️ Opponent Piece Foul", () => TestScenario_OpponentFoul()));

		// Queen scenarios
		section.AddChild(CreateSpacer(5));
		section.AddChild(CreateButton("👑 Queen Covering (Success)", () => TestScenario_QueenCoveringSuccess()));
		section.AddChild(CreateButton("👑 Queen Covering (Fail)", () => TestScenario_QueenCoveringFail()));

		// Turn scenarios
		section.AddChild(CreateSpacer(5));
		section.AddChild(CreateButton("🔄 Breaking Turn Test", () => TestScenario_BreakingTurn()));

		// Win condition
		section.AddChild(CreateSpacer(5));
		section.AddChild(CreateButton("🏆 Trigger Win Condition", () => TestScenario_WinCondition()));

		section.AddChild(CreateSpacer(5));

		return section;
	}

	private Control CreatePlayerStateSection()
	{
		var section = new VBoxContainer();

		var sectionLabel = new Label();
		sectionLabel.Text = "Player State";
		sectionLabel.AddThemeFontSizeOverride("font_size", 14);
		section.AddChild(sectionLabel);

		section.AddChild(CreateButton("⏭️ Skip Turn", () => SkipToNextPlayer()));
		section.AddChild(CreateButton("📊 Give Player 5 Pieces", () => SetPlayerPieces(5)));
		section.AddChild(CreateButton("🎯 Set Near-Win State", () => SetNearWinState()));

		section.AddChild(CreateSpacer(5));

		return section;
	}

	private Control CreateUtilitySection()
	{
		var section = new VBoxContainer();

		var sectionLabel = new Label();
		sectionLabel.Text = "Utilities";
		sectionLabel.AddThemeFontSizeOverride("font_size", 14);
		section.AddChild(sectionLabel);

		section.AddChild(CreateButton("🔄 Return to Practice", () => ReturnToPractice()));
		section.AddChild(CreateButton("📋 Print State to Console", () => PrintGameState()));
		section.AddChild(CreateButton("✓ Validate Board State", () => ValidateBoardState()));

		return section;
	}

	private Label CreateStateLabel()
	{
		var label = new Label();
		label.Text = "Game state will appear here...";

		var stylebox = new StyleBoxFlat();
		stylebox.BgColor = new Color(0.05f, 0.05f, 0.05f);
		stylebox.BorderColor = new Color(0.3f, 0.3f, 0.3f);
		stylebox.SetBorderWidthAll(1);
		stylebox.SetContentMarginAll(8);
		stylebox.SetCornerRadiusAll(4);
		label.AddThemeStyleboxOverride("normal", stylebox);

		label.CustomMinimumSize = new Vector2(260, 120);
		label.VerticalAlignment = VerticalAlignment.Top;
		label.AddThemeFontSizeOverride("font_size", 10);

		return label;
	}

	private Button CreateButton(string text, Action onPressed)
	{
		var button = new Button();
		button.Text = text;
		button.CustomMinimumSize = new Vector2(260, 28);
		button.AddThemeFontSizeOverride("font_size", 12);
		button.Pressed += () => {
			if (_currentGame == null || !IsInstanceValid(_currentGame))
			{
				GD.PrintErr("[CarromDebugOverlay] Game instance not valid");
				return;
			}
			onPressed?.Invoke();
		};
		return button;
	}

	private Control CreateSpacer(int height)
	{
		var spacer = new Control();
		spacer.CustomMinimumSize = new Vector2(0, height);
		return spacer;
	}

	// ================================================================
	// STATE DISPLAY
	// ================================================================

	private void UpdateStateDisplay()
	{
		if (_currentGame == null || !IsInstanceValid(_currentGame))
		{
			if (_stateLabel != null)
			{
				_stateLabel.Text = "Game instance not found";
			}
			return;
		}

		if (_stateLabel != null && _isVisible)
		{
			var state = _currentGame.DEBUG_GetGameState();
			_stateLabel.Text = state;
		}
	}

	// ================================================================
	// TEST SCENARIOS
	// ================================================================

	private void TestScenario_StrikerFoul()
	{
		GD.Print("[CarromDebugOverlay] Setting up striker foul scenario");
		_currentGame?.DEBUG_SetupFoulScenario(FoulType.StrikerPocketed);
		GD.Print("[CarromDebugOverlay] ✓ Striker foul scenario ready - pocket striker to test penalty");
	}

	private void TestScenario_OpponentFoul()
	{
		GD.Print("[CarromDebugOverlay] Setting up opponent piece foul scenario");
		_currentGame?.DEBUG_SetupFoulScenario(FoulType.OpponentPiecePocketed);
		GD.Print("[CarromDebugOverlay] ✓ Opponent foul scenario ready - pocket opponent piece to test");
	}

	private void TestScenario_QueenCoveringSuccess()
	{
		GD.Print("[CarromDebugOverlay] Setting up queen covering success scenario");
		_currentGame?.DEBUG_SetupQueenCoveringTest(shouldCover: true);
		GD.Print("[CarromDebugOverlay] ✓ Queen covering success scenario ready");
		GD.Print("[CarromDebugOverlay]   1. Pocket the red queen");
		GD.Print("[CarromDebugOverlay]   2. Immediately pocket your assigned piece");
		GD.Print("[CarromDebugOverlay]   3. Queen should be marked as covered");
	}

	private void TestScenario_QueenCoveringFail()
	{
		GD.Print("[CarromDebugOverlay] Setting up queen covering failure scenario");
		_currentGame?.DEBUG_SetupQueenCoveringTest(shouldCover: false);
		GD.Print("[CarromDebugOverlay] ✓ Queen covering failure scenario ready");
		GD.Print("[CarromDebugOverlay]   1. Pocket the red queen");
		GD.Print("[CarromDebugOverlay]   2. Don't pocket your piece (or foul)");
		GD.Print("[CarromDebugOverlay]   3. Click 'Pass Turn' - queen should return to center");
	}

	private void TestScenario_BreakingTurn()
	{
		GD.Print("[CarromDebugOverlay] Setting up breaking turn scenario");
		_currentGame?.DEBUG_ResetToBreakingTurn();
		GD.Print("[CarromDebugOverlay] ✓ Breaking turn scenario ready");
		GD.Print("[CarromDebugOverlay]   3 attempts available to disturb pieces");
	}

	private void TestScenario_WinCondition()
	{
		GD.Print("[CarromDebugOverlay] Triggering win condition for player1");
		_currentGame?.DEBUG_TriggerWinCondition("player1");
		GD.Print("[CarromDebugOverlay] ✓ Win condition triggered - game over screen should appear");
	}

	// ================================================================
	// PLAYER STATE MANIPULATION
	// ================================================================

	private void SkipToNextPlayer()
	{
		if (_currentGame == null || !IsInstanceValid(_currentGame))
		{
			GD.PrintErr("[CarromDebugOverlay] Cannot skip turn - game instance not valid");
			return;
		}

		// Get current state to determine current player
		var state = _currentGame.DEBUG_GetGameState();

		// Parse current player index from state (looks for "Current Player: playerX")
		int currentPlayerIndex = 0;
		if (state.Contains("Current Player: player2"))
			currentPlayerIndex = 1;

		// Switch to the other player (toggle between 0 and 1 for 2-player games)
		int nextPlayerIndex = (currentPlayerIndex + 1) % 2;

		GD.Print($"[CarromDebugOverlay] Skipping from player{currentPlayerIndex + 1} to player{nextPlayerIndex + 1}");
		_currentGame.DEBUG_SetCurrentPlayer(nextPlayerIndex);
	}

	private void SetPlayerPieces(int pieces)
	{
		GD.Print($"[CarromDebugOverlay] Setting current player to {pieces} pieces");
		_currentGame?.DEBUG_ForcePlayerState("player1", pieces, false, false);
	}

	private void SetNearWinState()
	{
		GD.Print("[CarromDebugOverlay] Setting near-win state (8 pieces + covered queen)");
		_currentGame?.DEBUG_ForcePlayerState("player1", 8, true, true);
		GD.Print("[CarromDebugOverlay] ✓ Near-win state set - pocket 1 more piece to win");
	}

	// ================================================================
	// UTILITIES
	// ================================================================

	private void ReturnToPractice()
	{
		GD.Print("[CarromDebugOverlay] Returning to practice mode");
		_currentGame?.DEBUG_ReturnToPractice();
	}

	private void PrintGameState()
	{
		if (_currentGame == null || !IsInstanceValid(_currentGame))
		{
			GD.Print("[CarromDebugOverlay] No game instance");
			return;
		}

		var state = _currentGame.DEBUG_GetGameState();
		GD.Print(state);
	}

	private void ValidateBoardState()
	{
		if (_currentGame == null || !IsInstanceValid(_currentGame))
		{
			GD.Print("[CarromDebugOverlay] No game instance");
			return;
		}

		_currentGame.DEBUG_ValidateBoardState();
	}
}
