using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Games.Racing.Tests;

/// <summary>
/// Smoke tests for RacingVisualFeedbackRenderer's M5 ShapeCanvas migration — pins that the
/// per-frame update path (EnsureShapesCommitted -> PushInputLine/PushMouseIndicators/PushTrailLine)
/// runs to completion without throwing across both the "no input" and "active target" branches, and
/// that repeated frames don't re-commit shapes. Geometry correctness is verified by eye per project
/// convention (see docs/filled-vector-rendering-plan.md §9).
/// </summary>
public class RacingVisualFeedbackRendererTests : TestClass
{
	private RacingCar _car;
	private RacingCameraController _cameraController;
	private RacingTrackValidationSystem _validationSystem;
	private RacingVisualFeedbackRenderer _renderer;

	public RacingVisualFeedbackRendererTests(Node testScene)
		: base(testScene)
	{
	}

	[Cleanup]
	public void Cleanup()
	{
		foreach (Node node in new Node[] { _renderer, _car, _validationSystem, _cameraController })
		{
			if (node != null && GodotObject.IsInstanceValid(node))
			{
				node.QueueFree();
			}
		}

		_renderer = null;
		_car = null;
		_validationSystem = null;
		_cameraController = null;
	}

	// RacingVisualFeedbackRenderer.UpdateVisualFeedback/UpdateTireTrails early-return unless
	// RacingCar.IsInitialized (camera controller + validation system both non-null), so a real
	// (if minimally set up) car is required to exercise the push paths at all.
	private RacingCar CreateInitializedCar()
	{
		_cameraController = new RacingCameraController();
		TestScene.AddChild(_cameraController);
		_cameraController.Initialize();

		_validationSystem = new RacingTrackValidationSystem();
		TestScene.AddChild(_validationSystem);

		_car = new RacingCar();
		TestScene.AddChild(_car);
		_car.Initialize(_cameraController, _validationSystem, "player1", "Player");

		return _car;
	}

	[Test]
	public void UpdateVisualFeedback_OnFirstCall_CommitsAllFiveShapesWithoutThrowing()
	{
		// Arrange
		CreateInitializedCar();
		_renderer = new RacingVisualFeedbackRenderer();
		TestScene.AddChild(_renderer);
		_renderer.Initialize(_car);

		// Act
		Should.NotThrow(() => _renderer.UpdateVisualFeedback(0.016f));
		Should.NotThrow(() => _renderer.UpdateTireTrails());

		// Assert - left trail, right trail, input line, touch stationary indicator, touch arc
		_renderer.ShapeCount.ShouldBe(5);
	}

	[Test]
	public void UpdateVisualFeedback_WithActiveTarget_PushesInputLineWithoutThrowing()
	{
		// Arrange
		CreateInitializedCar();
		_car.SetTargetPosition(new Vector2(150f, -60f));
		_renderer = new RacingVisualFeedbackRenderer();
		TestScene.AddChild(_renderer);
		_renderer.Initialize(_car);

		// Act & Assert - exercises PushInputLine's "has target" branch (targetPosition != Vector2.Zero)
		Should.NotThrow(() => _renderer.UpdateVisualFeedback(0.016f));
		Should.NotThrow(() => _renderer.UpdateTireTrails());
		_renderer.ShapeCount.ShouldBe(5);
	}

	[Test]
	public void UpdateVisualFeedback_AcrossMultipleFrames_StaysStableWithoutRecommittingShapes()
	{
		// Arrange
		CreateInitializedCar();
		_renderer = new RacingVisualFeedbackRenderer();
		TestScene.AddChild(_renderer);
		_renderer.Initialize(_car);

		// Act
		for (int i = 0; i < 5; i++)
		{
			Should.NotThrow(() => _renderer.UpdateVisualFeedback(0.016f));
			Should.NotThrow(() => _renderer.UpdateTireTrails());
		}

		// Assert
		_renderer.ShapeCount.ShouldBe(5);
	}
}
