using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace BarBox.Games.MiningGame
{
	[GlobalClass]
	public partial class MiningGameUI : Control
	{
		// ================================================================
		// UI CONSTANTS
		// ================================================================
		
		private const int MENU_BAR_OFFSET = 160;  // Account for menu bar + expandable section
		private const int CONTAINER_OFFSET = 180; // Menu bar + some padding
		private const int UI_MARGIN = 20;
		private const float PROGRESS_UPDATE_THRESHOLD = 0.01f;
		// ================================================================
		// UI NODES
		// ================================================================
		
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
		private Label _globalGemsLabel;
		private VBoxContainer _creditTimersContainer;
		private List<Label> _cachedTimerLabels = new();
		private Label _noTimersLabel;
		
		// ================================================================
		// REFERENCES
		// ================================================================
		
		private MiningGame _game;
		private MiningGameConfig _config;
		private MiningLocationData _locationData;
		private bool _isEnabled = false;
		
		// Performance optimization flags
		private int _lastPendingGems = -1;
		private int _lastMaxCapacity = -1;
		private float _lastProgress = -1;
			
		// ================================================================
		// INITIALIZATION
		// ================================================================
		
		public void Initialize(MiningGame game, MiningGameConfig config)
		{
			_game = game;
			_config = config;
			_locationData = game.GetLocationData(); // Get location data for theming
			
			CreateUI();
			ApplyTheme();
		}
		
		// Theme property accessors with fallbacks
		private Color GetBackgroundColor() => _locationData?.BackgroundColor ?? new Color(0.1f, 0.1f, 0.15f, 0.95f);
		public Color GetPrimaryAccent() => _locationData?.PrimaryAccent ?? new Color(0.4f, 0.2f, 0.8f);
		public Color GetSecondaryAccent() => _locationData?.SecondaryAccent ?? new Color(0.2f, 0.6f, 0.8f);
		private Color GetProgressBarColor() => _locationData?.ProgressBarColor ?? new Color(0.3f, 0.7f, 0.9f);
		public Color GetButtonEnabledColor() => _locationData?.ButtonEnabledColor ?? new Color(0.2f, 0.8f, 0.4f);
		public Color GetButtonDisabledColor() => _locationData?.ButtonDisabledColor ?? new Color(0.3f, 0.3f, 0.35f);
		public Color GetTextColor() => _locationData?.TextColor ?? new Color(0.9f, 0.9f, 0.9f);
		public Color GetHeaderColor() => _locationData?.HeaderColor ?? new Color(1.0f, 0.95f, 0.8f);
		private string GetLocationDisplayName() => _locationData?.LocationDisplayName ?? "Crystal Cavern";
		
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
			
			_purchaseCreditButton = UIBuilder.CreateActionButton($"Purchase Credit ({_config.CreditCost} gems)", this);
			_purchaseCreditButton.CustomMinimumSize = new Vector2(200, 40);
			_purchaseCreditButton.Pressed += () => _game?.PurchaseCredit();
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
			
			_globalGemsLabel = new Label();
			_globalGemsLabel.Text = "Loading gem inventory...";
			_globalGemsLabel.AddThemeColorOverride("font_color", GetTextColor());
			inventoryGroup.AddChild(_globalGemsLabel);
			
			// Credit timers section
			var creditSection = new VBoxContainer();
			creditSection.AddThemeConstantOverride("separation", 4);
			
			var creditHeader = new Label();
			creditHeader.Text = "Credit Recharge Timers:";
			creditHeader.AddThemeColorOverride("font_color", GetSecondaryAccent());
			creditSection.AddChild(creditHeader);
			
			_creditTimersContainer = new VBoxContainer();
			creditSection.AddChild(_creditTimersContainer);
			
			inventoryGroup.AddChild(creditSection);
			parent.AddChild(inventoryGroup);
		}
		
		private void AddSeparator(VBoxContainer parent)
		{
			var separator = new HSeparator();
			separator.AddThemeConstantOverride("separation", 10);
			parent.AddChild(separator);
		}
		
		// ================================================================
		// PUBLIC API - Direct method calls from MiningGame
		// ================================================================
		
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
				if (_extractButton != null)
					_extractButton.Disabled = true;
				if (_purchaseCreditButton != null)
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
				// Set disabled state messages
				if (_progressLabel != null)
					_progressLabel.Text = "Please log in to start mining";
				if (_gemsReadyLabel != null)
					_gemsReadyLabel.Text = "Gems ready: Login required";
				if (_globalGemsLabel != null)
					_globalGemsLabel.Text = "Please log in to view inventory";
			}
		}
		
		public void UpdateAllUI()
		{
			if (!_isEnabled) 
				return;
			
			UpdateMiningProgress();
			UpdateActionsButtons();
			UpdateUpgrades();
			UpdateGlobalInventory();
		}
		
		public void UpdateMiningProgress()
		{
			var locationTemplate = _game?.GetLocationData();
			if (locationTemplate == null || _game == null)
				return;
			
			// Update progress bar
			float progress = _game.GetMiningProgress();
			_miningProgressBar.Value = progress * 100;
			
			float timeRemaining = _game.GetTimeUntilNextTick();
			UpdateTimeDisplay(timeRemaining);
			
			// Update gems display
			int pendingGems = _game.GetPendingGems();
			int maxCapacity = _game.GetMaxCapacity();
			
			_gemsReadyLabel.Text = $"Gems ready: {pendingGems}";
			_capacityLabel.Text = $"Capacity: {pendingGems}/{maxCapacity}";
			
			// Color code based on capacity
			if (pendingGems >= maxCapacity)
			{
				_gemsReadyLabel.AddThemeColorOverride("font_color", Colors.Yellow);
				_capacityLabel.AddThemeColorOverride("font_color", Colors.Yellow);
			}
			else
			{
				_gemsReadyLabel.AddThemeColorOverride("font_color", GetTextColor());
				_capacityLabel.AddThemeColorOverride("font_color", GetTextColor());
			}
		}
		
		private void UpdateTimeDisplay(float timeRemaining)
		{
			if (timeRemaining <= 0)
			{
				_progressLabel.Text = "Mining...";
				return;
			}
			
			int hours = (int)(timeRemaining / 3600);
			int minutes = (int)((timeRemaining % 3600) / 60);
			int seconds = (int)(timeRemaining % 60);
			
			_progressLabel.Text = $"Next mining tick in: {hours:00}:{minutes:00}:{seconds:00}";
		}
		
		public void UpdateActionsButtons()
		{
			bool canExtract = _game?.CanExtractGems() ?? false;
			bool canPurchaseCredit = _game?.CanPurchaseCredit() ?? false;
			
			_extractButton.Disabled = !canExtract;
			_purchaseCreditButton.Disabled = !canPurchaseCredit;
			
			UIBuilder.UpdateButtonState(_extractButton, canExtract);
			UIBuilder.UpdateButtonState(_purchaseCreditButton, canPurchaseCredit);
			
			// Update extract button text based on gems available
			var locationData = _game?.GetLocationData();
			if (locationData != null && _game != null)
			{
				int pendingGems = _game.GetPendingGems();
				if (pendingGems > 0)
				{
					_extractButton.Text = $"Extract {pendingGems} Gems";
				}
				else
				{
					_extractButton.Text = "Extract Gems";
				}
			}
			else
			{
				_extractButton.Text = "Extract Gems";
			}
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
			var globalData = _game?.GetGlobalData();
			if (globalData == null) return;
			
			// Update gem inventory
			var inventoryLines = new List<string>();
			foreach (GemType gemType in Enum.GetValues<GemType>())
			{
				if (globalData.GlobalGemInventory.TryGetValue(gemType, out int amount) && amount > 0)
				{
					var gemColor = _locationData?.GetGemColor(gemType) ?? Colors.Gray;
					inventoryLines.Add($"  {gemType}: {amount}");
				}
			}
			
			if (inventoryLines.Count > 0)
			{
				_globalGemsLabel.Text = "Gems:\n" + string.Join("\n", inventoryLines);
			}
			else
			{
				_globalGemsLabel.Text = "No gems in inventory";
			}
			
			// Update credit timers using cached labels
			UpdateCreditTimers(globalData);
		}
		
		private void ApplyTheme()
		{
			Modulate = Colors.White;
		}
		
		private void UpdateCreditTimers(MiningGlobalData globalData)
		{
			var activeTimers = globalData.CreditTimers
				.Where(timer => !timer.IsRecharged(_game.GetStateTime()))
				.ToList();
			
			// Hide all cached labels first
			foreach (var label in _cachedTimerLabels)
				label.Visible = false;
			if (_noTimersLabel != null)
				_noTimersLabel.Visible = false;
			
			if (activeTimers.Count == 0)
			{
				// Show "no timers" message
				if (_noTimersLabel == null)
				{
					_noTimersLabel = new Label();
					_noTimersLabel.Text = "  No active timers";
					_noTimersLabel.AddThemeColorOverride("font_color", Colors.Gray);
					_creditTimersContainer.AddChild(_noTimersLabel);
				}
				_noTimersLabel.Visible = true;
			}
			else
			{
				// Update or create timer labels as needed
				for (int i = 0; i < activeTimers.Count; i++)
				{
					Label timerLabel;
					if (i < _cachedTimerLabels.Count)
					{
						timerLabel = _cachedTimerLabels[i];
					}
					else
					{
						timerLabel = new Label();
						timerLabel.AddThemeColorOverride("font_color", GetSecondaryAccent());
						_cachedTimerLabels.Add(timerLabel);
						_creditTimersContainer.AddChild(timerLabel);
					}
					
					float hoursRemaining = activeTimers[i].GetTimeRemainingHours(_game.GetStateTime());
					
					if (hoursRemaining < 1)
					{
						int minutesRemaining = (int)(hoursRemaining * 60);
						timerLabel.Text = $"  Credit recharging: {minutesRemaining} minutes";
					}
					else
					{
						timerLabel.Text = $"  Credit recharging: {hoursRemaining:F1} hours";
					}
					
					timerLabel.Visible = true;
				}
			}
		}
		
		public override void _Process(double delta)
		{
			if (!_isEnabled)
				return;

			// Only update real-time mining progress
			UpdateMiningProgressOnly();
		}
		
		private void UpdateMiningProgressOnly()
		{
			if (_game == null) return;
			
			float progress = _game.GetMiningProgress();
			
			// Only update progress bar if value changed significantly
			if (Math.Abs(progress - _lastProgress) > PROGRESS_UPDATE_THRESHOLD)
			{
				_miningProgressBar.Value = progress * 100;
				_lastProgress = progress;
			}
			
			// Always update time display as it changes continuously
			float timeRemaining = _game.GetTimeUntilNextTick();
			UpdateTimeDisplay(timeRemaining);
			
			// Update gems display only when values change
			int pendingGems = _game.GetPendingGems();
			int maxCapacity = _game.GetMaxCapacity();
			
			bool gemsChanged = pendingGems != _lastPendingGems;
			bool capacityChanged = maxCapacity != _lastMaxCapacity;
			
			if (gemsChanged || capacityChanged)
			{
				_gemsReadyLabel.Text = $"Gems ready: {pendingGems}";
				_capacityLabel.Text = $"Capacity: {pendingGems}/{maxCapacity}";
				
				// Update colors
				bool atCapacity = pendingGems >= maxCapacity;
				var textColor = atCapacity ? Colors.Yellow : GetTextColor();
				_gemsReadyLabel.AddThemeColorOverride("font_color", textColor);
				_capacityLabel.AddThemeColorOverride("font_color", textColor);
				
				_lastPendingGems = pendingGems;
				_lastMaxCapacity = maxCapacity;
			}
		}
		
		// ================================================================
		// NESTED CLASSES - UI helpers and specialized components
		// ================================================================
		
		public partial class UpgradeUIGroup : VBoxContainer
		{
			private UpgradeType _upgradeType;
			private MiningGame _game;
			private MiningGameConfig _config;
			private MiningGameUI _parentUI;
			
			private Label _titleLabel;
			private Label _descriptionLabel;
			private Label _costLabel;
			private Button _purchaseButton;
			private ProgressBar _levelProgressBar;
			
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
			
			private void CreateUpgradeUI()
			{
				// Title and level
				var headerHBox = new HBoxContainer();
				
				_titleLabel = new Label();
				_titleLabel.Text = _parentUI._locationData?.GetUpgradeDisplayName(_upgradeType) ?? _upgradeType.ToString();
				_titleLabel.CustomMinimumSize = new Vector2(150, 0);
				_titleLabel.AddThemeColorOverride("font_color", GetHeaderColor());
				headerHBox.AddChild(_titleLabel);
				
				// Level progress bar
				_levelProgressBar = new ProgressBar();
				_levelProgressBar.CustomMinimumSize = new Vector2(150, 20);
				_levelProgressBar.MaxValue = _config.MaxUpgradeLevel;
				_levelProgressBar.Value = 0;
				_levelProgressBar.ShowPercentage = false;
				
				var levelStyle = new StyleBoxFlat();
				levelStyle.BgColor = GetSecondaryAccent();
				levelStyle.SetCornerRadiusAll(2);
				_levelProgressBar.AddThemeStyleboxOverride("fill", levelStyle);
				
				headerHBox.AddChild(_levelProgressBar);
				
				AddChild(headerHBox);
				
				// Description
				_descriptionLabel = new Label();
				_descriptionLabel.Text = _parentUI._locationData?.GetUpgradeDescription(_upgradeType) ?? "Improves mining operations";
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
				var locationTemplate = _game?.GetLocationData();
				if (locationTemplate == null || _game == null) return;
				
				int currentLevel = _game.GetUpgradeLevel(_upgradeType);
				int maxLevel = _game.Config.MaxUpgradeLevel;
				
				// Update title with level
				_titleLabel.Text = $"{_parentUI._locationData?.GetUpgradeDisplayName(_upgradeType) ?? _upgradeType.ToString()} (Lv.{currentLevel}/{maxLevel})";
				
				// Update level progress bar
				_levelProgressBar.Value = currentLevel;
				
				// Update current stats
				string currentStats = GetCurrentStats(_upgradeType);
				_descriptionLabel.Text = $"{_parentUI._locationData?.GetUpgradeDescription(_upgradeType) ?? "Improves mining operations"}\n{currentStats}";
				
				if (currentLevel >= maxLevel)
				{
					_costLabel.Text = "MAX LEVEL";
					_costLabel.AddThemeColorOverride("font_color", Colors.Gold);
					_purchaseButton.Disabled = true;
					_purchaseButton.Text = "MAXED";
				}
				else
				{
					// Show cost for next level
					var cost = _game.Config.GetUpgradeCost(_upgradeType, currentLevel, locationTemplate.PrimaryGemType);
					var costStrings = new List<string>();
					
					foreach (var kvp in cost)
					{
						var gemColor = _parentUI._locationData?.GetGemColor(kvp.Key) ?? Colors.Gray;
						costStrings.Add($"{kvp.Value} {kvp.Key}");
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
					UpgradeType.CreditCharges => $"Current: {_game.GetMaxCreditCharges()} simultaneous purchases",
					_ => ""
				};
			}
			
			public void SetEnabled(bool enabled)
			{
				if (!enabled)
				{
					// Disable the purchase button when UI is disabled
					if (_purchaseButton != null)
						_purchaseButton.Disabled = true;
				}
				else
				{
					// When re-enabling, let the normal UpdateUI logic handle the button state
					// based on actual game conditions (affordability, etc.)
					UpdateUI();
				}
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
}