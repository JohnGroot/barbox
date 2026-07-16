using Godot;

namespace BarBox.Core.UI;

/// <summary>
/// Key type classifications for keyboard behavior
/// </summary>
public enum KeyType
{
	Char,           // Normal character key
	Special,        // Special action (backspace, enter, arrows)
	SpecialShift,   // Shift/caps lock toggle
	SwitchLayout,   // Switch to another layout
	HideKeyboard,   // Hide the keyboard
	Spacer, // Empty non-interactive spacer
}

/// <summary>
/// Predefined icon types that can be used for keys
/// </summary>
public enum PredefinedIcon
{
	None,
	Delete,
	Shift,
	Left,
	Right,
	Hide,
	Enter,
}

/// <summary>
/// Data for a single key parsed from JSON layout
/// </summary>
public struct KeyData
{
	public KeyType Type;
	public string Output;           // Key code string (e.g., "A", "Backspace", "Space")
	public string Display;          // Text to display on key
	public string DisplayUppercase; // Text when uppercase (null = use Display)
	public PredefinedIcon Icon;     // Predefined icon to display
	public string CustomIconPath;   // Custom icon resource path
	public float StretchRatio;      // Width multiplier (default 1.0)
	public string TargetLayout;     // Target layout name for switch-layout keys

	public static KeyData Default => new()
	{
		Type = KeyType.Char,
		StretchRatio = 1.0f,
	};

	public readonly string GetDisplayText(bool uppercase)
	{
		if (uppercase && !string.IsNullOrEmpty(DisplayUppercase))
		{
			return DisplayUppercase;
		}

		return Display ?? string.Empty;
	}

	public readonly bool HasIcon => Icon != PredefinedIcon.None || !string.IsNullOrEmpty(CustomIconPath);
}

/// <summary>
/// A row of keys in a keyboard layout
/// </summary>
public struct KeyboardRow
{
	public KeyData[] Keys;
}

/// <summary>
/// A complete keyboard layout with multiple rows
/// </summary>
public struct KeyboardLayout
{
	public string Name;
	public KeyboardRow[] Rows;
}

/// <summary>
/// Cached rectangle and data for a key used during rendering and hit detection
/// </summary>
public struct KeyRect
{
	public Rect2 Rect;
	public int LayoutIndex;
	public int RowIndex;
	public int KeyIndex;
	public KeyData Data;

	public readonly bool IsSpecial => Data.Type != KeyType.Char;
}

/// <summary>
/// Maps key output strings to Godot Key enum values
/// </summary>
public static class KeyCodeMap
{
	private static readonly System.Collections.Generic.Dictionary<string, Key> _keyMap = new()
	{
		// Special keys
		["Escape"] = Key.Escape,
		["Tab"] = Key.Tab,
		["Backspace"] = Key.Backspace,
		["Return"] = Key.Enter,
		["Enter"] = Key.KpEnter,
		["Insert"] = Key.Insert,
		["Delete"] = Key.Delete,
		["Home"] = Key.Home,
		["End"] = Key.End,
		["LeftArrow"] = Key.Left,
		["UpArrow"] = Key.Up,
		["RightArrow"] = Key.Right,
		["DownArrow"] = Key.Down,
		["Pageup"] = Key.Pageup,
		["Pagedown"] = Key.Pagedown,
		["Space"] = Key.Space,

		// Numbers
		["0"] = Key.Key0,
		["1"] = Key.Key1,
		["2"] = Key.Key2,
		["3"] = Key.Key3,
		["4"] = Key.Key4,
		["5"] = Key.Key5,
		["6"] = Key.Key6,
		["7"] = Key.Key7,
		["8"] = Key.Key8,
		["9"] = Key.Key9,

		// Letters (uppercase - lowercase handled via +32 offset)
		["A"] = Key.A,
		["B"] = Key.B,
		["C"] = Key.C,
		["D"] = Key.D,
		["E"] = Key.E,
		["F"] = Key.F,
		["G"] = Key.G,
		["H"] = Key.H,
		["I"] = Key.I,
		["J"] = Key.J,
		["K"] = Key.K,
		["L"] = Key.L,
		["M"] = Key.M,
		["N"] = Key.N,
		["O"] = Key.O,
		["P"] = Key.P,
		["Q"] = Key.Q,
		["R"] = Key.R,
		["S"] = Key.S,
		["T"] = Key.T,
		["U"] = Key.U,
		["V"] = Key.V,
		["W"] = Key.W,
		["X"] = Key.X,
		["Y"] = Key.Y,
		["Z"] = Key.Z,

		// Punctuation and symbols
		["!"] = Key.Exclam,
		["\""] = Key.Quotedbl,
		["#"] = Key.Numbersign,
		["$"] = Key.Dollar,
		["%"] = Key.Percent,
		["&"] = Key.Ampersand,
		["'"] = Key.Apostrophe,
		["("] = Key.Parenleft,
		[")"] = Key.Parenright,
		["*"] = Key.Asterisk,
		["+"] = Key.Plus,
		[","] = Key.Comma,
		["-"] = Key.Minus,
		["."] = Key.Period,
		["/"] = Key.Slash,
		[":"] = Key.Colon,
		[";"] = Key.Semicolon,
		["<"] = Key.Less,
		["="] = Key.Equal,
		[">"] = Key.Greater,
		["?"] = Key.Question,
		["@"] = Key.At,
		["["] = Key.Bracketleft,
		["\\"] = Key.Backslash,
		["]"] = Key.Bracketright,
		["^"] = Key.Asciicircum,
		["_"] = Key.Underscore,
		["{"] = Key.Braceleft,
		["|"] = Key.Bar,
		["}"] = Key.Braceright,
		["~"] = Key.Asciitilde,
	};

	private static readonly System.Collections.Generic.HashSet<string> _lowercaseKeys = new()
	{
		"A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
		"N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
	};

	public static Key GetKeyCode(string output)
	{
		if (_keyMap.TryGetValue(output, out var key))
		{
			return key;
		}

		return Key.Unknown;
	}

	public static bool HasLowercase(string output)
	{
		return _lowercaseKeys.Contains(output);
	}

	/// <summary>
	/// Get the key code adjusted for uppercase/lowercase
	/// For letters, lowercase = keycode + 32
	/// </summary>
	public static long GetKeyCodeWithCase(string output, bool uppercase)
	{
		var key = GetKeyCode(output);
		if (!uppercase && HasLowercase(output))
		{
			return (long)key + 32;
		}

		return (long)key;
	}
}
