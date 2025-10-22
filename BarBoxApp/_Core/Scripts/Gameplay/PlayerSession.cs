using Godot;

[GlobalClass]
public partial class PlayerSession : RefCounted
{
	public string PlayerId { get; private set; }
	public string PlayerName { get; set; }
	public UserSession UserSession { get; private set; }
	public bool IsActive { get; set; } = true;
	public float SessionStartTime { get; private set; }

	public PlayerSession(string playerId, UserSession userSession = null)
	{
		PlayerId = playerId;
		UserSession = userSession;
		PlayerName = userSession?.UserName ?? playerId;
		SessionStartTime = (float)Time.GetUnixTimeFromSystem();
	}

	public void SetUserSession(UserSession userSession)
	{
		UserSession = userSession;
		if (userSession != null)
		{
			PlayerName = userSession.UserName;
		}
	}

	public float GetSessionDuration()
	{
		return (float)Time.GetUnixTimeFromSystem() - SessionStartTime;
	}

	public bool HasUserSession()
	{
		return UserSession != null;
	}

	public int GetCredits()
	{
		return UserSession?.Credits ?? 0;
	}

	public override string ToString()
	{
		return $"PlayerSession[{PlayerId}]: {PlayerName} (Active: {IsActive})";
	}
}