using System;

/// <summary>
/// Result wrapper for explicit error handling
/// Extracted from DataStore for use across services
/// </summary>
public readonly struct Result<T>
{
	public T Value { get; }
	public string Error { get; }
	public bool IsSuccess { get; }

	private Result(T value)
	{
		Value = value;
		Error = string.Empty;
		IsSuccess = true;
	}

	private Result(string error)
	{
		Value = default(T);
		Error = error ?? string.Empty;
		IsSuccess = false;
	}

	public static Result<T> Success(T value) => new Result<T>(value);
	public static Result<T> Failure(string error) => new Result<T>(error);
}
