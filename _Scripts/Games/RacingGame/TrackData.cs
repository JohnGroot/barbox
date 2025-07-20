using Godot;
using System.Collections.Generic;

public class TrackData
{
	public List<Vector2> OriginalTrackPoints { get; set; } = new();
	public List<Vector2> SmoothTrackPoints { get; set; } = new();
	public List<Vector2> InnerEdgePoints { get; set; } = new();
	public List<Vector2> OuterEdgePoints { get; set; } = new();
	public List<Vector2> RacingLinePoints { get; set; } = new();
	public List<Vector2> CheckpointPositions { get; set; } = new();
	public List<Vector2[]> CheckpointLines { get; set; } = new();
	public List<Vector2> StartLinePoints { get; set; } = new();
	
	public Vector2 StartPoint { get; set; }
	public float TrackWidth { get; set; }
	public float CellSize { get; set; }
	public int StartPointIndex { get; set; }
	
	public Vector2 CenterPoint { get; set; }
	public bool IsValid => SmoothTrackPoints.Count >= 3 && 
	                      InnerEdgePoints.Count >= 3 && 
	                      OuterEdgePoints.Count >= 3;
	
	public void Clear()
	{
		OriginalTrackPoints.Clear();
		SmoothTrackPoints.Clear();
		InnerEdgePoints.Clear();
		OuterEdgePoints.Clear();
		RacingLinePoints.Clear();
		CheckpointPositions.Clear();
		CheckpointLines.Clear();
		StartLinePoints.Clear();
		StartPoint = Vector2.Zero;
		TrackWidth = 0f;
		CellSize = 0f;
		StartPointIndex = 0;
	}
}