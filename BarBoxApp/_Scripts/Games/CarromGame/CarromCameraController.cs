using Godot;

/// <summary>
/// Handles camera positioning and zoom for the Carrom game
/// Centers the camera on the board at origin (0,0) instead of moving the board to screen center
/// </summary>
[GlobalClass]
public partial class CarromCameraController : Node
{
	// ================================================================
	// EXPORT PROPERTIES
	// ================================================================
	
	[ExportCategory("Camera Settings")]
	[Export] public float DefaultZoom { get; set; } = 1.0f;
	[Export] public float MinZoom { get; set; } = 0.5f;
	[Export] public float MaxZoom { get; set; } = 2.0f;

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
		
		// Create and setup camera
		_boardCamera = new Camera2D();
		_boardCamera.Enabled = true;
		AddChild(_boardCamera);

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
	// CLEANUP
	// ================================================================

	public override void _ExitTree()
	{
		// Camera cleanup is handled automatically by Godot
		base._ExitTree();
	}
}