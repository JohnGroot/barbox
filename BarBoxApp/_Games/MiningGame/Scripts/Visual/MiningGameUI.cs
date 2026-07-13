using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BarBox.Games.MiningGame;

[GlobalClass]
public partial class MiningGameUI : Control
{
	private const int MENU_BAR_OFFSET = 160;  // Account for menu bar + expandable section
	private const int CONTAINER_OFFSET = 180; // Menu bar + some padding
	private const int UI_MARGIN = 20;
	private const float PROGRESS_UPDATE_THRESHOLD = 0.01f;

	private ColorRect _background;
	private VBoxContainer _mainContainer;

	// Header section
	private Label _titleLabel;
	private Label _locationLabel;

	// Mining progress section
	private ProgressBar _miningProgressBar;
	private Label _progressLabel;
	private Label _gemsReadyLabel;
	private Label _capacityLabel;

	// Actions section
	private Button _extractButton;
	private Button _purchaseCreditButton;

	// Upgrades section
	private VBoxContainer _upgradesContainer;
	private Dictionary<UpgradeType, UpgradeUIGroup> _upgradeGroups = new();

	// Global inventory section
	private Dictionary<GemType, Label> _gemLabels = new();

	private MiningGame _game;
	private MiningGameConfig _config;
	private MiningLocationConfig _locationConfig;
	private GemType _gemType = GemType.Amethyst;
	private bool _isEnabled;

	// Performance optimization flags
	private int _lastPendingGems = -1;
	private int _lastMaxCapacity = -1;
	private float _lastProgress = -1;
	private int _lastDisplayedSeconds = -1;

	public void Initialize(MiningGame game, MiningGameConfig config)
	{
		_game = game;
		_config = config;

		CreateUI();
		ApplyTheme();

		// Start disabled - enable only after location registered AND user logged in
		SetEnabled(false);
	}

	public void ApplyLocationConfig(MiningLocationConfig config)
	{
		_locationConfig = config;
		_gemType = config?.GetGemType() ?? GemType.Amethyst;

		// Update location display
		if (_locationLabel != null)
			_locationLabel.Text = GetLocationDisplayName();

		// Refresh UI colors based on new gem type
		RefreshThemeColors();
	}

	private void RefreshThemeColors()
	{
		// Background
		if (_background != null)
			_background.Color = GetBackgroundColor();

		// Header section
		if (_titleLabel != null)
			_titleLabel.AddThemeColorOverride("font_color", GetHeaderColor());
		if (_locationLabel != null)
			_locationLabel.AddThemeColorOverride("font_color", GetPrimaryAccent());

		// Progress bar
		if (_miningProgressBar != null)
		{
			var progressStyle = new StyleBoxFlat();
			progressStyle.BgColor = GetProgressBarColor();
			progressStyle.SetCornerRadiusAll(4);
			_miningProgressBar.AddThemeStyleboxOverride("fill", progressStyle);
		}

		// Text labels
		if (_progressLabel != null)
			_progressLabel.AddThemeColorOverride("font_color", GetTextColor());
		if (_gemsReadyLabel != null)
			_gemsReadyLabel.AddThemeColorOverride("font_color", GetTextColor());
		if (_capacityLabel != null)
			_capacityLabel.AddThemeColorOverride("font_color", GetTextColor());

		// Action buttons - recreate styles with new colors
		RefreshButtonStyles(_extractButton);
		RefreshButtonStyles(_purchaseCreditButton);

		// Update purchase credit button text with new gem emoji and credit amount
		if (_purchaseCreditButton != null)
		{
			var creditsPerPurchase = _game?.GetCreditsPerPurchase() ?? 1000;
			_purchaseCreditButton.Text = $"Buy {creditsPerPurchase:N0} Credits ({GemTheme.GetGemEmoji(_gemType)} {_config.CreditCost})";
		}

		// Upgrade groups - they get colors from parent UI, trigger refresh
		foreach (var upgradeGroup in _upgradeGroups.Values)
		{
			upgradeGroup.RefreshTheme();
		}

		// Global inventory labels
		foreach (var label in _gemLabels.Values)
		{
			label.AddThemeColorOverride("font_color", GetTextColor());
		}
	}

	private void RefreshButtonStyles(Button button)
	{
		if (button == null) return;

		var normalStyle = new StyleBoxFlat();
		normalStyle.BgColor = GetButtonEnabledColor();
		normalStyle.SetCornerRadiusAll(4);
		normalStyle.SetContentMarginAll(8);
		button.AddThemeStyleboxOverride("normal", normalStyle);

		var hoverStyle = new StyleBoxFlat();
		hoverStyle.BgColor = GetButtonEnabledColor() * 1.2f;
		hoverStyle.SetCornerRadiusAll(4);
		hoverStyle.SetContentMarginAll(8);
		button.AddThemeStyleboxOverride("hover", hoverStyle);

		var pressedStyle = new StyleBoxFlat();
		pressedStyle.BgColor = GetButtonEnabledColor() * 0.8f;
		pressedStyle.SetCornerRadiusAll(4);
		pressedStyle.SetContentMarginAll(8);
		button.AddThemeStyleboxOverride("pressed", pressedStyle);

		var disabledStyle = new StyleBoxFlat();
		disabledStyle.BgColor = GetButtonDisabledColor();
		disabledStyle.SetCornerRadiusAll(4);
		disabledStyle.SetContentMarginAll(8);
		button.AddThemeStyleboxOverride("disabled", disabledStyle);
	}

	private Color GetBackgroundColor() => GemTheme.GetBackgroundColor(_gemType);
	public Color GetPrimaryAccent() => GemTheme.GetPrimaryAccent(_gemType);
	public Color GetSecondaryAccent() => GemTheme.GetSecondaryAccent(_gemType);
	private Color GetProgressBarColor() => GemTheme.GetProgressBarColor(_gemType);
	public Color GetButtonEnabledColor() => GemTheme.GetButtonEnabledColor(_gemType);
	public Color GetButtonDisabledColor() => GemTheme.GetButtonDisabledColor(_gemType);
	public Color GetTextColor() => GemTheme.GetTextColor(_gemType);
	public Color GetHeaderColor() => GemTheme.GetHeaderColor(_gemType);
	private string GetLocationDisplayName() => _locationConfig?.DisplayName ?? "Crystal Cavern";
	internal GemType GetGemType() => _gemType;

	private void CreateUI()
	{
		// Main background - offset to avoid menu bar overlap
		_background = new ColorRect();
		_background.Color = GetBackgroundColor();
		_background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_background.OffsetTop = MENU_BAR_OFFSET;
		AddChild(_background);

		// Main container with margins - also offset for menu bar
		_mainContainer = new VBoxContainer();
		_mainContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_mainContainer.OffsetTop = CONTAINER_OFFSET;
		_mainContainer.OffsetLeft = UI_MARGIN;
		_mainContainer.OffsetRight = -UI_MARGIN;
		_mainContainer.OffsetBottom = -UI_MARGIN;
		AddChild(_mainContainer);

		CreateHeader(_mainContainer);
		AddSeparator(_mainContainer);
		CreateMiningSection(_mainContainer);
		AddSeparator(_mainContainer);
		CreateActionsSection(_mainContainer);
		AddSeparator(_mainContainer);
		CreateUpgradesSection(_mainContainer);
		AddSeparator(_mainContainer);
		CreateGlobalInventorySection(_mainContainer);
	}

	private void CreateHeader(VBoxContainer parent)
	{
		var headerContainer = new VBoxContainer();

		_titleLabel = new Label();
		_titleLabel.Text = "Mining Operation";
		_titleLabel.AddThemeFontSizeOverride("font_size", 24);
		_titleLabel.AddThemeColorOverride("font_color", GetHeaderColor());
		headerContainer.AddChild(_titleLabel);

		_locationLabel = new Label();
		_locationLabel.Text = GetLocationDisplayName();
		_locationLabel.AddThemeFontSizeOverride("font_size", 18);
		_locationLabel.AddThemeColorOverride("font_color", GetPrimaryAccent());
		headerContainer.AddChild(_locationLabel);

		parent.AddChild(headerContainer);
	}

	private void CreateMiningSection(VBoxContainer parent)
	{
		var miningGroup = UIBuilder.CreateGroup("Mining Progress", this);

		// Progress bar container
		var progressContainer = new VBoxContainer();

		_miningProgressBar = new ProgressBar();
		_miningProgressBar.Value = 0;
		_miningProgressBar.CustomMinimumSize = new Vector2(0, 30);
		_miningProgressBar.ShowPercentage = false;

		// Style the progress bar
		var progressStyle = new StyleBoxFlat();
		progressStyle.BgColor = GetProgressBarColor();
		progressStyle.SetCornerRadiusAll(4);
		_miningProgressBar.AddThemeStyleboxOverride("fill", progressStyle);

		var bgStyle = new StyleBoxFlat();
		bgStyle.BgColor = new Color(0.2f, 0.2f, 0.25f);
		bgStyle.SetCornerRadiusAll(4);
		_miningProgressBar.AddThemeStyleboxOverride("background", bgStyle);

		progressContainer.AddChild(_miningProgressBar);

		_progressLabel = new Label();
		_progressLabel.Text = "Next mining tick in: --:--:--";
		_progressLabel.AddThemeColorOverride("font_color", GetTextColor());
		progressContainer.AddChild(_progressLabel);

		miningGroup.AddChild(progressContainer);

		// Gems ready section
		var gemsContainer = new HBoxContainer();

		_gemsReadyLabel = new Label();
		_gemsReadyLabel.Text = "Gems ready: 0";
		_gemsReadyLabel.AddThemeColorOverride("font_color", GetTextColor());
		gemsContainer.AddChild(_gemsReadyLabel);

		gemsContainer.AddChild(new VSeparator());

		_capacityLabel = new Label();
		_capacityLabel.Text = "Capacity: 0/0";
		_capacityLabel.AddThemeColorOverride("font_color", GetTextColor());
		gemsContainer.AddChild(_capacityLabel);

		miningGroup.AddChild(gemsContainer);

		parent.AddChild(miningGroup);
	}

	private void CreateActionsSection(VBoxContainer parent)
	{
		var actionsGroup = UIBuilder.CreateGroup("Actions", this);
		var buttonsHBox = new HBoxContainer();
		buttonsHBox.AddThemeConstantOverride("separation", 10);

		_extractButton = UIBuilder.CreateActionButton("Extract Gems", this);
		_extractButton.CustomMinimumSize = new Vector2(150, 40);
		_extractButton.Pressed += () => _game?.ExtractGems();
		buttonsHBox.AddChild(_extractButton);

		var creditsPerPurchase = _game?.GetCreditsPerPurchase() ?? 1000;
		_purchaseCreditButton = UIBuilder.CreateActionButton($"Buy {creditsPerPurchase:N0} Credits ({GemTheme.GetGemEmoji(_gemType)} {_config.CreditCost})", this);
		_purchaseCreditButton.CustomMinimumSize = new Vector2(200, 40);
		_purchaseCreditButton.Pressed += () => _ = _game?.PurchaseCreditAsync();
		buttonsHBox.AddChild(_purchaseCreditButton);

		actionsGroup.AddChild(buttonsHBox);
		parent.AddChild(actionsGroup);
	}

	private void CreateUpgradesSection(VBoxContainer parent)
	{
		var upgradesGroup = UIBuilder.CreateGroup("Upgrades", this);
		_upgradesContainer = new VBoxContainer();
		_upgradesContainer.AddThemeConstantOverride("separation", 8);

		foreach (UpgradeType upgradeType in Enum.GetValues<UpgradeType>())
		{
			var upgradeUI = new UpgradeUIGroup(upgradeType, _game, _config, this);
			_upgradeGroups[upgradeType] = upgradeUI;
			_upgradesContainer.AddChild(upgradeUI);
		}

		upgradesGroup.AddChild(_upgradesContainer);
		parent.AddChild(upgradesGroup);
	}

	private void CreateGlobalInventorySection(VBoxContainer parent)
	{
		var inventoryGroup = UIBuilder.CreateGroup("Global Inventory", this);

		// Create GridContainer for 3x2 layout
		var gemsGrid = new GridContainer();
		gemsGrid.Columns = 3;
		gemsGrid.AddThemeConstantOverride("h_separation", 20);
		gemsGrid.AddThemeConstantOverride("v_separation", 8);

		// Create labels for all 5 gem types
		foreach (GemType gemType in Enum.GetValues<GemType>())
		{
			var label = new Label();
			label.CustomMinimumSize = new Vector2(120, 0);
			label.Text = "Loading...";
			label.AddThemeColorOverride("font_color", GetTextColor());
			_gemLabels[gemType] = label;
			gemsGrid.AddChild(label);
		}

		inventoryGroup.AddChild(gemsGrid);
		parent.AddChild(inventoryGroup);
	}

	private void AddSeparator(VBoxContainer parent)
	{
		var separator = new HSeparator();
		separator.AddThemeConstantOverride("separation", 10);
		parent.AddChild(separator);
	}

	public void SetEnabled(bool enabled)
	{
		_isEnabled = enabled;

		// Apply visual state - greyed out when disabled, normal when enabled
		Modulate = enabled ? Colors.White : new Color(0.6f, 0.6f, 0.6f, 0.8f);

		// Disable/enable all interactive elements
		SetInteractiveElementsEnabled(enabled);

		if (enabled)
			UpdateAllUI();
	}

	private void SetInteractiveElementsEnabled(bool enabled)
	{
		// Disable all action buttons when disabled, but don't override their normal disabled state when enabled
		if (!enabled)
		{
			_extractButton.Disabled = true;
			_purchaseCreditButton.Disabled = true;
		}

		// Disable all upgrade buttons
		foreach (var upgradeGroup in _upgradeGroups.Values)
		{
			upgradeGroup.SetEnabled(enabled);
		}

		// Handle state messages
		if (!enabled)
		{
			// Clear all progress and gem displays to show no data
			_progressLabel.Text = "Please log in to start mining";
			_gemsReadyLabel.Text = "Gems ready: Login required";
			_capacityLabel.Text = "Capacity: Login required";

			// Clear gem labels
			foreach (var label in _gemLabels.Values)
			{
				label.Text = "Please log in";
			}

			// Clear progress bar
			_miningProgressBar.Value = 0;

			// Clear button text to default state
			_extractButton.Text = "Extract Gems";
			var creditsPerPurchase = _game?.GetCreditsPerPurchase() ?? 1000;
			_purchaseCreditButton.Text = $"Buy {creditsPerPurchase:N0} Credits ({GemTheme.GetGemEmoji(_gemType)} {_config.CreditCost})";
		}
	}

	public void UpdateAllUI()
	{
		// Always update UI components regardless of enabled state
		// Individual components should handle missing data and disabled state gracefully
		UpdateMiningProgress();
		UpdateActionsButtons();
		UpdateUpgrades();
		UpdateGlobalInventory();
	}

	public void UpdateMiningProgress(bool incrementalOnly = false)
	{
		float progress = _game.GetMiningProgress();

		// For incremental updates, only update progress bar if value changed significantly
		if (!incrementalOnly || Math.Abs(progress - _lastProgress) > PROGRESS_UPDATE_THRESHOLD)
		{
			_miningProgressBar.Value = progress * 100;
			_lastProgress = progress;
		}

		// Always update time display as it changes continuously
		float timeRemaining = _game.GetTimeUntilNextTick();
		UpdateTimeDisplay(timeRemaining);

		// Update gems display
		int pendingGems = _game.GetPendingGems();
		int maxCapacity = _game.GetMaxCapacity();

		bool gemsChanged = pendingGems != _lastPendingGems || maxCapacity != _lastMaxCapacity;

		// For incremental updates, only update gems if changed; full updates always update
		if (!incrementalOnly || gemsChanged)
		{
			_gemsReadyLabel.Text = $"Gems ready: {pendingGems}";
			_capacityLabel.Text = $"Capacity: {pendingGems}/{maxCapacity}";

			// Color code based on capacity
			bool atCapacity = pendingGems >= maxCapacity;
			var textColor = atCapacity ? Colors.Yellow : GetTextColor();
			_gemsReadyLabel.AddThemeColorOverride("font_color", textColor);
			_capacityLabel.AddThemeColorOverride("font_color", textColor);

			_lastPendingGems = pendingGems;
			_lastMaxCapacity = maxCapacity;
		}
	}

	private void UpdateTimeDisplay(float timeRemaining)
	{
		if (timeRemaining <= 0)
		{
			if (_lastDisplayedSeconds != 0)
			{
				_progressLabel.Text = "Mining...";
				_lastDisplayedSeconds = 0;
			}
			return;
		}

		int totalSeconds = (int)timeRemaining;
		if (totalSeconds == _lastDisplayedSeconds)
			return;
		_lastDisplayedSeconds = totalSeconds;

		int hours = totalSeconds / 3600;
		int minutes = (totalSeconds % 3600) / 60;
		int seconds = totalSeconds % 60;

		_progressLabel.Text = $"Next mining tick in: {hours:00}:{minutes:00}:{seconds:00}";
	}

	public void UpdateActionsButtons()
	{
		bool canExtract = _game.CanExtractGems();
		bool canPurchaseCredit = _game.CanPurchaseCredit();

		_extractButton.Disabled = !canExtract;
		_purchaseCreditButton.Disabled = !canPurchaseCredit;

		UIBuilder.UpdateButtonState(_extractButton, canExtract);
		UIBuilder.UpdateButtonState(_purchaseCreditButton, canPurchaseCredit);

		// Update extract button text based on gems available
		int pendingGems = _game.GetPendingGems();
		_extractButton.Text = pendingGems > 0 ? $"Extract {pendingGems} Gems" : "Extract Gems";
	}

	public void UpdateUpgrades()
	{
		foreach (var kvp in _upgradeGroups)
		{
			kvp.Value.UpdateUI();
		}
	}

	public void UpdateGlobalInventory()
	{
		var globalData = _game.GetGlobalData();

		// Update gem inventory - populate each grid cell
		foreach (GemType gemType in Enum.GetValues<GemType>())
		{
			if (_gemLabels.TryGetValue(gemType, out var label))
			{
				if (globalData == null)
				{
					label.Text = "Login required";
				}
				else
				{
					int amount = globalData.GetGems(gemType);
					var gemEmoji = GemTheme.GetGemEmoji(gemType);
					label.Text = $"{gemEmoji} {gemType}: {amount}";
				}
			}
		}
	}

	private void ApplyTheme()
	{
		Modulate = Colors.White;
	}

	/// <summary>
	/// Display an error message overlay to the user.
	/// Creates a semi-transparent popup with the error message and a dismiss button.
	/// </summary>
	public void ShowError(string errorTitle, string errorMessage)
	{
		GD.Print($"[MiningGameUI] ShowError called: {errorTitle} - {errorMessage}");

		// Create error overlay
		var errorOverlay = new ColorRect();
		errorOverlay.Name = "ErrorOverlay";
		errorOverlay.Color = new Color(0, 0, 0, 0.8f); // Semi-transparent black
		errorOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		errorOverlay.ZIndex = 100; // Ensure it's on top

		// Create error panel
		var errorPanel = new PanelContainer();
		errorPanel.SetAnchorsPreset(Control.LayoutPreset.Center);
		errorPanel.CustomMinimumSize = new Vector2(500, 300);

		// Create content container
		var contentContainer = new VBoxContainer();
		contentContainer.AddThemeConstantOverride("separation", 20);

		// Error title
		var titleLabel = new Label();
		titleLabel.Text = errorTitle;
		titleLabel.AddThemeFontSizeOverride("font_size", 32);
		titleLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.3f, 0.3f)); // Red
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;

		// Error message
		var messageLabel = new Label();
		messageLabel.Text = errorMessage;
		messageLabel.AddThemeFontSizeOverride("font_size", 20);
		messageLabel.AddThemeColorOverride("font_color", GetTextColor());
		messageLabel.HorizontalAlignment = HorizontalAlignment.Center;
		messageLabel.AutowrapMode = TextServer.AutowrapMode.Word;
		messageLabel.CustomMinimumSize = new Vector2(450, 0);

		// Dismiss button
		var dismissButton = new Button();
		dismissButton.Text = "OK";
		dismissButton.CustomMinimumSize = new Vector2(200, 60);
		dismissButton.AddThemeFontSizeOverride("font_size", 24);

		// Add spacer to push button to bottom
		var spacer = new Control();
		spacer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

		// Center button container
		var buttonContainer = new HBoxContainer();
		buttonContainer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		buttonContainer.AddChild(dismissButton);
		buttonContainer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

		// Assemble content
		contentContainer.AddChild(titleLabel);
		contentContainer.AddChild(messageLabel);
		contentContainer.AddChild(spacer);
		contentContainer.AddChild(buttonContainer);

		errorPanel.AddChild(contentContainer);
		errorOverlay.AddChild(errorPanel);

		// Connect dismiss button
		dismissButton.Pressed += () => {
			errorOverlay.QueueFree();
			GD.Print("[MiningGameUI] Error overlay dismissed");
		};

		// Add to scene tree
		AddChild(errorOverlay);

		GD.Print("[MiningGameUI] Error overlay created and displayed");
	}

	public override void _Process(double delta)
	{
		if (!_isEnabled)
			return;

		// Incremental update for real-time mining progress
		UpdateMiningProgress(incrementalOnly: true);
	}

	public partial class UpgradeUIGroup : VBoxContainer
	{
		private UpgradeType _upgradeType;
		private MiningGame _game;
		private MiningGameConfig _config;
		private MiningGameUI _parentUI;

		private Label _titleLabel;
		private Label _ticksLabel;
		private Label _descriptionLabel;
		private Label _costLabel;
		private Button _purchaseButton;

		public UpgradeUIGroup(UpgradeType upgradeType, MiningGame game, MiningGameConfig config, MiningGameUI parentUI)
		{
			_upgradeType = upgradeType;
			_game = game;
			_config = config;
			_parentUI = parentUI;

			CreateUpgradeUI();
		}

		// Helper methods to access parent UI theme
		private Color GetHeaderColor() => _parentUI.GetHeaderColor();
		private Color GetTextColor() => _parentUI.GetTextColor();
		private Color GetSecondaryAccent() => _parentUI.GetSecondaryAccent();

		private string FormatEnumName(string enumName)
		{
			return System.Text.RegularExpressions.Regex.Replace(
				enumName,
				"(?<!^)([A-Z])",
				" $1"
			);
		}

		private string GenerateTickIndicators(int currentLevel, int maxLevel)
		{
			var filled = new string('●', currentLevel);
			var empty = new string('○', maxLevel - currentLevel);
			return filled + empty;
		}

		private void CreateUpgradeUI()
		{
			// Title and level
			var headerHBox = new HBoxContainer();
			headerHBox.AddThemeConstantOverride("separation", 12);

			_titleLabel = new Label();
			_titleLabel.Text = GemTheme.GetUpgradeDisplayName(_upgradeType);
			_titleLabel.CustomMinimumSize = new Vector2(220, 0);
			_titleLabel.AddThemeColorOverride("font_color", GetHeaderColor());
			headerHBox.AddChild(_titleLabel);

			// Tick indicators
			_ticksLabel = new Label();
			_ticksLabel.Text = GenerateTickIndicators(0, _config.MaxUpgradeLevel);
			_ticksLabel.AddThemeColorOverride("font_color", GetSecondaryAccent());
			_ticksLabel.AddThemeFontSizeOverride("font_size", 14);
			headerHBox.AddChild(_ticksLabel);

			AddChild(headerHBox);

			// Description
			_descriptionLabel = new Label();
			_descriptionLabel.Text = GemTheme.GetUpgradeDescription(_upgradeType);
			_descriptionLabel.AddThemeColorOverride("font_color", Colors.Gray);
			_descriptionLabel.AddThemeFontSizeOverride("font_size", 12);
			AddChild(_descriptionLabel);

			// Cost and button
			var actionHBox = new HBoxContainer();
			actionHBox.AddThemeConstantOverride("separation", 10);

			_costLabel = new Label();
			_costLabel.CustomMinimumSize = new Vector2(250, 0);
			_costLabel.AddThemeColorOverride("font_color", GetTextColor());
			actionHBox.AddChild(_costLabel);

			_purchaseButton = UIBuilder.CreateActionButton("Upgrade", _parentUI);
			_purchaseButton.CustomMinimumSize = new Vector2(100, 30);
			_purchaseButton.Pressed += () => _game?.PurchaseUpgrade(_upgradeType);
			actionHBox.AddChild(_purchaseButton);

			AddChild(actionHBox);
		}

		public void UpdateUI()
		{
			int currentLevel = _game.GetUpgradeLevel(_upgradeType);
			int maxLevel = _game.Config.MaxUpgradeLevel;

			// Update title with level
			_titleLabel.Text = $"{GemTheme.GetUpgradeDisplayName(_upgradeType)} (Lv.{currentLevel}/{maxLevel})";

			// Update tick indicators
			_ticksLabel.Text = GenerateTickIndicators(currentLevel, maxLevel);

			// Update current stats
			string currentStats = GetCurrentStats(_upgradeType);
			_descriptionLabel.Text = $"{GemTheme.GetUpgradeDescription(_upgradeType)}\n{currentStats}";

			if (currentLevel >= maxLevel)
			{
				_costLabel.Text = "MAX LEVEL";
				_costLabel.AddThemeColorOverride("font_color", Colors.Gold);
				_purchaseButton.Disabled = true;
				_purchaseButton.Text = "MAXED";
			}
			else
			{
				// Show cost for next level - use UI's gem type for primary gem
				var primaryGemType = _parentUI.GetGemType();
				var cost = _game.Config.GetUpgradeCost(_upgradeType, currentLevel, primaryGemType);
				var costStrings = new List<string>();

				foreach (var kvp in cost)
				{
					var gemEmoji = GemTheme.GetGemEmoji(kvp.Key);
					costStrings.Add($"{gemEmoji} {kvp.Value}");
				}

				_costLabel.Text = $"Cost: {string.Join(", ", costStrings)}";
				_costLabel.AddThemeColorOverride("font_color", GetTextColor());

				// Show tier indicator
				var nextTier = _game.Config?.GetUpgradeTier(currentLevel + 1) ?? UpgradeTier.Tier1;
				_purchaseButton.Text = $"Upgrade (T{(int)nextTier})";

				_purchaseButton.Disabled = !_game.CanPurchaseUpgrade(_upgradeType);
			}

			UIBuilder.UpdateButtonState(_purchaseButton, !_purchaseButton.Disabled);
		}

		private string GetCurrentStats(UpgradeType upgradeType)
		{
			return upgradeType switch
			{
				UpgradeType.Capacity => $"Current: {_game.GetMaxCapacity()} gems",
				UpgradeType.MiningSpeed => $"Current: {_game.GetMiningTickTime() / 60:F1} minutes/tick",
				UpgradeType.MiningAmount => $"Current: {_game.GetGemsPerTick()} gems/tick",
				_ => ""
			};
		}

		public void SetEnabled(bool enabled)
		{
			if (!enabled)
			{
				// Disable the purchase button when UI is disabled
				_purchaseButton.Disabled = true;
			}
			else
			{
				// When re-enabling, let the normal UpdateUI logic handle the button state
				// based on actual game conditions (affordability, etc.)
				UpdateUI();
			}
		}

		public void RefreshTheme()
		{
			// Update label colors
			_titleLabel.AddThemeColorOverride("font_color", GetHeaderColor());
			_ticksLabel.AddThemeColorOverride("font_color", GetSecondaryAccent());
			_costLabel.AddThemeColorOverride("font_color", GetTextColor());

			// Refresh button styles
			var normalStyle = new StyleBoxFlat();
			normalStyle.BgColor = _parentUI.GetButtonEnabledColor();
			normalStyle.SetCornerRadiusAll(4);
			normalStyle.SetContentMarginAll(8);
			_purchaseButton.AddThemeStyleboxOverride("normal", normalStyle);

			var hoverStyle = new StyleBoxFlat();
			hoverStyle.BgColor = _parentUI.GetButtonEnabledColor() * 1.2f;
			hoverStyle.SetCornerRadiusAll(4);
			hoverStyle.SetContentMarginAll(8);
			_purchaseButton.AddThemeStyleboxOverride("hover", hoverStyle);

			var pressedStyle = new StyleBoxFlat();
			pressedStyle.BgColor = _parentUI.GetButtonEnabledColor() * 0.8f;
			pressedStyle.SetCornerRadiusAll(4);
			pressedStyle.SetContentMarginAll(8);
			_purchaseButton.AddThemeStyleboxOverride("pressed", pressedStyle);

			var disabledStyle = new StyleBoxFlat();
			disabledStyle.BgColor = _parentUI.GetButtonDisabledColor();
			disabledStyle.SetCornerRadiusAll(4);
			disabledStyle.SetContentMarginAll(8);
			_purchaseButton.AddThemeStyleboxOverride("disabled", disabledStyle);
		}
	}

	public static class UIBuilder
	{
		public static VBoxContainer CreateGroup(string title, MiningGameUI ui)
		{
			var group = new VBoxContainer();
			group.AddThemeConstantOverride("separation", 5);

			var header = new Label();
			header.Text = title;
			header.AddThemeFontSizeOverride("font_size", 18);
			header.AddThemeColorOverride("font_color", ui.GetPrimaryAccent());
			group.AddChild(header);

			return group;
		}

		public static Button CreateActionButton(string text, MiningGameUI ui)
		{
			var button = new Button();
			button.Text = text;

			// Create button styles
			var normalStyle = new StyleBoxFlat();
			normalStyle.BgColor = ui.GetButtonEnabledColor();
			normalStyle.SetCornerRadiusAll(4);
			normalStyle.SetContentMarginAll(8);
			button.AddThemeStyleboxOverride("normal", normalStyle);

			var hoverStyle = new StyleBoxFlat();
			hoverStyle.BgColor = ui.GetButtonEnabledColor() * 1.2f;
			hoverStyle.SetCornerRadiusAll(4);
			hoverStyle.SetContentMarginAll(8);
			button.AddThemeStyleboxOverride("hover", hoverStyle);

			var pressedStyle = new StyleBoxFlat();
			pressedStyle.BgColor = ui.GetButtonEnabledColor() * 0.8f;
			pressedStyle.SetCornerRadiusAll(4);
			pressedStyle.SetContentMarginAll(8);
			button.AddThemeStyleboxOverride("pressed", pressedStyle);

			var disabledStyle = new StyleBoxFlat();
			disabledStyle.BgColor = ui.GetButtonDisabledColor();
			disabledStyle.SetCornerRadiusAll(4);
			disabledStyle.SetContentMarginAll(8);
			button.AddThemeStyleboxOverride("disabled", disabledStyle);

			return button;
		}

		public static void UpdateButtonState(Button button, bool enabled)
		{
			if (enabled)
			{
				button.Modulate = Colors.White;
			}
			else
			{
				button.Modulate = new Color(0.7f, 0.7f, 0.7f);
			}
		}
	}
}