#if TOOLS
using Godot;
using ShapesRenderer.Effects;

[Tool]
public partial class ShapesRendererPlugin : EditorPlugin
{
	private const string SHAPES_COMPOSITOR_EFFECT_NAME = "ShapesCompositorEffect";
	private const string SHAPES_COMPOSITOR_EFFECT_BASE_TYPE = "CompositorEffect";

	public override void _EnterTree()
	{
		// Add custom type for ShapesCompositorEffect
		var script = GD.Load<Script>("res://addons/ShapesRenderer/src/Effects/ShapesCompositorEffect.cs");
		var icon = EditorInterface.Singleton.GetBaseControl().GetThemeIcon(SHAPES_COMPOSITOR_EFFECT_BASE_TYPE, "EditorIcons");
		
		AddCustomType(
			SHAPES_COMPOSITOR_EFFECT_NAME,
			SHAPES_COMPOSITOR_EFFECT_BASE_TYPE,
			script,
			icon
		);

		GD.Print("[Shapes Renderer] Plugin initialized - ShapesCompositorEffect available in Create dialog");
	}

	public override void _ExitTree()
	{
		// Remove custom type
		RemoveCustomType(SHAPES_COMPOSITOR_EFFECT_NAME);
		
		GD.Print("[Shapes Renderer] Plugin cleanup completed");
	}

	public override string _GetPluginName()
	{
		return "Shapes Renderer";
	}
}
#endif