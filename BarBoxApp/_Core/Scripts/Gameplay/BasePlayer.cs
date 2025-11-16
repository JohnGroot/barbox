using Godot;

[GlobalClass]
public abstract partial class BasePlayer : Node2D
{
	[Signal] public delegate void PlayerReadyEventHandler();
	[Signal] public delegate void PlayerDestroyedEventHandler();

	[ExportCategory("Player Identity")]
	[Export] public string PlayerId { get; set; } = "player1";
	[Export] public string PlayerName { get; set; } = "Player";
	
	protected UserSession _userSession;
	protected bool _isActive = true;

	public override void _Ready()
	{
		InitializePlayer();
		EmitSignal(SignalName.PlayerReady);
	}

	/// <summary>
	/// Called during _Ready BEFORE PlayerReady signal emitted
	/// </summary>
	protected virtual void InitializePlayer() { }

	/// <summary>
	/// Called AFTER _Ready and InitializePlayer
	/// </summary>
	/// <param name="userSession"></param>
	public virtual void SetUserSession(UserSession userSession)
	{
		_userSession = userSession;
		if (_userSession != null)
		{
			PlayerName = _userSession.UserName;
		}
	}

	public virtual void DestroyPlayer()
	{
		_isActive = false;
		EmitSignal(SignalName.PlayerDestroyed);
	}

	public UserSession GetUserSession() => _userSession;
	public bool IsActive() => _isActive;
	public void SetActive(bool active) => _isActive = active;
}