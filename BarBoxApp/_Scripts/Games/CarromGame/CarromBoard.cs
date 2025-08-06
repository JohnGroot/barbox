using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Carrom board with custom drawing and physics boundaries
/// </summary>
[GlobalClass]
public partial class CarromBoard : Node2D
{
	[Signal] public delegate void PiecePocketedEventHandler(CarromPiece piece);

	[ExportCategory("Board Design")]
	[Export] public float BoardSize { get; set; } = 700;
	[Export] public Color BoardColor { get; set; } = new Color(0.8f, 0.6f, 0.4f); // Wood color
	[Export] public Color EdgeColor { get; set; } = Colors.RosyBrown;
	[Export] public Color LineColor { get; set; } = Colors.Black;
	[Export(PropertyHint.Range, "1.0,10.0,0.5")] public float LineWidth { get; set; } = 3.0f;
	[Export] public Color PocketColor { get; set; } = Colors.Black;
	[Export] public bool UseCurvedBorders { get; set; } = false;

	[ExportCategory("Physics Configuration")]
	[Export] public CarromPhysicsConfig PhysicsConfig { get; set; }

	/// <summary>
	/// Get baseline distance from board edge to baseline center (same for all sides since board is square)
	/// </summary>
	public float GetBaselineDistanceFromEdge() => BaselineDistanceFromEdge;

	// Physics and collision setup
	private StaticBody2D _boardPhysics;
	private List<CarromPocket> _pockets = new List<CarromPocket>();
	private Vector2[] _pocketPositions;
	private Vector2[] _cornerArrowStarts = new Vector2[4];
	private Vector2[] _cornerArrowEnds = new Vector2[4];
	private bool _needsRedraw = true;
	
	// Curved border geometry
	private const int CORNER_ARC_SEGMENTS = 48;

	// Board layout constants based on official Carrom Laws (indiancarrom.co.in)
	// A=73.5-74cm board size, B=6.35-7.60cm frame width, C=4.45cm pocket diameter
	// FROM: https://www.indiancarrom.co.in/laws-of-carrom/
	private const float A_BOARD_SIZE_REFERENCE = 74.0f; // Playing surface 73.5-74cm
	private const float B_BORDER_WIDTH_CM = 7.0f; // Frame width 6.35-7.60cm
	private const float C_POCKET_DIAMETER_CM = 4.45f; // Pocket diameter 4.45cm (±0.15cm)
	private const float BASELINE_LENGTH_CM = 47.0f; // Official specification: "Two straight lines of 47 cm each in length" (±0.30cm)
	private const float E_BASELINE_BOTTOM_LINE_CM = 0.575f; // Official baseline thickness: 0.50-0.65cm (using mid-range)
	private const float CORNER_LINE_THICKNESS_CM = 0.15f; // Corner line thickness from diagram
	private const float F_BASELINE_DISTANCE_CM = 10.15f; // Lower baseline 10.15cm from frame
	private float F_BASELINE_MARGIN_PERCENT = 0.068f; // F=10.15cm out of 74cm, closer to edge
	private const float G_BASELINE_CAP_DIAMETER_CM = 3.18f; // End cap circle diameter 3.18cm
	public float G_BASELINE_CIRCLE_DIAMETER_PERCENT = 0.043f; // G=3.18cm diameter
	private const float H_BASELINE_CAP_INNER_CM = 2.54f; // Inner solid circle 2.54cm (red center from laws)
	private const float I_CORNER_LINE_LENGTH_CM = 26.70f; // Official arrow length maximum (26.70cm)
	private float I_BASELINE_LENGTH_PERCENT = 0.361f; // I=26.70cm out of 74cm
	private const float J_CORNER_LINE_POCKET_DISTANCE_CM = 5.00f; // Official distance from pocket edge (5.00cm)
	private const float K_CENTER_DOT_DIAMETER_CM = 3.18f; // Center dot diameter
	private const float L_CENTER_RING_DIAMETER_CM = 17.0f; // Center ring diameter
	private const float M_CORNER_ARC_WIDTH_CM = 6.35f; // M dimension: corner arc width
	private const float N_CORNER_BASELINE_SPACING = 1.27f; // N dimension: corner line spacing between baseline tips
	private const float STRIKER_DIAMETER_CM = 4.13f; // Official striker max diameter from carrom laws
	
	// Official piece dimensions from https://www.indiancarrom.co.in/laws-of-carrom/
	private const float PIECE_DIAMETER_CM = 3.1f; // Mid-range of official 3.02-3.18cm diameter
	private const float PIECE_THICKNESS_CM = 0.8f; // Mid-range of official 0.70-0.90cm thickness

	// Physics constants - now using PhysicsConfig values
	
	// Computed properties using official Carrom Law specifications
	public float ScaleFactor => BoardSize / A_BOARD_SIZE_REFERENCE;
	public float BorderWidth => B_BORDER_WIDTH_CM * ScaleFactor;
	public float PocketRadius => (C_POCKET_DIAMETER_CM / 2) * ScaleFactor;
	public float BaselineLength => BASELINE_LENGTH_CM * ScaleFactor; // Official 47cm straight line portion (endcaps close the baseline)
	public float HalfBaseLineLength => (BaselineLength / 2) - BaselineCapRadius;
	public float BaselineBottomLineThickness => E_BASELINE_BOTTOM_LINE_CM * ScaleFactor; // E: 0.45cm
	public float CornerLineThickness => CORNER_LINE_THICKNESS_CM * ScaleFactor; // 0.15cm
	public float BaselineDistance => F_BASELINE_DISTANCE_CM * ScaleFactor; // F: 10.15cm from frame
	public float BaselineCapRadius => (G_BASELINE_CAP_DIAMETER_CM / 2) * ScaleFactor; // G: 3.18cm diameter
	public float BaselineCapInnerRadius => (H_BASELINE_CAP_INNER_CM / 2) * ScaleFactor; // H: 2.54cm solid circle
	public float CornerLineLength => I_CORNER_LINE_LENGTH_CM * ScaleFactor; // I dimension
	public float CornerLinePocketDistance => J_CORNER_LINE_POCKET_DISTANCE_CM * ScaleFactor; // J dimension
	public float CenterDotRadius => (K_CENTER_DOT_DIAMETER_CM / 2) * ScaleFactor; // K dimension
	public float CenterRingRadius => (L_CENTER_RING_DIAMETER_CM / 2) * ScaleFactor; // L dimension
	public float CornerBaselineDiameter => N_CORNER_BASELINE_SPACING * ScaleFactor;
	public float StrikerDiameter => STRIKER_DIAMETER_CM * ScaleFactor; // For end cap ring sizing
	
	// Official piece dimensions scaled to board size
	public float PieceRadius => (PIECE_DIAMETER_CM / 2) * ScaleFactor; // Official piece radius (1.55cm scaled)
	public float PieceDiameter => PIECE_DIAMETER_CM * ScaleFactor; // Official piece diameter (3.1cm scaled)
	public float PieceThickness => PIECE_THICKNESS_CM * ScaleFactor; // Official piece thickness (0.8cm scaled)
	public float OfficialStrikerRadius => (STRIKER_DIAMETER_CM / 2) * ScaleFactor; // Official striker radius (2.065cm scaled)
	
	// Baseline positioning properties (computed from official Carrom dimensions)
	public float BoardHalfSize => BoardSize / 2;
	public float BaselineStrikerDiameter => BaselineCapRadius * 2; // Official striker diameter for baseline calculations
	public float ThickLineDistanceFromCenter => BoardHalfSize - BaselineDistance; // Distance from center to thick exterior baseline
	public float ThinLineDistanceFromCenter => BoardHalfSize - BaselineDistance - BaselineStrikerDiameter; // Distance from center to thin interior baseline
	public float BaselineCenterDistanceFromCenter => (ThickLineDistanceFromCenter + ThinLineDistanceFromCenter) / 2; // Distance from center to baseline center
	public float BaselineDistanceFromEdge => BaselineDistance + BaselineStrikerDiameter / 2; // Distance from board edge to baseline center

	public override void _Ready()
	{
		SetupBoardPhysics();
		SetupPockets();
		CalculatePocketPositions();
		QueueRedraw(); // Trigger initial draw
	}

	/// <summary>
	/// Setup board physics boundaries
	/// </summary>
	private void SetupBoardPhysics()
	{
		_boardPhysics = new StaticBody2D();
		AddChild(_boardPhysics);

		// Setup physics material using centralized config
		if (PhysicsConfig != null)
		{
			_boardPhysics.PhysicsMaterialOverride = PhysicsConfig.CreateBoardMaterial();
		}

		// Create border collision shapes
		CreateBorderCollisions();
	}

	/// <summary>
	/// Create collision shapes for board borders with optional curved corners
	/// </summary>
	private void CreateBorderCollisions()
	{
		if (UseCurvedBorders && ValidateCurvedGeometry())
		{
			CreateCurvedBorderCollisions();
		}
		else
		{
			CreateRectangularBorderCollisions();
		}
	}
	
	/// <summary>
	/// Create traditional rectangular border collision shapes
	/// </summary>
	private void CreateRectangularBorderCollisions()
	{
		float totalBorder = BorderWidth + LineWidth;

		// Top border
		CreateBorderSegment(
			new Vector2(0, -BoardHalfSize - totalBorder / 2),
			new Vector2(BoardSize + totalBorder * 2, totalBorder)
		);

		// Bottom border
		CreateBorderSegment(
			new Vector2(0, BoardHalfSize + totalBorder / 2),
			new Vector2(BoardSize + totalBorder * 2, totalBorder)
		);

		// Left border
		CreateBorderSegment(
			new Vector2(-BoardHalfSize - totalBorder / 2, 0),
			new Vector2(totalBorder, BoardSize)
		);

		// Right border
		CreateBorderSegment(
			new Vector2(BoardHalfSize + totalBorder / 2, 0),
			new Vector2(totalBorder, BoardSize)
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
	/// Validate curved border geometry and provide fallbacks
	/// </summary>
	private bool ValidateCurvedGeometry()
	{
		// Ensure pocket radius is reasonable for curved corners
		if (PocketRadius > BorderWidth * 0.8f)
		{
			GD.PrintErr("[CarromBoard] PocketRadius too large for curved borders, falling back to rectangular borders");
			return false;
		}
		
		if (PocketRadius <= 0)
		{
			GD.PrintErr("[CarromBoard] Invalid PocketRadius for curved borders, falling back to rectangular borders");
			return false;
		}
		
		return true;
	}
	
	/// <summary>
	/// Create curved border collision using simple segments for reliable physics
	/// </summary>
	private void CreateCurvedBorderCollisions()
	{
		// Use simple rectangular outer border (reliable collision)
		float totalBorder = BorderWidth + LineWidth;
		
		// Create outer border collision (same as rectangular approach)
		CreateBorderSegment(
			new Vector2(0, -BoardHalfSize - totalBorder / 2),
			new Vector2(BoardSize + totalBorder * 2, totalBorder)
		);
		CreateBorderSegment(
			new Vector2(0, BoardHalfSize + totalBorder / 2),
			new Vector2(BoardSize + totalBorder * 2, totalBorder)
		);
		CreateBorderSegment(
			new Vector2(-BoardHalfSize - totalBorder / 2, 0),
			new Vector2(totalBorder, BoardSize)
		);
		CreateBorderSegment(
			new Vector2(BoardHalfSize + totalBorder / 2, 0),
			new Vector2(totalBorder, BoardSize)
		);
		
		// Add inner curved boundary collision using segments
		CreateInnerCurvedBoundary();
	}
	
	/// <summary>
	/// Create inner curved boundary collision using simple line segments
	/// </summary>
	private void CreateInnerCurvedBoundary()
	{
		float innerRadius = PocketRadius;
		float cornerOffset = BoardHalfSize - innerRadius;
		
		// Calculate corner centers for interior curves
		Vector2[] cornerCenters = new Vector2[]
		{
			new Vector2(-cornerOffset, -cornerOffset), // Top-left
			new Vector2(cornerOffset, -cornerOffset),  // Top-right
			new Vector2(cornerOffset, cornerOffset),   // Bottom-right
			new Vector2(-cornerOffset, cornerOffset)   // Bottom-left
		};
		
		// Create straight edge segments
		CreateLineSegment(new Vector2(-cornerOffset, -BoardHalfSize), new Vector2(cornerOffset, -BoardHalfSize)); // Top
		CreateLineSegment(new Vector2(BoardHalfSize, -cornerOffset), new Vector2(BoardHalfSize, cornerOffset)); // Right
		CreateLineSegment(new Vector2(cornerOffset, BoardHalfSize), new Vector2(-cornerOffset, BoardHalfSize)); // Bottom
		CreateLineSegment(new Vector2(-BoardHalfSize, cornerOffset), new Vector2(-BoardHalfSize, -cornerOffset)); // Left
		
		// Create curved corner segments using line segments
		CreateCurvedCornerSegments(cornerCenters[0], innerRadius, Mathf.Pi, 3 * Mathf.Pi / 2); // Top-left
		CreateCurvedCornerSegments(cornerCenters[1], innerRadius, 3 * Mathf.Pi / 2, 2 * Mathf.Pi); // Top-right
		CreateCurvedCornerSegments(cornerCenters[2], innerRadius, 0, Mathf.Pi / 2); // Bottom-right
		CreateCurvedCornerSegments(cornerCenters[3], innerRadius, Mathf.Pi / 2, Mathf.Pi); // Bottom-left
	}
	
	/// <summary>
	/// Create a simple line segment collision shape
	/// </summary>
	private void CreateLineSegment(Vector2 start, Vector2 end)
	{
		var collisionShape = new CollisionShape2D();
		var segmentShape = new SegmentShape2D();
		segmentShape.A = start;
		segmentShape.B = end;
		collisionShape.Shape = segmentShape;
		_boardPhysics.AddChild(collisionShape);
	}
	
	/// <summary>
	/// Create curved corner collision using multiple line segments
	/// </summary>
	private void CreateCurvedCornerSegments(Vector2 center, float radius, float startAngle, float endAngle)
	{
		// Generate arc points using existing method
		var arcPoints = GenerateCornerArc(center, radius, startAngle, endAngle, CORNER_ARC_SEGMENTS);
		
		// Create line segments between consecutive arc points
		for (int i = 0; i < arcPoints.Length - 1; i++)
		{
			CreateLineSegment(arcPoints[i], arcPoints[i + 1]);
		}
	}
	
	/// <summary>
	/// Generate tessellated quarter-circle arc points
	/// </summary>
	private Vector2[] GenerateCornerArc(Vector2 center, float radius, float startAngle, float endAngle, int segments)
	{
		var points = new Vector2[segments + 1];
		for (int i = 0; i <= segments; i++)
		{
			float t = (float)i / segments;
			float angle = Mathf.Lerp(startAngle, endAngle, t);
			points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
		}
		return points;
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
	/// Calculate pocket positions at actual board corners (C dimension defines distance from edge)
	/// </summary>
	private void CalculatePocketPositions()
	{
		// Pockets are positioned at corners - C dimension (4.45cm) defines both diameter and edge-to-edge distance
		float pocketCenterOffset = PocketRadius; // Distance from board edge to pocket center
		
		_pocketPositions = new Vector2[]
		{
			new Vector2(-BoardHalfSize + pocketCenterOffset, -BoardHalfSize + pocketCenterOffset), // Top-left corner
			new Vector2(BoardHalfSize - pocketCenterOffset, -BoardHalfSize + pocketCenterOffset),  // Top-right corner
			new Vector2(-BoardHalfSize + pocketCenterOffset, BoardHalfSize - pocketCenterOffset),  // Bottom-left corner
			new Vector2(BoardHalfSize - pocketCenterOffset, BoardHalfSize - pocketCenterOffset)    // Bottom-right corner
		};

		// Position the pocket nodes
		for (int i = 0; i < _pockets.Count && i < _pocketPositions.Length; i++)
		{
			_pockets[i].Position = _pocketPositions[i];
		}
	}

	/// <summary>
	/// Custom drawing for the carrom board with official elements only
	/// </summary>
	public override void _Draw()
	{
		if (!_needsRedraw) return;
		_needsRedraw = false;

		DrawBoard();
		DrawBorders();
		DrawBaselines();
		DrawOfficialArrows(); // Official 4 arrows pointing toward pockets
		DrawCenterCircle();
		DrawPockets();
		DrawCirclesBetweenBaselines();
	}

	/// <summary>
	/// Draw the main board surface
	/// </summary>
	private void DrawBoard()
	{
		Vector2 halfSizeVector = Vector2.One * BoardHalfSize;
		Rect2 boardRect = new Rect2(-halfSizeVector, Vector2.One * BoardSize);
		DrawRect(boardRect, BoardColor);
	}

	/// <summary>
	/// Draw board borders with optional curved corners
	/// </summary>
	private void DrawBorders()
	{
		if (UseCurvedBorders && ValidateCurvedGeometry())
		{
			DrawCurvedBorders();
		}
		else
		{
			DrawRectangularBorders();
		}
	}
	
	/// <summary>
	/// Draw traditional rectangular board borders
	/// </summary>
	private void DrawRectangularBorders()
	{
		Vector2 fullSize = Vector2.One * BoardSize;
		Vector2 halfSizeVector = Vector2.One * BoardHalfSize;
		
		// Outer border
		Rect2 outerBorder = new Rect2(-halfSizeVector - Vector2.One * BorderWidth, 
			fullSize + Vector2.One * BorderWidth * 2);
		DrawRect(outerBorder, EdgeColor);
		
		// Inner board surface (to create border effect)
		Rect2 innerBoard = new Rect2(-halfSizeVector, fullSize);
		DrawRect(innerBoard, BoardColor);
		
		// Inner border line
		var points = new[] {
			new Vector2(-BoardHalfSize, -BoardHalfSize),
			new Vector2(BoardHalfSize, -BoardHalfSize),
			new Vector2(BoardHalfSize, BoardHalfSize),
			new Vector2(-BoardHalfSize, BoardHalfSize),
			new Vector2(-BoardHalfSize, -BoardHalfSize)
		};
		for (int i = 0; i < points.Length - 1; i++)
		{
			DrawLine(points[i], points[i + 1], EdgeColor, LineWidth);
		}
	}
	
	/// <summary>
	/// Draw curved board borders using single polygon approach
	/// </summary>
	private void DrawCurvedBorders()
	{
		float totalBorder = BorderWidth + LineWidth;
		float innerRadius = PocketRadius;
		
		// Calculate corner centers for interior curves
		float cornerOffset = BoardHalfSize - innerRadius;
		Vector2[] cornerCenters = new Vector2[]
		{
			new Vector2(-cornerOffset, -cornerOffset), // Top-left
			new Vector2(cornerOffset, -cornerOffset),  // Top-right
			new Vector2(cornerOffset, cornerOffset),   // Bottom-right
			new Vector2(-cornerOffset, cornerOffset)   // Bottom-left
		};
		
		// Draw outer border (simple rectangle)
		float outerSize = BoardHalfSize + totalBorder;
		Rect2 outerBorder = new Rect2(new Vector2(-outerSize, -outerSize), new Vector2(outerSize * 2, outerSize * 2));
		DrawRect(outerBorder, EdgeColor);
		
		// Generate and draw inner boundary with curved corners (to create border effect)
		var innerBoundaryPoints = new List<Vector2>();
		
		// Top edge (left to right)
		innerBoundaryPoints.Add(new Vector2(-cornerOffset, -BoardHalfSize));
		innerBoundaryPoints.Add(new Vector2(cornerOffset, -BoardHalfSize));
		
		// Top-right corner arc (270° to 0°)
		var topRightArc = GenerateCornerArc(cornerCenters[1], innerRadius, 3 * Mathf.Pi / 2, 0, CORNER_ARC_SEGMENTS);
		innerBoundaryPoints.AddRange(topRightArc.Skip(1));
		
		// Right edge (top to bottom)
		innerBoundaryPoints.Add(new Vector2(BoardHalfSize, cornerOffset));
		
		// Bottom-right corner arc (0° to 90°)
		var bottomRightArc = GenerateCornerArc(cornerCenters[2], innerRadius, 0, Mathf.Pi / 2, CORNER_ARC_SEGMENTS);
		innerBoundaryPoints.AddRange(bottomRightArc.Skip(1));
		
		// Bottom edge (right to left)
		innerBoundaryPoints.Add(new Vector2(-cornerOffset, BoardHalfSize));
		
		// Bottom-left corner arc (90° to 180°)
		var bottomLeftArc = GenerateCornerArc(cornerCenters[3], innerRadius, Mathf.Pi / 2, Mathf.Pi, CORNER_ARC_SEGMENTS);
		innerBoundaryPoints.AddRange(bottomLeftArc.Skip(1));
		
		// Left edge (bottom to top)
		innerBoundaryPoints.Add(new Vector2(-BoardHalfSize, -cornerOffset));
		
		// Top-left corner arc (180° to 270°)
		var topLeftArc = GenerateCornerArc(cornerCenters[0], innerRadius, Mathf.Pi, 3 * Mathf.Pi / 2, CORNER_ARC_SEGMENTS);
		innerBoundaryPoints.AddRange(topLeftArc.Skip(1));
		
		// Draw board surface inside curved border
		DrawColoredPolygon(innerBoundaryPoints.ToArray(), BoardColor);
		
		// Draw inner border line to match visual style
		for (int i = 0; i < innerBoundaryPoints.Count; i++)
		{
			Vector2 current = innerBoundaryPoints[i];
			Vector2 next = innerBoundaryPoints[(i + 1) % innerBoundaryPoints.Count];
			DrawLine(current, next, EdgeColor, LineWidth);
		}
	}

	/// <summary>
	/// Draw baselines using official 6-element system with correct F dimension positioning
	/// Each baseline consists of: thick exterior line, thin interior line, and 2 end cap circles with solid centers
	/// </summary>
	private void DrawBaselines()
	{
		// Use computed properties for consistent baseline positioning across the entire codebase
		// F dimension: 10.15cm from board edge to EXTERIOR (thick) line of baseline
		
		// Draw all 4 baselines with proper 6-element structure using computed properties
		// Bottom baseline (player 0) - horizontal baseline at bottom
		DrawProperBaseline(new Vector2(0, BaselineCenterDistanceFromCenter), true, 0, ThickLineDistanceFromCenter, ThinLineDistanceFromCenter);
		
		// Top baseline (player 1) - horizontal baseline at top
		DrawProperBaseline(new Vector2(0, -BaselineCenterDistanceFromCenter), true, 1, -ThickLineDistanceFromCenter, -ThinLineDistanceFromCenter);
		
		// Left baseline (player 2) - vertical baseline at left
		DrawProperBaseline(new Vector2(-BaselineCenterDistanceFromCenter, 0), false, 2, -ThickLineDistanceFromCenter, -ThinLineDistanceFromCenter);
		
		// Right baseline (player 3) - vertical baseline at right
		DrawProperBaseline(new Vector2(BaselineCenterDistanceFromCenter, 0), false, 3, ThickLineDistanceFromCenter, ThinLineDistanceFromCenter);
	}

	/// <summary>
	/// Draw center elements: L ring (piece containment) + K dot (queen position) + 8-sided star
	/// </summary>
	private void DrawCenterCircle()
	{
		// Draw center ring using L dimension (ring that fits all pieces)
		DrawArc(Vector2.Zero, CenterRingRadius, 0, Mathf.Tau, 64, LineColor, LineWidth, true);
		
		// Draw 8-sided star within the center circle
		DrawEightSidedStar();
		
		// Draw center dot using K dimension (where queen goes)
		DrawCircle(Vector2.Zero, CenterDotRadius, LineColor);
	}

	/// <summary>
	/// Draw an 8-sided star within the center circle
	/// </summary>
	private void DrawEightSidedStar()
	{
		// Calculate star dimensions based on center ring radius
		float outerRadius = CenterRingRadius; // Star fits within the center ring
		float innerRadius = outerRadius * 0.2f; // Inner radius for star points
		
		Vector2[] starPoints = new Vector2[16]; // 8 outer points + 8 inner points
		
		// Generate 8-sided star points
		for (int i = 0; i < 8; i++)
		{
			float outerAngle = (i * Mathf.Tau) / 8f;
			float innerAngle = ((i + 0.5f) * Mathf.Tau) / 8f;
			
			// Outer point (star tip)
			starPoints[i * 2] = Vector2.Zero + new Vector2(
				Mathf.Cos(outerAngle) * outerRadius,
				Mathf.Sin(outerAngle) * outerRadius
			);
			
			// Inner point (between star tips)
			starPoints[i * 2 + 1] = Vector2.Zero + new Vector2(
				Mathf.Cos(innerAngle) * innerRadius,
				Mathf.Sin(innerAngle) * innerRadius
			);
		}
		
		// Draw the star outline
		for (int i = 0; i < starPoints.Length; i++)
		{
			Vector2 currentPoint = starPoints[i];
			Vector2 nextPoint = starPoints[(i + 1) % starPoints.Length];
			DrawLine(currentPoint, nextPoint, LineColor, LineWidth * 0.85f, true);
		}
	}

	/// <summary>
	/// Draw pocket holes
	/// </summary>
	private void DrawPockets()
	{
		// Ensure pocket positions are calculated before drawing
		if (_pocketPositions == null)
		{
			CalculatePocketPositions();
		}
		
		// Safety check in case calculation failed
		if (_pocketPositions == null || _pocketPositions.Length == 0)
		{
			return;
		}
		
		foreach (Vector2 pocketPos in _pocketPositions)
		{
			// Outer pocket ring
			DrawCircle(pocketPos, PocketRadius, PocketColor, true, -1f, true);
			
			// Inner pocket hole (darker) - use centralized config if available
			float innerMultiplier = PhysicsConfig?.PocketInnerMultiplier ?? 0.8f;
			DrawCircle(pocketPos, PocketRadius * innerMultiplier, PocketColor.Darkened(0.3f), true, -1f, true);
			
			// Highlight ring - use centralized config if available  
			float highlightMultiplier = PhysicsConfig?.PocketHighlightMultiplier ?? 0.9f;
			DrawArc(pocketPos, PocketRadius * highlightMultiplier, 0, Mathf.Tau, 32, 
					PocketColor.Lightened(0.2f), 2.0f, true);
		}
	}
	
	/// <summary>
	/// Draw inner playing area defined by the 4 baseline inner lines (official specification)
	/// </summary>
	private void DrawCirclesBetweenBaselines()
	{
		// Inner playing area is defined by the 4 baseline inner lines, not an arbitrary square
		// Calculate positions of the 4 baseline inner lines
		float baselineDistance = BaselineDistance; // F: 10.15cm from frame
		float innerOffset = BaselineBottomLineThickness / 2 + CornerLineThickness;
		float halfBaselineLength = BaselineLength / 2;
		
		// Calculate inner boundary positions (defined by baseline inner lines)
		float boardHalfSize = BoardSize / 2;
		float innerBoundaryY = boardHalfSize - baselineDistance - innerOffset; // Top of bottom baseline inner line
		float innerBoundaryX = boardHalfSize - baselineDistance - innerOffset; // Left of right baseline inner line
		
		// Draw the 4 inner boundary lines (these ARE the baseline inner lines, so they're drawn in DrawBaselines)
		// This method is now primarily for reference - the actual inner area is defined by baseline inner lines
		// We can draw connecting lines at the corners if needed for visual clarity
		
		// Optional: Draw corner connecting lines (very thin) to complete the inner boundary
		var cornerPoints = new[] {
			new Vector2(-halfBaselineLength, -innerBoundaryY), // Bottom-left baseline end to top-left baseline end
			new Vector2(-innerBoundaryX, -halfBaselineLength), // Top-left baseline end to left-top baseline end
			
			new Vector2(halfBaselineLength, -innerBoundaryY),  // Bottom-right baseline end to top-right baseline end
			new Vector2(innerBoundaryX, -halfBaselineLength),  // Top-right baseline end to right-top baseline end
			
			new Vector2(halfBaselineLength, innerBoundaryY),   // Top-right baseline end to bottom-right baseline end
			new Vector2(innerBoundaryX, halfBaselineLength),   // Bottom-right baseline end to right-bottom baseline end
			
			new Vector2(-halfBaselineLength, innerBoundaryY),  // Top-left baseline end to bottom-left baseline end
			new Vector2(-innerBoundaryX, halfBaselineLength)   // Bottom-left baseline end to left-bottom baseline end
		};
		
		// Draw thin connecting lines at corners (0.15cm thickness like corner lines)
		for (int i = 0; i < cornerPoints.Length; i += 2)
		{
			DrawArc(cornerPoints[i].Lerp(cornerPoints[i + 1], 0.5f), CornerBaselineDiameter, 0, Mathf.Tau, 32, LineColor, LineWidth / 2f, true);
		}
	}
	
	/// <summary>
	/// Handle piece being pocketed
	/// </summary>
	private void OnPiecePocketed(CarromPiece piece)
	{
		EmitSignal(SignalName.PiecePocketed, piece);
	}

	/// <summary>
	/// Calculate actual endcap positions for a baseline using official D dimension
	/// </summary>
	private (Vector2 leftEndcap, Vector2 rightEndcap) CalculateEndcapPositions(Vector2 baselineCenter, bool isHorizontal)
	{
		// Official specification: "Two straight lines of 47 cm each in length... 
		// The base lines shall be closed by circles of 3.18 cm in diameter at both ends."
		// This means: 47cm = straight line length, endcaps are added at both ends

		// float lineLength = BaselineLength; // 47cm straight line portion
		//float endcapRadius = BaselineCapRadius; // 1.59cm radius (3.18cm diameter / 2)
		float halfLineLength = HalfBaseLineLength; // 23.5cm from center to line end
		float endcapCenterOffset = halfLineLength; // Position endcap center beyond line end

		Vector2 leftEndPos = baselineCenter + (isHorizontal ? new Vector2(-endcapCenterOffset, 0) : new Vector2(0, -endcapCenterOffset));
		Vector2 rightEndPos = baselineCenter + (isHorizontal ? new Vector2(endcapCenterOffset, 0) : new Vector2(0, endcapCenterOffset));
		return (leftEndPos, rightEndPos);
	}

	/// <summary>
	/// Draw a complete baseline with all 6 elements using correct F dimension positioning
	/// </summary>
	private void DrawProperBaseline(Vector2 center, bool isHorizontal, int baselineIndex, float exteriorLinePos, float interiorLinePos)
	{
		// Use shared endcap calculation for consistency
		var (leftEndPos, rightEndPos) = CalculateEndcapPositions(center, isHorizontal);
		
		// Baseline lines should span exactly 47cm (straight line portion)
		// Lines connect from one endcap edge to the other endcap edge
		float halfLineLength = HalfBaseLineLength; // 23.5cm from center to line end
		
		// Element 1: Exterior (thick) line (E dimension) - 3x corner line thickness (0.45cm) - towards walls  
		DrawBaselineLine(center, isHorizontal, halfLineLength, exteriorLinePos, true);
		
		// Element 2: Interior (thin) line - same thickness as corner lines (0.15cm) - towards center
		DrawBaselineLine(center, isHorizontal, halfLineLength, interiorLinePos, false);
		
		// Elements 3 & 4: End cap rings positioned at the ends of the 47cm line
		DrawBaselineEndCapRing(leftEndPos);
		DrawBaselineEndCapRing(rightEndPos);
		
		// Elements 5 & 6: Solid circles (H dimension = 2.54cm) within end cap rings
		DrawBaselineEndCapSolidCircle(leftEndPos);
		DrawBaselineEndCapSolidCircle(rightEndPos);
	}
	
	/// <summary>
	/// Draw baseline line (either thick exterior or thin interior)
	/// </summary>
	/// <param name="center">Baseline center position</param>
	/// <param name="isHorizontal">True for horizontal baselines, false for vertical</param>
	/// <param name="halfLength">Half the length of the baseline</param>
	/// <param name="linePosition">Position offset from center where line should be drawn</param>
	/// <param name="isThickLine">True for thick exterior line, false for thin interior line</param>
	private void DrawBaselineLine(Vector2 center, bool isHorizontal, float halfLength, float linePosition, bool isThickLine)
	{
		Vector2 startPos, endPos;
		if (isHorizontal)
		{
			// Horizontal baseline - use Y position
			startPos = new Vector2(center.X - halfLength, linePosition);
			endPos = new Vector2(center.X + halfLength, linePosition);
		}
		else
		{
			// Vertical baseline - use X position
			startPos = new Vector2(linePosition, center.Y - halfLength);
			endPos = new Vector2(linePosition, center.Y + halfLength);
		}
		
		float thickness = isThickLine ? BaselineBottomLineThickness : CornerLineThickness;
		DrawLine(startPos, endPos, LineColor, thickness);
	}
	
	/// <summary>
	/// Draw end cap ring (diameter = striker width = 3.18cm)
	/// </summary>
	private void DrawBaselineEndCapRing(Vector2 center)
	{
		float ringRadius = BaselineCapRadius;
		
		// Draw a circle to cover up the lines underneath
		DrawCircle(center, ringRadius * 1.2f, BoardColor, true, -1f, true);

		DrawArc(center, ringRadius, 0, Mathf.Tau, 32, LineColor, BaselineBottomLineThickness / 2f, true);
	}
	
	/// <summary>
	/// Draw solid circle within end cap ring (H dimension: 2.54cm)
	/// </summary>
	private void DrawBaselineEndCapSolidCircle(Vector2 center)
	{
		DrawCircle(center, BaselineCapInnerRadius * .7f, LineColor, true, -1f, true);
	}
	
	/// <summary>
	/// Draw official 4 arrows at 45-degree angles pointing toward pockets with filled triangle tips
	/// </summary>
	private void DrawOfficialArrows()
	{
		// Official specification: 4 black arrows at 45-degree angles to adjacent sides
		// Arrows pass BETWEEN base circles (not from them)
		// Arrow length not exceeding 26.70 cm (I dimension)
		// Arrows leave 5.00 cm from pocket edge (J dimension)
		
		float arrowMaxLength = I_CORNER_LINE_LENGTH_CM * ScaleFactor; // 26.70cm max
		float pocketClearance = J_CORNER_LINE_POCKET_DISTANCE_CM * ScaleFactor; // 5.00cm from pocket
		
		// Calculate arrow positions - they start from positions between base circles
		// and point toward the 4 corner pockets at 45-degree angles
		
		// Arrow start positions - positioned between baselines, not on them
		Vector2[] arrowStarts = new[]
		{
			new Vector2(-BoardHalfSize * 0.3f, -BoardHalfSize * 0.3f), // Top-left quadrant
			new Vector2(BoardHalfSize * 0.3f, -BoardHalfSize * 0.3f),  // Top-right quadrant
			new Vector2(-BoardHalfSize * 0.3f, BoardHalfSize * 0.3f),  // Bottom-left quadrant
			new Vector2(BoardHalfSize * 0.3f, BoardHalfSize * 0.3f)    // Bottom-right quadrant
		};
		
		// Arrow directions - pointing toward corners at 45-degree angles
		Vector2[] arrowDirections = new[]
		{
			new Vector2(-1, -1).Normalized(), // Top-left
			new Vector2(1, -1).Normalized(),  // Top-right
			new Vector2(-1, 1).Normalized(),  // Bottom-left
			new Vector2(1, 1).Normalized()   // Bottom-right
		};
		
		// Get pocket positions with safety check
		Vector2[] pocketPositions = GetPocketPositions();
		if (pocketPositions.Length < 4)
		{
			// Skip drawing arrows if pocket positions not available
			return;
		}
		
		// Draw the 4 official arrows and store positions
		for (int i = 0; i < 4; i++)
		{
			Vector2 arrowStart = arrowStarts[i];
			Vector2 arrowDirection = arrowDirections[i];
			
			// Calculate arrow end position, ensuring it stops before pocket edge
			Vector2 pocketPos = pocketPositions[i];
			float distanceToPocket = arrowStart.DistanceTo(pocketPos);
			float arrowLength = Mathf.Min(arrowMaxLength, distanceToPocket - pocketClearance);
			
			Vector2 arrowEnd = arrowStart + arrowDirection * arrowLength;
			
			// Store arrow positions for external access
			_cornerArrowStarts[i] = arrowStart;
			_cornerArrowEnds[i] = arrowEnd;
			
			// Draw the arrow line
			DrawLine(arrowStart, arrowEnd, LineColor, CornerLineThickness, true);
			
			// Draw filled triangle tip pointing toward pocket
			Vector2 directionToPocket = (pocketPos - arrowEnd).Normalized();
			DrawTriangleTip(arrowEnd, directionToPocket, false);
			
			// Draw corner arc at the center-end (start position) of the arrow
			DrawCornerArrowArc(arrowStart, arrowDirection);
		}
	}

	/// <summary>
	/// Draw a filled equilateral triangle arrow tip
	/// </summary>
	/// <param name="basePosition">Position of the triangle base center</param>
	/// <param name="pointDirection">Direction the triangle tip should point</param>
	/// <param name="tipAtBase">If true, tip is at basePosition (corner line style), if false, base is at basePosition (arrow style)</param>
	private void DrawTriangleTip(Vector2 basePosition, Vector2 pointDirection, bool tipAtBase = false)
	{
		// N dimension from diagram is 2.54cm, triangle side length should be half of that = 1.27cm
		const float N_DIMENSION_CM = 2.54f; // From diagram
		float triangleSideLength = (N_DIMENSION_CM / 2) * ScaleFactor; // 1.27cm scaled
		
		Vector2 normalizedDirection = pointDirection.Normalized();
		
		// Calculate equilateral triangle vertices
		// For equilateral triangle: height = side_length * sqrt(3) / 2
		float triangleHeight = triangleSideLength * Mathf.Sqrt(3) / 2;
		
		Vector2 tipPoint, baseCenter;
		if (tipAtBase)
		{
			// Corner line style: tip at base position, base extends outward
			tipPoint = basePosition;
			baseCenter = basePosition - normalizedDirection * triangleHeight;
		}
		else
		{
			// Arrow style: base at base position, tip extends toward direction
			baseCenter = basePosition;
			tipPoint = basePosition + normalizedDirection * triangleHeight;
		}
		
		// Base vertices (perpendicular to direction, equidistant from center)
		Vector2 perpendicular = normalizedDirection.Rotated(Mathf.Pi / 2);
		Vector2 baseLeft = baseCenter + perpendicular * (triangleSideLength / 2);
		Vector2 baseRight = baseCenter - perpendicular * (triangleSideLength / 2);
		
		// Draw filled equilateral triangle
		Vector2[] trianglePoints = [tipPoint, baseLeft, baseRight];
		DrawColoredPolygon(trianglePoints, LineColor);
	}

	/// <summary>
	/// Draw corner arc at the center-end of a corner arrow using M dimension
	/// </summary>
	private void DrawCornerArrowArc(Vector2 arrowStart, Vector2 arrowDirection)
	{
		// M dimension from diagram defines corner arc width: 6.35cm
		float arcRadius = (M_CORNER_ARC_WIDTH_CM / 2) * ScaleFactor; // Half of M dimension for radius
		
		// Calculate arc angles based on opposite of arrow direction
		// Arc should face back toward the arrow start, not toward the pocket
		Vector2 oppositeDirection = -arrowDirection;
		float centerAngle = oppositeDirection.Angle();
		float arcSpan = Mathf.Pi / 1.25f; // 270 degrees total span
		float startAngle = centerAngle - (arcSpan); // 45 degrees before opposite direction
		float endAngle = centerAngle + (arcSpan);   // 45 degrees after opposite direction
		
		// Calculate arc center position
		Vector2 arcCenter = arrowStart - (oppositeDirection.Normalized() * arcRadius);
		
		// Draw the corner arc centered at arrow start position, facing back toward start
		DrawArc(arcCenter, arcRadius, startAngle, endAngle, 32, LineColor, CornerLineThickness, true);
		
		// Draw triangle arrow tips at the two ends of the arc (where corner lines start)
		Vector2 arcStartPoint = arcCenter + new Vector2(Mathf.Cos(startAngle), Mathf.Sin(startAngle)) * arcRadius;
		Vector2 arcEndPoint = arcCenter + new Vector2(Mathf.Cos(endAngle), Mathf.Sin(endAngle)) * arcRadius;
		
		// Calculate tangent directions at arc endpoints to point toward board corners
		// Each triangle should point in the direction that extends toward the nearest board corner
		
		// Calculate base tangent directions (counter-clockwise)
		Vector2 baseTangentStart = new Vector2(-Mathf.Sin(startAngle), Mathf.Cos(startAngle));
		Vector2 baseTangentEnd = new Vector2(-Mathf.Sin(endAngle), Mathf.Cos(endAngle));
		
		// Determine which direction points more toward the corners
		Vector2 nearestCorner = new Vector2(
			arrowDirection.X > 0 ? BoardHalfSize : -BoardHalfSize,
			arrowDirection.Y > 0 ? BoardHalfSize : -BoardHalfSize
		);
		
		// For START triangle: choose tangent direction that points more toward corner
		Vector2 startToCorner = (nearestCorner - arcStartPoint).Normalized();
		float startDotForward = baseTangentStart.Dot(startToCorner);
		float startDotBackward = (-baseTangentStart).Dot(startToCorner);
		Vector2 startTangent = startDotForward > startDotBackward ? baseTangentStart : -baseTangentStart;
		
		// For END triangle: choose tangent direction that points more toward corner
		Vector2 endToCorner = (nearestCorner - arcEndPoint).Normalized();
		float endDotForward = baseTangentEnd.Dot(endToCorner);
		float endDotBackward = (-baseTangentEnd).Dot(endToCorner);
		Vector2 endTangent = endDotForward > endDotBackward ? baseTangentEnd : -baseTangentEnd;
		
		// Draw triangular arrow tips at arc endpoints
		DrawTriangleTip(arcStartPoint, startTangent, false);
		DrawTriangleTip(arcEndPoint, endTangent, false);
	}
	

	/// <summary>
	/// Get baseline position for a player using dimension-aware calculations
	/// </summary>
	public Vector2 GetBaselinePosition(int playerIndex)
	{
		// Use computed properties for consistent positioning
		return playerIndex switch
		{
			0 => new Vector2(0, BaselineCenterDistanceFromCenter),     // Bottom (Player 1)
			1 => new Vector2(0, -BaselineCenterDistanceFromCenter),    // Top (Player 2)  
			2 => new Vector2(-BaselineCenterDistanceFromCenter, 0),    // Left (Player 3)
			3 => new Vector2(BaselineCenterDistanceFromCenter, 0),     // Right (Player 4)
			_ => new Vector2(0, BaselineCenterDistanceFromCenter)      // Default to bottom
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
	/// Get the closest position on baseline for a given global position
	/// </summary>
	public Vector2 GetClosestPositionOnBaseline(Vector2 globalPosition, int playerIndex)
	{
		Vector2 baselineCenter = GetBaselinePosition(playerIndex);
		bool isHorizontalBaseline = playerIndex <= 1; // Players 0,1 use horizontal baselines
		
		// Calculate baseline geometry
		float halfBaselineLength = HalfBaseLineLength;
		Vector2 baselineStart, baselineEnd;
		if (isHorizontalBaseline)
		{
			// Horizontal baseline (players 0,1)
			baselineStart = baselineCenter + new Vector2(-halfBaselineLength, 0);
			baselineEnd = baselineCenter + new Vector2(halfBaselineLength, 0);
		}
		else
		{
			// Vertical baseline (players 2,3)
			baselineStart = baselineCenter + new Vector2(0, -halfBaselineLength);
			baselineEnd = baselineCenter + new Vector2(0, halfBaselineLength);
		}
		
		// Project position onto baseline line segment
		Vector2 baselineVector = baselineEnd - baselineStart;
		Vector2 positionVector = globalPosition - baselineStart;
		
		// Calculate projection parameter (0.0 = start, 1.0 = end)
		float projectionParam = positionVector.Dot(baselineVector) / baselineVector.LengthSquared();
		
		// Clamp to baseline segment bounds
		projectionParam = Mathf.Clamp(projectionParam, 0.0f, 1.0f);
		
		// Calculate final projected position
		Vector2 projectedPosition = baselineStart + baselineVector * projectionParam;
		
		return projectedPosition;
	}

	/// <summary>
	/// Check if position is on the board
	/// </summary>
	public bool IsOnBoard(Vector2 position)
	{
		return position.X >= -BoardHalfSize && position.X <= BoardHalfSize &&
			   position.Y >= -BoardHalfSize && position.Y <= BoardHalfSize;
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
		// Ensure pocket positions are calculated
		if (_pocketPositions == null)
		{
			CalculatePocketPositions();
		}
		
		// Additional safety check in case calculation failed
		if (_pocketPositions == null)
		{
			GD.PrintErr("[CarromBoard] Failed to calculate pocket positions, returning empty array");
			return []; // Return empty array instead of null
		}
		
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
	/// Set physics configuration for all pockets and update board materials
	/// </summary>
	public void SetPocketPhysicsConfig(CarromPhysicsConfig physicsConfig)
	{
		// Update board physics config reference
		PhysicsConfig = physicsConfig;
		
		// Update board physics material if already initialized
		if (_boardPhysics != null && physicsConfig != null)
		{
			_boardPhysics.PhysicsMaterialOverride = physicsConfig.CreateBoardMaterial();
		}
		
		// Pockets no longer need physics config - they use simplified constants
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
				if (pocket != null)
				{
					pocket.PiecePocketed -= OnPiecePocketed;
				}
			}
		}
	}
}