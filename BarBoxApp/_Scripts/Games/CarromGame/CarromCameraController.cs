using Godot;

/// <summary>
/// Handles camera positioning and zoom for the Carrom game
/// Centers the camera on the board at origin (0,0) instead of moving the board to screen center
/// Supports rotation for 4-player competitive mode
/// </summary>
[GlobalClass]
public partial class CarromCameraController : Node
{
	[Signal] public delegate void RotationCompleteEventHandler();

	// ================================================================
	// EXPORT PROPERTIES
	// ================================================================
	
	[ExportCategory("Camera Settings")]
	[Export] public float DefaultZoom { get; set; } = 1.0f;
	[Export] public float MinZoom { get; set; } = 0.5f;
	[Export] public float MaxZoom { get; set; } = 2.0f;
	[Export] public float RotationDuration { get; set; } = 1.0f;

	// ================================================================
	// CONSTANTS
	// ================================================================
	
	// Total height reserved for TopMenuBar (100px) + ContextButtonBar (50px)
	private const float TOP_MENU_HEIGHT = 150.0f;

	// ================================================================
	// PRIVATE FIELDS
	// ================================================================

	private Camera2D _boardCamera;
	private CarromBoard _board;
	private Tween _rotationTween;
	private int _currentPlayerIndex = 0;

	// ================================================================
	// PUBLIC PROPERTIES
	// ================================================================

	public Camera2D BoardCamera => _boardCamera;
	public Vector2 CameraPosition => _boardCamera?.GlobalPosition ?? Vector2.Zero;
	public Vector2 CameraOffset => _boardCamera?.Offset ?? Vector2.Zero;
	public Vector2 CameraZoom => _boardCamera?.Zoom ?? Vector2.One;

	// ================================================================
	// INITIALIZATION
	// ================================================================

	/// <summary>
	/// Initialize the camera system
	/// </summary>
	public void Initialize(CarromBoard board)
	{
		_board = board;
		
		// Get camera from scene instead of creating one programmatically
		_boardCamera = GetParent().GetNode<Camera2D>("GameCamera");
		if (_boardCamera == null)
		{
			GD.PrintErr("[CarromCameraController] Failed to find GameCamera node in parent scene");
			return;
		}
		
		// Ensure camera is enabled and current
		_boardCamera.Enabled = true;
		_boardCamera.MakeCurrent();

		// Position camera to center on board at origin
		PositionCameraOverBoard();
		
	}

	// ================================================================
	// CAMERA POSITIONING
	// ================================================================

	/// <summary>
	/// Position camera to center on the board at origin with appropriate zoom
	/// </summary>
	public void PositionCameraOverBoard()
	{
		if (_boardCamera == null || _board == null) return;

		var viewport = GetViewport();
		if (viewport == null) return;

		// Preserve current rotation during positioning
		float currentRotation = _boardCamera.Rotation;

		Vector2 viewportSize = viewport.GetVisibleRect().Size;
		
		// Position camera at origin (0,0) to look directly at the board
		_boardCamera.GlobalPosition = Vector2.Zero;
		
		// Use camera offset to shift view down into playable area (below top menu)
		// This makes the board appear centered in the playable area rather than screen center
		_boardCamera.Offset = new Vector2(0, TOP_MENU_HEIGHT / 2);
		
		// Account for top menu bar in playable area calculation for zoom
		Vector2 playableArea = new Vector2(viewportSize.X, viewportSize.Y - TOP_MENU_HEIGHT);
		
		// Calculate zoom to fit board in playable area
		CalculateAndSetZoom(playableArea);
		
		// Restore the rotation after positioning
		_boardCamera.Rotation = currentRotation;
		_boardCamera.ForceUpdateScroll();
	}

	/// <summary>
	/// Calculate appropriate zoom level to fit the board in the playable area
	/// </summary>
	private void CalculateAndSetZoom(Vector2 playableArea)
	{
		if (_board == null) 
		{
			_boardCamera.Zoom = new Vector2(DefaultZoom, DefaultZoom);
			return;
		}

		// Get board dimensions
		float boardSize = _board.BoardSize;
		float padding = 0.1f; // 10% padding for visual breathing room
		Vector2 paddedBoardSize = Vector2.One * boardSize * (1.0f + padding * 2.0f);
		
		// Prevent division by zero
		if (paddedBoardSize.X <= 0 || paddedBoardSize.Y <= 0 || playableArea.X <= 0 || playableArea.Y <= 0)
		{
			GD.PrintErr("[CarromCameraController] Invalid size values - using default zoom");
			_boardCamera.Zoom = new Vector2(DefaultZoom, DefaultZoom);
			return;
		}
		
		// Calculate zoom to fit board in playable area
		float scaleX = playableArea.X / paddedBoardSize.X;
		float scaleY = playableArea.Y / paddedBoardSize.Y;
		float zoom = Mathf.Clamp(Mathf.Min(scaleX, scaleY), MinZoom, MaxZoom);
		
		_boardCamera.Zoom = new Vector2(zoom, zoom);
		
	}

	// ================================================================
	// COORDINATE TRANSFORMATION
	// ================================================================

	/// <summary>
	/// Transform screen coordinates to board coordinates
	/// Since the board is at origin (0,0), this directly gives board-local coordinates
	/// </summary>
	public Vector2 ScreenToBoardPosition(Vector2 screenPosition)
	{
		if (_boardCamera == null) return screenPosition;

		var viewport = GetViewport();
		if (viewport == null) return screenPosition;

		// Force camera transform update
		_boardCamera.ForceUpdateScroll();
		
		// Use Godot's viewport transform to convert screen to world coordinates
		// Since the board is at origin, world coordinates = board coordinates
		Transform2D canvasTransform = viewport.GetCanvasTransform();
		Vector2 boardPosition = canvasTransform.AffineInverse() * screenPosition;
		
		return boardPosition;
	}

	// ================================================================
	// VIEWPORT MANAGEMENT
	// ================================================================

	/// <summary>
	/// Handle viewport size changes (e.g., window resize, orientation change)
	/// </summary>
	public void OnViewportSizeChanged()
	{
		if (_board != null && _boardCamera != null)
		{
			// Recalculate camera positioning and zoom when viewport changes
			CallDeferred(MethodName.PositionCameraOverBoard);
		}
	}

	// ================================================================
	// CAMERA ROTATION FOR COMPETITIVE MODE
	// ================================================================

	/// <summary>
	/// Rotate camera so the specified player appears at the bottom of the screen
	/// Player indices: 0=bottom, 1=top, 2=left, 3=right
	/// </summary>
	public void RotateToPlayer(int playerIndex, float duration = -1.0f)
	{
		// Validate camera exists and is properly set up
		if (!ValidateCameraState())
		{
			GD.PrintErr("[CarromCameraController] Camera validation failed - cannot rotate");
			return;
		}

		_currentPlayerIndex = playerIndex;
		float useDuration = duration < 0 ? RotationDuration : duration;
		
		// Calculate target rotation - each player is 90 degrees apart
		// Player 0: 0° (bottom), Player 1: 180° (top), Player 2: 90° (left), Player 3: 270° (right)
		float targetRotation = playerIndex switch
		{
			0 => 0.0f,      // Bottom player - no rotation
			1 => Mathf.Pi,  // Top player - 180° rotation
			2 => Mathf.Pi / 2,    // Left player - 90° rotation
			3 => -Mathf.Pi / 2,   // Right player - 270° (-90°) rotation
			_ => 0.0f
		};

		// Stop any existing rotation
		_rotationTween?.Kill();

		if (useDuration <= 0.0f)
		{
			// Immediate rotation
			_boardCamera.Rotation = targetRotation;
			_boardCamera.ForceUpdateScroll();
			EmitSignal(SignalName.RotationComplete);
		}
		else
		{
			// Animated rotation
			_rotationTween = CreateTween();
			if (_rotationTween == null)
			{
				GD.PrintErr("[CarromCameraController] Failed to create tween - falling back to immediate rotation");
				_boardCamera.Rotation = targetRotation;
				_boardCamera.ForceUpdateScroll();
				EmitSignal(SignalName.RotationComplete);
				return;
			}
			
			_rotationTween.TweenProperty(_boardCamera, "rotation", targetRotation, useDuration);
			_rotationTween.TweenCallback(Callable.From(() => {
				_boardCamera.ForceUpdateScroll();
				EmitSignal(SignalName.RotationComplete);
			}));
		}
	}

	/// <summary>
	/// Rotate camera to current player immediately without animation
	/// </summary>
	public void SnapToPlayer(int playerIndex)
	{
		RotateToPlayer(playerIndex, 0.0f);
	}

	/// <summary>
	/// Get the current player index that the camera is oriented towards
	/// </summary>
	public int GetCurrentPlayerIndex()
	{
		return _currentPlayerIndex;
	}

	/// <summary>
	/// Reset camera to default orientation (player 0 at bottom)
	/// </summary>
	public void ResetRotation(float duration = -1.0f)
	{
		RotateToPlayer(0, duration);
	}

	// ================================================================
	// CAMERA VALIDATION AND DEBUGGING
	// ================================================================

	/// <summary>
	/// Validate that the camera is in a proper state for rotation operations
	/// </summary>
	private bool ValidateCameraState()
	{
		// Check camera exists and is valid
		if (_boardCamera == null || !IsInstanceValid(_boardCamera))
		{
			GD.PrintErr("[CarromCameraController] Camera is null or invalid");
			return false;
		}

		// Check camera is enabled
		if (!_boardCamera.Enabled)
		{
			GD.PrintErr("[CarromCameraController] Camera is disabled");
			return false;
		}

		// Ensure camera is current
		if (!_boardCamera.IsCurrent())
		{
			_boardCamera.MakeCurrent();
			if (!_boardCamera.IsCurrent())
			{
				GD.PrintErr("[CarromCameraController] Failed to make camera current");
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Get comprehensive camera debug information
	/// </summary>
	public string GetCameraDebugInfo()
	{
		if (_boardCamera == null)
			return "Camera: null";

		if (!IsInstanceValid(_boardCamera))
			return "Camera: invalid Godot object";

		return $"Camera - Enabled: {_boardCamera.Enabled}, Current: {_boardCamera.IsCurrent()}, " +
		       $"Position: {_boardCamera.GlobalPosition}, Offset: {_boardCamera.Offset}, " +
		       $"Zoom: {_boardCamera.Zoom}, Rotation: {Mathf.RadToDeg(_boardCamera.Rotation):F1}°";
	}

	// ================================================================
	// CLEANUP
	// ================================================================

	public override void _ExitTree()
	{
		_rotationTween?.Kill();
		base._ExitTree();
	}
}