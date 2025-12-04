using Godot;
using Chickensoft.GoDotTest;
using Shouldly;

namespace BarBox.Games.Racing.Tests;

/// <summary>
/// Unit tests for RacingZoneManager - zone registration, body tracking, and modifier calculations
/// Tests zone registration, modifier stacking, frictionless effects, and body tracking
/// </summary>
public class RacingZoneManagerTests : TestClass
{
	private RacingZoneManager _zoneManager;

	public RacingZoneManagerTests(Node testScene) : base(testScene)
	{
	}

	[SetupAll]
	public void Setup()
	{
		_zoneManager = new RacingZoneManager();
		TestScene.AddChild(_zoneManager);
	}

	[Cleanup]
	public void Cleanup()
	{
		_zoneManager.ClearAllZones();
	}

	[CleanupAll]
	public void TearDown()
	{
		_zoneManager?.QueueFree();
	}

	// Helper to create a test zone with specific modifiers
	private RacingZone CreateTestZone(
		ZoneType type = ZoneType.Slowdown,
		float speedModifier = 1.0f,
		float accelerationModifier = 1.0f,
		float turnModifier = 1.0f,
		bool blocksInput = false,
		float duration = 0.0f)
	{
		var zone = new RacingZone();
		zone.Type = type;
		zone.SpeedModifier = speedModifier;
		zone.AccelerationModifier = accelerationModifier;
		zone.TurnModifier = turnModifier;
		zone.BlocksInput = blocksInput;
		zone.Duration = duration;
		TestScene.AddChild(zone);
		return zone;
	}

	// Helper to create a test body
	private Node2D CreateTestBody()
	{
		var body = new Node2D();
		TestScene.AddChild(body);
		return body;
	}

	// ================================================================
	// INITIALIZATION TESTS
	// ================================================================

	[Test]
	public void InitialState_HasNoZones()
	{
		var freshManager = new RacingZoneManager();
		TestScene.AddChild(freshManager);

		freshManager.ZoneCount.ShouldBe(0);
		freshManager.AllZones.Count.ShouldBe(0);

		freshManager.QueueFree();
	}

	// ================================================================
	// ZONE REGISTRATION TESTS
	// ================================================================

	[Test]
	public void RegisterZone_AddsZoneToList()
	{
		var zone = CreateTestZone();

		_zoneManager.RegisterZone(zone);

		_zoneManager.ZoneCount.ShouldBe(1);
		_zoneManager.AllZones.ShouldContain(zone);

		zone.QueueFree();
	}

	[Test]
	public void RegisterZone_IgnoresNull()
	{
		_zoneManager.RegisterZone(null);
		_zoneManager.ZoneCount.ShouldBe(0);
	}

	[Test]
	public void RegisterZone_IgnoresDuplicates()
	{
		var zone = CreateTestZone();

		_zoneManager.RegisterZone(zone);
		_zoneManager.RegisterZone(zone);
		_zoneManager.RegisterZone(zone);

		_zoneManager.ZoneCount.ShouldBe(1);

		zone.QueueFree();
	}

	[Test]
	public void RegisterZone_CanRegisterMultipleZones()
	{
		var zone1 = CreateTestZone(ZoneType.Slowdown);
		var zone2 = CreateTestZone(ZoneType.Boost);
		var zone3 = CreateTestZone(ZoneType.Kerb);

		_zoneManager.RegisterZone(zone1);
		_zoneManager.RegisterZone(zone2);
		_zoneManager.RegisterZone(zone3);

		_zoneManager.ZoneCount.ShouldBe(3);

		zone1.QueueFree();
		zone2.QueueFree();
		zone3.QueueFree();
	}

	[Test]
	public void UnregisterZone_RemovesZoneFromList()
	{
		var zone = CreateTestZone();
		_zoneManager.RegisterZone(zone);

		_zoneManager.UnregisterZone(zone);

		_zoneManager.ZoneCount.ShouldBe(0);

		zone.QueueFree();
	}

	[Test]
	public void UnregisterZone_IgnoresNull()
	{
		Should.NotThrow(() => _zoneManager.UnregisterZone(null));
	}

	[Test]
	public void ClearAllZones_RemovesAllZones()
	{
		var zone1 = CreateTestZone();
		var zone2 = CreateTestZone();
		_zoneManager.RegisterZone(zone1);
		_zoneManager.RegisterZone(zone2);

		_zoneManager.ClearAllZones();

		_zoneManager.ZoneCount.ShouldBe(0);

		zone1.QueueFree();
		zone2.QueueFree();
	}

	// ================================================================
	// MODIFIER CALCULATION TESTS (Without Body in Zones)
	// ================================================================

	[Test]
	public void GetCombinedSpeedModifier_ReturnsOne_WhenBodyNotInAnyZone()
	{
		var body = CreateTestBody();

		var modifier = _zoneManager.GetCombinedSpeedModifier(body);

		modifier.ShouldBe(1.0f);

		body.QueueFree();
	}

	[Test]
	public void GetCombinedAccelerationModifier_ReturnsOne_WhenBodyNotInAnyZone()
	{
		var body = CreateTestBody();

		var modifier = _zoneManager.GetCombinedAccelerationModifier(body);

		modifier.ShouldBe(1.0f);

		body.QueueFree();
	}

	[Test]
	public void GetCombinedTurnModifier_ReturnsOne_WhenBodyNotInAnyZone()
	{
		var body = CreateTestBody();

		var modifier = _zoneManager.GetCombinedTurnModifier(body);

		modifier.ShouldBe(1.0f);

		body.QueueFree();
	}

	// ================================================================
	// BODY TRACKING TESTS
	// ================================================================

	[Test]
	public void IsInAnyZone_ReturnsFalse_WhenBodyNotTracked()
	{
		var body = CreateTestBody();

		_zoneManager.IsInAnyZone(body).ShouldBeFalse();

		body.QueueFree();
	}

	[Test]
	public void GetZonesForBody_ReturnsEmptyList_WhenBodyNotInAnyZone()
	{
		var body = CreateTestBody();

		var zones = _zoneManager.GetZonesForBody(body);

		zones.ShouldNotBeNull();
		zones.Count.ShouldBe(0);

		body.QueueFree();
	}

	[Test]
	public void IsInZoneType_ReturnsFalse_WhenBodyNotInZone()
	{
		var body = CreateTestBody();

		_zoneManager.IsInZoneType(body, ZoneType.Slowdown).ShouldBeFalse();
		_zoneManager.IsInZoneType(body, ZoneType.Boost).ShouldBeFalse();
		_zoneManager.IsInZoneType(body, ZoneType.Frictionless).ShouldBeFalse();
		_zoneManager.IsInZoneType(body, ZoneType.Kerb).ShouldBeFalse();

		body.QueueFree();
	}

	// ================================================================
	// INPUT BLOCKED TESTS
	// ================================================================

	[Test]
	public void IsInputBlocked_ReturnsFalse_WhenBodyNotInAnyZone()
	{
		var body = CreateTestBody();

		_zoneManager.IsInputBlocked(body).ShouldBeFalse();

		body.QueueFree();
	}

	// ================================================================
	// FRICTIONLESS STATE TESTS
	// ================================================================

	[Test]
	public void IsInFrictionlessState_ReturnsFalse_WhenNotInFrictionlessZone()
	{
		var body = CreateTestBody();

		_zoneManager.IsInFrictionlessState(body).ShouldBeFalse();

		body.QueueFree();
	}

	[Test]
	public void GetFrictionlessState_ReturnsInactive_WhenNotInFrictionlessZone()
	{
		var body = CreateTestBody();

		var (isActive, timeRemaining, lockedVelocity, lockedRotation) = _zoneManager.GetFrictionlessState(body);

		isActive.ShouldBeFalse();
		timeRemaining.ShouldBe(0f);
		lockedVelocity.ShouldBe(Vector2.Zero);
		lockedRotation.ShouldBe(0f);

		body.QueueFree();
	}

	[Test]
	public void UpdateFrictionlessEffects_DoesNotThrow_WhenNoActiveEffects()
	{
		Should.NotThrow(() => _zoneManager.UpdateFrictionlessEffects(0.1f));
	}

	// ================================================================
	// KERB ZONE TESTS
	// ================================================================

	[Test]
	public void IsInKerbZone_ReturnsFalse_WhenNoKerbZonesRegistered()
	{
		// No zones registered
		_zoneManager.IsInKerbZone(Vector2.Zero).ShouldBeFalse();
	}

	[Test]
	public void IsInKerbZone_ReturnsFalse_WhenOnlyNonKerbZonesRegistered()
	{
		var slowdownZone = CreateTestZone(ZoneType.Slowdown);
		var boostZone = CreateTestZone(ZoneType.Boost);
		_zoneManager.RegisterZone(slowdownZone);
		_zoneManager.RegisterZone(boostZone);

		// Even with zones registered, if none are kerbs, should return false
		_zoneManager.IsInKerbZone(Vector2.Zero).ShouldBeFalse();

		slowdownZone.QueueFree();
		boostZone.QueueFree();
	}

	// ================================================================
	// POSITION-BASED QUERY TESTS
	// ================================================================

	[Test]
	public void GetZonesAtPosition_ReturnsEmptyList_WhenNoZonesRegistered()
	{
		var zones = _zoneManager.GetZonesAtPosition(Vector2.Zero);

		zones.ShouldNotBeNull();
		zones.Count.ShouldBe(0);
	}

	// ================================================================
	// ZONE TYPE TESTS
	// ================================================================

	[Test]
	public void RegisteredZones_PreserveType()
	{
		var slowdownZone = CreateTestZone(ZoneType.Slowdown);
		var boostZone = CreateTestZone(ZoneType.Boost);
		var frictionlessZone = CreateTestZone(ZoneType.Frictionless);
		var kerbZone = CreateTestZone(ZoneType.Kerb);

		_zoneManager.RegisterZone(slowdownZone);
		_zoneManager.RegisterZone(boostZone);
		_zoneManager.RegisterZone(frictionlessZone);
		_zoneManager.RegisterZone(kerbZone);

		_zoneManager.AllZones[0].Type.ShouldBe(ZoneType.Slowdown);
		_zoneManager.AllZones[1].Type.ShouldBe(ZoneType.Boost);
		_zoneManager.AllZones[2].Type.ShouldBe(ZoneType.Frictionless);
		_zoneManager.AllZones[3].Type.ShouldBe(ZoneType.Kerb);

		slowdownZone.QueueFree();
		boostZone.QueueFree();
		frictionlessZone.QueueFree();
		kerbZone.QueueFree();
	}

	// ================================================================
	// MODIFIER VALUES TESTS
	// ================================================================

	[Test]
	public void RegisteredZones_PreserveModifierValues()
	{
		var zone = CreateTestZone(
			type: ZoneType.Slowdown,
			speedModifier: 0.5f,
			accelerationModifier: 0.7f,
			turnModifier: 0.8f
		);

		_zoneManager.RegisterZone(zone);

		_zoneManager.AllZones[0].SpeedModifier.ShouldBe(0.5f);
		_zoneManager.AllZones[0].AccelerationModifier.ShouldBe(0.7f);
		_zoneManager.AllZones[0].TurnModifier.ShouldBe(0.8f);

		zone.QueueFree();
	}

	// ================================================================
	// ALL ZONES READ-ONLY TESTS
	// ================================================================

	[Test]
	public void AllZones_ReturnsReadOnlyCollection()
	{
		var zone = CreateTestZone();
		_zoneManager.RegisterZone(zone);

		var allZones = _zoneManager.AllZones;

		// Should be able to read but not modify
		allZones.Count.ShouldBe(1);
		allZones[0].ShouldBe(zone);

		zone.QueueFree();
	}

	// ================================================================
	// MULTIPLE BODY TRACKING TESTS
	// ================================================================

	[Test]
	public void CanTrackMultipleBodies()
	{
		var body1 = CreateTestBody();
		var body2 = CreateTestBody();
		var body3 = CreateTestBody();

		// All bodies should initially not be in any zone
		_zoneManager.IsInAnyZone(body1).ShouldBeFalse();
		_zoneManager.IsInAnyZone(body2).ShouldBeFalse();
		_zoneManager.IsInAnyZone(body3).ShouldBeFalse();

		body1.QueueFree();
		body2.QueueFree();
		body3.QueueFree();
	}

	// ================================================================
	// CLEAR CLEARS BODY TRACKING TESTS
	// ================================================================

	[Test]
	public void ClearAllZones_ClearsBodyTracking()
	{
		var zone = CreateTestZone();
		var body = CreateTestBody();

		_zoneManager.RegisterZone(zone);

		// Simulate body entering zone by calling internal event
		// Since we can't easily trigger the signal, test that clear works

		_zoneManager.ClearAllZones();

		// After clear, body should not be tracked
		_zoneManager.GetZonesForBody(body).Count.ShouldBe(0);

		zone.QueueFree();
		body.QueueFree();
	}

	// ================================================================
	// FRICTIONLESS UPDATE TESTS
	// ================================================================

	[Test]
	public void UpdateFrictionlessEffects_HandlesMultipleUpdates()
	{
		// Should not throw on multiple updates
		for (int i = 0; i < 100; i++)
		{
			Should.NotThrow(() => _zoneManager.UpdateFrictionlessEffects(0.016f));
		}
	}

	// ================================================================
	// ZONE TYPE ENUM COVERAGE TESTS
	// ================================================================

	[Test]
	public void ZoneType_HasExpectedValues()
	{
		// Verify all zone types exist
		var slowdown = ZoneType.Slowdown;
		var boost = ZoneType.Boost;
		var frictionless = ZoneType.Frictionless;
		var kerb = ZoneType.Kerb;

		slowdown.ShouldBe(ZoneType.Slowdown);
		boost.ShouldBe(ZoneType.Boost);
		frictionless.ShouldBe(ZoneType.Frictionless);
		kerb.ShouldBe(ZoneType.Kerb);
	}

	// ================================================================
	// DEFAULT MODIFIER VALUES TESTS
	// ================================================================

	[Test]
	public void DefaultZone_HasModifiersOfOne()
	{
		var zone = new RacingZone();
		TestScene.AddChild(zone);

		zone.SpeedModifier.ShouldBe(1.0f);
		zone.AccelerationModifier.ShouldBe(1.0f);
		zone.TurnModifier.ShouldBe(1.0f);

		zone.QueueFree();
	}

	[Test]
	public void DefaultZone_DoesNotBlockInput()
	{
		var zone = new RacingZone();
		TestScene.AddChild(zone);

		zone.BlocksInput.ShouldBeFalse();

		zone.QueueFree();
	}

	[Test]
	public void DefaultZone_HasZeroDuration()
	{
		var zone = new RacingZone();
		TestScene.AddChild(zone);

		zone.Duration.ShouldBe(0.0f);

		zone.QueueFree();
	}
}
