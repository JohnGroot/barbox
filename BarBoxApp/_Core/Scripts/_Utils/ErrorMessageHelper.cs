using System;
using System.Text.Json;
using Godot;

namespace BarBox.Core.Utils;

/// <summary>
/// Utility class for formatting error messages to be user-friendly and legible within UI constraints.
/// </summary>
public static class ErrorMessageHelper
{
	private const int MAX_ERROR_LENGTH = 150;

	/// <summary>
	/// Extracts a user-friendly error message from a raw error string, attempting to parse
	/// backend JSON error structures and map common HTTP error patterns.
	/// </summary>
	public static string GetUserFriendlyError(string rawError)
	{
		if (string.IsNullOrWhiteSpace(rawError))
		{
			return "An unknown error occurred. Please try again.";
		}

		// Strip common technical prefixes first
		var cleanedError = StripTechnicalPrefixes(rawError);

		// Try to extract from "POST failed with code X: {json}" format
		if (cleanedError.Contains("POST failed with code"))
		{
			var statusCode = ExtractStatusCode(cleanedError);
			var jsonPart = ExtractJsonPart(cleanedError);

			if (!string.IsNullOrEmpty(jsonPart))
			{
				var parsedMessage = TryParseBackendError(jsonPart);
				if (!string.IsNullOrEmpty(parsedMessage))
				{
					return TruncateMessage(parsedMessage);
				}
			}

			// Map by status code if JSON parsing failed
			if (statusCode > 0)
			{
				return MapHttpStatusCode(statusCode);
			}
		}

		// Try to extract JSON from anywhere in the string
		var extractedJson = ExtractJsonPart(cleanedError);
		if (!string.IsNullOrEmpty(extractedJson))
		{
			var parsedMessage = TryParseBackendError(extractedJson);
			if (!string.IsNullOrEmpty(parsedMessage))
			{
				return TruncateMessage(parsedMessage);
			}
		}

		// Try to parse entire cleaned string as JSON
		var directParse = TryParseBackendError(cleanedError);
		if (!string.IsNullOrEmpty(directParse))
		{
			return TruncateMessage(directParse);
		}

		// Map common error patterns
		var mapped = MapCommonErrorPatterns(cleanedError);
		if (!string.IsNullOrEmpty(mapped))
		{
			return TruncateMessage(mapped);
		}

		// Fallback: clean up and truncate
		return TruncateMessage(CleanRawError(cleanedError));
	}

	/// <summary>
	/// Strips common technical prefixes from error messages.
	/// </summary>
	private static string StripTechnicalPrefixes(string error)
	{
		var prefixes = new[]
		{
			"Authentication failed:",
			"Login failed:",
			"Validation failed:",
			"Account creation error:",
			"Login error:",
			"Create account error:",
			"Error:",
		};

		foreach (var prefix in prefixes)
		{
			if (error.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				error = error.Substring(prefix.Length).Trim();
			}
		}

		return error;
	}

	/// <summary>
	/// Tries to parse a backend ErrorResponse JSON structure and extract the user-facing message.
	/// Handles nested structures like: {"detail": {"code":"ERROR_CODE","message":"User message"}}
	/// Also handles: {"code":"ERROR_CODE","message":"User message","details":{...},"request_id":"..."}
	/// </summary>
	private static string TryParseBackendError(string json)
	{
		try
		{
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;

			// First, check for nested "detail" object (common backend pattern)
			if (root.TryGetProperty("detail", out var detailElement))
			{
				// If detail is an object, recursively parse it
				if (detailElement.ValueKind == JsonValueKind.Object)
				{
					var detailJson = detailElement.GetRawText();
					var detailMessage = TryParseBackendError(detailJson);
					if (!string.IsNullOrWhiteSpace(detailMessage))
					{
						return detailMessage;
					}
				}

				// If detail is a string, return it directly
				else if (detailElement.ValueKind == JsonValueKind.String)
				{
					var detailString = detailElement.GetString();
					if (!string.IsNullOrWhiteSpace(detailString))
					{
						return detailString;
					}
				}
			}

			// Try to get the message field
			if (root.TryGetProperty("message", out var messageElement))
			{
				var message = messageElement.GetString();
				if (!string.IsNullOrWhiteSpace(message))
				{
					return message;
				}
			}

			// Try to get code if message is missing
			if (root.TryGetProperty("code", out var codeElement))
			{
				var code = codeElement.GetString();
				return MapErrorCode(code);
			}
		}
		catch (JsonException)
		{
			// Not valid JSON, continue to other parsing methods
		}

		return null;
	}

	private static int ExtractStatusCode(string error)
	{
		try
		{
			var startIndex = error.IndexOf("code ") + 5;
			var endIndex = error.IndexOf(":", startIndex);
			if (startIndex > 4 && endIndex > startIndex)
			{
				var codeStr = error.Substring(startIndex, endIndex - startIndex).Trim();
				if (int.TryParse(codeStr, out var code))
				{
					return code;
				}
			}
		}
		catch
		{
			// Parsing failed, return 0
		}

		return 0;
	}

	private static string ExtractJsonPart(string error)
	{
		var jsonStart = error.IndexOf('{');
		if (jsonStart >= 0)
		{
			return error.Substring(jsonStart);
		}

		return null;
	}

	private static string MapHttpStatusCode(int statusCode)
	{
		return statusCode switch
		{
			400 => "Invalid request. Please check your input and try again.",
			401 => "Invalid phone number or PIN.",
			403 => "Access denied. Please contact support.",
			404 => "Service not found. Please contact support.",
			409 => "An account with this information already exists.",
			429 => "Too many attempts. Please try again later.",
			500 => "Server error. Please try again later.",
			502 => "Service temporarily unavailable. Please try again.",
			503 => "Service temporarily unavailable. Please try again.",
			504 => "Connection timeout. Please check your network and try again.",
			_ => "An error occurred. Please try again.",
		};
	}

	private static string MapErrorCode(string code)
	{
		return code?.ToUpperInvariant() switch
		{
			"AUTH_FAILED" => "Invalid phone number or PIN.",
			"VALIDATION_ERROR" => "Please check your input and try again.",
			"PHONE_EXISTS" => "An account with this phone number already exists.",
			"USERNAME_TAKEN" => "This username is already taken.",
			"INTERNAL_ERROR" => "Server error. Please try again later.",
			"NETWORK_ERROR" => "Network error. Please check your connection.",
			"TIMEOUT" => "Connection timeout. Please try again.",
			"INVALID_PHONE" => "Please enter a valid 10-digit phone number.",
			"INVALID_PIN" => "Please enter a valid 4-digit PIN.",
			"INVALID_USERNAME" => "Username must be 1-7 characters, alphanumeric and underscore only.",
			_ => "An error occurred. Please try again.",
		};
	}

	private static string MapCommonErrorPatterns(string error)
	{
		var lowerError = error.ToLowerInvariant();

		if (lowerError.Contains("timeout"))
		{
			return "Connection timeout. Please check your network and try again.";
		}

		if (lowerError.Contains("already exists") || lowerError.Contains("conflict") || lowerError.Contains("409"))
		{
			return "An account with this information already exists.";
		}

		if (lowerError.Contains("invalid") && lowerError.Contains("phone"))
		{
			return "Please enter a valid 10-digit phone number.";
		}

		if (lowerError.Contains("invalid") && lowerError.Contains("pin"))
		{
			return "Please enter a valid 4-digit PIN.";
		}

		if (lowerError.Contains("username") && lowerError.Contains("taken"))
		{
			return "This username is already taken.";
		}

		if (lowerError.Contains("network") || lowerError.Contains("connection"))
		{
			return "Network error. Please check your connection and try again.";
		}

		if (lowerError.Contains("server error") || lowerError.Contains("500"))
		{
			return "Server error. Please try again later.";
		}

		if (lowerError.Contains("unavailable") || lowerError.Contains("503"))
		{
			return "Service temporarily unavailable. Please try again later.";
		}

		return null;
	}

	private static string CleanRawError(string error)
	{
		// Remove common prefixes
		error = error.Replace("POST failed with code", "Error")
					 .Replace("Account creation error:", string.Empty)
					 .Replace("Login error:", string.Empty)
					 .Replace("Validation failed:", string.Empty)
					 .Trim();

		// Remove request IDs and technical details
		var requestIdIndex = error.IndexOf("request_id");
		if (requestIdIndex > 0)
		{
			error = error.Substring(0, requestIdIndex).Trim().TrimEnd(',', '{', '}');
		}

		return error;
	}

	private static string TruncateMessage(string message)
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			return "An error occurred. Please try again.";
		}

		message = message.Trim();

		if (message.Length <= MAX_ERROR_LENGTH)
		{
			return message;
		}

		// Find last complete word before max length
		var truncateAt = MAX_ERROR_LENGTH - 3;
		var lastSpace = message.LastIndexOf(' ', truncateAt);

		if (lastSpace > MAX_ERROR_LENGTH / 2)
		{
			return message.Substring(0, lastSpace) + "...";
		}

		return message.Substring(0, truncateAt) + "...";
	}

	/// <summary>
	/// Extracts user-friendly messages from validation errors.
	/// </summary>
	public static string FormatValidationError(string field, string message, string value = null)
	{
		return field switch
		{
			"origin_id" => "System not properly registered. Please contact support.",
			"id" or "phone" => "This phone number is already registered. Please login instead.",
			"tag" or "username" => string.IsNullOrEmpty(value)
				? "This username is already taken. Please choose another."
				: $"Username '{value}' is already taken. Please choose another.",
			"pin" => "Please enter a valid 4-digit PIN.",
			_ => !string.IsNullOrWhiteSpace(message) ? TruncateMessage(message) : "Please check your input and try again.",
		};
	}
}
