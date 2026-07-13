# Adding a Game — Checklist

A new game touches both codebases. This is the end-to-end checklist; each
step links to the detailed guide that owns it.

## 1. Pick one identity

Decide the game's id up front — it is used **unchanged** as: the
`GameRegistry` id, the `GameController.GetGameId()` return value, and the
backend `game_tag` / `GAMES` dict key (e.g. `"racing"`, not `"racing_game"`
with a separate `"racing"` tag). One string, one meaning, everywhere.

## 2. App side (`BarBoxApp/`)

1. Create `_Games/YourGame/` following the pattern that fits — Consolidation
   for simple/idle games, Multi-Component for complex systems, Physics-Heavy
   for real-time — see `_Games/CLAUDE.md`.
2. Register the game in `GameRegistry.LoadGameConfigurations()`
   (`_Core/Scripts/Autoloads/GameRegistry.cs`): `GameId` = the id from step 1,
   `ScenePath` pointing at your root scene. The registry asserts at boot that
   the scene path resolves — a typo here fails immediately, not the first
   time someone opens the game.
3. Your root scene's script extends `GameController`
   (`_Core/Scripts/Gameplay/GameController.cs`) and implements
   `GetGameId()`. Plug into the sealed lifecycle via the `On*` hooks
   (`OnDiscoverServices`, `OnInitializeComponents`, `OnInitializeAsync`,
   `OnActivateGame`, `OnUserLoggedIn`/`OnUserLoggedOut`, `OnGameTeardown`) —
   don't write your own start/stop flow. See the Game SDK Contract section
   in `BarBoxApp/CLAUDE.md`.
4. Reach platform services exclusively through `Platform` (`Platform.Events`,
   `.Credits`, `.Session`, `.Host`, `.UI`, `.Input`, `.Location`,
   `.Registry`) — never `GetInstance()`/`GetAutoload()`/raw `/root/` paths
   from `_Games/` code.
5. For gameplay backend sessions, call the base class's
   `StartBackendSessionAsync(boxId, playerId, playerIds)` when play begins —
   it stores the session id and closes it automatically on teardown, so games
   don't duplicate that bookkeeping.
6. For per-event submission, create a `YourGameEventService : GameEventServiceBase`
   and call the inherited `SubmitResultAsync(eventType, payload)` — it
   handles session/validity checks and backend-error mapping once, for every
   game.
7. For credit spends, prefer `CreditService.SpendWithConfirmationAsync` for
   the common single-spend-with-confirmation shape. Multi-player
   rollback (Nines) and spend-then-deposit (Carrom) shapes aren't covered by
   a shared helper yet — see the roadmap's WS3 for that work.
8. Create a test suite under `_Games/YourGame/Tests/` (GoDotTest).

## 3. Backend side (`BarBoxServices/`)

Follow `BarBoxServices/agent_docs/game-module-guide.md` step-by-step
(`schemas.py` with the canonical `EventType` alias, `service.py`,
`router.py`, register in `GAMES`, extend the `SessionEventType` union,
classify every event in `games/validation.py`). Two boot-time guards now
catch the classic missed edits immediately instead of at first use:
- `_check_payload_model_coverage()` (`games/validation.py`) — every event
  type needs a payload model or an explicit no-payload allowlist entry.
- `_check_session_event_type_coverage()` (`structures.py`) — every
  registered game's `EventType` must be reachable through `SessionEventType`.

## 4. Verify end-to-end

```bash
cd BarBoxApp && dotnet build && bash scripts/run-tests.sh
cd BarBoxServices && sh scripts/dev.sh   # separate terminal
cd BarBoxServices && sh scripts/test.sh
```

Add a Hurl test under `BarBoxServices/test/02-feature/yourgame/` (picked up
automatically by `scripts/test.sh`).
