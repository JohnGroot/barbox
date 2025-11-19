using Godot;
using System.Collections.Generic;

namespace BarBox.Core.Autoloads;

public partial class InputManager : AutoloadBase
{
	[Signal] public delegate void TouchStartedEventHandler(Vector2 position, int fingerId);
	[Signal] public delegate void TouchMovedEventHandler(Vector2 position, int fingerId);
	[Signal] public delegate void TouchEndedEventHandler(Vector2 position, int fingerId);
	[Signal] public delegate void ClickStartedEventHandler(Vector2 position);
	[Signal] public delegate void ClickEndedEventHandler(Vector2 position);

	private Dictionary<int, Vector2> _activeTouches = new();
	private bool _mousePressed = false;
	private Vector2 _mousePosition = Vector2.Zero;

	protected override void OnServiceReady()
	{
		SetProcessInput(true);
	}

	public static InputManager GetAutoload()
	{
		return AutoloadBase.GetAutoload<InputManager>();
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventScreenTouch touchEvent)
		{
			HandleTouchInput(touchEvent);
		}
		else if (@event is InputEventScreenDrag dragEvent)
		{
			HandleDragInput(dragEvent);
		}
		else if (@event is InputEventMouseButton mouseButtonEvent)
		{
			HandleMouseButton(mouseButtonEvent);
		}
		else if (@event is InputEventMouseMotion mouseMotionEvent)
		{
			HandleMouseMotion(mouseMotionEvent);
		}
	}

	private void HandleTouchInput(InputEventScreenTouch touchEvent)
	{
		int fingerId = touchEvent.Index;
		Vector2 position = touchEvent.Position;

		if (touchEvent.Pressed)
		{
			_activeTouches[fingerId] = position;
			EmitSignal(SignalName.TouchStarted, position, fingerId);
		}
		else
		{
			if (_activeTouches.ContainsKey(fingerId))
			{
				_activeTouches.Remove(fingerId);
				EmitSignal(SignalName.TouchEnded, position, fingerId);
			}
		}
	}

	private void HandleDragInput(InputEventScreenDrag dragEvent)
	{
		int fingerId = dragEvent.Index;
		Vector2 position = dragEvent.Position;

		if (_activeTouches.ContainsKey(fingerId))
		{
			_activeTouches[fingerId] = position;
			EmitSignal(SignalName.TouchMoved, position, fingerId);
		}
	}

	private void HandleMouseButton(InputEventMouseButton mouseButtonEvent)
	{
		if (mouseButtonEvent.ButtonIndex == MouseButton.Left)
		{
			_mousePosition = mouseButtonEvent.Position;
			
			if (mouseButtonEvent.Pressed)
			{
				_mousePressed = true;
				EmitSignal(SignalName.ClickStarted, _mousePosition);
			}
			else
			{
				_mousePressed = false;
				EmitSignal(SignalName.ClickEnded, _mousePosition);
			}
		}
	}

	private void HandleMouseMotion(InputEventMouseMotion mouseMotionEvent)
	{
		_mousePosition = mouseMotionEvent.Position;
		
		if (_mousePressed)
		{
			// Treat mouse drag as touch move for compatibility
			EmitSignal(SignalName.TouchMoved, _mousePosition, 0);
		}
	}

	public bool IsTouchActive(int fingerId = -1)
	{
		if (fingerId == -1)
			return _activeTouches.Count > 0 || _mousePressed;
		
		return _activeTouches.ContainsKey(fingerId);
	}

	public Vector2 GetTouchPosition(int fingerId = 0)
	{
		return _activeTouches.GetValueOrDefault(fingerId, _mousePosition);
	}

	public Vector2[] GetAllTouchPositions()
	{
		var positions = new List<Vector2>();
		
		foreach (var touch in _activeTouches.Values)
		{
			positions.Add(touch);
		}
		
		if (_mousePressed)
		{
			positions.Add(_mousePosition);
		}
		
		return positions.ToArray();
	}

	public int GetActiveTouchCount()
	{
		int count = _activeTouches.Count;
		if (_mousePressed) count++;
		return count;
	}
}