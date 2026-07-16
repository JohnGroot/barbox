using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Data structure for defining game help content
/// Supports sections with headers, paragraphs, and lists for clear organization
/// </summary>
public struct HelpContentData
{
	public string Title;
	public List<HelpSection> Sections;

	public HelpContentData(string title)
	{
		Title = title;
		Sections = new List<HelpSection>();
	}

	public HelpContentData AddSection(string header, string content)
	{
		Sections.Add(new HelpSection(header, content));
		return this;
	}

	public HelpContentData AddSection(string header, params string[] paragraphs)
	{
		Sections.Add(new HelpSection(header, paragraphs));
		return this;
	}

	public HelpContentData AddListSection(string header, params string[] listItems)
	{
		Sections.Add(new HelpSection(header, listItems, true));
		return this;
	}
}

/// <summary>
/// Represents a section within help content
/// </summary>
public struct HelpSection
{
	public string Header;
	public List<string> Content;
	public bool IsList;

	public HelpSection(string header, string content)
	{
		Header = header;
		Content = new List<string> { content };
		IsList = false;
	}

	public HelpSection(string header, string[] content, bool isList = false)
	{
		Header = header;
		Content = [.. content];
		IsList = isList;
	}
}

/// <summary>
/// Factory methods for common help content patterns
/// </summary>
public static class HelpContentFactory
{
	public static HelpContentData CreateBasicGameHelp(string gameTitle)
	{
		return new HelpContentData($"{gameTitle} - How to Play")
			.AddSection("🎮 Basic Controls", "Touch or click to interact with the game.")
			.AddSection("⏸️ Pause & Resume", "Use the pause button in the top menu to pause the game.")
			.AddSection("🏠 Return to Menu", "Use the home button to return to the main menu.");
	}

	public static HelpContentData CreateMiningGameHelp()
	{
		return new HelpContentData("Mining Game - Complete Guide")
			.AddSection(
				"⛏️ How Mining Works",
				"Gems accumulate automatically every 2 hours while the machine is running.",
				"Each location mines a specific type of gem with unique properties.",
				"Mining continues even when you're not actively playing.")

			.AddSection(
				"💎 Extracting Gems",
				"Gems are stored locally on each machine until extracted.",
				"Click 'Extract' to move ready gems to your global inventory.",
				"Each machine has a maximum capacity - extract regularly to avoid waste.")

			.AddSection(
				"🪙 Purchasing Credits",
				"Trade 150 gems of any type for 1 credit.",
				"Credits can be used to play other BarBox games.",
				"Each credit has a 24-hour recharge timer before you can buy another.")

			.AddListSection(
				"⬆️ Upgrade Types",
				"💼 Capacity - Increases maximum gem storage",
				"⚡ Mining Amount - More gems per mining cycle",
				"🚀 Mining Speed - Faster mining cycles",
				"🎫 Credit Charges - Purchase multiple credits")

			.AddSection(
				"🏆 Upgrade Progression",
				"Each upgrade has 15 levels with increasing costs.",
				"Tier 1 (Levels 1-5): Only location gems required",
				"Tier 2 (Levels 6-10): Location gems + 1 random type",
				"Tier 3 (Levels 11-15): Location gems + 2 random types")

			.AddSection(
				"🗺️ Multiple Locations",
				"Each BarBox location has different gem types and themes.",
				"Upgrades are specific to each machine - progress separately.",
				"Visit different locations to mine various gem types.");
	}
}
