using Godot;
using System;
using System.Collections.Generic;

[GlobalClass]
public partial class UserData : Resource
{
	[Export] public string UserId { get; set; } = string.Empty;
	[Export] public string Pin { get; set; } = string.Empty;
	[Export] public int Credits { get; set; } = 0;
	public DateTime CreatedAt { get; set; } = DateTime.Now;
	public DateTime LastLoginAt { get; set; } = DateTime.Now;
	[Export] public Godot.Collections.Dictionary<string, int> HighScores { get; set; } = new();

	public static UserData FromDictionary(Godot.Collections.Dictionary data)
	{
		var userData = new UserData
		{
			UserId = data.GetValueOrDefault("user_id", "").AsString(),
			Pin = data.GetValueOrDefault("pin", "").AsString(),
			Credits = data.GetValueOrDefault("credits", 0).AsInt32(),
			CreatedAt = DateTime.Parse(data.GetValueOrDefault("created_at", DateTime.Now.ToString()).AsString()),
			LastLoginAt = DateTime.Parse(data.GetValueOrDefault("last_login_at", DateTime.Now.ToString()).AsString())
		};

		if (data.ContainsKey("high_scores"))
		{
			var highScoresDict = data["high_scores"].AsGodotDictionary();
			foreach (var kvp in highScoresDict)
			{
				userData.HighScores[kvp.Key.AsString()] = kvp.Value.AsInt32();
			}
		}

		return userData;
	}

	public Godot.Collections.Dictionary ToDictionary()
	{
		return new Godot.Collections.Dictionary
		{
			{ "user_id", UserId },
			{ "pin", Pin },
			{ "credits", Credits },
			{ "created_at", CreatedAt.ToString() },
			{ "last_login_at", LastLoginAt.ToString() },
			{ "high_scores", HighScores }
		};
	}
}