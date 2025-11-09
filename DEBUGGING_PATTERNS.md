# Full-Stack Debugging Patterns

A comprehensive guide to debugging complex issues in full-stack applications, based on real-world debugging sessions in the BarBox platform.

## Table of Contents
1. [The Core Debugging Pattern](#the-core-debugging-pattern)
2. [Case Study: Three Layered Bugs](#case-study-three-layered-bugs)
3. [Key Principles](#key-principles)
4. [Common Patterns](#common-patterns)
5. [Prevention Strategies](#prevention-strategies)

---

## The Core Debugging Pattern

### 1. Identify the Symptom
**Start with the user-facing error**, not the logs. What is the user experiencing?

```
Example: User clicks "Purchase Credits" → Error dialog appears
```

### 2. Check Service Logs
**Look at both frontend and backend logs simultaneously.**

- Frontend logs (Godot): What service reported the error?
- Backend logs (FastAPI): What was the actual HTTP response?

**Critical**: Look for **contradictions** between frontend and backend logs.

```
[CreditService] ERROR: EventService not available (exists: False, ready: False)
[PaymentService] ERROR: Diagnostics: EventService exists: True, EventService ready: True
                        ↑
                This contradiction is the key clue!
```

### 3. Trace the Call Stack
**Follow the execution path through ALL layers:**

```
User Action
  ↓
Frontend Service (Godot C#)
  ↓
HTTP Request
  ↓
Backend API (FastAPI)
  ↓
Database Query (SQLite)
  ↓
Database Schema
```

### 4. Create Reproduction Tests
**Write tests that reproduce the exact error BEFORE fixing.**

Why? Because:
- You need to verify the bug exists
- You need to verify your fix works
- You prevent regression

```csharp
[Test]
public async Task CreditService_CachedEventServiceReference_BecomesStaleWhenEventServiceBecomesReady()
{
    // Reproduces the exact production bug
    var eventService = EventService.GetInstance();
    eventService.ShouldNotBeNull();
    eventService.IsReady.ShouldBeTrue();

    var addResult = await _creditService.AddAsync(playerId, 10, "Test");

    // This SHOULD pass but FAILS with the bug
    addResult.IsSuccess.ShouldBeTrue();
}
```

### 5. Fix Each Layer
**Start at the lowest layer (database/backend) and work up.**

Why? Because:
- Frontend issues often stem from backend problems
- Backend issues often stem from database schema problems
- Fixing low-level issues can reveal higher-level issues

### 6. Verify at Each Layer
**Test the backend API directly BEFORE testing through the app.**

```bash
# Test backend endpoint directly
curl "http://127.0.0.1:8000/player/{player_id}/credits?location_id=JohnGrootMachine"

# If this fails, don't test through the app yet
# If this succeeds, then test through the app
```

### 7. Follow the Data Flow
**Trace how data moves through the system:**

```
Frontend Request:
  playerId: UUID
    ↓
HTTP GET /player/{playerId}/credits?location_id=X
    ↓
Backend Parameter:
  player_id: str(UUID)
    ↓
SQL Parameter:
  :player_id
    ↓
SQL WHERE Clause:
  WHERE bs.host_player_id = :player_id
    ↓
Database Column:
  box_session.host_player_id (UUID)
```

**Any mismatch in this chain causes failure.**

---

## Case Study: Three Layered Bugs

### The Symptom

User attempted to purchase credits in the BarBox app:
- Click "Buy 25 Credits"
- See error: "Credit purchase failed"
- No credits added to account

### Initial Investigation

**Frontend logs (Godot)**:
```
[PaymentService] Processing purchase...
[DebugPaymentService] Purchase approved - Transaction ID: DEBUG_TXN_xxx
[CreditService] ERROR: EventService not available (exists: False, ready: False)
[PaymentService] ERROR: Diagnostics: EventService exists: True, EventService ready: True
```

**The Contradiction**: CreditService says EventService doesn't exist, but PaymentService sees it fine.

### Bug #1: CreditService Race Condition

**Discovery**: Created reproduction tests showing CreditService had a stale cached reference.

**Root Cause**:
```csharp
// CreditService.cs
private EventService _eventService;

protected override void OnServiceInitialize()
{
    _eventService = EventService.GetInstance();  // ← Cached during init
}

public async Task<Result<int>> AddAsync(...)
{
    if (_eventService == null || !_eventService.IsReady)  // ← Checking stale reference
    {
        return Result<int>.Failure("Credit service unavailable");
    }
}
```

**The Problem**: If EventService wasn't ready when CreditService initialized, `_eventService` was set to null and **never updated**.

**The Fix**: Replace cached reference with fresh lookups (like PaymentService does):

```csharp
public async Task<Result<int>> AddAsync(...)
{
    var eventService = EventService.GetInstance();  // ← Fresh lookup every time
    if (eventService == null || !eventService.IsReady)
    {
        return Result<int>.Failure("Credit service unavailable");
    }
}
```

**Evidence Fix Worked**: Error message changed from "EventService not available" to "Query failed with code 500".

### Bug #2: Backend Method Name Mismatch

**Discovery**: After fixing Bug #1, got a new error: `AttributeError: 'CRUD' object has no attribute 'get_one_raw'`

**Root Cause**:
```python
# player.py line 356
result = await db_service.get_one_raw(sql, {...})  # ← Method doesn't exist!
```

**The Problem**: The CRUD class only has `get_many_raw()`, not `get_one_raw()`.

**The Fix**:
```python
# player.py line 356
result = await db_service.get_many_raw(sql, {...})  # ← Use correct method
```

**Evidence Fix Worked**: Error message changed from "get_one_raw not found" to "no such column: bs.player_id".

### Bug #3: SQL Schema Column Mismatch

**Discovery**: After fixing Bug #2, got a database error: `sqlite3.OperationalError: no such column: bs.player_id`

**Root Cause**:
```python
# player.py line 351
sql = """
    SELECT ...
    FROM box_session_event bse
    JOIN box_session bs ON bse.session_id = bs.id
    WHERE bs.player_id = :player_id  # ← Column doesn't exist!
```

**Actual Schema**:
```python
# defs.py
class BoxSession(Base):
    host_player_id: Mapped[UUID]  # ← The actual column name!
    # NOT player_id!
```

**The Fix**:
```python
# player.py line 351
WHERE bs.host_player_id = :player_id  # ← Use correct column name
```

**Evidence Fix Worked**: Credit purchases now complete successfully!

### The Layering Effect

```
Bug #1 (CreditService race)
   ↓ Masked
Bug #2 (Backend method name)
   ↓ Masked
Bug #3 (SQL column name)
```

**Key Insight**: Each bug was hidden behind the previous one. Fixing Bug #1 revealed Bug #2. Fixing Bug #2 revealed Bug #3.

**Without the debugging pattern**, we might have:
1. Fixed Bug #1
2. Been confused why it still didn't work
3. Given up or started looking in the wrong place

**With the pattern**:
1. Fixed Bug #1
2. Saw NEW error message (progress!)
3. Followed the same pattern to find Bug #2
4. Repeated for Bug #3
5. Complete fix achieved

---

## Key Principles

### 1. Error Messages Are Progress

**Don't panic when fixing one bug reveals another.** This is **normal** in layered systems.

```
Before:  "EventService not available"
After:   "Query failed with code 500"
         ↑
      THIS IS GOOD! We fixed the first layer and revealed the next.
```

### 2. Test at Each Layer

**Don't skip layers when testing:**

❌ Wrong:
```
Fix backend → Test in app → Still broken → Confusion
```

✅ Right:
```
Fix backend → Test backend directly → Works ✓
            → Test in app → Still broken → Next bug found
```

### 3. Contradictions Are Clues

**When two services report contradictory state, investigate the difference:**

```
ServiceA: "ResourceX doesn't exist"
ServiceB: "ResourceX exists and is ready"
           ↑
        One of these is using stale/incorrect data.
        Find out which one and why.
```

### 4. Schema Is Truth

**When debugging database issues, always check:**

1. What column does the SQL query use?
2. What column actually exists in the schema?
3. Do they match EXACTLY (including case)?

```python
# Query
WHERE bs.player_id = :player_id

# Schema
class BoxSession:
    host_player_id: Mapped[UUID]  # ← Doesn't match!
```

### 5. Fresh vs Cached References

**Cached references can become stale:**

```csharp
// ❌ Cached - can become stale
private Service _service;
OnInit() { _service = Service.Get(); }
OnUse() { if (_service == null) ... }  // Checks stale reference

// ✅ Fresh - always current
OnUse() {
    var service = Service.Get();  // Fresh lookup
    if (service == null) ...
}
```

---

## Common Patterns

### Pattern: Service Initialization Race

**Symptom**: Service A reports "Service B not available" even though Service B is running.

**Cause**: Service A cached a reference to Service B before B was ready.

**Solution**: Use fresh lookups instead of caching:

```csharp
// Instead of this
private ServiceB _serviceB;
void OnInit() { _serviceB = ServiceB.Get(); }
void OnUse() { if (_serviceB == null) ... }

// Do this
void OnUse() {
    var serviceB = ServiceB.Get();
    if (serviceB == null) ...
}
```

### Pattern: Schema-Query Mismatch

**Symptom**: Database error "no such column: X"

**Cause**: SQL query references a column that doesn't exist.

**Solution**: Always verify SQL queries against actual schema:

```python
# 1. Check the schema definition
class MyTable(Base):
    actual_column_name: Mapped[Type]

# 2. Update SQL query to match
sql = "SELECT * FROM my_table WHERE actual_column_name = :value"
```

### Pattern: Method Name Typo

**Symptom**: AttributeError: 'Class' object has no attribute 'method_name'

**Cause**: Calling a method that doesn't exist (typo or API change).

**Solution**:
1. Check the class definition for available methods
2. Use IDE autocomplete to prevent typos
3. Search codebase for similar working code

### Pattern: Parameter Type Mismatch

**Symptom**: Database query returns no results despite data existing.

**Cause**: Parameter type doesn't match column type.

**Solution**: Ensure types match:

```python
# Wrong: String UUID vs UUID column
WHERE id = '550e8400-e29b-41d4-a716-446655440000'

# Right: UUID parameter
WHERE id = :id  # With id = UUID('550e8400-...')
```

---

## Prevention Strategies

### 1. Type Safety

**Use strong typing everywhere possible:**

```csharp
// ✅ Good - compile-time type checking
public async Task<Result<int>> AddAsync(Guid playerId, int amount)

// ❌ Bad - runtime errors
public async Task AddAsync(object playerId, object amount)
```

### 2. Schema Validation

**Generate SQL queries from schema definitions when possible:**

```python
# Instead of hand-writing SQL
sql = "SELECT * FROM box_session WHERE player_id = ..."

# Use ORM query builder
session.query(BoxSession).filter(BoxSession.host_player_id == player_id)
```

### 3. Integration Tests

**Test the full stack, not just units:**

```csharp
[Test]
public async Task PurchaseCredits_EndToEnd_Success()
{
    // Login
    var loginResult = await sessionManager.Login(...);

    // Purchase
    var purchaseResult = await paymentService.Purchase(...);

    // Verify
    var balance = await creditService.GetBalance(...);
    balance.Value.ShouldBe(expectedAmount);
}
```

### 4. Explicit Initialization Order

**Define and enforce service initialization dependencies:**

```csharp
// SceneManager.cs - Explicit phases
Phase 1: Foundation (Location, Backend)
Phase 2: EventService (depends on Backend)
Phase 3: SessionManager, PaymentService (depend on EventService)
```

### 5. Fresh Lookups Over Caching

**Default to fresh lookups unless performance requires caching:**

```csharp
// Default approach
var service = ServiceLocator.Get<IService>();

// Only cache if profiling shows performance issue
// AND you have a mechanism to invalidate the cache
```

### 6. Structured Logging

**Log at each layer with context:**

```csharp
LogInfo($"Purchasing credits: playerId={playerId}, amount={amount}");
// ... operation ...
LogInfo($"Purchase successful: newBalance={newBalance}");

// Or on error
LogError($"Purchase failed: playerId={playerId}, error={error}");
```

```python
logger.info("querying_credits", player_id=str(player_id), location_id=location_id)
# ... query ...
logger.info("credits_retrieved", player_id=str(player_id), credits=credits)
```

---

## Debugging Checklist

When you encounter a bug:

- [ ] Identify the user-facing symptom
- [ ] Check logs from all layers (frontend, backend, database)
- [ ] Look for contradictions between layers
- [ ] Trace the complete call stack
- [ ] Create a reproduction test
- [ ] Fix at the lowest layer first
- [ ] Test each layer independently
- [ ] Verify the fix end-to-end
- [ ] Document what you learned
- [ ] Consider prevention strategies

---

## Summary

**The debugging pattern is iterative:**

1. Find a bug
2. Fix it
3. Test
4. Find the next bug (if revealed)
5. Repeat

**Key mindset shifts:**

- Revealing new bugs is **progress**, not failure
- Test each layer **independently** before testing integration
- **Contradictions** between services are valuable clues
- **Schema is truth** - always verify against actual database structure
- **Fresh lookups** are safer than cached references

**Remember**: Most complex bugs are actually **multiple simple bugs layered on each other.** The debugging pattern helps you peel back the layers systematically.

---

## Case Study: Test Infrastructure Failure (EventService Ready State)

### Symptom
After staged initialization refactor, 6 payment service tests failed:
- `PurchaseCredits_BackendNotReady_MustFailGracefully`
- `PurchaseCredits_BackendNotReady_CreditsNotAdded`
- `PurchaseCredits_BackendFailsThenRecovers_SubsequentPurchaseSucceeds`
- `PurchaseCredits_BackendNotReady_ErrorMessageHasDiagnostics`
- `MultiplePurchases_FirstFailsSecondSucceeds_VerifyStateConsistency`

Error: `"EventService" does not contain a definition for "SetReadyStateForTesting"`

### Investigation Process

**Step 1: Extract Error Message**
```
Error CS1061: 'EventService' does not contain a definition for 'SetReadyStateForTesting'
Location: FailureScenarioTestBase.cs:53, TestHelpers.cs:400
```

**Step 2: Read Test Code**
```csharp
// FailureScenarioTestBase.cs - what tests are trying to do
protected void SimulateEventServiceNotReady()
{
    _eventService.SetReadyStateForTesting(false);  // Method doesn't exist!
}
```

Tests validate **critical payment safety logic**: preventing credit purchases when backend unavailable.

**Step 3: Trace Production Code**
```csharp
// PaymentService.cs - what production code checks
var eventService = EventService.GetInstance();
if (eventService == null || !eventService.IsReady)  // This is the guard
{
    return PaymentResult.Failure("Credit system temporarily unavailable");
}
```

**Step 4: Classify Failure**
- ❌ NOT a production regression (payment logic is correct)
- ✅ Test infrastructure broken (can't simulate failures)
- ✅ Tests validate critical business logic (must be preserved)

### Root Cause
Staged initialization refactor removed test hooks from EventService. The commented-out code showed:
```csharp
// _eventService.SetReadyStateForTesting(false); // DISABLED: Method removed
```

### Solution: DEBUG-Only Test Hooks

**Pattern**: Compile-time conditional test infrastructure

```csharp
// EventService.cs
#if DEBUG
    private bool _testReadyOverride = false;
    private bool _useTestReadyOverride = false;

    /// <summary>
    /// FOR TESTING ONLY: Override ready state to simulate backend failures.
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

**Why This Works:**
1. **Zero Production Cost**: Entire infrastructure removed in RELEASE builds
2. **Type-Safe**: Compiler enforces correct usage
3. **Observable**: Logs show test state changes
4. **Isolated**: Only affects EventService, not other autoloads

### Key Debugging Decisions

**Decision 1: Fix Test Infrastructure vs. Disable Tests**
- **Chose**: Fix infrastructure
- **Rationale**: Tests validate critical payment safety (prevent charging users when credits can't be delivered)
- **Alternative**: Disabling tests would hide regressions in business-critical logic

**Decision 2: DEBUG-Only vs. Production Test Hooks**
- **Chose**: `#if DEBUG` conditional compilation
- **Rationale**: Zero production overhead, explicit separation of concerns
- **Alternative**: Virtual property would affect all services, add runtime cost

**Decision 3: Property Shadowing vs. Interface Mocking**
- **Chose**: `new bool IsReady` to shadow base property
- **Rationale**: Minimal changes, type-safe, works with Godot autoload pattern
- **Alternative**: Interface-based mocking would require major refactoring

### Lessons Learned

**1. Test Purpose is the Baseline**
When a test fails after refactoring, ask:
- Is the test validating critical logic? (YES → must fix)
- Is the production code correct? (YES → test infrastructure issue)
- Can we safely disable the test? (NO → business-critical validation)

**2. DEBUG-Only Patterns**
Standard industry practice for test infrastructure:
- Unity: `[Testing.InitializeOnLoad]`
- ASP.NET: `#if DEBUG` test controllers
- BarBox: `#if DEBUG` test hooks

**3. Property Shadowing Mechanics**
```csharp
EventService service = EventService.GetInstance();  // Typed as EventService
service.IsReady  // Uses EventService.IsReady (shadowed property) ✓

AutoloadBase service = EventService.GetInstance();  // Typed as AutoloadBase
service.IsReady  // Uses AutoloadBase.IsReady (base property) ✗
```

Shadowing works when type information is preserved (which our code correctly does).

### Results
- ✅ All 107 tests passing (100% pass rate)
- ✅ Zero production overhead (test hooks compiled out)
- ✅ Payment safety logic validated comprehensively
- ✅ Test infrastructure documented in Claude memory

### Prevention
Added to `.claud/memory/test-debugging-methodology.md`:
- Step-by-step test failure debugging process
- Decision tree for test vs. code fixes
- Pattern recognition for common failures
- Complete debugging checklist

**Key Principle**: If test purpose is valid and production code is correct, the failure indicates test infrastructure needs restoration, not test deletion.

---

## Further Reading

- [Godot Autoload Documentation](https://docs.godotengine.org/en/stable/tutorials/scripting/singletons_autoload.html)
- [FastAPI Dependency Injection](https://fastapi.tiangolo.com/tutorial/dependencies/)
- [SQLAlchemy Core Tutorial](https://docs.sqlalchemy.org/en/20/core/tutorial.html)
- BarBox Project Documentation:
  - `/BarBoxApp/CLAUDE.md` - Godot client architecture
  - `/BarBoxServices/CLAUDE.md` - Backend API architecture
  - `/.claud/memory/test-debugging-methodology.md` - Test failure debugging patterns
