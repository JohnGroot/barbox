using Godot;

[GlobalClass]
public abstract partial class BasePlayer : Node2D
{
	[Signal]
	public delegate void PlayerReadyEventHandler();

	[Signal]
	public delegate void PlayerDestroyedEventHandler();

	[ExportCategory("Player Identity")]
	[Export]
	public string PlayerId { get; set; } = "player1";

	[Export]
	public string PlayerName { get; set; } = "Player";

	protected UserSession _userSession;
	protected bool _isActive = true;

	public override void _Ready()
	{
		InitializePlayer();
		EmitSignal(SignalName.PlayerReady);
	}

	protected virtual void InitializePlayer()
	{
	}

	public virtual void SetUserSession(UserSession userSession)
	{
		_userSession = userSession;
		PlayerName = _userSession?.UserName ?? PlayerName;
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
