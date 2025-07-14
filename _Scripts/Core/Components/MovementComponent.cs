using Godot;

[GlobalClass]
public partial class MovementComponent : Node
{
	[ExportCategory("Movement Settings")]
	[Export] public float MovementSpeed { get; set; } = 200.0f;
	[Export] public bool FollowTarget { get; set; } = true;
	[Export] public float DragSensitivity { get; set; } = 1.0f;

	private BasePlayer _player;
	private InputComponent _inputComponent;
	private Vector2 _targetPosition;
	private bool _isMoving = false;

	public override void _Ready()
	{
		_player = GetParent<BasePlayer>();
		_inputComponent = _player.GetNode<InputComponent>("InputComponent");
		_targetPosition = _player.GlobalPosition;

		if (_inputComponent != null)
		{
			_inputComponent.InputStarted += OnInputStarted;
			_inputComponent.InputMoved += OnInputMoved;
			_inputComponent.InputEnded += OnInputEnded;
		}
	}

	public override void _Notification(int what)
	{
		if (what == NotificationExitTree)
		{
			if (GodotObject.IsInstanceValid(_inputComponent))
			{
				_inputComponent.InputStarted -= OnInputStarted;
				_inputComponent.InputMoved -= OnInputMoved;
				_inputComponent.InputEnded -= OnInputEnded;
			}
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_player.IsActive()) return;

		if (FollowTarget && _isMoving)
		{
			MoveTowardsTarget((float)delta);
		}
	}

	private void OnInputStarted(Vector2 position)
	{
		_targetPosition = position;
		_isMoving = true;
	}

	private void OnInputMoved(Vector2 position)
	{
		_targetPosition = position;
	}

	private void OnInputEnded(Vector2 position)
	{
		_isMoving = false;
	}

	private void MoveTowardsTarget(float delta)
	{
		Vector2 direction = (_targetPosition - _player.GlobalPosition).Normalized();
		float distance = _player.GlobalPosition.DistanceTo(_targetPosition);

		if (distance > 5.0f)
		{
			Vector2 movement = direction * MovementSpeed * DragSensitivity * delta;
			_player.GlobalPosition += movement;
		}
		else
		{
			_player.GlobalPosition = _targetPosition;
		}
	}

	public void SetTargetPosition(Vector2 position)
	{
		_targetPosition = position;
		_isMoving = true;
	}

	public void StopMovement()
	{
		_isMoving = false;
	}

	public Vector2 GetTargetPosition() => _targetPosition;
	public bool IsMoving() => _isMoving;
}