# BarBox Comprehensive Testing Suite

Complete end-to-end testing infrastructure for the BarBox game platform, covering backend API integration and Godot C# game testing.

## Table of Contents

- [Overview](#overview)
- [Quick Start](#quick-start)
- [Test Infrastructure](#test-infrastructure)
- [Running Tests](#running-tests)
- [Test Organization](#test-organization)
- [Writing New Tests](#writing-new-tests)
- [Debugging Tests](#debugging-tests)
- [CI/CD Integration](#cicd-integration)

## Overview

The BarBox testing suite uses:
- **GoDotTest** - C# test framework for Godot 4.x
- **Hurl** - HTTP integration testing for backend APIs
- **Real Backend Integration** - Tests use actual backend with isolated test database
- **Accuracy over Speed** - Comprehensive verification with full end-to-end flows

### Design Principles

✅ **Full Integration Testing** - All tests use real backend (no mocking)
✅ **Sequential Execution** - GoDotTest prevents race conditions
✅ **Isolated Test Database** - Separate port (8001) and database (`/tmp/barbox-test.db`)
✅ **Backend State Verification** - Tests verify data persists correctly
✅ **Per-Game Test Suites** - Each game has comprehensive test coverage

## Quick Start

### Prerequisites

1. **Godot 4.4** - Must be available as `godot` command
2. **Hurl** - Install via `brew install hurl` (macOS) or visit [hurl.dev](https://hurl.dev)
3. **Python 3.13+** - For backend services
4. **uv** - Python package manager

### Run All Tests

```bash
# From BarBoxApp directory
sh scripts/run-tests.sh
```

This will:
1. Start isolated test backend (port 8001)
2. Run backend Hurl integration tests
3. Build C# project
4. Run Godot C# tests via GoDotTest
5. Cleanup and report results

### Run Specific Test Types

```bash
# Backend tests only
sh scripts/run-tests.sh backend

# Frontend tests only
sh scripts/run-tests.sh frontend

# Quick tests (build + frontend, skip backend)
sh scripts/run-tests.sh quick
```

## Test Infrastructure

### Directory Structure

```
BarBoxApp/
├── Tests/
│   ├── TestRunner.cs               # GoDotTest entry point
│   ├── TestRunner.tscn             # Test scene
│   ├── Unit/
│   │   └── Services/               # Core service tests
│   │       ├── EventServiceTests.cs
│   │       ├── SessionManagerTests.cs
│   │       └── CreditServiceTests.cs
│   ├── Integration/                # Cross-service integration tests
│   └── Fixtures/                   # Shared test utilities
│       ├── TestConfig.cs
│       ├── TestHelpers.cs
│       └── BackendTestBase.cs
│
├── _Games/MiningGame/Tests/
│   └── MiningGameTests.cs          # Mining-specific tests
│
├── _Games/CarromGame/Tests/
│   └── CarromGameTests.cs          # Carrom-specific tests
│
├── _Games/RacingGame/Tests/
│   └── RacingGameTests.cs          # Racing-specific tests
│
└── scripts/
    └── run-tests.sh                # Main test runner

BarBoxServices/
├── test/
│   └── integration/                # Backend Hurl tests
│       ├── account-operations.hurl
│       ├── credit-operations.hurl
│       └── game-session-lifecycle.hurl
└── scripts/
    └── test-backend.sh             # Backend lifecycle management
```

### Test Backend

The test backend runs on **port 8001** with a separate SQLite database (`/tmp/barbox-test.db`).

**Manage test backend:**

```bash
cd BarBoxServices

# Start test backend
sh scripts/test-backend.sh start

# Check status
sh scripts/test-backend.sh status

# View logs
sh scripts/test-backend.sh logs

# Stop test backend
sh scripts/test-backend.sh stop

# Restart
sh scripts/test-backend.sh restart
```

### Production Backend

**Auto-Start Behavior**: The BarBox app **automatically starts the production backend** if it's not already running.

**Production backend runs on port 8000** with the main application database.

**How Auto-Start Works:**

When you launch the app:
1. BackendManager checks if backend is healthy via HTTP `/alive` endpoint
2. If not healthy, checks port 8000 for stale processes
3. Kills any stale processes found
4. Executes `BarBoxServices/scripts/dev.sh` in background
5. Polls for health check with 10-second timeout
6. If successful, app proceeds normally
7. If failed, shows error UI with retry button

**Backend Lifecycle:**

- **Auto-starts on app launch** if not detected
- **Persists after app shutdown** for faster subsequent launches
- **Stale process cleanup** prevents abandoned processes
- **Retry mechanism** via UI button for transient failures

**Manual Backend Control (Optional):**

You can also start the backend manually:

```bash
cd BarBoxServices

# Start production backend (blocking)
sh scripts/dev.sh

# Alternative: Run in background with logs
uvicorn barbox.main:app --reload --port 8000 > backend.log 2>&1 &
```

**Environment Configuration:**

```bash
export BARBOX_BACKEND_HOST=127.0.0.1  # Default
export BARBOX_BACKEND_PORT=8000        # Default
```

**Auto-Start Failure Handling:**

If backend fails to auto-start, the app displays:

```
╔════════════════════════════════════════════════════════╗
║  Backend Service Not Running                           ║
╠════════════════════════════════════════════════════════╣
║  Backend auto-start failed.                            ║
║                                                        ║
║  Possible causes:                                      ║
║    - Backend script not found                          ║
║    - Port 8000 blocked by firewall                     ║
║    - Python dependencies missing                       ║
║                                                        ║
║  To start manually:                                    ║
║    cd BarBoxServices                                   ║
║    sh scripts/dev.sh                                   ║
║                                                        ║
║  Then restart the app or click Retry Connection       ║
╚════════════════════════════════════════════════════════╝
```

App continues with degraded functionality (credit system, persistence, leaderboards unavailable).

## Running Tests

### All Tests (Recommended for Pre-Commit)

```bash
cd BarBoxApp
sh scripts/run-tests.sh
```

**Expected output:**
```
━━━ BarBox Comprehensive Test Suite ━━━

━━━ Phase 1: Starting Test Backend ━━━
ℹ Starting test backend on port 8001...
✓ Test backend started (PID: 12345)
✓ Test backend is ready

━━━ Phase 2: Running Backend Integration Tests (Hurl) ━━━
✓ Backend integration tests passed

━━━ Phase 3: Building C# Project ━━━
✓ Build successful

━━━ Phase 4: Running Godot C# Tests (GoDotTest) ━━━
✓ Godot C# tests passed

━━━ Phase 5: Cleanup ━━━
✓ Test backend stopped

━━━ All Tests Complete ━━━
✓ Test suite finished successfully!
```

### Run Specific Test Suite

```bash
# Run only Mining game tests
godot --path . --headless --run-tests=MiningGameTests --quit-on-finish

# Run only EventService tests
godot --path . --headless --run-tests=EventServiceTests --quit-on-finish

# Run specific test method
godot --path . --headless --run-tests=EventServiceTests.CreateActivitySession_WithValidParams_ReturnsSessionId --quit-on-finish
```

### Run Backend Tests Only

```bash
cd BarBoxServices
sh scripts/test.sh
```

Or run individual Hurl files:

```bash
hurl --error-format long test/integration/account-operations.hurl
hurl --error-format long test/integration/credit-operations.hurl
```

## Test Organization

### Test Base Classes

**`BackendTestBase`** - For tests requiring backend integration:

```csharp
public class MyServiceTests : BackendTestBase
{
    public MyServiceTests(Node testScene) : base(testScene) { }

    [Test]
    public async void MyTest()
    {
        // TestBoxId, TestPlayerId, TestPlayerUsername available
        // GetEventService(), GetSessionManager(), GetCreditService()
    }
}
```

**Standard `TestClass`** - For tests not needing backend:

```csharp
public class MyUtilityTests : TestClass
{
    public MyUtilityTests(Node testScene) : base(testScene) { }

    [Test]
    public void MyUtilityTest()
    {
        // Simple unit test
    }
}
```

### GoDotTest Lifecycle

```csharp
[SetupAll]    // Runs once before all tests in suite
[Setup]       // Runs before each test
[Test]        // Individual test method
[Cleanup]     // Runs after each test
[CleanupAll]  // Runs once after all tests
[Failure]     // Runs if any test fails
```

## Writing New Tests

### 1. Create Test File

Place tests in appropriate location:
- Core services → `Tests/Unit/Services/`
- Game-specific → `_Games/{GameName}/Tests/`
- Integration → `Tests/Integration/`

### 2. Follow Naming Conventions

```csharp
// File: MyServiceTests.cs
namespace BarBox.Tests.Unit.Services;

public class MyServiceTests : BackendTestBase
{
    // Method naming: Action_Condition_ExpectedResult
    [Test]
    public async void GetBalance_WithValidPlayerId_ReturnsBalance()
    {
        // Test implementation
    }

    [Test]
    public async void SpendCredits_WithInsufficientBalance_ReturnsError()
    {
        // Test implementation
    }
}
```

### 3. Test Structure (AAA Pattern)

```csharp
[Test]
public async void CreateSession_WithValidParams_ReturnsSessionId()
{
    // Arrange
    var gameTag = "mining";
    var boxId = TestHelpers.GenerateTestBoxId();
    var playerId = TestHelpers.GenerateTestPlayerId();

    // Act
    var result = await _eventService.CreateActivitySessionAsync(
        boxId, playerId, gameTag);

    // Assert
    if (!result.IsSuccess)
    {
        TestHelpers.LogTestError($"Expected success but got: {result.Error}");
    }
    else
    {
        TestHelpers.LogTestInfo($"Session created: {result.Value}");
    }

    // Cleanup
    if (result.IsSuccess)
    {
        await _eventService.CloseActivitySessionAsync(result.Value);
    }
}
```

### 4. Use Test Helpers

```csharp
// Generate unique test data
var boxId = TestHelpers.GenerateTestBoxId();
var playerId = TestHelpers.GenerateTestPlayerId();
var username = TestHelpers.GenerateTestUsername();
var phone = TestHelpers.GenerateTestPhoneNumber();

// Check backend health
var isHealthy = await TestHelpers.IsTestBackendHealthyAsync();

// Logging
TestHelpers.LogTestInfo("Test started");
TestHelpers.LogTestWarning("Unexpected condition");
TestHelpers.LogTestError("Test failed");

// Wait for condition
var success = await TestHelpers.WaitForConditionAsync(
    () => _session != null,
    timeoutSeconds: 5.0f);
```

### 5. Backend Hurl Tests

Create `.hurl` files in `BarBoxServices/test/integration/`:

```hurl
# Test: Create account successfully
POST http://127.0.0.1:8001/player/
{
  "id": "{{newUuid}}",
  "tag": "testuser",
  "origin_id": "{{box-id}}"
}
HTTP 201
[Captures]
player-id: jsonpath "$['id']"
[Asserts]
jsonpath "$.tag" == "testuser"

# Test: Duplicate account fails
POST http://127.0.0.1:8001/player/
{
  "id": "{{newUuid}}",
  "tag": "testuser",
  "origin_id": "{{box-id}}"
}
HTTP 400
```

### 6. Testing Failure Scenarios

The test suite includes infrastructure for simulating service failures to test error handling and recovery. This allows tests to replicate production bugs where backend services fail.

#### Failure Test Base Class

Use `FailureScenarioTestBase` to simulate service failures:

```csharp
using BarBox.Tests.Fixtures;

namespace BarBox.Tests.FailureScenarios;

public class MyFailureTests : FailureScenarioTestBase
{
    public MyFailureTests(Node testScene) : base(testScene) {}

    [Test]
    public async void MyOperation_WhenBackendNotReady_FailsGracefully()
    {
        // Check if failure simulation is available (requires DEBUG build)
        if (!CanSimulateFailures())
        {
            TestHelpers.LogTestInfo("Skipping - requires DEBUG build");
            return;
        }

        // Arrange
        var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");

        try
        {
            // Simulate backend failure
            SimulateEventServiceNotReady();

            // Verify simulation worked
            if (IsEventServiceNotReady())
            {
                TestHelpers.LogTestInfo("EventService forced to NOT READY state");
            }

            // Act - attempt operation that requires backend
            var result = await _myService.DoOperation();

            // Assert - must fail gracefully
            if (result.IsSuccess)
            {
                TestHelpers.LogTestError("REGRESSION: Operation succeeded without backend!");
            }
            else
            {
                TestHelpers.LogTestInfo($"✓ Correctly failed: {result.ErrorMessage}");
            }
        }
        finally
        {
            // ALWAYS restore service state
            RestoreEventServiceReady();
            await _sessionManager.LogoutUserAsync(TestPlayerPhone);
        }
    }
}
```

#### Failure Simulation Methods

```csharp
// Force EventService to not ready state (backend unavailable)
SimulateEventServiceNotReady();

// Restore EventService to ready state
RestoreEventServiceReady();

// Check if EventService is currently not ready
bool notReady = IsEventServiceNotReady();

// Check if failure simulation is supported (DEBUG build required)
bool canSimulate = CanSimulateFailures();
```

#### Using Test Helpers for Failure Simulation

```csharp
// Static helpers (can be used in any test)
if (TestHelpers.CanSimulateFailures())
{
    TestHelpers.ForceEventServiceNotReady();

    // Run test...

    TestHelpers.RestoreEventServiceReady();
}
```

#### Key Principles for Failure Tests

1. **Always use try-finally** - Restore service state even if test fails
2. **Check CanSimulateFailures()** - Gracefully skip on non-DEBUG builds
3. **Verify simulation worked** - Check IsEventServiceNotReady() before testing
4. **Test state transitions** - Backend fails → operation fails → backend recovers → operation succeeds

#### Example: Testing Credit Purchase Failure

```csharp
[Test]
public async void PurchaseCredits_BackendDown_FailsWithDiagnostics()
{
    if (!CanSimulateFailures()) return;

    var loginResult = await _sessionManager.LoginUserByPhoneAsync(TestPlayerPhone, "1234");
    var initialCredits = _sessionManager.GetUserSession(TestPlayerPhone).Credits;

    try
    {
        // Simulate backend failure
        SimulateEventServiceNotReady();

        // Act - payment processes but credits can't be added
        var result = await _paymentService.PurchaseCreditsAsync(
            TestPlayerPhone, new CreditPack(25, 25.00m));

        // Assert
        if (result.IsSuccess)
        {
            TestHelpers.LogTestError("CRITICAL: Payment succeeded without backend!");
        }

        // Verify credits unchanged
        var finalCredits = _sessionManager.GetUserSession(TestPlayerPhone).Credits;
        if (finalCredits != initialCredits)
        {
            TestHelpers.LogTestError($"Credits changed: {initialCredits} → {finalCredits}");
        }

        // Verify error has diagnostics
        if (!result.ErrorMessage.Contains("EventService") ||
            !result.ErrorMessage.Contains("ready"))
        {
            TestHelpers.LogTestError($"Poor error message: {result.ErrorMessage}");
        }
    }
    finally
    {
        RestoreEventServiceReady();
        await _sessionManager.LogoutUserAsync(TestPlayerPhone);
    }
}
```

#### Build Requirements

**Failure simulation requires DEBUG build:**

```bash
# Build in DEBUG mode (default for development)
godot --path . --headless --build-solutions

# Run tests with failure simulation
sh scripts/run-tests.sh
```

**Production builds (RELEASE) skip failure tests automatically.**

#### Test Directory Structure

```
Tests/
├── FailureScenarios/         # NEW: Dedicated failure tests
│   ├── PaymentServiceFailureTests.cs
│   └── ServiceFailureRecoveryTests.cs
├── Unit/
│   └── Services/             # Can include failure tests
├── Integration/
└── Fixtures/
    ├── FailureScenarioTestBase.cs  # NEW: Failure test base
    └── TestHelpers.cs               # NEW: Failure simulation helpers
```

### DEBUG-Only Test Infrastructure

The test infrastructure uses compile-time conditional compilation to provide test hooks that are **completely removed** from production builds.

#### Pattern: Conditional Compilation

```csharp
// EventService.cs (and other services that need test hooks)
#if DEBUG
    private bool _testReadyOverride = false;
    private bool _useTestReadyOverride = false;

    /// <summary>
    /// FOR TESTING ONLY: Override ready state to simulate backend failures.
    /// This allows tests to verify error handling when EventService is not available.
    /// </summary>
    public void SetReadyStateForTesting(bool ready)
    {
        _testReadyOverride = ready;
        _useTestReadyOverride = true;
        LogInfo($"[TEST] Ready state override set to: {ready}");
    }

    public void ClearReadyStateOverride()
    {
        _useTestReadyOverride = false;
        LogInfo("[TEST] Ready state override cleared");
    }

    // Shadow base property in DEBUG builds only
    public new bool IsReady => _useTestReadyOverride ? _testReadyOverride : base.IsReady;
#endif
```

#### Why This Works

**Zero Production Cost**
- Entire test infrastructure removed in RELEASE builds via `#if DEBUG`
- No runtime performance impact
- No additional memory overhead
- Methods don't exist in production (compile-time removal)

**Type-Safe**
- Compiler enforces correct usage
- IntelliSense shows test methods only in DEBUG configuration
- Build fails if test code tries to use hooks in RELEASE mode

**Observable Behavior**
- Test hooks log their state changes
- Makes test failures easier to diagnose
- Shows in test output when state is being manipulated

**Isolated Impact**
- Only affects specific services that need test hooks
- Doesn't require changes to base classes
- Doesn't affect other services in the autoload system

#### Using Test Infrastructure

**In Test Base Classes (FailureScenarioTestBase.cs)**:
```csharp
protected void SimulateEventServiceNotReady()
{
#if DEBUG
    if (_eventService == null)
    {
        TestHelpers.LogTestWarning("EventService not found");
        return;
    }

    _eventService.SetReadyStateForTesting(false);
    TestHelpers.LogTestInfo("EventService forced to NOT READY state");
#else
    TestHelpers.LogTestWarning("Failure simulation requires DEBUG build");
#endif
}

protected void RestoreEventServiceReady()
{
#if DEBUG
    if (_eventService != null && _eventServiceStateModified)
    {
        _eventService.SetReadyStateForTesting(true);
        TestHelpers.LogTestInfo("EventService restored to READY state");
    }
#endif
}
```

**In Test Methods**:
```csharp
[Test]
public async Task PurchaseCredits_BackendNotReady_MustFailGracefully()
{
    try
    {
        // Simulate backend failure
        SimulateEventServiceNotReady();

        // Act - attempt purchase
        var result = await _paymentService.PurchaseCreditsAsync(playerId, creditPack);

        // Assert - must fail gracefully
        result.IsSuccess.ShouldBeFalse("Purchase must fail without backend");
        result.ErrorMessage.ShouldContain("EventService", "Error should include diagnostics");
    }
    finally
    {
        // Always restore state
        RestoreEventServiceReady();
    }
}
```

#### Property Shadowing Mechanics

The `new` keyword shadows the base `IsReady` property in DEBUG builds:

```csharp
// When accessed through EventService type
EventService service = EventService.GetInstance();
service.IsReady  // Uses EventService.IsReady (shadowed, test-aware)

// When accessed through AutoloadBase type
AutoloadBase service = EventService.GetInstance();
service.IsReady  // Uses AutoloadBase.IsReady (original, not test-aware)
```

**Our code maintains proper typing**, so shadowing works correctly:
```csharp
// PaymentService.cs - correct pattern
var eventService = EventService.GetInstance();  // Returns EventService type
if (eventService.IsReady)  // Accesses shadowed property ✓
```

#### Best Practices

1. **Always Restore State**: Use `try/finally` to ensure state restoration
2. **Check Build Configuration**: Test infrastructure only exists in DEBUG builds
3. **Log State Changes**: Use `LogTestInfo` to track state manipulation
4. **Isolate Test Hooks**: Only add to services that need failure simulation
5. **Document Intent**: XML comments explain test-only purpose

#### When NOT to Use

- ❌ **Production code paths** - Never use test hooks in production logic
- ❌ **RELEASE builds** - Test infrastructure doesn't compile
- ❌ **Performance testing** - State override may affect timing
- ❌ **Integration testing** - Use real backend state when possible

#### Alternatives Considered

**Virtual Properties** (Rejected)
```csharp
// Would affect ALL services, not just EventService
public virtual bool IsReady => _state == ServiceState.Ready;
```
- Impact: Runtime cost for all services
- Risk: Could break other autoloads

**Interface Mocking** (Rejected)
```csharp
public interface IEventService { bool IsReady { get; } }
```
- Impact: Major refactoring required
- Complexity: Needs dependency injection
- Godot: Autoload pattern incompatible

**Reflection** (Rejected)
```csharp
typeof(EventService).GetField("_state").SetValue(...)
```
- Impact: Fragile, breaks with refactoring
- Performance: Reflection overhead
- Safety: No compile-time checks

## Debugging Tests

### Enable Verbose Output

```csharp
// In test file
TestHelpers.LogTestInfo($"Debug info: {variable}");
```

### Run Single Test with Godot Editor

1. Open Godot editor
2. Run with `--run-tests` flag:
   ```bash
   godot --path . --run-tests=MyTestClass.MyTestMethod
   ```
3. Check Output panel for test logs

### Debug in Rider/VS Code

**Rider:**
1. Set breakpoint in test method
2. Create Run Configuration:
   - Program: `godot`
   - Arguments: `--path . --run-tests=MyTestClass`
3. Debug

**VS Code:**
```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Debug Tests",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "/path/to/godot",
      "args": ["--path", ".", "--run-tests"],
      "cwd": "${workspaceFolder}/BarBoxApp"
    }
  ]
}
```

### View Backend Logs

```bash
# Check test backend logs
cd BarBoxServices
sh scripts/test-backend.sh logs

# Or tail live:
tail -f /tmp/barbox-test-backend.log
```

## CI/CD Integration

### GitHub Actions

```yaml
name: Run Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: macos-latest

    steps:
      - uses: actions/checkout@v3

      - name: Install dependencies
        run: |
          brew install godot hurl
          pip install uv

      - name: Run tests
        run: |
          cd BarBoxApp
          sh scripts/run-tests.sh
```

### Local Pre-Commit Hook

```bash
# .git/hooks/pre-commit
#!/bin/bash
cd BarBoxApp
sh scripts/run-tests.sh || exit 1
```

## Test Coverage

### Current Test Coverage

**Backend (Hurl):**
- ✅ Account creation (success, duplicate error)
- ✅ Login validation (valid, wrong credentials)
- ✅ Credit operations (grant, spend, check balance)
- ✅ Session lifecycle (enter game, emit events, exit game)

**Core Services (GoDotTest):**
- ✅ EventService (session creation, event emission)
- ✅ SessionManager (login, logout, multi-user)
- ✅ CreditService (balance query, spend, add, cache)

**Mining Game:**
- ✅ Inventory loading
- ✅ Upgrade levels
- ✅ Mining tick time
- ✅ Gem extraction
- ✅ Upgrade purchase
- ✅ Credit deposits

**Carrom Game:**
- ✅ Multiplayer session creation
- ✅ Scoring for multiple players
- ✅ Game completion
- ✅ Credit transfers
- ✅ Leaderboard updates

**Racing Game:**
- ✅ Race session start
- ✅ Checkpoint recording
- ✅ Lap completion
- ✅ Race finish
- ✅ Leaderboard queries

## Troubleshooting

### Test Backend Won't Start

```bash
# Check if port 8001 is in use
lsof -i :8001

# Kill existing process
kill $(lsof -t -i:8001)

# Restart test backend
cd BarBoxServices
sh scripts/test-backend.sh restart
```

### Godot Command Not Found

```bash
# Add Godot to PATH (macOS)
export PATH="/Applications/Godot.app/Contents/MacOS:$PATH"

# Or use full path in run-tests.sh
/Applications/Godot.app/Contents/MacOS/Godot --path . --run-tests
```

### Tests Fail with "Backend Not Available"

1. Ensure test backend is running: `sh BarBoxServices/scripts/test-backend.sh status`
2. Check backend health: `curl http://127.0.0.1:8001/alive`
3. View backend logs: `sh BarBoxServices/scripts/test-backend.sh logs`

### NuGet Package Issues

```bash
# Restore packages
cd BarBoxApp
dotnet restore

# Verify GoDotTest is installed
dotnet list package | grep GoDotTest
```

## Best Practices

### ✅ DO

- Write descriptive test names explaining what's being tested
- Clean up test resources in `[Cleanup]` or `[CleanupAll]`
- Use `TestHelpers` for logging and common operations
- Test both success and error cases
- Verify backend state after operations
- Use unique test data (GUIDs, random values)

### ❌ DON'T

- Hardcode test data that could conflict between runs
- Share state between tests (use `[Setup]` for fresh state)
- Skip cleanup - always close sessions
- Use production backend port (8000) for tests
- Commit test database files

## Resources

- [GoDotTest Documentation](https://github.com/chickensoft-games/GoDotTest)
- [Hurl Documentation](https://hurl.dev)
- [Godot C# Documentation](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/)
- [BarBox Architecture Docs](../CLAUDE.md)

## Contributing

When adding new features:

1. Write tests FIRST (TDD approach)
2. Ensure all existing tests still pass
3. Add new test cases for new functionality
4. Update this README if adding new test patterns
5. Run full test suite before committing

---

**Questions?** Check the main BarBox documentation or consult the test examples in this directory.
