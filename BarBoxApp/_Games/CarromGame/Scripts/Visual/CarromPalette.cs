using BarBox.Core.Drawing;
using Godot;

namespace BarBox.Games.Carrom;

/// <summary>
/// Colors meaningful only to the Carrom game — see BarBox.Core.Drawing.Palette's doc comment for
/// the split. Built from Palette's base swatches where possible, rather than new literals.
/// </summary>
public static class CarromPalette
{
	/// <summary>Restores a piece/striker's modulate to fully visible after a pocket or reset tween.</summary>
	public static readonly Color ModulateOpaque = new(1f, 1f, 1f, 1f);

	/// <summary>Hides a piece/striker's modulate while it waits to be tweened back onto the board.</summary>
	public static readonly Color ModulateHidden = new(1f, 1f, 1f, 0f);

	/// <summary>Bright green accent shared by the "good outcome" notification, the current-player indicator, and input-ready feedback.</summary>
	public static readonly Color BrightGreen = new(0.2f, 0.9f, 0.3f, 1f);

	/// <summary>Alert red shared by the foul notification and blocked-input feedback.</summary>
	public static readonly Color AlertRed = new(0.9f, 0.3f, 0.2f, 1f);

	/// <summary>Dark gray panel fill shared by empty player slots and inactive score entries.</summary>
	public static readonly Color DarkGray = new(0.15f, 0.15f, 0.15f, 1f);

	/// <summary>Neutral gray border shared by empty player slots and the debug state label.</summary>
	public static readonly Color BorderGray = new(0.3f, 0.3f, 0.3f, 1f);

	// CarromNotificationStyle - notification taxonomy colors

	/// <summary>Turn-start notification — sticky cyan, distinct from the timed green/red/gold events.</summary>
	public static readonly Color NotificationTurnStart = new(0.2f, 0.9f, 0.8f, 1f);

	public static readonly Color NotificationGood = BrightGreen;
	public static readonly Color NotificationFoul = AlertRed;

	/// <summary>Queen-pocketed/covered/returned notification gold.</summary>
	public static readonly Color NotificationQueen = new(1f, 0.84f, 0f, 1f);

	public static readonly Color NotificationBreaking = Palette.Purple;

	// CarromPlayerSetupMenu

	/// <summary>Empty player slot panel fill.</summary>
	public static readonly Color SlotBgEmpty = new(DarkGray, 0.9f);

	public static readonly Color SlotBorderEmpty = BorderGray;

	/// <summary>Occupied player slot panel fill — a slight green tint to read as "filled".</summary>
	public static readonly Color SlotBgOccupied = new(0.2f, 0.3f, 0.25f, 0.9f);

	public static readonly Color SlotBorderOccupied = new(0.4f, 0.6f, 0.4f, 1f);

	/// <summary>Modal backdrop scrim behind the setup menu and credit transfer dialogs.</summary>
	public static readonly Color ModalScrim = new(0f, 0f, 0f, 0.7f);

	public static readonly Color ModalPanelBg = new(0.1f, 0.1f, 0.12f, 1f);
	public static readonly Color ModalBorder = new(0.4f, 0.4f, 0.5f, 1f);

	// Same value as SlotBorderOccupied — coincidentally the same green accent, used for a different role.
	public static readonly Color PrimaryButtonBgNormal = SlotBorderOccupied;

	public static readonly Color PrimaryButtonBorderNormal = new(0.6f, 0.8f, 0.6f, 1f);
	public static readonly Color PrimaryButtonBgHover = new(0.5f, 0.7f, 0.5f, 1f);
	public static readonly Color PrimaryButtonBorderHover = new(0.7f, 0.9f, 0.7f, 1f);

	// CarromScoreDisplay
	public static readonly Color CurrentPlayerColor = new(BrightGreen, 0.95f);
	public static readonly Color InactivePlayerColor = new(DarkGray, 0.8f);

	/// <summary>Score bar panel fill — dark blue-black to sit behind the player entries.</summary>
	public static readonly Color ScorePanelBackground = new(0.05f, 0.05f, 0.1f, 0.95f);

	/// <summary>Turn-transition / Pass-Turn button fill.</summary>
	public static readonly Color TransitionAmber = new(1f, 0.8f, 0.2f, 0.95f);

	public static readonly Color InputBlockedColor = new(AlertRed, 0.9f);
	public static readonly Color InputReadyColor = new(BrightGreen, 0.9f);

	/// <summary>Current-player row highlight, pulsed between this and a dimmer alpha by a tween.</summary>
	public static readonly Color CurrentPlayerHighlightModulate = new(BrightGreen, 0.4f);

	public static readonly Color CurrentPlayerDimModulate = new(DarkGray, 0.3f);
	public static readonly Color ScorePanelBorder = new(0.3f, 0.3f, 0.4f, 1f);

	// CarromInputController

	/// <summary>Lateral-angle threshold arc drawn around the striker during aiming.</summary>
	public static readonly Color AngleArcOverlay = new(Palette.White, 0.3f);

	public static readonly Color PowerAimingZone = new(0.2f, 0.8f, 0.2f, 0.3f);
	public static readonly Color LateralMovementZone = new(0.8f, 0.2f, 0.2f, 0.3f);

	/// <summary>Deadzone ring tint while input is blocked (distinct from the score bar's InputBlockedColor).</summary>
	public static readonly Color InputBlockedGlow = new(0.9f, 0.2f, 0.1f, 0.6f);

	public static readonly Color InputReadyGlow = new(BrightGreen, 0.3f);
	public static readonly Color ValidPositionRing = new(Palette.Green, 0.3f);
	public static readonly Color InvalidPositionRing = new(Palette.Red, 0.3f);

	// CarromPiece

	/// <summary>Player-hint ring drawn around a piece, faded in/out via HighlightColor.A.</summary>
	public static readonly Color PieceHighlight = new(1f, 1f, 0.4f, 0.4f);

	/// <summary>Motion trail colors, one per piece type — tuned lighter/brighter than each piece's rendered color for visibility.</summary>
	public static readonly Color TrailWhite = new(0.4f, 0.8f, 1f, 1f);

	public static readonly Color TrailBlack = new(0.8f, 0.4f, 1f, 1f);
	public static readonly Color TrailRed = new(1f, 0.8f, 0.2f, 1f);
	public static readonly Color TrailStriker = new(0.2f, 1f, 0.2f, 1f);

	// CarromDebugOverlay
	public static readonly Color DebugPanelBackground = new(0.1f, 0.1f, 0.15f, 0.95f);
	public static readonly Color DebugPanelBorder = new(0.3f, 0.5f, 0.7f, 1f);
	public static readonly Color DebugTitleBackground = new(0.2f, 0.3f, 0.5f, 1f);
	public static readonly Color DebugHintText = new(0.7f, 0.7f, 0.7f, 1f);
	public static readonly Color DebugStateBackground = new(0.05f, 0.05f, 0.05f, 1f);

	// CarromBoard

	/// <summary>Default board playing-surface color.</summary>
	public static readonly Color BoardWood = new(0.8f, 0.6f, 0.4f, 1f);

	public static readonly Color CollisionDebug = new(Palette.Red, 0.5f);
}
