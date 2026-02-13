# Editor Plugin 2D Handle Implementation

Proper coordinate transformation for custom 2D editor handles (bezier curves, paths, custom nodes).

## Coordinate Transformation Chain

The key is accessing the actual 2D editor viewport's transform, not the overlay control's transform:

```csharp
[Tool]
public partial class MyEditorPlugin : EditorPlugin
{
	private SubViewport _editor2DViewport;

	public override void _EnterTree()
	{
		_editor2DViewport = EditorInterface.Singleton.GetEditorViewport2D();
	}

	public override bool _ForwardCanvasGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton && _editor2DViewport != null)
		{
			var viewportPos = mouseButton.Position;
			var editorTransform = _editor2DViewport.GlobalCanvasTransform;
			var worldPos = editorTransform.AffineInverse() * viewportPos;
			var localPos = myNode.ToLocal(worldPos);
			return HandleInput(localPos);
		}
		return false;
	}

	public override void _ForwardCanvasDrawOverViewport(Control viewportControl)
	{
		if (_editor2DViewport != null)
		{
			var editorTransform = _editor2DViewport.GlobalCanvasTransform;
			var screenPos = editorTransform * myNode.GetPointGlobalPosition(0);
			viewportControl.DrawCircle(screenPos, 6.0f, Colors.Yellow);
		}
	}
}
```

## Critical Rules

1. **Never use `viewportControl.GetCanvasTransform()`** - Returns identity transform for overlay controls
2. **Always use `EditorInterface.Singleton.GetEditorViewport2D().GlobalCanvasTransform`** - Contains actual zoom/pan state
3. **Apply `AffineInverse()` to convert screen to world**: `transform.AffineInverse() * screenPos`
4. **Use same transform for both input detection and handle rendering** - Ensures visual consistency
5. **Account for zoom in selection threshold**: `12.0f / transform.GetScale().X`

## Common Mistakes

- Using overlay control's canvas transform (always identity)
- Using `mouseButton.GlobalPosition` (screen coordinates, not world)
- Different transforms for input vs rendering (causes handle misalignment)
- Fixed selection thresholds (don't scale with zoom level)
