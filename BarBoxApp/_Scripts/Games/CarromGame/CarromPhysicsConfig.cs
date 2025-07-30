using Godot;

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
	[Export(PropertyHint.Range, "1.2,2.0,0.1")] public float StrikerMass { get; set; } = 1.5f;

	[ExportCategory("Material Properties")]
	[Export(PropertyHint.Range, "0.0,1.0,0.05")] public float PieceFriction { get; set; } = 0.1f;
	[Export(PropertyHint.Range, "0.0,2.0,0.1")] public float PieceBounce { get; set; } = 0.8f;
	[Export(PropertyHint.Range, "0.0,1.0,0.05")] public float BoardFriction { get; set; } = 0.2f;
	[Export(PropertyHint.Range, "0.0,1.0,0.1")] public float BoardBounce { get; set; } = 0.3f;

	[ExportCategory("Realistic Physics")]
	[Export(PropertyHint.Range, "0.0,0.3,0.01")] public float StaticFrictionCoefficient { get; set; } = 0.15f;
	[Export(PropertyHint.Range, "0.0,0.2,0.01")] public float KineticFrictionCoefficient { get; set; } = 0.08f;
	[Export(PropertyHint.Range, "50.0,200.0,5.0")] public float StaticToKineticTransitionSpeed { get; set; } = 100.0f;
	[Export(PropertyHint.Range, "0.5,3.0,0.1")] public float VelocityFrictionCurveExponent { get; set; } = 1.5f;
	[Export(PropertyHint.Range, "0.01,0.1,0.005")] public float MinimumFrictionCoefficient { get; set; } = 0.04f;
	[Export(PropertyHint.Range, "10.0,50.0,2.0")] public float VeryLowSpeedThreshold { get; set; } = 25.0f;
// Always use realistic physics - no toggle needed

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

	// Cached friction curve for performance (realistic physics only)
	private float[] _frictionCurveCache;
	private float[] _restitutionCurveCache;
	private const int CACHE_SIZE = 256;
	private const float MAX_CACHED_VELOCITY = 2000.0f;
	private bool _cacheInitialized = false;

	/// <summary>
	/// Initialize performance caches for realistic physics calculations
	/// </summary>
	private void InitializeCaches()
	{
		if (_cacheInitialized) return;

		_frictionCurveCache = new float[CACHE_SIZE];
		_restitutionCurveCache = new float[CACHE_SIZE];

		for (int i = 0; i < CACHE_SIZE; i++)
		{
			float velocity = (i / (float)(CACHE_SIZE - 1)) * MAX_CACHED_VELOCITY;
			_frictionCurveCache[i] = CalculateVelocityFrictionDirect(velocity);

			float impactVelocity = velocity;
			_restitutionCurveCache[i] = CalculateCollisionRestitutionDirect(impactVelocity);
		}

		_cacheInitialized = true;
	}

	/// <summary>
	/// Calculate velocity-dependent friction coefficient for realistic carrom physics
	/// Uses cached values for performance
	/// </summary>
	public float CalculateVelocityFriction(float velocity)
	{
		InitializeCaches();

		// Use cached values for performance
		int cacheIndex = Mathf.Clamp((int)(velocity / MAX_CACHED_VELOCITY * CACHE_SIZE), 0, CACHE_SIZE - 1);
		return _frictionCurveCache[cacheIndex];
	}

	/// <summary>
	/// Calculate physics-based deceleration force for realistic carrom physics
	/// </summary>
	public float CalculateDecelerationForce(float velocity, float mass)
	{
		float frictionCoeff = CalculateVelocityFriction(velocity);
		float normalForce = mass * 9.81f; // Standard gravity for normal force
		return frictionCoeff * normalForce;
	}

	/// <summary>
	/// Calculate velocity-dependent collision restitution for realistic carrom physics
	/// Uses cached values for performance
	/// </summary>
	public float CalculateCollisionRestitution(float impactVelocity)
	{
		InitializeCaches();

		// Use cached values for performance
		int cacheIndex = Mathf.Clamp((int)(impactVelocity / MAX_CACHED_VELOCITY * CACHE_SIZE), 0, CACHE_SIZE - 1);
		return _restitutionCurveCache[cacheIndex];
	}

	/// <summary>
	/// Calculate velocity-dependent friction coefficient (direct implementation)
	/// </summary>
	private float CalculateVelocityFrictionDirect(float velocity)
	{
		// Very low speed regime: slightly higher friction (near-static conditions)
		if (velocity < VeryLowSpeedThreshold)
		{
			float lowSpeedFactor = 1.0f - (velocity / VeryLowSpeedThreshold);
			return Mathf.Lerp(KineticFrictionCoefficient, StaticFrictionCoefficient, lowSpeedFactor * 0.5f);
		}

		// Transition from static to kinetic friction
		if (velocity < StaticToKineticTransitionSpeed)
		{
			float transitionFactor = velocity / StaticToKineticTransitionSpeed;
			return Mathf.Lerp(StaticFrictionCoefficient, KineticFrictionCoefficient, transitionFactor);
		}

		// High speed regime: friction decreases with velocity curve (powdered surface effect)
		float normalizedVelocity = Mathf.Clamp(velocity / StaticToKineticTransitionSpeed, 1.0f, 5.0f);
		float frictionMultiplier = 1.0f / Mathf.Pow(normalizedVelocity, VelocityFrictionCurveExponent - 1.0f);
		
		float calculatedFriction = KineticFrictionCoefficient * frictionMultiplier;
		return Mathf.Max(calculatedFriction, MinimumFrictionCoefficient);
	}

	/// <summary>
	/// Calculate collision restitution (direct implementation)
	/// </summary>
	private float CalculateCollisionRestitutionDirect(float impactVelocity)
	{
		// Normalize impact velocity (typical carrom piece speeds: 0-2000 units/sec)
		float normalizedImpact = Mathf.Clamp(impactVelocity / 1000.0f, 0.0f, 2.0f);
		
		// Reduce restitution for higher impact velocities
		float restitutionReduction = normalizedImpact * 0.2f; // Up to 20% reduction
		return Mathf.Max(PieceBounce - restitutionReduction, 0.3f);
	}
}