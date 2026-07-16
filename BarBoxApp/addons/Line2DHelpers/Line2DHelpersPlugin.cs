#if TOOLS
using System.Collections.Generic;
using Godot;

[Tool]
public partial class Line2DHelpersPlugin : EditorPlugin
{
	private const float POINT_SELECTION_THRESHOLD = 12.0f;
	private const float POINT_DRAW_RADIUS = 25.0f;
	private const float POINT_HIGHLIGHT_RADIUS = 23.0f;
	private const float PATH_LINE_WIDTH = 4.0f;

	// Cached colors to avoid per-frame allocations
	private static readonly Color PATH_COLOR = new Color(0, 1, 1, 0.6f);
	private static readonly Color POINT_COLOR = new Color(1, 1, 0, 1.0f);

	private static Line2DHelpersPlugin _instance;

	public static Line2DHelpersPlugin Instance => _instance;

	private Line2DInspectorPlugin _inspectorPlugin;
	private SubViewport _editor2DViewport;

	// Point picking state
	private Line2D _pickerTargetLine;
	private SpinBox _pickerTargetSpinBox;
	private Line2DSmoothingPanel _pickerSourcePanel;
	private bool _isPickingPoint;
	private int _currentStartIndex = -1;
	private int _currentEndIndex = -1;

	// Public accessors for toggle button state
	public bool IsPickingPoint => _isPickingPoint;

	public SpinBox ActivePickerSpinBox => _pickerTargetSpinBox;

	public override void _EnterTree()
	{
		_instance = this;
		_inspectorPlugin = new Line2DInspectorPlugin();
		AddInspectorPlugin(_inspectorPlugin);

		_editor2DViewport = EditorInterface.Singleton.GetEditorViewport2D();

		// Enable force draw over forwarding for overlay rendering
		SetForceDrawOverForwardingEnabled();

		GD.Print("[Line2D Helpers] Plugin initialized");
	}

	public override void _ExitTree()
	{
		CancelPointPicking();
		RemoveInspectorPlugin(_inspectorPlugin);
		_inspectorPlugin = null;
		_instance = null;

		GD.Print("[Line2D Helpers] Plugin cleanup completed");
	}

	public override string _GetPluginName()
	{
		return "Line2D Helpers";
	}

	public override bool _Handles(GodotObject @object)
	{
		// REQUIRED: Tell editor we handle Line2D nodes
		return @object is Line2D;
	}

	public override void _Edit(GodotObject @object)
	{
		// Called when a Line2D is selected
		// Don't overwrite _pickerTargetLine here - set by StartPointPicking
	}

	public override void _MakeVisible(bool visible)
	{
		// Clean up when plugin becomes invisible
		if (!visible)
		{
			CancelPointPicking();
		}
	}

	public void StartPointPicking(Line2D line2D, SpinBox targetSpinBox, int startIdx, int endIdx, Line2DSmoothingPanel sourcePanel = null)
	{
		_pickerTargetLine = line2D;
		_pickerTargetSpinBox = targetSpinBox;
		_pickerSourcePanel = sourcePanel;
		_currentStartIndex = startIdx;
		_currentEndIndex = endIdx;
		_isPickingPoint = true;

		// Ensure Line2D stays selected so draw callback continues firing
		EditorInterface.Singleton.EditNode(line2D);

		UpdateOverlays();
	}

	public void CancelPointPicking()
	{
		var panel = _pickerSourcePanel;

		_isPickingPoint = false;
		_pickerTargetLine = null;
		_pickerTargetSpinBox = null;
		_pickerSourcePanel = null;
		_currentStartIndex = -1;
		_currentEndIndex = -1;
		UpdateOverlays();

		// Notify panel that picking ended so it can update toggle button states
		panel?.OnPickingEnded();
	}

	public override bool _ForwardCanvasGuiInput(InputEvent @event)
	{
		if (!_isPickingPoint || _pickerTargetLine == null)
		{
			return false;
		}

		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Left && mouseButton.Pressed)
			{
				int pointIndex = GetClosestPointIndex(mouseButton.Position);
				if (pointIndex >= 0 && _pickerTargetSpinBox != null)
				{
					_pickerTargetSpinBox.Value = pointIndex;
				}

				CancelPointPicking();
				return true;
			}

			if (mouseButton.ButtonIndex == MouseButton.Right && mouseButton.Pressed)
			{
				CancelPointPicking();
				return true;
			}
		}

		if (@event is InputEventMouseMotion)
		{
			UpdateOverlays();
			return true;
		}

		return false;
	}

	public override void _ForwardCanvasForceDrawOverViewport(Control viewportControl)
	{
		if (!_isPickingPoint || _pickerTargetLine == null || _editor2DViewport == null)
		{
			return;
		}

		var editorTransform = _editor2DViewport.GlobalCanvasTransform;
		var points = _pickerTargetLine.Points;
		bool isClosed = _pickerTargetLine.Closed;

		// Draw path lines showing current smoothing range (thick, semi-transparent cyan)
		var pathIndices = GetPathIndices(_currentStartIndex, _currentEndIndex, points.Length, isClosed);
		for (int i = 0; i < pathIndices.Count - 1; i++)
		{
			var p1 = editorTransform * _pickerTargetLine.ToGlobal(points[pathIndices[i]]);
			var p2 = editorTransform * _pickerTargetLine.ToGlobal(points[pathIndices[i + 1]]);
			viewportControl.DrawLine(p1, p2, PATH_COLOR, PATH_LINE_WIDTH);
		}

		// Draw yellow circles at each point
		for (int i = 0; i < points.Length; i++)
		{
			var localPos = points[i];
			var worldPos = _pickerTargetLine.ToGlobal(localPos);
			var screenPos = editorTransform * worldPos;
			viewportControl.DrawCircle(screenPos, POINT_DRAW_RADIUS, POINT_COLOR);
		}

		// Highlight closest point to mouse
		var mousePos = viewportControl.GetLocalMousePosition();
		int closestIdx = GetClosestPointIndex(mousePos);
		if (closestIdx >= 0)
		{
			var worldPos = _pickerTargetLine.ToGlobal(points[closestIdx]);
			var screenPos = editorTransform * worldPos;
			viewportControl.DrawCircle(screenPos, POINT_HIGHLIGHT_RADIUS, Colors.White);
		}
	}

	private List<int> GetPathIndices(int start, int end, int pointCount, bool isClosed)
	{
		var indices = new List<int>();
		if (start < 0 || end < 0 || pointCount == 0)
		{
			return indices;
		}

		if (isClosed && start == end)
		{
			// Full loop: all points starting from start, wrapping back to start
			for (int i = start; i < pointCount; i++)
			{
				indices.Add(i);
			}

			for (int i = 0; i <= start; i++)
			{
				indices.Add(i);
			}
		}
		else if (isClosed && end < start)
		{
			// Wrap-around path: start→end_of_array→0→end
			for (int i = start; i < pointCount; i++)
			{
				indices.Add(i);
			}

			for (int i = 0; i <= end; i++)
			{
				indices.Add(i);
			}
		}
		else
		{
			// Normal path: start→end
			for (int i = start; i <= end; i++)
			{
				indices.Add(i);
			}
		}

		return indices;
	}

	private int GetClosestPointIndex(Vector2 screenPos)
	{
		if (_pickerTargetLine == null || _editor2DViewport == null)
		{
			return -1;
		}

		// Screen to world coordinates
		var editorTransform = _editor2DViewport.GlobalCanvasTransform;
		var worldPos = editorTransform.AffineInverse() * screenPos;

		// World to node-local coordinates
		var localPos = _pickerTargetLine.ToLocal(worldPos);

		// Find closest point with zoom-aware threshold (accounting for node scale)
		float zoomScale = editorTransform.Scale.X;
		float nodeScale = _pickerTargetLine.GlobalScale.X;
		float threshold = POINT_SELECTION_THRESHOLD / (zoomScale * nodeScale);
		float closestDistance = threshold;
		int closestIndex = -1;

		var points = _pickerTargetLine.Points;
		for (int i = 0; i < points.Length; i++)
		{
			float distance = localPos.DistanceTo(points[i]);
			if (distance < closestDistance)
			{
				closestDistance = distance;
				closestIndex = i;
			}
		}

		return closestIndex;
	}
}
#endif
