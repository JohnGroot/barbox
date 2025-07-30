using Godot;

/// <summary>
/// Carrom pocket detection using Area2D
/// </summary>
[GlobalClass]
public partial class CarromPocket : Area2D
{
	[Signal] public delegate void PiecePocketedEventHandler(CarromPiece piece);
	[Signal] public delegate void PieceEnteredPocketAreaEventHandler(CarromPiece piece);
	[Signal] public delegate void PieceLeftPocketAreaEventHandler(CarromPiece piece);

	[ExportCategory("Pocket Settings")]
	[Export] public float PocketRadius { get; set; } = 30.0f;
	[Export] public int PocketIndex { get; set; } = 0;
	[Export] public float PocketDepth { get; set; } = 0.7f; // How deep pieces must go to be pocketed (0.0-1.0)

	[ExportCategory("Detection Settings")]
	[Export] public bool RequireSlowEntry { get; set; } = true; // Pieces must be moving slowly to be pocketed
	[Export] public float MaxPocketingSpeed { get; set; } = 100.0f; // Max speed for pocketing
	[Export] public float PocketingDelay { get; set; } = 0.1f; // Delay before confirming pocket

	// Detection components
	private CircleShape2D _detectionShape;
	private CollisionShape2D _collisionShape2D;
	
	// Pocket state tracking
	private System.Collections.Generic.Dictionary<CarromPiece, float> _piecesInPocket = new();
	private System.Collections.Generic.Dictionary<CarromPiece, float> _pocketingTimers = new();

	public override void _Ready()
	{
		SetupPocketDetection();
		ConnectSignals();
		GD.Print($"[CarromPocket] Pocket {PocketIndex} initialized at {GlobalPosition}");
	}

	/// <summary>
	/// Setup pocket detection area
	/// </summary>
	private void SetupPocketDetection()
	{
		Monitoring = true;
		
		// Create detection shape
		_detectionShape = new CircleShape2D();
		_detectionShape.Radius = PocketRadius;
		
		_collisionShape2D = new CollisionShape2D();
		_collisionShape2D.Shape = _detectionShape;
		AddChild(_collisionShape2D);
		
		// Set collision layers for piece detection
		CollisionLayer = 0; // Pocket doesn't have collision
		CollisionMask = 1;  // Detects pieces on layer 1
	}

	/// <summary>
	/// Connect area signals
	/// </summary>
	private void ConnectSignals()
	{
		BodyEntered += OnBodyEntered;
		BodyExited += OnBodyExited;
	}

	/// <summary>
	/// Process pocket detection and timers
	/// </summary>
	public override void _Process(double delta)
	{
		float deltaF = (float)delta;
		
		// Update pocketing timers
		var keysToRemove = new System.Collections.Generic.List<CarromPiece>();
		var keysToProcess = new System.Collections.Generic.List<CarromPiece>(_pocketingTimers.Keys);
		
		foreach (var piece in keysToProcess)
		{
			if (!GodotObject.IsInstanceValid(piece))
			{
				keysToRemove.Add(piece);
				continue;
			}
			
			_pocketingTimers[piece] += deltaF;
			
			// Check if piece should be pocketed
			if (_pocketingTimers[piece] >= PocketingDelay)
			{
				if (ShouldPocketPiece(piece))
				{
					PocketPiece(piece);
				}
				keysToRemove.Add(piece);
			}
		}
		
		// Clean up completed timers
		foreach (var piece in keysToRemove)
		{
			_pocketingTimers.Remove(piece);
			_piecesInPocket.Remove(piece);
		}
	}

	/// <summary>
	/// Handle piece entering pocket area
	/// </summary>
	private void OnBodyEntered(Node2D body)
	{
		if (body is CarromPiece piece)
		{
			_piecesInPocket[piece] = 0.0f; // Track entry time
			EmitSignal(SignalName.PieceEnteredPocketArea, piece);
			
			// Start pocketing timer if piece meets initial conditions
			if (CanStartPocketing(piece))
			{
				_pocketingTimers[piece] = 0.0f;
				GD.Print($"[CarromPocket] {piece.Type} piece entered pocket {PocketIndex}, starting timer");
			}
		}
	}

	/// <summary>
	/// Handle piece leaving pocket area
	/// </summary>
	private void OnBodyExited(Node2D body)
	{
		if (body is CarromPiece piece)
		{
			_piecesInPocket.Remove(piece);
			_pocketingTimers.Remove(piece);
			EmitSignal(SignalName.PieceLeftPocketArea, piece);
			
			GD.Print($"[CarromPocket] {piece.Type} piece left pocket {PocketIndex}");
		}
	}

	/// <summary>
	/// Check if piece can start the pocketing process
	/// </summary>
	private bool CanStartPocketing(CarromPiece piece)
	{
		// Don't pocket pieces that are moving too fast
		if (RequireSlowEntry && piece.GetSpeed() > MaxPocketingSpeed)
		{
			return false;
		}
		
		// Check if piece is deep enough in pocket
		return IsPieceInPocketDepth(piece);
	}

	/// <summary>
	/// Check if piece should be pocketed after timer expires
	/// </summary>
	private bool ShouldPocketPiece(CarromPiece piece)
	{
		// Verify piece is still in the pocket area
		if (!_piecesInPocket.ContainsKey(piece))
		{
			return false;
		}
		
		// Check piece is deep enough in pocket
		if (!IsPieceInPocketDepth(piece))
		{
			return false;
		}
		
		// Check speed requirement if enabled
		if (RequireSlowEntry && piece.GetSpeed() > MaxPocketingSpeed)
		{
			return false;
		}
		
		return true;
	}

	/// <summary>
	/// Check if piece is deep enough in the pocket
	/// </summary>
	private bool IsPieceInPocketDepth(CarromPiece piece)
	{
		float distanceToCenter = GlobalPosition.DistanceTo(piece.GlobalPosition);
		float requiredDepth = PocketRadius * PocketDepth;
		return distanceToCenter <= requiredDepth;
	}

	/// <summary>
	/// Pocket the piece
	/// </summary>
	private void PocketPiece(CarromPiece piece)
	{
		// Stop piece physics
		piece.ForceStop();
		
		// Hide piece with animation effect
		CreatePocketingEffect(piece);
		
		// Disable piece
		piece.PocketPiece();
		
		// Emit pocketing signal
		EmitSignal(SignalName.PiecePocketed, piece);
		
		GD.Print($"[CarromPocket] {piece.Type} piece successfully pocketed in pocket {PocketIndex}");
	}

	/// <summary>
	/// Create visual effect for pocketing
	/// </summary>
	private void CreatePocketingEffect(CarromPiece piece)
	{
		// Create a simple scaling animation
		var tween = CreateTween();
		tween.SetParallel(true);
		
		// Scale down the piece
		tween.TweenProperty(piece, "scale", Vector2.Zero, 0.3f);
		tween.TweenProperty(piece, "modulate:a", 0.0f, 0.3f);
		
		// Move piece toward pocket center
		tween.TweenProperty(piece, "global_position", GlobalPosition, 0.2f);
	}

	/// <summary>
	/// Check if pocket contains a specific piece
	/// </summary>
	public bool ContainsPiece(CarromPiece piece)
	{
		return _piecesInPocket.ContainsKey(piece);
	}

	/// <summary>
	/// Get all pieces currently in pocket area
	/// </summary>
	public CarromPiece[] GetPiecesInPocket()
	{
		var pieces = new System.Collections.Generic.List<CarromPiece>();
		foreach (var piece in _piecesInPocket.Keys)
		{
			if (GodotObject.IsInstanceValid(piece))
			{
				pieces.Add(piece);
			}
		}
		return pieces.ToArray();
	}

	/// <summary>
	/// Get count of pieces in pocket
	/// </summary>
	public int GetPieceCount()
	{
		int count = 0;
		foreach (var piece in _piecesInPocket.Keys)
		{
			if (GodotObject.IsInstanceValid(piece))
			{
				count++;
			}
		}
		return count;
	}

	/// <summary>
	/// Force pocket a piece immediately (for testing/special cases)
	/// </summary>
	public void ForcePocketPiece(CarromPiece piece)
	{
		if (piece != null && GodotObject.IsInstanceValid(piece))
		{
			PocketPiece(piece);
		}
	}

	/// <summary>
	/// Clear all pieces from pocket (for game reset)
	/// </summary>
	public void ClearPocket()
	{
		_piecesInPocket.Clear();
		_pocketingTimers.Clear();
	}

	/// <summary>
	/// Get pocket position for external use
	/// </summary>
	public Vector2 GetPocketPosition()
	{
		return GlobalPosition;
	}

	/// <summary>
	/// Check if pocket is active
	/// </summary>
	public bool IsActive()
	{
		return Monitoring;
	}

	/// <summary>
	/// Enable or disable pocket detection
	/// </summary>
	public void SetActive(bool active)
	{
		Monitoring = active;
		
		if (!active)
		{
			ClearPocket();
		}
	}

	/// <summary>
	/// Get pocket statistics for debugging
	/// </summary>
	public System.Collections.Generic.Dictionary<string, object> GetPocketStats()
	{
		return new System.Collections.Generic.Dictionary<string, object>
		{
			["PocketIndex"] = PocketIndex,
			["PiecesInArea"] = GetPieceCount(),
			["PendingPockets"] = _pocketingTimers.Count,
			["IsActive"] = IsActive(),
			["Position"] = GlobalPosition,
			["Radius"] = PocketRadius
		};
	}

	/// <summary>
	/// Cleanup on exit
	/// </summary>
	public override void _Notification(int what)
	{
		if (what == NotificationExitTree)
		{
			// Clean up signals
			if (GodotObject.IsInstanceValid(this))
			{
				BodyEntered -= OnBodyEntered;
				BodyExited -= OnBodyExited;
			}
		}
	}
}