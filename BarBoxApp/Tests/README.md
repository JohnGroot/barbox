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
