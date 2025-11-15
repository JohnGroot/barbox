# Test Debugging Methodology

## Purpose

This document provides a systematic approach to debugging test failures in the BarBox codebase. Use this methodology when test suites fail after code changes to quickly identify root causes and implement appropriate fixes.

## Quick Reference

**When tests fail after your changes:**
1. Extract specific test names and error messages
2. Read test code to understand what it validates
3. Trace the code path to find the issue
4. Determine if test or code needs fixing using the decision tree
5. Apply the appropriate fix pattern
6. Add regression test for the fix

---

## Step 1: Extract Specific Test Names and Error Messages

### Run Tests with Full Output

```bash
# Full test suite
bash scripts/run-tests.sh 2>&1 | tee test-output.log

# Extract failure summary
grep "GoTest: > !!" test-output.log

# Find specific failures
grep -B 10 "FAILED\|Error occurred:" test-output.log
```

### Information to Capture

For each failing test, record:
- **Test class**: `PaymentServiceTests`
- **Test method**: `PurchaseCredits_WhenEventServiceNotReady_FailsWithDiagnostics`
- **Error message**: `Error occurred: _eventService.IsReady`
- **Expected vs Actual**: `Expected: FALSE, Actual: TRUE`
- **Stack trace location**: File and line number where it failed

---

## Step 2: Read Test Code to Understand Intent

### Anatomy of a Test

```csharp
[Test]
public async Task MethodName_Scenario_ExpectedBehavior()
{
	// SETUP: Arrange the test state
	SimulateEventServiceNotReady();

	// VERIFY SETUP: Ensure test state is correct
	_eventService.IsReady.ShouldBeFalse();  // ← Often fails here!

	// ACTION: Execute the operation being tested
	var result = await _paymentService.PurchaseCreditsAsync(...);

	// ASSERT: Verify the expected outcome
	result.IsSuccess.ShouldBeFalse();
	result.Error.ShouldContain("not available");
}
```

### Questions to Ask

1. **What is the test validating?**
   - User-facing feature? ("Users can purchase credits")
   - Error handling? ("Purchase fails gracefully when backend down")
   - Edge case? ("Concurrent purchases don't corrupt state")

2. **What state does the test expect?**
   - Setup phase creates specific conditions
   - Test assumes those conditions hold during execution

3. **What operation is being tested?**
   - Single method call? Complex workflow?
   - Synchronous or asynchronous?

4. **What outcome is expected?**
   - Success with specific result?
   - Failure with error message?
   - State change?

---

## Step 3: Trace the Code Path to Find the Issue

### Follow the Execution Flow

1. **Start at the test method** that's failing
2. **Follow each method call** in the setup phase
3. **Check if helper methods exist** and work as expected
4. **Verify test state is created** as the test expects
5. **Trace into production code** that's being tested

### Example Trace

```
Test: PurchaseCredits_WhenEventServiceNotReady_FailsWithDiagnostics
  └─> SimulateEventServiceNotReady()  [Line in test file]
      └─> FailureScenarioTestBase.SimulateEventServiceNotReady()  [Base class]
          └─> _eventService.SetReadyStateForTesting(false)  [Test utility]
              └─> ERROR: Method not found!
```

### Tools for Tracing

```bash
# Find method definitions
grep -r "method_name" /path/to/project --include="*.cs"

# Find method calls
grep -r "\.method_name\(" /path/to/project --include="*.cs"

# Check git history
git log -S "method_name" --oneline
git show <commit_hash>:path/to/file.cs
```

### Common Trace Patterns

**Pattern 1: Method Doesn't Exist**
- Test calls a method that was removed
- Look for commented-out code in test utilities
- Check git history for when method was removed

**Pattern 2: Method Behavior Changed**
- Method exists but does something different now
- Compare current implementation with test expectations
- Check if refactor changed behavior

**Pattern 3: State Not Created**
- Setup method runs but doesn't create expected state
- Check if initialization is now asynchronous
- Verify dependencies are initialized in correct order

---

## Step 4: Determine If Test or Code Needs Fixing

### Decision Tree

```
Is the test purpose still valid?
├─ YES: Does the test validate important behavior?
│  ├─ YES: Should this behavior still work in production?
│  │  ├─ YES: 🔴 VALID REGRESSION - Fix production code
│  │  └─ NO:  🟡 TEST NEEDS UPDATING - Behavior intentionally changed
│  └─ NO:  🟠 TEST SHOULD BE REMOVED - No longer relevant
└─ NO:  🔵 TEST INFRASTRUCTURE ISSUE - Fix test harness
```

### Detailed Questions

#### 1. Is the test purpose still valid?

**YES if the test validates:**
- User-facing functionality that should still work
- Error handling that prevents data loss or corruption
- Security checks
- Performance requirements
- API contracts

**NO if the test validates:**
- Internal implementation details that changed
- Deprecated features being removed
- Behavior that was incorrect

#### 2. Does the test validate important behavior?

**Important behaviors:**
- Prevents users from losing money/credits
- Prevents data corruption
- Ensures security (authentication, authorization)
- Critical user workflows (login, purchase, play game)
- Error messages that guide users

**Less important behaviors:**
- Internal metrics
- Debug logging
- Performance optimizations (unless critical)

#### 3. Should this behavior still work?

**YES - Fix the code if:**
- Behavior was accidentally broken
- Refactor introduced bug
- New code has unintended side effects

**NO - Update the test if:**
- Behavior was intentionally changed
- New implementation is better
- Requirements evolved

### Classification and Action

#### 🔴 Valid Regression - Fix Production Code

**Characteristics:**
- Test validates critical user-facing behavior
- Behavior worked before your changes
- Behavior should still work after your changes
- Test setup is correct

**Action:**
- Revert problematic code OR
- Fix the regression while preserving improvements OR
- Add backward compatibility

**Example:**
```csharp
// Regression: Tighter validation breaks valid inputs
// Test: LoginUser_WithValidPhone_Succeeds
// Issue: New validation rejects format "555-1234" which is valid

// Fix: Update validation to accept both formats
if (!IsValidPhoneFormat1(phone) && !IsValidPhoneFormat2(phone))
{
	return Error("Invalid phone");
}
```

#### 🟡 Test Needs Updating - Expectations Changed

**Characteristics:**
- Production code behavior intentionally changed
- New behavior is correct
- Test expects old behavior
- Test setup is outdated

**Action:**
- Update test expectations
- Update test setup/data
- Update assertions

**Example:**
```csharp
// Change: Polling interval reduced 200ms→100ms for performance
// Test: Expected to poll exactly 5 times in 1 second
// Old assertion: pollCount.ShouldBe(5)  // Assumes 200ms intervals
// New assertion: pollCount.ShouldBe(10) // Now 100ms intervals
```

#### 🟠 Test Should Be Removed

**Characteristics:**
- Tests deprecated features
- Tests internal implementation details that changed
- Duplicate coverage (other tests cover this)

**Action:**
- Remove test file
- Document why it was removed
- Ensure coverage exists elsewhere

#### 🔵 Test Infrastructure Issue - Fix Test Harness

**Characteristics:**
- Test can't create the state it's trying to test
- Test utility methods broken
- Test setup phase fails before reaching production code

**Action:**
- Fix test utility methods
- Restore test hooks/mocks
- Update test base classes

**Example:**
```csharp
// Issue: Test can't simulate backend failure
// Root cause: SetReadyStateForTesting() method removed
// Fix: Re-add method as DEBUG-only test hook

#if DEBUG
public void SetReadyStateForTesting(bool ready) {
	_testReadyOverride = ready;
	_useTestReadyOverride = true;
}
#endif
```

---

## Pattern Recognition for Common Test Failures

### Pattern 1: Method Not Found in Test Infrastructure

**Symptoms:**
```csharp
// In test utility file
// DISABLED: Method removed in refactor
// _eventService.SetReadyStateForTesting(false);
```

- Commented-out code in test utilities
- Comments say "DISABLED" or "Method removed"
- Tests expect state change that doesn't happen
- Setup assertions fail before reaching production code

**Root Cause:**
Test infrastructure not updated after production refactor

**Solution:**
1. Check if method is truly needed
2. If yes: Re-add as DEBUG-only test hook
3. If no: Update tests to use different approach

**Example Fix:**
```csharp
// Add to production code (DEBUG-only)
#if DEBUG
public void SetStateForTesting(State state) {
	_testStateOverride = state;
}
#endif

// Uncomment in test utility
_service.SetStateForTesting(State.Failed);
```

### Pattern 2: Stricter Validation Breaks Tests

**Symptoms:**
- Tests fail with validation errors
- Error message: "Invalid input" or "Validation failed"
- Production code has new input checks
- Tests use input that previously "worked"

**Root Cause:**
Input validation was tightened for security/correctness

**Solution Options:**
1. **Update test data** to meet new validation (preferred)
2. **Relax validation** if it's too strict
3. **Add backward compatibility** if old format still valid

**Example Fix:**
```csharp
// Test was using: phone = "1234"
// New validation requires: phone = "555-123-4567" (10 digits)

// Option 1: Update test data (preferred)
var phone = TestHelpers.GenerateValidPhone(); // "555-123-4567"

// Option 2: Relax validation (if 4 digits was valid)
if (phone.Length >= 4 && phone.Length <= 10) // Accept both
```

### Pattern 3: Timing/Polling Changes Cause Timeouts

**Symptoms:**
- Tests timeout waiting for state changes
- Code recently changed polling intervals or timeouts
- Error: "Operation timed out after X seconds"
- Tests that poll for results fail

**Root Cause:**
Timing constants changed for performance (faster polling, shorter timeouts)

**Solution:**
1. **Check if test timing is still reasonable**
   - If operation should complete in 1.5s, test waiting 5s is outdated
2. **Update test timeouts** to match new timing
3. **Use adaptive timing** in tests (retry with exponential backoff)
4. **Add test-specific overrides** if production timing is too aggressive for tests

**Example Fix:**
```csharp
// Production: 100ms polls, 1.5s timeout (15 attempts)
// Test was: Assert operation completes within 5 seconds

// Option 1: Update test expectation
await WaitForCondition(() => completed, timeout: 2.0f); // Was 5.0f

// Option 2: Test-specific timing override
#if DEBUG
public void SetPollingIntervalForTesting(float interval) {
	_testPollingInterval = interval;
}
#endif
```

### Pattern 4: Dependency Initialization Order Changed

**Symptoms:**
- Tests fail with "Service not ready" errors
- Error: "Cannot access X before initialization"
- Code recently changed from synchronous to asynchronous init
- Tests work when run individually but fail in suite

**Root Cause:**
Service initialization refactored (often sync → async)

**Solution:**
1. **Add async setup** to test base class
2. **Wait for initialization** before running tests
3. **Use staged initialization pattern** (AutoloadBase)

**Example Fix:**
```csharp
// Test base class
[SetUp]
public async Task Setup()
{
	_service = GetService();

	// Wait for async initialization
	await _service.WaitForReadyAsync(timeout: 5.0f);

	_service.IsReady.ShouldBeTrue("Service should be ready");
}
```

---

## How to Fix Regressions While Preserving Improvements

### Principle 1: Understand Intent of Both Changes

Before fixing, answer:

**Why was the improvement made?**
- Performance optimization? (faster response times)
- Security hardening? (prevent exploits)
- Architecture refactor? (better maintainability)
- Bug fix? (correct wrong behavior)

**What does the test validate?**
- User-facing feature? (critical)
- Error handling? (important for robustness)
- Edge case? (prevents rare bugs)
- Internal detail? (less critical)

### Principle 2: Fix Hierarchy (Prefer Earlier Options)

**1. Update test without changing production code** ⭐ BEST
- Test expectations were wrong
- Test data was invalid
- Test infrastructure is broken
- Test is obsolete

**2. Add backward compatibility** ⭐ GOOD
- Keep new behavior as default
- Support old behavior as fallback
- Document both as valid

**3. Relax overly strict changes** 🟡 ACCEPTABLE
- New validation too aggressive
- Timeout too short
- Error handling too conservative

**4. Partial revert with improvement** 🟠 LAST RESORT
- Keep some improvements
- Revert problematic parts
- Plan better fix for future

**5. Full revert** 🔴 AVOID
- Only if change fundamentally broken
- Document why and plan replacement

### Principle 3: Preserve Intent, Not Implementation

**Good Fix Example:**
```csharp
// INTENT: Prevent charging users when backend is down
// OLD IMPL: Sync health check (slow, blocks UI)
// NEW IMPL: Fast IsReady flag (fast, same safety)

// Test was checking for health check call
// Update test to check IsReady instead
_eventService.IsReady.ShouldBeFalse("Backend unavailable");
// Test intent preserved, implementation modernized
```

**Bad Fix Example:**
```csharp
// Reverts performance improvement just to pass test
await CheckBackendHealthSynchronously(); // Slow, but test passes
// Test passes but improvement lost
```

### Principle 4: Add Tests for the Fix

After fixing a regression, add a test that:

1. **Verifies the fix works**
   - New behavior is correct
   - Test should pass now

2. **Prevents future regressions**
   - Ensures old problematic behavior stays fixed
   - Would fail if someone reverts your fix

3. **Documents the intent**
   - Test name explains why both behaviors exist
   - Comments explain the trade-off

**Example:**
```csharp
[Test]
public async Task CreditPolling_OptimizedForGameResponsiveness_CompletesInUnder2Seconds()
{
	// This test verifies the performance optimization remains effective
	// Old: 5 second timeout (too slow for games)
	// New: 1.5 second timeout (responsive for gameplay)

	var startTime = Time.GetTicksMsec();
	var result = await _creditService.SpendAsync(playerId, 10, "test");
	var elapsed = (Time.GetTicksMsec() - startTime) / 1000.0f;

	elapsed.ShouldBeLessThan(2.0f, "Credit operations must be fast for game UX");
	// If this fails, someone may have reverted the optimization
}
```

---

## Debugging Checklist

Use this checklist when investigating test failures:

### Information Gathering
- [ ] Extracted all failing test names
- [ ] Captured full error messages
- [ ] Noted expected vs actual values
- [ ] Identified which commit introduced failures

### Understanding
- [ ] Read each failing test's code
- [ ] Understood what behavior test validates
- [ ] Identified test's setup/action/assert phases
- [ ] Determined if test purpose is still valid

### Investigation
- [ ] Traced code path from test to production
- [ ] Found where in the path execution fails
- [ ] Checked if test utility methods exist
- [ ] Verified test setup creates expected state

### Classification
- [ ] Determined if valid regression vs test issue
- [ ] Checked if test infrastructure is broken
- [ ] Verified recent changes that could cause failure
- [ ] Identified pattern (method missing, timing, validation, etc.)

### Solution
- [ ] Chose appropriate fix from hierarchy
- [ ] Understood intent of both test and code change
- [ ] Found minimal fix that preserves improvements
- [ ] Added regression test for the fix
- [ ] Documented why both behaviors are important

### Verification
- [ ] Fixed tests now pass
- [ ] All other tests still pass
- [ ] Production behavior preserved or improved
- [ ] No performance regressions introduced

---

## Real-World Example: Payment Test Infrastructure Failure

### Initial Failure Report

```
GoTest: > !! >> Test results: Passed: 101 | Failed: 6 | Skipped: 0

Failed tests:
- PaymentServiceTests::PurchaseCredits_WhenEventServiceNotReady_FailsWithDiagnostics
- PaymentServiceFailureTests::PurchaseCredits_BackendNotReady_MustFailGracefully
- PaymentServiceFailureTests::PurchaseCredits_BackendNotReady_CreditsNotAdded
- PaymentServiceFailureTests::PurchaseCredits_BackendFailsThenRecovers_SubsequentPurchaseSucceeds
- PaymentServiceFailureTests::PurchaseCredits_BackendNotReady_ErrorMessageHasDiagnostics
- PaymentServiceFailureTests::MultiplePurchases_FirstFailsSecondSucceeds_VerifyStateConsistency
```

### Step 1: Extract Error Messages

```
Error: _eventService.IsReady
Expected: FALSE
Actual: TRUE
```

### Step 2: Read Test Code

```csharp
[Test]
public async Task PurchaseCredits_WhenEventServiceNotReady_FailsWithDiagnostics()
{
	// Test Purpose: Verify payment fails safely when backend unavailable
	// Critical for preventing users from being charged when credits can't be added

	SimulateEventServiceNotReady(); // Setup: Force backend unavailable
	_eventService.IsReady.ShouldBeFalse(); // ← FAILS HERE

	var result = await _paymentService.PurchaseCreditsAsync(...);
	result.IsSuccess.ShouldBeFalse(); // Should reject payment
}
```

**Test Purpose:** VALID - Prevents charging users when backend down (critical safety check)

### Step 3: Trace Code Path

```
Test → SimulateEventServiceNotReady()
     → FailureScenarioTestBase.cs line 39
         → // DISABLED: Method removed in staged initialization refactor
         → // _eventService.SetReadyStateForTesting(false);
             → Method doesn't exist!
                 → EventService.IsReady remains TRUE
                     → Test setup fails
```

### Step 4: Determine Fix

**Classification:** 🔵 Test Infrastructure Issue

**Reasoning:**
- Test purpose is VALID (critical payment safety)
- Production code is CORRECT (properly checks IsReady)
- Test infrastructure is BROKEN (can't simulate failure)

**Not a valid regression** because:
- Production code correctly prevents payments when EventService.IsReady == false
- Test just can't simulate the false condition anymore

### Step 5: Apply Fix

**Solution:** Re-add test hook as DEBUG-only method

```csharp
// EventService.cs
#if DEBUG
private bool _testReadyOverride = false;
private bool _useTestReadyOverride = false;

public void SetReadyStateForTesting(bool ready)
{
	_testReadyOverride = ready;
	_useTestReadyOverride = true;
}

public void ClearReadyStateOverride()
{
	_useTestReadyOverride = false;
}
#endif

public bool IsReady
{
	get
	{
#if DEBUG
		if (_useTestReadyOverride) return _testReadyOverride;
#endif
		return _isReady && _isInitialized;
	}
}
```

```csharp
// FailureScenarioTestBase.cs
protected void SimulateEventServiceNotReady()
{
	_eventService.SetReadyStateForTesting(false); // Uncomment
}

protected void RestoreEventService()
{
	_eventService.ClearReadyStateOverride(); // Uncomment
}
```

### Step 6: Verify Fix

```bash
bash scripts/run-tests.sh
# Result: 107/107 tests passing (100%)
```

### Key Takeaways

1. **Not all test failures are regressions** - Sometimes test infrastructure breaks
2. **Test purpose determines fix priority** - Critical safety tests must work
3. **DEBUG-only test hooks are acceptable** - No production impact
4. **Preserve improvements** - Fixed tests without reverting any optimizations

---

## Summary

When tests fail after your changes:

1. **Extract specific failures** - Names, errors, expected vs actual
2. **Understand test intent** - What is it validating and why?
3. **Trace the code path** - Where exactly does it fail?
4. **Classify the failure** - Regression, test issue, or infrastructure?
5. **Choose appropriate fix** - Prefer updating tests over reverting code
6. **Preserve improvements** - Fix while maintaining optimizations
7. **Add regression tests** - Prevent the issue from recurring

**Golden Rule:** Tests are specifications. If a test validates important behavior and fails, either:
- Fix the code to match the spec (if spec is correct)
- Fix the spec to match the code (if code is better)
- Fix the test infrastructure (if test can't run)