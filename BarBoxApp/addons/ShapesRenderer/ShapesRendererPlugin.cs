#if TOOLS
using Godot;

[Tool]
public partial class ShapesRendererPlugin : EditorPlugin
{
	public override void _EnterTree()
	{
		// Register the Shapes compositor effect
		var script = GD.Load<Script>("res://addons/ShapesRenderer/src/Shapes.cs");
		var icon = EditorInterface.Singleton.GetBaseControl().GetThemeIcon(nameof(CompositorEffect), "EditorIcons");
		
		AddCustomType(nameof(Shapes), nameof(CompositorEffect), script, icon);
		
		GD.Print("[Shapes Renderer] Plugin initialized - Shapes CompositorEffect available");
	}

	public override void _ExitTree()
	{
		RemoveCustomType(nameof(Shapes));
		
		GD.Print("[Shapes Renderer] Plugin cleanup completed");
	}

	public override string _GetPluginName()
	{
		return "Shapes Renderer";
	}
}
#endif