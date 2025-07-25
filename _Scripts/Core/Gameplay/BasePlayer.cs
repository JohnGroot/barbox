using Godot;

[GlobalClass]
public abstract partial class BasePlayer : Node2D
{
	[Signal] public delegate void PlayerReadyEventHandler();
	[Signal] public delegate void PlayerDestroyedEventHandler();

	[ExportCategory("Player Identity")]
	[Export] public string PlayerId { get; set; } = "player1";
	[Export] public string PlayerName { get; set; } = "Player";
	
	protected UserData _userData;
	protected bool _isActive = true;

	public override void _Ready()
	{
		InitializePlayer();
		EmitSignal(SignalName.PlayerReady);
	}

	protected virtual void InitializePlayer()
	{
	}

	public virtual void SetUserData(UserData userData)
	{
		_userData = userData;
		if (_userData != null)
		{
			PlayerName = _userData.UserId;
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

	public UserData GetUserData() => _userData;
	public bool IsActive() => _isActive;
	public void SetActive(bool active) => _isActive = active;
}