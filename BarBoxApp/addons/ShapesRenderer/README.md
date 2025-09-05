# Shapes Renderer - GPU-Accelerated Shape Drawing for Godot 4.4

A high-performance Godot plugin for rendering smooth, anti-aliased polylines and shapes using GPU acceleration. Perfect for procedural graphics, debug visualization, data visualization, and real-time line drawing.

## Features

- 🚀 **GPU-Accelerated**: All rendering happens on the GPU using compute shaders
- ✨ **Anti-aliased**: Smooth, high-quality line rendering with rounded end caps
- 🎨 **Gradient Support**: Per-vertex colors with smooth interpolation using OKLab color space
- 📏 **Variable Width**: Different line widths along the polyline path
- 🎯 **High Performance**: Efficient batch rendering with minimal CPU overhead
- 🛠️ **Easy API**: Simple C# API for quick integration
- 🔧 **Editor Integration**: Works seamlessly in both editor and runtime

## Installation

1. Copy the `addons/shapes_renderer` folder to your project's `addons/` directory
2. Build the project to compile the C# code: `dotnet build`
3. Enable the plugin in Project Settings → Plugins → "Shapes Renderer"

## Quick Start

### Basic Setup

1. Add a `ShapesCompositorEffect` to your scene's rendering pipeline
2. Initialize the `ShapesAPI` with your compositor effect instance
3. Use the `ShapesAPI` class to draw shapes from your C# scripts

```csharp
using ShapesRenderer.Api;
using ShapesRenderer.Effects;
using Godot;

public partial class LineDrawer : Node
{
    public override void _Ready()
    {
        // Find and initialize the shapes renderer
        var shapesEffect = GetNode<ShapesCompositorEffect>("ShapesCompositorEffect");
        ShapesAPI.Initialize(shapesEffect);
        
        // Draw a simple line
        ShapesAPI.DrawLine(Vector3.Zero, Vector3.One, Colors.Red, 5.0f);
        
        // Draw a polyline with multiple points
        var points = new Vector3[]
        {
            new Vector3(0, 0, 0),
            new Vector3(1, 2, 0),
            new Vector3(3, 1, 0),
            new Vector3(4, 3, 0)
        };
        ShapesAPI.DrawPolyline(points, Colors.Blue, 3.0f);
    }
}
```

### Advanced Usage

```csharp
using ShapesRenderer.Api;
using ShapesRenderer.Effects;
using Godot;

public partial class AdvancedShapes : Node
{
    public override void _Ready()
    {
        // Initialize the shapes API
        var shapesEffect = GetNode<ShapesCompositorEffect>("ShapesCompositorEffect");
        ShapesAPI.Initialize(shapesEffect);
        
        DrawGradientLine();
        DrawVariableWidthLine();
        DrawRectangle();
    }
    
    private void DrawGradientLine()
    {
        var start = new Vector3(-2, 0, 0);
        var end = new Vector3(2, 0, 0);
        ShapesAPI.DrawLineGradient(start, end, Colors.Red, Colors.Blue, 8.0f);
    }
    
    private void DrawVariableWidthLine()
    {
        var points = new Vector3[]
        {
            new Vector3(-1, -1, 0),
            new Vector3(0, 1, 0),
            new Vector3(1, -1, 0)
        };
        var widths = new float[] { 2.0f, 10.0f, 2.0f };
        ShapesAPI.DrawPolylineVariableWidth(points, Colors.Green, widths);
    }
    
    private void DrawRectangle()
    {
        ShapesAPI.DrawRectangleOutline(Vector3.Zero, new Vector2(4, 2), Colors.Yellow, 3.0f);
    }
}
```

## API Reference

### ShapesAPI Class

The main interface for drawing shapes. All methods are static and can be called directly.

#### Basic Line Drawing

```csharp
// Draw a line between two points
ShapesAPI.DrawLine(Vector3 start, Vector3 end, Color color, float width = 1.0f)

// Draw a gradient line
ShapesAPI.DrawLineGradient(Vector3 start, Vector3 end, Color startColor, Color endColor, float width = 1.0f)
```

#### Polyline Drawing

```csharp
// Draw a polyline with uniform color and width
ShapesAPI.DrawPolyline(Vector3[] points, Color color, float width = 1.0f)

// Draw a polyline with gradient colors
ShapesAPI.DrawPolylineGradient(Vector3[] points, Color[] colors, float width = 1.0f)

// Draw a polyline with variable width
ShapesAPI.DrawPolylineVariableWidth(Vector3[] points, Color color, float[] widths)
```

#### Shape Drawing

```csharp
// Draw a rectangular outline
ShapesAPI.DrawRectangleOutline(Vector3 center, Vector2 size, Color color, float width = 1.0f)
```

#### Initialization

```csharp
// Initialize the API with a ShapesCompositorEffect instance (required before drawing)
ShapesAPI.Initialize(ShapesCompositorEffect shapesEffect)
```

#### Utility Methods

```csharp
// Check if the renderer is available
bool ShapesAPI.IsAvailable()

// Clear all current shapes (called automatically each frame)
ShapesAPI.Clear()
```

### Low-Level API (Polyline Class)

For advanced users who need more control:

```csharp
using ShapesRenderer.Effects;

// Use the builder pattern for complex polylines
var builder = Polyline.Begin();

// Add points
builder.Point(new Vector3(0, 0, 0));
builder.Point(new Vector3(1, 1, 0));

// Add colors (optional - will interpolate between colors)
builder.Color(Colors.Red);
builder.Color(Colors.Blue);

// Set width (optional - can vary along the line)
builder.Width(5.0f);

// Finish the polyline
builder.End();
```

## Performance Considerations

### Optimization Tips

1. **Batch your draws**: Group multiple `DrawPolyline` calls together rather than calling them spread across multiple frames
2. **Limit point count**: Very complex polylines (>1000 points) may impact performance
3. **Use appropriate widths**: Very wide lines (>50 pixels) require more GPU processing
4. **Color gradients**: Using uniform colors is slightly faster than gradients

### Technical Details

- **GPU Memory**: The plugin uses GPU storage buffers that automatically resize as needed
- **Rendering Pipeline**: Integrates with Godot's CompositorEffect system for post-processing
- **Shader Compilation**: Shaders are compiled once and cached for subsequent use
- **Memory Management**: Uses efficient linear arena allocation for CPU-side data structures

## Troubleshooting

### Common Issues

**Lines not appearing:**
- Ensure you have a `ShapesCompositorEffect` in your scene
- Check that the plugin is properly enabled and built
- Verify points are in the camera's view volume

**Performance issues:**
- Reduce the number of points in complex polylines
- Avoid drawing too many separate polylines per frame
- Consider using LOD (Level of Detail) for distant lines

**Colors look wrong:**
- The plugin uses OKLab color space for smooth interpolation
- Ensure colors are in linear space for best results

### Debug Information

Enable debug logging in the plugin to see performance and rendering information:

```csharp
// Check if shapes renderer is working
if (!ShapesAPI.IsAvailable())
{
    GD.PrintErr("Shapes Renderer not available - check plugin setup");
}
```

## Examples

### Real-time Graph Visualization

```csharp
public partial class GraphVisualizer : Node
{
    private List<Vector3> dataPoints = new();
    
    public override void _Ready()
    {
        // Generate sample data
        for (int i = 0; i < 100; i++)
        {
            float x = i * 0.1f;
            float y = Mathf.Sin(x) * 2.0f;
            dataPoints.Add(new Vector3(x, y, 0));
        }
    }
    
    public override void _Process(double delta)
    {
        // Draw animated graph
        var colors = new Color[dataPoints.Count];
        for (int i = 0; i < colors.Length; i++)
        {
            float t = i / (float)(colors.Length - 1);
            colors[i] = Color.FromHsv(t * 0.7f, 1.0f, 1.0f);
        }
        
        ShapesAPI.DrawPolylineGradient(dataPoints.ToArray(), colors, 4.0f);
    }
}
```

### Trail Effect

```csharp
public partial class TrailEffect : Node
{
    private Queue<Vector3> trailPoints = new();
    private int maxTrailLength = 20;
    
    public void AddTrailPoint(Vector3 point)
    {
        trailPoints.Enqueue(point);
        if (trailPoints.Count > maxTrailLength)
        {
            trailPoints.Dequeue();
        }
        
        if (trailPoints.Count > 1)
        {
            var points = trailPoints.ToArray();
            var widths = new float[points.Length];
            
            // Taper the trail from thick to thin
            for (int i = 0; i < widths.Length; i++)
            {
                float t = i / (float)(widths.Length - 1);
                widths[i] = Mathf.Lerp(1.0f, 8.0f, t);
            }
            
            ShapesAPI.DrawPolylineVariableWidth(points, Colors.Orange, widths);
        }
    }
}
```

## Technical Architecture

### Plugin Structure

```
addons/shapes_renderer/
├── plugin.cfg                     # Plugin metadata
├── ShapesRendererPlugin.cs       # Main plugin class
├── src/
│   ├── effects/
│   │   └── ShapesCompositorEffect.cs  # Core rendering effect
│   ├── api/
│   │   └── ShapesAPI.cs              # Public API
│   └── utils/
│       ├── LinearArena.cs            # Memory management
│       └── MemoryHelpers.cs          # Memory utilities
├── shaders/
│   ├── polyline.glsl            # Main polyline shader
│   └── oklab.glsl              # Color space utilities
└── README.md                   # This file
```

### Rendering Pipeline

1. **CPU Side**: Collect polyline data using builder pattern
2. **Batch Processing**: Group multiple polylines into GPU-friendly buffers
3. **GPU Upload**: Transfer vertex, color, and width data to GPU storage buffers
4. **Shader Rendering**: Process polylines in parallel with anti-aliasing
5. **Composite**: Blend results with scene using CompositorEffect system

## License

This plugin is part of the BarBox project. Please refer to the project's main license file for licensing information.

## Contributing

Contributions are welcome! Please ensure all changes maintain compatibility with Godot 4.4+ and follow the existing code style.

### Development Setup

1. Clone the repository and open in your preferred C# IDE
2. Ensure Godot 4.4+ is installed
3. Build the project with `dotnet build`
4. Test changes with the included example scenes

## Changelog

### Version 1.0.0 (Initial Release)
- GPU-accelerated polyline rendering
- Anti-aliased lines with rounded caps
- Gradient color support using OKLab interpolation
- Variable width line support
- High-level ShapesAPI for easy integration
- CompositorEffect integration for post-processing
- Comprehensive documentation and examples