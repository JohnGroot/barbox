using Godot;

[GlobalClass]
public partial class PlayerSession : RefCounted
{
	public string PlayerId { get; private set; }
	public string PlayerName { get; set; }
	public UserData UserData { get; private set; }
	public bool IsActive { get; set; } = true;
	public float SessionStartTime { get; private set; }

	public PlayerSession(string playerId, UserData userData = null)
	{
		PlayerId = playerId;
		UserData = userData;
		PlayerName = userData?.UserId ?? playerId;
		SessionStartTime = (float)Time.GetUnixTimeFromSystem();
	}

	public void SetUserData(UserData userData)
	{
		UserData = userData;
		if (userData != null)
		{
			PlayerName = userData.UserId;
		}
	}

	public float GetSessionDuration()
	{
		return (float)Time.GetUnixTimeFromSystem() - SessionStartTime;
	}

	public bool HasUserData()
	{
		return UserData != null;
	}

	public override string ToString()
	{
		return $"PlayerSession[{PlayerId}]: {PlayerName} (Active: {IsActive})";
	}
}