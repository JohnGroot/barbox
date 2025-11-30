namespace BarBox.Core.UI;

using Godot;
using Godot.Collections;

/// <summary>
/// Loads keyboard layouts from JSON files
/// </summary>
public static class OnScreenKeyboardLayoutLoader
{
	/// <summary>
	/// Load layouts from a JSON file path
	/// </summary>
	public static KeyboardLayout[] LoadFromFile(string filePath)
	{
		if (!FileAccess.FileExists(filePath))
		{
			GD.PrintErr($"Keyboard layout file not found: {filePath}");
			return System.Array.Empty<KeyboardLayout>();
		}

		using var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			GD.PrintErr($"Failed to open keyboard layout file: {filePath}");
			return System.Array.Empty<KeyboardLayout>();
		}

		var jsonText = file.GetAsText();
		return ParseJson(jsonText);
	}

	/// <summary>
	/// Parse layouts from JSON string
	/// </summary>
	public static KeyboardLayout[] ParseJson(string jsonText)
	{
		var json = new Json();
		var error = json.Parse(jsonText);
		if (error != Error.Ok)
		{
			GD.PrintErr($"Failed to parse keyboard layout JSON: {json.GetErrorMessage()} at line {json.GetErrorLine()}");
			return System.Array.Empty<KeyboardLayout>();
		}

		var data = json.Data.AsGodotDictionary();
		if (data == null || !data.ContainsKey("layouts"))
		{
			GD.PrintErr("Keyboard layout JSON missing 'layouts' array");
			return System.Array.Empty<KeyboardLayout>();
		}

		var layoutsArray = data["layouts"].AsGodotArray();
		var layouts = new System.Collections.Generic.List<KeyboardLayout>();

		foreach (var layoutVariant in layoutsArray)
		{
			var layoutDict = layoutVariant.AsGodotDictionary();
			if (layoutDict == null) continue;

			var layout = ParseLayout(layoutDict);
			layouts.Add(layout);
		}

		return layouts.ToArray();
	}

	private static KeyboardLayout ParseLayout(Dictionary layoutDict)
	{
		var layout = new KeyboardLayout
		{
			Name = layoutDict.ContainsKey("name") ? layoutDict["name"].AsString() : "unnamed"
		};

		if (!layoutDict.ContainsKey("rows"))
		{
			layout.Rows = System.Array.Empty<KeyboardRow>();
			return layout;
		}

		var rowsArray = layoutDict["rows"].AsGodotArray();
		var rows = new System.Collections.Generic.List<KeyboardRow>();

		foreach (var rowVariant in rowsArray)
		{
			var rowDict = rowVariant.AsGodotDictionary();
			if (rowDict == null) continue;

			var row = ParseRow(rowDict);
			rows.Add(row);
		}

		layout.Rows = rows.ToArray();
		return layout;
	}

	private static KeyboardRow ParseRow(Dictionary rowDict)
	{
		var row = new KeyboardRow();

		if (!rowDict.ContainsKey("keys"))
		{
			row.Keys = System.Array.Empty<KeyData>();
			return row;
		}

		var keysArray = rowDict["keys"].AsGodotArray();
		var keys = new System.Collections.Generic.List<KeyData>();

		foreach (var keyVariant in keysArray)
		{
			var keyDict = keyVariant.AsGodotDictionary();
			if (keyDict == null) continue;

			var key = ParseKey(keyDict);
			keys.Add(key);
		}

		row.Keys = keys.ToArray();
		return row;
	}

	private static KeyData ParseKey(Dictionary keyDict)
	{
		var key = KeyData.Default;

		// Parse type
		if (keyDict.ContainsKey("type"))
		{
			key.Type = ParseKeyType(keyDict["type"].AsString());
		}

		// Parse output
		if (keyDict.ContainsKey("output"))
		{
			key.Output = keyDict["output"].AsString();
		}

		// Parse display text
		if (keyDict.ContainsKey("display"))
		{
			key.Display = keyDict["display"].AsString();
		}

		// Parse uppercase display
		if (keyDict.ContainsKey("display-uppercase"))
		{
			key.DisplayUppercase = keyDict["display-uppercase"].AsString();
		}

		// Parse stretch ratio
		if (keyDict.ContainsKey("stretch-ratio"))
		{
			key.StretchRatio = (float)keyDict["stretch-ratio"].AsDouble();
		}

		// Parse layout name for switch-layout keys
		if (keyDict.ContainsKey("layout-name"))
		{
			key.TargetLayout = keyDict["layout-name"].AsString();
		}

		// Parse icon
		if (keyDict.ContainsKey("display-icon"))
		{
			var iconStr = keyDict["display-icon"].AsString();
			ParseIcon(iconStr, ref key);
		}

		return key;
	}

	private static KeyType ParseKeyType(string typeStr)
	{
		return typeStr switch
		{
			"char" => KeyType.Char,
			"special" => KeyType.Special,
			"special-shift" => KeyType.SpecialShift,
			"switch-layout" => KeyType.SwitchLayout,
			"special-hide-keyboard" => KeyType.HideKeyboard,
			"spacer" => KeyType.Spacer,
			_ => KeyType.Char
		};
	}

	private static void ParseIcon(string iconStr, ref KeyData key)
	{
		if (string.IsNullOrEmpty(iconStr))
			return;

		// Check for predefined icons: "PREDEFINED:DELETE", "PREDEFINED:SHIFT", etc.
		if (iconStr.StartsWith("PREDEFINED:"))
		{
			var iconName = iconStr.Substring(11); // After "PREDEFINED:"
			key.Icon = iconName switch
			{
				"DELETE" => PredefinedIcon.Delete,
				"SHIFT" => PredefinedIcon.Shift,
				"LEFT" => PredefinedIcon.Left,
				"RIGHT" => PredefinedIcon.Right,
				"HIDE" => PredefinedIcon.Hide,
				"ENTER" => PredefinedIcon.Enter,
				_ => PredefinedIcon.None
			};
		}
		// Custom icon path starting with "res://"
		else if (iconStr.StartsWith("res://") || iconStr.StartsWith("res:"))
		{
			key.CustomIconPath = iconStr;
		}
	}
}
