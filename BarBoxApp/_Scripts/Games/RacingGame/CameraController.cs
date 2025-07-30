using Godot;
using System.Collections.Generic;

namespace BarBox.Games.Racing
{
	/// <summary>
	/// Handles camera positioning, zoom calculation, and screen edge colliders for racing game
	/// Extracted from RacingGame to follow single responsibility principle
	/// </summary>
	[GlobalClass]
	public partial class RacingCameraController : Node
{
	// ================================================================
	// EXPORT PROPERTIES
	// ================================================================
	
	[ExportCategory("Screen Boundaries")]
	[Export] public float ScreenEdgeColliderThickness { get; set; } = 8.0f;

	// ================================================================
	// CONSTANTS
	// ================================================================
	
	// Total height reserved for TopMenuBar (100px) + ContextButtonBar (50px)
	private const float TOP_MENU_HEIGHT = 150.0f;

	// ================================================================
	// PRIVATE FIELDS
	// ================================================================

	// Camera system
	private Camera2D _trackCamera;
	private List<StaticBody2D> _screenEdgeColliders = new List<StaticBody2D>();

	// Track reference for positioning
	private ITrackDefinition _trackDefinition;

	// ================================================================
	// PUBLIC PROPERTIES
	// ================================================================

	public Camera2D TrackCamera => _trackCamera;
	public Vector2 CameraPosition => _trackCamera?.GlobalPosition ?? Vector2.Zero;
	public Vector2 CameraZoom => _trackCamera?.Zoom ?? Vector2.One;

	// ================================================================
	// INITIALIZATION
	// ================================================================

	/// <summary>
	/// Setup the camera system
	/// </summary>
	public void Initialize()
	{
		_trackCamera = new Camera2D();
		_trackCamera.Enabled = true;
		AddChild(_trackCamera);

		var viewport = GetViewport();
		if (viewport != null)
		{
			var screenSize = viewport.GetVisibleRect().Size;
			
			// Account for top menu bar - position camera in center of playable area
			Vector2 playableCenter = new Vector2(screenSize.X / 2, TOP_MENU_HEIGHT + (screenSize.Y - TOP_MENU_HEIGHT) / 2);
			_trackCamera.GlobalPosition = playableCenter;
			
			GD.Print($"[CameraController] Camera positioned at playable center: {playableCenter}");
		}
	}

	/// <summary>
	/// Initialize camera controller with track definition
	/// </summary>
	/// <param name="trackDefinition">Track definition for positioning</param>
	public void SetTrackDefinition(ITrackDefinition trackDefinition)
	{
		_trackDefinition = trackDefinition;
	}

	/// <summary>
	/// Update settings from external configuration
	/// </summary>
	/// <param name="screenEdgeColliderThickness">Thickness of screen edge colliders</param>
	public void UpdateSettings(float screenEdgeColliderThickness)
	{
		ScreenEdgeColliderThickness = screenEdgeColliderThickness;
	}

	// ================================================================
	// CAMERA POSITIONING
	// ================================================================

	/// <summary>
	/// Position camera over track and zoom to fit (restored original logic)
	/// </summary>
	public void PositionCameraOverTrack()
	{
		if (_trackDefinition == null || _trackCamera == null) return;

		Vector2 trackCenter = _trackDefinition.GetStartLinePosition(); // Use start line as reference
		Rect2 trackBounds = GetTrackBounds();
		
		GD.Print($"[CameraController] Track center: {trackCenter}, Track bounds: {trackBounds}");
		
		if (trackBounds.Size == Vector2.Zero) 
		{
			GD.PrintErr("[CameraController] Track bounds size is zero - cannot calculate zoom");
			return;
		}
		
		var viewport = GetViewport();
		if (viewport != null)
		{
			Vector2 viewportSize = viewport.GetVisibleRect().Size;
			
			// Account for top menu bar in playable area calculation
			Vector2 playableArea = new Vector2(viewportSize.X, viewportSize.Y - TOP_MENU_HEIGHT);
			
			float padding = 0.15f; // 15% padding for visual breathing room
			Vector2 paddedTrackSize = trackBounds.Size * (1.0f + padding * 2.0f);
			
			// Prevent division by zero in zoom calculation
			if (paddedTrackSize.X <= 0 || paddedTrackSize.Y <= 0 || playableArea.X <= 0 || playableArea.Y <= 0)
			{
				GD.PrintErr("[CameraController] Invalid size values for zoom calculation - using default zoom");
				_trackCamera.Zoom = Vector2.One;
				return;
			}
			
			float scaleX = playableArea.X / paddedTrackSize.X;
			float scaleY = playableArea.Y / paddedTrackSize.Y;
			float zoom = Mathf.Clamp(Mathf.Min(scaleX, scaleY), 0.2f, 1.0f);
			_trackCamera.Zoom = new Vector2(zoom, zoom);
			
			GD.Print($"[CameraController] Track bounds: {trackBounds.Size}, Playable area: {playableArea}, Calculated zoom: {zoom}");
			
			// Center camera in playable area
			Vector2 playableCenter = new Vector2(viewportSize.X / 2, TOP_MENU_HEIGHT + playableArea.Y / 2);
			_trackCamera.GlobalPosition = playableCenter;
			
			GD.Print($"[CameraController] Camera positioned at: {playableCenter}");
		}
	}

	/// <summary>
	/// Get track bounds from track definition
	/// </summary>
	/// <returns>Track bounds rectangle</returns>
	private Rect2 GetTrackBounds()
	{
		if (_trackDefinition is TrackDefinition trackDef)
		{
			return trackDef.GetTrackBounds();
		}
		
		// Fallback: create default bounds around start line
		var startPos = _trackDefinition?.GetStartLinePosition() ?? Vector2.Zero;
		return new Rect2(startPos - Vector2.One * 200, Vector2.One * 400);
	}

	// ================================================================
	// SCREEN EDGE COLLIDERS
	// ================================================================

	/// <summary>
	/// Setup screen edge colliders around camera view
	/// </summary>
	public void SetupScreenEdgeColliders()
	{
		var viewport = GetViewport();
		if (viewport == null || _trackCamera == null) return;

		ClearScreenEdgeColliders();

		Vector2 viewportSize = viewport.GetVisibleRect().Size;
		
		// Account for top menu bar in playable area calculation
		Vector2 playableAreaSize = new Vector2(viewportSize.X, viewportSize.Y - TOP_MENU_HEIGHT);
		
		Vector2 cameraPos = _trackCamera.GlobalPosition;
		Vector2 cameraZoom = _trackCamera.Zoom;
		Vector2 viewSize = playableAreaSize / cameraZoom;
		Vector2 viewTopLeft = cameraPos - viewSize / 2;
		Vector2 viewBottomRight = cameraPos + viewSize / 2;

		// Create four edge colliders within playable area
		CreateScreenEdgeCollider(new Vector2(viewTopLeft.X - ScreenEdgeColliderThickness / 2, cameraPos.Y), new Vector2(ScreenEdgeColliderThickness, viewSize.Y));
		CreateScreenEdgeCollider(new Vector2(viewBottomRight.X + ScreenEdgeColliderThickness / 2, cameraPos.Y), new Vector2(ScreenEdgeColliderThickness, viewSize.Y));
		CreateScreenEdgeCollider(new Vector2(cameraPos.X, viewTopLeft.Y - ScreenEdgeColliderThickness / 2), new Vector2(viewSize.X, ScreenEdgeColliderThickness));
		CreateScreenEdgeCollider(new Vector2(cameraPos.X, viewBottomRight.Y + ScreenEdgeColliderThickness / 2), new Vector2(viewSize.X, ScreenEdgeColliderThickness));
	}

	/// <summary>
	/// Clear all screen edge colliders
	/// </summary>
	private void ClearScreenEdgeColliders()
	{
		foreach (var collider in _screenEdgeColliders)
		{
			if (GodotObject.IsInstanceValid(collider))
				collider.QueueFree();
		}
		_screenEdgeColliders.Clear();
	}

	/// <summary>
	/// Create a single screen edge collider
	/// </summary>
	/// <param name="position">Collider position</param>
	/// <param name="size">Collider size</param>
	private void CreateScreenEdgeCollider(Vector2 position, Vector2 size)
	{
		var staticBody = new StaticBody2D();
		staticBody.GlobalPosition = position;

		var collisionShape = new CollisionShape2D();
		var rectangleShape = new RectangleShape2D();
		rectangleShape.Size = size;
		collisionShape.Shape = rectangleShape;

		staticBody.AddChild(collisionShape);
		AddChild(staticBody);
		_screenEdgeColliders.Add(staticBody);
	}

	// ================================================================
	// COORDINATE TRANSFORMATION
	// ================================================================

	/// <summary>
	/// Transform screen coordinates to world coordinates using Godot's viewport transform
	/// </summary>
	/// <param name="screenPosition">Screen position</param>
	/// <returns>World position</returns>
	public Vector2 TransformScreenToWorldPosition(Vector2 screenPosition)
	{
		if (_trackCamera == null) return screenPosition;

		var viewport = GetViewport();
		if (viewport == null) return screenPosition;

		// Ensure camera transform is up to date (addresses Godot's known camera update issue)
		_trackCamera.ForceUpdateScroll();
		
		// Use Godot's viewport transform to convert screen coordinates to world coordinates
		// This properly accounts for camera position, zoom, and all viewport transforms
		Transform2D canvasTransform = viewport.GetCanvasTransform();
		Vector2 worldPosition = canvasTransform.AffineInverse() * screenPosition;
		
		// Debug output to verify coordinate accuracy
		GD.Print($"[CameraController] Viewport transform - Screen: {screenPosition}, World: {worldPosition}");
		
		return worldPosition;
	}

	// ================================================================
	// VIEWPORT MANAGEMENT
	// ================================================================

	/// <summary>
	/// Handle viewport size changes (e.g., window resize, orientation change)
	/// </summary>
	public void OnViewportSizeChanged()
	{
		if (_trackDefinition != null && _trackCamera != null)
		{
			// Recalculate camera positioning and zoom when viewport changes
			CallDeferred(MethodName.PositionCameraOverTrack);
			CallDeferred(MethodName.SetupScreenEdgeColliders);
		}
	}

	// ================================================================
	// CLEANUP
	// ================================================================

	/// <summary>
	/// Clean up camera system
	/// </summary>
	public override void _ExitTree()
	{
		ClearScreenEdgeColliders();
		base._ExitTree();
	}
}
}