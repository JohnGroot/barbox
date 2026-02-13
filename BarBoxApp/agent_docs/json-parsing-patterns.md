# JSON Parsing Patterns (Godot)

Godot's `Json.ParseString()` has specific behavior that differs from System.Text.Json. Understanding these patterns prevents common parsing errors.

## Godot JSON Parsing

### Single-level parsing
```csharp
var json = Json.ParseString(responseBody);
if (json.Error == Error.Ok)
{
	var dict = json.AsGodotDictionary();  // Direct call on Json object
	var array = json.AsGodotArray();
}
```

### Nested access
```csharp
var parentDict = json.AsGodotDictionary();
var childArray = parentDict["key"].AsGodotArray();     // Direct on Variant
var childDict = parentDict["key"].AsGodotDictionary();
```

### Double-parsing (backend returns JSON string)
```csharp
var jsonString = dict["key"].AsString();
var innerJson = Json.ParseString(jsonString);
if (innerJson.Error == Error.Ok)
	var innerArray = innerJson.AsGodotArray();  // Direct call, no Variant cast
```

## Anti-Patterns

```csharp
// WRONG: Explicit Variant cast (causes InvalidCastException)
var array = ((Variant)json.Obj).AsGodotArray();

// WRONG: Accessing .Obj directly
var dict = json.Obj.AsGodotDictionary();

// WRONG: Using Dictionary<string, object> for serialization
var payload = new Dictionary<string, object> { ... };
await PostAsync<Dictionary<string, object>, Response>(...);  // Fails with Variant error
```

## System.Text.Json Serialization

**For POST/PUT requests, ALWAYS use proper DTO classes:**

```csharp
public partial class PlayerCreateRequest
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("tag")]
	public string Tag { get; set; }
}

var request = new PlayerCreateRequest { Id = "...", Tag = "..." };
await PostAsync<PlayerCreateRequest, PlayerDetailResponse>("/endpoint", request);
```

**Why:** `Dictionary<string, object>` contains Godot `Variant` types that System.Text.Json cannot serialize.

## Common Errors

| Error | Cause | Solution |
|-------|-------|---------|
| `"The type 'Godot.Variant' is not a supported dictionary key"` | Using Dictionary or anonymous object | Use proper DTO class |
| `"Unable to cast object of type 'Godot.Collections.Array' to type 'Godot.Variant'"` | Unnecessary Variant cast | Direct method call on Json object |
