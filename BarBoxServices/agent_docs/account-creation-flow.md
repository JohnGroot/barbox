# Account Creation Flow

The account creation system uses **pessimistic validation** to provide clear error messages before attempting to create resources.

## Validation Endpoint

### `POST /player/validate`

Pre-flight validation check without creating the player.

**Request:**
```json
{"id": "player-uuid", "tag": "username", "origin_id": "box-uuid"}
```

**Success:** `{"valid": true, "errors": []}`

**Errors:**
```json
{
  "valid": false,
  "errors": [
    {"field": "origin_id", "message": "Origin box '...' does not exist.", "value": "box-uuid"},
    {"field": "tag", "message": "Username 'testuser' is already taken.", "value": "testuser"}
  ]
}
```

## Recommended Client Flow

1. Client-side validation (phone, PIN, username format)
2. `POST /player/validate` with all data
3. Display errors if `valid: false`
4. `POST /player/` only if validation passed
5. Handle creation errors with error code mapping

## Error Codes

| Code | Meaning |
|------|---------|
| `VALIDATION_ERROR` | Input validation failed |
| `FK_VIOLATION` | Foreign key constraint (e.g., box doesn't exist) |
| `DUPLICATE_RESOURCE` | Resource already exists (409) |
| `UNIQUE_CONSTRAINT` | Database uniqueness violated |
| `INTERNAL_ERROR` | Unexpected server error (500) |

## Request ID Tracking

Every response includes `X-Request-Id` header. All log messages include it for cross-system tracing.
