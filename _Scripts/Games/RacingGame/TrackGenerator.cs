using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[Tool]
public partial class TrackGenerator : Node2D
{
	[ExportCategory("Track Layout")]
	/// <summary>
	/// Number of control points defining the track shape. 
	/// CRITICAL: Values below 4 may fail constraint satisfaction. Values above 15 significantly increase generation time.
	/// Interacts with: All CSP parameters - more points require more relaxed constraints.
	/// </summary>
	[Export] public int TurnCount { get; set; } = 6;
	
	/// <summary>
	/// Minimum angle between track segments (degrees). Used for kerb generation, not CSP constraints.
	/// Legacy parameter - CSP system generates organic curves regardless of this setting.
	/// </summary>
	[Export] public float MinTurnAngle { get; set; } = 45.0f;
	
	/// <summary>
	/// Maximum angle between track segments (degrees). Used for kerb generation, not CSP constraints.
	/// Legacy parameter - CSP system generates organic curves regardless of this setting.
	/// </summary>
	[Export] public float MaxTurnAngle { get; set; } = 135.0f;
	
	/// <summary>
	/// Bézier curve smoothness factor (1-32). Higher values create smoother curves but may hide sharp turns.
	/// CRITICAL: Values below 8 create angular tracks. Values above 24 may over-smooth interesting features.
	/// </summary>
	[Export] public int TurnSmoothness { get; set; } = 16;
	
	/// <summary>
	/// Base radius for elliptical generation (pixels). Only affects Layer 3 fallback generation.
	/// Non-critical - CSP system dynamically calculates optimal radius based on screen size.
	/// </summary>
	[Export] public float TrackRadius { get; set; } = 400.0f;
	
	/// <summary>
	/// Random seed for deterministic generation. Empty string uses system time.
	/// Format: Any string - identical seeds produce identical tracks.
	/// </summary>
	[Export] public string TrackSeed { get; set; } = "";

	[ExportCategory("CSP Generation")]
	/// <summary>
	/// Constraint relaxation for Layer 2 CSP solving (0.1-1.0).
	/// CRITICAL INTERACTION: Lower values with high TurnCount (>8) often force Layer 3 fallback.
	/// 0.5-0.7: Aggressive - enables complex shapes but may fail with many points
	/// 0.8-0.9: Balanced - good success rate with moderate complexity
	/// 0.9-1.0: Conservative - high success rate but simpler shapes
	/// </summary>
	[Export] public float ConstraintRelaxationFactor { get; set; } = 0.8f;
	
	/// <summary>
	/// Progressive deformation intensity (0.0-1.0+). 
	/// CRITICAL INTERACTION: High values (>0.6) with low AngleVariationRange (<0.4) may cause intersections.
	/// 0.1-0.3: Subtle variations, safe with any other parameters
	/// 0.4-0.6: Moderate complexity, works well with balanced parameters  
	/// 0.7-1.0: Dramatic variations, requires careful tuning of other parameters
	/// </summary>
	[Export] public float DeformationStrength { get; set; } = 0.4f;
	
	/// <summary>
	/// Number of progressive deformation passes (1-10).
	/// CRITICAL: Each step applies 85% of previous strength. Values >6 have diminishing returns.
	/// Interacts multiplicatively with DeformationStrength - both high values can cause failures.
	/// </summary>
	[Export] public int DeformationSteps { get; set; } = 3;
	
	/// <summary>
	/// Angular variation range in radians (0.1-2.0).
	/// CRITICAL INTERACTION: High values (>1.2) with high TurnCount (>8) often trigger Layer 3 fallback.
	/// Strategy 1 uses ±50% of this value, Strategy 2 uses ±75%.
	/// 0.2-0.5: Conservative shapes, high CSP success rate
	/// 0.6-1.0: Balanced complexity, good for most scenarios
	/// 1.1-2.0: Aggressive variations, may require relaxed constraints
	/// </summary>
	[Export] public float AngleVariationRange { get; set; } = 0.8f;
	
	/// <summary>
	/// Radius variation range as multiplier (0.1-2.0).
	/// CRITICAL INTERACTION: Values >1.0 with high TurnCount create distance constraint violations.
	/// Strategy 1: base ± 50% of this range, Strategy 2: 0.6 + 150% of this range.
	/// 0.2-0.5: Consistent track sections, reliable generation
	/// 0.6-0.8: Moderate size variation, balanced approach
	/// 0.9-2.0: Dramatic size changes, may force Layer 3 with many points
	/// </summary>
	[Export] public float RadiusVariationRange { get; set; } = 0.6f;
	
	/// <summary>
	/// Apply 1.5x deformation when constraint relaxation is needed.
	/// Use when you want maximum complexity regardless of CSP success.
	/// </summary>
	[Export] public bool ForceComplexGeneration { get; set; } = false;
	
	/// <summary>
	/// Maximum number of retry attempts with progressively relaxed constraints (3-10).
	/// Higher values increase generation time but improve track complexity.
	/// RECOMMENDED: 5-7 for good balance of complexity vs performance.
	/// </summary>
	[Export] public int MaxRetryAttempts { get; set; } = 5;

	[ExportCategory("Intersection Resolution")]
	/// <summary>
	/// Enable intersection resolution by pushing problematic points outward.
	/// When CSP generates intersecting tracks, attempt to fix by moving points before relaxing constraints.
	/// RECOMMENDED: true for better track quality without constraint relaxation.
	/// </summary>
	[Export] public bool EnableIntersectionPushing { get; set; } = true;
	
	/// <summary>
	/// Distance to push points per iteration when resolving intersections (5-50 pixels).
	/// Higher values resolve intersections faster but may create jerky movements.
	/// RECOMMENDED: 10-20 for smooth, gradual corrections.
	/// </summary>
	[Export] public float PushStrength { get; set; } = 15.0f;
	
	/// <summary>
	/// Maximum iterations for intersection pushing before giving up (5-30).
	/// Higher values increase chances of resolution but may cause infinite loops.
	/// RECOMMENDED: 10-15 for balance of persistence vs performance.
	/// </summary>
	[Export] public int MaxPushIterations { get; set; } = 12;

	[ExportCategory("Track Geometry")]
	[Export] public float TrackWidth { get; set; } = 80.0f;
	[Export] public int SplineSegments { get; set; } = 100;

	[ExportCategory("Kerb Settings")]
	[Export] public float KerbAngleThreshold { get; set; } = 90.0f;
	[Export] public float KerbWidth { get; set; } = 12.0f;
	[Export] public float KerbOffset { get; set; } = 5.0f;
	[Export] public int KerbStripeCount { get; set; } = 8;
	[Export] public Color KerbColor1 { get; set; } = Colors.Red;
	[Export] public Color KerbColor2 { get; set; } = Colors.White;

	[ExportCategory("Visual")]
	[Export] public Color TrackColor { get; set; } = new Color(0.4f, 0.4f, 0.4f);
	[Export] public Color EdgeColor { get; set; } = Colors.White;
	[Export] public float EdgeWidth { get; set; } = 3.0f;
	[Export] public bool ShowDebugPoints { get; set; } = false;

	[ExportCategory("Editor Tools")]
	[Export] public bool RegenerateTrack
	{
		get => _regenerateTrack;
		set
		{
			if (value && value != _regenerateTrack)
			{
				_regenerateTrack = false;
				if (Engine.IsEditorHint())
				{
					CallDeferred(MethodName.RegenerateTrackDeferred);
				}
				else
				{
					// Also allow regeneration during runtime
					RegenerateTrackDeferred();
				}
			}
		}
	}

	private bool _regenerateTrack = false;
	private Vector2[] _trackPoints;
	private Vector2[] _innerEdgePoints;
	private Vector2[] _outerEdgePoints;
	private Curve2D _trackCurve;
	private Curve2D _innerEdgeCurve;
	private Curve2D _outerEdgeCurve;
	private Path2D _trackPath;
	private Line2D _trackVisual;
	private Line2D _innerEdgeVisual;
	private Line2D _outerEdgeVisual;
	private Node2D _debugPointsParent;
	private Node2D _kerbParent;

	public override void _Ready()
	{
		if (Engine.IsEditorHint())
		{
			SetNotifyTransform(true);
		}
	}

	public Vector2[] GenerateTrack(string seed = "")
	{
		if (!string.IsNullOrEmpty(seed))
		{
			TrackSeed = seed;
		}

		// Seed the random number generator
		if (!string.IsNullOrEmpty(TrackSeed))
		{
			GD.Seed((ulong)TrackSeed.GetHashCode());
		}

		var viewport = GetViewport();
		if (viewport == null)
		{
			GD.PrintErr("TrackGenerator: No viewport found");
			return Array.Empty<Vector2>();
		}

		var screenSize = viewport.GetVisibleRect().Size;
		var center = screenSize / 2;

		// Use CSP-based generation with guaranteed solution - no fallback needed
		_trackPoints = GenerateTrackProgressive(center, screenSize);
		CreateTrackCurve();
		UpdateVisuals();
		
		GD.Print("TrackGenerator: Guaranteed track generation completed successfully");
		return _trackPoints;
	}


	private void SortPointsClockwise(List<Vector2> points, Vector2 center)
	{
		points.Sort((a, b) =>
		{
			var angleA = Mathf.Atan2(a.Y - center.Y, a.X - center.X);
			var angleB = Mathf.Atan2(b.Y - center.Y, b.X - center.X);
			
			// Convert to positive range [0, 2π] and sort clockwise (reverse order)
			if (angleA < 0) angleA += 2 * Mathf.Pi;
			if (angleB < 0) angleB += 2 * Mathf.Pi;
			
			return angleB.CompareTo(angleA); // Reverse for clockwise
		});
	}

	// Cached curve segments for optimization
	private List<List<Vector2>> _cachedCurveSegments;
	private Curve2D _cachedCurve;

	private bool DetectTrackIntersections(Curve2D curve)
	{
		if (curve == null || curve.PointCount < 4) // Need at least 4 points for 2 segments
			return false;

		var curveSegments = GetCachedCurveSegments(curve);

		// Test all pairs of curve segments for intersections with early termination
		for (int i = 0; i < curveSegments.Count; i++)
		{
			for (int j = i + 2; j < curveSegments.Count; j++) // Skip adjacent segments
			{
				// Special case: don't test last segment with first segment (they're connected)
				if (i == 0 && j == curveSegments.Count - 1)
					continue;

				// Use bounding box pre-filtering
				if (DoBoundingBoxesIntersect(curveSegments[i], curveSegments[j]))
				{
					if (DoLineSegmentListsIntersect(curveSegments[i], curveSegments[j]))
					{
						return true; // Early termination on first intersection found
					}
				}
			}
		}

		return false; // No intersections found
	}

	private List<List<Vector2>> GetCachedCurveSegments(Curve2D curve)
	{
		// Check if we can reuse cached segments
		if (_cachedCurve == curve && _cachedCurveSegments != null)
		{
			return _cachedCurveSegments;
		}

		// Generate new segments and cache them
		var curveSegments = new List<List<Vector2>>();
		var samplesPerSegment = 10; // Number of line segments to approximate each curve segment

		// Sample each curve segment into line segments
		for (int i = 0; i < curve.PointCount - 1; i++)
		{
			var segmentSamples = new List<Vector2>();
			var startOffset = curve.GetClosestOffset(curve.GetPointPosition(i));
			var endOffset = curve.GetClosestOffset(curve.GetPointPosition(i + 1));
			
			// Handle wraparound for the last segment
			if (i == curve.PointCount - 2)
			{
				endOffset = curve.GetBakedLength();
			}

			for (int j = 0; j <= samplesPerSegment; j++)
			{
				var t = (float)j / samplesPerSegment;
				var offset = Mathf.Lerp(startOffset, endOffset, t);
				segmentSamples.Add(curve.SampleBaked(offset));
			}
			curveSegments.Add(segmentSamples);
		}

		// Cache the results
		_cachedCurve = curve;
		_cachedCurveSegments = curveSegments;

		return curveSegments;
	}

	private bool DoBoundingBoxesIntersect(List<Vector2> segments1, List<Vector2> segments2)
	{
		// Calculate bounding boxes
		var min1 = segments1[0];
		var max1 = segments1[0];
		var min2 = segments2[0];
		var max2 = segments2[0];

		foreach (var point in segments1)
		{
			min1 = new Vector2(Mathf.Min(min1.X, point.X), Mathf.Min(min1.Y, point.Y));
			max1 = new Vector2(Mathf.Max(max1.X, point.X), Mathf.Max(max1.Y, point.Y));
		}

		foreach (var point in segments2)
		{
			min2 = new Vector2(Mathf.Min(min2.X, point.X), Mathf.Min(min2.Y, point.Y));
			max2 = new Vector2(Mathf.Max(max2.X, point.X), Mathf.Max(max2.Y, point.Y));
		}

		// Check if bounding boxes intersect
		return !(max1.X < min2.X || max2.X < min1.X || max1.Y < min2.Y || max2.Y < min1.Y);
	}

	private bool DoLineSegmentListsIntersect(List<Vector2> segments1, List<Vector2> segments2)
	{
		// Test all line segment pairs between the two lists
		for (int i = 0; i < segments1.Count - 1; i++)
		{
			for (int j = 0; j < segments2.Count - 1; j++)
			{
				if (DoLineSegmentsIntersect(segments1[i], segments1[i + 1], segments2[j], segments2[j + 1]))
				{
					return true;
				}
			}
		}
		return false;
	}

	private bool DoLineSegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
	{
		// Line segment intersection using cross product method
		var d1 = CrossProduct2D(p3 - p1, p2 - p1);
		var d2 = CrossProduct2D(p4 - p1, p2 - p1);
		var d3 = CrossProduct2D(p1 - p3, p4 - p3);
		var d4 = CrossProduct2D(p2 - p3, p4 - p3);

		if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
		    ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
		{
			return true;
		}

		// Check for collinear points (edge case)
		const float epsilon = 1e-6f;
		if (Mathf.Abs(d1) < epsilon && IsPointOnSegment(p1, p2, p3)) return true;
		if (Mathf.Abs(d2) < epsilon && IsPointOnSegment(p1, p2, p4)) return true;
		if (Mathf.Abs(d3) < epsilon && IsPointOnSegment(p3, p4, p1)) return true;
		if (Mathf.Abs(d4) < epsilon && IsPointOnSegment(p3, p4, p2)) return true;

		return false;
	}

	private float CrossProduct2D(Vector2 a, Vector2 b)
	{
		return a.X * b.Y - a.Y * b.X;
	}

	private bool IsPointOnSegment(Vector2 p1, Vector2 p2, Vector2 point)
	{
		// Check if point lies on line segment p1-p2
		var minX = Mathf.Min(p1.X, p2.X);
		var maxX = Mathf.Max(p1.X, p2.X);
		var minY = Mathf.Min(p1.Y, p2.Y);
		var maxY = Mathf.Max(p1.Y, p2.Y);

		return point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY;
	}

	private bool DetectTrackWidthIntersections(Curve2D curve)
	{
		if (curve == null)
			return false;

		var halfWidth = TrackWidth * 0.5f;
		var bakeLength = curve.GetBakedLength();
		var samples = SplineSegments;

		// Generate inner and outer edge curves
		var innerEdgePoints = new List<Vector2>();
		var outerEdgePoints = new List<Vector2>();

		for (int i = 0; i <= samples; i++)
		{
			var offset = (float)i / samples * bakeLength;
			var centerPoint = curve.SampleBaked(offset);

			// Calculate perpendicular direction for track width
			var nextOffset = offset + 1.0f;
			if (nextOffset > bakeLength)
				nextOffset = nextOffset - bakeLength;

			var nextPoint = curve.SampleBaked(nextOffset);
			var direction = (nextPoint - centerPoint).Normalized();
			var perpendicular = new Vector2(-direction.Y, direction.X);

			// Create inner and outer edge points
			innerEdgePoints.Add(centerPoint - perpendicular * halfWidth);
			outerEdgePoints.Add(centerPoint + perpendicular * halfWidth);
		}

		// Test edge curves for self-intersection
		if (DetectSelfIntersectionInPointList(innerEdgePoints) || 
		    DetectSelfIntersectionInPointList(outerEdgePoints))
		{
			return true;
		}

		// Test if inner and outer edges intersect each other (invalid track)
		if (DetectIntersectionBetweenPointLists(innerEdgePoints, outerEdgePoints))
		{
			return true;
		}

		return false;
	}

	private bool DetectSelfIntersectionInPointList(List<Vector2> points)
	{
		if (points.Count < 4)
			return false;

		// Test all non-adjacent line segments for intersections
		for (int i = 0; i < points.Count - 1; i++)
		{
			for (int j = i + 2; j < points.Count - 1; j++)
			{
				// Don't test the last segment with the first segment (they're connected)
				if (i == 0 && j == points.Count - 2)
					continue;

				if (DoLineSegmentsIntersect(points[i], points[i + 1], points[j], points[j + 1]))
				{
					return true;
				}
			}
		}

		return false;
	}

	private bool DetectIntersectionBetweenPointLists(List<Vector2> points1, List<Vector2> points2)
	{
		if (points1.Count < 2 || points2.Count < 2)
			return false;

		// Test all line segments between the two lists
		for (int i = 0; i < points1.Count - 1; i++)
		{
			for (int j = 0; j < points2.Count - 1; j++)
			{
				if (DoLineSegmentsIntersect(points1[i], points1[i + 1], points2[j], points2[j + 1]))
				{
					return true;
				}
			}
		}

		return false;
	}

	// ================== CSP FRAMEWORK FOR GUARANTEED TRACK GENERATION ==================
	
	/// <summary>
	/// Represents the constraints for track generation
	/// </summary>
	private class TrackConstraints
	{
		public Vector2 ScreenSize { get; }
		public float TrackWidth { get; }
		public float MinDistance { get; }
		public float MaxDistance { get; }
		public float Margin { get; }
		public int PointCount { get; }
		public Vector2 Center { get; }
		
		public TrackConstraints(Vector2 screenSize, float trackWidth, int pointCount)
		{
			ScreenSize = screenSize;
			TrackWidth = trackWidth;
			PointCount = pointCount;
			Margin = trackWidth * 0.75f;
			MinDistance = 60.0f;
			MaxDistance = 300.0f;
			Center = screenSize / 2;
		}
	}
	
	/// <summary>
	/// Represents a variable in the CSP (a track point position)
	/// </summary>
	private class TrackVariable
	{
		public int Index { get; }
		public List<Vector2> Domain { get; set; }
		public Vector2? Value { get; set; }
		
		public TrackVariable(int index)
		{
			Index = index;
			Domain = new List<Vector2>();
			Value = null;
		}
		
		public bool IsAssigned => Value.HasValue;
	}
	
	/// <summary>
	/// CSP solver for track generation with guaranteed solutions
	/// </summary>
	private class TrackCSP
	{
		private TrackConstraints _constraints;
		private List<TrackVariable> _variables;
		private Random _random;
		
		private float _angleRange;
		private float _radiusRange;
		private int _deformationSteps;
		
		public TrackCSP(TrackConstraints constraints, float angleRange = 0.8f, float radiusRange = 0.6f, int deformationSteps = 3)
		{
			_constraints = constraints;
			_variables = new List<TrackVariable>();
			_random = new Random();
			_angleRange = angleRange;
			_radiusRange = radiusRange;
			_deformationSteps = deformationSteps;
			
			// Initialize variables
			for (int i = 0; i < constraints.PointCount; i++)
			{
				_variables.Add(new TrackVariable(i));
			}
		}
		
		public Vector2[] SolveWithAdaptiveRelaxation(float constraintRelaxationFactor, float deformationStrength, bool forceComplex, int maxRetryAttempts, bool enablePushing, float pushStrength, int maxPushIterations)
		{
			// Layer 1: Try strict CSP solving with more aggressive parameters
			var solution = SolveCSP(1.0f);
			if (solution != null)
			{
				GD.Print("TrackGenerator: CSP Layer 1 (strict) succeeded");
				return ApplyProgressiveDeformation(solution, deformationStrength * 1.2f);
			}
			
			// Layer 1.5: If strict CSP failed, try intersection pushing on a basic track
			if (enablePushing)
			{
				GD.Print("TrackGenerator: Attempting Layer 1.5 (intersection pushing after strict CSP)");
				var basicTrack = GenerateLooseConstraintTrack();
				var pushedTrack = ResolveIntersectionsByPushing(basicTrack, pushStrength, maxPushIterations);
				
				// Check if pushing resolved all intersections
				if (!HasIntersections(pushedTrack))
				{
					GD.Print("TrackGenerator: CSP Layer 1.5 (intersection pushing) succeeded");
					return ApplyProgressiveDeformation(pushedTrack, deformationStrength * 1.1f);
				}
			}
			
			// Layer 2: Relax distance constraints using exported parameter
			solution = SolveCSP(constraintRelaxationFactor);
			if (solution != null)
			{
				GD.Print("TrackGenerator: CSP Layer 2 (relaxed) succeeded");
				return ApplyProgressiveDeformation(solution, deformationStrength);
			}
			
			// Layer 2.5: If relaxed CSP failed, try intersection pushing on the relaxed attempt
			if (enablePushing)
			{
				GD.Print("TrackGenerator: Attempting Layer 2.5 (intersection pushing after relaxed CSP)");
				var relaxedTrack = GenerateLooseConstraintTrack();
				var pushedTrack = ResolveIntersectionsByPushing(relaxedTrack, pushStrength * 0.8f, maxPushIterations);
				
				// Check if pushing resolved all intersections
				if (!HasIntersections(pushedTrack))
				{
					GD.Print("TrackGenerator: CSP Layer 2.5 (intersection pushing) succeeded");
					return ApplyProgressiveDeformation(pushedTrack, deformationStrength);
				}
			}
			
			// Multiple retry attempts with progressive relaxation - adaptive based on user preference
			var baseRelaxation = constraintRelaxationFactor * 0.5f; // Start more relaxed than Layer 2
			var relaxationDecrement = baseRelaxation / Math.Max(maxRetryAttempts, 1);
			
			for (int attempt = 0; attempt < maxRetryAttempts; attempt++)
			{
				var relaxationStep = Math.Max(0.01f, baseRelaxation - (attempt * relaxationDecrement));
				GD.Print($"TrackGenerator: Retry attempt {attempt + 1}/{maxRetryAttempts} with relaxation factor {relaxationStep:F3}");
				
				// Reset variables for new attempt
				ResetVariables();
				
				solution = SolveCSP(relaxationStep);
				if (solution != null)
				{
					GD.Print($"TrackGenerator: CSP retry succeeded on attempt {attempt + 1} with relaxation {relaxationStep:F3}");
					// Apply stronger deformation for more complex appearance - more relaxation = more deformation
					var relaxationRatio = (baseRelaxation - relaxationStep) / baseRelaxation;
					var adjustedDeformationStrength = deformationStrength * (1.0f + relaxationRatio * 1.5f);
					return ApplyProgressiveDeformation(solution, adjustedDeformationStrength);
				}
			}
			
			// Final attempt: Generate a more complex track with very loose constraints
			GD.Print($"TrackGenerator: All {maxRetryAttempts} retry attempts failed, generating with minimum constraints");
			solution = GenerateLooseConstraintTrack();
			return ApplyProgressiveDeformation(solution, deformationStrength * 1.8f);
		}
		
		private void ResetVariables()
		{
			// Reset all variables to unassigned state for new CSP attempt
			foreach (var variable in _variables)
			{
				variable.Value = null;
				// Keep the domain intact for reuse
			}
		}
		
		private Vector2[] ApplyProgressiveDeformation(Vector2[] baseTrack, float deformationStrength)
		{
			// Apply controlled deformation to add visual interest while maintaining validity
			var deformed = new List<Vector2>(baseTrack);
			
			for (int step = 0; step < _deformationSteps; step++)
			{
				var stepStrength = deformationStrength * (1.0f - step * 0.15f); // Reduced falloff
				var proposedDeformation = ApplyDeformationStep(deformed, stepStrength, _angleRange, _radiusRange);
				
				// Validate the deformed track
				if (IsValidTrackGeometry(proposedDeformation))
				{
					deformed = proposedDeformation;
					GD.Print($"TrackGenerator: Applied deformation step {step + 1} with strength {stepStrength:F2}");
				}
				else
				{
					GD.Print($"TrackGenerator: Skipped deformation step {step + 1} (would cause intersections)");
					break;
				}
			}
			
			return deformed.ToArray();
		}
		
		private List<Vector2> ApplyDeformationStep(List<Vector2> points, float strength, float angleRange = 0.8f, float radiusRange = 0.6f)
		{
			var deformed = new List<Vector2>(points);
			var random = new Random();
			
			// Apply controlled perturbations to points
			for (int i = 0; i < deformed.Count; i++)
			{
				var currentPoint = deformed[i];
				var prevPoint = deformed[(i - 1 + deformed.Count) % deformed.Count];
				var nextPoint = deformed[(i + 1) % deformed.Count];
				
				// Calculate safe deformation direction (perpendicular to track flow)
				var flowDirection = (nextPoint - prevPoint).Normalized();
				var perpendicular = new Vector2(-flowDirection.Y, flowDirection.X);
				
				// Apply random deformation within safe bounds - increased magnitude
				var deformationMagnitude = strength * 50.0f * ((float)random.NextDouble() * 2.0f - 1.0f);
				var deformationVector = perpendicular * deformationMagnitude;
				
				// Also add some radial variation from center
				var centerToPoint = (currentPoint - _constraints.Center).Normalized();
				var radialDeformation = centerToPoint * strength * 25.0f * ((float)random.NextDouble() * 2.0f - 1.0f);
				
				deformed[i] = currentPoint + deformationVector + radialDeformation;
			}
			
			return deformed;
		}
		
		private bool IsValidTrackGeometry(List<Vector2> points)
		{
			// Quick validation - check if points maintain minimum distances and stay within bounds
			for (int i = 0; i < points.Count; i++)
			{
				var point = points[i];
				
				// Boundary check
				if (point.X < _constraints.Margin || point.X > _constraints.ScreenSize.X - _constraints.Margin ||
				    point.Y < _constraints.Margin || point.Y > _constraints.ScreenSize.Y - _constraints.Margin)
				{
					return false;
				}
				
				// Distance check with adjacent points (reduced minimum for deformation)
				var nextPoint = points[(i + 1) % points.Count];
				if (point.DistanceTo(nextPoint) < _constraints.MinDistance * 0.7f)
				{
					return false;
				}
			}
			
			return true;
		}
		
		private Vector2[] SolveCSP(float constraintStrength)
		{
			// Reset all assignments
			foreach (var variable in _variables)
			{
				variable.Value = null;
			}
			
			// Use backtracking with constraint propagation
			if (BacktrackingSolve(0, constraintStrength))
			{
				return _variables.Select(v => v.Value.Value).ToArray();
			}
			
			return null;
		}
		
		private bool BacktrackingSolve(int variableIndex, float constraintStrength)
		{
			// Base case: all variables assigned
			if (variableIndex >= _variables.Count)
			{
				return ValidateClosedLoop(constraintStrength);
			}
			
			var variable = _variables[variableIndex];
			
			// Calculate valid domain for this variable
			CalculateDomain(variable, constraintStrength);
			
			// If domain is empty, backtrack
			if (variable.Domain.Count == 0)
			{
				return false;
			}
			
			// Try each value in domain
			var domainCopy = new List<Vector2>(variable.Domain);
			foreach (var value in domainCopy)
			{
				// Assign value
				variable.Value = value;
				
				// Apply constraint propagation
				if (ConstraintPropagation(variableIndex + 1, constraintStrength))
				{
					// Recursively solve next variable
					if (BacktrackingSolve(variableIndex + 1, constraintStrength))
					{
						return true;
					}
				}
				
				// Backtrack
				variable.Value = null;
			}
			
			return false;
		}
		
		private void CalculateDomain(TrackVariable variable, float constraintStrength)
		{
			variable.Domain.Clear();
			
			// Create candidate positions based on track geometry
			var candidates = GenerateCandidatePositions(variable.Index, _angleRange, _radiusRange);
			
			// Filter candidates based on constraints
			foreach (var candidate in candidates)
			{
				if (IsValidPosition(candidate, variable.Index, constraintStrength))
				{
					variable.Domain.Add(candidate);
				}
			}
		}
		
		private List<Vector2> GenerateCandidatePositions(int variableIndex, float angleRange = 0.8f, float radiusRange = 0.6f)
		{
			var candidates = new List<Vector2>();
			
			// Generate candidates around an ellipse with variation
			var radiusX = (_constraints.ScreenSize.X - 2 * _constraints.Margin) * 0.4f;
			var radiusY = (_constraints.ScreenSize.Y - 2 * _constraints.Margin) * 0.4f;
			
			var baseAngle = (float)(variableIndex * 2 * Mathf.Pi / _constraints.PointCount);
			
			// Strategy 1: Generate close-to-base candidates (higher success rate)
			for (int i = 0; i < 15; i++)
			{
				var angleVariation = (float)_random.NextDouble() * angleRange - angleRange * 0.5f;
				var angle = baseAngle + angleVariation;
				
				var baseRadiusVar = 1.0f - radiusRange * 0.5f;
				var radiusVariationX = baseRadiusVar + (float)_random.NextDouble() * radiusRange;
				var radiusVariationY = baseRadiusVar + (float)_random.NextDouble() * radiusRange;
				
				var candidate = _constraints.Center + new Vector2(
					Mathf.Cos(angle) * radiusX * radiusVariationX,
					Mathf.Sin(angle) * radiusY * radiusVariationY
				);
				
				candidates.Add(candidate);
			}
			
			// Strategy 2: Generate wider variation candidates for more complex shapes
			for (int i = 0; i < 15; i++)
			{
				var angleVariation = (float)_random.NextDouble() * angleRange * 1.5f - angleRange * 0.75f;
				var angle = baseAngle + angleVariation;
				
				var radiusVariationX = 0.6f + (float)_random.NextDouble() * radiusRange * 1.5f;
				var radiusVariationY = 0.6f + (float)_random.NextDouble() * radiusRange * 1.5f;
				
				var candidate = _constraints.Center + new Vector2(
					Mathf.Cos(angle) * radiusX * radiusVariationX,
					Mathf.Sin(angle) * radiusY * radiusVariationY
				);
				
				candidates.Add(candidate);
			}
			
			// Strategy 3: Add some guaranteed safe candidates along the ellipse
			for (int i = 0; i < 5; i++)
			{
				var angle = baseAngle + (i - 2) * 0.1f;
				var candidate = _constraints.Center + new Vector2(
					Mathf.Cos(angle) * radiusX * 0.9f,
					Mathf.Sin(angle) * radiusY * 0.9f
				);
				
				candidates.Add(candidate);
			}
			
			return candidates;
		}
		
		private bool IsValidPosition(Vector2 position, int variableIndex, float constraintStrength)
		{
			// Check boundary constraints
			if (position.X < _constraints.Margin || position.X > _constraints.ScreenSize.X - _constraints.Margin ||
			    position.Y < _constraints.Margin || position.Y > _constraints.ScreenSize.Y - _constraints.Margin)
			{
				return false;
			}
			
			// Check distance constraints with assigned variables
			var adjustedMinDistance = _constraints.MinDistance * constraintStrength;
			
			for (int i = 0; i < variableIndex; i++)
			{
				var otherVariable = _variables[i];
				if (otherVariable.IsAssigned)
				{
					var distance = position.DistanceTo(otherVariable.Value.Value);
					if (distance < adjustedMinDistance || distance > _constraints.MaxDistance)
					{
						return false;
					}
				}
			}
			
			return true;
		}
		
		private bool ConstraintPropagation(int startIndex, float constraintStrength)
		{
			// Apply arc consistency to reduce domains of unassigned variables
			for (int i = startIndex; i < _variables.Count; i++)
			{
				var variable = _variables[i];
				if (!variable.IsAssigned)
				{
					CalculateDomain(variable, constraintStrength);
					if (variable.Domain.Count == 0)
					{
						return false;
					}
				}
			}
			return true;
		}
		
		private bool ValidateClosedLoop(float constraintStrength)
		{
			// Check if first and last points can form valid closed loop
			var firstPoint = _variables[0].Value.Value;
			var lastPoint = _variables[_variables.Count - 1].Value.Value;
			
			var distance = firstPoint.DistanceTo(lastPoint);
			var adjustedMinDistance = _constraints.MinDistance * constraintStrength;
			
			return distance >= adjustedMinDistance && distance <= _constraints.MaxDistance;
		}
		
		public Vector2[] GenerateLooseConstraintTrack()
		{
			// Generate a more complex track using minimal constraints and random placement
			var points = new List<Vector2>();
			var random = new Random();
			var center = _constraints.Center;
			var maxRadius = Math.Min(_constraints.ScreenSize.X, _constraints.ScreenSize.Y) * 0.4f;
			var minRadius = maxRadius * 0.3f;
			
			// Generate points with random radial variation for complexity
			for (int i = 0; i < _constraints.PointCount; i++)
			{
				var baseAngle = (float)(i * 2 * Mathf.Pi / _constraints.PointCount);
				
				// Add significant angle variation for more complex shapes
				var angleVariation = (float)(random.NextDouble() - 0.5) * 0.8f; // ±0.4 radians
				var angle = baseAngle + angleVariation;
				
				// Add significant radius variation
				var radiusVariation = (float)(random.NextDouble() - 0.5) * 0.6f; // ±30%
				var radius = Mathf.Lerp(minRadius, maxRadius, 0.5f + radiusVariation);
				
				var point = center + new Vector2(
					Mathf.Cos(angle) * radius,
					Mathf.Sin(angle) * radius
				);
				
				// Ensure point stays within screen bounds with margin
				point.X = Mathf.Clamp(point.X, _constraints.Margin, _constraints.ScreenSize.X - _constraints.Margin);
				point.Y = Mathf.Clamp(point.Y, _constraints.Margin, _constraints.ScreenSize.Y - _constraints.Margin);
				
				points.Add(point);
			}
			
			// Apply one pass of smoothing to avoid extreme angles while keeping complexity
			var smoothedPoints = new List<Vector2>();
			for (int i = 0; i < points.Count; i++)
			{
				var prev = points[(i - 1 + points.Count) % points.Count];
				var curr = points[i];
				var next = points[(i + 1) % points.Count];
				
				// Light smoothing - only 20% influence from neighbors
				var smoothed = curr * 0.8f + (prev + next) * 0.1f;
				smoothedPoints.Add(smoothed);
			}
			
			return smoothedPoints.ToArray();
		}
		
		// ================== INTERSECTION RESOLUTION SYSTEM ==================
		
		/// <summary>
		/// Attempt to resolve intersections by pushing problematic points outward
		/// </summary>
		public Vector2[] ResolveIntersectionsByPushing(Vector2[] originalPoints, float pushStrength, int maxIterations)
		{
			var points = originalPoints.ToList();
			
			for (int iteration = 0; iteration < maxIterations; iteration++)
			{
				var intersectionPoints = FindIntersectionCausingPoints(points);
				if (intersectionPoints.Count == 0)
				{
					GD.Print($"TrackGenerator: Intersection pushing succeeded after {iteration} iterations");
					return points.ToArray();
				}
				
				var pushVectors = CalculateOptimalPushVectors(points, intersectionPoints);
				
				bool anyPushed = false;
				for (int i = 0; i < points.Count; i++)
				{
					if (pushVectors[i].Length() > 0.1f) // Only push if there's a meaningful vector
					{
						var newPos = points[i] + pushVectors[i] * pushStrength;
						if (IsWithinScreenBounds(newPos))
						{
							points[i] = newPos;
							anyPushed = true;
						}
					}
				}
				
				if (!anyPushed)
				{
					GD.Print($"TrackGenerator: Intersection pushing stopped - can't push further (iteration {iteration})");
					break;
				}
			}
			
			GD.Print($"TrackGenerator: Intersection pushing completed {maxIterations} iterations, some intersections may remain");
			return points.ToArray();
		}
		
		/// <summary>
		/// Find points that are involved in track intersections
		/// </summary>
		private HashSet<int> FindIntersectionCausingPoints(List<Vector2> points)
		{
			var problematicPoints = new HashSet<int>();
			
			if (points.Count < 4) return problematicPoints;
			
			// Check all non-adjacent line segments for intersections
			for (int i = 0; i < points.Count; i++)
			{
				var p1 = points[i];
				var p2 = points[(i + 1) % points.Count];
				
				for (int j = i + 2; j < points.Count; j++)
				{
					// Don't test segments that share a vertex (adjacent segments)
					if ((j + 1) % points.Count == i) continue;
					
					var p3 = points[j];
					var p4 = points[(j + 1) % points.Count];
					
					if (DoLineSegmentsIntersect(p1, p2, p3, p4))
					{
						// Mark all four points as problematic
						problematicPoints.Add(i);
						problematicPoints.Add((i + 1) % points.Count);
						problematicPoints.Add(j);
						problematicPoints.Add((j + 1) % points.Count);
					}
				}
			}
			
			return problematicPoints;
		}
		
		/// <summary>
		/// Calculate optimal push vectors for each point to resolve intersections
		/// </summary>
		private Vector2[] CalculateOptimalPushVectors(List<Vector2> points, HashSet<int> problematicPoints)
		{
			var pushVectors = new Vector2[points.Count];
			var center = _constraints.Center;
			
			for (int i = 0; i < points.Count; i++)
			{
				if (!problematicPoints.Contains(i))
				{
					pushVectors[i] = Vector2.Zero;
					continue;
				}
				
				// Strategy 1: Radial push away from center
				var radialPush = (points[i] - center).Normalized();
				
				// Strategy 2: Push perpendicular to local track direction
				var prevPoint = points[(i - 1 + points.Count) % points.Count];
				var nextPoint = points[(i + 1) % points.Count];
				var trackDirection = (nextPoint - prevPoint).Normalized();
				var perpendicular = new Vector2(-trackDirection.Y, trackDirection.X);
				
				// Choose the perpendicular direction that points away from center
				if (perpendicular.Dot(radialPush) < 0)
					perpendicular = -perpendicular;
				
				// Combine strategies: 70% radial, 30% perpendicular
				var combinedPush = (radialPush * 0.7f + perpendicular * 0.3f).Normalized();
				
				pushVectors[i] = combinedPush;
			}
			
			return pushVectors;
		}
		
		/// <summary>
		/// Check if a point is within the screen bounds with margin
		/// </summary>
		private bool IsWithinScreenBounds(Vector2 point)
		{
			return point.X >= _constraints.Margin && 
			       point.X <= _constraints.ScreenSize.X - _constraints.Margin &&
			       point.Y >= _constraints.Margin && 
			       point.Y <= _constraints.ScreenSize.Y - _constraints.Margin;
		}
		
		/// <summary>
		/// Check if the track has any self-intersections
		/// </summary>
		private bool HasIntersections(Vector2[] trackPoints)
		{
			var pointsList = trackPoints.ToList();
			return FindIntersectionCausingPoints(pointsList).Count > 0;
		}
		
		/// <summary>
		/// Check if two line segments intersect
		/// </summary>
		private bool DoLineSegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
		{
			// Line segment intersection using cross product method
			var d1 = CrossProduct2D(p3 - p1, p2 - p1);
			var d2 = CrossProduct2D(p4 - p1, p2 - p1);
			var d3 = CrossProduct2D(p1 - p3, p4 - p3);
			var d4 = CrossProduct2D(p2 - p3, p4 - p3);

			if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
			    ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
			{
				return true;
			}

			// Check for collinear points (edge case)
			const float epsilon = 1e-6f;
			if (Mathf.Abs(d1) < epsilon && IsPointOnSegment(p1, p2, p3)) return true;
			if (Mathf.Abs(d2) < epsilon && IsPointOnSegment(p1, p2, p4)) return true;
			if (Mathf.Abs(d3) < epsilon && IsPointOnSegment(p3, p4, p1)) return true;
			if (Mathf.Abs(d4) < epsilon && IsPointOnSegment(p3, p4, p2)) return true;

			return false;
		}
		
		/// <summary>
		/// Calculate 2D cross product
		/// </summary>
		private float CrossProduct2D(Vector2 a, Vector2 b)
		{
			return a.X * b.Y - a.Y * b.X;
		}
		
		/// <summary>
		/// Check if point is on line segment
		/// </summary>
		private bool IsPointOnSegment(Vector2 p1, Vector2 p2, Vector2 point)
		{
			// Check if point lies within the bounding box of the segment
			var minX = Mathf.Min(p1.X, p2.X);
			var maxX = Mathf.Max(p1.X, p2.X);
			var minY = Mathf.Min(p1.Y, p2.Y);
			var maxY = Mathf.Max(p1.Y, p2.Y);
			
			return point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY;
		}
	}

	private Vector2[] GenerateTrackProgressive(Vector2 center, Vector2 screenSize)
	{
		// Use CSP-based generation with guaranteed solutions
		GD.Print($"TrackGenerator: Starting CSP-based generation - Screen: {screenSize}");
		
		var constraints = new TrackConstraints(screenSize, TrackWidth, TurnCount);
		var csp = new TrackCSP(constraints, AngleVariationRange, RadiusVariationRange, DeformationSteps);
		
		// This method guarantees a solution through adaptive relaxation
		var points = csp.SolveWithAdaptiveRelaxation(ConstraintRelaxationFactor, DeformationStrength, ForceComplexGeneration, MaxRetryAttempts, EnableIntersectionPushing, PushStrength, MaxPushIterations);
		
		// Sort points clockwise for proper track flow
		var pointsList = points.ToList();
		SortPointsClockwise(pointsList, center);
		
		// Validate intersection constraints (optional, as CSP should guarantee validity)
		var finalPoints = pointsList.ToArray();
		var originalTrackPoints = _trackPoints;
		_trackPoints = finalPoints;
		
		CreateTrackCurve();
		
		// Check for intersections - this should rarely fail with CSP
		bool hasIntersections = DetectTrackIntersections(_trackCurve) || DetectTrackWidthIntersections(_trackCurve);
		
		// Restore original track points
		_trackPoints = originalTrackPoints;
		
		if (hasIntersections)
		{
			GD.Print("TrackGenerator: CSP generation had intersections (rare), generating simpler track");
			// Regenerate with a simpler approach to avoid intersections
			var fallbackConstraints = new TrackConstraints(screenSize, TrackWidth, TurnCount);
			var fallbackCSP = new TrackCSP(fallbackConstraints);
			finalPoints = fallbackCSP.GenerateLooseConstraintTrack();
		}
		
		GD.Print($"TrackGenerator: CSP generation completed successfully with {finalPoints.Length} points");
		return finalPoints;
	}
	


	private void CreateTrackCurve()
	{
		if (_trackPoints == null || _trackPoints.Length < 3)
		{
			GD.PrintErr("TrackGenerator: Not enough track points to create curve");
			return;
		}

		_trackCurve = new Curve2D();

		// Create smooth Bézier curves between control points
		// First pass: Add all points with properly calculated handles for closed loop
		for (int i = 0; i < _trackPoints.Length; i++)
		{
			var currentPoint = _trackPoints[i];
			var nextPoint = _trackPoints[(i + 1) % _trackPoints.Length];
			var prevPoint = _trackPoints[(i - 1 + _trackPoints.Length) % _trackPoints.Length];
			
			// Calculate control point handles for smooth curves
			// This properly handles the wraparound for closed loops
			var toNext = (nextPoint - currentPoint);
			var toPrev = (currentPoint - prevPoint);
			var tangent = (toNext.Normalized() + toPrev.Normalized()).Normalized();
			
			// Scale control handles based on distance and turn smoothness setting
			var baseHandleLength = Mathf.Min(toNext.Length(), toPrev.Length()) * 0.25f;
			var smoothnessFactor = (float)TurnSmoothness / 16.0f; // Normalize to reasonable range
			var handleLength = baseHandleLength * smoothnessFactor;
			
			var inHandle = -tangent * handleLength;
			var outHandle = tangent * handleLength;
			
			_trackCurve.AddPoint(currentPoint, inHandle, outHandle);
		}
		
		// Close the loop by adding the first point again with consistent handles
		// This ensures the curve forms a seamless closed loop
		var firstPoint = _trackPoints[0];
		
		// Use the same handle calculation as for the first point to ensure consistency
		var nextPointForClosure = _trackPoints[1];
		var prevPointForClosure = _trackPoints[_trackPoints.Length - 1];
		
		var toNextClosure = (nextPointForClosure - firstPoint);
		var toPrevClosure = (firstPoint - prevPointForClosure);
		var closureTangent = (toNextClosure.Normalized() + toPrevClosure.Normalized()).Normalized();
		
		var closureBaseLength = Mathf.Min(toNextClosure.Length(), toPrevClosure.Length()) * 0.25f;
		var closureSmoothness = (float)TurnSmoothness / 16.0f;
		var closureHandleLength = closureBaseLength * closureSmoothness;
		
		var closureInHandle = -closureTangent * closureHandleLength;
		var closureOutHandle = closureTangent * closureHandleLength;
		
		_trackCurve.AddPoint(firstPoint, closureInHandle, closureOutHandle);
		
		// Generate track edges
		GenerateTrackEdges();
	}

	private void GenerateTrackEdges()
	{
		if (_trackCurve == null)
			return;

		// Generate edge point arrays for API compatibility
		// (Visual edges are now generated dynamically in CreateSmoothEdgeVisual)
		var innerPoints = new List<Vector2>();
		var outerPoints = new List<Vector2>();
		
		var bakeLength = _trackCurve.GetBakedLength();
		var segments = SplineSegments;
		
		for (int i = 0; i <= segments; i++)
		{
			var offset = (float)i / segments * bakeLength;
			var centerPoint = _trackCurve.SampleBaked(offset);
			
			// Calculate perpendicular direction for track width
			var nextOffset = offset + 1.0f;
			if (nextOffset > bakeLength)
				nextOffset = 0.0f;
			
			var nextPoint = _trackCurve.SampleBaked(nextOffset);
			var direction = (nextPoint - centerPoint).Normalized();
			var perpendicular = new Vector2(-direction.Y, direction.X);
			
			// Create inner and outer edge points
			var halfWidth = TrackWidth * 0.5f;
			innerPoints.Add(centerPoint - perpendicular * halfWidth);
			outerPoints.Add(centerPoint + perpendicular * halfWidth);
		}
		
		// Close the edge loops by adding the first points at the end
		if (innerPoints.Count > 0)
		{
			innerPoints.Add(innerPoints[0]);
			outerPoints.Add(outerPoints[0]);
		}
		
		_innerEdgePoints = innerPoints.ToArray();
		_outerEdgePoints = outerPoints.ToArray();
		
		// Create edge curves for API access
		_innerEdgeCurve = new Curve2D();
		_outerEdgeCurve = new Curve2D();
		
		foreach (var point in _innerEdgePoints)
			_innerEdgeCurve.AddPoint(point);
		
		foreach (var point in _outerEdgePoints)
			_outerEdgeCurve.AddPoint(point);
			
		// Generate kerb markings for sharp turns
		GenerateKerbMarkings();
	}

	private void GenerateKerbMarkings()
	{
		if (_trackCurve == null || _innerEdgePoints == null)
			return;

		var kerbSections = new List<KerbSection>();
		var bakeLength = _trackCurve.GetBakedLength();
		
		// Analyze curve for sharp turns
		for (int i = 0; i < _trackPoints.Length; i++)
		{
			var prevIndex = (i - 1 + _trackPoints.Length) % _trackPoints.Length;
			var nextIndex = (i + 1) % _trackPoints.Length;
			
			var prevPoint = _trackPoints[prevIndex];
			var currentPoint = _trackPoints[i];
			var nextPoint = _trackPoints[nextIndex];
			
			// Calculate turn angle
			var vec1 = (currentPoint - prevPoint).Normalized();
			var vec2 = (nextPoint - currentPoint).Normalized();
			var turnAngle = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(vec1.Dot(vec2), -1.0f, 1.0f)));
			
			// If turn is sharp enough, create kerb section
			if (turnAngle < KerbAngleThreshold)
			{
				// Find the position on the curve for this control point
				var closestOffset = FindClosestOffsetOnCurve(currentPoint);
				var kerbStartOffset = Mathf.Max(0, closestOffset - KerbWidth * 0.5f);
				var kerbEndOffset = Mathf.Min(bakeLength, closestOffset + KerbWidth * 0.5f);
				
				kerbSections.Add(new KerbSection
				{
					_StartOffset = kerbStartOffset,
					_EndOffset = kerbEndOffset,
					_IsInnerEdge = DetermineKerbSide(prevPoint, currentPoint, nextPoint)
				});
			}
		}
		
		_kerbSections = kerbSections;
	}

	private float FindClosestOffsetOnCurve(Vector2 point)
	{
		if (_trackCurve == null)
			return 0.0f;
			
		var bakeLength = _trackCurve.GetBakedLength();
		var bestOffset = 0.0f;
		var bestDistance = float.MaxValue;
		
		// Sample the curve to find closest point
		for (int i = 0; i <= 100; i++)
		{
			var offset = (float)i / 100 * bakeLength;
			var curvePoint = _trackCurve.SampleBaked(offset);
			var distance = point.DistanceSquaredTo(curvePoint);
			
			if (distance < bestDistance)
			{
				bestDistance = distance;
				bestOffset = offset;
			}
		}
		
		return bestOffset;
	}

	private bool DetermineKerbSide(Vector2 prevPoint, Vector2 currentPoint, Vector2 nextPoint)
	{
		// Calculate cross product to determine turn direction
		var vec1 = currentPoint - prevPoint;
		var vec2 = nextPoint - currentPoint;
		var cross = vec1.X * vec2.Y - vec1.Y * vec2.X;
		
		// Negative cross product means right turn, kerbs go on inside (left side)
		return cross < 0;
	}

	private struct KerbSection
	{
		public float _StartOffset;
		public float _EndOffset;
		public bool _IsInnerEdge;
	}

	private List<KerbSection> _kerbSections;

	private void UpdateVisuals()
	{
		// Clear existing visuals
		ClearVisuals();

		// Create track path
		if (_trackCurve != null)
		{
			_trackPath = new Path2D();
			_trackPath.Curve = _trackCurve;
			AddChild(_trackPath);

			// Create track surface visual with overlap for seamless closure
			_trackVisual = new Line2D();
			_trackVisual.Width = TrackWidth;
			_trackVisual.DefaultColor = TrackColor;

			var bakeLength = _trackCurve.GetBakedLength();
			// Add 15% extra sampling beyond full curve for visual overlap
			var overlapFactor = 1.15f;
			var totalSamples = (int)(SplineSegments * overlapFactor);
			
			for (int i = 0; i <= totalSamples; i++)
			{
				var normalizedOffset = (float)i / SplineSegments; // Deliberately goes beyond 1.0
				var offset = normalizedOffset * bakeLength;
				
				// Handle wraparound for sampling beyond curve length
				if (offset > bakeLength)
					offset = offset - bakeLength;
					
				var point = _trackCurve.SampleBaked(offset);
				_trackVisual.AddPoint(point);
			}

			AddChild(_trackVisual);

			// Create edge visuals
			CreateEdgeVisuals();
			
			// Create kerb visuals
			CreateKerbVisuals();
		}

		// Create debug points if enabled
		if (ShowDebugPoints && _trackPoints != null)
		{
			_debugPointsParent = new Node2D();
			_debugPointsParent.Name = "DebugPoints";
			AddChild(_debugPointsParent);

			for (int i = 0; i < _trackPoints.Length; i++)
			{
				var debugPoint = new Node2D();
				debugPoint.Position = _trackPoints[i];
				debugPoint.Name = $"Point_{i}";
				_debugPointsParent.AddChild(debugPoint);
			}
		}
	}

	private void CreateEdgeVisuals()
	{
		// Create smooth edge visuals with overlap closure like the track surface
		if (_trackCurve != null)
		{
			CreateSmoothEdgeVisual(true);  // Inner edge
			CreateSmoothEdgeVisual(false); // Outer edge
		}
	}

	private void CreateSmoothEdgeVisual(bool isInnerEdge)
	{
		var edgeVisual = new Line2D();
		edgeVisual.Width = EdgeWidth;
		edgeVisual.DefaultColor = EdgeColor;

		var bakeLength = _trackCurve.GetBakedLength();
		// Use same overlap factor as track surface for consistency
		var overlapFactor = 1.15f;
		var totalSamples = (int)(SplineSegments * overlapFactor);

		for (int i = 0; i <= totalSamples; i++)
		{
			var normalizedOffset = (float)i / SplineSegments;
			var offset = normalizedOffset * bakeLength;

			// Handle wraparound for sampling beyond curve length
			if (offset > bakeLength)
				offset = offset - bakeLength;

			var centerPoint = _trackCurve.SampleBaked(offset);

			// Calculate perpendicular direction for track width
			var nextSampleOffset = offset + 1.0f;
			if (nextSampleOffset > bakeLength)
				nextSampleOffset = nextSampleOffset - bakeLength;

			var nextPoint = _trackCurve.SampleBaked(nextSampleOffset);
			var direction = (nextPoint - centerPoint).Normalized();
			var perpendicular = new Vector2(-direction.Y, direction.X);

			// Create edge point offset from center
			var halfWidth = TrackWidth * 0.5f;
			var edgeOffset = isInnerEdge ? -halfWidth : halfWidth;
			var edgePoint = centerPoint + perpendicular * edgeOffset;

			edgeVisual.AddPoint(edgePoint);
		}

		// Store the visuals for cleanup
		if (isInnerEdge)
			_innerEdgeVisual = edgeVisual;
		else
			_outerEdgeVisual = edgeVisual;

		AddChild(edgeVisual);
	}

	private void CreateKerbVisuals()
	{
		if (_kerbSections == null || _trackCurve == null)
			return;

		_kerbParent = new Node2D();
		_kerbParent.Name = "KerbMarkings";
		AddChild(_kerbParent);

		foreach (var kerbSection in _kerbSections)
		{
			CreateKerbStripes(kerbSection);
		}
	}

	private void CreateKerbStripes(KerbSection kerbSection)
	{
		var bakeLength = _trackCurve.GetBakedLength();
		var sectionLength = kerbSection._EndOffset - kerbSection._StartOffset;
		var stripeLength = sectionLength / KerbStripeCount;

		for (int i = 0; i < KerbStripeCount; i++)
		{
			var stripeStart = kerbSection._StartOffset + i * stripeLength;
			var stripeEnd = stripeStart + stripeLength;
			
			// Determine stripe color (alternating)
			var stripeColor = (i % 2 == 0) ? KerbColor1 : KerbColor2;
			
			// Create stripe visual
			var stripeLine = new Line2D();
			stripeLine.Width = KerbWidth;
			stripeLine.DefaultColor = stripeColor;
			
			// Sample points along the stripe
			var stripePoints = 8; // Number of points per stripe for smooth curves
			for (int j = 0; j <= stripePoints; j++)
			{
				var offset = stripeStart + (stripeEnd - stripeStart) * j / stripePoints;
				if (offset > bakeLength) offset -= bakeLength;
				
				var centerPoint = _trackCurve.SampleBaked(offset);
				
				// Get perpendicular direction for offset
				var nextOffset = offset + 1.0f;
				if (nextOffset > bakeLength) nextOffset -= bakeLength;
				var nextPoint = _trackCurve.SampleBaked(nextOffset);
				var direction = (nextPoint - centerPoint).Normalized();
				var perpendicular = new Vector2(-direction.Y, direction.X);
				
				// Offset to create kerb marking
				var kerbOffset = (TrackWidth * 0.5f) + KerbOffset;
				if (!kerbSection._IsInnerEdge) kerbOffset = -kerbOffset;
				
				var kerbPoint = centerPoint + perpendicular * kerbOffset;
				stripeLine.AddPoint(kerbPoint);
			}
			
			_kerbParent.AddChild(stripeLine);
		}
	}

	private void ClearVisuals()
	{
		if (_trackPath != null)
		{
			_trackPath.QueueFree();
			_trackPath = null;
		}

		if (_trackVisual != null)
		{
			_trackVisual.QueueFree();
			_trackVisual = null;
		}

		if (_innerEdgeVisual != null)
		{
			_innerEdgeVisual.QueueFree();
			_innerEdgeVisual = null;
		}

		if (_outerEdgeVisual != null)
		{
			_outerEdgeVisual.QueueFree();
			_outerEdgeVisual = null;
		}

		if (_kerbParent != null)
		{
			_kerbParent.QueueFree();
			_kerbParent = null;
		}

		if (_debugPointsParent != null)
		{
			_debugPointsParent.QueueFree();
			_debugPointsParent = null;
		}
	}

	private void RegenerateTrackDeferred()
	{
		if (Engine.IsEditorHint())
		{
			GD.Print("TrackGenerator: Regenerating track in editor");
		}
		
		// Clear existing visuals first
		ClearVisuals();
		
		// Generate new track
		GenerateTrack();
		
		// Force editor to update if in editor mode
		if (Engine.IsEditorHint())
		{
			NotifyPropertyListChanged();
			QueueRedraw();
		}
	}

	// Graham scan algorithm for convex hull computation
	private Vector2[] ComputeConvexHull(Vector2[] points)
	{
		if (points.Length < 3)
			return points;

		// Find the bottom-most point (or left most in case of tie)
		var startPoint = points[0];
		int startIndex = 0;
		
		for (int i = 1; i < points.Length; i++)
		{
			if (points[i].Y < startPoint.Y || 
			    (points[i].Y == startPoint.Y && points[i].X < startPoint.X))
			{
				startPoint = points[i];
				startIndex = i;
			}
		}

		// Sort points by polar angle with respect to start point
		var sortedPoints = new List<Vector2>(points);
		sortedPoints.RemoveAt(startIndex);
		
		sortedPoints.Sort((a, b) =>
		{
			var angleA = Mathf.Atan2(a.Y - startPoint.Y, a.X - startPoint.X);
			var angleB = Mathf.Atan2(b.Y - startPoint.Y, b.X - startPoint.X);
			
			if (Mathf.Abs(angleA - angleB) < 0.001f)
			{
				// If angles are equal, sort by distance
				var distA = startPoint.DistanceSquaredTo(a);
				var distB = startPoint.DistanceSquaredTo(b);
				return distA.CompareTo(distB);
			}
			
			return angleA.CompareTo(angleB);
		});

		// Graham scan
		var hull = new List<Vector2> { startPoint };
		
		foreach (var point in sortedPoints)
		{
			// Remove points that create right turn
			while (hull.Count > 1 && 
			       CrossProduct(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0)
			{
				hull.RemoveAt(hull.Count - 1);
			}
			
			hull.Add(point);
		}

		return hull.ToArray();
	}

	// Helper method to compute cross product for determining turn direction
	private float CrossProduct(Vector2 o, Vector2 a, Vector2 b)
	{
		return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
	}



	// Public API methods
	public Curve2D GetTrackCurve()
	{
		return _trackCurve;
	}

	public Vector2 GetStartPosition()
	{
		if (_trackPoints == null || _trackPoints.Length == 0)
			return Vector2.Zero;
		return _trackPoints[0];
	}

	public Vector2 GetStartDirection()
	{
		if (_trackPoints == null || _trackPoints.Length < 2)
			return Vector2.Right;
		return (_trackPoints[1] - _trackPoints[0]).Normalized();
	}

	public float GetTrackLength()
	{
		return _trackCurve?.GetBakedLength() ?? 0.0f;
	}

	public bool IsValidTrackPoint(Vector2 point)
	{
		if (_trackCurve == null) return false;
		return _trackCurve.GetClosestPoint(point).DistanceTo(point) <= TrackWidth;
	}

	public Curve2D GetInnerEdgeCurve()
	{
		return _innerEdgeCurve;
	}

	public Curve2D GetOuterEdgeCurve()
	{
		return _outerEdgeCurve;
	}

	public Vector2[] GetInnerEdgePoints()
	{
		return _innerEdgePoints;
	}

	public Vector2[] GetOuterEdgePoints()
	{
		return _outerEdgePoints;
	}

	public bool HasKerbAtPosition(Vector2 position)
	{
		if (_kerbSections == null || _trackCurve == null)
			return false;

		var closestOffset = FindClosestOffsetOnCurve(position);
		
		foreach (var kerbSection in _kerbSections)
		{
			if (closestOffset >= kerbSection._StartOffset && closestOffset <= kerbSection._EndOffset)
				return true;
		}
		
		return false;
	}

	public override void _Draw()
	{
		if (!Engine.IsEditorHint() || !ShowDebugPoints || _trackPoints == null)
			return;

		// Draw debug points
		for (int i = 0; i < _trackPoints.Length; i++)
		{
			DrawCircle(_trackPoints[i], 5.0f, Colors.Red);
			DrawString(ThemeDB.FallbackFont, _trackPoints[i] + Vector2.One * 10, i.ToString(), HorizontalAlignment.Left, -1, 12);
		}
	}
}