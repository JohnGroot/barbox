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
   were correct on 2026-07-13 and WILL drift.
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
- `ToastService` (`Platform.Toast`), `PlayerRoster`, `GameTestFixture` —
  added in WS3.

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
| WS0 | Security remediation (operational, out-of-band) | — | med (keys rotated) | PARTIALLY DONE — see Appendix B |
| WS1 | EventService split (5 increments, spec in Appendix A) | — | High (inc ①), low after | **DONE (2026-07-13)** — all 5 increments, see §3 notes |
| WS2 | New-game DX: one identity, one discovery idiom, registry hygiene, drift guards, docs | WS1 (rename settles names) | Low | In progress (item 1 done 2026-07-13) |
| WS3 | Game SDK v1: credit confirmation UI + credit shapes, ToastService, PlayerRoster, GameTestFixture | WS1 inc ② for the credit items | Medium | Not started |
| WS4 | Backend dedup (leaderboard SQL, auth deps, payments service layer, error envelope, ApiPaths) | none — parallel-safe with WS1–3 | Medium | Not started |
| WS5 | Backend infra: machine-credits TOCTOU fix, Alembic, dead deps, formatting | none; TOCTOU fix may be pulled forward anytime | Low/Med | Not started |
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
3. **Registry hygiene — not JSON.** Keep the hardcoded C# registry (compile-
   checked, ~4 lines per game); delete the stale "Load from
   _Data/GameRegistry.json" comment (`GameRegistry.cs:82`). Upgrade the silent
   failure mode: `GameHost.LoadGameOverlay` logs and returns on an unknown id
   (`GameHost.cs:57-61`) — instead, add a boot-time assertion in
   `GameRegistry` initialization that every registered game's `ScenePath`
   exists and ids are unique, and make unknown-id loading fail loudly.
4. **Backend drift guards.** The two forgettable manual edits when adding a
   game are the `SessionEventType` union (`structures.py:94-100`) and payload
   classification (`games/validation.py`). The payload guard already exists
   (`_check_payload_model_coverage`, `validation.py:47-59`). Add the missing
   one: an import-time assert that every `GAMES` key's event types are
   reachable through `SessionEventType` (i.e., the union includes each game's
   `EventType`), so a missing union edit fails at boot, not with a runtime
   422.
5. **Docs pass (after items 1–4 settle the contract).** Rewrite the
   pre-GameController guidance:
   - `BarBoxApp/_Games/CLAUDE.md:21-51` — "Game Lifecycle Rules" and "Context
     Detection" still teach hand-rolled `_gameActive` checks,
     `CallDeferred(StartGame)`, and `GameHost.GetInstance()` context probing.
     Rewrite around the `GameController` phase hooks and `Platform.X`.
   - `BarBoxApp/CLAUDE.md` — never mentions `GameController`,
     `SubmitResultAsync`, or `SpendWithConfirmationAsync`; add the SDK
     contract summary.
   - `README.md:112-129` "Adding a New Game" — shows direct
     `CreateActivitySessionAsync`/`CloseActivitySessionAsync` calls that the
     base class now owns, and a wrong `Games/` path casing. Replace with the
     checklist pointer.
   - `BarBoxApp/agent_docs/game-development-patterns.md` (lifecycle +
     context-detection sections), `autoload-service-patterns.md`,
     `architectural-tenets.md` — same drift; review alongside.
   - Write **`docs/adding-a-game.md`**: the §5 checklist as a living doc,
     covering both codebases end-to-end.

### WS3 — Game SDK v1

Priority order within the workstream. Money first.

1. **Credit correctness** *(gated on WS1 ② for the machine-pot item)*:
   - **Real spend confirmation.** `CreditConfirmationHelper.cs:34` is a stub
     that auto-confirms every spend, in production too — Racing charges real
     credits with no user-facing confirmation. Decision made: build the real
     modal. Route `ShowCreditConfirmationAsync` through `UIManager`'s existing
     modal/confirmation infrastructure (`UIManager.ShowConfirmationAsync`,
     `UIManager.cs:332`) with amount + balance shown; keep a dev/test bypass
     flag that cannot be on in production builds.
   - **`CreditService.SpendManyWithRollbackAsync(players, amount)`** — the
     multi-player deduct-with-rollback shape. Migrate Nines' hand-rolled
     `DeductCreditsWithRollbackAsync`/`RollbackCreditsAsync`
     (`NinesGame.cs:547-608`) onto it.
   - **`CreditService.TransferToMachineAsync(...)`** — the spend-then-deposit
     (player → machine pot) shape with rollback on partial failure. Migrate
     Carrom's table-credit flow (`CarromPlayerSetupMenu.cs:1409` spend,
     `:1293` refund).
   - **Mining deposit**: `MiningState.cs:434` fire-and-forgets `AddAsync`
     when converting gems to credits — await it and surface failure to the
     player.
   - Verify each migration against the existing money tests
     (`CreditServiceTests`, `CarromMachineCreditsIntegrationTests`, the Nines
     rollback test) + the Hurl credit suites.
2. **ToastService.** Four incompatible "tell the user something" mechanisms
   exist; `CarromNotificationSystem` (555 lines — typed, stacking, timed/
   sticky, fade animations) is the best. Promote a generalized version to
   `_Core`, expose as `Platform.Toast`, migrate Carrom onto it, then replace
   the bespoke `NinesUI.ShowError`, `MiningGameUI.ShowError`, and Racing's
   inline error paths.
3. **PlayerRoster.** Four independent roster implementations with
   inconsistent identity keying: Nines (`NinesGame.cs:339-455`, phone-keyed),
   Carrom (`CarromGame.cs:1458-1475` + the setup menu, phone-as-PlayerId),
   Mining (`MiningGame.cs:537-567`, single primary user), Racing (stateless).
   Extract a `_Core` roster component: **UUID-keyed, phone number as an
   attribute**. ⚠️ Preserve the Nines emit convention: `nines/jackpot_won`
   sends the winner's **phone number** as `player_id` and the backend
   leaderboard join depends on it (`games/nines/service.py` joins
   `p.phone_number`) — keep that mapping at the emit boundary; do not "fix"
   it to a UUID.
4. **GameTestFixture.** The four `*GameTests` files each rebuild
   session-setup/teardown boilerplate over `BackendTestBase`. Extract a
   shared fixture so a new game's first flow test is ~20 lines. (This is why
   Mining has 8 tests and Nines 4, vs Racing's 113 — testability should come
   with the SDK, not require bespoke scaffolding.)

### WS4 — Backend dedup

Independent of app work; each item is its own commit + Hurl-green.

1. **Leaderboard SQL builder** in `games/common.py`. The
   `box_session_event ⋈ box_session … json_extract … LEFT JOIN player`
   skeleton is duplicated ~12×: `racing/service.py:40,74,94`,
   `carrom/service.py:31,81`, `nines/service.py:30`,
   `mining/service.py:39,50,61,129,189,242`, plus `web/player.py:483`,
   `web/machine_credits.py:41,67`. The copies genuinely differ in **join-key
   strategy** (UUID vs de-hyphenated UUID vs phone_number) — parameterize the
   builder by that strategy rather than forcing one shape.
2. **Signed-credit SUM helper.** The `SUM(CASE WHEN type='…earn' THEN + …
   'spend' THEN -)` shape is duplicated at `web/player.py:486` and
   `web/machine_credits.py:44` (different event names, same skeleton).
3. **Collapse the 3 box-auth dependencies.**
   `web/dependencies.py:42/116/182` are three copies of
   header→`verify_box_api_key`→fetch→404. Parameterize into one.
4. **Extract `payments/service.py`.** `web/payments.py` (895 lines) holds
   business logic in the router (`_issue_credits_for_payment:99`,
   `get_or_create_credit_session:251`, admin reconciliation `:726+`). Split
   to match the games-module service/router convention.
5. **Standardize the error envelope** on `structures.ErrorDetail`
   (`structures.py:~208`); today some handlers return structured
   `{code,message,details}`, others bare `detail="string"`
   (`dependencies.py:70,96,146`, `machine_credits.py:240`), and payments adds
   an ad-hoc `retryable` field — fold it into the model or drop it.
6. **App-side `ApiPaths` constants class.** Backend paths are scattered
   string literals across `EventService.cs`, `SessionManager.cs`, the game
   event services, and `NinesGame.cs`. One constants class makes
   App↔Services path sync greppable and durable (this is what would have
   prevented the Nines `/game/` prefix 404). Coordinate with WS1 — do this
   after the split so paths land in their final owners.

### WS5 — Backend infra

1. **Machine-credits TOCTOU** (`web/machine_credits.py:236-247`): consume is
   check-then-insert — two concurrent consumes can both pass the balance
   check and overdraw. Serialize the check (transaction/row lock) or enforce
   with a DB constraint. **This is a money bug — may be pulled forward ahead
   of everything.**
2. **Alembic baseline.** Schema is `Base.metadata.create_all` only
   (`web/main.py:48`) on a persistent prod SQLite volume; `alembic` is a
   declared-but-unused dep (`pyproject.toml:30`). Add the baseline + wire
   into deploy **before** any further schema churn (WS4's items don't change
   schema; anything that does waits for this).
3. **Drop dead deps**: `dramatiq[redis,watch]`, `pottery`
   (`pyproject.toml:8,13`) — zero imports in `src/`; `env.py:41` `redis_url`
   is unused.
4. **Formatting**: backend mixes tab- and 4-space-indented files; enforce via
   Ruff/`.editorconfig`; resolve the disabled `'D'` docstring rule note.

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

When WS1–WS3 land, adding a game looks like this (this section graduates into
`docs/adding-a-game.md` during WS2 item 5):

**App side:**
1. Copy the Mining reference shape (Game / Engine / State / UI / Config /
   EventService).
2. Subclass `GameController`; implement `GetGameId()` — one id, used
   everywhere (registry, backend tag, scene).
3. Add one registration line in `GameRegistry.cs` — boot assertion catches a
   bad scene path or duplicate id.
4. All platform access through `Platform.X`. Money through `CreditService`
   primitives (single spend / multi-spend-rollback / machine transfer — all
   confirmation-gated). Results through your `GameEventServiceBase` subclass'
   `SubmitResultAsync`. User feedback through `Platform.Toast`. Players
   through `PlayerRoster`.
5. Call `StartBackendSessionAsync` when gameplay begins; the base auto-closes
   it on teardown.

**Backend side:**
6. Create `games/<tag>/` (`schemas.py` with `EventType` alias, `service.py`,
   `router.py`); add one `GAMES` dict entry. Import-time guards catch a
   missing `SessionEventType` union edit or unclassified payload.

**Tests:**
7. Subclass `GameTestFixture` for flow tests; drop one auto-discovered Hurl
   file under `test/02-feature/<tag>/`.

**Verify:** the three §0 commands, plus play the game once in-editor and
confirm `play/begin → score/result → finish` events post and credit entry
gates correctly.

Nice-to-have beyond this roadmap (WS6): `_Games/_Template`, shared
leaderboard widget, countdown primitive.

## §6 — Documentation Debt Tracker

Doc fixes that CANNOT land until their refactor does (safe quick fixes were
already applied 2026-07-13 — commit `docs: fix stale game lists, registration
steps, and onboarding errors`).

| Doc item | Blocked on | Notes |
|----------|-----------|-------|
| `BarBoxApp/_Games/CLAUDE.md` lifecycle + context-detection rewrite | WS2 items 1–4 | Rewrite around GameController hooks + Platform.X (WS2 item 5) |
| `BarBoxApp/CLAUDE.md` SDK contract section | WS2 items 1–4 | Add GameController / SubmitResultAsync / credit primitives |
| `README.md:112-129` "Adding a New Game" | WS2 items 1–4 | Replace hand-rolled session code with checklist pointer |
| `BarBoxApp/agent_docs/game-development-patterns.md` lifecycle/context sections, `autoload-service-patterns.md`, `architectural-tenets.md` | WS2 items 1–4 | Same pre-GameController drift |
| `docs/adding-a-game.md` (new) | WS2 items 1–4 | The §5 checklist as a living doc |
| ~~Any doc naming `EventService` / its methods~~ | — | **DONE 2026-07-13** — renamed to `SessionEventService` in README.md, `BarBoxApp/docs/API.md`, `agent_docs/box-identity-guide.md`, `agent_docs/architectural-tenets.md`, `docs/architecture/sessions.md`. `agent_docs/httpclient-patterns.md`'s "Use for: BackendManager, EventService" line is skipped — it's stale beyond a rename (HttpClient now lives on `BackendClient`, not the session/emit class) and belongs with the WS2 item 5 autoload-doc pass below. `*EventService.cs` mentions in `_Games/CLAUDE.md`/`game-development-patterns.md` are the per-game convention (`MiningEventService`, etc.), unrelated to this rename — left untouched. |
| `BarBoxServices/docs/STRIPE_PAYMENT_INTEGRATION_PLAN.md` | nothing — anytime | Reframe from "plan" to "implemented reference" (Stripe is live in `web/payments.py`) |
| `BarBoxServices/docs/FLY_IO_DEPLOYMENT_GUIDE.md` Phase 1 | nothing — anytime | Reframe "files to create" → "files that exist" (status note added 2026-07-13; full reframe pending) |

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

## Appendix B — ⚠️ SECURITY (out-of-band; keys rotated, code items remain)

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

- `POST /box/` and `PUT /box/{box_id}` are **unauthenticated and return the
  derived api_key** (`web/box.py:21,75-100`; PUT recovery path `:195-258`
  deliberately "always returns key for recovery/re-deployment"). Anyone who
  can reach the API can mint a valid box key — **this is now the main
  exposure**. Add auth (or at minimum a registration secret) to box
  registration — small, code-level; can ride along with WS4.
- ~~`.env.example` "test keys are safe to commit" guidance~~ — **removed
  2026-07-13** (commit `a386ab5`).
- The `JWT_SECRET_KEY` default remains
  `"dev-secret-UNSAFE-change-in-production"` (`env.py:45`) — an environment
  missing the env var silently falls back to a public value, which would also
  make box keys publicly derivable. Consider making production boot fail on
  the default.

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
