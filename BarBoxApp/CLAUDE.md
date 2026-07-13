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

## Architectural Tenets

1. **Phased Initialization** - Validate at phase boundaries, not runtime
2. **Sequential Execution** - Trace-friendly, avoid deferred execution
3. **Guaranteed Components** - Trust lifecycle; null checks only after `await` or in cleanup
4. **Result<T> Over Exceptions** - Exceptions only for backend I/O
5. **Configuration as Contract** - Fail fast on missing config
6. **Minimal API Surface** - Expose only what games need

## Code Style

- **Indentation**: Tabs only
- **Classes**: PascalCase. **Methods/Properties**: PascalCase
- **Fields**: Public PascalCase, Private `_camelCase`
- **Organization**: Use `[ExportCategory("Section Name")]` for Inspector
- **Comments**: Only where they add value beyond code. Explain "why" not "what". Write for the **human maintainer reading the code** — never address an agent or reference process/review artifacts (architectural tenets, audit/plan stage numbers, "introduced by X migration"). That context belongs in instruction files (CLAUDE.md), plan docs, or commit messages, not code comments.
- **Modern C#**: File-scoped namespaces, `??=`, collection expressions, required properties in new code

## Development Rules

- Make minimal, surgical changes for easy review
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
