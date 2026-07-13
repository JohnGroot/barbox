#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BarBox.Core.Autoloads;
using Godot;
using LightResults;

namespace BarBox.Games.Nines;

/// <summary>
/// Main game controller for Nines card game.
/// Uses consolidation pattern with nested Engine, State, and CardVisual components.
/// </summary>
public partial class NinesGame : GameController
{
	protected override string GetGameId() => "nines";

	#region Constants

	private const string CONFIG_PATH = "res://_Games/Nines/NinesGameConfig.tres";

	#endregion

	#region Exports

	[Export]
	public NinesGameConfig Config { get; set; } = null!;

	#endregion

	#region Nested Components

	private NinesEngine _engine = null!;
	private NinesState _state = null!;
	private NinesUI _ui = null!;

	// Card visuals - keyed by grid position
	private readonly Dictionary<Vector2I, CardVisual> _cardVisuals = new();

	// Deck visual and label
	private CardVisual _deckVisual = null!;
	private Label _deckLabel = null!;

	// UI scaling
	private float _scaleFactor = 1.0f;
	private Vector2 _scaledCardSize;
	private float _scaledGridSpacing;

	// Session management
	private SessionManager? _sessionManager;

	// Credit management
	private CreditService? _creditService;
	private SessionEventService? _eventService;
	private NinesEventService? _ninesEventService;

	#endregion

	#region Lifecycle

	private void LoadConfig()
	{
		if (Config == null)
		{
			Config = GD.Load<NinesGameConfig>(CONFIG_PATH);
			if (Config == null)
			{
				GD.PrintErr("[Nines] Failed to load config, using defaults");
				Config = new NinesGameConfig();
			}
		}
	}

	protected override void OnInitializeComponents()
	{
		LoadConfig();

		_engine = new NinesEngine(this);
		_state = new NinesState(this);

		// Calculate scaling based on viewport
		var viewportSize = GetViewportRect().Size;
		_scaleFactor = Config.GetScaleFactor(viewportSize);
		_scaledCardSize = Config.GetScaledCardSize(_scaleFactor);
		_scaledGridSpacing = Config.GetScaledGridSpacing(_scaleFactor);
		GD.Print($"[Nines] Scale factor: {_scaleFactor}, Card size: {_scaledCardSize}");

		// Create deck visual
		_deckVisual = new CardVisual(this, null, _scaleFactor)
		{
			Position = GetDeckPosition(),
			ShowBack = true
		};
		AddChild(_deckVisual);

		// Create deck label to the right of the deck
		var deckPos = GetDeckPosition();
		_deckLabel = new Label
		{
			Text = "Deck: 0",
			Position = new Vector2(deckPos.X + _scaledCardSize.X + 10 * _scaleFactor, deckPos.Y + _scaledCardSize.Y / 2 - 12 * _scaleFactor)
		};
		_deckLabel.AddThemeFontSizeOverride("font_size", Config.GetScaledFontSize(24, _scaleFactor));
		AddChild(_deckLabel);

		// Setup services and UI
		SetupSessionManager();
		SetupCreditService();
		SetupUI();

		GD.Print($"[Nines] Game initialized - SessionEventService: {_eventService != null}");
	}

	private void SetupUI()
	{
		_ui = GetNodeOrNull<NinesUI>("UI");
		if (_ui == null)
		{
			_ui = new NinesUI();
			_ui.Name = "UI";
			AddChild(_ui);
		}
		_ui.Initialize(this);
	}

	/// <summary>
	/// Override to provide game-specific context buttons.
	/// During active game: show Forfeit button.
	/// Otherwise: show Return to Menu button.
	/// </summary>
	public override ContextButtonData[] GetContextButtons()
	{
		// During active game, show Forfeit button
		if (_state.CurrentPhase != GamePhase.Idle && _state.CurrentPhase != GamePhase.GameOver)
		{
			return new[]
			{
				new ContextButtonData("Forfeit", () => _ui?.ShowForfeitConfirmation(), "", true, "Forfeit the current game")
			};
		}

		// When not in active game, show Return to Menu
		return new[]
		{
			GameContextButton.CreateReturnToMenuButton(() => {
				_sessionManager?.ResetAllIdleTimers();
				ReturnToMainMenu();
			})
		};
	}

	/// <summary>
	/// Override to provide game title for TopMenuBar.
	/// </summary>
	public override string GetGameTitle() => "Nines";

	private void SetupSessionManager()
	{
		_sessionManager = Platform.Session;
		if (_sessionManager == null)
		{
			GD.Print("[Nines] SessionManager not available - single player mode");
			return;
		}

		// User login/logout signals are auto-connected by base GameController
		// Just sync existing sessions
		SyncExistingSessions();
	}

	protected override void OnGameTeardown()
	{
		AutoLogoutOnExit();
	}

	private void SyncExistingSessions()
	{
		if (_sessionManager == null)
			return;

		var phoneNumbers = _sessionManager.GetActivePhoneNumbers();
		foreach (var phoneNumber in phoneNumbers)
		{
			var session = _sessionManager.GetSessionByPhone(phoneNumber);
			if (session != null)
			{
				AddPlayerFromSession(session);
			}
		}

		_ui?.RefreshPlayerList();
	}

	private void SetupCreditService()
	{
		_creditService = Platform.Credits;
		_eventService = Platform.Events;
		_ninesEventService = new NinesEventService(_eventService);

		if (_creditService == null)
		{
			GD.Print("[Nines] CreditService not available");
		}
		else
		{
			GD.Print("[Nines] CreditService available");
		}

		// Load jackpot from backend (time-based calculation)
		InitializeJackpotAsync();
	}

	private async void InitializeJackpotAsync()
	{
		await RefreshJackpotFromBackendAsync();
	}

	/// <summary>
	/// Query backend for last jackpot win timestamp and calculate current jackpot value.
	/// Formula: BaseJackpotValue + (DaysSinceLastWin * DailyJackpotGrowth)
	/// </summary>
	private async Task RefreshJackpotFromBackendAsync()
	{
		if (_eventService == null)
		{
			// No backend - use base value
			_state.JackpotAmount = Config.BaseJackpotValue;
			_ui?.UpdateJackpotDisplay(_state.JackpotAmount);
			GD.Print($"[Nines] No backend - using base jackpot: {_state.JackpotAmount}");
			return;
		}

		try
		{
			var venueName = _eventService.GetVenueName();

			// Query backend for last jackpot win timestamp
			var queryParams = new Dictionary<string, string> { { "venue_name", venueName } };
			var queryResult = await _eventService.QueryAsync<NinesJackpotResponse>(
				$"/game/nines/jackpot/{venueName}",
				queryParams);

			if (queryResult.IsSuccess(out var response) && response != null)
			{
				// Parse timestamp from backend response
				if (response.LastWinTimestamp.HasValue)
				{
					var lastWinTimestamp = response.LastWinTimestamp.Value;
					var daysSinceWin = (DateTime.UtcNow - lastWinTimestamp).TotalDays;
					var daysInt = (int)Math.Floor(daysSinceWin);

					_state.JackpotAmount = Config.BaseJackpotValue + (daysInt * Config.DailyJackpotGrowth);
					GD.Print($"[Nines] Jackpot calculated: {Config.BaseJackpotValue} + ({daysInt} days * {Config.DailyJackpotGrowth}) = {_state.JackpotAmount}");
				}
				else
				{
					// No previous win - use base value
					_state.JackpotAmount = Config.BaseJackpotValue;
					GD.Print($"[Nines] No previous win recorded - using base jackpot: {_state.JackpotAmount}");
				}
			}
			else
			{
				// Query failed - use base value
				_state.JackpotAmount = Config.BaseJackpotValue;
				GD.Print($"[Nines] Backend query failed - using base jackpot: {_state.JackpotAmount}");
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[Nines] Error querying jackpot: {ex.Message}");
			_state.JackpotAmount = Config.BaseJackpotValue;
		}

		_ui?.UpdateJackpotDisplay(_state.JackpotAmount);
	}

	/// <summary>
	/// Record jackpot win with backend to reset the time-based calculation.
	/// </summary>
	private async Task RecordJackpotWinAsync()
	{
		if (_ninesEventService == null)
		{
			GD.Print("[Nines] No event service - jackpot win not recorded");
			return;
		}

		var winningPlayer = GetCurrentPlayer();

		// player_id is the winner's phone number (Nines convention, not a UUID).
		var result = await _ninesEventService.EmitJackpotWonAsync(
			_ninesEventService.GetVenueName(),
			winningPlayer?.PhoneNumber ?? "unknown",
			winningPlayer?.DisplayName ?? "Unknown",
			_state.JackpotAmount);

		if (result.IsSuccess(out _))
			GD.Print("[Nines] Jackpot win recorded with backend");
		else if (result.IsFailure(out var error))
			GD.PrintErr($"[Nines] Failed to record jackpot win: {error.Message}");
	}

	private void AutoLogoutOnExit()
	{
		if (_sessionManager == null || _state.Players.Count == 0)
			return;

		// Find earliest logged-in player (by slot index)
		var earliestPlayer = _state.Players
			.Where(p => p.IsLoggedIn)
			.OrderBy(p => p.SlotIndex)
			.FirstOrDefault();

		// Logout all except earliest
		foreach (var player in _state.Players.Where(p => p.IsLoggedIn && p != earliestPlayer))
		{
			GD.Print($"[Nines] Auto-logout: {player.DisplayName}");
			_ = _sessionManager.LogoutUserAsync(player.PhoneNumber);
		}
	}

	#endregion

	#region Player Management

	protected override void OnUserLoggedIn(UserSession session)
	{
		if (session == null)
			return;

		var phoneNumber = session.PhoneNumber;

		// Check if already in players list
		if (_state.Players.Any(p => p.PhoneNumber == phoneNumber))
		{
			// Update existing player data
			var existingPlayer = _state.Players.First(p => p.PhoneNumber == phoneNumber);
			existingPlayer.DisplayName = !string.IsNullOrEmpty(session.UserName) ? session.UserName : "Player";
			// Credits will be fetched from SessionEventService in credit flow implementation
		}
		else if (_state.Players.Count < Config.MaxPlayers)
		{
			// Add new player
			AddPlayerFromSession(session);
		}

		_ui?.RefreshPlayerList();
		_ui?.RefreshJackpotButtonState();
	}

	protected override void OnUserLoggedOut(string phoneNumber)
	{
		// Block logout during active game
		if (_state.CurrentPhase != GamePhase.Idle && _state.CurrentPhase != GamePhase.GameOver)
		{
			GD.PrintErr("[Nines] Cannot logout during active game");
			return;
		}

		var player = _state.Players.FirstOrDefault(p => p.PhoneNumber == phoneNumber);
		if (player != null)
		{
			HandlePlayerRemoval(player);
		}

		_ui?.RefreshPlayerList();
		_ui?.RefreshJackpotButtonState();
	}

	private void AddPlayerFromSession(UserSession session)
	{
		var slotIndex = _state.Players.Count;
		var displayName = !string.IsNullOrEmpty(session.UserName)
			? session.UserName
			: $"Player {slotIndex + 1}";

		var player = new NinesPlayer
		{
			PhoneNumber = session.PhoneNumber,
			DisplayName = displayName,
			Credits = 0, // Credits will be fetched from SessionEventService in credit flow implementation
			SlotIndex = slotIndex
		};

		_state.Players.Add(player);
		GD.Print($"[Nines] Player added: {player.DisplayName} (slot {slotIndex})");
	}

	public void AddAnonymousPlayer()
	{
		if (_state.Players.Count >= Config.MaxPlayers)
		{
			GD.PrintErr("[Nines] Cannot add more players - max reached");
			return;
		}

		var slotIndex = _state.Players.Count;
		var player = new NinesPlayer
		{
			PhoneNumber = string.Empty, // Anonymous
			DisplayName = $"Player {slotIndex + 1}",
			Credits = 0,
			SlotIndex = slotIndex
		};

		_state.Players.Add(player);
		_ui?.RefreshPlayerList();
		GD.Print($"[Nines] Anonymous player added: {player.DisplayName}");
	}

	private void HandlePlayerRemoval(NinesPlayer player)
	{
		// If current player, advance turn
		if (_state.CurrentPlayerIndex >= 0 &&
		    _state.CurrentPlayerIndex < _state.Players.Count &&
		    _state.Players[_state.CurrentPlayerIndex] == player)
		{
			// Skip to next player if game is active
			if (_state.CurrentPhase == GamePhase.TurnActive)
			{
				AdvanceToNextPlayer();
			}
		}

		_state.Players.Remove(player);

		// Recalculate slot indices
		for (int i = 0; i < _state.Players.Count; i++)
		{
			// Note: SlotIndex is init-only, but we keep original order
		}

		// Adjust current player index if needed
		if (_state.CurrentPlayerIndex >= _state.Players.Count)
		{
			_state.CurrentPlayerIndex = Math.Max(0, _state.Players.Count - 1);
		}

		GD.Print($"[Nines] Player removed: {player.DisplayName}");
	}

	public IReadOnlyList<NinesPlayer> GetPlayers() => _state.Players;

	public int GetCurrentPlayerIndex() => _state.CurrentPlayerIndex;

	public NinesPlayer? GetPlayer(int index)
	{
		if (index >= 0 && index < _state.Players.Count)
			return _state.Players[index];
		return null;
	}

	#endregion

	#region Game Flow - Public API

	/// <summary>
	/// Show a notification via the shared platform overlay. Lets NinesUI
	/// surface errors without reaching into Platform directly.
	/// </summary>
	internal void ShowNotification(string message, NotificationSeverity severity) =>
		Platform.Notifications?.Show(message, severity);

	/// <summary>
	/// Check if game can be started - requires at least one logged-in player.
	/// </summary>
	public bool CanPlay()
	{
		// Need credit service for entry fee
		if (_creditService == null)
			return false;

		// Need at least one player
		if (_state.Players.Count == 0)
			return false;

		// All players must be logged in (to deduct credits)
		return _state.Players.All(p => p.IsLoggedIn);
	}

	/// <summary>
	/// Start a new game - deducts entry cost from all players.
	/// </summary>
	public async Task<bool> StartGameAsync()
	{
		GD.Print("[Nines] Starting game");

		if (!CanPlay())
		{
			GD.PrintErr("[Nines] Cannot start game - requirements not met");
			return false;
		}

		// Deduct credits from all players with rollback on failure
		var deductionResult = await DeductCreditsWithRollbackAsync();
		if (deductionResult.IsFailure(out var error))
		{
			_ui?.ShowError(error.Message);
			return false;
		}

		// Create the backend activity session so game events (jackpot win) are
		// recorded against a real session. Non-fatal: the game still plays if it fails.
		await TryCreateBackendSessionAsync();

		StartGame();
		return true;
	}

	/// <summary>
	/// Create the backend activity session for the current logged-in players.
	/// Nines previously skipped this entirely, leaving jackpot events without a
	/// session to attach to. Session close is handled by the base class.
	/// </summary>
	private async Task TryCreateBackendSessionAsync()
	{
		if (_sessionManager == null)
			return;

		var sessions = new List<UserSession>();
		foreach (var player in _state.Players.Where(p => p.IsLoggedIn))
		{
			var session = _sessionManager.GetSessionByPhone(player.PhoneNumber);
			if (session != null)
				sessions.Add(session);
		}

		if (sessions.Count == 0)
			return;

		var boxId = Platform.BoxId;
		var hostPlayerId = sessions[0].PlayerId;
		var playerIdStrings = sessions.Select(s => s.PlayerId.ToString()).ToList();

		var result = await StartBackendSessionAsync(boxId, hostPlayerId, playerIdStrings);
		if (result.IsFailure(out var error))
			GD.PrintErr($"[Nines] WARNING: Failed to create backend session: {error.Message} - jackpot win may not record");
		else
			GD.Print("[Nines] Backend session created");
	}

	private async Task<Result<bool>> DeductCreditsWithRollbackAsync()
	{
		if (_creditService == null)
		{
			return Result.Failure<bool>("Credit service not available");
		}

		var players = new List<(Guid PlayerId, string Label)>();
		foreach (var player in _state.Players.Where(p => p.IsLoggedIn))
		{
			var session = _sessionManager?.GetSessionByPhone(player.PhoneNumber);
			if (session == null)
			{
				GD.PrintErr($"[Nines] No session for player: {player.DisplayName}");
				return Result.Failure<bool>($"No session for player: {player.DisplayName}");
			}

			players.Add((session.PlayerId, player.DisplayName));
		}

		var result = await _creditService.SpendManyWithRollbackAsync(players, Config.EntryCost, "Nines Entry");
		if (result.IsFailure(out var error))
		{
			GD.PrintErr($"[Nines] {error.Message}");
			return Result.Failure<bool>(error.Message);
		}

		GD.Print($"[Nines] All entry fees collected ({players.Count} players x {Config.EntryCost} credits)");
		return Result.Success(true);
	}

	private void StartGame()
	{
		_engine.InitializeDeck();
		_state.Reset();
		CreateCardStacks();

		TransitionToPhase(GamePhase.Dealing);
		StartDealingSequence();
	}

	public void EndGame(GameEndReason reason)
	{
		TransitionToPhase(GamePhase.GameOver);

		switch (reason)
		{
			case GameEndReason.Win:
				GD.Print("[Nines] Game Won!");
				var jackpotWon = _state.JackpotAmount;
				AwardJackpotAsync(jackpotWon);
				_ui?.ShowResultsModal(true, jackpotWon);
				break;

			case GameEndReason.Lose:
				GD.Print("[Nines] Game Lost!");
				_ui?.ShowResultsModal(false, 0);
				break;

			case GameEndReason.Forfeit:
				GD.Print("[Nines] Game Forfeited!");
				ForceReturnToMenu();
				break;
		}
	}

	public void ForfeitGame()
	{
		if (_state.CurrentPhase == GamePhase.Idle || _state.CurrentPhase == GamePhase.GameOver)
			return;

		EndGame(GameEndReason.Forfeit);
	}

	private async void AwardJackpotAsync(int jackpotAmount)
	{
		if (_creditService == null)
			return;

		GD.Print($"[Nines] Awarding jackpot of {jackpotAmount} credits");

		// Award jackpot to the player who finished the deck (current player)
		var winningPlayer = GetCurrentPlayer();
		if (winningPlayer == null || !winningPlayer.IsLoggedIn)
		{
			GD.PrintErr("[Nines] No valid winning player to award jackpot");
			return;
		}

		var session = _sessionManager?.GetSessionByPhone(winningPlayer.PhoneNumber);
		if (session == null)
		{
			GD.PrintErr($"[Nines] No session for winning player: {winningPlayer.DisplayName}");
			return;
		}

		var addResult = await _creditService.AddAsync(session.PlayerId, jackpotAmount, "Nines Jackpot Win");
		if (addResult.IsFailure(out var error))
		{
			GD.PrintErr($"[Nines] Failed to award jackpot: {error.Message}");
			return;
		}

		GD.Print($"[Nines] Jackpot of {jackpotAmount} awarded to {winningPlayer.DisplayName}");

		// Record jackpot win with backend for time-based calculation
		await RecordJackpotWinAsync();

		// Refresh jackpot display (will recalculate based on new timestamp)
		await RefreshJackpotFromBackendAsync();
	}

	public void ReturnToMenu()
	{
		// If game is active, show forfeit confirmation instead
		if (_state.CurrentPhase != GamePhase.Idle && _state.CurrentPhase != GamePhase.GameOver)
		{
			_ui?.ShowForfeitConfirmation();
			return;
		}

		ForceReturnToMenu();
	}

	private void ForceReturnToMenu()
	{
		ClearCardVisuals();
		_state.Reset();
		TransitionToPhase(GamePhase.Idle);
		_ui?.ShowMainMenu();
	}

	#endregion

	#region Phase Management

	private void TransitionToPhase(GamePhase newPhase)
	{
		if (_state.CurrentPhase == newPhase)
			return;

		var oldPhase = _state.CurrentPhase;
		ExitPhase(oldPhase);
		_state.CurrentPhase = newPhase;
		EnterPhase(newPhase);

		// Update TopMenuBar context when phase changes
		RefreshUI();

		GD.Print($"[Nines] Phase: {oldPhase} -> {newPhase}");
	}

	private void EnterPhase(GamePhase phase)
	{
		switch (phase)
		{
			case GamePhase.Idle:
				_ui?.ShowMainMenu();
				break;

			case GamePhase.TurnActive:
				_state.TurnSubState = TurnSubState.SelectingStack;
				_ui?.UpdateCurrentPlayer(GetCurrentPlayerName());
				EnableStackSelection();
				break;

			case GamePhase.GameOver:
				DisableAllInput();
				break;
		}
	}

	private void ExitPhase(GamePhase phase)
	{
		switch (phase)
		{
			case GamePhase.Idle:
				_ui?.HideMainMenu();
				break;

			case GamePhase.TurnActive:
				DisableStackSelection();
				_ui?.HidePredictionPopup();
				break;
		}
	}

	private void SetTurnSubState(TurnSubState subState)
	{
		_state.TurnSubState = subState;

		switch (subState)
		{
			case TurnSubState.SelectingStack:
				EnableStackSelection();
				break;

			case TurnSubState.SelectingPrediction:
				if (_state.SelectedStack != null)
				{
					var screenPos = GetStackScreenPosition(_state.SelectedStack);
					_ui?.ShowPredictionPopup(screenPos);
				}
				break;

			case TurnSubState.SelectingRevive:
				EnableFlippedStackSelection();
				break;
		}
	}

	#endregion

	#region Card Stack Management

	private void CreateCardStacks()
	{
		ClearCardVisuals();

		for (int row = 0; row < Config.GridSize; row++)
		{
			for (int col = 0; col < Config.GridSize; col++)
			{
				var gridPos = new Vector2I(col, row);
				var stack = new CardStack { GridPosition = gridPos };
				_state.Stacks.Add(stack);
			}
		}
	}

	private void ClearCardVisuals()
	{
		foreach (var visual in _cardVisuals.Values)
		{
			visual.QueueFree();
		}
		_cardVisuals.Clear();
		_state.Stacks.Clear();
	}

	private Vector2 GetStackWorldPosition(CardStack stack)
	{
		var gridOffset = GetGridOffset();
		return new Vector2(
			gridOffset.X + stack.GridPosition.X * (_scaledCardSize.X + _scaledGridSpacing),
			gridOffset.Y + stack.GridPosition.Y * (_scaledCardSize.Y + _scaledGridSpacing)
		);
	}

	private Vector2 GetStackScreenPosition(CardStack stack)
	{
		return GetStackWorldPosition(stack) + _scaledCardSize / 2;
	}

	private Vector2 GetGridOffset()
	{
		var viewportSize = GetViewportRect().Size;
		var gridSize = GetScaledGridTotalSize();
		return (viewportSize - gridSize) / 2;
	}

	private Vector2 GetScaledGridTotalSize()
	{
		return new Vector2(
			Config.GridSize * _scaledCardSize.X + (Config.GridSize - 1) * _scaledGridSpacing,
			Config.GridSize * _scaledCardSize.Y + (Config.GridSize - 1) * _scaledGridSpacing
		);
	}

	private Vector2 GetDeckPosition()
	{
		var gridOffset = GetGridOffset();
		var gridTotalSize = GetScaledGridTotalSize();

		// Center horizontally above grid, with scaled gap above grid
		var gap = 30 * _scaleFactor;
		return new Vector2(
			gridOffset.X + (gridTotalSize.X - _scaledCardSize.X) / 2,
			gridOffset.Y - _scaledCardSize.Y - gap
		);
	}

	#endregion

	#region Dealing Sequence

	private void StartDealingSequence()
	{
		var tween = CreateTween();

		for (int i = 0; i < _state.Stacks.Count; i++)
		{
			var stack = _state.Stacks[i];
			var card = _engine.DrawCard();
			if (card == null)
			{
				GD.PrintErr("[Nines] Not enough cards to deal!");
				break;
			}

			stack.AddCard(card.Value);

			// Create visual at deck position
			var visual = new CardVisual(this, card.Value, _scaleFactor)
			{
				Position = GetDeckPosition(),
				ShowBack = true
			};
			AddChild(visual);
			_cardVisuals[stack.GridPosition] = visual;

			// Tween to stack position, then flip
			var targetPos = GetStackWorldPosition(stack);
			tween.TweenProperty(visual, "position", targetPos, Config.DealCardDuration);
			tween.TweenInterval(Config.DealDelayBetweenCards);
			tween.TweenCallback(Callable.From(() => visual.FlipToFront()));
		}

		// Update deck count
		tween.TweenCallback(Callable.From(() =>
		{
			_deckLabel.Text = $"Deck: {_engine.CardsRemaining}";
			TransitionToPhase(GamePhase.TurnActive);
		}));
	}

	#endregion

	#region Input Handling

	private void EnableStackSelection()
	{
		// Enable input on face-up stack visuals
		foreach (var (gridPos, visual) in _cardVisuals)
		{
			var stack = _state.Stacks.FirstOrDefault(s => s.GridPosition == gridPos);
			if (stack != null && stack.IsFaceUp)
			{
				visual.Selectable = true;
			}
		}
	}

	private void DisableStackSelection()
	{
		foreach (var visual in _cardVisuals.Values)
		{
			visual.Selectable = false;
			visual.Selected = false;
		}
	}

	private void EnableFlippedStackSelection()
	{
		// Enable input only on face-down stack visuals
		foreach (var (gridPos, visual) in _cardVisuals)
		{
			var stack = _state.Stacks.FirstOrDefault(s => s.GridPosition == gridPos);
			if (stack != null && !stack.IsFaceUp)
			{
				visual.Selectable = true;
			}
		}
	}

	private void DisableAllInput()
	{
		DisableStackSelection();
	}

	public void OnStackSelected(CardStack stack)
	{
		if (_state.CurrentPhase != GamePhase.TurnActive)
			return;

		switch (_state.TurnSubState)
		{
			case TurnSubState.SelectingStack:
				_state.SelectedStack = stack;
				HighlightStack(stack);
				SetTurnSubState(TurnSubState.SelectingPrediction);
				break;

			case TurnSubState.SelectingRevive:
				ReviveStack(stack);
				break;
		}
	}

	public void OnPredictionMade(PredictionType prediction)
	{
		if (_state.CurrentPhase != GamePhase.TurnActive ||
		    _state.TurnSubState != TurnSubState.SelectingPrediction)
			return;

		_state.CurrentPrediction = prediction;
		_ui?.HidePredictionPopup();
		TransitionToPhase(GamePhase.Resolving);
		ResolvePrediction();
	}

	public void OnPredictionCancelled()
	{
		if (_state.TurnSubState == TurnSubState.SelectingPrediction)
		{
			ClearStackHighlight();
			_state.SelectedStack = null;
			_ui?.HidePredictionPopup();
			SetTurnSubState(TurnSubState.SelectingStack);
		}
	}

	private void HighlightStack(CardStack stack)
	{
		if (_cardVisuals.TryGetValue(stack.GridPosition, out var visual))
		{
			visual.Selected = true;
			visual.QueueRedraw();
		}
	}

	private void ClearStackHighlight()
	{
		foreach (var visual in _cardVisuals.Values)
		{
			visual.Selected = false;
			visual.QueueRedraw();
		}
	}

	#endregion

	#region Prediction Resolution

	private void ResolvePrediction()
	{
		if (_state.SelectedStack == null || _state.CurrentPrediction == null)
		{
			GD.PrintErr("[Nines] Cannot resolve - no stack or prediction selected");
			TransitionToPhase(GamePhase.TurnActive);
			return;
		}

		var drawnCard = _engine.DrawCard();
		if (drawnCard == null)
		{
			// Deck empty - this shouldn't happen mid-prediction
			GD.PrintErr("[Nines] Deck empty during prediction!");
			EndGame(GameEndReason.Win);
			return;
		}

		var targetCard = _state.SelectedStack.TopCard;
		if (targetCard == null)
		{
			GD.PrintErr("[Nines] Target stack has no card!");
			return;
		}

		var result = _engine.EvaluatePrediction(
			_state.CurrentPrediction.Value,
			targetCard.Value,
			drawnCard.Value
		);

		AnimateCardReveal(drawnCard.Value, result);
	}

	private void AnimateCardReveal(PlayingCard drawnCard, PredictionResult result)
	{
		var tween = CreateTween();

		// Create card visual at deck
		var cardVisual = new CardVisual(this, drawnCard, _scaleFactor)
		{
			Position = GetDeckPosition(),
			ShowBack = true
		};
		AddChild(cardVisual);

		// Move toward selected stack
		var targetPos = GetStackWorldPosition(_state.SelectedStack!);
		tween.TweenProperty(cardVisual, "position", targetPos, Config.CardPlaceDuration);

		// Flip and reveal
		tween.TweenCallback(Callable.From(() => cardVisual.FlipToFront()));
		tween.TweenInterval(Config.RevealDelay);

		// Show result feedback
		tween.TweenCallback(Callable.From(() =>
		{
			_ui?.ShowResultFeedback(result, GetStackScreenPosition(_state.SelectedStack!));
		}));

		tween.TweenInterval(Config.ResultFeedbackDuration);

		// Process result
		tween.TweenCallback(Callable.From(() =>
		{
			ProcessPredictionResult(result, drawnCard, cardVisual);
		}));
	}

	private void ProcessPredictionResult(PredictionResult result, PlayingCard drawnCard, CardVisual cardVisual)
	{
		_deckLabel.Text = $"Deck: {_engine.CardsRemaining}";
		UpdatePlayerStats(result);

		switch (result)
		{
			case PredictionResult.Correct:
				PlaceCardOnStack(drawnCard, cardVisual);
				CheckWinCondition();
				AdvanceToNextPlayer();
				break;

			case PredictionResult.SameCorrect:
				PlaceCardOnStack(drawnCard, cardVisual);
				CheckWinCondition();
				HandleSameBonus();
				break;

			case PredictionResult.Wrong:
			case PredictionResult.SameWrong:
				cardVisual.QueueFree(); // Remove the drawn card
				FlipStackFaceDown(_state.SelectedStack!);
				CheckLoseCondition();
				AdvanceToNextPlayer();
				break;
		}
	}

	private void PlaceCardOnStack(PlayingCard card, CardVisual visual)
	{
		if (_state.SelectedStack == null)
			return;

		_state.SelectedStack.AddCard(card);

		// Update visual to show new top card
		if (_cardVisuals.TryGetValue(_state.SelectedStack.GridPosition, out var stackVisual))
		{
			stackVisual.SetCard(card);
		}

		visual.QueueFree();
	}

	private void FlipStackFaceDown(CardStack stack)
	{
		stack.FlipFaceDown();

		if (_cardVisuals.TryGetValue(stack.GridPosition, out var visual))
		{
			visual.FlipToBack();
		}
	}

	private void ReviveStack(CardStack stack)
	{
		stack.FlipFaceUp();

		if (_cardVisuals.TryGetValue(stack.GridPosition, out var visual))
		{
			visual.FlipToFront();
		}

		// After reviving, player draws again
		DisableStackSelection();
		_state.SelectedStack = null;
		TransitionToPhase(GamePhase.TurnActive);
	}

	private void HandleSameBonus()
	{
		// Check if there are any facedown stacks to revive
		var facedownStacks = _state.Stacks.Where(s => !s.IsFaceUp).ToList();

		if (facedownStacks.Count > 0)
		{
			GD.Print("[Nines] Same bonus! Player can revive a facedown stack");
			TransitionToPhase(GamePhase.TurnActive);
			SetTurnSubState(TurnSubState.SelectingRevive);
		}
		else
		{
			// No facedown stacks, just draw again (same player continues)
			GD.Print("[Nines] Same bonus! Player draws again (no stacks to revive)");
			_state.SelectedStack = null;
			TransitionToPhase(GamePhase.TurnActive);
		}
	}

	private void UpdatePlayerStats(PredictionResult result)
	{
		var player = GetCurrentPlayer();
		if (player == null)
			return;

		switch (result)
		{
			case PredictionResult.Correct:
				player.CorrectPredictions++;
				break;
			case PredictionResult.SameCorrect:
				player.SameCorrectPredictions++;
				break;
			case PredictionResult.Wrong:
			case PredictionResult.SameWrong:
				player.WrongPredictions++;
				break;
		}
	}

	#endregion

	#region Win/Lose Conditions

	private void CheckWinCondition()
	{
		if (_engine.CardsRemaining == 0)
		{
			GD.Print("[Nines] Deck empty - WIN!");
			EndGame(GameEndReason.Win);
		}
	}

	private void CheckLoseCondition()
	{
		var faceUpStacks = _state.Stacks.Count(s => s.IsFaceUp);
		if (faceUpStacks == 0)
		{
			GD.Print("[Nines] All stacks flipped - LOSE!");
			EndGame(GameEndReason.Lose);
		}
	}

	#endregion

	#region Turn Management

	private void AdvanceToNextPlayer()
	{
		if (_state.CurrentPhase == GamePhase.GameOver)
			return;

		_state.CurrentPlayerIndex = (_state.CurrentPlayerIndex + 1) % Math.Max(1, _state.Players.Count);
		_state.SelectedStack = null;
		_state.CurrentPrediction = null;

		TransitionToPhase(GamePhase.TurnActive);
	}

	private NinesPlayer? GetCurrentPlayer()
	{
		if (_state.Players.Count == 0)
			return null;

		return _state.Players[_state.CurrentPlayerIndex];
	}

	private string GetCurrentPlayerName()
	{
		var player = GetCurrentPlayer();
		return player?.DisplayName ?? "Player 1";
	}

	#endregion

	#region Nested Class: NinesEngine

	public class NinesEngine
	{
		private readonly NinesGame _game;
		private readonly List<PlayingCard> _deck = new();

		public int CardsRemaining => _deck.Count;

		public NinesEngine(NinesGame game)
		{
			_game = game;
		}

		public void InitializeDeck()
		{
			_deck.Clear();

			foreach (CardSuit suit in Enum.GetValues(typeof(CardSuit)))
			{
				foreach (CardRank rank in Enum.GetValues(typeof(CardRank)))
				{
					_deck.Add(new PlayingCard { Rank = rank, Suit = suit });
				}
			}

			Shuffle();
		}

		public void Shuffle()
		{
			// Fisher-Yates shuffle
			var random = new Random();
			for (int i = _deck.Count - 1; i > 0; i--)
			{
				int j = random.Next(i + 1);
				(_deck[i], _deck[j]) = (_deck[j], _deck[i]);
			}
		}

		public PlayingCard? DrawCard()
		{
			if (_deck.Count == 0)
				return null;

			var card = _deck[^1];
			_deck.RemoveAt(_deck.Count - 1);
			return card;
		}

		public PredictionResult EvaluatePrediction(PredictionType prediction, PlayingCard targetCard, PlayingCard drawnCard)
		{
			bool isHigher = drawnCard.IsHigherThan(targetCard);
			bool isLower = drawnCard.IsLowerThan(targetCard);
			bool isSame = drawnCard.IsSameAs(targetCard);

			return prediction switch
			{
				PredictionType.Higher => isHigher ? PredictionResult.Correct : PredictionResult.Wrong,
				PredictionType.Lower => isLower ? PredictionResult.Correct : PredictionResult.Wrong,
				PredictionType.Same => isSame ? PredictionResult.SameCorrect : PredictionResult.SameWrong,
				_ => PredictionResult.Wrong
			};
		}
	}

	#endregion

	#region Nested Class: NinesState

	public class NinesState
	{
		private readonly NinesGame _game;

		public GamePhase CurrentPhase { get; set; } = GamePhase.Idle;
		public TurnSubState TurnSubState { get; set; } = TurnSubState.SelectingStack;

		public List<CardStack> Stacks { get; } = new();
		public List<NinesPlayer> Players { get; } = new();

		public int CurrentPlayerIndex { get; set; }
		public CardStack? SelectedStack { get; set; }
		public PredictionType? CurrentPrediction { get; set; }

		public int JackpotAmount { get; set; }

		public NinesState(NinesGame game)
		{
			_game = game;
			// Players populated via SessionManager.SyncExistingSessions()
		}

		public void Reset()
		{
			CurrentPhase = GamePhase.Idle;
			TurnSubState = TurnSubState.SelectingStack;
			Stacks.Clear();
			CurrentPlayerIndex = 0;
			SelectedStack = null;
			CurrentPrediction = null;
		}

		public int FaceUpStackCount => Stacks.Count(s => s.IsFaceUp);
		public bool AllStacksFlipped => FaceUpStackCount == 0;
	}

	#endregion

	#region Nested Class: CardVisual

	public partial class CardVisual : Node2D
	{
		private readonly NinesGame _game;
		private readonly float _scale;
		private PlayingCard? _card;

		public bool ShowBack { get; set; }
		public bool Selectable { get; set; }
		public bool Selected { get; set; }

		private NinesGameConfig Config => _game.Config;
		private Vector2 ScaledCardSize => Config.GetScaledCardSize(_scale);

		public CardVisual(NinesGame game, PlayingCard? card, float scaleFactor)
		{
			_game = game;
			_card = card;
			_scale = scaleFactor;
		}

		public void SetCard(PlayingCard card)
		{
			_card = card;
			ShowBack = false;
			QueueRedraw();
		}

		public void FlipToFront()
		{
			ShowBack = false;
			QueueRedraw();
		}

		public void FlipToBack()
		{
			ShowBack = true;
			QueueRedraw();
		}

		public override void _Draw()
		{
			var rect = new Rect2(Vector2.Zero, ScaledCardSize);
			var scaledBorderWidth = Config.GetScaledBorderWidth(_scale);

			if (ShowBack)
			{
				DrawCardBack(rect, scaledBorderWidth);
			}
			else if (_card.HasValue)
			{
				DrawCardFront(rect, _card.Value, scaledBorderWidth);
			}

			if (Selected)
			{
				DrawRect(rect, Config.SelectedHighlightColor, false, 4f * _scale);
			}
		}

		private void DrawCardBack(Rect2 rect, float borderWidth)
		{
			// Card back background
			DrawRect(rect, Config.CardBackColor);

			// Border
			DrawRect(rect, Config.CardBorderColor, false, borderWidth);

			// Simple pattern
			var center = rect.Size / 2;
			var patternSize = rect.Size * 0.6f;
			var patternRect = new Rect2(
				center - patternSize / 2,
				patternSize
			);
			DrawRect(patternRect, Config.CardBackColor.Lightened(0.2f), false, 2f * _scale);
		}

		private void DrawCardFront(Rect2 rect, PlayingCard card, float borderWidth)
		{
			// Card face background
			DrawRect(rect, Config.CardFaceColor);

			// Border
			DrawRect(rect, Config.CardBorderColor, false, borderWidth);

			// Get color based on suit
			var textColor = card.IsRed ? Config.RedSuitColor : Config.BlackSuitColor;

			// Draw rank and suit in corners
			var rankStr = card.GetRankDisplay();
			var suitStr = card.GetSuitSymbol();

			// Scaled positions and font sizes
			var smallFontSize = Config.GetScaledFontSize(16, _scale);
			var suitFontSize = Config.GetScaledFontSize(14, _scale);
			var centerFontSize = Config.GetScaledFontSize(32, _scale);

			// Top-left
			DrawCardText(rankStr, new Vector2(8 * _scale, 20 * _scale), textColor, smallFontSize);
			DrawCardText(suitStr, new Vector2(8 * _scale, 38 * _scale), textColor, suitFontSize);

			// Center suit (larger)
			var centerPos = rect.Size / 2 - new Vector2(10 * _scale, 12 * _scale);
			DrawCardText(suitStr, centerPos, textColor, centerFontSize);

			// Bottom-right (rotated 180 - just offset for now)
			var bottomRight = rect.Size - new Vector2(20 * _scale, 20 * _scale);
			DrawCardText(rankStr, bottomRight, textColor, smallFontSize);
			DrawCardText(suitStr, bottomRight + new Vector2(0, 18 * _scale), textColor, suitFontSize);
		}

		private void DrawCardText(string text, Vector2 position, Color color, int fontSize)
		{
			// Use default font
			var font = ThemeDB.FallbackFont;
			if (font != null)
			{
				DrawString(font, position, text, HorizontalAlignment.Left, -1, fontSize, color);
			}
		}

		public override void _Input(InputEvent @event)
		{
			if (!Selectable)
				return;

			if (@event is InputEventMouseButton mouseButton &&
			    mouseButton.ButtonIndex == MouseButton.Left &&
			    mouseButton.Pressed)
			{
				var localPos = ToLocal(mouseButton.Position);
				var rect = new Rect2(Vector2.Zero, ScaledCardSize);

				if (rect.HasPoint(localPos))
				{
					OnCardClicked();
				}
			}
			else if (@event is InputEventScreenTouch touch && touch.Pressed)
			{
				var localPos = ToLocal(touch.Position);
				var rect = new Rect2(Vector2.Zero, ScaledCardSize);

				if (rect.HasPoint(localPos))
				{
					OnCardClicked();
				}
			}
		}

		private void OnCardClicked()
		{
			// Find the stack for this visual and notify game
			foreach (var (gridPos, visual) in _game._cardVisuals)
			{
				if (visual == this)
				{
					var stack = _game._state.Stacks.FirstOrDefault(s => s.GridPosition == gridPos);
					if (stack != null)
					{
						_game.OnStackSelected(stack);
					}
					break;
				}
			}
		}
	}

	#endregion
}
