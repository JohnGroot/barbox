using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

/// <summary>
/// Pins the formula itself — Pulse is the shared replacement for hand-rolled
/// bias + amplitude * sin(...) call sites (see RacingHUDArcRenderer), so a regression here would
/// silently change every consumer's animation.
/// </summary>
public class OscillatorTests : TestClass
{
	public OscillatorTests(Node testScene)
		: base(testScene)
	{
	}

	[Test]
	public void Pulse_AtZeroTimeZeroPhase_ReturnsBias()
	{
		// Arrange & Act
		float result = Oscillator.Pulse(time: 0f, frequency: 1f, bias: 0.8f, amplitude: 0.2f);

		// Assert
		result.ShouldBe(0.8f, 0.0001f, "sin(0) == 0, so the result is exactly bias");
	}

	[Test]
	public void Pulse_AtQuarterPeriod_ReturnsBiasPlusAmplitude()
	{
		// Arrange
		const float frequency = 2f;
		float quarterPeriod = (Mathf.Pi / 2f) / frequency;

		// Act
		float result = Oscillator.Pulse(quarterPeriod, frequency, bias: 1f, amplitude: 0.5f);

		// Assert
		result.ShouldBe(1.5f, 0.0001f, "sin peaks at pi/2, so the result is bias + amplitude");
	}

	[Test]
	public void Pulse_IsPeriodic_RepeatsAfterOneCycle()
	{
		// Arrange
		const float frequency = 3f;
		const float time = 0.7f;
		float onePeriodLater = time + (Mathf.Tau / frequency);

		// Act
		float first = Oscillator.Pulse(time, frequency, bias: 0.5f, amplitude: 0.4f, phaseRad: 1.1f);
		float second = Oscillator.Pulse(onePeriodLater, frequency, bias: 0.5f, amplitude: 0.4f, phaseRad: 1.1f);

		// Assert
		second.ShouldBe(first, 0.0001f, "advancing by a full period must reproduce the same value");
	}

	[Test]
	public void Pulse_ZeroAmplitude_IsConstantBias()
	{
		// Arrange
		const float Bias = 0.42f;

		// Act & Assert
		for (float t = 0f; t < 10f; t += 1.3f)
		{
			float result = Oscillator.Pulse(t, frequency: 5f, bias: Bias, amplitude: 0f);
			result.ShouldBe(Bias, 0.0001f);
		}
	}

	[Test]
	public void Pulse_MatchesManualSinCalculation()
	{
		// Arrange
		const float time = 2.4f;
		const float frequency = 4.0f;
		const float bias = 0.5f;
		const float amplitude = 0.5f;
		const float phase = 2.0f;
		float expected = bias + (amplitude * Mathf.Sin((time * frequency) + phase));

		// Act
		float result = Oscillator.Pulse(time, frequency, bias, amplitude, phase);

		// Assert
		result.ShouldBe(expected, 0.0001f);
	}
}
