using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Games.Racing.Tests;

/// <summary>
/// Smoke tests for RacingHUDArcRenderer's M5 ShapeCanvas migration — pins that _Process (which
/// EnsureShapesCommitted + PushShapeState route through, per the HasVisualStateChanged/
/// RecordDrawnState dirty-flag pattern) runs to completion without throwing for the speedometer,
/// lap ring, and countdown ring, and that the tessellated geometry is non-empty. Uses the internal
/// RebuildBuckets() (the same hook RacingTrackRendererTests/ShapeCanvasTests use) rather than
/// calling _Draw() directly — _Draw() issues Godot DrawString calls that are only legal inside a
/// real NOTIFICATION_DRAW callback, so invoking it outside the engine's draw pass logs a Godot
/// engine error even though nothing throws. Geometry/animation correctness is verified by eye per
/// project convention (see docs/filled-vector-rendering-plan.md §8/§9); the Inter-font fix
/// (ThemeDB.FallbackFont -> Inter) is pinned separately below by asserting the font resource itself
/// loads, since exercising DrawString's actual text output isn't reachable from a headless test.
/// </summary>
public class RacingHUDArcRendererTests : TestClass
{
	private const string InterFontPath = "res://_Core/Fonts/Inter/InterDisplay-SemiBold.ttf";

	private RacingHUDArcRenderer _hud;

	public RacingHUDArcRendererTests(Node testScene)
		: base(testScene)
	{
	}

	[Cleanup]
	public void Cleanup()
	{
		if (_hud != null && GodotObject.IsInstanceValid(_hud))
		{
			_hud.QueueFree();
		}

		_hud = null;
	}

	[Test]
	public void InterFontResource_ExistsAndLoads()
	{
		ResourceLoader.Exists(InterFontPath).ShouldBeTrue($"{InterFontPath} must exist for RacingHUDArcRenderer's DrawString calls to render in Inter, not the engine fallback font");

		var font = GD.Load<FontFile>(InterFontPath);
		font.ShouldNotBeNull();
	}

	[Test]
	public void Process_WithHudStatePushed_CommitsAndTessellatesShapesWithoutThrowing()
	{
		// Arrange
		_hud = new RacingHUDArcRenderer();
		TestScene.AddChild(_hud);
		_hud.UpdateHUDState(currentSpeed: 120f, maxSpeed: 200f, currentLap: 2, targetLaps: 3, lapProgress: 0.4f, timeText: "00:12.345");

		// Act & Assert
		Should.NotThrow(() => _hud._Process(0.016));
		Should.NotThrow(() => _hud.RebuildBuckets());

		// speedo bg+progress, needle shadow+needle, lap bg+progress, countdown bg+3 glow rings+progress
		_hud.ShapeCount.ShouldBe(11);
		_hud.TriangleCount.ShouldBeGreaterThan(0);
	}

	[Test]
	public void Process_CalledRepeatedlyWithUnchangedState_SettlesWithoutThrowing()
	{
		// Arrange
		_hud = new RacingHUDArcRenderer();
		TestScene.AddChild(_hud);
		_hud.UpdateHUDState(80f, 200f, 1, 3, 0.1f, "00:05.000");

		// Act & Assert - pins the HasVisualStateChanged/RecordDrawnState gate: repeated frames with
		// no state change must not throw or grow the shape count.
		for (int i = 0; i < 10; i++)
		{
			Should.NotThrow(() => _hud._Process(0.016));
		}

		Should.NotThrow(() => _hud.RebuildBuckets());
		_hud.ShapeCount.ShouldBe(11);
	}

	[Test]
	public void StartCountdown_ThenProcess_RendersCountdownRingWithoutThrowing()
	{
		// Arrange
		_hud = new RacingHUDArcRenderer();
		TestScene.AddChild(_hud);
		_hud.UpdateHUDState(0f, 200f, 1, 3, 0f, "00:00.000");

		// Act & Assert
		_hud.StartCountdown(3, 0.0f);
		Should.NotThrow(() => _hud._Process(0.016));
		Should.NotThrow(() => _hud.RebuildBuckets());

		_hud.HideCountdown();
		Should.NotThrow(() => _hud._Process(0.016));
		Should.NotThrow(() => _hud.RebuildBuckets());
	}
}
