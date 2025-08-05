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
			return null;
		}
		
		// Instantiate the scene
		var piece = pieceTemplate.Instantiate<CarromPiece>();
		if (piece == null)
		{
			return null;
		}
		
		// Configure the piece
		piece.Type = type;
		piece.PhysicsConfig = _physicsConfig;
		piece.Position = position; // Use local position relative to board
		
		// Apply centralized physics limits
		piece.SetPhysicsLimits(_minVelocityThreshold, _angularMinThreshold, 
			_maxVelocityLimit, _maxAngularVelocity, _velocityAlertThreshold);
		
		
		// Add to board
		_board.AddChild(piece);
		
		// Track pieces (excluding striker for some operations)
		if (type != PieceType.Striker)
		{
			_allPieces.Add(piece);
		}
		
		
		EmitSignal(SignalName.PieceCreated, piece);
		
		return piece;
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
		
		if (IsInstanceValid(piece))
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
		return [.._allPieces];
	}

	/// <summary>
	/// Get pieces of specific type
	/// </summary>
	public List<CarromPiece> GetPiecesOfType(PieceType type)
	{
		return _allPieces.Where(p => GodotObject.IsInstanceValid(p) && p.Type == type).ToList();
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