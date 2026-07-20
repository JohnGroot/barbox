# BarBox Performance Audit & Profiling Plan

**Created:** 2026-07-14. **Scope:** BarBoxApp (Godot 4.7, C#, net9.0) and BarBoxServices (FastAPI, SQLAlchemy 2.0 async, SQLite).

This doc is the execution plan for (1) setting up CLI-driven performance tooling — JetBrains dotTrace/dotMemory for the .NET client, Godot-native diagnostics for engine-side time, py-spy + timing middleware for the backend — both locally and against the remote build box, and (2) a prioritized audit backlog of concrete optimizations in the realtime games (Racing, Carrom) and the backend hot paths.

Relationship to other docs:
- `docs/architecture-roadmap.md` is the active architecture plan. Transport-level items here (A2, A4) intersect **WS1 (BackendClient as sole transport)** — fixes for those land inside WS1-owned code, not as per-game patches.
- `docs/codebase-audit-and-simplification-plan.md` Stage 2 already did the low-risk hot-path allocation cleanups (Feb–Jul 2026). This doc picks up where Stage 2 stopped: items that need measurement before/after, and the tooling to do that measurement.

**Licensing:** dotTrace/dotMemory command-line *collectors* are free and redistributable; *analyzing* snapshots requires dotUltimate (available — Rider is the analysis UI). Free fallback stack (`dotnet-trace` / `dotnet-counters` / `dotnet-gcdump`) is documented in C1 so profiling never blocks on a license.

---

## Progress Log

**2026-07-14 — A0 done.** `a1cbb27` added aggregate Stopwatch timing around
`CarromPiece._PhysicsProcess` and `CarromPhysicsMonitor._PhysicsProcess`,
pushed via `DebugPerformanceMonitor.SetMetric` (debug-build-only).

**2026-07-14 — A5 done, all 5 items.** `18ce722` bundled: `RacingGame.cs`
debug `SetMetric` string interpolation throttled to every 3rd frame;
`RacingZoneManager.GetZonesForBody` reuses a pooled list instead of
`ToList()` (mirroring `GetZonesAtPosition`'s existing pattern — the doc's
original citation named the wrong method, same underlying fix);
`RacingVisualFeedbackRenderer.CleanupExpiredTrailPoints` fixed to scan
oldest-first (it scanned newest-first and broke immediately, so it never
actually trimmed expired points); `CarromModeManagerBase`'s empty `_Process`
override removed. The `RacingUIManager.cs` string-interpolation item (doc's
3rd bullet) was reviewed 2026-07-15 and found not to be real duplication —
the two call sites format different things (a `"label: value s"` HUD string
vs. a `MM:SS.mmm` timer string) — left as-is, no action needed.

**2026-07-14 — A3 done.** `84e7573` dirty-flags the HUD arc redraw and
caches text measurements in `RacingHUDArcRenderer`.

**2026-07-15 — A1 done.** `5c99826` + `e0703b4` added a spatial-index path
for Racing off-track validation (shadow-validated against the old
`Curve2D.GetClosestPoint` scan before cutover), replacing the O(baked-points)
per-frame native scan.

**2026-07-15 — A2 done.** `bf62635` (+ follow-up doc clarification `91eb03b`)
queues lap-boundary backend saves instead of blocking the main thread at
lap/race boundaries.

**2026-07-15 — B0 done.** `182152e` added the per-request timing middleware
(p50/p95 baseline, `X-Process-Time` header).

**2026-07-15 — B0b done.** `3a1e94d` fixed `add_request_id_middleware`
discarding the bound logger, so per-request `request_id` correlation
actually attaches to logs now.

**2026-07-15 — B1 done.** `2703d75` added the `box_session_event(type,
session_id)` index via Alembic migration.

**2026-07-15 — B3 done.** `820b18d` combined the machine-credit
balance+contributions checks into one scan (was 4 scans per consume).

**2026-07-15 — B5 done.** `d2809ef` deduped the leaderboard SQL builder and
added an `ORDER BY` direction parameter.

**2026-07-15 — B6 done.** `0d14682` joined the session+box lookup into one
query and demoted the per-request `box_authenticated` log to debug.

**2026-07-15 — B2 done.** `f49eef7` first fixed the double json_extract
evaluation (comment at `carrom/service.py:52-53`). Then, closing out this
plan's remaining B2 item, the per-row UUID `SUBSTR` hyphen-reconstruction was
replaced with a `json_each(payload, '$.scores')` join on
`REPLACE(je.key, '-', '') = player_id` — mirrors the cheaper direction
`total_wins` already used. Verified against
`test/02-feature/carrom/carrom-game-flow.hurl`'s exact score/win assertions.
Preserves existing semantics exactly (only the session host's own score
counts, not every player's) — that's intentional, test-covered behavior, not
something this item fixes.

**2026-07-15 — B4 done.** `GET /box/session/{id}` now takes
`include_events: bool = Query(False)`, gating the `joinedload` that
previously always eager-loaded the full event history on every poll. Zero
production App callers existed (confirmed via `ApiPaths.cs`); only
`RacingGameTests.cs` and 2 hurl assertions needed `?include_events=true`
added.

**2026-07-15 — WS0 box-registration item done** (tracked in
`architecture-roadmap.md` Appendix B, noted here since it rode along with
this pass). `POST /box/` and `PUT /box/{box_id}`'s create branch now require
an `X-Registration-Secret` header; the idempotent recovery branch (existing
box_id) stays unauthenticated by design.

**2026-07-19 — C1 done (local pieces).** dotTrace/dotMemory were already
present via JetBrains Toolbox (`~/Applications/dotTrace 2026.1.4.app`,
`~/Applications/dotMemory.app` — the bundled `Contents/DotFiles/macos-arm64/
dot{Trace,Memory}` binaries are the CLI entry points) rather than the
`dotnet tool install -g JetBrains.*` packages this doc originally assumed;
no need to double-install. Installed the free-fallback tools that weren't
already present: `dotnet-counters`, `dotnet-trace`, `dotnet-gcdump` (global
dotnet tools) and `py-spy` (via `uv tool install`). **Needs-verification
still open:** invoking the bundled `dotTrace` CLI binary directly (`dotTrace
... help`) hung with no output on first try — likely a first-run EULA/license
prompt consuming stdin — resolve interactively in Rider or a real terminal
before relying on it for `dottrace attach`.

**2026-07-19 — C0b done.** Vendored `godot-extended-libraries/godot-debug-menu`
at pinned release `v1.2.0` into `BarBoxApp/addons/debug_menu/` (5 files:
`plugin.cfg`, `plugin.gd`, `debug_menu.gd`, `debug_menu.tscn`, `LICENSE.md`).
Enabled via `project.godot`'s `[editor_plugins] enabled` array. It
self-registers the `DebugMenu` autoload and creates its own `cycle_debug_menu`
InputMap action bound to F3 at runtime (`debug_menu.gd:85-91`) — no manual
Input Map changes needed. Complements, doesn't replace,
`_Core/Scripts/Debug/DebugPerformanceMonitor.cs`.

**2026-07-19 — C2a done (written, not hand-tested).** Added
`BarBoxServices/deployment/profile-remote.sh`, mirroring `connect.sh`'s
positional-arg/color-helper conventions and `deploy.sh`'s flag-parsing loop.
**Correction to this doc's original PID-resolution spec:** the deployed
units are `systemctl --user` (see `barbox-backend.service`/
`barbox-frontend.service`), not system units as C2a's original text assumed —
the script uses `systemctl --user show <unit> -p MainPID --value` for the
backend (its `ExecStart` runs `fastapi` directly, so MainPID *is* the
process). For the frontend, `MainPID` is `start_frontend.sh` (it stays
resident for signal forwarding — see its `trap`/`wait`), so the script
reuses the existing `pgrep -f 'BarBox\.x86_64'` pattern from
`stop_all.sh`/`start_frontend.sh` instead of walking child PIDs. **Not
hand-tested against the real box this session** (no Tailscale/kiosk access)
— syntax-checked (`bash -n`) only.

**Still open in this doc:** A4 (needs a real profiling session, tooling is
now in place), B7 (deferred pending B1/B2 numbers, per original design), and
the remaining Part C items: C0a (Tailscale remote-editor-profiler recipe,
needs hand-testing against the kiosk), C0c (`--print-fps`/`--gpu-profile` /
`AddCustomMonitor` doc notes), C3 (pin down the two windowed profiling
scenarios), C4 (py-spy recipe write-up — tool is installed, recipe doc isn't
written), and hand-testing `profile-remote.sh` itself against the real build
box. All need either a live profiling question to chase or Tailscale/kiosk
access this session didn't have.

---

## §0 — How to Use This Doc

- **Re-verify every file:line anchor before editing.** Anchors were verified 2026-07-14 and will drift as the codebase changes. `RacingUIManager.cs` anchors in A5 were re-verified post-audit-Stage-2.
- **No optimization lands without a before/after measurement** from Part C tooling (or the A0/B0 instrumentation). This is the gate for every A/B item.
- **Scoping rule for remote profiling:** the build box is for soak / idle / real-usage capture only. Scenario-driven verification of specific work items (A1–A3) happens **locally** with a windowed run — never over SSH.
- One work item per commit where practical; update the Progress Log with a dated entry per session.

Verification commands:

```bash
# Frontend
cd BarBoxApp && dotnet build
cd BarBoxApp && bash scripts/run-tests.sh     # backend + Hurl + GoDotTest phases

# Backend
cd BarBoxServices && sh scripts/test.sh       # Hurl integration tests
```

---

## Part C — Profiling Tooling Setup (execute first)

### C0 — Godot-native diagnostics (engine-side view dotTrace can't see)

Godot's built-in profiler has **zero C# visibility** — C# execution shows up only as opaque time inside engine callbacks (open proposal, godot-proposals #14291, still unimplemented as of 2026). Conversely, dotTrace sees managed frames but engine C++ time only as unresolved native frames. The two are strictly complementary: use C0 tooling to answer *"is the frame budget going to physics / rendering / script?"*, then C1 tooling to answer *"which C# method?"*.

All of this stays **in-repo** — Godot addons must be vendored inside the project dir (`BarBoxApp/addons/`) to be exported at all, and the glue scripts are coupled to this repo's paths.

Work items:

- **C0a — Remote editor-profiler recipe.** Export a *debug* build (`scripts/build-export.sh` variant with the debug template), run it on the kiosk with `--remote-debug tcp://<dev-machine-ip>:6007`, with the editor's "Keep Debug Server Open" enabled. The editor Profiler (engine-category totals: Physics Process, Process, etc.) and the Visual Profiler (per-pass GPU + CPU render time) then work against the kiosk over Tailscale. Document the recipe in this section once hand-tested; needs-verification: kiosk firewall/Tailscale reachability for the editor connection.
- **C0b — Vendor `godot-debug-menu`** ([godot-extended-libraries/godot-debug-menu](https://github.com/godot-extended-libraries/godot-debug-menu)) into `BarBoxApp/addons/debug_menu/` at a pinned release. Pure GDScript, works in **release** exports, F3 cycles an overlay with frametime graph and best/1%-worst frametimes — the kiosk-stutter tool when no profiler is attached. Complements the existing `_Core/Scripts/Debug/DebugPerformanceMonitor.cs` (which is debug-build-only and metric-push-based).
- **C0c — Scriptable stdout telemetry.** `--print-fps` and `--gpu-profile` CLI flags on exported builds for zero-cost smoke checks; `Godot.Performance.AddCustomMonitor` (C#-accessible) for counters we want to poll/log headlessly.

Deferred / skipped (documented so future sessions don't re-litigate):

- **Tracy — deferred, build on demand.** Native in Godot 4.6+ (`scons profiler=tracy`, Tracy version pinned to the engine — 0.13.0 for 4.7). Excellent remote client-server capture, but requires maintaining a custom .NET-enabled engine build for Linux + macOS and does **not** resolve C# frames. Adopt only if C0a's engine-category totals prove too coarse to localize an engine-side problem. The superseded third-party `godot_tracy` / `GodotTracy` modules should not be used.
- **Perfetto** — Android-only. Skip.
- **EngineDebugger/servers-profiler plumbing** — script-language-centric, low ROI given dotTrace. Skip.
- **Sentry SDK for Godot** — official and stable, but it's crash/error telemetry with no profiling; belongs to a separate observability decision, out of scope here.

### C1 — Local JetBrains CLI profiling (macOS, Apple Silicon)

Install (once):

```bash
dotnet tool install --global JetBrains.dotTrace.GlobalTools    # → dottrace
dotnet tool install --global JetBrains.dotMemory.Console       # → dotmemory
```

Platform limits, in short: on macOS/Linux only **Sampling and Timeline** profiling types are available (Tracing and line-by-line are Windows-only); snapshot analysis happens in **Rider** (the CLI XML report/compare tooling is Windows-only); attach works on .NET 5+/macOS and .NET Core 3+/Linux, which Godot 4.7's CoreCLR-hosted net9.0 satisfies.

Local runs are interactive (launch the game, play the scenario, stop) — copy-paste recipes, not wrappers:

```bash
# Resolve the Godot binary the same way run-tests.sh does
GODOT="${GODOT_BIN:-$HOME/Library/Application Support/godotenv/godot/bin/godot}"

# Launch the editor build of the game under Timeline profiling (GC + allocation view)
dottrace start --profiling-type=Timeline --save-to=profiling/racing-$(date +%s).dtt \
  "$GODOT" -- --path BarBoxApp

# Attach to an already-running Godot process (Sampling; timed collection)
dottrace attach <pid> --profiling-type=Sampling --timeout=60s --save-to=profiling/attach.dtp

# Memory snapshot of a running game
dotmemory get-snapshot <pid> --save-to-dir=profiling/
```

Open `.dtp`/`.dtt`/`.dmw` files in Rider (dotUltimate). Release exports already emit portable PDBs (`BarBox.csproj` sets `DebugType=portable` + `DebugSymbols=true` in Release), so exported-build snapshots symbolicate.

Free fallback (no license needed for analysis; works against any CoreCLR process incl. Godot):

```bash
dotnet tool install -g dotnet-counters dotnet-trace dotnet-gcdump
dotnet-counters monitor -p <pid>                      # live GC/alloc-rate/threadpool counters
dotnet-trace collect -p <pid> --format speedscope     # open in https://speedscope.app or Rider
dotnet-gcdump collect -p <pid>                        # heap dump, open in Rider
```

Needs-verification on first use: dotTrace attach against the hostfxr-hosted Godot process on macOS ARM64 (expected to work since the runtime is standard CoreCLR — confirm hands-on and note the result in the Progress Log).

### C2 — Remote profiling of the build box (one-command wrapper)

Target: the Linux test box at Tailscale `100.93.137.42`, user `barbox` (see `BarBoxServices/deployment/connect.sh`), running systemd units `barbox-frontend` (Godot kiosk) and `barbox-backend` (FastAPI).

**C2a — Write and hand-test `BarBoxServices/deployment/profile-remote.sh`.** This is the single work item for remote profiling; there is no manual checklist to follow. The script lives beside `connect.sh` and reuses its conventions (target user `barbox`, IP as arg 1 with the documented default, color helpers).

Interface:

```
profile-remote.sh <target-ip> [frontend|backend] [duration-seconds]
    [--memory]           # dotmemory get-snapshot instead of dottrace (frontend only)
    [--out profiling/]   # local output dir (untracked)
```

Behavior, in order:

1. **Preflight**: `ssh -o BatchMode=yes -o ConnectTimeout=5` reachability check; fail fast with a "check Tailscale" hint.
2. **Idempotent tool ensure** (remote): `command -v dottrace || dotnet tool install -g JetBrains.dotTrace.GlobalTools` (same for `dotmemory`); py-spy via `uv tool install py-spy` or `pipx`. First run is slow; later runs are a no-op check.
3. **PID resolution** — never bare `pgrep`:
   - backend: `systemctl show barbox-backend -p MainPID --value` — the unit is `Type=simple` running `fastapi run`, so MainPID **is** the server process. (Generic `pgrep uvicorn` patterns from tutorials will not match.)
   - frontend: MainPID is **`start_frontend.sh`, not Godot** — walk children (`pgrep -P <mainpid>`) to the exported binary. Needs-verification: record the actual Godot process name on the box in this doc when first run.
4. **Collect**: `dottrace attach <pid> --timeout=<dur>s --save-to=~/profiling/<target>-<timestamp>.dtp`, or `dotmemory get-snapshot <pid>` with `--memory`, or `py-spy record -p <pid> -d <dur> --subprocesses -o ...svg` for backend.
5. **Pull + verify**: `rsync` the snapshot to the local `--out` dir, assert nonzero size, print the open-in-Rider path; `trap` cleanup of partial remote snapshots on interrupt.

Documented failure modes (encode as script error messages):
- Host unreachable → Tailscale hint.
- Empty MainPID → service not running (`systemctl status <unit>` hint).
- Attach denied → **py-spy only**: ptrace-based, check `/proc/sys/kernel/yama/ptrace_scope` (Ubuntu default `1` restricts same-user attach to descendants); remediate with `sudo py-spy` or a temporary `sysctl kernel.yama.ptrace_scope=0`. dotTrace/dotnet-trace attach via the EventPipe diagnostics socket and are **not** affected — but cross-user attach is unsupported, so the script must attach as the unit's own user (`barbox`).
- Disk/memory headroom → prefer **Sampling** for first remote passes; Timeline `.dtt` and dotMemory `.dmw` files are large and the frontend unit has `MemoryMax=4G`.

### C3 — Repeatable profiling scenarios

- **Headless GoDotTest run** as the deterministic harness: `"$GODOT" --path BarBoxApp --headless --run-tests --quit-on-finish` (as `run-tests.sh` Phase 4 does, with `BARBOX_BACKEND_URL`/`BARBOX_TEST_MODE=1` exported). Byte-for-byte repeatable, good for comparing managed CPU/alloc deltas across commits. **Caveat: headless skips rendering — `_Draw`/`QueueRedraw` findings (A3) require a windowed run.**
- **Windowed local scenarios** (used for A1–A3 verification): a scripted "Racing: N laps on the same track" session and a "Carrom: full break + settle" session, launched under `dottrace start` per C1. Keep the scenario descriptions here updated so before/after snapshots are comparable.

### C4 — Backend measurement + profiling (Python)

dotTrace/dotMemory do not apply to Python; the backend gets its own two-part setup:

- **Timing middleware** (this is B0): per-request duration histogram → p50/p95 by route, `X-Process-Time` response header, and slow-query logging. There is currently **no** timing instrumentation anywhere in the backend (no Sentry/Prometheus/OTel) — this is the baseline data source for all B items.
- **py-spy recipes**:

```bash
uv tool install py-spy

# Local dev server — CAUTION: `fastapi dev`/--reload spawns a reloader parent + worker child.
# A naive PID grab profiles the reloader and produces an empty flamegraph.
py-spy record --subprocesses -p <parent-pid> -d 60 -o profiling/backend.svg
py-spy top --subprocesses -p <parent-pid>            # live view

# Remote (build box / Fly): via profile-remote.sh (C2a), which resolves the unit MainPID.
```

Load source for baselines: the Hurl suite (`sh scripts/test.sh`) or a `hurl --repeat` loop against the leaderboard/credit endpoints listed in Part B.

---

## Part A — Client Game Optimizations

Context: `codebase-audit-and-simplification-plan.md` Stage 2 already removed the per-physics-frame allocations (UI throttle, static sample offsets, reusable buffers, cached nodes, LINQ out of hot paths). What remains is CPU-cost and hitch work that needs measurement.

### A0 — Instrumentation gap: Carrom per-frame timing (do before optimizing)

**DONE (2026-07-14)** — see Progress Log.

Racing pushes per-frame Stopwatch timings to the debug overlay (`RacingGame.cs:227,537-552` → `DebugPerformanceMonitor.SetMetric`); Carrom pushes only static state metrics (`CarromGameDebug.cs:481-488`). Add aggregate Stopwatch timing around `CarromPiece._PhysicsProcess` (summed across the ~19 pieces + striker) and `CarromPhysicsMonitor._PhysicsProcess`, pushed via the existing `_Core/Scripts/Debug/DebugPerformanceMonitor.cs` `SetMetric` API. Effort: small. Risk: none (debug-build-only path).

### A1 (HIGH) — Racing off-track validation: repeated `Curve2D.GetClosestPoint` scans

**DONE (2026-07-15)** — see Progress Log.

`_Games/RacingGame/Scripts/Logic/RacingTrackValidationSystem.cs:136` — `GetDistanceToTrackCenterLine` calls `_trackCurve.GetClosestPoint(position)`, an O(baked-points) native scan, every frame from `UpdateOffTrackPenalties`. When the car center is off-track, `IsCarCompletelyOffTrack` (`:144`) samples 8 boundary points, each calling `IsOnTrack` → another `GetClosestPoint` — up to ~9 curve scans per frame at speed. The position/rotation cache (`POSITION_CACHE_THRESHOLD_SQ`, `:48`) only helps when nearly stationary.

Fix direction: precompute a spatial structure at track load (baked distance field / grid of nearest-curve-offset, or a segment BVH reusing `LineUtils` primitives) so the per-frame query is O(1)/O(log n). No allocation problem — pure CPU.
Measurement: the existing `Validation` ms debug metric (`RacingGame.cs:537-552`) before/after, plus a C1 Sampling snapshot of the Racing-laps scenario. Effort: medium. Risk: medium (must match current on/off-track semantics exactly — penalty behavior is gameplay-visible).

### A2 (HIGH) — Racing lap-boundary backend saves hitch the main thread

**DONE (2026-07-15)** — see Progress Log.

`_Games/RacingGame/Scripts/RacingGame.cs:581` (`_ = SaveBestLapTime(...)` on every lap) and `:604` (`_ = SaveGlobalHighScore(...)`), with `GetPlayerLapTimes(playerId).ToArray()` at `:596`. The async continuations — JSON serialize, response parse, and the transport's poll-yield loops — all run on the main thread interleaved with frames, right at lap/race boundaries where a hitch is most visible.

Fix direction: decouple save timing from lap events (queue results, flush at race end or during low-intensity moments), and/or move serialization off the hot moment. Transport-level fixes belong to A4/WS1 — this item is only about the *call sites*.
Measurement: Timeline snapshot (C1) of the lap-crossing moment; frametime graph from C0b during laps. Effort: small-medium. Risk: low (persistence semantics must survive an early quit).

### A3 (MED) — Racing HUD arc renderer: unconditional per-frame redraw + text allocations

**DONE (2026-07-14)** — see Progress Log.

`_Games/RacingGame/Scripts/Visual/RacingHUDArcRenderer.cs:174` `_Process` calls `QueueRedraw()` every frame while `ShouldRender` (`:198-201`); `_Draw` (`:211`) renders multiple 32-segment `DrawArc` + `DrawLine` calls and allocates `speedInt.ToString()` (`:285`) plus a `_cachedFont.GetStringSize(...)` call every frame (~60/s).

Fix direction: dirty-flag the redraw (state-diff like `RacingUIState.StateEquals` already does for UI), cache the speed string per integer value (speed changes far less than 60×/s as an int), cache `GetStringSize` per cached string. **Requires a windowed run to verify (C3 caveat).**
Measurement: alloc-rate via `dotnet-counters` or Timeline GC view; frametime before/after. Effort: small. Risk: low.

### A4 (MED, measure-first, cross-ref roadmap WS1) — Transport-level HTTP poll bursts

`_Core/Scripts/_Utils/HttpClientExtensions.cs:35-65` `PollUntilAsync`: every `BackendClient` request runs bursts of 10 synchronous `client.Poll()` calls, then a frame-aligned `StaticDelayAsync(0.01)` — at 60fps the effective granularity is one frame (~16.6ms), and every HTTP request holds main-thread continuations across multiple frames. This affects **all** games' backend traffic, not just Racing (which is why fixing A2's call sites doesn't retire this).

This is a *measure-then-decide* item, not a prescribed fix: Godot's `HttpClient` is poll-based by design, and the fix options (poll once per `_Process` frame instead of burst+delay; switch `BackendClient` to `System.Net.Http.HttpClient`; background-thread polling) are architectural decisions that belong in **WS1's BackendClient work** (`architecture-roadmap.md`). Cheap hypothesis to test first with C1 tooling: the burst of 10 buys almost nothing between frames — state changes arrive with the socket, not with repeated `Poll()` in a tight loop.
Measurement: per-request main-thread cost via a Timeline snapshot around a burst of API calls (e.g., the Racing save flow or session-event traffic). Effort: measurement small; fix sized inside WS1. Risk: N/A here.

### A5 (LOW) — Cleanup batch (one commit)

**DONE (2026-07-14)**, all 5 items — see Progress Log for the
`RacingUIManager.cs` item's no-action rationale.

Small items, batched; each verified by build + tests, no individual measurement gate:

- `RacingGame.cs:549-551` — per-frame string interpolation (3 strings/frame) in debug builds inside `_Process`; also a `Stopwatch.Restart` each frame. Debug-only; make the metric push conditional on the overlay being visible, or throttle it.
- `_Games/RacingGame/Scripts/Logic/RacingZoneManager.cs:432` — `zones.ToList()` in `GetZonesAtPosition`, allocating per zone re-check (gated by `ZONE_CHECK_DISTANCE_SQ`, so not strictly per-frame).
- `_Games/RacingGame/Scripts/Visual/RacingUIManager.cs:511` (`$"{timeLabel}: {timeDisplay:F1}s"`) and `:1356` (`FormatLapTime` interpolation) — string allocs per UI update, bounded by the 3rd-frame throttle + state-diff skip. Anchors re-verified 2026-07-14.
- `_Games/RacingGame/Scripts/Visual/RacingVisualFeedbackRenderer.cs:244` — `RemoveAt(0)` on the trail list is O(n) shift (bounded by `MaxTrailPoints`; consider ring buffer if touched). **Correctness sub-bullet:** `:251` `CleanupExpiredTrailPoints` iterates from the newest point and `break`s immediately, so it never actually trims expired points — cleanup silently relies on the `MaxTrailPoints` cap. Fix the logic even though the perf impact is negligible.
- `_Games/CarromGame/Scripts/Logic/CarromModeManagerBase.cs:430` — empty `_Process` override still registered as a per-frame callback (settlement became synchronous); remove it and its comment.

### Carrom health summary (no action)

Audited 2026-07-14 and found well optimized — do not re-audit without a new symptom:
- Custom powder friction in `CarromPiece._PhysicsProcess` (`:371`) is pure value-type math with a friction-coefficient cache; trail rendering uses a preallocated circular `TrailPoint[]` buffer (`:69`, `:750`).
- Drawing is event-driven via batched `QueueRedraw` dirty flags, not per-frame (`CarromInputController.cs:171-179`, `CarromPiece.cs:475`); trajectory prediction writes into class-level reusable buffers.
- `CarromPhysicsMonitor` polls the factory's cached `IReadOnlyList` with a `LengthSquared` fast reject (`CarromPhysicsMonitor.cs:99`, `CarromPieceFactory.cs:26,206`).
- Pockets use signal-based enter/exit, not polling (`CarromPocket.cs:152,162`).
- LINQ exists only in non-hot event/UI handlers (`CarromGame.cs:1298,1531,1787,2347`; `CarromModeManagerBase.cs:687`) — no action.
- Mining ticks per-frame but is trivial (`MiningEngine.cs:22`); Nines is turn-based. Neither needs work.

---

## Part B — Backend Latency

Context: the deployment is one Fly `shared-cpu-1x`/512MB machine, SQLite on a volume, **single uvicorn worker** (`Dockerfile:32`, no `--workers`), NullPool (connection per session), and in-process locks (`credits/service.py:22`) that assume a single process. That stack is a known horizontal-scaling ceiling — recorded here as context, **not** a work item; don't attack it piecemeal.

All paths below are under `BarBoxServices/src/bxctl/`.

### B0 (do first) — Timing middleware baseline

**DONE (2026-07-15)** — see Progress Log.

Add the C4 timing middleware: per-route p50/p95, `X-Process-Time` header, slow-query logging. No baseline data exists today; every other B item's before/after depends on this. Load with the Hurl suite. Effort: small. Risk: none.

### B0b — request_id bind-discard bug

**DONE (2026-07-15)** — see Progress Log.

`app/main.py (add_request_id_middleware)` — `add_request_id_middleware` calls `logger.bind(request_id=...)` and discards the result, so per-request log correlation is never actually attached. Fix (use `structlog.contextvars` or pass the bound logger). Kept separate from B0 so the "baselines exist" gate stays unambiguous. Effort: trivial. Risk: none.

### B1 (HIGH) — Index `box_session_event`

**DONE (2026-07-15)** — see Progress Log.

The baseline migration (`alembic/versions/abececed02c7_baseline_schema.py:142-154`) creates `box_session_event` with **only a PK on `id`** — no index on `session_id`, `type`, or `timestamp`. The table holds every score/credit/machine event ever and every hot query filters on `bse.type` and joins `session_id`, so leaderboards, credit balances, and machine-credit pots all full-scan it with per-row `json_extract`. Affected queries: `games/common.py:217-233`, `games/carrom/service.py:31-111`, `players/service.py (get_player_credits)`, `credits/service.py (get_balance)`.

Fix: alembic migration adding indexes on `(type)` and `(session_id)` (or composite `(type, session_id)`); optionally SQLite generated columns + indexes for `payload.$.box_id` / `$.game_tag` / `$.track_id` if the type-index alone doesn't get leaderboards where they need to be.
Measurement: B0 p95 per route + `EXPLAIN QUERY PLAN` before/after. Effort: small (plain indexes) to medium (generated columns). Risk: low (additive migration; verify write-path cost is acceptable — this table is also the hot write path).

### B2 (HIGH) — Carrom leaderboard query

**DONE (2026-07-15)** — see Progress Log.

`games/carrom/service.py:31-111` — the heaviest query in the codebase: a CTE that reconstructs a hyphenated UUID with 5 `SUBSTR` concatenations per row, then a dynamic-path `json_extract(payload, '$.scores."' || uuid || '"')` evaluated twice (SELECT + GROUP BY), over a full scan.

Fix direction: after B1, restructure to avoid the per-row UUID reconstruction (store the join key in a stable form, or extract once in a CTE column and reuse). Consider whether the shared builder (B5) can absorb it once the builder supports descending metrics.
Measurement: B0 p95 on `GET /game/carrom/leaderboard` + `EXPLAIN QUERY PLAN`. Effort: medium. Risk: medium (results must match exactly; Hurl leaderboard tests cover this).

### B3 (MED) — Machine-credit consume does 4 aggregate scans

**DONE (2026-07-15)** — see Progress Log.

`credits/service.py (consume)` — `consume` calls `get_balance` twice (balance pre-check, then return value), and each call runs two full-scan aggregates (`:30-100`) → 4 scans per consume, serialized under the per-pot `asyncio.Lock`. Fix: compute once, reuse; ideally a single query for balance+contributions. Measurement: B0 p95 on the consume route. Effort: small. Risk: low.

### B4 (MED) — Session GET loads all events

**DONE (2026-07-15)** — see Progress Log.

`boxes/service.py (get_session)` — `GET /box/session/{id}` uses `joinedload(BoxSession.events)` + `.unique()`, returning one row per event and serializing the entire event history on every poll of a long session. Fix: don't eager-load events for the state poll (separate endpoint/param for history, or cap/paginate). Requires checking what the client actually consumes (`BarBoxApp` ApiPaths callers) — cross-repo path-sync rule applies. Effort: small-medium. Risk: medium (client contract).

### B5 (MED) — Shared leaderboard builder hygiene

**DONE (2026-07-15)** — see Progress Log.

`games/common.py:186-233` `uuid_join_leaderboard_sql`:
- `username_expr` (3-way COALESCE incl. `json_extract`) is repeated verbatim in SELECT and GROUP BY (`:220`, `:230`) — evaluated twice per row.
- `event_type` / `value_json_path` / `extra_select` / `extra_where` are f-string-interpolated (`:217-229`) — internal constants today, so not an injection hole, but it defeats statement caching and is fragile; move to bind params where SQLite allows.
- `ORDER BY metric_value ASC` hardcoded (`:231`) — supporting a direction parameter is a prerequisite for B2 absorbing Carrom's query.
Effort: small. Risk: low (Racing leaderboard Hurl tests as the gate).

### B6 (LOW) — Event-ingest auth does 2 extra SELECTs

**DONE (2026-07-15)** — see Progress Log.

`app/dependencies.py (_get_authenticated_box_by_session)` — `_get_authenticated_box_by_session` issues a session lookup then a box lookup on every event-ingest call, on top of the insert itself. Fix: single joined query. Also consider demoting the per-request `logger.info("box_authenticated...")` (`:154,197,250`) to debug — it fires on every request. Effort: small. Risk: low.

### B7 (LOW, deferred) — Caching + log volume

Leaderboards and balances are recomputed from scratch on every request (only `env.acquire()` is cached anywhere). A short-TTL in-process cache for leaderboard reads is the obvious win **after** B1/B2 land — measure first; the indexes may make it unnecessary. Bundle with a pass demoting hot-path `logger.info` calls (e.g., `box.py:557`, `machine_credits.py:90`) to debug.

---

## Verification

- **Client:** before/after snapshot pairs stored under an untracked local `profiling/` dir (add to `.gitignore` when first used); frame-time and per-system ms deltas via `DebugPerformanceMonitor` metrics (A0 fills the Carrom gap); frametime graphs via the C0b overlay for on-kiosk validation; full test suite (`bash scripts/run-tests.sh`) green.
- **Backend:** per-route p50/p95 from the B0 middleware before/after each B item, driven by the Hurl suite; `EXPLAIN QUERY PLAN` diffs for B1/B2/B5; full `sh scripts/test.sh` green.
- **Tooling items (Part C):** each recipe/script is verified by running it once for real and noting the result (and any needs-verification resolution) in the Progress Log.

---

## Suggested Execution Order

1. **C1** (local JetBrains CLI install + first attach test — resolves the macOS ARM64 needs-verification)
2. **C0** (vendor debug-menu, hand-test the remote-debug recipe)
3. **C3** (pin down the two windowed scenarios)
4. **A0 + B0/B0b** (instrumentation)
5. **C2a** (`profile-remote.sh`)
6. **Baselines**: local scenario snapshots + backend p50/p95 under Hurl load
7. **A1, A2, B1, B2** (the four HIGH items), re-measure after each
8. **A3, B3, B4, B5** by priority; **A4** measured here, fix deferred into WS1
9. **A5, B6** cleanup batches; **B7** only if post-B1/B2 numbers still warrant it
10. Tracy only on demand (C0 deferral criteria)

---

## Appendix — CLI Reference & Open Questions

### Verified commands (2026-07)

| Tool | Install | Key commands |
|---|---|---|
| dotTrace CLI | `dotnet tool install -g JetBrains.dotTrace.GlobalTools` | `dottrace start --profiling-type=Sampling\|Timeline --save-to=x.dtp <exe> [args]`; `dottrace attach <pid> --timeout=Ns --save-to=x.dtp` |
| dotMemory CLI | `dotnet tool install -g JetBrains.dotMemory.Console` | `dotmemory get-snapshot <pid>`; `dotmemory attach <pid>`; `dotmemory start-net-core <exe>` |
| dotnet-counters | `dotnet tool install -g dotnet-counters` | `dotnet-counters monitor -p <pid>` |
| dotnet-trace | `dotnet tool install -g dotnet-trace` | `dotnet-trace collect -p <pid> [--format speedscope]` |
| dotnet-gcdump | `dotnet tool install -g dotnet-gcdump` | `dotnet-gcdump collect -p <pid>` |
| py-spy | `uv tool install py-spy` | `py-spy record --subprocesses -p <pid> -d 60 -o out.svg`; `py-spy top --subprocesses -p <pid>` |

RID-specific standalone packages (e.g. `JetBrains.dotTrace.CommandLineTools.linux-x64`, `JetBrains.dotMemory.Console.macos-arm64`) exist as tar.gz for hosts where global dotnet tools are unwanted.

Platform matrix: macOS/Linux dotTrace = Sampling + Timeline only; CLI report/compare = Windows-only (analyze in Rider); attach = same-user only; py-spy attach = ptrace (yama scope applies), dotTrace/dotnet-trace attach = EventPipe socket (yama does not apply).

dotMemory soak-run triggers (deferred from C3; only relevant once a leak is suspected): `--trigger-timer=30s`, `--trigger-mem-inc=<percent>`, `--trigger-on-activation`, `--trigger-delay=Ns`, plus stdin/`--service-input` control messages for scripted snapshots.

### Needs-verification (resolve at execution time, note results in Progress Log)

- dotTrace attach to the hostfxr-hosted Godot process on macOS ARM64 (expected OK — standard CoreCLR).
- The frontend Godot process name on the build box (for `profile-remote.sh`'s MainPID child-walk).
- Kiosk firewall/Tailscale reachability for the C0a `--remote-debug` editor connection.
- Exact JetBrains tool versions at install time (2026.x line as of writing).
- Headless `tracy-capture` CLI workflow with Godot — only if Tracy is ever adopted.
- dotMemory Unit (automated allocation assertions in tests) under GoDotTest on macOS/Linux — optional spike, historically Windows-oriented; not a dependency of anything above.
