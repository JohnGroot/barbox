namespace BarBox
{
	/// <summary>
	/// Centralized backend configuration constants.
	/// Use these instead of hardcoding connection details.
	/// </summary>
	public static class BackendConstants
	{
		/// <summary>
		/// Backend host IP address.
		/// Use 127.0.0.1 instead of "localhost" to avoid DNS resolution.
		/// </summary>
		public const string HOST = "127.0.0.1";

		/// <summary>
		/// Backend server port.
		/// </summary>
		public const int PORT = 8000;

		/// <summary>
		/// Complete backend base URL.
		/// </summary>
		public const string BASE_URL = "http://127.0.0.1:8000";

		// Common HTTP Headers

		/// <summary>
		/// Standard User-Agent header for BarBox client requests.
		/// </summary>
		public const string USER_AGENT = "User-Agent: BarBox-Client/1.0";

		/// <summary>
		/// Accept JSON content type header.
		/// </summary>
		public const string ACCEPT_JSON = "Accept: application/json";

		/// <summary>
		/// JSON content type header.
		/// </summary>
		public const string CONTENT_TYPE_JSON = "Content-Type: application/json";

		/// <summary>
		/// Common headers for JSON API requests.
		/// </summary>
		public static readonly string[] JSON_HEADERS = new[]
		{
			CONTENT_TYPE_JSON,
			ACCEPT_JSON,
			USER_AGENT
		};
	}
}
