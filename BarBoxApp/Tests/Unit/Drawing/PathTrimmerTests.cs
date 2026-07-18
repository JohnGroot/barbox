using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

public class PathTrimmerTests : TestClass
{
	private static readonly Vector2[] Line100 = [new(0f, 0f), new(100f, 0f)];

	private static readonly Vector2[] Square = [new(0f, 0f), new(100f, 0f), new(100f, 100f), new(0f, 100f)];

	private static readonly Vector2[] KinkedLine = [new(0f, 0f), new(10f, 0f), new(100f, 0f)];

	public PathTrimmerTests(Node testScene)
		: base(testScene)
	{
	}

	[Test]
	public void Trim_OnAnOpenPolyline_WindowsToTheRequestedFraction()
	{
		// Arrange
		var source = new FlatPath();
		PathFlattener.Polyline(Line100, closed: false, source);
		var output = new FlatPath();

		// Act
		bool ok = PathTrimmer.Trim(source, 0.25f, 0.75f, output);

		// Assert
		ok.ShouldBeTrue();
		output.Points[0].X.ShouldBe(25f, 0.01f);
		output.Points[output.Count - 1].X.ShouldBe(75f, 0.01f);
	}

	[Test]
	public void Trim_OnAClosedContour_SetsClosedFalse()
	{
		// Arrange
		var source = new FlatPath();
		PathFlattener.Polyline(Square, closed: true, source);
		var output = new FlatPath();

		// Act
		bool ok = PathTrimmer.Trim(source, 0.1f, 0.6f, output);

		// Assert
		ok.ShouldBeTrue();
		output.Closed.ShouldBeFalse("A trimmed closed contour becomes an open sub-path");
	}

	[Test]
	public void Trim_PreservesTheSourcesTValuesVerbatim()
	{
		// Arrange - the interior vertex sits at source T = 0.1 (10 of 100 units in)
		var source = new FlatPath();
		PathFlattener.Polyline(KinkedLine, closed: false, source);
		var output = new FlatPath();

		// Act
		bool ok = PathTrimmer.Trim(source, 0.05f, 0.95f, output);

		// Assert
		ok.ShouldBeTrue();
		bool foundSourceT = false;
		for (int i = 0; i < output.Count; i++)
		{
			if (Mathf.Abs(output.T[i] - 0.1f) < 0.01f)
			{
				foundSourceT = true;
			}
		}

		foundSourceT.ShouldBeTrue(
			"Interior vertex T must equal the source's own arc-length T, not a renormalized value " +
			"over the trimmed length - gradients must stay anchored to the untrimmed path");
	}

	[Test]
	public void Trim_StartGreaterThanOrEqualToEnd_ReturnsFalseAndClearsOutput()
	{
		// Arrange
		var source = new FlatPath();
		PathFlattener.Polyline(Line100, closed: false, source);
		var output = new FlatPath();
		output.Add(new Vector2(1f, 1f));

		// Act
		bool ok = PathTrimmer.Trim(source, 0.6f, 0.4f, output);

		// Assert
		ok.ShouldBeFalse();
		output.Count.ShouldBe(0, "A rejected trim must leave the output cleared, not stale");
	}

	[Test]
	public void Trim_OnATooShortSource_ReturnsFalse()
	{
		// Arrange
		var source = new FlatPath();
		source.Add(new Vector2(1f, 1f));
		source.FinalizeT();
		var output = new FlatPath();

		// Act
		bool ok = PathTrimmer.Trim(source, 0f, 1f, output);

		// Assert
		ok.ShouldBeFalse();
	}

	[Test]
	public void Trim_RangeOutsideZeroOne_ClampsThenValidates()
	{
		// Arrange - [1.5, 2.0] clamps to [1, 1], which collapses to zero length
		var source = new FlatPath();
		PathFlattener.Polyline(Line100, closed: false, source);
		var output = new FlatPath();

		// Act
		bool ok = PathTrimmer.Trim(source, 1.5f, 2.0f, output);

		// Assert
		ok.ShouldBeFalse();
	}
}
