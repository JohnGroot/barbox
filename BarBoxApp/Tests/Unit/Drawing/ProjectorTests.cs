using System;
using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

public class ProjectorTests : TestClass
{
	private const float Tolerance = 0.0001f;

	public ProjectorTests(Node testScene)
		: base(testScene)
	{
	}

	[Test]
	public void Project_IsometricWorldUp_MapsToNegativeScreenY()
	{
		// Arrange - Godot 2D is Y-down; getting this backwards mirrors every wireframe
		var projector = Projector.Isometric(scale: 1f);

		// Act
		var projected = projector.Project(Vector3.Up);

		// Assert
		projected.Y.ShouldBeLessThan(0f, "World up should project to negative (upward) screen Y");
		projected.X.ShouldBe(0f, Tolerance, "World up should not deflect horizontally at any azimuth");
	}

	[Test]
	public void Project_IsometricUnitAxes_MatchClassicIsometricLayout()
	{
		// Arrange - the 30/45 default: +X right-and-down, +Z left-and-down, +Y up
		var projector = Projector.Isometric(scale: 1f);

		// Right lies in the XZ plane, so the basis works out to Right = (cos az, 0, -sin az)
		// and Up.Y = cos elevation. The horizontal axes foreshorten by sin/cos az; the
		// vertical one does not.
		float expectedHorizontal = Mathf.Sin(Mathf.DegToRad(45f));
		float expectedVertical = Mathf.Cos(Mathf.DegToRad(30f));

		// Act
		var x = projector.Project(new Vector3(1f, 0f, 0f));
		var y = projector.Project(new Vector3(0f, 1f, 0f));
		var z = projector.Project(new Vector3(0f, 0f, 1f));

		// Assert
		float expectedDrop = Mathf.Sin(Mathf.DegToRad(30f)) * expectedHorizontal;

		x.X.ShouldBe(expectedHorizontal, Tolerance, "+X should read to the screen right");
		x.Y.ShouldBe(expectedDrop, Tolerance, "+X should read downward");

		y.X.ShouldBe(0f, Tolerance);
		y.Y.ShouldBe(-expectedVertical, Tolerance, "+Y should read straight up");

		z.X.ShouldBe(-expectedHorizontal, Tolerance, "+Z should read to the screen left");
		z.Y.ShouldBe(expectedDrop, Tolerance, "+Z should read downward");
	}

	[Test]
	public void Project_IsometricOrigin_MapsToScreenOrigin()
	{
		// Arrange
		var projector = Projector.Isometric(scale: 40f);

		// Act
		var projected = projector.Project(Vector3.Zero);

		// Assert
		projected.X.ShouldBe(0f, Tolerance);
		projected.Y.ShouldBe(0f, Tolerance);
	}

	[Test]
	public void Project_IsometricScaleZero_CollapsesEverythingToOrigin()
	{
		// Arrange
		var projector = Projector.Isometric(scale: 0f);

		// Act
		var projected = projector.Project(new Vector3(100f, -50f, 25f));

		// Assert
		projected.ShouldBe(Vector2.Zero);
	}

	[Test]
	public void Project_IsometricScale_ScalesLinearly()
	{
		// Arrange
		var single = Projector.Isometric(scale: 1f);
		var quadruple = Projector.Isometric(scale: 4f);
		var point = new Vector3(3f, 2f, 1f);

		// Act
		var a = single.Project(point);
		var b = quadruple.Project(point);

		// Assert
		b.X.ShouldBe(a.X * 4f, Tolerance);
		b.Y.ShouldBe(a.Y * 4f, Tolerance);
	}

	[Test]
	public void Project_PerspectiveFartherSegment_ForeshortensMore()
	{
		// Arrange - two equal-length world segments at different depths
		var projector = Projector.Perspective(
			camPos: new Vector3(0f, 0f, -10f),
			lookAt: Vector3.Zero,
			focalLength: 1f,
			scale: 1f);

		// Act
		var nearA = projector.Project(new Vector3(-1f, 0f, 0f));
		var nearB = projector.Project(new Vector3(1f, 0f, 0f));
		var farA = projector.Project(new Vector3(-1f, 0f, 100f));
		var farB = projector.Project(new Vector3(1f, 0f, 100f));

		// Assert
		float nearLength = nearA.DistanceTo(nearB);
		float farLength = farA.DistanceTo(farB);
		farLength.ShouldBeLessThan(nearLength, "The farther segment should project shorter");
		farLength.ShouldBeGreaterThan(0f);
	}

	[Test]
	public void Project_PerspectivePointOnViewAxis_MapsToPrincipalPoint()
	{
		// Arrange
		var projector = Projector.Perspective(
			camPos: new Vector3(0f, 0f, -10f),
			lookAt: Vector3.Zero,
			focalLength: 1f,
			scale: 1f);

		// Act
		var projected = projector.Project(new Vector3(0f, 0f, 50f));

		// Assert
		projected.X.ShouldBe(0f, Tolerance);
		projected.Y.ShouldBe(0f, Tolerance);
	}

	[Test]
	public void Project_PerspectivePointBehindCamera_ClampsWithoutNaN()
	{
		// Arrange - no near-plane clipping in v1; the contract is only that it stays finite
		var projector = Projector.Perspective(
			camPos: new Vector3(0f, 0f, -10f),
			lookAt: Vector3.Zero,
			focalLength: 1f,
			scale: 1f);

		// Act
		var projected = projector.Project(new Vector3(1f, 1f, -50f));

		// Assert
		float.IsFinite(projected.X).ShouldBeTrue("Geometry behind the camera must not produce NaN");
		float.IsFinite(projected.Y).ShouldBeTrue();
	}

	[Test]
	public void Project_PerspectiveWorldUp_MapsToNegativeScreenY()
	{
		// Arrange
		var projector = Projector.Perspective(
			camPos: new Vector3(0f, 0f, -10f),
			lookAt: Vector3.Zero,
			focalLength: 1f,
			scale: 1f);

		// Act
		var projected = projector.Project(new Vector3(0f, 1f, 0f));

		// Assert
		projected.Y.ShouldBeLessThan(0f, "World up should project upward on a Y-down canvas");
	}

	[Test]
	public void WithYawPitch_Zero_LeavesProjectionUnchanged()
	{
		// Arrange
		var projector = Projector.Isometric(scale: 10f);
		var point = new Vector3(1f, 2f, 3f);
		var before = projector.Project(point);

		// Act
		var after = projector.WithYawPitch(0f, 0f).Project(point);

		// Assert
		after.X.ShouldBe(before.X, Tolerance);
		after.Y.ShouldBe(before.Y, Tolerance);
	}

	[Test]
	public void WithYawPitch_AppliedTwice_MatchesSingleCompositionOfTotalAngles()
	{
		// Arrange - a turntable steps this every frame, so it must not accumulate drift
		var projector = Projector.Isometric(scale: 10f);
		var point = new Vector3(1f, 2f, 3f);

		// Act
		var stepped = projector.WithYawPitch(0.2f, 0.1f).WithYawPitch(0.2f, 0.1f).Project(point);
		var direct = projector.WithYawPitch(0.4f, 0.2f).Project(point);

		// Assert
		stepped.X.ShouldBe(direct.X, Tolerance, "Two steps should equal one combined rotation");
		stepped.Y.ShouldBe(direct.Y, Tolerance);
	}

	[Test]
	public void WithYawPitch_ManySmallSteps_DoesNotDrift()
	{
		// Arrange
		var projector = Projector.Isometric(scale: 10f);
		var point = new Vector3(1f, 2f, 3f);
		var stepped = projector;

		// Act
		for (int i = 0; i < 100; i++)
		{
			stepped = stepped.WithYawPitch(0.01f, 0f);
		}

		// Assert
		var direct = projector.WithYawPitch(1f, 0f).Project(point);
		var actual = stepped.Project(point);
		actual.X.ShouldBe(direct.X, 0.001f, "100 steps should not drift from one combined rotation");
		actual.Y.ShouldBe(direct.Y, 0.001f);
	}

	[Test]
	public void WithYawPitch_PerspectiveYaw_OrbitsCameraAtConstantRadius()
	{
		// Arrange
		var projector = Projector.Perspective(
			camPos: new Vector3(0f, 0f, -10f),
			lookAt: Vector3.Zero,
			focalLength: 1f,
			scale: 1f);
		float radiusBefore = (projector.CamPos - projector.LookAt).Length();

		// Act
		var orbited = projector.WithYawPitch(0.5f, 0.2f);

		// Assert
		float radiusAfter = (orbited.CamPos - orbited.LookAt).Length();
		radiusAfter.ShouldBe(radiusBefore, 0.001f, "Yaw/pitch should orbit, not dolly");
		orbited.LookAt.ShouldBe(projector.LookAt);
	}

	[Test]
	public void Project_IsometricTopDown_StaysFiniteAtThePole()
	{
		// Arrange - elevation 90 makes Forward parallel to world up, collapsing the naive basis
		var projector = Projector.Isometric(scale: 1f, elevationDeg: 90f, azimuthDeg: 0f);

		// Act
		var projected = projector.Project(new Vector3(1f, 0f, 1f));

		// Assert
		float.IsFinite(projected.X).ShouldBeTrue("The gimbal guard should keep the pole well-defined");
		float.IsFinite(projected.Y).ShouldBeTrue();
	}

	[Test]
	public void ProjectMany_IntoSpan_MatchesPerPointProject()
	{
		// Arrange
		var projector = Projector.Isometric(scale: 3f);
		var source = new[] { Vector3.Zero, new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 1f) };
		var destination = new Vector2[3];

		// Act
		projector.ProjectMany(source, destination);

		// Assert
		for (int i = 0; i < source.Length; i++)
		{
			destination[i].ShouldBe(projector.Project(source[i]));
		}
	}

	[Test]
	public void ProjectMany_IntoFlatPath_FillsPointsAndNormalizedT()
	{
		// Arrange
		var projector = Projector.Isometric(scale: 1f);
		var source = new[] { Vector3.Zero, new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 0f) };
		var path = new FlatPath();

		// Act
		projector.ProjectMany(source, closed: false, path);

		// Assert
		path.Count.ShouldBe(3);
		path.Closed.ShouldBeFalse();
		path.T[0].ShouldBe(0f, Tolerance);
		path.T[2].ShouldBe(1f, Tolerance);
		path.Points[0].ShouldBe(projector.Project(source[0]));
	}
}
