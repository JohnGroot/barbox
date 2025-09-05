using Godot;
using ShapesRenderer.Effects;

namespace ShapesRenderer.Api;

/// <summary>
/// Public API for drawing shapes and polylines using the Shapes Renderer plugin.
/// This provides a convenient interface for drawing GPU-accelerated shapes.
/// </summary>
public static class ShapesAPI
{
	private static ShapesCompositorEffect _instance;
	
	/// <summary>
	/// Sets the ShapesCompositorEffect instance to use for drawing.
	/// This must be called before using any drawing methods.
	/// </summary>
	/// <param name="shapesEffect">The ShapesCompositorEffect instance to use</param>
	public static void Initialize(ShapesCompositorEffect shapesEffect)
	{
		_instance = shapesEffect;
	}
	
	/// <summary>
	/// Gets the currently active ShapesCompositorEffect instance.
	/// Returns null if Initialize() has not been called.
	/// </summary>
	public static ShapesCompositorEffect Instance => _instance;
	
	/// <summary>
	/// Draws a simple polyline with the specified points, color, and width.
	/// </summary>
	/// <param name="points">Array of 3D points that make up the line</param>
	/// <param name="color">Color of the line</param>
	/// <param name="width">Width of the line in pixels</param>
	public static void DrawPolyline(Vector3[] points, Color color, float width = 1.0f)
	{
		if (points == null || points.Length < 2)
		{
			GD.PrintErr("[ShapesAPI] DrawPolyline requires at least 2 points");
			return;
		}
		
		var builder = Polyline.Begin();
		builder.Points(points);
		builder.Color(color);
		builder.Width(width);
		builder.End();
	}
	
	/// <summary>
	/// Draws a polyline with gradient colors between each point.
	/// </summary>
	/// <param name="points">Array of 3D points that make up the line</param>
	/// <param name="colors">Array of colors for each point (should match points length)</param>
	/// <param name="width">Width of the line in pixels</param>
	public static void DrawPolylineGradient(Vector3[] points, Color[] colors, float width = 1.0f)
	{
		if (points == null || points.Length < 2)
		{
			GD.PrintErr("[ShapesAPI] DrawPolylineGradient requires at least 2 points");
			return;
		}
		
		if (colors == null || colors.Length == 0)
		{
			DrawPolyline(points, Colors.Magenta, width);
			return;
		}
		
		var builder = Polyline.Begin();
		builder.Points(points);
		builder.Colors(colors);
		builder.Width(width);
		builder.End();
	}
	
	/// <summary>
	/// Draws a polyline with variable width along its length.
	/// </summary>
	/// <param name="points">Array of 3D points that make up the line</param>
	/// <param name="color">Color of the line</param>
	/// <param name="widths">Array of widths for each point</param>
	public static void DrawPolylineVariableWidth(Vector3[] points, Color color, float[] widths)
	{
		if (points == null || points.Length < 2)
		{
			GD.PrintErr("[ShapesAPI] DrawPolylineVariableWidth requires at least 2 points");
			return;
		}
		
		if (widths == null || widths.Length == 0)
		{
			DrawPolyline(points, color, 1.0f);
			return;
		}
		
		var builder = Polyline.Begin();
		builder.Points(points);
		builder.Color(color);
		builder.Widths(widths);
		builder.End();
	}
	
	/// <summary>
	/// Draws a line between two points.
	/// </summary>
	/// <param name="start">Start point of the line</param>
	/// <param name="end">End point of the line</param>
	/// <param name="color">Color of the line</param>
	/// <param name="width">Width of the line in pixels</param>
	public static void DrawLine(Vector3 start, Vector3 end, Color color, float width = 1.0f)
	{
		DrawPolyline(new Vector3[] { start, end }, color, width);
	}
	
	/// <summary>
	/// Draws a line with gradient between two colors.
	/// </summary>
	/// <param name="start">Start point of the line</param>
	/// <param name="end">End point of the line</param>
	/// <param name="startColor">Color at the start of the line</param>
	/// <param name="endColor">Color at the end of the line</param>
	/// <param name="width">Width of the line in pixels</param>
	public static void DrawLineGradient(Vector3 start, Vector3 end, Color startColor, Color endColor, float width = 1.0f)
	{
		DrawPolylineGradient(
			[start, end],
			[startColor, endColor], 
			width
		);
	}
	
	/// <summary>
	/// Draws a rectangular outline in 3D space.
	/// </summary>
	/// <param name="center">Center point of the rectangle</param>
	/// <param name="size">Size of the rectangle (width, height)</param>
	/// <param name="color">Color of the outline</param>
	/// <param name="width">Width of the line in pixels</param>
	public static void DrawRectangleOutline(Vector3 center, Vector2 size, Color color, float width = 1.0f)
	{
		var halfSize = size * 0.5f;
		var points = new Vector3[]
		{
			center + new Vector3(-halfSize.X, -halfSize.Y, 0),
			center + new Vector3(halfSize.X, -halfSize.Y, 0),
			center + new Vector3(halfSize.X, halfSize.Y, 0),
			center + new Vector3(-halfSize.X, halfSize.Y, 0),
			center + new Vector3(-halfSize.X, -halfSize.Y, 0) // Close the rectangle
		};
		
		DrawPolyline(points, color, width);
	}
	
	/// <summary>
	/// Clears all currently batched polylines.
	/// This is called automatically after rendering but can be called manually if needed.
	/// </summary>
	public static void Clear()
	{
		Polyline.Reset();
	}
	
	/// <summary>
	/// Checks if the Shapes Renderer is available and working.
	/// </summary>
	/// <returns>True if the renderer is available, false otherwise</returns>
	public static bool IsAvailable()
	{
		return Instance != null && GodotObject.IsInstanceValid(Instance);
	}
}