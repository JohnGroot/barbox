# Godot HttpClient Patterns

Godot's `HttpClient` requires specific handling to avoid connection timeouts and ensure reliable HTTP communication.

## Critical Requirements

1. **Always call `Poll()` in wait loops** - The HttpClient state machine requires explicit polling
2. **Check both `Resolving` and `Connecting` states** - DNS resolution is a separate state
3. **Use `"127.0.0.1"` instead of `"localhost"`** - Avoids DNS resolution delays
4. **Use `DelayAsync()` not `Task.Delay()`** - Frame-aware timing

## Reference Implementation

See `_Core/Scripts/Autoloads/BackendManager.cs` (`IsBackendHealthyAsync` method) for the complete pattern including DNS resolution handling, Poll() usage, timeout handling, and error handling.

## Connection Patterns

**Persistent Connection (AutoloadBase Services)**
- Use for: BackendManager, EventService, long-lived services
- Reuse same `HttpClient` instance across multiple requests
- Implement reconnection logic on failures

**Ephemeral Connection (Game Services)**
- Use for: One-off queries, leaderboard fetches
- Create new `HttpClient` per call
- Always close/dispose in `finally` block

## Response Body Timing (CRITICAL)

`ReadResponseBodyChunk()` returns immediately with whatever data is available, which may be zero bytes.

```csharp
await WaitForResponseAsync();  // Waits for headers only
var responseCode = _httpClient.GetResponseCode();

// CRITICAL: Poll to ensure body is fully received
_httpClient.Poll();
await DelayAsync(0.05f);
_httpClient.Poll();

var bodyBytes = _httpClient.ReadResponseBodyChunk();
var bodyText = bodyBytes.GetStringFromUtf8();

if (string.IsNullOrEmpty(bodyText))
	return Result.Failure("Empty response body");

var response = JsonSerializer.Deserialize<TResponse>(bodyText);
```

**Common Error:** `"The input does not contain any JSON tokens"` = empty body passed to deserializer.

## Common Mistakes

- Forgetting `Poll()` - state machine never advances
- Not checking `Status.Resolving` - timeout on DNS lookup
- Using `"localhost"` instead of IP - triggers DNS lookup delays
- Using `Task.Delay()` instead of `DelayAsync()` - ignores pause states
- Not closing ephemeral connections - memory leaks

## Debugging Connection Issues

1. Check for `Poll()` calls in connection and request loops
2. Verify `Status.Resolving` is checked in connection loop
3. Confirm using `"127.0.0.1"` not `"localhost"`
4. Verify using `DelayAsync()` not `Task.Delay()`
5. Test backend is running: `curl http://127.0.0.1:8000/alive`
