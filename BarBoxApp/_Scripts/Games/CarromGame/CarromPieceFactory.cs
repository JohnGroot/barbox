using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Factory for creating and managing Carrom pieces
/// </summary>
[GlobalClass]
public partial class CarromPieceFactory : Node2D
{
	[Signal] public delegate void PieceCreatedEventHandler(CarromPiece piece);
	[Signal] public delegate void PieceDestroyedEventHandler(CarromPiece piece);

	// Dependencies
	private CarromBoard _board;
	private CarromPhysicsConfig _physicsConfig;

	// Piece templates
	private PackedScene _whitePieceTemplate;
	private PackedScene _blackPieceTemplate;
	private PackedScene _redPieceTemplate;
	private PackedScene _strikerTemplate;

	// Piece tracking
	private List<CarromPiece> _allPieces = new List<CarromPiece>();

	// Physics limits - set by CarromGame.cs
	private float _minVelocityThreshold = 1.0f;
	private float _angularMinThreshold = 0.1f;
	private float _maxVelocityLimit = 2000.0f;
	private float _maxAngularVelocity = 50.0f;
	private float _velocityAlertThreshold = 1800.0f;

	/// <summary>
	/// Initialize the piece factory
	/// </summary>
	public void Initialize(CarromBoard board, CarromPhysicsConfig physicsConfig,
						   PackedScene whiteTemplate, PackedScene blackTemplate,
						   PackedScene redTemplate, PackedScene strikerTemplate)
	{
		_board = board;
		_physicsConfig = physicsConfig;
		_whitePieceTemplate = whiteTemplate;
		_blackPieceTemplate = blackTemplate;
		_redPieceTemplate = redTemplate;
		_strikerTemplate = strikerTemplate;
		
		// Configure physics config with official board scaling for proportional piece sizes
		if (_board != null && _physicsConfig != null)
		{
			_physicsConfig.SetBoardScaling(
				_board.ScaleFactor,
				_board.PieceRadius,
				_board.OfficialStrikerRadius
			);
		}
		
	}

	/// <summary>
	/// Set physics limits from CarromGame
	/// </summary>
	public void SetPhysicsLimits(float minVelocityThreshold, float angularMinThreshold, 
		float maxVelocityLimit, float maxAngularVelocity, float velocityAlertThreshold)
	{
		_minVelocityThreshold = minVelocityThreshold;
		_angularMinThreshold = angularMinThreshold;
		_maxVelocityLimit = maxVelocityLimit;
		_maxAngularVelocity = maxAngularVelocity;
		_velocityAlertThreshold = velocityAlertThreshold;
		
	}

	/// <summary>
	/// Create a carrom piece of specified type
	/// </summary>
	public CarromPiece CreatePiece(PieceType type, Vector2 position)
	{
		PackedScene pieceTemplate = GetPieceTemplate(type);
		
		if (pieceTemplate == null)
		{
			GD.PrintErr($"[CarromPieceFactory] No template found for piece type: {type}");
			return null;
		}
		
		// Instantiate the scene
		var piece = pieceTemplate.Instantiate<CarromPiece>();
		if (piece == null)
		{
			GD.PrintErr($"[CarromPieceFactory] Failed to instantiate piece of type: {type}");
			return null;
		}
		
		// Configure the piece
		piece.Type = type;
		piece.PhysicsConfig = _physicsConfig;
		piece.Position = position; // Use local position relative to board
		
		// Apply centralized physics limits
		piece.SetPhysicsLimits(_minVelocityThreshold, _angularMinThreshold, 
			_maxVelocityLimit, _maxAngularVelocity, _velocityAlertThreshold);
		
		// Connect piece signals
		piece.PieceStopped += OnPieceStopped;
		piece.PieceCollided += OnPieceCollided;
		
		// Add to board
		_board.AddChild(piece);
		
		// Track pieces (excluding striker for some operations)
		if (type != PieceType.Striker)
		{
			_allPieces.Add(piece);
		}
		
		// Connect to board signals for pocketing
		piece.Connect(CarromPiece.SignalName.PieceStopped, Callable.From<CarromPiece>(OnPieceStopped));
		
		EmitSignal(SignalName.PieceCreated, piece);
		
		return piece;
	}

	/// <summary>
	/// Create multiple pieces in formation patterns
	/// </summary>
	public List<CarromPiece> CreatePiecesInFormation(List<PieceType> pieceTypes, Vector2 centerPosition, float radius, string pattern = "hexagonal")
	{
		var createdPieces = new List<CarromPiece>();
		
		switch (pattern.ToLower())
		{
			case "hexagonal":
				createdPieces = CreateHexagonalFormation(pieceTypes, centerPosition, radius);
				break;
			case "circular":
				createdPieces = CreateCircularFormation(pieceTypes, centerPosition, radius);
				break;
			case "linear":
				createdPieces = CreateLinearFormation(pieceTypes, centerPosition, radius);
				break;
			default:
				GD.PrintErr($"[CarromPieceFactory] Unknown formation pattern: {pattern}");
				break;
		}
		
		return createdPieces;
	}

	/// <summary>
	/// Create standard competitive carrom formation
	/// </summary>
	public List<CarromPiece> CreateStandardCarromFormation(Vector2 centerPosition)
	{
		// Traditional carrom setup: 9 white, 9 black, 1 red queen
		var pieces = new List<PieceType>();
		
		// Add pieces to pattern (9 white, 9 black, 1 red)
		for (int i = 0; i < 9; i++) pieces.Add(PieceType.White);
		for (int i = 0; i < 9; i++) pieces.Add(PieceType.Black);
		pieces.Add(PieceType.Red); // Queen
		
		return CreateCarromHexagonalFormation(pieces, centerPosition, 60.0f);
	}

	/// <summary>
	/// Create carrom-specific hexagonal formation
	/// </summary>
	private List<CarromPiece> CreateCarromHexagonalFormation(List<PieceType> pieceTypes, Vector2 center, float radius)
	{
		var createdPieces = new List<CarromPiece>();
		
		// Queen in center (red piece)
		var queenPiece = CreatePiece(PieceType.Red, center);
		if (queenPiece != null) createdPieces.Add(queenPiece);
		
		int pieceIndex = 0;
		
		// Inner ring (6 pieces)
		for (int i = 0; i < 6 && pieceIndex < pieceTypes.Count - 1; i++)
		{
			float angle = i * Mathf.Pi / 3;
			Vector2 pos = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (radius * 0.5f);
			var piece = CreatePiece(pieceTypes[pieceIndex], pos);
			if (piece != null) createdPieces.Add(piece);
			pieceIndex++;
		}
		
		// Outer ring (12 pieces)
		for (int i = 0; i < 12 && pieceIndex < pieceTypes.Count - 1; i++)
		{
			float angle = i * Mathf.Pi / 6;
			Vector2 pos = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
			var piece = CreatePiece(pieceTypes[pieceIndex], pos);
			if (piece != null) createdPieces.Add(piece);
			pieceIndex++;
		}
		
		return createdPieces;
	}

	/// <summary>
	/// Create hexagonal formation
	/// </summary>
	private List<CarromPiece> CreateHexagonalFormation(List<PieceType> pieceTypes, Vector2 center, float radius)
	{
		var createdPieces = new List<CarromPiece>();
		
		for (int i = 0; i < pieceTypes.Count; i++)
		{
			float angle = i * Mathf.Tau / pieceTypes.Count;
			Vector2 pos = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
			var piece = CreatePiece(pieceTypes[i], pos);
			if (piece != null) createdPieces.Add(piece);
		}
		
		return createdPieces;
	}

	/// <summary>
	/// Create circular formation
	/// </summary>
	private List<CarromPiece> CreateCircularFormation(List<PieceType> pieceTypes, Vector2 center, float radius)
	{
		var createdPieces = new List<CarromPiece>();
		
		for (int i = 0; i < pieceTypes.Count; i++)
		{
			float angle = i * Mathf.Tau / pieceTypes.Count;
			Vector2 pos = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
			var piece = CreatePiece(pieceTypes[i], pos);
			if (piece != null) createdPieces.Add(piece);
		}
		
		return createdPieces;
	}

	/// <summary>
	/// Create linear formation
	/// </summary>
	private List<CarromPiece> CreateLinearFormation(List<PieceType> pieceTypes, Vector2 center, float spacing)
	{
		var createdPieces = new List<CarromPiece>();
		
		float startX = center.X - (pieceTypes.Count - 1) * spacing * 0.5f;
		
		for (int i = 0; i < pieceTypes.Count; i++)
		{
			Vector2 pos = new Vector2(startX + i * spacing, center.Y);
			var piece = CreatePiece(pieceTypes[i], pos);
			if (piece != null) createdPieces.Add(piece);
		}
		
		return createdPieces;
	}

	/// <summary>
	/// Get piece template for type
	/// </summary>
	private PackedScene GetPieceTemplate(PieceType type)
	{
		return type switch
		{
			PieceType.White => _whitePieceTemplate,
			PieceType.Black => _blackPieceTemplate,
			PieceType.Red => _redPieceTemplate,
			PieceType.Striker => _strikerTemplate,
			_ => null
		};
	}

	/// <summary>
	/// Clear all pieces created by this factory
	/// </summary>
	public void ClearAllPieces()
	{
		foreach (var piece in _allPieces)
		{
			if (GodotObject.IsInstanceValid(piece))
			{
				EmitSignal(SignalName.PieceDestroyed, piece);
				piece.QueueFree();
			}
		}
		_allPieces.Clear();
		
	}

	/// <summary>
	/// Remove specific piece
	/// </summary>
	public void RemovePiece(CarromPiece piece)
	{
		if (_allPieces.Contains(piece))
		{
			_allPieces.Remove(piece);
		}
		
		if (GodotObject.IsInstanceValid(piece))
		{
			EmitSignal(SignalName.PieceDestroyed, piece);
			piece.QueueFree();
		}
	}

	/// <summary>
	/// Get all pieces created by this factory
	/// </summary>
	public List<CarromPiece> GetAllPieces()
	{
		return new List<CarromPiece>(_allPieces);
	}

	/// <summary>
	/// Get pieces of specific type
	/// </summary>
	public List<CarromPiece> GetPiecesOfType(PieceType type)
	{
		return _allPieces.Where(p => GodotObject.IsInstanceValid(p) && p.Type == type).ToList();
	}

	/// <summary>
	/// Check if all pieces are stopped
	/// </summary>
	public bool AreAllPiecesStopped()
	{
		foreach (CarromPiece piece in _allPieces)
		{
			if (piece.Visible && !piece.IsStopped())
			{
				return false;
			}
		}
		return true;
	}

	/// <summary>
	/// Handle piece stopped signal
	/// </summary>
	private void OnPieceStopped(CarromPiece piece)
	{
	}

	/// <summary>
	/// Handle piece collision signal
	/// </summary>
	private void OnPieceCollided(CarromPiece thisPiece, CarromPiece otherPiece, Vector2 impactForce)
	{
	}

	/// <summary>
	/// Cleanup on exit
	/// </summary>
	public override void _Notification(int what)
	{
		if (what == NotificationExitTree)
		{
			ClearAllPieces();
		}
	}
}