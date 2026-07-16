using System.Collections.Generic;
using Godot;

namespace BarBox.Games.Carrom;

/// <summary>
/// Centralized physics configuration for Carrom game components
/// Referencing this paper for "Realistic" physics resolution: https://physlab.org/wp-content/uploads/2016/03/Conservation_momentum.pdf
/// </summary>
[GlobalClass]
public partial class CarromPhysicsConfig : Resource
{
	[ExportCategory("Movement Detection")]
	[Export(PropertyHint.Range, "10.0,50.0,1.0")]
	public float PieceStopThreshold { get; set; } = 20.0f;

	[Export(PropertyHint.Range, "0.1,1.0,0.05")]
	public float StopConfirmationTime { get; set; } = 0.2f;

	[Export(PropertyHint.Range, "1.0,2.0,0.1")]
	public float StopHysteresisFactor { get; set; } = 1.2f;

	[ExportCategory("Piece Physics")]
	[Export(PropertyHint.Range, "0.0,10.0,0.1")]
	public float LinearDamping { get; set; } = 2.0f;

	[Export(PropertyHint.Range, "0.0,10.0,0.1")]
	public float AngularDamping { get; set; } = 3.0f;

	[Export(PropertyHint.Range, "0.5,3.0,0.1")]
	public float DefaultMass { get; set; } = 1.0f;

	[Export(PropertyHint.Range, "1.2,3.0,0.1")]
	public float StrikerMass { get; set; } = 2.7f;

	[ExportCategory("Material Properties")]
	[Export(PropertyHint.Range, "0.0,1.0,0.05")]
	public float PieceFriction { get; set; } = 0.02f;

	[Export(PropertyHint.Range, "0.0,2.0,0.1")]
	public float PieceBounce { get; set; } = 0.95f;

	[Export(PropertyHint.Range, "0.8,1.0,0.05")]
	public float CollisionSafetyMargin { get; set; } = 0.85f;

	[ExportCategory("Piece Sizes")]
	[Export(PropertyHint.Range, "8.0,20.0,0.5")]
	public float DefaultPieceRadius { get; set; } = 12.0f;

	[Export(PropertyHint.Range, "14.0,20.0,0.5")]
	public float StrikerRadius { get; set; } = 15.0f;

	// Board scaling properties for official proportions
	private float _scaleFactor = 1.0f;
	private float _officialPieceRadius = 0.0f;
	private float _officialStrikerRadius = 0.0f;
	private bool _useOfficialScaling = false;

	// Cached physics materials for performance optimization
	private PhysicsMaterial _cachedPieceMaterial;
	private bool _materialsInitialized = false;

	// Property change tracking for cache invalidation
	private float _lastPieceFriction = -1f;
	private float _lastPieceBounce = -1f;

	[ExportCategory("Collision Detection")]
	[Export(PropertyHint.Range, "5,20,1")]
	public int MaxContactsReported { get; set; } = 10;

	[Export]
	public bool ContactMonitor { get; set; } = true;

	[ExportCategory("Strike Physics")]
	[Export(PropertyHint.Range, "50.0,500.0,25.0")]
	public float MinStrikePower { get; set; } = 150.0f; // From scene settings

	[Export(PropertyHint.Range, "1000.0,6000.0,100.0")]
	public float MaxStrikePower { get; set; } = 4500.0f; // From scene settings

	[Export(PropertyHint.Range, "30.0,180.0,5.0")]
	public float MaxStrikeAngle { get; set; } = 90.0f; // From scene settings

	[ExportCategory("Pocket Physics")]
	[Export(PropertyHint.Range, "1.2,2.5,0.1")]
	public float PocketInfluenceZoneMultiplier { get; set; } = 1.8f; // Multiplier of pocket radius for influence zone

	[Export(PropertyHint.Range, "0.6,0.9,0.05")]
	public float PocketHoleZoneMultiplier { get; set; } = 0.75f; // Multiplier of pocket radius for hole detection

	[ExportCategory("Speed Ratios")]
	[Export(PropertyHint.Range, "0.02,1,0.005")]
	public float PocketSlowCaptureSpeedRatio { get; set; } = 0.05f; // 5% of max strike power

	[Export(PropertyHint.Range, "0.1,1,0.01")]
	public float PocketMaxCaptureSpeedRatio { get; set; } = 0.15f; // 15% of max strike power

	[Export(PropertyHint.Range, "0.3,0.6,0.01")]
	public float PocketBounceOutSpeedRatio { get; set; } = 0.4f; // 40% of max strike power

	[Export(PropertyHint.Range, "0.0,1.0,0.1")]
	public float PocketSpeedCaptureChance { get; set; } = 0.9f; // Base capture chance for medium speeds

	[Export(PropertyHint.Range, "10.0,100.0,5.0")]
	public float PocketRadialForceStrength { get; set; } = 40.0f; // Strength of inward attraction force

	[Export(PropertyHint.Range, "1.0,5.0,0.2")]
	public float PocketFrictionMultiplier { get; set; } = 2.5f; // Extra friction near pocket edges

	[Export(PropertyHint.Range, "15.0,120.0,5.0")]
	public float PocketMaxApproachAngle { get; set; } = 45.0f; // Max approach angle (degrees) for successful entry

	[ExportCategory("Powder Effect Friction")]
	[Export(PropertyHint.Range, "0.0,2.0,0.005")]
	public float InitialFrictionCoefficient { get; set; } = 0.05f; // μ_k when powder acts like ball bearings

	[Export(PropertyHint.Range, "0.0,2.0,0.01")]
	public float FinalFrictionCoefficient { get; set; } = 0.15f; // μ_k after powder scatters

	[Export(PropertyHint.Range, "100.0,500.0,25.0")]
	public float PowderTransitionDistance { get; set; } = 200.0f; // Distance (units) over which friction transitions

	[Export(PropertyHint.Range, "800.0,1000.0,20.0")]
	public float GravityConstant { get; set; } = 900.0f; // Gravity acceleration (units/s²) for friction calculations

	[Export]
	public bool EnablePowderEffectFriction { get; set; } = true; // Enable realistic powder-effect friction (vs built-in damping)

	[ExportCategory("Physics Constants")]
	[Export(PropertyHint.Range, "30.0,100.0,5.0")]
	public float MinimumMovementValidation { get; set; } = 50.0f; // Minimum velocity to count as "real movement"

	[Export(PropertyHint.Range, "30.0,100.0,5.0")]
	public float AngularTorqueScale { get; set; } = 50.0f; // Realistic physics constants

	[Export(PropertyHint.Range, "0.01,0.1,0.005")]
	public float BoardFriction { get; set; } = 0.02f; // Very low friction for smooth sliding

	[Export(PropertyHint.Range, "0.7,1.0,0.05")]
	public float BoardBounce { get; set; } = 0.95f; // High bounce for realistic collisions

	[Export(PropertyHint.Range, "0.6,1.0,0.05")]
	public float PocketInnerMultiplier { get; set; } = 0.8f; // Inner pocket hole size multiplier

	[Export(PropertyHint.Range, "0.8,1.0,0.05")]
	public float PocketHighlightMultiplier { get; set; } = 0.9f; // Pocket highlight ring multiplier

	[ExportCategory("Movement Detection Timeouts")]
	[Export(PropertyHint.Range, "1.0,5.0,0.5")]
	public float CompletelyStillTimeout { get; set; } = 2.0f; // Completely still after N seconds

	[Export(PropertyHint.Range, "3.0,10.0,1.0")]
	public float StationaryPieceTimeout { get; set; } = 5.0f; // Any stationary piece after N seconds

	[Export(PropertyHint.Range, "8.0,15.0,1.0")]
	public float AbsoluteTimeout { get; set; } = 10.0f; // Absolute timeout - any piece after N seconds

	[Export(PropertyHint.Range, "10.0,20.0,1.0")]
	public float UltimateTimeout { get; set; } = 15.0f; // Ultimate fallback - no piece should block settlement

	private void UpdateMaterialCache()
	{
		if (_cachedPieceMaterial == null || _lastPieceFriction != PieceFriction || _lastPieceBounce != PieceBounce)
		{
			_cachedPieceMaterial ??= new PhysicsMaterial();
			_cachedPieceMaterial.Friction = PieceFriction;
			_cachedPieceMaterial.Bounce = PieceBounce;
			_lastPieceFriction = PieceFriction;
			_lastPieceBounce = PieceBounce;
		}

		_materialsInitialized = true;
	}

	public PhysicsMaterial CreatePieceMaterial()
	{
		UpdateMaterialCache();
		return _cachedPieceMaterial;
	}

	public PhysicsMaterial CreateBoardMaterial()
	{
		// Create fresh material with centralized constants instead of cached version
		var boardMaterial = new PhysicsMaterial();
		boardMaterial.Friction = BoardFriction;
		boardMaterial.Bounce = BoardBounce;
		return boardMaterial;
	}

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

	public bool IsPieceStopped(Vector2 velocity, float angularVelocity, bool wasMoving)
	{
		float effectiveThreshold = wasMoving ? PieceStopThreshold : PieceStopThreshold * StopHysteresisFactor;
		float linearSpeed = velocity.Length();
		float angularThreshold = PieceStopThreshold * 0.05f;

		return linearSpeed < effectiveThreshold && Mathf.Abs(angularVelocity) < angularThreshold;
	}

	/// <summary>
	/// Get slow capture speed threshold (always captured)
	/// </summary>
	public float PocketSlowCaptureSpeed => MaxStrikePower * PocketSlowCaptureSpeedRatio;

	/// <summary>
	/// Get max capture speed threshold (reliable pocketing)
	/// </summary>
	public float PocketMaxCaptureSpeed => MaxStrikePower * PocketMaxCaptureSpeedRatio;

	/// <summary>
	/// Get bounce out speed threshold (pieces likely bounce out)
	/// </summary>
	public float PocketBounceOutSpeed => MaxStrikePower * PocketBounceOutSpeedRatio;

	public float CalculatePocketCaptureChance(float pieceSpeed)
	{
		if (pieceSpeed <= PocketSlowCaptureSpeed)
		{
			return 1.0f; // Guaranteed capture for very slow pieces
		}

		if (pieceSpeed <= PocketMaxCaptureSpeed)
		{
			return 1.0f; // Guaranteed capture for slow pieces
		}

		if (pieceSpeed >= PocketBounceOutSpeed)
		{
			return 0.0f; // No capture for very fast pieces
		}

		// Linear interpolation between max capture speed and bounce out speed
		float speedRange = PocketBounceOutSpeed - PocketMaxCaptureSpeed;
		float speedOffset = pieceSpeed - PocketMaxCaptureSpeed;
		float speedFactor = 1.0f - (speedOffset / speedRange);
		return PocketSpeedCaptureChance * speedFactor;
	}

	public float CalculatePocketRadialForce(float distanceToCenter, float pocketRadius)
	{
		float influenceRadius = pocketRadius * PocketInfluenceZoneMultiplier;

		if (distanceToCenter >= influenceRadius)
		{
			return 0.0f; // No force outside influence zone
		}

		// Force increases as piece gets closer to center (inverse square law)
		float normalizedDistance = distanceToCenter / influenceRadius;
		float forceMultiplier = 1.0f - (normalizedDistance * normalizedDistance);

		return PocketRadialForceStrength * forceMultiplier;
	}

	public bool IsValidPocketApproachAngle(Vector2 pieceVelocity, Vector2 pocketDirection)
	{
		if (pieceVelocity.Length() < 1.0f)
		{
			return true; // Very slow pieces can enter from any angle
		}

		float approachAngle = Mathf.Abs(pieceVelocity.Normalized().AngleTo(pocketDirection.Normalized()));
		float maxAngleRadians = Mathf.DegToRad(PocketMaxApproachAngle);

		return approachAngle <= maxAngleRadians;
	}

	public float CalculatePowderFrictionCoefficient(float distanceTraveled)
	{
		if (!EnablePowderEffectFriction || PowderTransitionDistance <= 0.0f)
		{
			return FinalFrictionCoefficient; // Use final coefficient if powder effect disabled
		}

		// Linear interpolation from initial to final friction over transition distance
		float t = Mathf.Clamp(distanceTraveled / PowderTransitionDistance, 0.0f, 1.0f);
		return Mathf.Lerp(InitialFrictionCoefficient, FinalFrictionCoefficient, t);
	}
}
