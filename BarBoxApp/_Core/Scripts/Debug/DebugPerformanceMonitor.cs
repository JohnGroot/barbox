using System.Collections.Generic;
using Godot;

namespace BarBox.Core.Debug;

/// <summary>
/// Debug performance overlay for all games. Dynamically created by ApplicationBootstrap
/// when OS.IsDebugBuild(). Press Ctrl+Shift+P to toggle visibility.
/// Games push custom metrics via the static Instance using SetMetric/ClearGameMetrics.
/// </summary>
public partial class DebugPerformanceMonitor : CanvasLayer
{
	public static DebugPerformanceMonitor Instance { get; private set; }

	private PanelContainer _panel;
	private VBoxContainer _vbox;
	private Label _baseMetricsLabel;
	private Label _gameMetricsLabel;
	private Timer _refreshTimer;
	private bool _isVisible = false;

	private readonly Dictionary<string, string> _gameMetrics = new();

	public override void _Ready()
	{
		Layer = 1000;
		Instance = this;

		_panel = CreatePanel();
		AddChild(_panel);

		_refreshTimer = new Timer();
		_refreshTimer.WaitTime = 0.5f;
		_refreshTimer.Timeout += RefreshDisplay;
		AddChild(_refreshTimer);
		_refreshTimer.Start();

		_panel.Visible = false;
		_isVisible = false;

		GD.Print("[DebugPerformanceMonitor] Ready - Press Ctrl+Shift+P to toggle");
	}

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}

		if (_refreshTimer != null && IsInstanceValid(_refreshTimer))
		{
			_refreshTimer.Stop();
			_refreshTimer.QueueFree();
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent &&
			keyEvent.Pressed &&
			!keyEvent.Echo &&
			keyEvent.CtrlPressed &&
			keyEvent.ShiftPressed &&
			keyEvent.Keycode == Key.P)
		{
			_isVisible = !_isVisible;
			if (_panel != null)
			{
				_panel.Visible = _isVisible;
			}

			GetViewport().SetInputAsHandled();
		}
	}

	// ================================================================
	// GAME METRICS API
	// ================================================================
	public void SetMetric(string key, string value)
	{
		_gameMetrics[key] = value;
	}

	public void ClearGameMetrics()
	{
		_gameMetrics.Clear();
	}

	// ================================================================
	// UI CONSTRUCTION
	// ================================================================
	private PanelContainer CreatePanel()
	{
		var panel = new PanelContainer();
		panel.SetAnchorsPreset(Control.LayoutPreset.TopLeft, false);
		panel.CustomMinimumSize = new Vector2(240, 0);
		panel.OffsetLeft = 10;
		panel.OffsetTop = 10;

		var styleBox = new StyleBoxFlat();
		styleBox.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.9f);
		styleBox.BorderColor = new Color(0.3f, 0.5f, 0.7f);
		styleBox.SetBorderWidthAll(2);
		styleBox.SetCornerRadiusAll(8);
		styleBox.SetContentMarginAll(10);
		panel.AddThemeStyleboxOverride("panel", styleBox);

		_vbox = new VBoxContainer();
		panel.AddChild(_vbox);

		var title = new Label();
		title.Text = "Performance Monitor";
		title.AddThemeFontSizeOverride("font_size", 14);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		_vbox.AddChild(title);

		_baseMetricsLabel = new Label();
		_baseMetricsLabel.AddThemeFontSizeOverride("font_size", 11);
		_vbox.AddChild(_baseMetricsLabel);

		_gameMetricsLabel = new Label();
		_gameMetricsLabel.AddThemeFontSizeOverride("font_size", 11);
		_gameMetricsLabel.Modulate = new Color(0.6f, 1.0f, 0.6f);
		_vbox.AddChild(_gameMetricsLabel);

		return panel;
	}

	// ================================================================
	// REFRESH
	// ================================================================
	private void RefreshDisplay()
	{
		if (!_isVisible)
		{
			return;
		}

		RefreshBaseMetrics();
		RefreshGameMetrics();
	}

	private void RefreshBaseMetrics()
	{
		float fps = (float)Performance.GetMonitor(Performance.Monitor.TimeFps);
		float frameTime = (float)Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000f;
		float physicsTime = (float)Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000f;
		long memoryStatic = (long)Performance.GetMonitor(Performance.Monitor.MemoryStatic);

		_baseMetricsLabel.Text =
			$"FPS: {fps:F0}  Frame: {frameTime:F1}ms\n" +
			$"Physics: {physicsTime:F1}ms\n" +
			$"Memory: {memoryStatic / (1024 * 1024):F1} MB";
	}

	private void RefreshGameMetrics()
	{
		if (_gameMetrics.Count == 0)
		{
			_gameMetricsLabel.Text = string.Empty;
			return;
		}

		var text = "\n--- Game ---";
		foreach (var kvp in _gameMetrics)
		{
			text += $"\n{kvp.Key}: {kvp.Value}";
		}

		_gameMetricsLabel.Text = text;
	}
}
