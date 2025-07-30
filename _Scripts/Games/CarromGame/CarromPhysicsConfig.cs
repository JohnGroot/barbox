using Godot;

/// <summary>
/// Centralized physics configuration for Carrom game components
/// </summary>
[GlobalClass]
public partial class CarromPhysicsConfig : Resource
{
	[ExportCategory("Movement Detection")]
	[Export(PropertyHint.Range, "10.0,50.0,1.0")] public float PieceStopThreshold { get; set; } = 20.0f;
	[Export(PropertyHint.Range, "0.1,1.0,0.05")] public float StopConfirmationTime { get; set; } = 0.2f;
	[Export(PropertyHint.Range, "1.0,2.0,0.1")] public float StopHysteresisFactor { get; set; } = 1.2f;

	[ExportCategory("Piece Physics")]
	[Export(PropertyHint.Range, "0.0,10.0,0.1")] public float LinearDamping { get; set; } = 2.0f;
	[Export(PropertyHint.Range, "0.0,10.0,0.1")] public float AngularDamping { get; set; } = 3.0f;
	[Export(PropertyHint.Range, "0.5,3.0,0.1")] public float DefaultMass { get; set; } = 1.0f;
	[Export(PropertyHint.Range, "1.2,2.0,0.1")] public float StrikerMass { get; set; } = 1.5f;

	[ExportCategory("Material Properties")]
	[Export(PropertyHint.Range, "0.0,1.0,0.05")] public float PieceFriction { get; set; } = 0.1f;
	[Export(PropertyHint.Range, "0.0,2.0,0.1")] public float PieceBounce { get; set; } = 0.8f;
	[Export(PropertyHint.Range, "0.0,1.0,0.05")] public float BoardFriction { get; set; } = 0.2f;
	[Export(PropertyHint.Range, "0.0,1.0,0.1")] public float BoardBounce { get; set; } = 0.3f;

	[ExportCategory("Piece Sizes")]
	[Export(PropertyHint.Range, "8.0,20.0,0.5")] public float DefaultPieceRadius { get; set; } = 12.0f;
	[Export(PropertyHint.Range, "14.0,20.0,0.5")] public float StrikerRadius { get; set; } = 15.0f;

	[ExportCategory("Collision Detection")]
	[Export(PropertyHint.Range, "5,20,1")] public int MaxContactsReported { get; set; } = 10;
	[Export] public bool ContactMonitor { get; set; } = true;

	/// <summary>
	/// Create physics material for pieces
	/// </summary>
	public PhysicsMaterial CreatePieceMaterial()
	{
		var material = new PhysicsMaterial();
		material.Friction = PieceFriction;
		material.Bounce = PieceBounce;
		return material;
	}

	/// <summary>
	/// Create physics material for board
	/// </summary>
	public PhysicsMaterial CreateBoardMaterial()
	{
		var material = new PhysicsMaterial();
		material.Friction = BoardFriction;
		material.Bounce = BoardBounce;
		return material;
	}

	/// <summary>
	/// Get mass for piece type
	/// </summary>
	public float GetMassForPieceType(PieceType type)
	{
		return type == PieceType.Striker ? StrikerMass : DefaultMass;
	}

	/// <summary>
	/// Get radius for piece type
	/// </summary>
	public float GetRadiusForPieceType(PieceType type)
	{
		return type == PieceType.Striker ? StrikerRadius : DefaultPieceRadius;
	}

	/// <summary>
	/// Check if piece is stopped based on threshold and hysteresis
	/// </summary>
	public bool IsPieceStopped(Vector2 velocity, float angularVelocity, bool wasMoving)
	{
		float effectiveThreshold = wasMoving ? PieceStopThreshold : PieceStopThreshold * StopHysteresisFactor;
		float linearSpeed = velocity.Length();
		float angularThreshold = PieceStopThreshold * 0.05f;
		
		return linearSpeed < effectiveThreshold && Mathf.Abs(angularVelocity) < angularThreshold;
	}
}