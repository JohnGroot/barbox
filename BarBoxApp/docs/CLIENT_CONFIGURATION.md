# Client Configuration Guide

This guide covers how to configure the BarBoxApp (Godot client) to connect to different backend environments.

## Overview

BarBoxApp uses environment variables for configuration, loaded from `.env.local` files. This allows the same codebase to connect to local development backends, remote test backends, or production backends.

### Supported Environments

| Environment | Backend URL | Use Case |
|-------------|-------------|----------|
| **Local Development** | `http://localhost:8000` | Coding in Godot editor, backend auto-starts |
| **Local → Remote** | `https://barbox-backend.fly.dev` | Testing production backend from editor |
| **Test Machine** | `https://barbox-backend.fly.dev` | Physical test machine |
| **Production** | `https://barbox-backend.fly.dev` | Production arcade installations |

---

## Environment Variable Reference

### Required Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `BARBOX_BACKEND_URL` | Full URL to backend server | `https://barbox-backend.fly.dev` |
| `BARBOX_BOX_ID` | Unique UUID for this box | `12345678-1234-1234-1234-123456789012` |
| `BARBOX_API_KEY` | Authentication key from registration | `<your-box-api-key>` |

### Optional Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `BARBOX_BOX_NAME` | Hostname | Human-readable box name (lowercase_with_underscores) |
| `BARBOX_VENUE_NAME` | Hostname | Venue identifier for venue-scoped data |
| `BARBOX_TEST_MODE` | (unset) | Set to `1` to skip local backend auto-start |

### Backend Auto-Start Behavior

In the Godot editor, the `BackendManager` automatically starts a local backend when:
- Running in the editor (not an exported build)
- `BARBOX_TEST_MODE` is NOT set
- Backend URL points to localhost

To **disable auto-start** (e.g., when testing remote backend from editor):
```bash
BARBOX_TEST_MODE=1
```

---

## Configuration Files

### File Priority

Configuration is loaded in this order (later overrides earlier):

1. `.env` - Template file (committed to repo, default values)
2. `.env.local` - Machine-specific config (gitignored, your actual values)
3. System environment variables - Override everything

### File Location

All `.env` files should be in the `BarBoxApp/` directory root:

```
BarBoxApp/
├── .env              # Template (committed)
├── .env.example      # Documentation (committed)
├── .env.local        # Your config (gitignored)
├── .env.local.dev    # Optional: saved local config
├── .env.local.remote # Optional: saved remote config
└── ...
```

---

## Configuration by Environment

### Local Development (Default)

For development in the Godot editor with auto-started local backend:

```bash
# BarBoxApp/.env.local

# Local backend (auto-starts when you run in editor)
BARBOX_BACKEND_URL=http://localhost:8000

# Test box credentials (auto-seeded by backend in dev mode)
BARBOX_BOX_ID=00000000-0000-0000-0000-000000000001
BARBOX_API_KEY=YOUR_DEV_BOX_API_KEY_HERE

# Display names
BARBOX_BOX_NAME=dev_box
BARBOX_VENUE_NAME=dev_local
```

**What happens:**
1. Launch game in Godot editor
2. BackendManager auto-starts `BarBoxServices/scripts/dev.sh`
3. Backend seeds test data (test box, test players)
4. Game connects to local backend

### Local Editor → Remote Backend

For testing the production Fly.io backend from your local Godot editor:

```bash
# BarBoxApp/.env.local

# Remote backend
BARBOX_BACKEND_URL=https://barbox-backend.fly.dev

# Your registered test box (see "Box Registration" below)
BARBOX_BOX_ID=<your-test-box-uuid>
BARBOX_API_KEY=<your-test-box-api-key>

# IMPORTANT: Skip local backend auto-start
BARBOX_TEST_MODE=1

# Display names
BARBOX_BOX_NAME=dev_remote_test
BARBOX_VENUE_NAME=dev_venue
```

**What happens:**
1. Launch game in Godot editor
2. BackendManager skips auto-start (TEST_MODE=1)
3. Game connects directly to Fly.io backend
4. Uses your registered test box credentials

### Test/Production Machine

For physical arcade machines connecting to production:

```bash
# BarBoxApp/.env.local

# Production backend
BARBOX_BACKEND_URL=https://barbox-backend.fly.dev

# This machine's registered box (see "Box Registration" below)
BARBOX_BOX_ID=<machine-box-uuid>
BARBOX_API_KEY=<machine-api-key>

# Display names (match what you registered)
BARBOX_BOX_NAME=arcade_box_1
BARBOX_VENUE_NAME=johnnys_arcade
```

**Note:** Exported builds don't auto-start backends, so `BARBOX_TEST_MODE` isn't needed.

---

## Box Registration

Each physical machine (or test environment) needs its own registered box on the backend.

### Step 1: Generate Box ID

```bash
# macOS/Linux
TEST_BOX_ID=$(uuidgen)
echo "Box ID: $TEST_BOX_ID"

# PowerShell
$TEST_BOX_ID = [guid]::NewGuid().ToString()
Write-Host "Box ID: $TEST_BOX_ID"
```

### Step 2: Register Box

```bash
curl -X PUT "https://barbox-backend.fly.dev/box/$TEST_BOX_ID" \
  -H "Content-Type: application/json" \
  -d "{
    \"id\": \"$TEST_BOX_ID\",
    \"name\": \"My Test Box\",
    \"tag\": \"test_box_1\"
  }"
```

### Step 3: Save API Key

The response includes an `api_key` field. **Save this immediately!** The API key is only returned once during registration.

```json
{
  "id": "12345678-1234-1234-1234-123456789012",
  "name": "My Test Box",
  "tag": "test_box_1",
  "api_key": "YOUR_API_KEY_HERE_SAVE_THIS"
}
```

### Step 4: Configure Client

Create `.env.local` with the registered credentials:

```bash
BARBOX_BACKEND_URL=https://barbox-backend.fly.dev
BARBOX_BOX_ID=12345678-1234-1234-1234-123456789012
BARBOX_API_KEY=YOUR_API_KEY_HERE_SAVE_THIS
BARBOX_BOX_NAME=my_test_box
BARBOX_VENUE_NAME=my_venue
```

---

## Switching Between Environments

### Manual Config File Switching

Keep multiple config files and copy as needed:

```bash
# Save your configs
cp .env.local .env.local.remote
cp .env.local .env.local.dev

# Switch to local development
cp .env.local.dev .env.local

# Switch to remote testing
cp .env.local.remote .env.local
```

### Example Config Files

**`.env.local.dev` (Local development):**
```bash
BARBOX_BACKEND_URL=http://localhost:8000
BARBOX_BOX_ID=00000000-0000-0000-0000-000000000001
BARBOX_API_KEY=YOUR_DEV_BOX_API_KEY_HERE
BARBOX_BOX_NAME=dev_box
BARBOX_VENUE_NAME=dev_local
```

**`.env.local.remote` (Remote testing):**
```bash
BARBOX_BACKEND_URL=https://barbox-backend.fly.dev
BARBOX_BOX_ID=<your-registered-test-box>
BARBOX_API_KEY=<your-test-box-api-key>
BARBOX_TEST_MODE=1
BARBOX_BOX_NAME=dev_remote
BARBOX_VENUE_NAME=dev_venue
```

---

## Verification

### Test Backend Connectivity

```bash
# Check backend is reachable
curl https://barbox-backend.fly.dev/alive
# Expected: {"status":"alive"}
```

### Test Box Authentication

```bash
curl -X GET "https://barbox-backend.fly.dev/box/<your-box-id>" \
  -H "X-Box-API-Key: <your-api-key>"
```

### Test from Godot

1. Launch game in Godot editor
2. Check console output for:
   - "Backend is available and healthy at..."
   - No "Backend failed" errors
3. Test player registration flow

---

## Troubleshooting

### "Backend failed" Error

**Symptoms:** Error on startup, game can't connect to backend

**Solutions:**
1. Check backend is running:
   ```bash
   curl https://barbox-backend.fly.dev/alive
   ```
2. Verify `.env.local` has correct `BARBOX_BACKEND_URL`
3. Check for typos in URL (http vs https, trailing slashes)

### Local Backend Auto-Starts When Testing Remote

**Symptoms:** Godot tries to start local backend even though you want remote

**Solution:** Add `BARBOX_TEST_MODE=1` to `.env.local`:
```bash
BARBOX_TEST_MODE=1
```

### 401 Unauthorized Errors

**Symptoms:** Backend reachable but requests fail with 401

**Solutions:**
1. Verify `BARBOX_API_KEY` matches registration response
2. Check `BARBOX_BOX_ID` is correct
3. If key was lost, re-register the box with a new UUID

### 404 Box Not Found

**Symptoms:** Backend returns 404 for box operations

**Solution:** Register the box first:
```bash
curl -X PUT "https://barbox-backend.fly.dev/box/$BARBOX_BOX_ID" \
  -H "Content-Type: application/json" \
  -d '{"id": "'$BARBOX_BOX_ID'", "name": "Box Name", "tag": "box_tag"}'
```

### Empty Response / JSON Parse Errors

**Symptoms:** Backend returns empty body or malformed JSON

**Solutions:**
1. Check backend logs: `fly logs --app barbox-backend`
2. Verify request format (Content-Type header)
3. Check for network issues between client and Fly.io

---

## Security Notes

- **Never commit `.env.local`** - It's gitignored for a reason
- **API keys are secrets** - Store them securely, don't share in chat/email
- **One key per box** - Each physical machine gets its own API key
- **Key recovery** - If you lose an API key, re-register the box with a new UUID

---

## Related Documentation

- [Fly.io Deployment Guide](../../BarBoxServices/docs/FLY_IO_DEPLOYMENT_GUIDE.md) - Backend deployment
- [BarBoxServices CLAUDE.md](../../BarBoxServices/CLAUDE.md) - Backend API reference
- [.env.example](../.env.example) - Configuration template with comments
