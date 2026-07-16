using System;
using System.Text.RegularExpressions;

/// <summary>
/// Utility class for validating and formatting user input fields
/// Provides real-time validation for phone numbers, PINs, and usernames
/// </summary>
public static class InputValidator
{
	// Validation constants
	public const int PHONE_NUMBER_LENGTH = 10;
	public const int PIN_LENGTH = 4;
	public const int USERNAME_MAX_LENGTH = 7;

	// Regex patterns
	private static readonly Regex PhoneNumberRegex = new Regex(@"^\d{10}$");
	private static readonly Regex PinRegex = new Regex(@"^\d{4}$");
	private static readonly Regex UsernameRegex = new Regex(@"^[a-zA-Z0-9_]+$");

	#region Phone Number Validation

	/// <summary>
	/// Clean and format phone number input.
	/// Supports both US format (XXXXXXXXXX) and E.164 format (+1XXXXXXXXXX).
	/// E.164 format is preserved for backend communication.
	/// </summary>
	public static string CleanPhoneNumber(string input)
	{
		if (string.IsNullOrEmpty(input))
		{
			return string.Empty;
		}

		// Check if input is E.164 format (starts with +)
		if (input.StartsWith("+"))
		{
			// E.164 format: preserve + and all digits
			// Remove non-digit characters except leading +
			var e164 = "+" + Regex.Replace(input.Substring(1), @"[^\d]", string.Empty);
			return e164;
		}

		// US format: remove all non-digit characters and limit to 10 digits
		var digitsOnly = Regex.Replace(input, @"[^\d]", string.Empty);

		// Limit to 10 digits
		if (digitsOnly.Length > PHONE_NUMBER_LENGTH)
		{
			digitsOnly = digitsOnly.Substring(0, PHONE_NUMBER_LENGTH);
		}

		return digitsOnly;
	}

	/// <summary>
	/// Format phone number for display: (XXX) XXX-XXXX
	/// </summary>
	public static string FormatPhoneNumberDisplay(string phoneNumber)
	{
		var cleaned = CleanPhoneNumber(phoneNumber);

		if (cleaned.Length == 0)
		{
			return string.Empty;
		}

		if (cleaned.Length <= 3)
		{
			return $"({cleaned}";
		}

		if (cleaned.Length <= 6)
		{
			return $"({cleaned.Substring(0, 3)}) {cleaned.Substring(3)}";
		}

		return $"({cleaned.Substring(0, 3)}) {cleaned.Substring(3, 3)}-{cleaned.Substring(6)}";
	}

	/// <summary>
	/// Validate phone number.
	/// Accepts US format (10 digits) or E.164 format (+1 followed by 10 digits).
	/// </summary>
	public static bool IsValidPhoneNumber(string phoneNumber)
	{
		var cleaned = CleanPhoneNumber(phoneNumber);

		// Check E.164 format: +1 followed by 10 digits
		if (cleaned.StartsWith("+"))
		{
			return cleaned.Length == 12 && cleaned.StartsWith("+1") &&
				   Regex.IsMatch(cleaned.Substring(2), @"^\d{10}$");
		}

		// Check US format: exactly 10 digits
		return PhoneNumberRegex.IsMatch(cleaned);
	}

	#endregion

	#region PIN Validation

	/// <summary>
	/// Clean PIN input (digits only, limit to 4 characters)
	/// </summary>
	public static string CleanPin(string input)
	{
		if (string.IsNullOrEmpty(input))
		{
			return string.Empty;
		}

		// Remove all non-digit characters
		var digitsOnly = Regex.Replace(input, @"[^\d]", string.Empty);

		// Limit to 4 digits
		if (digitsOnly.Length > PIN_LENGTH)
		{
			digitsOnly = digitsOnly.Substring(0, PIN_LENGTH);
		}

		return digitsOnly;
	}

	/// <summary>
	/// Validate PIN (must be exactly 4 digits)
	/// </summary>
	public static bool IsValidPin(string pin)
	{
		var cleaned = CleanPin(pin);
		return PinRegex.IsMatch(cleaned);
	}

	#endregion

	#region Username Validation

	/// <summary>
	/// Clean username input (alphanumeric + underscore, limit to 7 characters)
	/// </summary>
	public static string CleanUsername(string input)
	{
		if (string.IsNullOrEmpty(input))
		{
			return string.Empty;
		}

		// Remove invalid characters (keep only alphanumeric and underscore)
		var cleaned = Regex.Replace(input, @"[^a-zA-Z0-9_]", string.Empty);

		// Limit to 7 characters
		if (cleaned.Length > USERNAME_MAX_LENGTH)
		{
			cleaned = cleaned.Substring(0, USERNAME_MAX_LENGTH);
		}

		return cleaned;
	}

	/// <summary>
	/// Validate username (1-7 characters, alphanumeric + underscore)
	/// </summary>
	public static bool IsValidUsername(string username)
	{
		if (string.IsNullOrEmpty(username))
		{
			return false;
		}

		var cleaned = CleanUsername(username);
		return cleaned.Length >= 1 && cleaned.Length <= USERNAME_MAX_LENGTH && UsernameRegex.IsMatch(cleaned);
	}

	#endregion

	#region Validation State

	/// <summary>
	/// Validation state for UI feedback
	/// </summary>
	public enum ValidationState
	{
		None,      // No validation performed yet
		Valid,     // Input is valid
		Invalid,   // Input is invalid
		Incomplete, // Input is not complete but on the right track
	}

	/// <summary>
	/// Get validation state for phone number
	/// </summary>
	public static ValidationState GetPhoneNumberValidationState(string input)
	{
		var cleaned = CleanPhoneNumber(input);

		if (string.IsNullOrEmpty(cleaned))
		{
			return ValidationState.None;
		}

		if (cleaned.Length == PHONE_NUMBER_LENGTH)
		{
			return ValidationState.Valid;
		}

		if (cleaned.Length < PHONE_NUMBER_LENGTH)
		{
			return ValidationState.Incomplete;
		}

		return ValidationState.Invalid;
	}

	/// <summary>
	/// Get validation state for PIN
	/// </summary>
	public static ValidationState GetPinValidationState(string input)
	{
		var cleaned = CleanPin(input);

		if (string.IsNullOrEmpty(cleaned))
		{
			return ValidationState.None;
		}

		if (cleaned.Length == PIN_LENGTH)
		{
			return ValidationState.Valid;
		}

		if (cleaned.Length < PIN_LENGTH)
		{
			return ValidationState.Incomplete;
		}

		return ValidationState.Invalid;
	}

	/// <summary>
	/// Get validation state for username
	/// </summary>
	public static ValidationState GetUsernameValidationState(string input)
	{
		if (string.IsNullOrEmpty(input))
		{
			return ValidationState.None;
		}

		var cleaned = CleanUsername(input);

		if (string.IsNullOrEmpty(cleaned))
		{
			return ValidationState.Invalid;
		}

		if (cleaned.Length >= 1 && cleaned.Length <= USERNAME_MAX_LENGTH)
		{
			return ValidationState.Valid;
		}

		return ValidationState.Invalid;
	}

	#endregion
}
