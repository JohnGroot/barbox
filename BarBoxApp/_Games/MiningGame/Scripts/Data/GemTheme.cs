using Godot;

namespace BarBox.Games.MiningGame;

/// <summary>
/// Static utility for deriving deterministic UI themes from gem types.
/// All colors and visual properties are computed from the gem type,
/// eliminating the need for per-location color configuration.
/// </summary>
public static class GemTheme
{
	public static Color GetGemColor(GemType gemType) => gemType switch
	{
		GemType.Ruby => new Color(0.8f, 0.1f, 0.2f),
		GemType.Sapphire => new Color(0.1f, 0.3f, 0.8f),
		GemType.Emerald => new Color(0.1f, 0.7f, 0.2f),
		GemType.Diamond => new Color(0.9f, 0.9f, 0.95f),
		GemType.Amethyst => new Color(0.6f, 0.2f, 0.8f),
		_ => Colors.Gray,
	};

	public static string GetGemEmoji(GemType gemType) => gemType switch
	{
		GemType.Ruby => "🟥",
		GemType.Sapphire => "🔷",
		GemType.Emerald => "🟢",
		GemType.Diamond => "💎",
		GemType.Amethyst => "🟣",
		_ => "💠",
	};

	public static string GetGemIconPath(GemType gemType) =>
		$"res://Assets/Icons/Gems/{gemType.ToString().ToLower()}.png";

	public static Color GetBackgroundColor(GemType gemType)
	{
		var baseColor = GetGemColor(gemType);
		return new Color(
			baseColor.R * 0.15f,
			baseColor.G * 0.15f,
			baseColor.B * 0.15f,
			0.95f);
	}

	public static Color GetPrimaryAccent(GemType gemType) =>
		GetGemColor(gemType).Lightened(0.1f);

	public static Color GetSecondaryAccent(GemType gemType) =>
		GetGemColor(gemType).Darkened(0.2f);

	public static Color GetProgressBarColor(GemType gemType) =>
		GetGemColor(gemType).Lightened(0.3f);

	public static Color GetButtonEnabledColor(GemType _) =>
		new(0.2f, 0.8f, 0.4f);

	public static Color GetButtonDisabledColor(GemType _) =>
		new(0.3f, 0.3f, 0.35f);

	public static Color GetTextColor(GemType _) =>
		new(0.9f, 0.9f, 0.9f);

	public static Color GetHeaderColor(GemType gemType) =>
		GetGemColor(gemType).Lightened(0.5f);

	public static string GetUpgradeEmoji(UpgradeType upgradeType) => upgradeType switch
	{
		UpgradeType.Capacity => "💼",
		UpgradeType.MiningSpeed => "⚡",
		UpgradeType.MiningAmount => "🪨",
		_ => string.Empty,
	};

	public static string GetUpgradeDisplayName(UpgradeType upgradeType) => upgradeType switch
	{
		UpgradeType.Capacity => $"{GetUpgradeEmoji(upgradeType)} Gem Capacity",
		UpgradeType.MiningSpeed => $"{GetUpgradeEmoji(upgradeType)} Mining Speed",
		UpgradeType.MiningAmount => $"{GetUpgradeEmoji(upgradeType)} Gems Per Tick",
		_ => $"{GetUpgradeEmoji(upgradeType)} {FormatEnumName(upgradeType.ToString())}",
	};

	public static string GetUpgradeDescription(UpgradeType upgradeType) => upgradeType switch
	{
		UpgradeType.Capacity => "Increases maximum gem storage capacity",
		UpgradeType.MiningSpeed => "Reduces time between mining ticks",
		UpgradeType.MiningAmount => "Increases gems earned per tick",
		_ => "Improves mining operations",
	};

	private static string FormatEnumName(string enumName) =>
		System.Text.RegularExpressions.Regex.Replace(enumName, "(?<!^)([A-Z])", " $1");
}
