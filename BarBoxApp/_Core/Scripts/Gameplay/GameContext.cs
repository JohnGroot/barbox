using BarBox.Core.Autoloads;
using Godot;
using System;

namespace BarBox.Core.Gameplay;

/// <summary>
/// Provides access to platform services. Constructed once in GameController._Ready().
/// Games access via the protected Platform property.
/// </summary>
public class GameContext
{
	// Core Services (always available after ApplicationBootstrap)
	public SessionManager Session { get; private init; }
	public EventService Events { get; private init; }
	public LocationManager Location { get; private init; }
	public CreditService Credits { get; private init; }

	// Platform Services
	public GameHost Host { get; private init; }
	public UIManager UI { get; private init; }
	public InputManager Input { get; private init; }
	public GameRegistry Registry { get; private init; }

	// Context Information (cached via BuildContext)
	public bool IsProduction => BuildContext.IsProduction;
	public bool IsDevelopment => BuildContext.IsDevelopment;
	public bool ShouldBypassCredits => BuildContext.ShouldBypassCredits;

	// Location Information (delegated to LocationManager)
	public string VenueName => Location?.VenueName;
	public string BoxName => Location?.BoxName;
	public Guid BoxId => Location?.BoxId ?? Guid.Empty;

	// Convenience Properties
	public UserSession PrimaryUser => Session?.GetPrimarySession();
	public bool HasActiveUser => PrimaryUser != null;

	private GameContext() { }

	/// <summary>
	/// Creates GameContext by discovering all autoload services.
	/// Called once during GameController initialization.
	/// </summary>
	public static GameContext CreateFromAutoloads()
	{
		return new GameContext
		{
			Session = AutoloadBase.GetAutoload<SessionManager>(),
			Events = AutoloadBase.GetAutoload<EventService>(),
			Location = AutoloadBase.GetAutoload<LocationManager>(),
			Credits = AutoloadBase.GetAutoload<CreditService>(),
			Host = AutoloadBase.GetAutoload<GameHost>(),
			UI = AutoloadBase.GetAutoload<UIManager>(),
			Input = AutoloadBase.GetAutoload<InputManager>(),
			Registry = AutoloadBase.GetAutoload<GameRegistry>()
		};
	}
}
