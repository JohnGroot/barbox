# BarBox Architecture Roadmap

> **Purpose:** The forward-looking plan for evolving BarBox toward its north
> star: *a competent developer adds game #5 in a day* by copying the Mining
> reference, touching one registry line per side, and following one checklist —
> with the compiler and boot-time guards catching every forgettable step.
>
> **Created:** 2026-07-13, from a deep architecture audit (3 parallel
> reconnaissance passes: platform core, all four games, backend) after the
> completion of the earlier audit's Stages 1/2/4 and part of Stage 5
> (see Appendix C). Supersedes the untracked
> `docs/codebase-audit-and-simplification-plan.md` as the active plan.
>
> **Scope:** Both `BarBoxApp/` (Godot 4.7 C#) and `BarBoxServices/` (FastAPI).
> Pre-production — churn is acceptable where it buys real simplification.

## §0 — How to Use This Doc

For a fresh session picking up a workstream:

1. Read §1 (target architecture) and your target workstream in §3. Skim §2 for
   dependency gates. You do not need the appendices unless your workstream
   points at them.
2. **Re-verify every line anchor before editing** — this doc's line numbers
   were correct on 2026-07-13 and WILL drift. In particular, any `web/*.py`
   path predates the `services-refactor` branch's move to feature packages
   (`src/bxctl/{boxes,players,credits,payments,testing,games}/`, each with
   its own `CLAUDE.md`) — treat those paths as pointers to the old module,
   not literal locations.
3. One logical change per commit (conventional format, no agent signatures).
   Build + full test suites green before every commit.
4. Update the status column in §2 (and add a dated note under the workstream
   in §3) at the end of each session, so the next session knows where things
   stand.

Work lands on the `codebase-audit-simplification-plan` branch (or a
per-workstream branch off it) until ready to merge.

**Verification commands** (run after every increment, not just at the end —
the C# suite hits the real backend, so transport/contract regressions surface
immediately):

```bash
# App: build + full suite (291 C# tests as of 2026-07-13; includes a backend Hurl pass)
cd BarBoxApp && dotnet build && bash scripts/run-tests.sh

# Backend: Hurl integration suite (24 files as of 2026-07-13)
cd BarBoxServices && sh scripts/test.sh
```

## §1 — Target Architecture

Three layers, top-down. The goal is that a game author only ever learns
layer 2.

**Layer 1 — Platform** (`_Core/Scripts/Autoloads/`): infrastructure services.
`BackendClient` (new — sole HTTP transport, from WS1), `SessionEventService`
(renamed `EventService` — activity/lobby sessions + event emit, from WS1),
`CreditService`, `SessionManager`, `LocationManager`, `BackendManager`,
`GameRegistry`, `GameHost`, `SceneManager`, `UIManager`, `InputManager`,
`PaymentService`/`StripePaymentService`, orchestrated by
`ApplicationBootstrap`. Games never touch these directly — only through
layer 2.

**Layer 2 — Game SDK** (the contract a game consumes):

- `GameController` (`_Core/Scripts/Gameplay/GameController.cs`) — sealed
  lifecycle base. Owns backend session create/close
  (`StartBackendSessionAsync` + auto-close), login/logout wiring, pause/
  resume, return-to-menu. A game implements `GetGameId()` and overrides the
  phase hooks (`OnDiscoverServices` → `OnInitializeComponents` →
  `OnInitializeAsync` → `OnActivateGame`; `OnGameTeardown` for cleanup).
- `GameContext` / `Platform.X` — **the only service-discovery idiom**
  (`Platform.Session/Events/Credits/Location/Host/UI/Input/Registry`). No
  `GetInstance()`, no `GetAutoload()`, no raw `GetNode("/root/…")` in
  `_Games/` (enforced by convention + WS2 cleanup).
- `GameEventServiceBase` — every game gets a thin `*EventService` subclass;
  results go through `SubmitResultAsync`, queries through
  `QueryBackendAsync<T>`.
- `CreditService` primitives — ALL money flows: single spend
  (`SpendWithConfirmationAsync`), multi-player spend with rollback, and
  machine-pot transfer (the latter two added in WS3).
- `NotificationService` (`Platform.Notifications`), `PlayerRoster`,
  `GameSessionTestBase` — added in WS3.

**Layer 3 — Games** (`_Games/*`): free-form internally. Mining is the
reference implementation (Game / Engine / State / UI / Config / EventService
core split). The only hard rules: subclass `GameController`, discover via
`Platform.X`, money only through `CreditService` primitives, results only
through your `GameEventServiceBase` subclass.

**Tenets** (carried from the repo's architectural tenets, applied to this
roadmap):

- Composition over inheritance below `GameController` — no deeper base-class
  hierarchies; shared behavior ships as components/services games *use*.
- **Rule of three** before any new shared abstraction: extract when the third
  consumer appears (or when the duplicated code is money-handling — then two
  is enough).
- Signals for cross-scene/UI integration only; direct method calls within a
  system. No internal signal chains.
- Hot paths allocation-free (the GC techniques already applied to Racing are
  the standard).

## §2 — Prioritized Roadmap

| # | Workstream | Depends on | Risk | Status |
|---|-----------|------------|------|--------|
| WS0 | Security remediation (operational, out-of-band) | — | med (keys rotated) | **DONE (2026-07-15)** — all code-level items resolved, see Appendix B |
| WS1 | EventService split (5 increments, spec in Appendix A) | — | High (inc ①), low after | **DONE (2026-07-13)** — all 5 increments, see §3 notes |
| WS2 | New-game DX: one identity, one discovery idiom, registry hygiene, drift guards, docs | WS1 (rename settles names) | Low | **DONE (2026-07-13)** — all 5 items, see §3 notes |
| WS3 | Game SDK v1: credit confirmation UI + credit shapes, ToastService, PlayerRoster, GameTestFixture | WS1 inc ② for the credit items | Medium | **DONE (2026-07-13)** — all 4 items, see §3 notes |
| WS4 | Backend dedup (leaderboard SQL, auth deps, payments service layer, error envelope, ApiPaths) | none — parallel-safe with WS1–3 | Medium | **DONE (2026-07-14)** — all 6 items, see §3 notes |
| WS5 | Backend infra: machine-credits TOCTOU fix, Alembic, dead deps, formatting | none; TOCTOU fix may be pulled forward anytime | Low/Med | **DONE (2026-07-14)** — all 4 items, see §3 notes |
| WS6 | Deferred until a second consumer appears: leaderboard widget, countdown, lobby, `_Games/_Template` | trigger-based | — | Intentionally deferred |

**Sequencing rationale:**

- **WS1 first.** Every WS3 credit primitive lands in `CreditService` over
  `BackendClient`. Building them earlier means writing them against
  `EventService.SpendCreditsAsync`/`DepositMachineCreditsAsync` and
  re-plumbing them after the split. Hard gate: WS3's machine-pot transfer
  work waits for WS1 increment ② (credits moving out of EventService,
  including Carrom's machine-credit call sites).
- **WS2 after WS1.** The identity unification and the
  `EventService → SessionEventService` rename both churn autoload names and
  call sites; do that churn once, in order, not interleaved.
- **WS4/WS5 are independent** of all app work — interleave whenever backend
  focus is available. The TOCTOU fix in WS5 is a money bug, not a refactor;
  pull it forward at will.
- **God-object decomposition is deliberately NOT a workstream.**
  `CarromGame.cs` (2479), `RacingGame.cs` (2420), `CarromPlayerSetupMenu.cs`
  (1744), `NinesGame.cs` (1526) are ugly but working and test-covered.
  Splitting them helps those games, not future ones. They shrink as a side
  effect of WS3 (notifications, roster, and credit code move out to the SDK).

## §3 — Workstream Specs

> All line anchors verified 2026-07-13 — re-confirm at execution time.

### WS1 — EventService split

`EventService` (`_Core/Scripts/Autoloads/EventService.cs`, 1527 lines) is one
autoload doing two unrelated jobs: HTTP transport and every domain API
(sessions/emit, credits, auth, box registration, Stripe, machine credits),
with `CreditService`/`SessionManager`/`StripePaymentService` wrapping its
methods in a double-hop.

The full execution spec — target shape, the auth-coupling seam
(`JwtTokenProvider` delegate breaking the SessionManager↔EventService cycle),
the rename analysis, five verified increments, and cross-cutting risks — is
carried verbatim in **Appendix A**. Execute it as written: increment ①
(BackendClient transport autoload) is the risky one; ②–⑤ (credits → 
CreditService, Stripe → StripePaymentService, auth → SessionManager/
LocationManager, rename to `SessionEventService`) are mechanical afterwards.
Build + full C# suite + Hurl after every increment.

**2026-07-13 — Increment ① DONE.** `BackendClient.cs` created as the sole
HTTP transport autoload (registered in `project.godot` between
`BackendManager` and `EventService`); `EventService` slimmed to session/emit/
identity, calling through `_backend` for everything else. `JwtTokenProvider`
delegate wired from `SessionManager.OnServiceInitialize`.
`ApplicationBootstrap` Phase 2 now initializes `BackendClient` before
`EventService`. Build + full C# suite (291/291) + Hurl green. Two things not
in the original Appendix A spec, discovered during execution and worth
knowing before touching this code again:
- `CreateActivitySessionAsync`/`CloseActivitySessionAsync`/
  `CreateLobbySessionAsync` stayed on `EventService` per the increment ①
  scope, but they use Godot's `HttpClient` directly (not the generic verbs) —
  `BackendClient` exposes a small set of `internal` forwarding members
  (`Request`, `Poll`, `GetStatus`, `GetResponseCode`,
  `ReadResponseBodyChunk/Async`, `Close`, plus `internal`
  `EnsureConnectedAsync`/`WaitForResponseAsync`/`BuildHeaders`/
  `BuildPlayerHeaders`) so these three methods can keep working unchanged.
  Increment ②–④ should fold these into whichever service ends up owning
  session lifecycle rather than growing this internal surface further.
- `EmitUserEventAsync`'s `playerId` parameter was already unused pre-split
  (verified at the old `BuildHeaders()` call site — no JWT was ever attached
  to user events) — this was preserved as-is, not "fixed," since fixing it
  would be an undocumented behavior change riding along in a pure-transport
  refactor.

**2026-07-13 — Increment ② DONE.** `GetPlayerCreditsAsync`,
`AddCreditsAsync`/`SpendCreditsAsync`, and the three `*MachineCreditsAsync`
methods moved out of `EventService` into `CreditService`. `CreditService` now
depends on `BackendClient` directly for player-credit queries and machine
credits (new `EnsureBackendClientReadyAsync` retry helper, mirroring the
existing `EnsureEventServiceReadyAsync` one), and still depends on
`EventService.EmitUserEventAsync` for the two credit-earn/spend session
events (that primitive stays owned by EventService/session-emit, per Target
Architecture — CreditService now calls it directly instead of through the
deleted `AddCreditsAsync`/`SpendCreditsAsync` double-hop). `GetMachineCreditsAsync`/
`DepositMachineCreditsAsync`/`ConsumeMachineCreditsAsync` are new public
`CreditService` methods (they weren't public there before — only `EventService`
exposed them). Carrom's `CarromPlayerSetupMenu.cs` (the only direct game
caller, per Appendix A) updated to call `CreditService` instead of
`EventService` for all three; its now-dead `_eventService` field was removed
(zero remaining call sites in that file). Build + full C# suite (291/291) +
Hurl green — required updating several backend integration tests
(`CarromMachineCreditsIntegrationTests`, `PlayerRegistrationTests`,
`CreditPurchaseFlowTests`, `EventServiceTests`) that called the now-removed
`EventService` credit methods directly; all now go through `CreditService`.

**2026-07-13 — Increment ③ DONE.** `CreateCheckoutSessionAsync`/
`GetCheckoutStatusAsync` moved from `EventService` into `StripePaymentService`
(private methods there now, since it's the sole caller — not part of any
shared domain API). `StripePaymentService` now holds a `BackendClient`
reference instead of `EventService`, calling `_backend.PostAsync`/`QueryAsync`
directly; its `IsEventServiceValid`/`_eventService` renamed to
`IsBackendClientValid`/`_backend` throughout. No other caller existed (grep
confirmed) and no test referenced these two methods directly, so no test
updates were needed this increment. Build + full C# suite (291/291) + Hurl
green.

**2026-07-13 — Increment ④ DONE.** `IsUsernameAvailableAsync`/
`ValidatePlayerCreationAsync`/`CreatePlayerAsync` moved into `SessionManager`
(as private `QueryUsernameAvailableAsync`/`ValidatePlayerCreationBackendAsync`/
`CreatePlayerBackendAsync` over a new `_backend` field, called from
`SessionManager`'s existing same-named public wrappers — those wrappers'
validation/cleaning logic and signatures are unchanged). `RegisterBoxWithDetailAsync`/
`RegisterBoxAsync` moved into `LocationManager` (its only caller,
`VerifyBoxRegistrationDeferred`, updated to check `BackendClient` readiness
instead of `EventService`). `GetPlayerIdFromPhone` moved to a genuine static
method on `SessionManager` (it already just delegated to
`SessionManager.GetSessionByPhone` before this) — all 22 call sites across
the app and test suite renamed from `EventService.GetPlayerIdFromPhone` to
`SessionManager.GetPlayerIdFromPhone`. `EventService.PostAsync`/`QueryAsync`
pass-throughs (session/emit domain, login/logout) are unaffected — they
weren't in this increment's move list. Build + full C# suite (291/291,
unchanged pass count — no test called the six moved methods' `EventService`
form directly) + Hurl green.

**2026-07-13 — Increment ⑤ DONE.** `EventService` renamed to
`SessionEventService` throughout: class name (file renamed
`EventService.cs` → `SessionEventService.cs`, including its `.uid` sidecar),
the `project.godot` autoload node, all `GetInstance()` callers, the 7 test
node-paths (`GetNode<EventService>("/root/EventService")` →
`GetNode<SessionEventService>("/root/SessionEventService")` in
`BackendTestBase`, `TestHelpers`, and the four `*GameTests` files),
`GameEventServiceBase`'s field type, and the 4 game event-service ctors.
Per the churn-minimizer, `GameContext.Events`'s property *name* is
unchanged — only its type became `SessionEventService`. Applied via a
word-boundary rename (`\bEventService\b`) so per-game event-service classes
(`MiningEventService`, `CarromEventService`, etc., and
`GameEventServiceBase`) were correctly left untouched, as were lowercase
`_eventService` field/variable names and test helper method names like
`SimulateEventServiceNotReady` (out of this increment's scope). Docs
naming the class updated too (§6). Build + full C# suite (291/291) + Hurl
green.

**WS1 status: all 5 increments DONE.** WS2 is now unblocked (its
sequencing note "after WS1 (rename settles names)" is satisfied).

### WS2 — New-game developer experience

Small items, each its own commit. Do 1–4 after WS1 (item 1 and WS1's rename
touch the same call-site surface); item 5 (docs) strictly last.

1. ~~**One identity per game.**~~ **DONE (2026-07-13)** — registry ids
   renamed to backend tags (`mining_game`→`mining`, `racing_game`→`racing`,
   `carrom_game`→`carrom`, `nines_game`→`nines`) in `GameRegistry.cs`, the two
   `.tscn` scene metadata copies (`CarromGame.tscn`, `MiningGame.tscn`), and
   the four `GetGameId()` overrides. `GetGameTag()` deleted from
   `GameController.cs` and all four per-game overrides;
   `StartBackendSessionAsync` now passes `GetGameId()` directly. No menu-
   wiring or test references to the old `_game`-suffixed ids existed. Full
   C# + Hurl suite green (291 passed).
2. ~~**One discovery idiom.**~~ **DONE (2026-07-13)** — converted Racing,
   Carrom, and Nines off `GetInstance()`/`GetAutoload()`/raw `/root/` paths
   onto `Platform.X` (`Session`, `Events`, `Credits`, `Host`, `UI`, `Input`,
   `Location`). Also found and converted call sites the roadmap's line
   anchors didn't list (they'd drifted): `RacingGame.cs` UIManager/
   SessionManager lookups outside the discovery block, `CarromGame.cs`'s
   context-button closures and a second `LocationManager.GetAutoload()` at
   the multiplayer-session start. Nines' `IsProductionContext()` helper
   (wrapping a raw `/root/GameHost` lookup) was dead code — deleted rather
   than converted, since `Platform.IsProduction` already covers that need.
   Added the "`Platform.X` only" rule to `BarBoxApp/_Games/CLAUDE.md` and
   fixed its own `Context Detection` example, which still showed
   `GameHost.GetInstance()`. Left untouched: non-`GameController` helper/UI
   classes (`RacingUIManager.cs`, `CarromCompetitiveModeManager.cs`,
   `CarromPlayerSetupMenu.cs`, `MiningGame.cs`/`MiningState.cs`, `NinesUI.cs`)
   — they have no `Platform` property to call through, and the roadmap's item
   scope named only the three `GameController` subclasses. Test files'
   `/root/SessionEventService` node-path lookups are test-fixture plumbing,
   not game code, and also out of scope. Full C# + Hurl suite green (291
   passed).
3. ~~**Registry hygiene — not JSON.**~~ **DONE (2026-07-13)** — deleted the
   stale "Load from _Data/GameRegistry.json" comment. `GameRegistry.RegisterGame`
   now throws on a duplicate id instead of logging a warning and silently
   overwriting; a new `ValidateGameConfigurations()` runs in
   `OnServiceEnterTree` right after registration and throws if any game's
   `ScenePath` doesn't resolve (`ResourceLoader.Exists`), so a bad scene path
   fails at boot instead of the next time that game is opened.
   `GameHost.LoadGameOverlay`'s unknown-id branch now throws
   `InvalidOperationException` instead of logging and returning — with the
   boot-time check in place, an unregistered id reaching this method is a
   caller bug (e.g. a typo in menu wiring), not a data condition to degrade
   gracefully around. The lone real caller (`MainController.OnGameSelected`)
   already re-validates via `GetGameData` before calling, so this only
   changes behavior for a genuine bug. Full C# + Hurl suite green (291
   passed).
4. ~~**Backend drift guards.**~~ **DONE (2026-07-13)** — added
   `_check_session_event_type_coverage()` in `structures.py`, called at
   import time right after the `SessionEventType` union is defined. It
   flattens the union's `Literal` members (PEP 695 `type` aliases need
   `get_args(SessionEventType.__value__)`, not `get_args(SessionEventType)`
   directly, since the alias itself is a `TypeAliasType`) and checks every
   `GAMES[name]["schemas"].EventType` value is covered; raises `RuntimeError`
   naming the missing event(s) otherwise. Verified it actually fires by
   temporarily dropping a game from the union and confirming the import
   fails with the expected message. Full C# + Hurl suite green (291 C# +
   24/24 Hurl).
5. ~~**Docs pass.**~~ **DONE (2026-07-13)** — rewrote the pre-GameController
   guidance:
   - `BarBoxApp/_Games/CLAUDE.md` — "Game Lifecycle Rules" and "Context
     Detection" now teach the `On*` phase hooks and `Platform.X`, not
     `_gameActive`/`CallDeferred(StartGame)`/`GetInstance()` probing.
   - `BarBoxApp/CLAUDE.md` — added a "Game SDK Contract" section naming
     `GameController`, its `On*` hooks, `SubmitResultAsync`,
     `SpendWithConfirmationAsync`, and `StartBackendSessionAsync`.
   - `README.md` "Adding a New Game" — replaced the stale direct
     `CreateActivitySessionAsync`/`CloseActivitySessionAsync` example with a
     pointer to the new checklist doc.
   - `BarBoxApp/agent_docs/game-development-patterns.md` — rewrote "Game
     Lifecycle" and "Context-Aware Design" around the `On*` hooks and
     `Platform.X`; fixed a stale `InputManager.GetAutoload()` reference.
   - `BarBoxApp/agent_docs/architectural-tenets.md` — Tenet 1's example used
     pre-rename method names (`DiscoverServices`/`InitializeComponents` with
     `base.X()` calls) and `GameHost.IsProductionContext()`; updated to
     `OnDiscoverServices`/`OnInitializeComponents`/`Platform.Events`. Tenets 3
     and 5 had the same stale method names in their tables/examples.
   - `BarBoxApp/agent_docs/autoload-service-patterns.md` — reviewed; no
     changes. Its `GetInstance()`/`GetAutoload()` examples are for
     autoload-to-autoload calls, which the WS2 item 2 rule doesn't touch
     (only `_Games/` code goes through `Platform.X`).
   - `BarBoxServices/agent_docs/game-module-guide.md` — updated step 6's note
     to mention the new boot-time `_check_session_event_type_coverage()`
     guard from item 4, not just the HTTP 422 fallback.
   - Wrote **`docs/adding-a-game.md`**: end-to-end checklist across both
     codebases, referenced from `README.md` and `BarBoxApp/CLAUDE.md`.
   - Found but left out of scope (not named in this item, flagged in §6
     instead): `docs/architecture/sessions.md` still shows games hand-rolling
     `CreateActivitySessionAsync`/`CloseActivitySessionAsync` directly,
     predating `GameController.StartBackendSessionAsync`.

### WS3 — Game SDK v1

Priority order within the workstream. Money first.

1. ~~**Credit correctness** *(gated on WS1 ② for the machine-pot item)*~~
   **DONE (2026-07-13)**:
   - **Real spend confirmation.** `CreditConfirmationHelper.ShowCreditConfirmationAsync`
     now routes through `UIManager.ShowConfirmationAsync` with amount + balance
     shown; the development auto-confirm bypass is derived from
     `BuildContext.IsExportedBuild`, so it cannot be true in a production build.
   - **`CreditService.SpendManyWithRollbackAsync(players, amount, reason)`**
     added — takes `(PlayerId, Label)` pairs so per-player failure messages
     keep a display name. Nines' `DeductCreditsWithRollbackAsync` now resolves
     sessions up front and delegates to it; `RollbackCreditsAsync` deleted.
   - **`CreditService.TransferToMachineAsync(gameTag, boxId, playerId, amount, lobbySessionId, spendReason)`**
     added — spends from the player then deposits to the machine pot,
     refunding the player if the deposit fails. Carrom's
     `OnTransferCreditButtonPressed` migrated onto it (previously logged and
     continued on deposit failure, leaving the local table-credit count out of
     sync with the backend with no refund).
   - **Mining deposit**: `MiningState.PurchaseCredit` (now `PurchaseCreditAsync`,
     returns `CreditPurchaseResult`) awaits `AddAsync`, refunds the spent gems
     and surfaces failure via `MiningGameUI.ShowError` if the deposit fails.
   - Verified via the full C# suite (291 tests, including `CreditServiceTests`,
     `CarromMachineCreditsIntegrationTests`, `NinesGameTests`) + Hurl credit
     suites after each of the four sub-items, each landed as its own commit.
2. ~~**ToastService.**~~ **DONE (2026-07-13)** — named `NotificationService`
   (user preferred "notification" over "toast"), exposed as
   `Platform.Notifications`:
   - `_Core/Scripts/Autoloads/NotificationService.cs` promotes Carrom's
     stacking/timed/sticky/fade-animation machinery (previously
     `CarromNotificationSystem`, 555 lines) to a shared autoload. Generic
     `Show(message, severity, color?, sticky?, duration?)` API — severity
     (Info/Success/Warning/Error) supplies defaults, callers can override any
     of them.
   - Carrom migrated: its game-specific notification taxonomy (turn start,
     foul, queen events, breaking attempts) stays local as
     `CarromNotificationStyle`, mapping onto the platform service's generic
     params. `CarromNotificationSystem.cs` deleted.
   - Nines' `NinesUI.ShowError` previously only `GD.PrintErr`'d — never
     actually reached the player. Now routes through
     `Platform.Notifications`.
   - Mining's `MiningGameUI.ShowError` previously built its own full-screen
     blocking overlay with a dismiss button; now routes through
     `Platform.Notifications` (a deliberate UX change: blocking modal →
     non-blocking notification).
   - Racing had no notification mechanism at all — every failure only ever
     hit `GD.PrintErr`. Added `Platform.Notifications` calls at the
     genuinely user-facing failure points (race save failure, time
     trial/race-again cancellation, missing session, credit purchase
     unavailable); internal/config-time diagnostics (track metadata,
     retry-loop logging) deliberately left console-only. Racing's leaderboard
     display (`ShowGlobalHighScores`) was found to only ever `GD.Print` to
     the console on both success and failure paths — a separate, larger,
     pre-existing gap, left out of this item's scope.
3. ~~**PlayerRoster.**~~ **DONE for Nines (2026-07-13)**, Carrom/Mining/Racing
   found not to need the same migration:
   - Added `_Core/Scripts/Gameplay/PlayerRoster.cs` —
     `PlayerRosterEntry` (abstract base: `PlayerId` (Guid, the key),
     `PhoneNumber`, `DisplayName`, `SlotIndex`, `IsLoggedIn`) and
     `PlayerRoster<T>` (Add/Remove/Find-by-id/Find-by-phone). UUID-keyed,
     phone number is an attribute, not the key.
   - Nines migrated: `NinesPlayer` now extends `PlayerRosterEntry` (keeping
     only its own `Credits`/prediction-analytics fields); `NinesState.Players`
     is backed by a `PlayerRoster<NinesPlayer>`. Identity checks
     (`OnUserLoggedIn`) now key on `session.PlayerId` instead of phone number.
     ⚠️ Preserved: `nines/jackpot_won` still sends the winner's **phone
     number** as `player_id` (`RecordJackpotWinAsync` reads `.PhoneNumber`,
     untouched) — the backend leaderboard join depends on it
     (`games/nines/service.py` joins `p.phone_number`).
   - **Carrom found NOT to fit this shape**: its competitive-mode "roster"
     (`CarromCompetitiveModeManager.GetPlayers()`) keys players by fixed
     `"player1"`/`"player2"` turn-display labels, not phone/UUID — session
     identity is resolved separately, only at credit-spend time, via
     `SessionManager.GetSessionByPhone`. The `CarromGame.cs:1458-1465`
     `_playerMgmt`/`CarromPlayer` roster cited in the original spec turned out
     to be vestigial — a single dev-mode "default" player, unused everywhere
     else (`_players` field is dead code, never read). Migrating Carrom's
     *actual* multiplayer identity model onto `PlayerRoster<T>` would mean
     rewriting how competitive mode assigns identity to turns/scoring/UI
     display across `CarromCompetitiveModeManager.cs`, `CarromScoreDisplay.cs`,
     and `CarromPlayerSetupMenu.cs` — a much larger, higher-risk refactor of
     already well-tested gameplay logic, not a drop-in roster swap. Deferred;
     worth its own scoped pass if picked up later.
   - **Mining has no roster to extract** — genuinely single-primary-user,
     reads `SessionManager.GetPrimarySession()` directly wherever needed
     (`MiningGame.cs:537-540`). Confirmed via code read, no action needed.
   - **Racing confirmed stateless** per the original spec — `_playerMgmt`
     is the already-shared `_Core.PlayerManagementComponent`, but it tracks
     physical in-race car/player entities (string-keyed `BasePlayer`), not
     login/credit identity. Different concept from the roster this item
     targets; nothing to migrate.
4. ~~**GameTestFixture.**~~ **DONE (2026-07-13)**: Mining/Nines/Racing's
   `*GameTests` files didn't even extend `BackendTestBase` — each hand-rolled
   an identical `[SetupAll]`/`[CleanupAll]` (health check, get
   `SessionEventService`, create an activity session against the seeded test
   box/player, close it after the suite). Added
   `Tests/Fixtures/GameSessionTestBase.cs` (`: BackendTestBase`) to own that
   once; each of the three now just overrides `GameTag` — no
   `[SetupAll]`/`[CleanupAll]` of its own. Carrom's session is multiplayer and
   created per-test (not once upfront), so it doesn't fit this shape and
   stays on `BackendTestBase` directly, unchanged.

### WS4 — Backend dedup

**DONE (2026-07-14)** — all 6 items:

1. ~~**Leaderboard SQL builder** in `games/common.py`. The
   `box_session_event ⋈ box_session … json_extract … LEFT JOIN player`
   skeleton is duplicated ~12×: `racing/service.py:40,74,94`,
   `carrom/service.py:31,81`, `nines/service.py:30`,
   `mining/service.py:39,50,61,129,189,242`, plus `web/player.py:483`,
   `web/machine_credits.py:41,67`. The copies genuinely differ in **join-key
   strategy** (UUID vs de-hyphenated UUID vs phone_number) — parameterize the
   builder by that strategy rather than forcing one shape.~~ **DONE
   2026-07-14.** Added `games.common.uuid_join_leaderboard_sql()` for the
   raw-UUID join-key strategy and rewrote Racing's 3 near-identical queries
   (best_lap, best_race with/without a laps filter) to use it — those were
   genuine copies differing only by event type, the aggregated JSON field,
   and an optional extra WHERE clause. Carrom (de-hyphenated UUID
   reformatting, two directions), Nines (phone_number join), and Mining
   (no player join at all — pure per-player aggregation) use genuinely
   different join-key strategies per the investigation and were **not**
   forced into this shape; left as-is.
2. ~~**Signed-credit SUM helper.** The `SUM(CASE WHEN type='…earn' THEN + …
   'spend' THEN -)` shape is duplicated at `web/player.py:486` and
   `web/machine_credits.py:44` (different event names, same skeleton).~~
   **DONE 2026-07-14.** Added `games.common.signed_sum_sql()`; both callers
   now build their SUM/CASE fragment from it, keeping their own
   FROM/JOIN/WHERE (which genuinely differ: player-scoped vs
   box+game-scoped).
3. ~~**Collapse the 3 box-auth dependencies.**
   `web/dependencies.py:42/116/182` are three copies of
   header→`verify_box_api_key`→fetch→404. Parameterize into one.~~ **DONE
   2026-07-14.** Extracted `_verify_box_api_key_header()` (shared 401
   checks, used by all 3) and `_fetch_box_or_404()` (shared fetch+404, used
   by the path- and header-based flavors — the session-scoped flavor's
   missing-box case is a 500 data-integrity error, not a registration gap,
   and correctly stays separate).
4. ~~**Extract `payments/service.py`.** `web/payments.py` (895 lines) holds
   business logic in the router (`_issue_credits_for_payment:99`,
   `get_or_create_credit_session:251`, admin reconciliation `:726+`). Split
   to match the games-module service/router convention.~~ **DONE
   2026-07-14.** `web/payments.py` → `web/payments/{service,router}.py`.
   `service.py` holds `issue_credits_for_payment`,
   `get_or_create_credit_session`, `build_reconciliation_report`,
   `retry_credit_issuance`, `CREDIT_PACKS`/`get_stripe_client`/
   `get_stripe_price_id`; `router.py` keeps checkout creation and webhook
   handling as endpoint-level Stripe-SDK glue (deliberately not extracted
   further — that orchestration isn't a reusable business rule). `main.py`
   and `test.py` updated for the new import shape.
5. ~~**Standardize the error envelope** on `structures.ErrorDetail`
   (`structures.py:~208`); today some handlers return structured
   `{code,message,details}`, others bare `detail="string"`
   (`dependencies.py:70,96,146`, `machine_credits.py:240`), and payments adds
   an ad-hoc `retryable` field — fold it into the model or drop it.~~ **DONE
   2026-07-14.** Added `ErrorDetail.retryable`; box-auth 401s and the
   machine-credits insufficient-balance 400 now use the `{code, message}`
   dict shape; payments' invented string codes and the new
   `INSUFFICIENT_CREDITS`/`UNAUTHORIZED` codes are now `ErrorCode` members.
   The Stripe webhook's internal signature/payload 400s were left as bare
   strings — those go to Stripe's retry logic, not the app, and weren't
   part of this item's citation.
6. ~~**App-side `ApiPaths` constants class.** Backend paths are scattered
   string literals across `EventService.cs`, `SessionManager.cs`, the game
   event services, and `NinesGame.cs`. One constants class makes
   App↔Services path sync greppable and durable (this is what would have
   prevented the Nines `/game/` prefix 404). Coordinate with WS1 — do this
   after the split so paths land in their final owners.~~ **DONE
   2026-07-14.** Added `_Core/Scripts/_Utils/ApiPaths.cs`, grouped to mirror
   the backend's router prefixes; all 12 cited literal call sites updated.

### WS5 — Backend infra

**DONE (2026-07-14)** — all 4 items:

1. ~~**Machine-credits TOCTOU** (`web/machine_credits.py:236-247`): consume is
   check-then-insert — two concurrent consumes can both pass the balance
   check and overdraw. Serialize the check (transaction/row lock) or enforce
   with a DB constraint. **This is a money bug — may be pulled forward ahead
   of everything.**~~ **DONE 2026-07-14.** Single-process deployment
   (`fly.toml` `min_machines_running = 1`, no `--workers`, and
   `start_backend.sh` refuses concurrent starts) made an in-process
   `asyncio.Lock` keyed by `(game_tag, box_id)` sufficient — SQLite has no
   row-level locking to reach for instead. Guards the balance check and the
   consume-event insert in one critical section.
2. ~~**Alembic baseline.** Schema is `Base.metadata.create_all` only
   (`web/main.py:48`) on a persistent prod SQLite volume; `alembic` is a
   declared-but-unused dep (`pyproject.toml:30`). Add the baseline + wire
   into deploy **before** any further schema churn (WS4's items don't change
   schema; anything that does waits for this).~~ **DONE 2026-07-14.** Added
   `alembic/` (async template) with one baseline migration reflecting the
   current 7-table schema; `alembic` moved from dev-only to a runtime dep
   since production now runs migrations at boot. Production `lifespan`
   (`web/main.py`) now calls `alembic upgrade head` instead of `create_all`;
   dev/test keep `create_all`/`drop_all` for fast local iteration (no
   migration authoring needed for local schema changes). The existing prod
   volume predates `alembic_version` tracking, so first boot on this
   revision detects that case (tables exist, no `alembic_version` table) and
   stamps at head instead of upgrading — verified locally against both a
   fresh DB and a pre-existing `create_all` DB.
3. ~~**Drop dead deps**: `dramatiq[redis,watch]`, `pottery`
   (`pyproject.toml:8,13`) — zero imports in `src/`; `env.py:41` `redis_url`
   is unused.~~ **DONE 2026-07-14.** Removed both packages and the
   `redis_url` setting, plus its stale mentions in `.env.example` and
   `docs/FLY_IO_DEPLOYMENT_GUIDE.md`.
4. ~~**Formatting**: backend mixes tab- and 4-space-indented files; enforce
   via Ruff/`.editorconfig`; resolve the disabled `'D'` docstring rule
   note.~~ **DONE 2026-07-14.** Ran `ruff format` across `src/`/`alembic/`
   (24 files, whitespace-only) and added `.editorconfig` to hold the line.
   Resolved the `'D'` TODO as a deliberate decision (docstrings stay
   optional) rather than leaving it open. Added `scripts/lint.sh`
   (`ruff format --check` + `ruff check` + `ty check`) for local use.
   (Update 2026-07-16: the ruff/ty backlog noted here was since cleared;
   all three checks are clean and gate commits via pre-commit hooks.)

### WS6 — Deferred (trigger-based, do NOT build speculatively)

| Item | Trigger | Starting point when triggered |
|------|---------|-------------------------------|
| Leaderboard widget | A second game wants a leaderboard UI | Generalize Racing's stack: `RacingEventService.GetLeaderboardAsync` + `RacingTracksLeaderboardUI` + `RacingRaceCompleteUI` |
| Countdown/timer primitive | A second timed game | Racing's countdown chain (`RacingGame`/`RacingTimingSystem`/`RacingUIManager`) |
| Multiplayer lobby flow | A third multiplayer game | Carrom's `CarromPlayerSetupMenu` + WS3's PlayerRoster |
| `_Games/_Template` skeleton | First external/new contributor onboarding | Strip a copy of Mining |

## §4 — Explicit Non-Goals

Guardrails against over-engineering; each with the reason it's excluded:

- **No ECS / component framework.** Four scene-tree games; Godot nodes ARE
  the component system.
- **No JSON / reflection-based / self-registering game registry.** Trades
  compile-time safety for runtime failure modes to save ~4 lines per game.
- **No standalone Carrom/Racing/Nines decomposition.** Working, test-covered
  code; splitting it benefits no future game. Shrinks incidentally via WS3.
- **No leaderboard widget yet.** One consumer (Racing). WS6 trigger applies.
- **No countdown/lobby framework speculatively.** Rule of three.
- **No `async void` sweep, no full signal-chain→direct-call conversion, no
  Nines null-style pass.** Judged low-value/medium-churn in the prior audit
  and deferred; that judgment stands. (Carrom's 43-signal internal chains
  violate the repo rule, but converting them is behavior-risky and helps no
  new game — revisit only if Carrom gets feature work.)
- **No SDK as a separate assembly/NuGet.** Folder convention + docs suffice
  pre-production.

## §5 — Definition of Done: the "Fifth Game" Checklist

**Superseded (2026-07-13):** this section's draft graduated into
`docs/adding-a-game.md` when WS2 item 5 landed, and that doc has since been
kept current through WS3 (credit shapes, `Platform.Notifications`,
`PlayerRoster`, `GameSessionTestBase`). See `docs/adding-a-game.md` for the
authoritative checklist — don't maintain two copies.

Nice-to-have beyond this roadmap (WS6): `_Games/_Template`, shared
leaderboard widget, countdown primitive.

## §6 — Documentation Debt Tracker

Doc fixes that CANNOT land until their refactor does (safe quick fixes were
already applied 2026-07-13 — commit `docs: fix stale game lists, registration
steps, and onboarding errors`).

| Doc item | Blocked on | Notes |
|----------|-----------|-------|
| ~~`BarBoxApp/_Games/CLAUDE.md` lifecycle + context-detection rewrite~~ | — | **DONE 2026-07-13** (WS2 item 5) |
| ~~`BarBoxApp/CLAUDE.md` SDK contract section~~ | — | **DONE 2026-07-13** (WS2 item 5) |
| ~~`README.md:112-129` "Adding a New Game"~~ | — | **DONE 2026-07-13** (WS2 item 5) |
| ~~`BarBoxApp/agent_docs/game-development-patterns.md` lifecycle/context sections, `architectural-tenets.md`~~ | — | **DONE 2026-07-13** (WS2 item 5). `autoload-service-patterns.md` reviewed, no changes needed — its examples are autoload-to-autoload, not `_Games/` code. |
| ~~`docs/adding-a-game.md` (new)~~ | — | **DONE 2026-07-13** (WS2 item 5) |
| ~~`docs/architecture/sessions.md` — still shows games calling `CreateActivitySessionAsync`/`CloseActivitySessionAsync` directly~~ | — | **DONE 2026-07-15.** Rewrote the key-methods list, both game-integration code examples, the Common Patterns session-lifecycle example, and two Troubleshooting bullets to reflect `GameController.StartBackendSessionAsync` + automatic base-class close. |
| ~~Any doc naming `EventService` / its methods~~ | — | **DONE 2026-07-13** — renamed to `SessionEventService` in README.md, `BarBoxApp/docs/API.md`, `agent_docs/box-identity-guide.md`, `agent_docs/architectural-tenets.md`, `docs/architecture/sessions.md`. `agent_docs/httpclient-patterns.md`'s "Use for: BackendManager, EventService" line is skipped — it's stale beyond a rename (HttpClient now lives on `BackendClient`, not the session/emit class) and belongs with the WS2 item 5 autoload-doc pass below. `*EventService.cs` mentions in `_Games/CLAUDE.md`/`game-development-patterns.md` are the per-game convention (`MiningEventService`, etc.), unrelated to this rename — left untouched. |
| `BarBoxServices/docs/STRIPE_PAYMENT_INTEGRATION_PLAN.md` | nothing — anytime | Reframe from "plan" to "implemented reference" (Stripe is live in `web/payments.py`) |
| ~~`BarBoxServices/docs/FLY_IO_DEPLOYMENT_GUIDE.md` Phase 1~~ | — | **DONE 2026-07-15.** Retitled "Required Files to Create"/"Phase 1: Create Deployment Files" to reference-style headers, completing the reframe the 2026-07-13 status note started. Also added `BOX_REGISTRATION_SECRET` to the `fly secrets set` examples (WS0 item above). |

**Standing rule:** any workstream that changes a documented contract updates
the affected docs in the same commit series. Doc drift is a bug.

---

## Appendix A — EventService Split: Full Execution Spec

> Carried verbatim from the prior audit doc (2026-07-13). Self-contained so a
> fresh session can execute it. Line numbers are current as of 2026-07-13
> (`EventService.cs` is ~1527 lines after the dead-signal removal);
> re-confirm at execution time.

### Problem

`EventService` (`_Core/Scripts/Autoloads/EventService.cs`) is one autoload doing
two unrelated jobs:

1. **Transport** — HTTP connection lifecycle + generic verbs. This is pure
   infrastructure: `OnServiceInitializeAsync` (`:59-129`, config from
   LocationManager + waits on BackendManager), `EnsureConnectedAsync`
   (`:1309-1409`, the connection state machine), `WaitForResponseAsync`
   (`:1411-1436`), `BuildHeaders`/`BuildJsonHeaders`/`BuildPlayerHeaders`
   (`:~1250-1307`), `GetBoxApiKey` (`:1439-1456`), and the 4 generic verbs
   `QueryAsync` (`:609`), `QueryRawAsync` (`:674`), `PostAsync` (`:785`),
   `PutAsync` (`:854`). All share the pattern
   `EnsureConnected → BuildHeaders → _httpClient.Request → WaitForResponse →
   GetResponseCode → ReadResponseBodyAsync → deserialize`.
2. **Domain APIs** — thin URL+payload wrappers grouped by concern:
   - SESSION/EMIT: `CreateActivitySessionAsync` (`:145`), `CloseActivitySessionAsync`
     (`:263`), `CreateLobbySessionAsync` (`:334`), `EmitEventAsync` (`:429`,
     raw POST to `/box/session/{current}`), `EmitUserEventAsync` (`:502`, raw
     POST to lobby session), `GetCurrentSessionId` + identity getters.
   - CREDITS: `GetPlayerCreditsAsync` (`:~1085`), `AddCreditsAsync`,
     `SpendCreditsAsync`, `GetMachineCreditsAsync`, `DepositMachineCreditsAsync`,
     `ConsumeMachineCreditsAsync`.
   - AUTH: `IsUsernameAvailableAsync` (`:907`), `ValidatePlayerCreationAsync`,
     `CreatePlayerAsync`, `RegisterBoxWithDetailAsync`, `RegisterBoxAsync`,
     `GetPlayerIdFromPhone` (static, `:1462`).
   - STRIPE: `CreateCheckoutSessionAsync`, `GetCheckoutStatusAsync`.

The domain methods are almost all one-liners over a generic verb. And three
services already *wrap* them, so real calls **double-hop**:
`CreditService.SpendAsync → EventService.SpendCreditsAsync → EmitUserEventAsync`,
`SessionManager → EventService` (login/lobby/auth),
`StripePaymentService → EventService` (checkout). `PaymentService` only touches
EventService for a readiness gate.

### Target architecture

- **`BackendClient` (new autoload, `_Core/Scripts/Autoloads/BackendClient.cs`)** —
  the SOLE HTTP transport. Owns `HttpClient`, connection config, connect/wait
  primitives, header building, the 4 generic verbs, and a raw
  `PostToSessionAsync(Guid sessionId, string json, Guid? playerId)` for the emit
  path. Depends only on LocationManager (config), BackendManager (readiness),
  and a JWT-token-provider delegate.
- **`EventService` (slimmed to session + emit)** — activity/lobby session
  lifecycle, `EmitEventAsync`/`EmitUserEventAsync`, `GetCurrentSessionId`, and
  the identity getters (`GetVenueName`/`GetBoxName`/`GetBoxId`). All HTTP goes
  through `BackendClient`. (Rename considered below.)
- **`CreditService` / `SessionManager` / `StripePaymentService`** — own their
  domain and call `BackendClient` **directly**, deleting the double-hop.
- **`GameEventServiceBase`** and its subclasses keep calling EventService for
  emit/query — unchanged.

### Auth-coupling seam (must be done in increment 1)

Today `EventService.BuildHeaders` (`:1276`) calls
`SessionManager.GetJwtToken(playerId)` to add the `Authorization: Bearer` header —
a concrete back-edge, while SessionManager *wraps* EventService. Bidirectional.

Fix: `BackendClient` exposes `public Func<Guid, string> JwtTokenProvider { get; set; }`.
`SessionManager.OnServiceInitialize` sets
`BackendClient.GetInstance().JwtTokenProvider = GetJwtToken;`. `BackendClient`'s
header builder invokes the provider if set (else no JWT). Now the transport
depends on a delegate, not the SessionManager type — the cycle is broken. Ordering
is safe: JWT is only needed for player-scoped requests during gameplay (well after
SessionManager init in Phase 3); box-API-key headers need no provider.

### Class rename analysis (decision: rename to `SessionEventService`, in increment 5)

Post-split the class is no longer a generic "event service" — it owns the
**game activity-session lifecycle and the events posted into those sessions**.
The generic name is part of what invited the god-object creep. Options weighed:

| Name | Verdict |
|------|---------|
| keep `EventService` | Lowest churn; "event emit" is still central. But the generic name is exactly what let transport/credits/auth/stripe accrete here; keeping it invites recurrence. Acceptable fallback only. |
| `GameEventService` | **Rejected** — collides with `GameEventServiceBase` and its subclasses (`MiningEventService`, …) that *wrap* this class. Confusing. |
| `GameSessionService` | Foregrounds sessions but hides that it emits events. |
| **`SessionEventService`** | **Recommended** — accurately "manages sessions and the events posted to them"; reads correctly against `GameEventServiceBase` which holds a reference to it; no collision. |

Rename churn (all mechanical): class name; the autoload **node name**
`/root/EventService` in `project.godot`; test node paths
`GetNode<EventService>("/root/EventService")` (in `CarromGameTests:42`,
`MiningGameTests:42`, `NinesGameTests:43`, `RacingGameTests:44`,
`BackendTestBase:162`, `TestHelpers:269`, `CreditServiceInitializationRaceTests`);
`EventService.GetInstance()` callers; `GameEventServiceBase._eventService` field
type; the 4 game event-service ctors that take `EventService`.

Churn-minimizer: **keep the `GameContext.Events` property name** (`GameContext.cs:15`)
so all game-facing code (`Platform.Events`) is untouched — only the property's
*type* changes. **Do the rename LAST (increment 5)**, after domain methods have
moved out and the class's responsibility is settled — renaming a moving target
mid-split adds needless churn. If the rename churn (autoload node + ~7 test
node-paths) isn't judged worth it, keeping `EventService` is an acceptable
fallback; the structural wins stand without it.

### Increment sequence (each builds clean + full C# suite green before commit)

**① BackendClient autoload = transport [foundation, atomic, highest risk].**
- New `_Core/Scripts/Autoloads/BackendClient.cs : AutoloadBase`. Move verbatim:
  the connection config fields + `OnServiceInitializeAsync` transport bootstrap
  (HttpClient create, URL/box-identity/api-key load, wait-for-BackendManager),
  `EnsureConnectedAsync`, `WaitForResponseAsync`, `GetBoxApiKey`, the three
  header builders (with the `JwtTokenProvider` seam replacing the direct
  `SessionManager.GetJwtToken` call), and the 4 generic verbs. Add
  `PostToSessionAsync(Guid sessionId, string json, Guid? playerId = null)`
  encapsulating the raw POST that `EmitEventAsync`/`EmitUserEventAsync` do today.
- EventService: drop the moved code; hold `BackendClient _backend` (via
  `GetAutoload`); its `IsReady` reflects `_backend` readiness; every remaining
  method calls `_backend.QueryAsync/PostAsync/PutAsync/PostToSessionAsync`.
  Identity getters read from LocationManager (or delegate to `_backend`).
  **No other domain method moves out yet** — so no caller changes beyond
  Event/Session wiring.
- `project.godot`: register `BackendClient` autoload **before** `EventService`.
  Init: BackendClient takes over EventService's Phase-2 transport bootstrap; add
  it to `ApplicationBootstrap` Phase 2 (before EventService) or make EventService
  await `BackendClient` readiness.
- `SessionManager.OnServiceInitialize`: set `BackendClient.JwtTokenProvider`.
- Verify: `dotnet build` + full `run-tests.sh` (login/credit/emit paths all
  exercise transport end-to-end against the real backend).

**② CREDITS → CreditService.** CreditService builds its own URLs/payloads over
`BackendClient` (it already owns caching/polling); delete `GetPlayerCreditsAsync`,
`AddCreditsAsync`, `SpendCreditsAsync`, `GetMachineCreditsAsync`,
`DepositMachineCreditsAsync`, `ConsumeMachineCreditsAsync` from EventService.
Update the only direct game caller — Carrom machine-pot in
`CarromPlayerSetupMenu.cs` (`DepositMachineCreditsAsync:~1432`,
`ConsumeMachineCreditsAsync:~1509`, `GetMachineCreditsAsync:~1656`) to go through
CreditService. Verify (CreditServiceTests, CarromMachineCreditsIntegrationTests).

**③ STRIPE → StripePaymentService.** Move `CreateCheckoutSessionAsync`,
`GetCheckoutStatusAsync` into `StripePaymentService` over `BackendClient`; delete
from EventService. Verify (PaymentServiceTests).

**④ AUTH → SessionManager / LocationManager.** Move `IsUsernameAvailableAsync`,
`ValidatePlayerCreationAsync`, `CreatePlayerAsync` into SessionManager;
`RegisterBoxWithDetailAsync`/`RegisterBoxAsync` into LocationManager (its only
caller, `LocationManager.cs:299,306`); `GetPlayerIdFromPhone` is already a static
SessionManager delegate — inline it. Delete from EventService. The token-provider
seam from ① already removed the back-edge. Verify (SessionManagerTests,
PlayerRegistrationTests).

**⑤ Rename EventService → `SessionEventService`** (optional, see analysis). Class
+ autoload node + test node-paths + `GetInstance` callers + field types; keep
`GameContext.Events`. Verify.

**End state:** `SessionEventService` = session lifecycle + emit + identity over
`BackendClient`; each domain owned by exactly one service; no double-hops; the
auth cycle broken.

### Cross-cutting risks

- **Init ordering (①).** The autoload registration + Phase-2 sequencing is the
  only genuinely tricky part. `BackendClient` must be ready before EventService
  and before any credit/auth/stripe call. Keep BackendClient's readiness gating
  identical to EventService's current gate so downstream `IsReady` checks behave.
- **`GetPlayerIdFromPhone` is `static`** and used as `EventService.GetPlayerIdFromPhone`
  in callers — moving/renaming touches those call sites (grep first).
- **Tests hit the real backend**, so transport regressions surface immediately;
  run the full suite after every increment, not just build.

---

## Appendix B — ⚠️ SECURITY (out-of-band; keys rotated, code items resolved 2026-07-15)

> Carried from the prior audit doc. Operational, not code-cleanup; recorded
> here so it is not lost. **Not part of any coding workstream.**

**✅ Rotation verified (2026-07-13)** — checked against git history by hash
comparison (no secret values exposed):

- **Stripe secret key**: current config value differs from the leaked
  `sk_test_…`; `STRIPE_SECRET_KEY` is set as a deployed Fly secret.
- **Stripe webhook secret (production)**: the Fly value differs from the
  leaked `whsec_…`. The local dev Stripe-CLI listener secret
  (`BarBoxServices/.env:33`, untracked) briefly still held the leaked value;
  the CLI secret had already rolled over and the file was synced to the new
  value on 2026-07-13 — the leaked webhook secret is no longer in use
  anywhere.
- **Leaked box API key**: dead **by design** — it's 43 chars non-hex,
  predating the current scheme where keys are 64-hex
  `HMAC(JWT_SECRET_KEY, box_id)` verified by exact derivation
  (`web/auth.py:33-52`). It cannot authenticate regardless of the secret.
- **`JWT_SECRET_KEY`**: set as a deployed Fly secret (rotated per operator;
  the value can't be compared from digests — and no historical revision ever
  contained a real production JWT secret, only the public default).

Net: the secrets in git history are dead credentials; history scrubbing is
optional hygiene, not urgent.

**Still open (code-level):**

- ~~`POST /box/` and `PUT /box/{box_id}` are **unauthenticated and return the
  derived api_key**~~ — **DONE 2026-07-15.** Both now require an
  `X-Registration-Secret` header (`BOX_REGISTRATION_SECRET` env var,
  `env.py`) — but only when minting a key for a box_id that doesn't exist
  yet: `create_box` (POST, always creation) gates the whole endpoint;
  `register_box` (PUT) checks the secret inline only in its "box doesn't
  exist - create it" branch (`web/box.py`), leaving the idempotent recovery
  branch ("box already exists, return its key") unauthenticated exactly as
  documented — confirmed already-registered boxes never need this and
  require zero App/config changes, since their `PUT` calls always hit the
  recovery branch. `LocationManager.cs`/`BackendClient.cs` on the App side
  send the secret (`BARBOX_REGISTRATION_SECRET`) when configured, but it's
  only actually required for provisioning a brand-new box_id. **Operators
  should keep it configured permanently** (not remove it after first boot)
  — sending it is harmless once a box exists, and keeping it in place
  preserves self-heal-after-DB-loss (backup restore, migration issue) without
  an on-site visit; `deployment/deploy.sh`'s own local self-registration flow
  now generates/reads `BOX_REGISTRATION_SECRET` from the backend's `.env` the
  same way it already does for `JWT_SECRET_KEY`, and `scripts/seed-test-data.sh`
  sends it too, since both call the same create path.
- ~~`.env.example` "test keys are safe to commit" guidance~~ — **removed
  2026-07-13** (commit `a386ab5`).
- ~~The `JWT_SECRET_KEY` default remains
  `"dev-secret-UNSAFE-change-in-production"`~~ — **already resolved**, found
  during 2026-07-15 doc-accuracy pass: `env.py`'s `model_post_init` (added
  prior to this roadmap being written) already raises `ValueError` at boot if
  `is_production()` and the key starts with `"dev-"`. This item was stale;
  no code change was needed, just correcting the doc.

---

## Appendix C — Prior Work Summary (compressed)

From `docs/codebase-audit-and-simplification-plan.md` (local, untracked —
consult it for full per-commit detail). All on branch
`codebase-audit-simplification-plan`, all verified with build + full test
suites at the time:

- **Stage 1 (2026-07-12)** — correctness fixes: Nines jackpot endpoint 404,
  winner-name privacy fallback, payload-model wiring + import-time drift
  guard, `register_location` GET→POST, dead fixture cleanup. ⚠️ Preserved
  convention: Nines `player_id` is a **phone number**, and the backend
  leaderboard join on `p.phone_number` is CORRECT — do not "fix" it.
- **Stage 2 (2026-07-12)** — perf quick wins: removed per-physics-frame
  signal emission in RacingCar, orphan Racing UI labels, Mining double
  UI-drive, per-step Carrom ray-query allocation, dead code.
- **Stage 4 (2026-07-12→13)** — ★ unified the game↔backend boundary: session
  create/close moved into `GameController` (`StartBackendSessionAsync` +
  auto-close), result submission through `GameEventServiceBase.SubmitResultAsync`
  (Carrom and Nines gained their first event-service subclasses; fixed a
  latent bug where Nines jackpots only recorded if another game had left a
  session open), `CreditService.SpendWithConfirmationAsync` added (Racing
  adopted it). Nines gained its first tests (test count 287→291).
- **Stage 5 (partial, 2026-07-13)** — deleted 9 zero-subscriber platform
  signals; serialized ApplicationBootstrap init + shutdown logout (fixed a
  latent race); converted un-disconnectable lambda signal handlers in Carrom.
  Remaining Stage-5 item (EventService split) became **WS1 / Appendix A** of
  this roadmap; the other leftovers (`async void` sweep, signal-chain
  conversion, Nines null-style) were judged low-value and are now §4
  non-goals.
- **Docs quick-fix pass (2026-07-13)** — corrected stale game lists,
  backend registration steps, Mining file-count claims, README onboarding
  errors (see §6 for what remains blocked on refactors).
