# BarBoxApp

Godot 4.7 game platform using C# with autoload-based service architecture and context-aware game development.

## Project Structure

- `_Core/Scripts/` - Platform services (autoloads, components, gameplay base classes)
- `_Core/Scenes/` - Scene files (.tscn)
- `_Games/` - Individual game implementations
- `_Core/Scripts/_Utils/` - Utility classes (AutoloadBase, TweenConstants, LineUtils)

## Architecture

- **Service-Oriented**: AutoloadBase pattern for platform services with group-based discovery
- **Context-Aware**: Games adapt to development vs production environments
- **Template-Instance**: Resources for config, Nodes for runtime state
- **Optional Integration**: Games work standalone or with full platform
- **Unified Authentication**: Phone/Username/PIN system with SessionManager

## Game SDK Contract

Every game extends `GameController` (`_Core/Scripts/Gameplay/GameController.cs`)
and plugs into its sealed `_Ready()`/`_ExitTree()` lifecycle through the `On*`
virtual hooks (`OnDiscoverServices`, `OnInitializeComponents`,
`OnInitializeAsync`, `OnActivateGame`, `OnGameTeardown`, ...) rather than
writing its own start/stop flow. Platform services are reached through the
`Platform` property (`GameContext`) only — never `GetInstance()`/
`GetAutoload()`/raw `/root/` paths from `_Games/` (see `_Games/CLAUDE.md`).

Money and backend-result reporting go through the SDK's shared entry points
instead of each game hand-rolling them:
- `CreditService.SpendWithConfirmationAsync`/`SpendManyWithRollbackAsync`/`TransferToMachineAsync` - single spend, multi-player rollback, and machine-pot transfer shapes; games shouldn't hand-roll credit deduct/rollback logic
- `GameEventServiceBase.SubmitResultAsync` - validated, error-mapped event submission for a game's `*EventService`
- `GameController.StartBackendSessionAsync`/base `_ExitTree` - activity-session create/close, owned by the base class
- `Platform.Notifications.Show(message, severity)` - shared stacking/auto-dismissing notification overlay for player-visible status and errors
- `PlayerRoster<T>`/`PlayerRosterEntry` (`_Core/Scripts/Gameplay/PlayerRoster.cs`) - UUID-keyed multi-player roster, phone number as an attribute

See `docs/adding-a-game.md` for the full new-game checklist across both codebases.

## Architectural Tenets

1. **Phased Initialization** - Validate at phase boundaries, not runtime
2. **Sequential Execution** - Trace-friendly, avoid deferred execution
3. **Guaranteed Components** - Trust lifecycle; null checks only after `await` or in cleanup
4. **Result<T> Over Exceptions** - Exceptions only for backend I/O
5. **Configuration as Contract** - Fail fast on missing config
6. **Minimal API Surface** - Expose only what games need

## Code Style

Machine-enforced via `.editorconfig` + StyleCop/Roslynator (`stylecop.json`, packages in
`BarBox.csproj`). Regular builds skip the analyzers (`Directory.Build.props`); run them with:

```bash
RunAnalyzersDuringBuild=true dotnet format BarBox.csproj --severity warn --exclude addons/ShapesRenderer/ --verify-no-changes
```

Drop `--verify-no-changes` to auto-fix. Gated rules are `warning` severity in
`.editorconfig`; advisory ones are `suggestion`. Note: `--include`/`--exclude` paths are
cwd-relative, and fix mode exits 0 even when unfixable violations remain — use
`--verify-no-changes` to gate.

- **Indentation**: Tabs only
- **Classes**: PascalCase. **Methods/Properties**: PascalCase
- **Fields**: Public PascalCase, Private `_camelCase`
- **Organization**: Use `[ExportCategory("Section Name")]` for Inspector
- **Comments**: See Comment Philosophy below
- **Modern C#**: File-scoped namespaces, `??=`, collection expressions, required properties in new code

## Comment Philosophy

A comment earns its place only by stating what the code *cannot*: a constraint, a non-obvious rationale, or an external contract (threading, Godot lifecycle, backend API shape). Everything else is noise — write for the human maintainer reading the code, never an agent or a process artifact.

- **Delete on sight**: restatement comments, change-log narration ("now managed by", "no longer", "moved to X"), fix/process markers (`CRITICAL FIX:`, `PHASE n:`, `OPTIMIZED:`), phase-plan narration, XML docs that just restate the signature, commented-out code.
- **Keep**: constraints, timing/ordering requirements, Godot lifecycle gotchas, backend contracts, security/threading rationale — the stuff the next reader can't infer from the code itself.
- **Section markers**: no `// ====` banners. `#region`/`#endregion` only in long files where collapsibility genuinely aids navigation (see `_Games/Nines/Scripts/NinesGame.cs`); smaller files use no markers at all.

## Development Rules

- Make minimal, surgical changes for easy review
- **Colors**: no hardcoded color *literals* (`new Color(0.2f, 0.4f, ...)`, `new Color("#3b82f6")`) outside `_Core/Scripts/Drawing/Palette.cs` — add a named entry there and reference it. Constructing a `Color` from *computed* values is fine and not a violation (e.g. `OkLab.ToSrgb`, a stroke's alpha fade). Exempt: tests, the parked `addons/ShapesRenderer/`, and `.tscn`-authored colors like `default_color`/`ZoneColor`, which are authoring data
- Use `nameof` for method/variable string references
- Cache strings as consts instead of inline
- Use `TweenConstants` for animations to reduce GC allocation
- **Signals**: Only for external integration (GameHost). Direct method calls internally
- **File consolidation**: Keep simple games to a small core split (Game, Engine/State, UI, Config, EventService — see `_Games/CLAUDE.md`)
- **Nested classes** for tightly related functionality

## Error Handling

- Return `Result<T>`, not exceptions, in game logic
- Fail fast: surface errors at their source
- Visible error paths: all failure cases explicit in signatures

## Godot Essentials

- Classes must be `public partial class` for Godot integration
- Use `Object.IsInstanceValid()` ONLY in cleanup or after async boundaries
- Cache node references; use `%` prefix for unique names
- Always disconnect signals in `_Notification(NotificationExitTree)`
- `QueueFree()` for nodes, `GD.Print()` for debugging
- JSON: Call `AsGodotDictionary()`/`AsGodotArray()` directly on `Json` object. Never cast to `Variant`
- POST serialization: Always use DTO classes with `[JsonPropertyName]`, never `Dictionary<string, object>`

## Local Documentation

- **Godot 4.6 Docs**: `GodotDocs_4_6/` at repo root (prefer over web searches, gitignored). Note: docs are one minor version behind — the project is on Godot 4.7; double-check 4.7 release notes for anything version-sensitive.

## Reference Documentation

Detailed patterns and examples in `agent_docs/`:

| File | Contents |
|------|----------|
| `architectural-tenets.md` | The 6 tenets with code examples |
| `modern-csharp-patterns.md` | C# 8-12 patterns and migration guide |
| `godot-async-patterns.md` | DelayAsync, Task.Run anti-patterns, cancellation |
| `httpclient-patterns.md` | Godot HttpClient polling, connection patterns |
| `json-parsing-patterns.md` | Godot JSON vs System.Text.Json gotchas |
| `editor-plugin-handles.md` | 2D editor handle coordinate transforms |
| `box-identity-guide.md` | BoxId/BoxName/VenueName conventions |
| `autoload-service-patterns.md` | AutoloadBase, service discovery, signals |
| `backend-process-management.md` | OS.Execute vs OS.CreateProcess |
| `tween-constants-reference.md` | GC-optimized animation constants |
| `game-development-patterns.md` | Game architecture patterns and input handling |
| `plantuml-guidelines.md` | Diagram syntax reference |
