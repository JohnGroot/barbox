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
}
