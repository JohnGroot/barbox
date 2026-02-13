using Godot;
using System.Collections.Generic;

namespace BarBox.Games.Carrom;

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
	private List<CarromPiece> _allPieces = [];

	// Physics limits - set by CarromGame.cs
	private float _minVelocityThreshold = 1.0f;
	private float _angularMinThreshold = 0.1f;
	private float _maxVelocityLimit = 2000.0f;
	private float _maxAngularVelocity = 50.0f;
	private float _velocityAlertThreshold = 1800.0f;

	public void Initialize(CarromBoard board, CarromPhysicsConfig physicsConfig,
		PackedScene whiteTemplate, PackedScene blackTemplate,
		PackedScene redTemplate, PackedScene strikerTemplate)
	{
		if (board == null || !GodotObject.IsInstanceValid(board))
		{
			GD.PrintErr("[CarromPieceFactory] Initialize failed - board is null or invalid");
			return;
		}

		if (physicsConfig == null || !GodotObject.IsInstanceValid(physicsConfig))
		{
			GD.PrintErr("[CarromPieceFactory] Initialize failed - physics config is null or invalid");
			return;
		}

		var templates = new (PackedScene template, string name)[]
		{
			(whiteTemplate, "white"),
			(blackTemplate, "black"),
			(redTemplate, "red"),
			(strikerTemplate, "striker")
		};

		foreach (var (template, name) in templates)
		{
			if (template == null || !GodotObject.IsInstanceValid(template))
			{
				GD.PrintErr($"[CarromPieceFactory] Initialize failed - {name} template is null or invalid");
				return;
			}
		}

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

	public void SetPhysicsLimits(float minVelocityThreshold, float angularMinThreshold,
		float maxVelocityLimit, float maxAngularVelocity, float velocityAlertThreshold)
	{
		_minVelocityThreshold = minVelocityThreshold;
		_angularMinThreshold = angularMinThreshold;
		_maxVelocityLimit = maxVelocityLimit;
		_maxAngularVelocity = maxAngularVelocity;
		_velocityAlertThreshold = velocityAlertThreshold;
	}

	public CarromPiece CreatePiece(PieceType type, Vector2 position)
	{
		if (_board == null || !GodotObject.IsInstanceValid(_board))
		{
			GD.PrintErr($"[CarromPieceFactory] Cannot create piece - board reference is null or invalid");
			return null;
		}

		PackedScene pieceTemplate = GetPieceTemplate(type);

		if (pieceTemplate == null)
		{
			GD.PrintErr($"[CarromPieceFactory] No template found for piece type: {type}");
			return null;
		}
		
		if (!GodotObject.IsInstanceValid(pieceTemplate))
		{
			GD.PrintErr($"[CarromPieceFactory] Piece template for {type} is invalid");
			return null;
		}

		CarromPiece piece = null;
		try
		{
			piece = pieceTemplate.Instantiate<CarromPiece>();
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[CarromPieceFactory] Failed to instantiate piece {type}: {ex.Message}");
			return null;
		}
		
		if (piece == null || !GodotObject.IsInstanceValid(piece))
		{
			GD.PrintErr($"[CarromPieceFactory] Failed to create valid piece instance for type: {type}");
			return null;
		}

		piece.Type = type;

		if (_physicsConfig != null && GodotObject.IsInstanceValid(_physicsConfig))
		{
			piece.PhysicsConfig = _physicsConfig;
		}

		piece.Position = position;

		piece.SetPhysicsLimits(_minVelocityThreshold, _angularMinThreshold,
			_maxVelocityLimit, _maxAngularVelocity, _velocityAlertThreshold);


		try
		{
			_board.AddChild(piece);
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"[CarromPieceFactory] Failed to add piece to board: {ex.Message}");
			piece?.QueueFree();
			return null;
		}

		// Include ALL pieces (including striker) for proper settlement detection
		_allPieces.Add(piece);

		EmitSignal(SignalName.PieceCreated, piece);

		return piece;
	}

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

	public IReadOnlyList<CarromPiece> GetAllPieces()
	{
		return _allPieces;
	}

	public List<CarromPiece> GetPiecesOfType(PieceType type)
	{
		var result = new List<CarromPiece>();
		foreach (var p in _allPieces)
		{
			if (GodotObject.IsInstanceValid(p) && p.Type == type)
				result.Add(p);
		}
		return result;
	}


	public override void _Notification(int what)
	{
		if (what == NotificationExitTree)
		{
			ClearAllPieces();
		}
	}
}