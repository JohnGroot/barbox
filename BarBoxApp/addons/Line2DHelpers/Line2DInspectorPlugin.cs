#if TOOLS
using Godot;

[Tool]
public partial class Line2DInspectorPlugin : EditorInspectorPlugin
{
	public override bool _CanHandle(GodotObject obj)
	{
		return obj is Line2D;
	}

	public override void _ParseBegin(GodotObject obj)
	{
		if (obj is Line2D line2D)
		{
			var panel = new Line2DSmoothingPanel();
			panel.Initialize(line2D);
			AddCustomControl(panel);
		}
	}
}
#endif
