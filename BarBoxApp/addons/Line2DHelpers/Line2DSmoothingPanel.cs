#if TOOLS
using System.Collections.Generic;
using Godot;

/// <summary>
/// Inspector panel for Line2D turn smoothing using Catmull-Rom splines
/// </summary>
[Tool]
public partial class Line2DSmoothingPanel : FoldableContainer
{
	private const string HEADER_TEXT = "Line2D Turn Smoothing";
	private const float DEFAULT_POINTS_PER_10_UNITS = 1.0f;
	private const float DEFAULT_BULGE_FACTOR = 1.0f;

	// Button styling
	private static readonly Color APPLY_BUTTON_COLOR = new Color(0.3f, 0.7f, 0.4f);   // Green

	// Cached StyleBox objects (shared across all panel instances)
	private static StyleBoxFlat _applyNormalStyle;
	private static StyleBoxFlat _applyHoverStyle;
	private static StyleBoxFlat _applyPressedStyle;

	private Line2D _targetLine2D;
	private SpinBox _startIndexSpinBox;
	private SpinBox _endIndexSpinBox;
	private Button _startPickerButton;
	private Button _endPickerButton;
	private SpinBox _pointsPerUnitSpinBox;
	private SpinBox _bulgeFactorSpinBox;
	private CheckBox _evenSpacingCheckBox;
	private Button _applyButton;
	private Button _straightenButton;
	private Label _statusLabel;

	public void Initialize(Line2D line2D)
	{
		_targetLine2D = line2D;
		BuildUI();
		UpdateSpinBoxLimits();
	}

	private void BuildUI()
	{
		// Configure the FoldableContainer
		Title = HEADER_TEXT;
		Folded = false;

		// Main content container
		var mainContainer = new VBoxContainer();
		AddChild(mainContainer);

		// Point Range Section
		var rangeLabel = new Label { Text = "Point Range" };
		mainContainer.AddChild(rangeLabel);

		var rangeContainer = new HBoxContainer();
		rangeContainer.AddThemeConstantOverride("separation", 8);

		var startContainer = new HBoxContainer();
		startContainer.AddThemeConstantOverride("separation", 4);
		startContainer.AddChild(new Label { Text = "Start:" });
		_startIndexSpinBox = new SpinBox
		{
			MinValue = 0,
			Step = 1,
			CustomMinimumSize = new Vector2(60, 0),
		};
		_startIndexSpinBox.ValueChanged += OnStartIndexChanged;
		startContainer.AddChild(_startIndexSpinBox);
		_startPickerButton = new Button
		{
			Text = "⊙",
			TooltipText = "Click to toggle point picking from viewport",
			ToggleMode = true,
			CustomMinimumSize = new Vector2(28, 0),
		};
		_startPickerButton.Toggled += OnStartPickerToggled;
		startContainer.AddChild(_startPickerButton);
		rangeContainer.AddChild(startContainer);

		var endContainer = new HBoxContainer();
		endContainer.AddThemeConstantOverride("separation", 4);
		endContainer.AddChild(new Label { Text = "End:" });
		_endIndexSpinBox = new SpinBox
		{
			MinValue = 1,
			Step = 1,
			Value = 1,
			CustomMinimumSize = new Vector2(60, 0),
		};
		endContainer.AddChild(_endIndexSpinBox);
		_endPickerButton = new Button
		{
			Text = "⊙",
			TooltipText = "Click to toggle point picking from viewport",
			ToggleMode = true,
			CustomMinimumSize = new Vector2(28, 0),
		};
		_endPickerButton.Toggled += OnEndPickerToggled;
		endContainer.AddChild(_endPickerButton);
		rangeContainer.AddChild(endContainer);

		mainContainer.AddChild(rangeContainer);

		// Smoothing Settings Section
		mainContainer.AddChild(new HSeparator());
		var settingsLabel = new Label { Text = "Smoothing Settings" };
		mainContainer.AddChild(settingsLabel);

		// Points per 10 units
		var densityContainer = new HBoxContainer();
		densityContainer.AddThemeConstantOverride("separation", 4);
		densityContainer.AddChild(new Label { Text = "Points per 10 units:" });
		_pointsPerUnitSpinBox = new SpinBox
		{
			MinValue = 0.0000001,
			MaxValue = 100000.0,
			Step = 0.00000001,
			Value = DEFAULT_POINTS_PER_10_UNITS,
			CustomMinimumSize = new Vector2(60, 0),
		};
		densityContainer.AddChild(_pointsPerUnitSpinBox);
		mainContainer.AddChild(densityContainer);

		// Bulge factor
		var bulgeContainer = new HBoxContainer();
		bulgeContainer.AddThemeConstantOverride("separation", 4);
		bulgeContainer.AddChild(new Label { Text = "Bulge factor:" });
		_bulgeFactorSpinBox = new SpinBox
		{
			MinValue = 0.0,
			MaxValue = 2.0,
			Step = 0.1,
			Value = DEFAULT_BULGE_FACTOR,
			CustomMinimumSize = new Vector2(60, 0),
		};
		bulgeContainer.AddChild(_bulgeFactorSpinBox);
		mainContainer.AddChild(bulgeContainer);

		// Even spacing checkbox
		var spacingContainer = new HBoxContainer();
		spacingContainer.AddThemeConstantOverride("separation", 4);
		_evenSpacingCheckBox = new CheckBox
		{
			Text = "Even Spacing",
			TooltipText = "Redistribute points evenly along the curve for smoother results",
			ButtonPressed = false,
		};
		spacingContainer.AddChild(_evenSpacingCheckBox);
		mainContainer.AddChild(spacingContainer);

		// Status label
		_statusLabel = new Label { Text = string.Empty };
		_statusLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
		mainContainer.AddChild(_statusLabel);

		// Apply Button
		mainContainer.AddChild(new HSeparator());
		_applyButton = new Button { Text = "Apply Smooth" };
		_applyButton.Pressed += OnApplyPressed;

		// Style apply button with contrasting color (use cached StyleBox objects)
		_applyNormalStyle ??= CreateApplyStyleBox(APPLY_BUTTON_COLOR);
		_applyHoverStyle ??= CreateApplyStyleBox(APPLY_BUTTON_COLOR.Lightened(0.15f));
		_applyPressedStyle ??= CreateApplyStyleBox(APPLY_BUTTON_COLOR.Darkened(0.15f));

		_applyButton.AddThemeStyleboxOverride("normal", _applyNormalStyle);
		_applyButton.AddThemeStyleboxOverride("hover", _applyHoverStyle);
		_applyButton.AddThemeStyleboxOverride("pressed", _applyPressedStyle);

		mainContainer.AddChild(_applyButton);

		// Straighten Button
		_straightenButton = new Button { Text = "Straighten" };
		_straightenButton.Pressed += OnStraightenPressed;
		mainContainer.AddChild(_straightenButton);
	}

	private static StyleBoxFlat CreateApplyStyleBox(Color bgColor)
	{
		var styleBox = new StyleBoxFlat();
		styleBox.BgColor = bgColor;
		styleBox.SetCornerRadiusAll(4);
		styleBox.SetContentMarginAll(8);
		return styleBox;
	}

	private void UpdateSpinBoxLimits()
	{
		if (_targetLine2D == null)
		{
			_applyButton.Disabled = true;
			_statusLabel.Text = "No Line2D selected";
			return;
		}

		int pointCount = _targetLine2D.GetPointCount();
		if (pointCount < 2)
		{
			_applyButton.Disabled = true;
			_statusLabel.Text = "Line2D needs at least 2 points";
			return;
		}

		bool isClosed = _targetLine2D.Closed;

		if (isClosed)
		{
			// For closed loops: any point can be start or end (allows wrap-around)
			_startIndexSpinBox.MaxValue = pointCount - 1;
			_endIndexSpinBox.MaxValue = pointCount - 1;
			_endIndexSpinBox.MinValue = 0;
		}
		else
		{
			// For open lines: keep existing constraints
			_startIndexSpinBox.MaxValue = Mathf.Max(0, pointCount - 2);
			_endIndexSpinBox.MaxValue = Mathf.Max(1, pointCount - 1);
			_endIndexSpinBox.MinValue = _startIndexSpinBox.Value + 1;
		}

		// Set default end to min(5, last point) for reasonable initial selection
		if (!isClosed && _endIndexSpinBox.Value <= _startIndexSpinBox.Value)
		{
			_endIndexSpinBox.Value = Mathf.Min(5, pointCount - 1);
		}

		UpdateStatusLabel();
		_applyButton.Disabled = false;
	}

	private void OnStartIndexChanged(double value)
	{
		// Only enforce end > start for non-closed loops
		if (_targetLine2D != null && !_targetLine2D.Closed)
		{
			_endIndexSpinBox.MinValue = value + 1;
			if (_endIndexSpinBox.Value <= value)
			{
				_endIndexSpinBox.Value = value + 1;
			}
		}

		UpdateStatusLabel();
	}

	private void OnStartPickerToggled(bool pressed)
	{
		if (pressed)
		{
			// Deactivate end picker if active
			_endPickerButton.SetPressedNoSignal(false);

			int startIdx = (int)_startIndexSpinBox.Value;
			int endIdx = (int)_endIndexSpinBox.Value;
			Line2DHelpersPlugin.Instance?.StartPointPicking(_targetLine2D, _startIndexSpinBox, startIdx, endIdx, this);
		}
		else
		{
			Line2DHelpersPlugin.Instance?.CancelPointPicking();
		}
	}

	private void OnEndPickerToggled(bool pressed)
	{
		if (pressed)
		{
			// Deactivate start picker if active
			_startPickerButton.SetPressedNoSignal(false);

			int startIdx = (int)_startIndexSpinBox.Value;
			int endIdx = (int)_endIndexSpinBox.Value;
			Line2DHelpersPlugin.Instance?.StartPointPicking(_targetLine2D, _endIndexSpinBox, startIdx, endIdx, this);
		}
		else
		{
			Line2DHelpersPlugin.Instance?.CancelPointPicking();
		}
	}

	/// <summary>
	/// Called by the plugin when picking ends (point selected or cancelled)
	/// </summary>
	public void OnPickingEnded()
	{
		_startPickerButton.SetPressedNoSignal(false);
		_endPickerButton.SetPressedNoSignal(false);
	}

	private void UpdateStatusLabel()
	{
		if (_targetLine2D == null)
		{
			return;
		}

		int startIdx = (int)_startIndexSpinBox.Value;
		int endIdx = (int)_endIndexSpinBox.Value;
		int totalPoints = _targetLine2D.GetPointCount();

		int segmentCount;
		if (_targetLine2D.Closed && startIdx == endIdx)
		{
			// Full loop: all segments
			segmentCount = totalPoints;
		}
		else if (_targetLine2D.Closed && endIdx < startIdx)
		{
			// Wrap-around: segments from start→end of array + 0→end
			segmentCount = (totalPoints - startIdx) + endIdx;
		}
		else
		{
			segmentCount = endIdx - startIdx;
		}

		_statusLabel.Text = $"Smoothing {segmentCount} segment(s) ({totalPoints} total points)";
	}

	private void OnApplyPressed()
	{
		if (_targetLine2D == null)
		{
			return;
		}

		int startIdx = (int)_startIndexSpinBox.Value;
		int endIdx = (int)_endIndexSpinBox.Value;

		// Convert from "points per 10 units" to "points per unit"
		float pointsPerUnit = (float)_pointsPerUnitSpinBox.Value / 10.0f;
		float bulgeFactor = (float)_bulgeFactorSpinBox.Value;
		bool evenSpacing = _evenSpacingCheckBox.ButtonPressed;

		ApplySmoothing(startIdx, endIdx, pointsPerUnit, bulgeFactor, evenSpacing);
	}

	private void ApplySmoothing(int startIdx, int endIdx, float pointsPerUnit, float bulgeFactor, bool evenSpacing)
	{
		// Get original points (Points property returns a copy)
		var pointsArray = _targetLine2D.Points;

		bool isClosedLoop = _targetLine2D.Closed;

		// Full loop (start == end) or wrap-around (end < start) both use wrap method
		bool isWrapAround = isClosedLoop && endIdx <= startIdx;

		Vector2[] smoothedSegment;
		List<Vector2> newPointsList;

		if (isWrapAround)
		{
			// 2. Wrap-around smoothing
			smoothedSegment = CatmullRomSpline.GenerateSmoothPointsWithWrap(
				pointsArray, startIdx, endIdx, pointsPerUnit, bulgeFactor);

			// 2b. Apply even spacing if enabled
			if (evenSpacing && smoothedSegment.Length > 2)
			{
				smoothedSegment = CatmullRomSpline.RedistributeEvenly(smoothedSegment, smoothedSegment.Length);
			}

			// 3. For wrap-around: keep unaffected points (between end and start), then add smoothed
			newPointsList = new List<Vector2>();

			// Add unaffected points between end and start
			for (int i = endIdx + 1; i < startIdx; i++)
			{
				newPointsList.Add(pointsArray[i]);
			}

			// Add smoothed segment
			newPointsList.AddRange(smoothedSegment);
		}
		else
		{
			// 2. Normal smoothing
			smoothedSegment = CatmullRomSpline.GenerateSmoothPoints(
				pointsArray, startIdx, endIdx, pointsPerUnit, bulgeFactor, isClosedLoop);

			// 2b. Apply even spacing if enabled
			if (evenSpacing && smoothedSegment.Length > 2)
			{
				smoothedSegment = CatmullRomSpline.RedistributeEvenly(smoothedSegment, smoothedSegment.Length);
			}

			// 3. Build new points array: before + smoothed + after
			newPointsList = new List<Vector2>();

			// Add points before the smoothed range
			for (int i = 0; i < startIdx; i++)
			{
				newPointsList.Add(pointsArray[i]);
			}

			// Add the smoothed points
			newPointsList.AddRange(smoothedSegment);

			// Add points after the smoothed range
			for (int i = endIdx + 1; i < pointsArray.Length; i++)
			{
				newPointsList.Add(pointsArray[i]);
			}
		}

		// 4. Apply with undo support
		ApplySmoothingWithUndo(_targetLine2D, [.. newPointsList], pointsArray, startIdx, endIdx);

		// 5. Update limits and status for next operation
		UpdateSpinBoxLimits();
	}

	private void ApplySmoothingWithUndo(Line2D line2D, Vector2[] newPoints, Vector2[] originalPoints, int startIdx, int endIdx)
	{
		// Use EditorUndoRedoManager directly - NOT GetHistoryUndoRedo()
		// The Godot docs warn: "directly operating on the UndoRedo object might affect editor's stability"
		var undoRedo = EditorInterface.Singleton.GetEditorUndoRedo();

		// CreateAction with customContext ensures correct scene history is used
		undoRedo.CreateAction(
			$"Smooth Line2D Turn ({startIdx}-{endIdx})",
			mergeMode: UndoRedo.MergeMode.Disable,
			customContext: line2D);

		// Use EditorUndoRedoManager.AddDoProperty/AddUndoProperty directly
		undoRedo.AddDoProperty(line2D, Line2D.PropertyName.Points, newPoints);
		undoRedo.AddUndoProperty(line2D, Line2D.PropertyName.Points, originalPoints);

		undoRedo.CommitAction();

		GD.Print($"[Line2D Helpers] Smoothed points {startIdx} to {endIdx}: {originalPoints.Length} -> {newPoints.Length} points");
	}

	private void OnStraightenPressed()
	{
		if (_targetLine2D == null)
		{
			return;
		}

		int startIdx = (int)_startIndexSpinBox.Value;
		int endIdx = (int)_endIndexSpinBox.Value;

		ApplyStraighten(startIdx, endIdx);
	}

	private void ApplyStraighten(int startIdx, int endIdx)
	{
		var pointsArray = _targetLine2D.Points;
		bool isClosedLoop = _targetLine2D.Closed;
		bool isWrapAround = isClosedLoop && endIdx <= startIdx;

		var newPointsList = new List<Vector2>();

		if (isWrapAround)
		{
			// Keep points between end and start (the non-selected portion)
			for (int i = endIdx; i <= startIdx; i++)
			{
				newPointsList.Add(pointsArray[i]);
			}
		}
		else
		{
			// Keep points before start
			for (int i = 0; i < startIdx; i++)
			{
				newPointsList.Add(pointsArray[i]);
			}

			// Add only start and end points (straight line)
			newPointsList.Add(pointsArray[startIdx]);
			newPointsList.Add(pointsArray[endIdx]);

			// Keep points after end
			for (int i = endIdx + 1; i < pointsArray.Length; i++)
			{
				newPointsList.Add(pointsArray[i]);
			}
		}

		ApplyStraightenWithUndo(_targetLine2D, [.. newPointsList], pointsArray, startIdx, endIdx);
		UpdateSpinBoxLimits();
	}

	private void ApplyStraightenWithUndo(Line2D line2D, Vector2[] newPoints, Vector2[] originalPoints, int startIdx, int endIdx)
	{
		var undoRedo = EditorInterface.Singleton.GetEditorUndoRedo();
		undoRedo.CreateAction(
			$"Straighten Line2D ({startIdx}-{endIdx})",
			mergeMode: UndoRedo.MergeMode.Disable,
			customContext: line2D);

		undoRedo.AddDoProperty(line2D, Line2D.PropertyName.Points, newPoints);
		undoRedo.AddUndoProperty(line2D, Line2D.PropertyName.Points, originalPoints);

		undoRedo.CommitAction();

		GD.Print($"[Line2D Helpers] Straightened points {startIdx} to {endIdx}: {originalPoints.Length} -> {newPoints.Length} points");
	}
}
#endif
