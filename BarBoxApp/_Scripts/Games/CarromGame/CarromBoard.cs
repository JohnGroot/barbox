using Godot;
using System.Collections.Generic;

/// <summary>
/// Carrom board with custom drawing and physics boundaries
/// </summary>
[GlobalClass]
public partial class CarromBoard : Node2D
{
	[Signal] public delegate void PiecePocketedEventHandler(CarromPiece piece);

	[ExportCategory("Board Design")]
	[Export] public Vector2 BoardSize { get; set; } = new Vector2(800, 800);
	[Export(PropertyHint.Range, "20.0,100.0,5.0")] public float BorderWidth { get; set; } = 40.0f;
	[Export] public Color BoardColor { get; set; } = new Color(0.8f, 0.6f, 0.4f); // Wood color
	[Export] public Color LineColor { get; set; } = Colors.Black;
	[Export(PropertyHint.Range, "1.0,10.0,0.5")] public float LineWidth { get; set; } = 3.0f;

	[ExportCategory("Pocket Settings")]
	[Export(PropertyHint.Range, "15.0,50.0,1.0")] public float PocketRadius { get; set; } = 30.0f;
	[Export] public Color PocketColor { get; set; } = Colors.Black;

	[ExportCategory("Baseline Settings")]
	[Export(PropertyHint.Range, "0.05,0.4,0.005")] public float BaselineMarginPercent { get; set; } = 0.125f; // 12.5% margin from board edge
	[Export(PropertyHint.Range, "0.1,0.5,0.05")] public float BaselineLengthPercent { get; set; } = 0.25f; // 25% of board width
	[Export(PropertyHint.Range, "10.0,25.0,0.5")] public float StrikerRadius { get; set; } = 15.0f; // Striker radius for baseline width

	// Computed baseline properties
	public float BaselineDistance => BoardSize.Y * BaselineMarginPercent;
	public float BaselineLength => BoardSize.X * BaselineLengthPercent;
	public float StrikerWidth => StrikerRadius * 2.0f;

	// Dimension-aware baseline calculations
	/// <summary>
	/// Get baseline margin distance for a specific player using correct dimension
	/// </summary>
	public float GetBaselineMarginForPlayer(int playerIndex)
	{
		return playerIndex switch
		{
			0 or 1 => BoardSize.Y * BaselineMarginPercent, // Bottom/Top players use Y margin
			2 or 3 => BoardSize.X * BaselineMarginPercent, // Left/Right players use X margin
			_ => BoardSize.Y * BaselineMarginPercent // Default to Y margin
		};
	}

	/// <summary>
	/// Get horizontal baseline distance (for top/bottom players)
	/// </summary>
	public float GetHorizontalBaselineDistance() => BoardSize.Y * BaselineMarginPercent;

	/// <summary>
	/// Get vertical baseline distance (for left/right players)
	/// </summary>
	public float GetVerticalBaselineDistance() => BoardSize.X * BaselineMarginPercent;

	/// <summary>
	/// Validate baseline margin settings to ensure they don't exceed board dimensions
	/// </summary>
	public bool ValidateBaselineMargins()
	{
		float maxMarginPercent = 0.4f; // Maximum 40% margin to leave room for gameplay
		
		if (BaselineMarginPercent < 0.05f || BaselineMarginPercent > maxMarginPercent)
		{
			GD.PrintErr($"[CarromBoard] Invalid BaselineMarginPercent: {BaselineMarginPercent}. Must be between 0.05 and {maxMarginPercent}");
			return false;
		}
		
		if (BaselineLengthPercent < 0.1f || BaselineLengthPercent > 0.5f)
		{
			GD.PrintErr($"[CarromBoard] Invalid BaselineLengthPercent: {BaselineLengthPercent}. Must be between 0.1 and 0.5");
			return false;
		}
		
		// Ensure baselines don't overlap with center or pockets
		float minBoardDimension = Mathf.Min(BoardSize.X, BoardSize.Y);
		float maxAllowableMargin = minBoardDimension * 0.3f; // 30% of smallest dimension
		float actualMarginX = BoardSize.X * BaselineMarginPercent;
		float actualMarginY = BoardSize.Y * BaselineMarginPercent;
		
		if (actualMarginX > maxAllowableMargin || actualMarginY > maxAllowableMargin)
		{
			GD.PrintErr($"[CarromBoard] Baseline margins too large for board size. Max allowable: {maxAllowableMargin}");
			return false;
		}
		
		return true;
	}

	// Physics and collision setup
	private StaticBody2D _boardPhysics;
	private List<CarromPocket> _pockets = new List<CarromPocket>();
	private Vector2[] _pocketPositions;
	private bool _needsRedraw = true;

	// Board layout constants
	private const float CENTER_CIRCLE_RADIUS = 50.0f;

	public override void _Ready()
	{
		// Validate all settings before initialization
		if (!ValidateAllSettings())
		{
			GD.PrintErr("[CarromBoard] Board initialization failed due to invalid settings");
			return;
		}
		
		SetupBoardPhysics();
		SetupPockets();
		CalculatePocketPositions();
		QueueRedraw(); // Trigger initial draw
		
		GD.Print("[CarromBoard] Board initialized successfully with dimension-aware baselines");
	}

	/// <summary>
	/// Validate all export settings and clamp to safe ranges
	/// </summary>
	private bool ValidateAllSettings()
	{
		// Validate board size
		if (BoardSize.X < 400 || BoardSize.Y < 400)
		{
			GD.PrintErr("[CarromBoard] Board size too small. Minimum 400x400");
			BoardSize = new Vector2(Mathf.Max(BoardSize.X, 400), Mathf.Max(BoardSize.Y, 400));
		}
		
		if (BoardSize.X > 1200 || BoardSize.Y > 1200)
		{
			GD.PrintErr("[CarromBoard] Board size too large. Maximum 1200x1200");
			BoardSize = new Vector2(Mathf.Min(BoardSize.X, 1200), Mathf.Min(BoardSize.Y, 1200));
		}
		
		// Clamp export values to safe ranges
		BorderWidth = Mathf.Clamp(BorderWidth, 20.0f, 100.0f);
		LineWidth = Mathf.Clamp(LineWidth, 1.0f, 10.0f);
		PocketRadius = Mathf.Clamp(PocketRadius, 15.0f, 50.0f);
		BaselineMarginPercent = Mathf.Clamp(BaselineMarginPercent, 0.05f, 0.4f);
		BaselineLengthPercent = Mathf.Clamp(BaselineLengthPercent, 0.1f, 0.5f);
		StrikerRadius = Mathf.Clamp(StrikerRadius, 10.0f, 25.0f);
		
		// Validate relationships between settings
		return ValidateBaselineMargins() && ValidatePocketPositioning();
	}

	/// <summary>
	/// Validate pocket positioning doesn't interfere with board boundaries
	/// </summary>
	private bool ValidatePocketPositioning()
	{
		Vector2 halfSize = BoardSize / 2;
		float pocketOffset = PocketRadius * 0.7f;
		
		// Check if pockets fit within board corners
		if (pocketOffset > halfSize.X * 0.3f || pocketOffset > halfSize.Y * 0.3f)
		{
			GD.PrintErr($"[CarromBoard] Pocket radius too large for board size. Reducing from {PocketRadius}");
			PocketRadius = Mathf.Min(halfSize.X, halfSize.Y) * 0.2f;
			return false;
		}
		
		return true;
	}

	/// <summary>
	/// Setup board physics boundaries
	/// </summary>
	private void SetupBoardPhysics()
	{
		_boardPhysics = new StaticBody2D();
		AddChild(_boardPhysics);

		// Setup physics material for low friction sliding
		var boardMaterial = new PhysicsMaterial();
		boardMaterial.Friction = 0.05f; // Very low friction for smooth sliding
		boardMaterial.Bounce = 0.9f; // High bounce for realistic collisions
		_boardPhysics.PhysicsMaterialOverride = boardMaterial;

		// Create border collision shapes
		CreateBorderCollisions();
	}

	/// <summary>
	/// Create collision shapes for board borders
	/// </summary>
	private void CreateBorderCollisions()
	{
		Vector2 halfSize = BoardSize / 2;
		float totalBorder = BorderWidth + LineWidth;

		// Top border
		CreateBorderSegment(
			new Vector2(0, -halfSize.Y - totalBorder / 2),
			new Vector2(BoardSize.X + totalBorder * 2, totalBorder)
		);

		// Bottom border
		CreateBorderSegment(
			new Vector2(0, halfSize.Y + totalBorder / 2),
			new Vector2(BoardSize.X + totalBorder * 2, totalBorder)
		);

		// Left border
		CreateBorderSegment(
			new Vector2(-halfSize.X - totalBorder / 2, 0),
			new Vector2(totalBorder, BoardSize.Y)
		);

		// Right border
		CreateBorderSegment(
			new Vector2(halfSize.X + totalBorder / 2, 0),
			new Vector2(totalBorder, BoardSize.Y)
		);
	}

	/// <summary>
	/// Create a single border collision segment
	/// </summary>
	private void CreateBorderSegment(Vector2 position, Vector2 size)
	{
		var collisionShape = new CollisionShape2D();
		var rectShape = new RectangleShape2D();
		rectShape.Size = size;
		collisionShape.Shape = rectShape;
		collisionShape.Position = position;
		_boardPhysics.AddChild(collisionShape);
	}

	/// <summary>
	/// Setup pocket detection areas
	/// </summary>
	private void SetupPockets()
	{
		for (int i = 0; i < 4; i++)
		{
			var pocket = new CarromPocket();
			pocket.PocketRadius = PocketRadius;
			pocket.PocketIndex = i;
			AddChild(pocket);
			_pockets.Add(pocket);

			// Connect pocket signals
			pocket.PiecePocketed += OnPiecePocketed;
		}
	}

	/// <summary>
	/// Calculate pocket positions
	/// </summary>
	private void CalculatePocketPositions()
	{
		Vector2 halfSize = BoardSize / 2;
		float pocketOffset = PocketRadius * 0.7f; // Slightly inset from corners
		
		_pocketPositions = new Vector2[]
		{
			new Vector2(-halfSize.X + pocketOffset, -halfSize.Y + pocketOffset), // Top-left
			new Vector2(halfSize.X - pocketOffset, -halfSize.Y + pocketOffset),  // Top-right
			new Vector2(-halfSize.X + pocketOffset, halfSize.Y - pocketOffset),  // Bottom-left
			new Vector2(halfSize.X - pocketOffset, halfSize.Y - pocketOffset)    // Bottom-right
		};

		// Position the pocket nodes
		for (int i = 0; i < _pockets.Count && i < _pocketPositions.Length; i++)
		{
			_pockets[i].Position = _pocketPositions[i];
		}
	}

	/// <summary>
	/// Custom drawing for the carrom board
	/// </summary>
	public override void _Draw()
	{
		if (!_needsRedraw) return;
		_needsRedraw = false;

		DrawBoard();
		DrawBorders();
		DrawBaselines();
		DrawCenterCircle();
		DrawPockets();
		DrawDirectionArrows();
	}

	/// <summary>
	/// Draw the main board surface
	/// </summary>
	private void DrawBoard()
	{
		Vector2 halfSize = BoardSize / 2;
		Rect2 boardRect = new Rect2(-halfSize, BoardSize);
		DrawRect(boardRect, BoardColor);
	}

	/// <summary>
	/// Draw board borders
	/// </summary>
	private void DrawBorders()
	{
		Vector2 halfSize = BoardSize / 2;
		
		// Outer border
		Rect2 outerBorder = new Rect2(-halfSize - Vector2.One * BorderWidth, 
									  BoardSize + Vector2.One * BorderWidth * 2);
		DrawRect(outerBorder, LineColor);
		
		// Inner board surface (to create border effect)
		Rect2 innerBoard = new Rect2(-halfSize, BoardSize);
		DrawRect(innerBoard, BoardColor);
		
		// Inner border line
		var points = new Vector2[] {
			new Vector2(-halfSize.X, -halfSize.Y),
			new Vector2(halfSize.X, -halfSize.Y),
			new Vector2(halfSize.X, halfSize.Y),
			new Vector2(-halfSize.X, halfSize.Y),
			new Vector2(-halfSize.X, -halfSize.Y)
		};
		for (int i = 0; i < points.Length - 1; i++)
		{
			DrawLine(points[i], points[i + 1], LineColor, LineWidth);
		}
	}

	/// <summary>
	/// Draw baseline markings for each player with parallel lines and circular caps using dimension-aware calculations
	/// </summary>
	private void DrawBaselines()
	{
		Vector2 halfSize = BoardSize / 2;
		float baselineY = halfSize.Y - GetHorizontalBaselineDistance(); // Use horizontal distance for Y positions
		float baselineX = halfSize.X - GetVerticalBaselineDistance();   // Use vertical distance for X positions
		float halfStrikerWidth = StrikerWidth / 2;
		float halfBaselineLength = BaselineLength / 2;
		
		// Bottom baseline (player 0)
		DrawBaselineWithCaps(
			new Vector2(-halfBaselineLength, baselineY - halfStrikerWidth),
			new Vector2(halfBaselineLength, baselineY - halfStrikerWidth),
			new Vector2(-halfBaselineLength, baselineY + halfStrikerWidth),
			new Vector2(halfBaselineLength, baselineY + halfStrikerWidth)
		);
		
		// Top baseline (player 1)
		DrawBaselineWithCaps(
			new Vector2(-halfBaselineLength, -baselineY - halfStrikerWidth),
			new Vector2(halfBaselineLength, -baselineY - halfStrikerWidth),
			new Vector2(-halfBaselineLength, -baselineY + halfStrikerWidth),
			new Vector2(halfBaselineLength, -baselineY + halfStrikerWidth)
		);
		
		// Left baseline (player 2)
		DrawBaselineWithCaps(
			new Vector2(-baselineX - halfStrikerWidth, -halfBaselineLength),
			new Vector2(-baselineX - halfStrikerWidth, halfBaselineLength),
			new Vector2(-baselineX + halfStrikerWidth, -halfBaselineLength),
			new Vector2(-baselineX + halfStrikerWidth, halfBaselineLength)
		);
		
		// Right baseline (player 3)
		DrawBaselineWithCaps(
			new Vector2(baselineX - halfStrikerWidth, -halfBaselineLength),
			new Vector2(baselineX - halfStrikerWidth, halfBaselineLength),
			new Vector2(baselineX + halfStrikerWidth, -halfBaselineLength),
			new Vector2(baselineX + halfStrikerWidth, halfBaselineLength)
		);
	}

	/// <summary>
	/// Draw center circle
	/// </summary>
	private void DrawCenterCircle()
	{
		// Outer circle
		DrawArc(Vector2.Zero, CENTER_CIRCLE_RADIUS, 0, Mathf.Tau, 64, LineColor, LineWidth);
		
		// Inner decorative circle
		DrawArc(Vector2.Zero, CENTER_CIRCLE_RADIUS * 0.7f, 0, Mathf.Tau, 64, LineColor, LineWidth / 2);
		
		// Center dot
		DrawCircle(Vector2.Zero, 3.0f, LineColor);
	}

	/// <summary>
	/// Draw pocket holes
	/// </summary>
	private void DrawPockets()
	{
		foreach (Vector2 pocketPos in _pocketPositions)
		{
			// Outer pocket ring
			DrawCircle(pocketPos, PocketRadius, PocketColor);
			
			// Inner pocket hole (darker)
			DrawCircle(pocketPos, PocketRadius * 0.8f, PocketColor.Darkened(0.3f));
			
			// Highlight ring
			DrawArc(pocketPos, PocketRadius * 0.9f, 0, Mathf.Tau, 32, 
					PocketColor.Lightened(0.2f), 2.0f);
		}
	}

	/// <summary>
	/// Draw direction arrows near pockets
	/// </summary>
	private void DrawDirectionArrows()
	{
		foreach (Vector2 pocketPos in _pocketPositions)
		{
			// Calculate arrow direction (pointing toward center)
			Vector2 directionToCenter = -pocketPos.Normalized();
			Vector2 arrowStart = pocketPos + directionToCenter * (PocketRadius + 15);
			Vector2 arrowEnd = arrowStart + directionToCenter * 25;
			
			// Draw arrow shaft
			DrawLine(arrowStart, arrowEnd, LineColor, 2.0f);
			
			// Draw arrowhead
			Vector2 perpendicular = directionToCenter.Rotated(Mathf.Pi / 2) * 8;
			Vector2 arrowTip = arrowEnd + directionToCenter * 8;
			
			DrawLine(arrowEnd, arrowTip - perpendicular, LineColor, 2.0f);
			DrawLine(arrowEnd, arrowTip + perpendicular, LineColor, 2.0f);
		}
	}

	/// <summary>
	/// Handle piece being pocketed
	/// </summary>
	private void OnPiecePocketed(CarromPiece piece)
	{
		EmitSignal(SignalName.PiecePocketed, piece);
		GD.Print($"[CarromBoard] Piece {piece.Type} pocketed");
	}

	/// <summary>
	/// Draw baseline with parallel lines and circular caps
	/// </summary>
	private void DrawBaselineWithCaps(Vector2 line1Start, Vector2 line1End, Vector2 line2Start, Vector2 line2End)
	{
		// Draw parallel lines
		DrawLine(line1Start, line1End, LineColor, LineWidth);
		DrawLine(line2Start, line2End, LineColor, LineWidth);
		
		// Draw circular caps at ends
		Vector2 center1Start = (line1Start + line2Start) / 2;
		Vector2 center1End = (line1End + line2End) / 2;
		
		DrawCircle(center1Start, StrikerRadius, LineColor);
		DrawCircle(center1End, StrikerRadius, LineColor);
		
		// Draw filled circular caps with board color
		DrawCircle(center1Start, StrikerRadius - LineWidth, BoardColor);
		DrawCircle(center1End, StrikerRadius - LineWidth, BoardColor);
	}

	/// <summary>
	/// Get baseline position for a player using dimension-aware calculations
	/// </summary>
	public Vector2 GetBaselinePosition(int playerIndex)
	{
		Vector2 halfSize = BoardSize / 2;
		
		return playerIndex switch
		{
			0 => new Vector2(0, halfSize.Y - GetHorizontalBaselineDistance()),     // Bottom (Player 1)
			1 => new Vector2(0, -(halfSize.Y - GetHorizontalBaselineDistance())), // Top (Player 2)  
			2 => new Vector2(-(halfSize.X - GetVerticalBaselineDistance()), 0),   // Left (Player 3)
			3 => new Vector2(halfSize.X - GetVerticalBaselineDistance(), 0),      // Right (Player 4)
			_ => new Vector2(0, halfSize.Y - GetHorizontalBaselineDistance())     // Default to bottom
		};
	}

	/// <summary>
	/// Get valid striker positions along baseline (proportional system)
	/// </summary>
	public Vector2[] GetBaselinePositions(int playerIndex)
	{
		Vector2 baselineCenter = GetBaselinePosition(playerIndex);
		Vector2[] positions = new Vector2[11]; // 11 positions along baseline
		
		bool isHorizontalBaseline = playerIndex <= 1; // Players 0,1 use horizontal baselines
		
		for (int i = 0; i < positions.Length; i++)
		{
			float t = (i / (float)(positions.Length - 1)) - 0.5f; // -0.5 to 0.5
			float offset = t * BaselineLength; // Use full proportional baseline length
			
			if (isHorizontalBaseline)
			{
				positions[i] = baselineCenter + new Vector2(offset, 0);
			}
			else
			{
				positions[i] = baselineCenter + new Vector2(0, offset);
			}
		}
		
		return positions;
	}

	/// <summary>
	/// Check if position is on the board
	/// </summary>
	public bool IsOnBoard(Vector2 position)
	{
		Vector2 halfSize = BoardSize / 2;
		return position.X >= -halfSize.X && position.X <= halfSize.X &&
			   position.Y >= -halfSize.Y && position.Y <= halfSize.Y;
	}

	/// <summary>
	/// Check if position is within baseline area for a player (accounting for parallel lines)
	/// </summary>
	public bool IsInBaselineArea(Vector2 position, int playerIndex)
	{
		Vector2[] baselinePositions = GetBaselinePositions(playerIndex);
		Vector2 baselineStart = baselinePositions[0];
		Vector2 baselineEnd = baselinePositions[baselinePositions.Length - 1];
		
		// Check if position is within the baseline area (between parallel lines)
		bool isHorizontal = playerIndex <= 1;
		float halfStrikerWidth = StrikerWidth / 2;
		float tolerance = StrikerRadius; // Use striker radius as tolerance
		
		if (isHorizontal)
		{
			// For horizontal baselines, check Y within striker width and X within baseline length
			return Mathf.Abs(position.Y - baselineStart.Y) <= halfStrikerWidth + tolerance &&
				   position.X >= baselineStart.X - tolerance &&
				   position.X <= baselineEnd.X + tolerance;
		}
		else
		{
			// For vertical baselines, check X within striker width and Y within baseline length
			return Mathf.Abs(position.X - baselineStart.X) <= halfStrikerWidth + tolerance &&
				   position.Y >= baselineStart.Y - tolerance &&
				   position.Y <= baselineEnd.Y + tolerance;
		}
	}

	/// <summary>
	/// Get center position of the board
	/// </summary>
	public Vector2 GetCenterPosition()
	{
		return Vector2.Zero;
	}

	/// <summary>
	/// Refresh board drawing
	/// </summary>
	public void RefreshBoard()
	{
		_needsRedraw = true;
		QueueRedraw();
	}

	/// <summary>
	/// Get pocket positions for external use
	/// </summary>
	public Vector2[] GetPocketPositions()
	{
		return (Vector2[])_pocketPositions.Clone();
	}

	/// <summary>
	/// Get pocket by index
	/// </summary>
	public CarromPocket GetPocket(int index)
	{
		if (index >= 0 && index < _pockets.Count)
		{
			return _pockets[index];
		}
		return null;
	}

	/// <summary>
	/// Cleanup on exit
	/// </summary>
	public override void _Notification(int what)
	{
		if (what == NotificationExitTree)
		{
			// Disconnect pocket signals
			foreach (var pocket in _pockets)
			{
				if (GodotObject.IsInstanceValid(pocket))
				{
					pocket.PiecePocketed -= OnPiecePocketed;
				}
			}
		}
	}
}