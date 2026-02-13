# Modern C# Patterns (C# 8-12)

Use modern C# features to write clearer, more maintainable code. Apply to new code consistently; migrate existing code opportunistically during refactoring.

## File-Scoped Namespaces (C# 10)

```csharp
// Preferred - saves one indentation level
namespace BarBox.Core.Services;

public partial class MyService : AutoloadBase { }
```

## Null-Coalescing Assignment (C# 8)

```csharp
_cache ??= new Dictionary<string, CachedBalance>();
_playerLapTimes[playerId] ??= new List<float>();
```

## Collection Expressions (C# 12)

```csharp
List<int> numbers = [];
string[] items = ["one", "two", "three"];
CheckpointTime[] GetCheckpoints(string id) => [];
```

## Required Properties (C# 11)

```csharp
public partial class PlayerLoginRequest
{
	[JsonPropertyName("phone_number")]
	public required string PhoneNumber { get; init; }

	[JsonPropertyName("pin")]
	public required string Pin { get; init; }
}
```

Apply to DTOs, request/response objects, immutable data structures.

## Target-Typed New (C# 9)

```csharp
private CancellationTokenSource _cancellation = new();
private Dictionary<string, int> _scores = new();
```

## Pattern Matching (C# 8-11)

```csharp
return _racingState switch
{
	RacingState.CountdownWaiting => false,
	RacingState.GameOverDeciding => false,
	_ => true
};

if (result is { IsSuccess: true, Value: var data })
	ProcessData(data);
```

## Raw String Literals (C# 11)

```csharp
var errorMessage = $$"""
	Operation failed:
	- Primary: {{primaryError}}
	- Secondary: {{secondaryError}}
	""";
```

## Primary Constructors (C# 12)

```csharp
// Simple DTOs only
public class CachedBalance(int amount, DateTime lastUpdated)
{
	public int Amount { get; set; } = amount;
	public DateTime LastUpdated { get; set; } = lastUpdated;
	public bool IsStale => (DateTime.UtcNow - LastUpdated).TotalSeconds > CACHE_TTL_SECONDS;
}
```

## Inline Out Variables (C# 7)

```csharp
if (_scores.TryGetValue(playerId, out var score))
	return score;
```

## Migration Priority

1. File-scoped namespaces (readability)
2. Null-coalescing assignment (clarity)
3. Required properties for DTOs (safety)

**Principle:** Modern C# features should improve legibility and intent, not add complexity.
