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
	[Export] public float BoardSize { get; set; } = 700;
	[Export] public Color BoardColor { get; set; } = new Color(0.8f, 0.6f, 0.4f); // Wood color
	[Export] public Color EdgeColor { get; set; } = Colors.RosyBrown;
	[Export] public Color LineColor { get; set; } = Colors.Black;
	[Export(PropertyHint.Range, "1.0,10.0,0.5")] public float LineWidth { get; set; } = 3.0f;
	[Export] public Color PocketColor { get; set; } = Colors.Black;

	/// <summary>
	/// Get horizontal baseline distance (for top/bottom players)
	/// </summary>
	public float GetHorizontalBaselineDistance() => BoardSize * F_BASELINE_MARGIN_PERCENT;

	/// <summary>
	/// Get vertical baseline distance (for left/right players)
	/// </summary>
	public float GetVerticalBaselineDistance() => BoardSize * F_BASELINE_MARGIN_PERCENT;
	// Physics and collision setup
	private StaticBody2D _boardPhysics;
	private List<CarromPocket> _pockets = new List<CarromPocket>();
	private Vector2[] _pocketPositions;
	private Vector2[] _cornerArrowStarts = new Vector2[4];
	private Vector2[] _cornerArrowEnds = new Vector2[4];
	private bool _needsRedraw = true;

	// Board layout constants based on official Carrom Laws (indiancarrom.co.in)
	// A=73.5-74cm board size, B=6.35-7.60cm frame width, C=4.45cm pocket diameter
	// FROM: https://www.indiancarrom.co.in/laws-of-carrom/
	private const float A_BOARD_SIZE_REFERENCE = 74.0f; // Playing surface 73.5-74cm
	private const float B_BORDER_WIDTH_CM = 7.0f; // Frame width 6.35-7.60cm
	private const float C_POCKET_DIAMETER_CM = 4.45f; // Pocket diameter 4.45cm (±0.15cm)
	private const float BASELINE_LENGTH_CM = 47.0f; // Official specification: "Two straight lines of 47 cm each in length" (±0.30cm)
	private const float E_BASELINE_BOTTOM_LINE_CM = 0.45f; // Bottom line: 3x corner line thickness (0.15 × 3)
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
	private const float STRIKER_DIAMETER_CM = 4.13f; // Official striker max diameter from carrom laws

	// Physics constants
	private const float BOARD_FRICTION = 0.05f; // Very low friction for smooth sliding
	private const float BOARD_BOUNCE = 0.9f; // High bounce for realistic collisions
	private const float POCKET_INNER_MULTIPLIER = 0.8f; // Inner pocket hole size multiplier
	private const float POCKET_HIGHLIGHT_MULTIPLIER = 0.9f; // Pocket highlight ring multiplier
	
	// Computed properties using official Carrom Law specifications
	public float ScaleFactor => BoardSize / A_BOARD_SIZE_REFERENCE;
	public float BorderWidth => B_BORDER_WIDTH_CM * ScaleFactor;
	public float PocketRadius => (C_POCKET_DIAMETER_CM / 2) * ScaleFactor;
	public float BaselineLength => BASELINE_LENGTH_CM * ScaleFactor; // Official 47cm straight line portion (endcaps close the baseline)
	public float BaselineBottomLineThickness => E_BASELINE_BOTTOM_LINE_CM * ScaleFactor; // E: 0.45cm
	public float CornerLineThickness => CORNER_LINE_THICKNESS_CM * ScaleFactor; // 0.15cm
	public float BaselineDistance => F_BASELINE_DISTANCE_CM * ScaleFactor; // F: 10.15cm from frame
	public float BaselineCapRadius => (G_BASELINE_CAP_DIAMETER_CM / 2) * ScaleFactor; // G: 3.18cm diameter
	public float BaselineCapInnerRadius => (H_BASELINE_CAP_INNER_CM / 2) * ScaleFactor; // H: 2.54cm solid circle
	public float CornerLineLength => I_CORNER_LINE_LENGTH_CM * ScaleFactor; // I dimension
	public float CornerLinePocketDistance => J_CORNER_LINE_POCKET_DISTANCE_CM * ScaleFactor; // J dimension
	public float CenterDotRadius => (K_CENTER_DOT_DIAMETER_CM / 2) * ScaleFactor; // K dimension
	public float CenterRingRadius => (L_CENTER_RING_DIAMETER_CM / 2) * ScaleFactor; // L dimension
	public float StrikerDiameter => STRIKER_DIAMETER_CM * ScaleFactor; // For end cap ring sizing

	public override void _Ready()
	{
		SetupBoardPhysics();
		SetupPockets();
		CalculatePocketPositions();
		QueueRedraw(); // Trigger initial draw
		
		GD.Print("[CarromBoard] Board initialized successfully with proportional diagram-based layout");
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
		boardMaterial.Friction = BOARD_FRICTION; // Very low friction for smooth sliding
		boardMaterial.Bounce = BOARD_BOUNCE; // High bounce for realistic collisions
		_boardPhysics.PhysicsMaterialOverride = boardMaterial;

		// Create border collision shapes
		CreateBorderCollisions();
	}

	/// <summary>
	/// Create collision shapes for board borders
	/// </summary>
	private void CreateBorderCollisions()
	{
		Vector2 halfSize = Vector2.One * (BoardSize / 2);
		float totalBorder = BorderWidth + LineWidth;

		// Top border
		CreateBorderSegment(
			new Vector2(0, -halfSize.Y - totalBorder / 2),
			new Vector2(BoardSize + totalBorder * 2, totalBorder)
		);

		// Bottom border
		CreateBorderSegment(
			new Vector2(0, halfSize.Y + totalBorder / 2),
			new Vector2(BoardSize + totalBorder * 2, totalBorder)
		);

		// Left border
		CreateBorderSegment(
			new Vector2(-halfSize.X - totalBorder / 2, 0),
			new Vector2(totalBorder, BoardSize)
		);

		// Right border
		CreateBorderSegment(
			new Vector2(halfSize.X + totalBorder / 2, 0),
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
		Vector2 halfSize = Vector2.One * (BoardSize / 2);
		// Pockets are positioned at corners - C dimension (4.45cm) defines both diameter and edge-to-edge distance
		float pocketCenterOffset = PocketRadius; // Distance from board edge to pocket center
		
		_pocketPositions = new Vector2[]
		{
			new Vector2(-halfSize.X + pocketCenterOffset, -halfSize.Y + pocketCenterOffset), // Top-left corner
			new Vector2(halfSize.X - pocketCenterOffset, -halfSize.Y + pocketCenterOffset),  // Top-right corner
			new Vector2(-halfSize.X + pocketCenterOffset, halfSize.Y - pocketCenterOffset),  // Bottom-left corner
			new Vector2(halfSize.X - pocketCenterOffset, halfSize.Y - pocketCenterOffset)    // Bottom-right corner
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
		DrawInnerPlayingArea();
		DrawBaselines();
		DrawOfficialArrows(); // Official 4 arrows pointing toward pockets
		DrawCenterCircle();
		DrawPockets();
	}

	/// <summary>
	/// Draw the main board surface
	/// </summary>
	private void DrawBoard()
	{
		Vector2 halfSize = Vector2.One * (BoardSize / 2);
		Rect2 boardRect = new Rect2(-halfSize, Vector2.One * BoardSize);
		DrawRect(boardRect, BoardColor);
	}

	/// <summary>
	/// Draw board borders
	/// </summary>
	private void DrawBorders()
	{
		Vector2 fullSize = Vector2.One * BoardSize;
		Vector2 halfSize = Vector2.One * (BoardSize / 2);
		
		// Outer border
		Rect2 outerBorder = new Rect2(-halfSize - Vector2.One * BorderWidth, 
			fullSize + Vector2.One * BorderWidth * 2);
		DrawRect(outerBorder, EdgeColor);
		
		// Inner board surface (to create border effect)
		Rect2 innerBoard = new Rect2(-halfSize, fullSize);
		DrawRect(innerBoard, BoardColor);
		
		// Inner border line
		var points = new[] {
			new Vector2(-halfSize.X, -halfSize.Y),
			new Vector2(halfSize.X, -halfSize.Y),
			new Vector2(halfSize.X, halfSize.Y),
			new Vector2(-halfSize.X, halfSize.Y),
			new Vector2(-halfSize.X, -halfSize.Y)
		};
		for (int i = 0; i < points.Length - 1; i++)
		{
			DrawLine(points[i], points[i + 1], EdgeColor, LineWidth);
		}
	}

	/// <summary>
	/// Draw baselines using official 6-element system with correct F dimension positioning
	/// </summary>
	private void DrawBaselines()
	{
		// F dimension: 10.15cm from edge to EXTERIOR (thick) line of baseline
		float fDistance = BaselineDistance; // F: 10.15cm from frame to exterior thick line
		float baselineDiameter = BaselineCapRadius * 2; // Use official striker diameter (4.13cm max)
		
		// Calculate baseline positions with correct F dimension interpretation
		float halfSize = BoardSize / 2;
		
		// Exterior line positions (F distance from edge) - thick line towards walls
		float exteriorLineY = halfSize - fDistance;
		float exteriorLineX = halfSize - fDistance;
		
		// Interior line positions (F + striker diameter from edge) - thin line towards center
		float interiorLineY = halfSize - fDistance - baselineDiameter;
		float interiorLineX = halfSize - fDistance - baselineDiameter;
		
		// Baseline center positions (midpoint between exterior and interior lines)
		float centerY = (exteriorLineY + interiorLineY) / 2;
		float centerX = (exteriorLineX + interiorLineX) / 2;
		
		// Draw all 4 baselines with proper 6-element structure
		// Bottom baseline (player 0)
		DrawProperBaseline(new Vector2(0, centerY), true, 0, exteriorLineY, interiorLineY);
		
		// Top baseline (player 1)
		DrawProperBaseline(new Vector2(0, -centerY), true, 1, -exteriorLineY, -interiorLineY);
		
		// Left baseline (player 2)
		DrawProperBaseline(new Vector2(-centerX, 0), false, 2, -exteriorLineX, -interiorLineX);
		
		// Right baseline (player 3)
		DrawProperBaseline(new Vector2(centerX, 0), false, 3, exteriorLineX, interiorLineX);
	}

	/// <summary>
	/// Draw inner playing area defined by the 4 baseline inner lines (official specification)
	/// </summary>
	private void DrawInnerPlayingArea()
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
		// for (int i = 0; i < cornerPoints.Length; i += 2)
		// {
		// 	DrawLine(cornerPoints[i], cornerPoints[i + 1], LineColor, CornerLineThickness);
		// }
	}
	
	/// <summary>
	/// Draw center elements: L ring (piece containment) + K dot (queen position)
	/// </summary>
	private void DrawCenterCircle()
	{
		// Draw center ring using L dimension (ring that fits all pieces)
		DrawArc(Vector2.Zero, CenterRingRadius, 0, Mathf.Tau, 64, LineColor, LineWidth);
		
		// Draw center dot using K dimension (where queen goes)
		DrawCircle(Vector2.Zero, CenterDotRadius, LineColor);
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
			DrawCircle(pocketPos, PocketRadius, PocketColor);
			
			// Inner pocket hole (darker)
			DrawCircle(pocketPos, PocketRadius * POCKET_INNER_MULTIPLIER, PocketColor.Darkened(0.3f));
			
			// Highlight ring
			DrawArc(pocketPos, PocketRadius * POCKET_HIGHLIGHT_MULTIPLIER, 0, Mathf.Tau, 32, 
					PocketColor.Lightened(0.2f), 2.0f);
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
	/// Calculate actual endcap positions for a baseline using official D dimension
	/// </summary>
	private (Vector2 leftEndcap, Vector2 rightEndcap) CalculateEndcapPositions(Vector2 baselineCenter, bool isHorizontal)
	{
		// Official specification: "Two straight lines of 47 cm each in length... 
		// The base lines shall be closed by circles of 3.18 cm in diameter at both ends."
		// This means: 47cm = straight line length, endcaps are added at both ends
		
		float lineLength = BaselineLength; // 47cm straight line portion
		float endcapRadius = BaselineCapRadius; // 1.59cm radius (3.18cm diameter / 2)
		float halfLineLength = (lineLength / 2) - endcapRadius; // 23.5cm from center to line end
		float endcapCenterOffset = halfLineLength; // Position endcap center beyond line end
		
		Vector2 leftEndPos = baselineCenter + (isHorizontal ? new Vector2(-endcapCenterOffset, 0) : new Vector2(0, -endcapCenterOffset));
		Vector2 rightEndPos = baselineCenter + (isHorizontal ? new Vector2(endcapCenterOffset, 0) : new Vector2(0, endcapCenterOffset));
		
		// Debug output to verify calculations
		if (isHorizontal) // Only log for horizontal baselines to avoid spam
		{
			float centerToCenterDistance = leftEndPos.DistanceTo(rightEndPos);
			float totalVisualSpan = centerToCenterDistance + (endcapRadius * 2); // Add one radius on each side
			GD.Print($"[CarromBoard] Baseline calculation: LineLength={lineLength:F2}, EndcapRadius={endcapRadius:F2}");
			GD.Print($"[CarromBoard] Center-to-center={centerToCenterDistance:F2}, Total visual span={totalVisualSpan:F2} (should be ~{lineLength + endcapRadius * 2:F2})");
		}
		
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
		float halfLineLength = (BaselineLength / 2) - BaselineCapRadius; // 23.5cm from center to line end
		
		// Element 1: Exterior (thick) line (E dimension) - 3x corner line thickness (0.45cm) - towards walls  
		DrawBaselineOutsideLine(center, isHorizontal, halfLineLength, baselineIndex, exteriorLinePos);
		
		// Element 2: Interior (thin) line - same thickness as corner lines (0.15cm) - towards center
		DrawBaselineInsideLine(center, isHorizontal, halfLineLength, baselineIndex, interiorLinePos);
		
		// Elements 3 & 4: End cap rings positioned at the ends of the 47cm line
		DrawBaselineEndCapRing(leftEndPos);
		DrawBaselineEndCapRing(rightEndPos);
		
		// Elements 5 & 6: Solid circles (H dimension = 2.54cm) within end cap rings
		DrawBaselineEndCapSolidCircle(leftEndPos);
		DrawBaselineEndCapSolidCircle(rightEndPos);
	}
	
	/// <summary>
	/// Draw baseline exterior (thick) line (E dimension: 0.45cm thick) towards walls at specific position
	/// </summary>
	private void DrawBaselineOutsideLine(Vector2 center, bool isHorizontal, float halfLength, int baselineIndex, float linePosition)
	{
		if (isHorizontal)
		{
			// Horizontal baseline - use Y position
			Vector2 startPos = new Vector2(center.X - halfLength, linePosition);
			Vector2 endPos = new Vector2(center.X + halfLength, linePosition);
			DrawLine(startPos, endPos, LineColor, BaselineBottomLineThickness);
		}
		else
		{
			// Vertical baseline - use X position
			Vector2 startPos = new Vector2(linePosition, center.Y - halfLength);
			Vector2 endPos = new Vector2(linePosition, center.Y + halfLength);
			DrawLine(startPos, endPos, LineColor, BaselineBottomLineThickness);
		}
	}
	
	/// <summary>
	/// Draw baseline interior (thin) line (0.15cm thick) towards center at specific position
	/// </summary>
	private void DrawBaselineInsideLine(Vector2 center, bool isHorizontal, float halfLength, int baselineIndex, float linePosition)
	{
		if (isHorizontal)
		{
			// Horizontal baseline - use Y position
			Vector2 startPos = new Vector2(center.X - halfLength, linePosition);
			Vector2 endPos = new Vector2(center.X + halfLength, linePosition);
			DrawLine(startPos, endPos, LineColor, CornerLineThickness);
		}
		else
		{
			// Vertical baseline - use X position
			Vector2 startPos = new Vector2(linePosition, center.Y - halfLength);
			Vector2 endPos = new Vector2(linePosition, center.Y + halfLength);
			DrawLine(startPos, endPos, LineColor, CornerLineThickness);
		}
	}
	
	/// <summary>
	/// Draw end cap ring (diameter = striker width = 3.18cm)
	/// </summary>
	private void DrawBaselineEndCapRing(Vector2 center)
	{
		float ringRadius = BaselineCapRadius;
		DrawArc(center, ringRadius, 0, Mathf.Tau, 32, LineColor, BaselineBottomLineThickness);
	}
	
	/// <summary>
	/// Draw solid circle within end cap ring (H dimension: 2.54cm)
	/// </summary>
	private void DrawBaselineEndCapSolidCircle(Vector2 center)
	{
		DrawCircle(center, BaselineCapInnerRadius * .8f, LineColor);
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
		
		float boardHalfSize = BoardSize / 2;
		float arrowMaxLength = I_CORNER_LINE_LENGTH_CM * ScaleFactor; // 26.70cm max
		float pocketClearance = J_CORNER_LINE_POCKET_DISTANCE_CM * ScaleFactor; // 5.00cm from pocket
		
		// Calculate arrow positions - they start from positions between base circles
		// and point toward the 4 corner pockets at 45-degree angles
		
		// Arrow start positions - positioned between baselines, not on them
		Vector2[] arrowStarts = new[]
		{
			new Vector2(-boardHalfSize * 0.3f, -boardHalfSize * 0.3f), // Top-left quadrant
			new Vector2(boardHalfSize * 0.3f, -boardHalfSize * 0.3f),  // Top-right quadrant
			new Vector2(-boardHalfSize * 0.3f, boardHalfSize * 0.3f),  // Bottom-left quadrant
			new Vector2(boardHalfSize * 0.3f, boardHalfSize * 0.3f)    // Bottom-right quadrant
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
			DrawLine(arrowStart, arrowEnd, LineColor, CornerLineThickness);
			
			// Draw filled triangle tip pointing toward pocket
			DrawArrowTriangleTip(arrowEnd, arrowDirection, pocketPos);
			
			// Draw corner arc at the center-end (start position) of the arrow
			DrawCornerArrowArc(arrowStart, arrowDirection);
		}
	}

	/// <summary>
	/// Draw a filled equilateral triangle arrow tip pointing toward the target pocket
	/// </summary>
	private void DrawArrowTriangleTip(Vector2 arrowEnd, Vector2 arrowDirection, Vector2 pocketPos)
	{
		// N dimension from diagram is 2.54cm, triangle side length should be half of that = 1.27cm
		const float N_DIMENSION_CM = 2.54f; // From diagram
		float triangleSideLength = (N_DIMENSION_CM / 2) * ScaleFactor; // 1.27cm scaled
		
		// Direction from arrow end toward pocket (ensures tip points to pocket)
		Vector2 directionToPocket = (pocketPos - arrowEnd).Normalized();
		
		// Calculate equilateral triangle vertices
		// For equilateral triangle: height = side_length * sqrt(3) / 2
		float triangleHeight = triangleSideLength * Mathf.Sqrt(3) / 2;
		
		// Tip point (pointing toward pocket)
		Vector2 tipPoint = arrowEnd + directionToPocket * triangleHeight;
		
		// Base vertices (perpendicular to direction, equidistant from center)
		Vector2 perpendicular = directionToPocket.Rotated(Mathf.Pi / 2);
		Vector2 baseLeft = arrowEnd + perpendicular * (triangleSideLength / 2);
		Vector2 baseRight = arrowEnd - perpendicular * (triangleSideLength / 2);
		
		// Draw filled equilateral triangle
		Vector2[] trianglePoints = { tipPoint, baseLeft, baseRight };
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
		DrawArc(arcCenter, arcRadius, startAngle, endAngle, 32, LineColor, CornerLineThickness);
		
		// Draw triangle arrow tips at the two ends of the arc (where corner lines start)
		Vector2 arcStartPoint = arcCenter + new Vector2(Mathf.Cos(startAngle), Mathf.Sin(startAngle)) * arcRadius;
		Vector2 arcEndPoint = arcCenter + new Vector2(Mathf.Cos(endAngle), Mathf.Sin(endAngle)) * arcRadius;
		
		// Calculate tangent directions at arc endpoints to point toward board corners
		// Each triangle should point in the direction that extends toward the nearest board corner
		
		// Calculate base tangent directions (counter-clockwise)
		Vector2 baseTangentStart = new Vector2(-Mathf.Sin(startAngle), Mathf.Cos(startAngle));
		Vector2 baseTangentEnd = new Vector2(-Mathf.Sin(endAngle), Mathf.Cos(endAngle));
		
		// Determine which direction points more toward the corners
		float boardHalfSize = BoardSize / 2;
		Vector2 nearestCorner = new Vector2(
			arrowDirection.X > 0 ? boardHalfSize : -boardHalfSize,
			arrowDirection.Y > 0 ? boardHalfSize : -boardHalfSize
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
		DrawCornerLineTriangleTip(arcStartPoint, startTangent);
		DrawCornerLineTriangleTip(arcEndPoint, endTangent);
	}
	
	/// <summary>
	/// Draw a filled equilateral triangle arrow tip at corner line starts (same size as DrawArrowTriangleTip)
	/// </summary>
	private void DrawCornerLineTriangleTip(Vector2 position, Vector2 direction)
	{
		// Use same triangle size as DrawArrowTriangleTip: N dimension from diagram is 2.54cm, triangle side length should be half of that = 1.27cm
		const float N_DIMENSION_CM = 2.54f; // From diagram
		float triangleSideLength = (N_DIMENSION_CM / 2) * ScaleFactor; // 1.27cm scaled
		
		// Normalize direction vector
		Vector2 normalizedDirection = direction.Normalized();
		
		// Calculate equilateral triangle vertices
		// For equilateral triangle: height = side_length * sqrt(3) / 2
		float triangleHeight = triangleSideLength * Mathf.Sqrt(3) / 2;
		
		// For corner line triangles, we want the BASE at the arc endpoint (position parameter)
		// and the TIP pointing outward along the corner line direction
		
		// Base center is at the arc endpoint position
		Vector2 baseCenter = position;
		
		// Tip point extends outward from base center along the direction
		Vector2 tipPoint = baseCenter + normalizedDirection * triangleHeight;
		
		// Base vertices are perpendicular to direction, centered at the arc endpoint
		Vector2 perpendicular = normalizedDirection.Rotated(Mathf.Pi / 2);
		Vector2 baseLeft = baseCenter + perpendicular * (triangleSideLength / 2);
		Vector2 baseRight = baseCenter - perpendicular * (triangleSideLength / 2);
		
		// Draw filled equilateral triangle
		Vector2[] trianglePoints = { tipPoint, baseLeft, baseRight };
		DrawColoredPolygon(trianglePoints, LineColor);
	}
	

	/// <summary>
	/// Get baseline position for a player using dimension-aware calculations
	/// </summary>
	public Vector2 GetBaselinePosition(int playerIndex)
	{
		float halfSize = BoardSize / 2;
		
		return playerIndex switch
		{
			0 => new Vector2(0, halfSize - GetHorizontalBaselineDistance()),     // Bottom (Player 1)
			1 => new Vector2(0, -(halfSize - GetHorizontalBaselineDistance())), // Top (Player 2)  
			2 => new Vector2(-(halfSize - GetVerticalBaselineDistance()), 0),   // Left (Player 3)
			3 => new Vector2(halfSize - GetVerticalBaselineDistance(), 0),      // Right (Player 4)
			_ => new Vector2(0, halfSize - GetHorizontalBaselineDistance())     // Default to bottom
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
		Vector2 halfSize = Vector2.One * (BoardSize / 2);;
		return position.X >= -halfSize.X && position.X <= halfSize.X &&
			   position.Y >= -halfSize.Y && position.Y <= halfSize.Y;
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
	/// Get corner arrow start positions for external use
	/// </summary>
	public Vector2[] GetCornerArrowStarts()
	{
		return (Vector2[])_cornerArrowStarts.Clone();
	}

	/// <summary>
	/// Get corner arrow end positions for external use
	/// </summary>
	public Vector2[] GetCornerArrowEnds()
	{
		return (Vector2[])_cornerArrowEnds.Clone();
	}

	/// <summary>
	/// Get corner arrow start and end position for a specific arrow index (0-3)
	/// </summary>
	public (Vector2 start, Vector2 end) GetCornerArrow(int index)
	{
		if (index >= 0 && index < 4)
		{
			return (_cornerArrowStarts[index], _cornerArrowEnds[index]);
		}
		return (Vector2.Zero, Vector2.Zero);
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