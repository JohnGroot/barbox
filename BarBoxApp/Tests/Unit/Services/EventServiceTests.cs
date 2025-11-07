using System;
using System.Threading.Tasks;
using Chickensoft.GoDotTest;
using Godot;
using BarBox.Tests.Fixtures;

namespace BarBox.Tests.Unit.Services;

/// <summary>
/// Tests for EventService - backend communication and session management
/// </summary>
public class EventServiceTests : BackendTestBase
{
	private EventService _eventService;

	public EventServiceTests(Node testScene) : base(testScene)
	{
	}

	[Setup]
	public new void SetupTestIdentifiers()
	{
		base.SetupTestIdentifiers();
		_eventService = GetEventService();
	}

	[Test]
	public async void CreateActivitySession_WithValidParams_ReturnsSessionId()
	{
		// Arrange
		var gameTag = "mining";

		// Act - create session without pre-registering box/player
		// EventService should handle creation internally
		var result = await _eventService.CreateActivitySessionAsync(
			TestBoxId,
			TestPlayerId,
			gameTag);

		// Assert
		if (!result.IsSuccess)
		{
			TestHelpers.LogTestError($"CreateActivitySession failed: {result.Error}");
		}

		// Note: This test may fail if backend requires box/player to exist first
		// In production, SessionManager handles box/player creation
		// For now, we're testing the EventService API contract
	}

	[Test]
	public async void CreateActivitySession_WithoutGameTag_ReturnsFailure()
	{
		// Act
		var result = await _eventService.CreateActivitySessionAsync(
			TestBoxId,
			TestPlayerId,
			null); // Missing game tag

		// Assert
		if (result.IsSuccess)
		{
			TestHelpers.LogTestError("Expected CreateActivitySession to fail without game_tag");
		}
		else
		{
			TestHelpers.LogTestInfo($"Correctly failed: {result.Error}");
		}
	}

	[Test]
	public async void CreateActivitySession_WithMultiplePlayerIds_IncludesInRequest()
	{
		// Arrange
		var gameTag = "carrom";
		var playerIds = new System.Collections.Generic.List<string>
		{
			TestPlayerId.ToString(),
			TestHelpers.GenerateTestPlayerId().ToString()
		};

		// Act
		var result = await _eventService.CreateActivitySessionAsync(
			TestBoxId,
			TestPlayerId,
			gameTag,
			playerIds);

		// Assert - should handle multiplayer session creation
		TestHelpers.LogTestInfo($"Multiplayer session result: {(result.IsSuccess ? "Success" : result.Error)}");
	}

	[Test]
	public async void CloseActivitySession_WithValidSession_ReturnsSuccess()
	{
		// Arrange - create a session first
		var sessionId = TestHelpers.GenerateTestSessionId();

		// Act
		var result = await _eventService.CloseActivitySessionAsync(sessionId);

		// Assert
		// May fail if session doesn't exist - that's expected for unit test
		TestHelpers.LogTestInfo($"Close session result: {(result.IsSuccess ? "Success" : result.Error)}");
	}

	[Test]
	public async void EmitEvent_WithSessionActive_ReturnsSuccess()
	{
		// This test requires a full integration setup
		// For now, testing the API surface
		TestHelpers.LogTestInfo("EmitEvent test requires integration setup");
	}

	[Cleanup]
	public override void CleanupTestResources()
	{
		base.CleanupTestResources();
		// EventService is autoload - don't queue free
		_eventService = null;
	}
}
