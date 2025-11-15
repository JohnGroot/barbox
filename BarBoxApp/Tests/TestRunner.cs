using Chickensoft.GoDotTest;
using Godot;

/// <summary>
/// Test runner entry point for GoDotTest framework.
/// This scene is loaded when --run-tests is passed on the command line.
/// </summary>
public partial class TestRunner : Node2D
{
	public override void _Ready()
	{
		// Initialize and run all tests
		GoTest.RunTests(assembly: typeof(TestRunner).Assembly, this);
	}
}
