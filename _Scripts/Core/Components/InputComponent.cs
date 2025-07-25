using Godot;

[GlobalClass]
public partial class InputComponent : Node
{
	[Signal] public delegate void InputStartedEventHandler(Vector2 position);
	[Signal] public delegate void InputMovedEventHandler(Vector2 position);
	[Signal] public delegate void InputEndedEventHandler(Vector2 position);

	[ExportCategory("Input Settings")]
	[Export] public float InputRadius { get; set; } = 50.0f;
	[Export] public bool AcceptInput { get; set; } = true;

	private BasePlayer _player;
	private InputManager _inputManager;
	private bool _isDragging = false;
	private int _activeFingerId = -1;

	public override void _Ready()
	{
		_player = GetParent<BasePlayer>();
		_inputManager = InputManager.GetAutoload();

		if (_inputManager != null)
		{
			ConnectInputSignals();
		}
	}

	public override void _ExitTree()
	{
		if (GodotObject.IsInstanceValid(_inputManager))
		{
			DisconnectInputSignals();
		}
		base._ExitTree();
	}

	private void ConnectInputSignals()
	{
		_inputManager.TouchStarted += OnTouchStarted;
		_inputManager.TouchMoved += OnTouchMoved;
		_inputManager.TouchEnded += OnTouchEnded;
		_inputManager.ClickStarted += OnClickStarted;
		_inputManager.ClickEnded += OnClickEnded;
	}

	private void DisconnectInputSignals()
	{
		_inputManager.TouchStarted -= OnTouchStarted;
		_inputManager.TouchMoved -= OnTouchMoved;
		_inputManager.TouchEnded -= OnTouchEnded;
		_inputManager.ClickStarted -= OnClickStarted;
		_inputManager.ClickEnded -= OnClickEnded;
	}

	private void OnTouchStarted(Vector2 position, int fingerId)
	{
		if (!AcceptInput || !_player.IsActive()) return;

		if (!_isDragging && IsPositionInRange(position))
		{
			_isDragging = true;
			_activeFingerId = fingerId;
			EmitSignal(SignalName.InputStarted, position);
		}
	}

	private void OnTouchMoved(Vector2 position, int fingerId)
	{
		if (!AcceptInput || !_player.IsActive()) return;

		if (_isDragging && fingerId == _activeFingerId)
		{
			EmitSignal(SignalName.InputMoved, position);
		}
	}

	private void OnTouchEnded(Vector2 position, int fingerId)
	{
		if (!AcceptInput || !_player.IsActive()) return;

		if (_isDragging && fingerId == _activeFingerId)
		{
			_isDragging = false;
			_activeFingerId = -1;
			EmitSignal(SignalName.InputEnded, position);
		}
	}

	private void OnClickStarted(Vector2 position)
	{
		OnTouchStarted(position, 0);
	}

	private void OnClickEnded(Vector2 position)
	{
		OnTouchEnded(position, 0);
	}

	private bool IsPositionInRange(Vector2 position)
	{
		return _player.GlobalPosition.DistanceTo(position) <= InputRadius;
	}

	public bool IsInputActive() => _isDragging;
	public void SetInputRadius(float radius) => InputRadius = radius;
	public void SetAcceptInput(bool accept) => AcceptInput = accept;
}