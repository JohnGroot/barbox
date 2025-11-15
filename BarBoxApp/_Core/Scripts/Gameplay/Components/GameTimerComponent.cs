using Godot;

/// <summary>
/// Optional component for games with time limits
/// Extracted from GameController to keep base class minimal
/// </summary>
public partial class GameTimerComponent : Node
{
	[Signal] public delegate void TimeUpEventHandler();
	[Signal] public delegate void TimeRemainingChangedEventHandler(float timeRemaining);

	[ExportCategory("Timer Settings")]
	[Export] public float TimeLimit { get; set; } = 0.0f; // 0 = no time limit

	private Timer _timer;
	private bool _isActive = false;

	public override void _Ready()
	{
		if (TimeLimit <= 0)
			return;

		_timer = new Timer();
		_timer.WaitTime = TimeLimit;
		_timer.OneShot = true;
		_timer.Timeout += OnTimeUp;
		AddChild(_timer);
	}

	/// <summary>
	/// Start the timer
	/// </summary>
	public void StartTimer()
	{
		if (_timer == null || TimeLimit <= 0)
			return;

		_timer.Start();
		_isActive = true;
	}

	/// <summary>
	/// Stop the timer
	/// </summary>
	public void StopTimer()
	{
		if (_timer == null)
			return;

		_timer.Stop();
		_isActive = false;
	}

	/// <summary>
	/// Pause the timer
	/// </summary>
	public void PauseTimer()
	{
		if (_timer == null)
			return;

		_timer.Paused = true;
	}

	/// <summary>
	/// Resume the timer
	/// </summary>
	public void ResumeTimer()
	{
		if (_timer == null)
			return;

		_timer.Paused = false;
	}

	/// <summary>
	/// Get remaining time
	/// </summary>
	public float GetTimeRemaining()
	{
		return (float)(_timer?.TimeLeft ?? 0.0);
	}

	/// <summary>
	/// Check if timer is active
	/// </summary>
	public bool IsActive()
	{
		return _isActive && _timer != null && !_timer.IsStopped();
	}

	private void OnTimeUp()
	{
		_isActive = false;
		EmitSignal(SignalName.TimeUp);
	}

	public override void _Process(double delta)
	{
		if (_isActive && _timer != null && !_timer.IsStopped())
		{
			EmitSignal(SignalName.TimeRemainingChanged, GetTimeRemaining());
		}
	}

	public override void _ExitTree()
	{
		if (GodotObject.IsInstanceValid(_timer))
		{
			_timer.Timeout -= OnTimeUp;
		}

		base._ExitTree();
	}
}
