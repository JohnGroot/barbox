using System.Collections.Generic;
using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Games.Racing.Tests;

/// <summary>
/// Smoke tests for RacingTrackRenderer against the real track scenes — especially CircuitA, the
/// track with the most divergent transform (unscaled root, rotated + scaled TrackLine, 5x-scaled
/// triggers), and so the one most likely to expose a transform-baking bug. Geometry correctness
/// is verified by eye per project convention (see docs/filled-vector-rendering-plan.md §9); this
/// pins that Setup() runs to completion on every real track without throwing.
/// </summary>
public class RacingTrackRendererTests : TestClass
{
	private static readonly string[] TrackScenePaths =
	[
		"res://_Games/RacingGame/Scenes/_Tracks/CircuitA.tscn",
		"res://_Games/RacingGame/Scenes/_Tracks/OvalTrack.tscn",
		"res://_Games/RacingGame/Scenes/_Tracks/GoCartTrack.tscn",
	];

	private readonly List<RacingTrackDefinition> _tracks = [];

	public RacingTrackRendererTests(Node testScene)
		: base(testScene)
	{
	}

	[Cleanup]
	public void Cleanup()
	{
		foreach (RacingTrackDefinition track in _tracks)
		{
			if (GodotObject.IsInstanceValid(track))
			{
				track.QueueFree();
			}
		}

		_tracks.Clear();
	}

	[Test]
	public void Setup_OnEveryRealTrack_RendersWithoutThrowingAndProducesGeometry()
	{
		foreach (string path in TrackScenePaths)
		{
			// Arrange
			var scene = GD.Load<PackedScene>(path);
			var track = scene.Instantiate<RacingTrackDefinition>();
			TestScene.AddChild(track);
			_tracks.Add(track);
			track.SetupTrack();

			var renderer = new RacingTrackRenderer();
			track.AddChild(renderer);

			// Act
			renderer.Setup(track);
			renderer.RebuildBuckets();

			// Assert
			renderer.ShapeCount.ShouldBeGreaterThan(0, $"{path} produced no shapes");
			renderer.TriangleCount.ShouldBeGreaterThan(0, $"{path} produced no triangles");
			track.TrackLine.Visible.ShouldBeFalse($"{path}: TrackLine must be hidden once the renderer takes over");
		}
	}

	[Test]
	public void Setup_OnEveryRealTrack_DoesNotDrawACheckpointStrokeOverTheStartLine()
	{
		// Arrange - every shipped track configures FinishLinePath identically to StartLinePath
		// (a looping track with no separate finish trigger), so StartLine and FinishLine resolve
		// to the same node. RenderStartLineAndCheckpoints must recognize the alias and skip
		// re-rendering that node as a checkpoint, or it draws a stroke over the checkered start
		// line and steals its VisualColorChanged hook.
		foreach (string path in TrackScenePaths)
		{
			var scene = GD.Load<PackedScene>(path);
			var track = scene.Instantiate<RacingTrackDefinition>();
			TestScene.AddChild(track);
			_tracks.Add(track);
			track.SetupTrack();

			track.StartLine.ShouldBeSameAs(track.FinishLine, $"{path}: this test's premise (aliased start/finish) no longer holds — revisit the skip condition in RenderStartLineAndCheckpoints");

			var renderer = new RacingTrackRenderer();
			track.AddChild(renderer);

			// Act
			renderer.Setup(track);

			// Assert - RenderCheckpointStroke is the only place that assigns VisualColorChanged;
			// it must not have run against the aliased start/finish trigger.
			track.StartLine.VisualColorChanged.ShouldBeNull($"{path}: the checkered start line's trigger must not have a checkpoint-stroke color hook attached");
		}
	}

	[Test]
	public void Setup_CircuitA_BakesTheAsphaltStrokeToTrackLinesActualScaledWidth()
	{
		// Arrange - CircuitA's TrackLine carries rotation 1.574 + scale 1.25 + a position offset;
		// the asphalt stroke's canvas-space Width must track TrackLine.Width * scale, not
		// TrackLine.Width unscaled, or the visible band would sit a fraction off from the
		// (untouched) collision geometry it is meant to match.
		var scene = GD.Load<PackedScene>("res://_Games/RacingGame/Scenes/_Tracks/CircuitA.tscn");
		var track = scene.Instantiate<RacingTrackDefinition>();
		TestScene.AddChild(track);
		_tracks.Add(track);
		track.SetupTrack();

		var renderer = new RacingTrackRenderer();
		track.AddChild(renderer);

		// Act
		renderer.Setup(track);
		renderer.RebuildBuckets();

		// Assert
		float expectedWidth = track.TrackLine.Width * track.TrackLine.GlobalTransform.Scale.X;
		Shape asphalt = renderer.StaticBucket.Shapes[0];
		asphalt.Stroke.Width.ShouldBe(expectedWidth, 0.01f, "The asphalt stroke must be committed first and carry the baked (not raw local) width");
	}
}
