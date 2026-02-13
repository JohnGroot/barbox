# SQLite JSON Handling

SQLite's `json_extract()` returns parsed Python objects, NOT JSON strings.

## Key Behavior

```python
sql = "SELECT json_extract(bse.payload, '$.lap_times') FROM box_session_event bse"
row = result.fetchone()
lap_times = row['lap_times']  # Python list [12.5, 14.3], NOT a JSON string!
```

## Robust Parsing Pattern

**ALWAYS handle both parsed lists AND JSON strings:**

```python
def _parse_json_field(value) -> list[float] | None:
    if value is None:
        return None
    if isinstance(value, list):
        try:
            return [float(x) for x in value]
        except (ValueError, TypeError):
            return None
    if isinstance(value, str):
        if not value or value == "null":
            return None
        try:
            parsed = json.loads(value)
            if isinstance(parsed, list):
                return [float(x) for x in parsed]
        except (json.JSONDecodeError, ValueError, TypeError):
            return None
    return None
```

## Per-Entry Error Handling

**NEVER use list comprehensions for error-prone parsing:**

```python
# BAD: One error crashes entire query
leaderboard = [parse(row) for row in result.tuples()]

# GOOD: Per-entry error handling
leaderboard = []
for row in result.tuples():
    try:
        entry = schemas.LeaderboardEntry(...)
        leaderboard.append(entry)
    except Exception as e:
        print(f"[ERROR] Failed to parse: {row}, {e}")
        continue
```

## Common Errors

| Error | Cause | Solution |
|-------|-------|---------|
| `TypeError: the JSON object must be str` | `json.loads()` on Python list | Type check first |
| `Pydantic validation error: list_type` | Backend returns JSON string | Parse before passing |
| List comprehension crash | One bad row | Use explicit loop |
