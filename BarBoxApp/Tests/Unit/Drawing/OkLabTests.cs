using System;
using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

public class OkLabTests : TestClass
{
	public OkLabTests(Node testScene)
		: base(testScene)
	{
	}

	[Test]
	public void FromLinear_White_ProducesLightnessOneWithZeroChroma()
	{
		// Arrange - the canonical anchor of the space; a transposed matrix port fails here
		var white = new Vector3(1f, 1f, 1f);

		// Act
		var lab = OkLab.FromLinear(white);

		// Assert
		lab.X.ShouldBe(1f, 0.001f, "Linear white should have OKLab lightness 1");
		lab.Y.ShouldBe(0f, 0.001f, "Linear white should be achromatic on the a axis");
		lab.Z.ShouldBe(0f, 0.001f, "Linear white should be achromatic on the b axis");
	}

	[Test]
	public void FromLinear_Red_MatchesOttossonReference()
	{
		// Arrange
		var red = new Vector3(1f, 0f, 0f);

		// Act
		var lab = OkLab.FromLinear(red);

		// Assert
		lab.X.ShouldBe(0.62796f, 0.001f);
		lab.Y.ShouldBe(0.22486f, 0.001f);
		lab.Z.ShouldBe(0.12585f, 0.001f);
	}

	[Test]
	public void ToLinear_FromLinearRoundTrip_RecoversInput()
	{
		// Arrange
		var samples = new[]
		{
			new Vector3(0f, 0f, 0f),
			new Vector3(1f, 1f, 1f),
			new Vector3(1f, 0f, 0f),
			new Vector3(0.2f, 0.5f, 0.8f),
		};

		foreach (var linear in samples)
		{
			// Act
			var recovered = OkLab.ToLinear(OkLab.FromLinear(linear));

			// Assert
			recovered.X.ShouldBe(linear.X, 0.001f);
			recovered.Y.ShouldBe(linear.Y, 0.001f);
			recovered.Z.ShouldBe(linear.Z, 0.001f);
		}
	}

	[Test]
	public void FromSrgb_ToSrgbRoundTrip_WithinEpsilonAcrossGamut()
	{
		// Arrange - the 8-bit cube at stride 17 (16^3 = 4096 colors)
		const float Epsilon = 0.001f;
		var worst = 0f;

		for (int r = 0; r <= 255; r += 17)
		{
			for (int g = 0; g <= 255; g += 17)
			{
				for (int b = 0; b <= 255; b += 17)
				{
					var original = Color.Color8((byte)r, (byte)g, (byte)b);

					// Act
					var recovered = OkLab.ToSrgb(OkLab.FromSrgb(original), original.A);

					// Assert
					worst = Math.Max(worst, Math.Abs(recovered.R - original.R));
					worst = Math.Max(worst, Math.Abs(recovered.G - original.G));
					worst = Math.Max(worst, Math.Abs(recovered.B - original.B));
				}
			}
		}

		worst.ShouldBeLessThan(Epsilon, "sRGB round trip through OKLab should be lossless within epsilon");
	}

	[Test]
	public void Mix_BlueToYellowMidpoint_StaysSaturated()
	{
		// Arrange - an sRGB lerp collapses this midpoint to gray; avoiding that is why OKLab exists
		var blue = Colors.Blue;
		var yellow = Colors.Yellow;

		// Act
		var mid = OkLab.Mix(blue, yellow, 0.5f);

		// Assert
		var lab = OkLab.FromSrgb(mid);
		var chroma = MathF.Sqrt((lab.Y * lab.Y) + (lab.Z * lab.Z));
		chroma.ShouldBeGreaterThan(0.05f, "OKLab midpoint of blue and yellow should not be gray");
	}

	[Test]
	public void Mix_AtEndpoints_ReturnsInputsExactly()
	{
		// Arrange
		var a = new Color(0.1f, 0.6f, 0.9f, 0.8f);
		var b = new Color(0.9f, 0.2f, 0.1f, 0.4f);

		// Act
		var atZero = OkLab.Mix(a, b, 0f);
		var atOne = OkLab.Mix(a, b, 1f);

		// Assert
		atZero.ShouldBe(a, "t=0 should return the first endpoint bit-exact");
		atOne.ShouldBe(b, "t=1 should return the second endpoint bit-exact");
	}

	[Test]
	public void Mix_Midpoint_LerpsAlphaLinearly()
	{
		// Arrange
		var a = new Color(1f, 0f, 0f, 0.2f);
		var b = new Color(0f, 0f, 1f, 1f);

		// Act
		var mid = OkLab.Mix(a, b, 0.5f);

		// Assert
		mid.A.ShouldBe(0.6f, 0.001f, "Alpha should lerp linearly, not through OKLab");
	}

	[Test]
	public void Sample_AtStopPositions_ReturnsStopColorsExactly()
	{
		// Arrange
		var stops = new[]
		{
			new ColorStop(0f, Colors.Red),
			new ColorStop(0.5f, Colors.Green),
			new ColorStop(1f, Colors.Blue),
		};

		// Act
		var atStart = OkLab.Sample(stops, 0f);
		var atMid = OkLab.Sample(stops, 0.5f);
		var atEnd = OkLab.Sample(stops, 1f);

		// Assert
		atStart.ShouldBe(Colors.Red);
		atMid.ShouldBe(Colors.Green);
		atEnd.ShouldBe(Colors.Blue);
	}

	[Test]
	public void Sample_OutsideStopRange_ClampsToEndStops()
	{
		// Arrange
		var stops = new[]
		{
			new ColorStop(0.25f, Colors.Red),
			new ColorStop(0.75f, Colors.Blue),
		};

		// Act
		var below = OkLab.Sample(stops, 0f);
		var above = OkLab.Sample(stops, 1f);

		// Assert
		below.ShouldBe(Colors.Red, "Below the first stop should clamp, not extrapolate");
		above.ShouldBe(Colors.Blue, "Above the last stop should clamp, not extrapolate");
	}

	[Test]
	public void Sample_BetweenStops_MatchesDirectMix()
	{
		// Arrange
		var stops = new[]
		{
			new ColorStop(0f, Colors.Red),
			new ColorStop(1f, Colors.Blue),
		};

		// Act
		var sampled = OkLab.Sample(stops, 0.3f);

		// Assert
		sampled.ShouldBe(OkLab.Mix(Colors.Red, Colors.Blue, 0.3f));
	}

	[Test]
	public void Sample_NullOrSingleStop_ReturnsSafeValue()
	{
		// Arrange
		var single = new[] { new ColorStop(0.4f, Colors.Green) };

		// Act
		var fromNull = OkLab.Sample(null, 0.5f);
		var fromEmpty = OkLab.Sample([], 0.5f);
		var fromSingle = OkLab.Sample(single, 0.9f);

		// Assert
		fromNull.ShouldBe(Colors.Magenta, "Null stops should fall back visibly rather than throw");
		fromEmpty.ShouldBe(Colors.Magenta);
		fromSingle.ShouldBe(Colors.Green, "A single stop is constant across all t");
	}
}
