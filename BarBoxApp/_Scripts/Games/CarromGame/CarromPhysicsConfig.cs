using Godot;
using System.Collections.Generic;

/// <summary>
/// Centralized physics configuration for Carrom game components
/// Referencing this paper for "Realistic" physics resolution: https://physlab.org/wp-content/uploads/2016/03/Conservation_momentum.pdf
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
	[Export(PropertyHint.Range, "1.2,3.0,0.1")] public float StrikerMass { get; set; } = 2.7f;

	[ExportCategory("Material Properties")]
	[Export(PropertyHint.Range, "0.0,1.0,0.05")] public float PieceFriction { get; set; } = 0.1f;
	[Export(PropertyHint.Range, "0.0,2.0,0.1")] public float PieceBounce { get; set; } = 0.6f;
	[Export(PropertyHint.Range, "0.0,1.0,0.05")] public float BoardFriction { get; set; } = 0.2f;
	[Export(PropertyHint.Range, "0.0,1.0,0.1")] public float BoardBounce { get; set; } = 0.3f;
	
	[ExportCategory("High-Speed Collision Enhancement")]
	[Export(PropertyHint.Range, "0.0,1.0,0.05")] public float HighSpeedFriction { get; set; } = 0.3f;
	[Export(PropertyHint.Range, "0.0,1.0,0.1")] public float HighSpeedBounce { get; set; } = 0.5f;
	[Export(PropertyHint.Range, "200.0,1000.0,50.0")] public float HighSpeedThreshold { get; set; } = 500.0f;
	[Export(PropertyHint.Range, "0.8,1.0,0.05")] public float CollisionSafetyMargin { get; set; } = 0.9f;
	


	[ExportCategory("Piece Sizes")]
	[Export(PropertyHint.Range, "8.0,20.0,0.5")] public float DefaultPieceRadius { get; set; } = 12.0f; // Fallback when no board scaling available
	[Export(PropertyHint.Range, "14.0,20.0,0.5")] public float StrikerRadius { get; set; } = 15.0f; // Fallback when no board scaling available
	
	// Board scaling properties for official proportions
	private float _scaleFactor = 1.0f;
	private float _officialPieceRadius = 0.0f;
	private float _officialStrikerRadius = 0.0f;
	private bool _useOfficialScaling = false;

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
	/// Create enhanced physics material for high-speed collisions
	/// </summary>
	public PhysicsMaterial CreateHighSpeedPieceMaterial()
	{
		var material = new PhysicsMaterial();
		material.Friction = HighSpeedFriction;
		material.Bounce = HighSpeedBounce;
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
	/// Configure physics config to use official board scaling
	/// Should be called by the game controller when board is available
	/// </summary>
	public void SetBoardScaling(float scaleFactor, float officialPieceRadius, float officialStrikerRadius)
	{
		_scaleFactor = scaleFactor;
		_officialPieceRadius = officialPieceRadius;
		_officialStrikerRadius = officialStrikerRadius;
		_useOfficialScaling = true;
		
		GD.Print($"[CarromPhysicsConfig] Using official scaling: ScaleFactor={scaleFactor:F2}, PieceRadius={officialPieceRadius:F1}, StrikerRadius={officialStrikerRadius:F1}");
	}

	/// <summary>
	/// Get radius for piece type (uses official board scaling if available, otherwise fallback values)
	/// </summary>
	public float GetRadiusForPieceType(PieceType type)
	{
		if (_useOfficialScaling)
		{
			return type == PieceType.Striker ? _officialStrikerRadius : _officialPieceRadius;
		}
		
		// Fallback to export values when no board scaling available
		return type == PieceType.Striker ? StrikerRadius : DefaultPieceRadius;
	}
	
	/// <summary>
	/// Get collision radius with safety margin to prevent tunneling
	/// </summary>
	public float GetCollisionRadiusForPieceType(PieceType type)
	{
		return GetRadiusForPieceType(type) * CollisionSafetyMargin;
	}
	
	/// <summary>
	/// Check if velocity qualifies as high-speed for enhanced collision detection
	/// </summary>
	public bool IsHighSpeedVelocity(float velocity)
	{
		return velocity > HighSpeedThreshold;
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