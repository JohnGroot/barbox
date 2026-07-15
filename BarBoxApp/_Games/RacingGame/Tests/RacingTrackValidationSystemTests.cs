using Godot;
using Chickensoft.GoDotTest;
using Shouldly;

namespace BarBox.Games.Racing.Tests;

/// <summary>
/// Unit tests for RacingTrackValidationSystem - off-track penalty and track validation
/// Tests penalty calculations, modifier application, and zone integration
/// </summary>
public class RacingTrackValidationSystemTests : TestClass
{
	private RacingTrackValidationSystem _validationSystem;

	public RacingTrackValidationSystemTests(Node testScene) : base(testScene)
	{
	}

	[Setup]
	public void Setup()
	{
		// Create fresh system for each test to avoid state carryover
		_validationSystem = new RacingTrackValidationSystem();
		TestScene.AddChild(_validationSystem);
	}

	[Cleanup]
	public void Cleanup()
	{
		// Clean up after each test
		if (_validationSystem != null && Godot.GodotObject.IsInstanceValid(_validationSystem))
		{
			_validationSystem.QueueFree();
			_validationSystem = null;
		}
	}

	// ================================================================
	// INITIALIZATION TESTS
	// ================================================================

	[Test]
	public void InitialState_IsNotInitialized()
	{
		var freshSystem = new RacingTrackValidationSystem();
		TestScene.AddChild(freshSystem);

		freshSystem.IsInitialized().ShouldBeFalse();

		freshSystem.QueueFree();
	}

	[Test]
	public void InitialState_HasDefaultExportValues()
	{
		var freshSystem = new RacingTrackValidationSystem();
		TestScene.AddChild(freshSystem);

		// Check default penalty values
		freshSystem.OffTrackSpeedPenalty.ShouldBe(0.3f);
		freshSystem.OffTrackTurnPenalty.ShouldBe(0.3f);
		freshSystem.OffTrackAccelerationPenalty.ShouldBe(0.3f);
		freshSystem.CenterLineProximityRange.ShouldBe(40.0f);
		freshSystem.CenterLineAccelerationBonus.ShouldBe(1.5f);

		freshSystem.QueueFree();
	}

	[Test]
	public void InitialState_PenaltyMultipliersAreOne()
	{
		var freshSystem = new RacingTrackValidationSystem();
		TestScene.AddChild(freshSystem);

		freshSystem.CurrentSpeedPenaltyMultiplier.ShouldBe(1.0f);
		freshSystem.CurrentTurnPenaltyMultiplier.ShouldBe(1.0f);
		freshSystem.CurrentAccelerationPenaltyMultiplier.ShouldBe(1.0f);
		freshSystem.IsCurrentlyOffTrack.ShouldBeFalse();

		freshSystem.QueueFree();
	}

	// ================================================================
	// RESET PENALTY TESTS
	// ================================================================

	[Test]
	public void ResetPenalties_RestoresDefaultMultipliers()
	{
		// Simulate some penalty state changes by setting properties via reflection would be complex
		// Instead test that ResetPenalties sets known values
		_validationSystem.ResetPenalties();

		_validationSystem.CurrentSpeedPenaltyMultiplier.ShouldBe(1.0f);
		_validationSystem.CurrentTurnPenaltyMultiplier.ShouldBe(1.0f);
		_validationSystem.CurrentAccelerationPenaltyMultiplier.ShouldBe(1.0f);
		_validationSystem.IsCurrentlyOffTrack.ShouldBeFalse();
	}

	// ================================================================
	// ZONE MANAGER INTEGRATION TESTS
	// ================================================================

	[Test]
	public void GetZoneManager_ReturnsNull_WhenNotSet()
	{
		var freshSystem = new RacingTrackValidationSystem();
		TestScene.AddChild(freshSystem);

		freshSystem.GetZoneManager().ShouldBeNull();

		freshSystem.QueueFree();
	}

	[Test]
	public void SetZoneManager_StoresReference()
	{
		var zoneManager = new RacingZoneManager();
		TestScene.AddChild(zoneManager);

		_validationSystem.SetZoneManager(zoneManager);
		_validationSystem.GetZoneManager().ShouldBe(zoneManager);

		_validationSystem.SetZoneManager(null); // Clean up
		zoneManager.QueueFree();
	}

	// ================================================================
	// MODIFIER GETTER TESTS (WITHOUT TRACK)
	// ================================================================

	[Test]
	public void GetSpeedModifier_ReturnsCurrentPenalty_WhenNoZoneManager()
	{
		// Without zone manager, should return track penalty only
		_validationSystem.SetZoneManager(null);
		var modifier = _validationSystem.GetSpeedModifier(Vector2.Zero);

		// Initial state has multiplier of 1.0
		modifier.ShouldBe(1.0f);
	}

	[Test]
	public void GetTurnModifier_ReturnsCurrentPenalty_WhenNoZoneManager()
	{
		_validationSystem.SetZoneManager(null);
		var modifier = _validationSystem.GetTurnModifier(Vector2.Zero);

		modifier.ShouldBe(1.0f);
	}

	[Test]
	public void GetAccelerationModifier_ReturnsCurrentPenalty_WhenNoZoneManager()
	{
		_validationSystem.SetZoneManager(null);
		var modifier = _validationSystem.GetAccelerationModifier(Vector2.Zero);

		modifier.ShouldBe(1.0f);
	}

	// ================================================================
	// TRACK VALIDATION TESTS (WITHOUT TRACK)
	// ================================================================

	[Test]
	public void IsOnTrack_ReturnsFalse_WhenNoTrackDefinition()
	{
		// Without track definition, all positions should be off-track
		_validationSystem.IsOnTrack(Vector2.Zero).ShouldBeFalse();
		_validationSystem.IsOnTrack(new Vector2(100, 100)).ShouldBeFalse();
	}

	[Test]
	public void GetDistanceToTrackCenterLine_ReturnsMaxValue_WhenNoCurve()
	{
		// Without curve, should return max value
		var distance = _validationSystem.GetDistanceToTrackCenterLine(Vector2.Zero);
		distance.ShouldBe(float.MaxValue);
	}

	[Test]
	public void IsCarCompletelyOffTrack_ReturnsTrue_WhenNoTrackDefinition()
	{
		// Without track definition, car is always off track
		var isOffTrack = _validationSystem.IsCarCompletelyOffTrack(
			Vector2.Zero,
			0.0f,
			new Vector2(50, 30)
		);

		isOffTrack.ShouldBeTrue();
	}

	// ================================================================
	// INPUT BLOCKED TESTS
	// ================================================================

	[Test]
	public void IsInputBlocked_ReturnsFalse_WhenNoZoneManager()
	{
		_validationSystem.SetZoneManager(null);

		var mockBody = new Node2D();
		TestScene.AddChild(mockBody);

		_validationSystem.IsInputBlocked(mockBody).ShouldBeFalse();

		mockBody.QueueFree();
	}

	// ================================================================
	// PENALTY LERPING TESTS
	// ================================================================

	[Test]
	public void UpdateOffTrackPenalties_LerpsPenaltiesGradually()
	{
		// Without track, car is completely off track
		// Penalties should lerp toward OffTrackSpeedPenalty (0.3f)

		_validationSystem.ResetPenalties();

		// Use non-zero position to trigger cache recalculation
		// (cache threshold is 5 pixels, so we need to be far from (0,0))
		var testPosition = new Vector2(100, 100);

		// Small delta - should start lerping but not reach target immediately
		_validationSystem.UpdateOffTrackPenalties(testPosition, 0.0f, new Vector2(50, 30), 0.1f);

		// Multiplier should be less than 1.0 (starting penalty) but greater than 0.3 (target)
		_validationSystem.CurrentSpeedPenaltyMultiplier.ShouldBeLessThan(1.0f);
		_validationSystem.CurrentSpeedPenaltyMultiplier.ShouldBeGreaterThan(0.3f);
	}

	[Test]
	public void UpdateOffTrackPenalties_SetsIsOffTrackFlag_WhenCompletelyOffTrack()
	{
		// Without track definition, car is completely off track
		// Use non-zero position to trigger cache recalculation
		var testPosition = new Vector2(100, 100);
		_validationSystem.UpdateOffTrackPenalties(testPosition, 0.0f, new Vector2(50, 30), 0.1f);

		_validationSystem.IsCurrentlyOffTrack.ShouldBeTrue();
	}

	[Test]
	public void UpdateOffTrackPenalties_ConvergesToTargetPenalty_OverTime()
	{
		_validationSystem.ResetPenalties();

		// Use non-zero position to trigger cache recalculation
		var testPosition = new Vector2(100, 100);

		// Run many update cycles to converge
		for (int i = 0; i < 100; i++)
		{
			_validationSystem.UpdateOffTrackPenalties(testPosition, 0.0f, new Vector2(50, 30), 0.1f);
		}

		// Should be very close to target penalty (0.3f)
		_validationSystem.CurrentSpeedPenaltyMultiplier.ShouldBe(0.3f, 0.01f);
		_validationSystem.CurrentTurnPenaltyMultiplier.ShouldBe(0.3f, 0.01f);
		_validationSystem.CurrentAccelerationPenaltyMultiplier.ShouldBe(0.3f, 0.01f);
	}

	[Test]
	public void UpdateOffTrackPenalties_AllPenaltiesLerpTogether()
	{
		_validationSystem.ResetPenalties();

		// Use non-zero position to trigger cache recalculation
		var testPosition = new Vector2(100, 100);
		_validationSystem.UpdateOffTrackPenalties(testPosition, 0.0f, new Vector2(50, 30), 0.1f);

		// All penalties should change together
		float speed = _validationSystem.CurrentSpeedPenaltyMultiplier;
		float turn = _validationSystem.CurrentTurnPenaltyMultiplier;
		float accel = _validationSystem.CurrentAccelerationPenaltyMultiplier;

		// They should all be moving toward the same target (0.3f)
		speed.ShouldBeLessThan(1.0f);
		turn.ShouldBeLessThan(1.0f);
		accel.ShouldBeLessThan(1.0f);
	}

	// ================================================================
	// CAR SIZE VARIATION TESTS
	// ================================================================

	[Test]
	public void IsCarCompletelyOffTrack_HandlesLargeCars()
	{
		// Large car should still work
		var isOffTrack = _validationSystem.IsCarCompletelyOffTrack(
			Vector2.Zero,
			0.0f,
			new Vector2(200, 100) // Large car
		);

		isOffTrack.ShouldBeTrue(); // Still off track when no track defined
	}

	[Test]
	public void IsCarCompletelyOffTrack_HandlesSmallCars()
	{
		var isOffTrack = _validationSystem.IsCarCompletelyOffTrack(
			Vector2.Zero,
			0.0f,
			new Vector2(10, 5) // Small car
		);

		isOffTrack.ShouldBeTrue();
	}

	[Test]
	public void IsCarCompletelyOffTrack_HandlesCarRotation()
	{
		// Rotated car should still be validated
		float rotation = Mathf.Pi / 4; // 45 degrees

		var isOffTrack = _validationSystem.IsCarCompletelyOffTrack(
			new Vector2(100, 100),
			rotation,
			new Vector2(50, 30)
		);

		isOffTrack.ShouldBeTrue(); // Off track when no track defined
	}

	// ================================================================
	// COMBINED MODIFIER TESTS
	// ================================================================

	[Test]
	public void GetSpeedModifier_WithBody_ReturnsTrackModifier_WhenNoZoneManager()
	{
		var mockBody = new Node2D();
		TestScene.AddChild(mockBody);

		_validationSystem.SetZoneManager(null);
		var modifier = _validationSystem.GetSpeedModifier(mockBody, Vector2.Zero);

		// Without zone manager, should return track modifier only
		modifier.ShouldBe(_validationSystem.CurrentSpeedPenaltyMultiplier);

		mockBody.QueueFree();
	}

	[Test]
	public void GetTurnModifier_WithBody_ReturnsTrackModifier_WhenNoZoneManager()
	{
		var mockBody = new Node2D();
		TestScene.AddChild(mockBody);

		_validationSystem.SetZoneManager(null);
		var modifier = _validationSystem.GetTurnModifier(mockBody, Vector2.Zero);

		modifier.ShouldBe(_validationSystem.CurrentTurnPenaltyMultiplier);

		mockBody.QueueFree();
	}

	[Test]
	public void GetAccelerationModifier_WithBody_ReturnsTrackModifier_WhenNoZoneManager()
	{
		var mockBody = new Node2D();
		TestScene.AddChild(mockBody);

		_validationSystem.SetZoneManager(null);
		var modifier = _validationSystem.GetAccelerationModifier(mockBody, Vector2.Zero);

		// Should return the penalty multiplier (currently 1.0 after reset)
		modifier.ShouldBe(_validationSystem.CurrentAccelerationPenaltyMultiplier);

		mockBody.QueueFree();
	}

	// ================================================================
	// EXPORT PROPERTY TESTS
	// ================================================================

	[Test]
	public void ExportProperties_CanBeModified()
	{
		_validationSystem.OffTrackSpeedPenalty = 0.5f;
		_validationSystem.OffTrackSpeedPenalty.ShouldBe(0.5f);

		_validationSystem.OffTrackTurnPenalty = 0.4f;
		_validationSystem.OffTrackTurnPenalty.ShouldBe(0.4f);

		_validationSystem.CenterLineProximityRange = 60.0f;
		_validationSystem.CenterLineProximityRange.ShouldBe(60.0f);

		// Reset to defaults
		_validationSystem.OffTrackSpeedPenalty = 0.3f;
		_validationSystem.OffTrackTurnPenalty = 0.3f;
		_validationSystem.CenterLineProximityRange = 40.0f;
	}

	[Test]
	public void ModifiedPenaltyValues_AffectLerpTarget()
	{
		// Set a different penalty target
		_validationSystem.OffTrackSpeedPenalty = 0.5f;
		_validationSystem.ResetPenalties();

		// Use non-zero position to trigger cache recalculation
		var testPosition = new Vector2(100, 100);

		// Run updates to converge
		for (int i = 0; i < 100; i++)
		{
			_validationSystem.UpdateOffTrackPenalties(testPosition, 0.0f, new Vector2(50, 30), 0.1f);
		}

		// Should converge to new target
		_validationSystem.CurrentSpeedPenaltyMultiplier.ShouldBe(0.5f, 0.01f);

		// Reset
		_validationSystem.OffTrackSpeedPenalty = 0.3f;
	}

	// ================================================================
	// LINE2D GEOMETRY REGRESSION TESTS
	// ================================================================

	private RacingTrackDefinition CreateTrackWithLine(Vector2[] points, float width, bool closed = false)
	{
		var trackDef = new RacingTrackDefinition();
		TestScene.AddChild(trackDef);

		var line = new Line2D();
		line.Width = width;
		line.Closed = closed;
		foreach (var p in points)
			line.AddPoint(p);
		trackDef.AddChild(line);
		trackDef.TrackLine = line;

		// Need start/finish lines for SetupTrack to work
		var startLine = new RacingLineTrigger();
		trackDef.AddChild(startLine);
		trackDef.StartLinePath = trackDef.GetPathTo(startLine);

		trackDef.SetupTrack();
		return trackDef;
	}

	[Test]
	public void IsValidTrackPoint_ReturnsTrue_ForPointOnStraightTrack()
	{
		// Horizontal track from (0,0) to (200,0), width 40
		var points = new[] { new Vector2(0, 0), new Vector2(200, 0) };
		var trackDef = CreateTrackWithLine(points, 40f);

		// Center of track
		trackDef.IsValidTrackPoint(new Vector2(100, 0)).ShouldBeTrue();
		// Near edge but within half-width (20)
		trackDef.IsValidTrackPoint(new Vector2(100, 19)).ShouldBeTrue();
		trackDef.IsValidTrackPoint(new Vector2(100, -19)).ShouldBeTrue();

		trackDef.QueueFree();
	}

	[Test]
	public void IsValidTrackPoint_ReturnsFalse_ForPointOffStraightTrack()
	{
		var points = new[] { new Vector2(0, 0), new Vector2(200, 0) };
		var trackDef = CreateTrackWithLine(points, 40f);

		// Outside half-width (20) + margin
		trackDef.IsValidTrackPoint(new Vector2(100, 25)).ShouldBeFalse();
		trackDef.IsValidTrackPoint(new Vector2(100, -25)).ShouldBeFalse();
		// Way off track
		trackDef.IsValidTrackPoint(new Vector2(100, 100)).ShouldBeFalse();

		trackDef.QueueFree();
	}

	[Test]
	public void IsValidTrackPoint_WorksOnMultiSegmentTrack()
	{
		// L-shaped track
		var points = new[] {
			new Vector2(0, 0),
			new Vector2(100, 0),
			new Vector2(100, 100)
		};
		var trackDef = CreateTrackWithLine(points, 30f);

		// On horizontal segment
		trackDef.IsValidTrackPoint(new Vector2(50, 0)).ShouldBeTrue();
		// On vertical segment
		trackDef.IsValidTrackPoint(new Vector2(100, 50)).ShouldBeTrue();
		// Off both segments
		trackDef.IsValidTrackPoint(new Vector2(0, 100)).ShouldBeFalse();

		trackDef.QueueFree();
	}

	[Test]
	public void IsValidTrackPoint_WorksOnClosedLoop()
	{
		// Triangle loop
		var points = new[] {
			new Vector2(0, 0),
			new Vector2(200, 0),
			new Vector2(100, 173)
		};
		var trackDef = CreateTrackWithLine(points, 30f, closed: true);

		// On bottom edge
		trackDef.IsValidTrackPoint(new Vector2(100, 0)).ShouldBeTrue();
		// On closing segment (last point to first point)
		// Midpoint of closing segment: (50, 86.5)
		trackDef.IsValidTrackPoint(new Vector2(50, 86)).ShouldBeTrue();
		// Center of triangle (should be off-track for narrow width)
		trackDef.IsValidTrackPoint(new Vector2(100, 57)).ShouldBeFalse();

		trackDef.QueueFree();
	}

	[Test]
	public void IsOnTrack_WithRealTrack_ReturnsTrueForOnTrackPoints()
	{
		var points = new[] { new Vector2(0, 0), new Vector2(300, 0) };
		var trackDef = CreateTrackWithLine(points, 50f);

		var curve = trackDef.GetTrackCurve();
		_validationSystem.Initialize(trackDef, curve);

		// On track
		_validationSystem.IsOnTrack(new Vector2(150, 0)).ShouldBeTrue();
		// Off track
		_validationSystem.IsOnTrack(new Vector2(150, 50)).ShouldBeFalse();

		trackDef.QueueFree();
	}

	[Test]
	public void IsCarCompletelyOffTrack_ReturnsFalse_WhenCarOnTrack()
	{
		var points = new[] { new Vector2(0, 0), new Vector2(300, 0) };
		var trackDef = CreateTrackWithLine(points, 80f);

		var curve = trackDef.GetTrackCurve();
		_validationSystem.Initialize(trackDef, curve);

		// Car centered on track
		var result = _validationSystem.IsCarCompletelyOffTrack(
			new Vector2(150, 0), 0f, new Vector2(40, 20));
		result.ShouldBeFalse();

		trackDef.QueueFree();
	}

	[Test]
	public void IsCarCompletelyOffTrack_ReturnsTrue_WhenCarFarOffTrack()
	{
		var points = new[] { new Vector2(0, 0), new Vector2(300, 0) };
		var trackDef = CreateTrackWithLine(points, 50f);

		var curve = trackDef.GetTrackCurve();
		_validationSystem.Initialize(trackDef, curve);

		// Car far off track
		var result = _validationSystem.IsCarCompletelyOffTrack(
			new Vector2(150, 200), 0f, new Vector2(40, 20));
		result.ShouldBeTrue();

		trackDef.QueueFree();
	}

	// ================================================================
	// SPATIAL INDEX REGRESSION TESTS (Task A1a)
	//
	// Assert the additive spatial-index path (IsValidTrackPointFast) is
	// bit-identical to the live full-scan path (IsValidTrackPoint) across a
	// scripted full-lap replay. Tolerance: exact boolean equality - the grid
	// applies the same per-segment distance test as the scan over a proven
	// superset of relevant segments, so results must match with no tolerance.
	// ================================================================

	// Build a closed oval loop, offset from the origin, with straights + curved
	// corners. An elongated oval also brings opposite straights within a moderate
	// distance of each other, exercising the "nearby but wrong-side" segment case.
	private static Vector2[] BuildOvalLoop(Vector2 center, float radiusX, float radiusY, int pointCount)
	{
		var pts = new Vector2[pointCount];
		for (int i = 0; i < pointCount; i++)
		{
			float a = Mathf.Tau * i / pointCount;
			pts[i] = center + new Vector2(Mathf.Cos(a) * radiusX, Mathf.Sin(a) * radiusY);
		}
		return pts;
	}

	// Walk the closed loop's centerline and emit scripted car positions: on-center,
	// just inside/outside the half-width band, exactly on the boundary, and far off.
	// This drives both on-track and off-track classifications and straddles the
	// on/off boundary at many positions and orientations around the lap.
	private static System.Collections.Generic.List<Vector2> ScriptLapPositions(
		Vector2[] loop, float halfWidth, int subStepsPerSegment)
	{
		var positions = new System.Collections.Generic.List<Vector2>();
		var lateralOffsets = new[]
		{
			0f,
			halfWidth - 1f,   // just inside -> on track
			halfWidth + 1f,   // just outside -> off track
			halfWidth,        // exactly on boundary
			halfWidth * 3f,   // clearly off track
		};

		int n = loop.Length;
		for (int i = 0; i < n; i++)
		{
			Vector2 a = loop[i];
			Vector2 b = loop[(i + 1) % n];
			Vector2 tangent = (b - a);
			if (tangent.LengthSquared() < 0.0001f)
				continue;
			tangent = tangent.Normalized();
			Vector2 normal = new Vector2(-tangent.Y, tangent.X);

			for (int s = 0; s < subStepsPerSegment; s++)
			{
				float t = (float)s / subStepsPerSegment;
				Vector2 onLine = a.Lerp(b, t);

				foreach (var off in lateralOffsets)
				{
					positions.Add(onLine + normal * off);
					positions.Add(onLine - normal * off);
				}
			}
		}
		return positions;
	}

	[Test]
	public void SpatialIndex_MatchesFullScan_AcrossScriptedLap()
	{
		float width = 60f;
		float halfWidth = width / 2f;
		var loop = BuildOvalLoop(new Vector2(500, 350), 300f, 180f, 32);
		var trackDef = CreateTrackWithLine(loop, width, closed: true);

		var scripted = ScriptLapPositions(loop, halfWidth, subStepsPerSegment: 6);
		scripted.Count.ShouldBeGreaterThan(1000); // meaningful lap coverage

		int onTrackCount = 0;
		int offTrackCount = 0;
		int mismatches = 0;

		foreach (var p in scripted)
		{
			bool oldResult = trackDef.IsValidTrackPoint(p);
			bool newResult = trackDef.IsValidTrackPointFast(p);

			if (oldResult != newResult)
				mismatches++;

			if (oldResult) onTrackCount++;
			else offTrackCount++;
		}

		// Bit-identical classification at every sampled position.
		mismatches.ShouldBe(0);

		// The lap must genuinely exercise BOTH classifications (not all-on / all-off),
		// otherwise a trivially-agreeing stub would pass.
		onTrackCount.ShouldBeGreaterThan(0);
		offTrackCount.ShouldBeGreaterThan(0);

		trackDef.QueueFree();
	}

	[Test]
	public void SpatialIndex_MatchesFullScan_OnOpenMultiSegmentTrack()
	{
		// Open (non-closed) track with sharp corners and start/end (no wraparound).
		float width = 40f;
		var points = new[]
		{
			new Vector2(-200, -100),
			new Vector2(100, -100),
			new Vector2(100, 150),
			new Vector2(400, 150),
			new Vector2(400, -50),
		};
		var trackDef = CreateTrackWithLine(points, width, closed: false);

		var scripted = ScriptLapPositions(points, width / 2f, subStepsPerSegment: 8);
		// Also probe the exact endpoints and just beyond them (open-track ends).
		scripted.Add(points[0]);
		scripted.Add(points[^1]);
		scripted.Add(points[0] + new Vector2(-30, 0));
		scripted.Add(points[^1] + new Vector2(0, -30));

		int mismatches = 0;
		foreach (var p in scripted)
		{
			if (trackDef.IsValidTrackPoint(p) != trackDef.IsValidTrackPointFast(p))
				mismatches++;
		}
		mismatches.ShouldBe(0);

		trackDef.QueueFree();
	}

	[Test]
	public void SpatialIndex_MatchesFullScan_OnDenseGridSweep()
	{
		// Exhaustive sweep over the track's bounding region catches any point whose
		// true closest segment lies in a neighboring cell - the key completeness risk.
		float width = 50f;
		var loop = BuildOvalLoop(new Vector2(0, 0), 220f, 140f, 24);
		var trackDef = CreateTrackWithLine(loop, width, closed: true);

		int mismatches = 0;
		int sampleCount = 0;
		for (float x = -320f; x <= 320f; x += 3.5f)
		{
			for (float y = -240f; y <= 240f; y += 3.5f)
			{
				var p = new Vector2(x, y);
				if (trackDef.IsValidTrackPoint(p) != trackDef.IsValidTrackPointFast(p))
					mismatches++;
				sampleCount++;
			}
		}

		mismatches.ShouldBe(0);
		sampleCount.ShouldBeGreaterThan(10000);

		trackDef.QueueFree();
	}

	[Test]
	public void SpatialIndex_Validation_TimingComparison()
	{
		// Capture a before/after Validation-cost comparison for the new query path
		// standalone (it is not yet wired into live frames). Debug-build only, mirrors
		// the RacingGame Stopwatch + OS.IsDebugBuild timing idiom. No assertion on
		// timing - environment-dependent; correctness is covered by the tests above.
		if (!OS.IsDebugBuild())
			return;

		float width = 60f;
		var loop = BuildOvalLoop(new Vector2(500, 350), 300f, 180f, 48);
		var trackDef = CreateTrackWithLine(loop, width, closed: true);
		var scripted = ScriptLapPositions(loop, width / 2f, subStepsPerSegment: 6);

		const int Iterations = 20;
		var sw = new System.Diagnostics.Stopwatch();

		sw.Restart();
		for (int it = 0; it < Iterations; it++)
			foreach (var p in scripted)
				_ = trackDef.IsValidTrackPoint(p);
		sw.Stop();
		double oldMs = sw.Elapsed.TotalMilliseconds;

		sw.Restart();
		for (int it = 0; it < Iterations; it++)
			foreach (var p in scripted)
				_ = trackDef.IsValidTrackPointFast(p);
		sw.Stop();
		double newMs = sw.Elapsed.TotalMilliseconds;

		int queries = scripted.Count * Iterations;
		GD.Print($"[A1a] Validation timing over {queries} queries " +
			$"({loop.Length} segments): full-scan={oldMs:F2}ms, spatial-index={newMs:F2}ms, " +
			$"per-query old={oldMs / queries * 1000.0:F3}us new={newMs / queries * 1000.0:F3}us");

		trackDef.QueueFree();
	}
}
