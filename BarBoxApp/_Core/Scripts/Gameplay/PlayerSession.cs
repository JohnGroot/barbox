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
		PlayerName = userSession?.GlobalData?.UserName ?? playerId;
		SessionStartTime = (float)Time.GetUnixTimeFromSystem();
	}

	public void SetUserSession(UserSession userSession)
	{
		UserSession = userSession;
		if (userSession?.GlobalData != null)
		{
			PlayerName = userSession.GlobalData.UserName;
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
		return UserSession?.GlobalData?.GlobalCredits ?? 0;
	}

	public override string ToString()
	{
		return $"PlayerSession[{PlayerId}]: {PlayerName} (Active: {IsActive})";
	}
}