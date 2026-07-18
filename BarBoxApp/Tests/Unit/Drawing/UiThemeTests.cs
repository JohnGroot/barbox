using BarBox.Core.Drawing;
using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace BarBox.Tests.Unit.Drawing;

public class UiThemeTests : TestClass
{
	public UiThemeTests(Node testScene)
		: base(testScene)
	{
	}

	[Test]
	public void OutlineButton_Normal_HasTransparentBackgroundAndAccentBorder()
	{
		// Arrange & Act
		UiTheme.OutlineButtonBoxes boxes = UiTheme.OutlineButton(Palette.Blue);

		// Assert
		boxes.Normal.BgColor.ShouldBe(Colors.Transparent);
		boxes.Normal.BorderColor.ShouldBe(Palette.Blue);
	}

	[Test]
	public void OutlineButton_Hover_FillsWithLowAlphaAccent()
	{
		// Arrange & Act
		UiTheme.OutlineButtonBoxes boxes = UiTheme.OutlineButton(Palette.Blue);

		// Assert
		boxes.Hover.BgColor.A.ShouldBe(0.15f, 0.001f);
		boxes.Hover.BgColor.R.ShouldBe(Palette.Blue.R, 0.001f);
		boxes.Hover.BgColor.G.ShouldBe(Palette.Blue.G, 0.001f);
		boxes.Hover.BgColor.B.ShouldBe(Palette.Blue.B, 0.001f);
	}

	[Test]
	public void OutlineButton_Pressed_FillsSolidWithAccent()
	{
		// Arrange & Act
		UiTheme.OutlineButtonBoxes boxes = UiTheme.OutlineButton(Palette.Blue);

		// Assert
		boxes.Pressed.BgColor.ShouldBe(Palette.Blue);
	}

	[Test]
	public void OutlineButton_Disabled_HasReducedBorderAlpha()
	{
		// Arrange & Act
		UiTheme.OutlineButtonBoxes boxes = UiTheme.OutlineButton(Palette.Blue);

		// Assert
		boxes.Disabled.BgColor.ShouldBe(Colors.Transparent);
		boxes.Disabled.BorderColor.A.ShouldBe(0.35f, 0.001f);
	}

	[Test]
	public void OutlineButton_AllFourStates_ShareTheSameBorderWidthAndCornerRadius()
	{
		// Arrange & Act
		UiTheme.OutlineButtonBoxes boxes = UiTheme.OutlineButton(Palette.Blue);

		// Assert
		foreach (StyleBoxFlat box in new[] { boxes.Normal, boxes.Hover, boxes.Pressed, boxes.Disabled })
		{
			box.BorderWidthLeft.ShouldBe(2);
			box.BorderWidthRight.ShouldBe(2);
			box.BorderWidthTop.ShouldBe(2);
			box.BorderWidthBottom.ShouldBe(2);
			box.CornerRadiusTopLeft.ShouldBe(10);
			box.CornerRadiusTopRight.ShouldBe(10);
			box.CornerRadiusBottomLeft.ShouldBe(10);
			box.CornerRadiusBottomRight.ShouldBe(10);
		}
	}

	[Test]
	public void PanelBox_HasFlatBackgroundAndHairlineBorder()
	{
		// Arrange & Act
		StyleBoxFlat box = UiTheme.PanelBox();

		// Assert
		box.BgColor.ShouldBe(Palette.Panel);
		box.BorderColor.ShouldBe(Palette.Grid);
		box.BorderWidthLeft.ShouldBe(1);
		box.CornerRadiusTopLeft.ShouldBe(8);
	}

	[Test]
	public void ModalBox_HasWiderBorderAndLargerCornerRadiusThanPanelBox()
	{
		// Arrange
		StyleBoxFlat panel = UiTheme.PanelBox();

		// Act
		StyleBoxFlat modal = UiTheme.ModalBox();

		// Assert
		modal.BgColor.ShouldBe(Palette.PanelRaised);
		modal.BorderWidthLeft.ShouldBeGreaterThan(panel.BorderWidthLeft);
		modal.CornerRadiusTopLeft.ShouldBeGreaterThan(panel.CornerRadiusTopLeft);
	}

	[Test]
	public void ApplyOutlineButton_SetsAllFourStyleboxOverridesOnTheButton()
	{
		// Arrange
		var button = new Button();

		// Act
		UiTheme.ApplyOutlineButton(button, Palette.Blue);

		// Assert
		(button.GetThemeStylebox("normal") as StyleBoxFlat).ShouldNotBeNull();
		(button.GetThemeStylebox("normal") as StyleBoxFlat).BorderColor.ShouldBe(Palette.Blue);
		(button.GetThemeStylebox("hover") as StyleBoxFlat).ShouldNotBeNull();
		(button.GetThemeStylebox("pressed") as StyleBoxFlat).ShouldNotBeNull();
		(button.GetThemeStylebox("disabled") as StyleBoxFlat).ShouldNotBeNull();

		// Cleanup
		button.Free();
	}

	[Test]
	public void ApplyOutlineButton_SetsFontColorOverridesToTheAccent()
	{
		// Arrange
		var button = new Button();

		// Act
		UiTheme.ApplyOutlineButton(button, Palette.Blue);

		// Assert
		button.GetThemeColor("font_color").ShouldBe(Palette.Blue);
		button.GetThemeColor("font_hover_color").ShouldBe(Palette.Blue);
		button.GetThemeColor("font_pressed_color").ShouldBe(Palette.Ink);
		button.GetThemeColor("font_disabled_color").ShouldBe(Palette.EdgeGray);

		// Cleanup
		button.Free();
	}
}
