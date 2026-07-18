using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Games.Racing.Tests;

/// <summary>
/// Smoke tests for RacingCar's M5 ShapeCanvas migration (body fill + windshield line + headlight
/// cone) — pins that SetupCarBody commits its shapes without throwing and that headlight mutation
/// reuses the committed shape rather than re-committing. Geometry correctness is verified by eye
/// per project convention (see docs/filled-vector-rendering-plan.md §9).
/// </summary>
public class RacingCarTests : TestClass
{
	private RacingCar _car;

	public RacingCarTests(Node testScene)
		: base(testScene)
	{
	}

	[Cleanup]
	public void Cleanup()
	{
		if (_car != null && GodotObject.IsInstanceValid(_car))
		{
			_car.QueueFree();
		}

		_car = null;
	}

	// RacingCar keeps its visual canvas as a protected field on the CharacterBody2D child rather
	// than exposing it publicly, so tests reach it the same way any other external observer would:
	// by walking the car body's children.
	private static ShapeCanvas FindVisualCanvas(RacingCar car)
	{
		foreach (Node child in car.GetCarBody().GetChildren())
		{
			if (child is ShapeCanvas canvas)
			{
				return canvas;
			}
		}

		return null;
	}

	[Test]
	public void Ready_BuildsBodyWindshieldAndHeadlightShapesWithoutThrowing()
	{
		// Arrange & Act
		_car = new RacingCar();
		TestScene.AddChild(_car);

		// Assert
		ShapeCanvas canvas = FindVisualCanvas(_car);
		canvas.ShouldNotBeNull("RacingCar must attach its visual ShapeCanvas to the CharacterBody2D");
		canvas.ShapeCount.ShouldBe(3, "body fill+outline, windshield line, and headlight cone (HeadlightEnabled defaults true)");
	}

	[Test]
	public void UpdateHeadlightSettings_Enabled_MutatesExistingShapeRatherThanRecommitting()
	{
		// Arrange
		_car = new RacingCar();
		TestScene.AddChild(_car);
		ShapeCanvas canvas = FindVisualCanvas(_car);
		int shapeCountBefore = canvas.ShapeCount;

		// Act
		Should.NotThrow(() => _car.UpdateHeadlightSettings(true, range: 300f, width: 60f, color: Palette.Yellow));

		// Assert
		canvas.ShapeCount.ShouldBe(shapeCountBefore);
	}

	[Test]
	public void UpdateHeadlightSettings_Disabled_HidesHeadlightShapeWithoutThrowing()
	{
		// Arrange
		_car = new RacingCar();
		TestScene.AddChild(_car);

		// Act & Assert
		Should.NotThrow(() => _car.UpdateHeadlightSettings(false, range: 200f, width: 47f, color: RacingPalette.HeadlightGlow));
	}
}
