# BarBox Testing Standards

This document defines testing standards and patterns for the BarBox test suite.

## Table of Contents
- [AAA Pattern Standard](#aaa-pattern-standard)
- [Test Categorization](#test-categorization)
- [Service Discovery](#service-discovery)
- [Test Data Management](#test-data-management)
- [Common Patterns](#common-patterns)

## AAA Pattern Standard

All tests MUST follow the Arrange-Act-Assert (AAA) pattern with clear section comments.

### Perfect AAA Example

```csharp
[Test]
public async Task MethodName_Condition_ExpectedOutcome()
{
	// Arrange - Set up test preconditions
	var servicesReady = await WaitForServicesReadyAsync();
	if (!servicesReady)
	{
		TestHelpers.LogTestWarning("Services not ready - skipping test");
		return;
	}

	var testData = CreateTestData();
	var expectedResult = 42;

	// Act - Execute the method under test
	var result = await _service.PerformOperationAsync(testData);

	// Assert - Verify outcomes
	result.IsSuccess(out var value).ShouldBeTrue("Operation should succeed");
	value.ShouldBe(expectedResult, "Result should match expected value");

	// Cleanup - Only if not handled by [Cleanup] attribute
	await CleanupTestDataAsync();
}
```

### AAA Section Rules

#### Arrange Section
- **Purpose**: Set up all test preconditions
- **Includes**:
  - Service readiness checks
  - Test data creation
  - Mock configuration
  - Expected value definitions
- **Early Returns**: Service availability checks with early return are acceptable here

```csharp
// Arrange
var servicesReady = await WaitForServicesReadyAsync();
if (!servicesReady)
{
	TestHelpers.LogTestWarning("Services not ready - skipping test");
	return; // Early return for missing prerequisites
}

var playerId = TestHelpers.GenerateTestPlayerId();
var expectedCredits = 100;
```

#### Act Section
- **Purpose**: Execute the single operation being tested
- **Rules**:
  - Should be ONE logical operation (may span multiple lines)
  - No assertions in this section
  - No early returns
  - Capture all results needed for assertions

```csharp
// Act
var result = await _creditService.AddAsync(playerId, 50, "Test credit");
```

#### Assert Section
- **Purpose**: Verify all expected outcomes
- **Rules**:
  - Group related assertions together
  - Use descriptive assertion messages
  - Test both success cases and side effects
  - Check state changes if applicable

```csharp
// Assert
result.IsSuccess(out var credits).ShouldBeTrue("Credit addition should succeed");
credits.ShouldBe(expectedCredits, "Credits should match expected value");

var balance = await _creditService.GetBalanceAsync(playerId);
balance.IsSuccess(out var actualBalance).ShouldBeTrue();
actualBalance.ShouldBe(expectedCredits, "Balance should reflect added credits");
```

#### Cleanup Section
- **Purpose**: Resource cleanup not handled by [Cleanup] attribute
- **When to Use**:
  - Test-specific cleanup not appropriate for all tests
  - Conditional cleanup based on test path
- **Prefer**: Use [Cleanup] attribute when possible

```csharp
// Cleanup
if (sessionCreated)
{
	await _sessionManager.LogoutUserAsync(TestPlayerPhone);
}
```

### Anti-Patterns to Avoid

#### ❌ Mixed Act and Assert
```csharp
// BAD: Assertions mixed with actions
var result = await _service.DoSomethingAsync();
result.IsSuccess(out var _).ShouldBeTrue(); // Assertion here
var nextResult = await _service.DoAnotherThingAsync(); // More actions
nextResult.ShouldNotBeNull(); // More assertions
```

#### ❌ Multiple Actions Without Clear Separation
```csharp
// BAD: Multiple operations without clear Act section
var login = await _sessionManager.LoginAsync(phone);
login.ShouldNotBeNull();
var credits = await _creditService.GetBalanceAsync(playerId);
credits.ShouldBe(100);
```

#### ✅ Correct: Sequential Actions as Separate Tests
```csharp
// GOOD: Each test focuses on one operation
[Test]
public async Task Login_WithValidPhone_CreatesSession()
{
	// Arrange
	var phone = TestPlayerPhone;

	// Act
	var result = await _sessionManager.LoginAsync(phone);

	// Assert
	result.IsSuccess(out var session).ShouldBeTrue();
	session.ShouldNotBeNull();
}

[Test]
public async Task GetBalance_AfterLogin_ReturnsInitialBalance()
{
	// Arrange
	await _sessionManager.LoginAsync(TestPlayerPhone);
	var playerId = EventService.GetPlayerIdFromPhone(TestPlayerPhone);

	// Act
	var result = await _creditService.GetBalanceAsync(playerId);

	// Assert
	result.IsSuccess(out var balance).ShouldBeTrue();
	balance.ShouldBe(100);
}
```

## Test Categorization

Use `[Category]` attributes to organize tests by type and purpose.

### Standard Categories

```csharp
[Category("Unit")] // Fast, isolated, no external dependencies
[Category("Integration")] // Tests multiple components together
[Category("Slow")] // Tests that take >1 second
[Category("Backend")] // Requires test backend running
[Category("FailureScenario")] // Tests error handling and edge cases
```

### Example Usage

```csharp
[Test]
[Category("Integration")]
[Category("Backend")]
public async Task PlayerRegistration_WithBackend_PersistsToDatabase()
{
	// Integration test requiring backend
}

[Test]
[Category("Unit")]
public void CalculateScore_WithValidInput_ReturnsExpectedValue()
{
	// Fast unit test, no async, no dependencies
}
```

### Benefits
- Run specific test categories: `dotnet test --filter Category=Unit`
- Identify slow tests: `--filter Category!=Slow`
- Skip backend tests in CI: `--filter Category!=Backend`

## Service Discovery

All service access uses the cached service discovery pattern.

### Pattern Overview
```csharp
// Get service reference (cached, validated)
var eventService = GetEventService();

// Always null-check (services may not exist in some scenarios)
if (eventService == null)
{
	TestHelpers.LogTestWarning("EventService not available - skipping test");
	return;
}

// Use service
var result = await eventService.CreateActivitySessionAsync(...);
```

### Available Service Getters
- `GetEventService()` - Backend communication and sessions
- `GetSessionManager()` - User authentication and sessions
- `GetCreditService()` - Credit balance and transactions
- `GetPaymentService()` - Payment processing
- `GetBackendManager()` - Backend process management

### Service Discovery Documentation

See `BackendTestBase.cs` (lines 118-143) for complete service discovery pattern documentation:
- Cache validation with `GodotObject.IsInstanceValid()`
- Lazy initialization on first use
- Automatic cleanup in [Cleanup] methods

## Test Data Management

### Deterministic Test Data

Use deterministic generation for reproducible tests:

```csharp
// Deterministic from test name (same every run)
var playerId = TestHelpers.GenerateDeterministicGuid(nameof(MyTest));
var phoneNumber = TestHelpers.GenerateTestPhoneNumber(nameof(MyTest).GetHashCode());

// Random generation (different each run) - use sparingly
var boxId = TestHelpers.GenerateTestBoxId();
var username = TestHelpers.GenerateTestUsername();
```

### Standard Test Properties

`BackendTestBase` provides these for each test:

```csharp
protected Guid TestBoxId { get; } // Unique per test
protected Guid TestPlayerId { get; } // Unique per test
protected string TestPlayerUsername { get; } // Max 7 chars
protected string TestPlayerPhone { get; } // 555-XXX-XXXX format
```

### Test Isolation

Each test runs with fresh identifiers:
- `[Setup]` generates new test IDs
- `[Cleanup]` clears cached service references
- Ensures no test pollution

## Common Patterns

### Waiting for Services

```csharp
// Wait for all critical services
var servicesReady = await WaitForServicesReadyAsync();
if (!servicesReady)
{
	TestHelpers.LogTestWarning("Services not ready - skipping test");
	return;
}

// Wait for specific service
var backendReady = await TestHelpers.WaitForBackendReadyAsync();
var eventServiceReady = await TestHelpers.WaitForEventServiceReadyAsync();
```

### Backend Health Checks

```csharp
// Check if test backend is running
var isHealthy = await TestHelpers.IsTestBackendHealthyAsync();
if (!isHealthy)
{
	TestHelpers.LogTestWarning("Test backend not available");
	return;
}
```

### Result Pattern Assertions

```csharp
// Success case
var result = await _service.DoSomethingAsync();
result.IsSuccess(out var value).ShouldBeTrue("Operation should succeed");
value.ShouldNotBeNull();

// Failure case
var result = await _service.DoInvalidOperationAsync();
result.IsFailure(out var error).ShouldBeTrue("Invalid operation should fail");
error.Message.ShouldContain("expected error");
```

### Async Timing

```csharp
// Use frame-aware delays (respects game pause)
await AutoloadBase.StaticDelayAsync(1.0f);

// Use WaitForConditionAsync for polling
var success = await TestHelpers.WaitForConditionAsync(
	() => _service.IsReady,
	timeoutSeconds: 10.0f);
success.ShouldBeTrue("Service should become ready");
```

### Test Logging

```csharp
// Info logging
TestHelpers.LogTestInfo("Starting player registration test");

// Warning logging
TestHelpers.LogTestWarning("Backend connection slow - may timeout");

// Error logging
TestHelpers.LogTestError("Critical service not found!");
```

## Test Fixture Hierarchy

```
TestClass (GoDotTest base)
  └─ BackendTestBase
      └─ FailureScenarioTestBase
```

### BackendTestBase
- Base for all integration tests
- Provides service discovery
- Manages test identifiers
- Backend health checking

### FailureScenarioTestBase
- Extends BackendTestBase
- Adds failure simulation
- Service state manipulation (DEBUG only)
- State restoration in cleanup

## Configuration

### TestConfig.cs Constants

Use centralized constants instead of magic numbers:

```csharp
TestConfig.DefaultTimeoutSeconds // 30s - standard timeout
TestConfig.ExtendedTimeoutSeconds // 30.0f - slow operations
TestConfig.FastPollDelaySeconds // 0.05f - quick polling (50ms)
TestConfig.BackendPollDelaySeconds // 0.1f - backend checks
TestConfig.MaxBackendReadinessAttempts // 30 - connection retries
TestConfig.MaxConnectionRetries // 50 - HTTP retry count
TestConfig.TestBackendPort // 8001 - test backend port
TestConfig.TestDatabasePath // /tmp/barbox-test.db
```

## Running Tests

### Command Line

```bash
# Run all tests
sh scripts/run-tests.sh

# Run only frontend (Godot) tests
sh scripts/run-tests.sh frontend

# Run only backend (Hurl) tests
sh scripts/run-tests.sh backend

# Run tests with Godot directly
/Applications/Godot.app/Contents/MacOS/Godot --path . --headless --run-tests --quit-on-finish
```

### Environment Variables

Required for tests to run properly:

```bash
export BARBOX_TEST_MODE=1 # Enable test mode
export BARBOX_BACKEND_PORT=8001 # Test backend port
export BARBOX_API_KEY=<from-seed> # API key from test seed
export BARBOX_BOX_ID=<from-seed> # Box ID from test seed
```

### Test Backend

The test suite automatically:
1. Starts test backend on port 8001
2. Seeds deterministic test data
3. Captures API key and Box ID
4. Exports as environment variables
5. Runs all tests
6. Stops test backend

See `scripts/run-tests.sh` for complete orchestration.

## Best Practices Summary

1. **Always use AAA pattern** with clear section comments
2. **One logical operation per test** - don't test multiple features
3. **Null-check services** - may legitimately be unavailable
4. **Use deterministic test data** for reproducible tests
5. **Categorize tests** with `[Category]` attributes
6. **Use TestConfig constants** instead of magic numbers
7. **Log test progress** with TestHelpers logging methods
8. **Clean up in [Cleanup]** not in test methods
9. **Early return for missing services** in Arrange section only
10. **Frame-aware timing** with StaticDelayAsync, not Task.Delay

## References

- GoDotTest Documentation: https://github.com/chickensoft-games/GoDotTest
- Shouldly Documentation: https://docs.shouldly.org/
- BackendTestBase: `Tests/Fixtures/BackendTestBase.cs`
- TestHelpers: `Tests/Fixtures/TestHelpers.cs`
- TestConfig: `Tests/Fixtures/TestConfig.cs`
