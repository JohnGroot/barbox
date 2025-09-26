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

	protected virtual void InitializePlayer()
	{
	}

	public virtual void SetUserSession(UserSession userSession)
	{
		_userSession = userSession;
		if (_userSession?.GlobalData != null)
		{
			PlayerName = _userSession.GlobalData.UserName;
		}
	}

	public virtual void DestroyPlayer()
	{
		_isActive = false;
		EmitSignal(SignalName.PlayerDestroyed);
	}

	public override void _ExitTree()
	{
		// Clean up any signals that might be connected
		// Subclasses should override this to disconnect their specific signals
		base._ExitTree();
	}

	public UserSession GetUserSession() => _userSession;
	public bool IsActive() => _isActive;
	public void SetActive(bool active) => _isActive = active;
}