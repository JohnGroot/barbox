using System;
using Godot;

namespace BarBox.Games.MiningGame
{
	/// <summary>
	/// Resource for mining location data including UI theme and game mechanics.
	/// </summary>
	[GlobalClass]
	public partial class MiningLocationData : Resource
	{
		[Export] public string LocationId { get; set; } = "";
		[Export] public GemType PrimaryGemType { get; set; } = GemType.Amethyst;
		
		[ExportCategory("Location Settings")]
		[Export] public string LocationDisplayName { get; set; } = "Crystal Cavern";
		
		[ExportCategory("UI Theme")]
		[Export] public Color BackgroundColor { get; set; } = new(0.1f, 0.1f, 0.15f, 0.95f);
		[Export] public Color PrimaryAccent { get; set; } = new(0.4f, 0.2f, 0.8f);
		[Export] public Color SecondaryAccent { get; set; } = new(0.2f, 0.6f, 0.8f);
		[Export] public Color ProgressBarColor { get; set; } = new(0.3f, 0.7f, 0.9f);
		[Export] public Color ButtonEnabledColor { get; set; } = new(0.2f, 0.8f, 0.4f);
		[Export] public Color ButtonDisabledColor { get; set; } = new(0.3f, 0.3f, 0.35f);
		[Export] public Color TextColor { get; set; } = new(0.9f, 0.9f, 0.9f);
		[Export] public Color HeaderColor { get; set; } = new(1.0f, 0.95f, 0.8f);

		public int GetMaxCapacity(int capacityLevel, MiningGameConfig config)
		{
			if (config == null)
				throw new ArgumentNullException(nameof(config));
			
			return config.BaseCapacity + (capacityLevel * config.CapacityPerLevel);
		}
		
		public float GetMiningTickTime(int speedLevel, MiningGameConfig config)
		{
			if (config == null)
				throw new ArgumentNullException(nameof(config));
			
			return config.BaseMiningTimeSeconds * Mathf.Pow(config.SpeedMultiplierPerLevel, speedLevel);
		}
		
		public int GetGemsPerTick(int amountLevel, MiningGameConfig config)
		{
			if (config == null)
				throw new ArgumentNullException(nameof(config));
			
			return config.BaseGemsPerTick + (amountLevel * config.AmountPerLevel);
		}
		
		public string GetUpgradeEmoji(UpgradeType upgradeType)
		{
			return upgradeType switch
			{
				UpgradeType.Capacity => "💼",
				UpgradeType.MiningSpeed => "⚡",
				UpgradeType.MiningAmount => "🪨",
				_ => ""
			};
		}

		private string FormatEnumName(string enumName)
		{
			// Insert space before each capital letter (except first)
			return System.Text.RegularExpressions.Regex.Replace(
				enumName,
				"(?<!^)([A-Z])",
				" $1"
			);
		}

		public string GetUpgradeDisplayName(UpgradeType upgradeType)
		{
			return upgradeType switch
			{
				UpgradeType.Capacity => $"{GetUpgradeEmoji(upgradeType)} Gem Capacity",
				UpgradeType.MiningSpeed => $"{GetUpgradeEmoji(upgradeType)} Mining Speed",
				UpgradeType.MiningAmount => $"{GetUpgradeEmoji(upgradeType)} Gems Per Tick",
				_ => $"{GetUpgradeEmoji(upgradeType)} {FormatEnumName(upgradeType.ToString())}"
			};
		}
		
		public string GetUpgradeDescription(UpgradeType upgradeType)
		{
			return upgradeType switch
			{
				UpgradeType.Capacity => "Increases maximum gem storage capacity",
				UpgradeType.MiningSpeed => "Reduces time between mining ticks",
				UpgradeType.MiningAmount => "Increases gems earned per tick",
				_ => "Improves mining operations"
			};
		}
		
		public string GetGemEmoji(GemType gemType)
		{
			return gemType switch
			{
				GemType.Ruby => "🟥",
				GemType.Sapphire => "🔷",
				GemType.Emerald => "🟢",
				GemType.Diamond => "💎",
				GemType.Amethyst => "🟣",
				_ => "💠"
			};
		}

		public Color GetGemColor(GemType gemType)
		{
			return gemType switch
			{
				GemType.Ruby => new Color(0.8f, 0.1f, 0.2f),
				GemType.Sapphire => new Color(0.1f, 0.3f, 0.8f),
				GemType.Emerald => new Color(0.1f, 0.7f, 0.2f),
				GemType.Diamond => new Color(0.9f, 0.9f, 0.95f),
				GemType.Amethyst => new Color(0.6f, 0.2f, 0.8f),
				_ => Colors.Gray
			};
		}
		
		public string GetGemIconPath(GemType gemType)
		{
			// Placeholder paths - replace with actual icon paths when assets are available
			return $"res://Assets/Icons/Gems/{gemType.ToString().ToLower()}.png";
		}
	}
}