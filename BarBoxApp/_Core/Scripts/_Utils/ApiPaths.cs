using System;

namespace BarBox.Core.Utils;

/// <summary>
/// Backend endpoint paths, grouped to mirror BarBoxServices' router prefixes
/// (`APIRouter(prefix=...)`). Keeping these in one place makes an app/server
/// path drift (renamed route, missing prefix) a compile-time grep hit instead
/// of a runtime 404.
/// </summary>
public static class ApiPaths
{
	public static class Player
	{
		public const string Login = "/player/auth/login";
		public const string Logout = "/player/auth/logout";
		public const string Validate = "/player/validate";
		public const string Create = "/player/";

		public static string UsernameAvailable(string username) =>
			$"/player/username/{Uri.EscapeDataString(username)}/available";
	}

	public static class Box
	{
		public static string Session(Guid boxId, Guid sessionId, string gameTag) =>
			$"/box/{boxId}/session/{sessionId}?game_tag={Uri.EscapeDataString(gameTag)}";

		public static string CloseSession(Guid sessionId) =>
			$"/box/session/{sessionId}/close";

		public static string LobbySession(Guid boxId) =>
			$"/box/{boxId}/lobby/session";
	}

	public static class Racing
	{
		public const string Leaderboard = "/game/racing/leaderboard";
	}

	public static class Mining
	{
		public static string RegisterLocation(string venueName) =>
			$"/game/mining/location/register?venue_name={Uri.EscapeDataString(venueName)}";

		public static string PlayerState(Guid playerId) =>
			$"/game/mining/player/{playerId}/state";
	}

	public static class Nines
	{
		public static string Jackpot(string venueName) =>
			$"/game/nines/jackpot/{venueName}";
	}
}
