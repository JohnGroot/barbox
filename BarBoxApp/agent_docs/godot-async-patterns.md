# Godot Async Patterns

Consolidated async best practices for Godot's real-time execution model. These patterns apply across all BarBox services and games.

## Core Rules

1. **Frame-aware timing**: Use `DelayAsync()` / `StaticDelayAsync()`, never `Task.Delay()`
2. **No unnecessary threading**: Don't wrap already-async operations in `Task.Run()`
3. **Thread safety**: Use `CallDeferred` for scene tree modifications from async contexts
4. **Cancellation**: Implement `CancellationToken` for long-running operations
5. **Error handling**: Always wrap `async void` methods in try-catch

## DelayAsync Utilities

```csharp
// For AutoloadBase-derived services
await DelayAsync(1.0f);

// For utility classes not derived from AutoloadBase
await AutoloadBase.StaticDelayAsync(1.0f);
```

Both methods:
- Respect game pause states (Task.Delay continues during pause)
- Handle frame drops gracefully
- Integrate properly with Godot's game loop

## Anti-Patterns

### Task.Delay (computer clock timing)
```csharp
// BAD: Ignores pause states, uses computer clock
await Task.Delay(1000);

// GOOD: Frame-aware, respects pause states
await DelayAsync(1.0f);
```

### Unnecessary Task.Run
```csharp
// BAD: Creates unnecessary thread pool threads
await Task.Run(async () => await dataStore.GetAsync(key));

// GOOD: Direct async call (already thread-safe)
var result = await dataStore.GetAsync(key);
```

### Missing async void error handling
```csharp
// DANGEROUS: Unhandled exceptions crash the app
private async void InitializeAsync()
{
	await LoadConfigAsync();
}

// SAFE: Proper error handling
private async void InitializeAsync()
{
	try
	{
		await LoadConfigAsync();
	}
	catch (System.Exception ex)
	{
		LogError($"Init failed: {ex.Message}");
	}
}
```

## Thread Safety with CallDeferred

```csharp
private async void SaveDataAsync()
{
	try
	{
		var result = await dataStore.SaveAsync(data);
		CallDeferred(MethodName.OnSaveComplete, result.IsSuccess);
	}
	catch (System.Exception ex)
	{
		CallDeferred(MethodName.LogError, ex.Message);
	}
}
```

## Cancellation Pattern

```csharp
private CancellationTokenSource _operationCancellation;

public async Task<bool> ConnectAsync()
{
	_operationCancellation?.Cancel();
	_operationCancellation = new CancellationTokenSource();
	var token = _operationCancellation.Token;

	while (IsConnecting() && !token.IsCancellationRequested)
		await DelayAsync(0.1f);

	return IsConnected() && !token.IsCancellationRequested;
}

protected override void OnServiceDestroyed()
{
	_operationCancellation?.Cancel();
	_operationCancellation?.Dispose();
}
```
