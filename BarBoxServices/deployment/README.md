# BarBox Deployment Scripts

Scripts for deploying the BarBox arcade platform to remote Linux machines via Tailscale or local network.

**Deployment Mode:** Export builds only (versioned releases with instant rollback)

---

## Table of Contents

- [Quick Start](#quick-start)
- [⚠️ Critical Warnings](#-critical-warnings)
- [.NET Assemblies Validation](#net-assemblies-validation)
- [Deployment Modes](#deployment-modes)
- [Scripts Reference](#scripts-reference)
- [Deployment Architecture](#deployment-architecture)
- [Security Considerations](#security-considerations)
- [Operational Procedures](#operational-procedures)
- [Logging](#logging)
- [Systemd Services](#systemd-services)
- [Troubleshooting](#troubleshooting)
- [Complete Examples](#complete-examples)
- [Notes](#notes)

---

## Quick Start

### Prerequisites

**Development Machine (for creating exports):**
- Godot 4.7 with Mono support
- .NET SDK 9.0
- BarBoxApp project checked out
- Build scripts: `BarBoxApp/scripts/build-export.sh`

**Target Machine (Linux arcade terminal):**
- Ubuntu/Debian-based Linux (or compatible)
- SSH server with key authentication configured
- User account with sudo privileges
- Display server (X11/Wayland) for frontend GUI
- Internet connection (for initial dependency installation)

### First Deployment Workflow

#### Step 1: Build Export (Development Machine)

Build a versioned release export from the Godot project:

```bash
cd BarBoxApp
sh scripts/build-export.sh 2025.01.15-1430
```

This creates a release directory at `builds/releases/2025.01.15-1430/` containing:
- `BarBox.x86_64` - Executable binary (~50-100MB)
- `BarBox.pck` - Game data/scripts pack (~100-300MB)
- `VERSION` - Version metadata file
- `checksums.txt` - SHA-256 integrity hashes

#### Step 2: Deploy Export to Target

Deploy the built export to the remote machine:

```bash
cd ../BarBoxServices/deployment
./deploy.sh 100.93.137.42 --build 2025.01.15-1430
```

**What this deployment does:**
1. Transfers export binary and PCK to target machine
2. Installs system dependencies (.NET SDK 9.0, Godot 4.7, Python/uv, jq)
3. Sets up Python virtual environment for backend
4. Generates secure configuration files (`.env`, `.env.local`)
5. Registers the box with backend and obtains API key
6. Configures systemd services (optional, shows instructions)

**First deployment takes 5-10 minutes** due to dependency installation. Subsequent deployments are much faster (~1-2 minutes).

#### Step 3: Verify Deployment

Services may auto-start via systemd (if configured), or start manually:

```bash
ssh barbox@100.93.137.42
cd ~/Desktop/barbox

# Check deployed version
./get-version.sh

# Start services manually if needed
./start_all.sh

# Verify backend health
curl http://127.0.0.1:8000/alive
# Should return: {"status":"ok"}
```

### Updating Deployments

#### Full Export Update (Code Changes)

When you have code changes requiring a complete rebuild:

```bash
# 1. Build new version
cd BarBoxApp
sh scripts/build-export.sh 2025.01.16-0900

# 2. Deploy it (skip dependencies if unchanged)
cd ../BarBoxServices/deployment
./deploy.sh 100.93.137.42 --build 2025.01.16-0900 --skip-deps
```

**Time:** ~1-2 minutes (dependencies already installed)

#### PCK-Only Update (Content Changes)

When you only changed game content/scripts (no C# code changes):

```bash
# 1. Build PCK only (faster than full export)
cd BarBoxApp
sh scripts/build-pck.sh 2025.01.16-0915

# 2. Deploy PCK update
cd ../BarBoxServices/deployment
./deploy.sh 100.93.137.42 --pck-update 2025.01.16-0915
```

**Time:** ~30-60 seconds (only PCK file transferred)

**⚠️ WARNING:** PCK updates currently modify releases in-place, losing version history. See [Critical Warnings](#-critical-warnings) section.

#### Backend/Service Updates Only

When updating only Python backend code (no frontend changes):

```bash
cd BarBoxServices/deployment
./deploy.sh 100.93.137.42 --skip-deps --update
```

This syncs backend code and restarts services without touching the frontend.

---

## ⚠️ Critical Warnings

### 1. PCK Updates Modify Releases In-Place

**CURRENT BEHAVIOR** (architectural limitation):

PCK updates (`--pck-update`) currently modify the active release directory directly:
- Original PCK is overwritten and cannot be recovered
- VERSION file does not reflect PCK update version
- Rollback to previous PCK state requires full export deployment

**FUTURE BEHAVIOR** (planned improvement):

PCK updates will create new version entries with hardlinked binaries to preserve full history.

**RECOMMENDATION FOR PRODUCTION:**

Always deploy full export builds (`--build`) rather than PCK updates in production environments. Use PCK updates only for development/testing rapid iteration.

### 2. Deployment Does Not Auto-Rollback

**If deployment fails, services are left in stopped state.**

The deployment script does NOT automatically:
- Rollback to previous version on failure
- Restart services after failed deployment
- Verify deployment health

**You must manually:**
1. Check deployment logs for errors
2. Decide whether to retry or rollback
3. Manually rollback if needed (see [Emergency Rollback](#emergency-rollback-procedure))

**Future enhancement:** `--auto-rollback` flag for automatic rollback on failure.

### 3. Database Is Not Automatically Backed Up

**`BarBoxServices/app.db` is NOT backed up by deployment.**

**Before deploying to production:**
1. Back up database: `cp app.db app.db.backup-$(date +%Y%m%d)`
2. Set up automated backups (cron job or systemd timer)
3. Test restore procedure before you need it

**Example backup cron:**
```bash
# Backup database daily at 3 AM
0 3 * * * cd ~/Desktop/barbox/BarBoxServices && cp app.db backups/app.db-$(date +\%Y\%m\%d-\%H\%M\%S)
```

### 4. Logs Grow Unbounded

**Log files are NOT rotated automatically.**

Logs will eventually fill disk:
- `~/.local/share/barbox/logs/backend.log`
- `~/.local/share/barbox/logs/frontend.log`

**Set up logrotate** (see [Logging](#logging) section) or manually truncate:
```bash
> ~/.local/share/barbox/logs/backend.log
> ~/.local/share/barbox/logs/frontend.log
```

### 5. Resource Limits Only Apply to Systemd Services

**Manual scripts have NO memory or CPU limits.**

If using manual scripts in production:
- A memory leak can crash the system
- High CPU usage can make system unresponsive

**Use systemd services for production deployments** to enforce resource limits.

---

## .NET Assemblies Validation

BarBox implements comprehensive validation to ensure .NET assemblies are correctly transferred and available at every stage of the deployment pipeline. This prevents the common "unable to find the .net assemblies directory" error.

### Four-Layer Validation System

**1. Build Validation** (`build-export.sh`)
- Verifies `data_BarBox_linuxbsd_x86_64/` directory exists after Godot export
- Checks for minimum 50 files (expected ~200)
- Validates critical runtime files exist: `libcoreclr.so`, `BarBox.dll`, `libhostfxr.so`
- **Fails immediately** if assemblies directory incomplete

**2. Pre-Transfer Validation** (`deploy.sh` - before rsync)
- Verifies assemblies directory exists in local build
- Counts assembly files and ensures minimum threshold met
- **Prevents deployment** if assemblies missing from build

**3. Post-Transfer Verification** (`deploy.sh` - after rsync)
- SSH to target and counts files in deployed assemblies directory
- Verifies ~200 files arrived successfully
- **Deployment fails** if remote assemblies incomplete

**4. Startup Validation** (`start_frontend.sh`)
- Checks assemblies directory exists before launching game
- Verifies minimum file count
- **Game refuses to start** with clear error if assemblies missing

### Error Messages

Each validation layer provides clear error messages with remediation steps:

**Build Error:**
```
ERROR: .NET assemblies directory not created
Expected: builds/releases/<version>/data_BarBox_linuxbsd_x86_64
```
**Solution:** Re-export with Godot to ensure .NET assemblies included

**Pre-Transfer Error:**
```
[ERROR] .NET assemblies directory not found: builds/releases/<version>/data_BarBox_linuxbsd_x86_64
[WARN] Rebuild with: cd BarBoxApp && sh scripts/build-export.sh
```
**Solution:** Build export before deploying

**Post-Transfer Error:**
```
[ERROR] .NET assemblies transfer incomplete!
[ERROR] Found 0 files on remote (expected ~200)
```
**Solution:** Re-run deployment, check network connectivity

**Startup Error:**
```
[ERROR] .NET assemblies directory missing: ~/Desktop/barbox/current/data_BarBox_linuxbsd_x86_64
[ERROR] Re-deploy with: cd BarBoxServices/deployment && ./deploy.sh <ip> --build <version>
```
**Solution:** Deploy valid export build

### rsync Configuration

The deployment uses specific rsync syntax to transfer the assemblies directory correctly:

```bash
rsync -avz --checksum \
  "$BUILD_PATH/data_BarBox_linuxbsd_x86_64" \  # NO trailing slash
  "$TARGET:$TARGET_PATH/releases/$BUILD_VERSION/"
```

**Critical:** No trailing slash on source directory. With trailing slash, rsync copies *contents* instead of the directory itself, causing assemblies to be scattered.

### LD_LIBRARY_PATH Setup

The frontend startup script configures the .NET runtime to find assemblies:

```bash
export LD_LIBRARY_PATH="$BARBOX_ROOT/current/data_BarBox_linuxbsd_x86_64:$LD_LIBRARY_PATH"
```

This tells the .NET runtime where to find `libcoreclr.so` and other runtime libraries.

### Typical Assembly Count

A complete Godot 4.7 .NET export contains approximately:
- **202 files** in `data_BarBox_linuxbsd_x86_64/` directory
- **~85MB total size**
- Includes .NET 9.0 runtime libraries, Godot assemblies, and game DLLs

---

## Deployment Modes

BarBox supports two deployment modes:

| Feature | Export Build (Recommended) | PCK Update (Fast Iteration) |
|---------|---------------------------|------------------------------|
| **Use Case** | Production deployments | Rapid development iteration |
| **Deployment Time** | 1-2 minutes | 30-60 seconds |
| **Version Management** | Full history | Limited (in-place modification) |
| **Rollback Support** | Instant (symlink) | Limited |
| **Binary Changes** | Yes | No |
| **Content Changes** | Yes | Yes |
| **Requires Rebuild** | Yes (full export) | No (PCK only) |
| **Disk Usage** | ~150-400MB per version | Minimal (~100-300MB PCK) |
| **Status** | ✅ Recommended | ⚠️ Development only |

### Export Build Deployment

**Recommended for production.** Deploy pre-built binaries with full version management.

**Pros:**
- Versioned releases with instant rollback
- Immutable release history
- Production-ready and tested
- Blue-green deployment ready

**Cons:**
- Requires building export on development machine
- Larger initial transfer size

**Command:**
```bash
./deploy.sh <target-ip> --build <version>
```

### PCK Update Deployment

**Use for rapid development iteration only.** Updates game content without binary changes.

**Pros:**
- Very fast deployment (30-60 seconds)
- No need to rebuild entire export
- Good for content/script-only changes

**Cons:**
- Currently modifies releases in-place (loses history)
- Cannot rollback to previous PCK state
- VERSION file becomes inaccurate
- Not recommended for production

**Command:**
```bash
./deploy.sh <target-ip> --pck-update <version>
```

---

## Scripts Reference

### Development Machine Scripts

These scripts run from your development machine to deploy to remote targets.

#### deploy.sh - Unified Deployment Script

**Primary deployment tool** - handles all deployment modes.

**Usage:**
```bash
./deploy.sh <target-ip> [options]
```

**Deployment Modes:**

**1. Export Build Deployment** (Recommended)
```bash
./deploy.sh 100.93.137.42 --build 2025.01.15-1430
```

- Deploys pre-built export from `builds/releases/<version>/`
- Creates `/releases/<version>/` on target machine
- Updates `/current` symlink to new version
- Verifies checksums for integrity

**2. PCK Update Deployment** (Fast Iteration)
```bash
./deploy.sh 100.93.137.42 --pck-update 2025.01.15-1445
```

- Updates only the PCK file (game data/scripts)
- Requires existing export deployment (checks for `current` symlink)
- No binary changes, much faster than full export
- ⚠️ Modifies release in-place (see warnings)

**Options:**
- `--skip-deps` - Skip dependency installation (for updates when dependencies unchanged)
- `--skip-register` - Skip box registration (if API key already configured)
- `--update` - Quick update mode (equivalent to `--skip-deps --update`)
- `--help` - Show help message with all options

**What deploy.sh Does (Phase-by-Phase):**

**Phase 1: Code Transfer**
- Syncs BarBoxServices (Python backend)
- Transfers export build to target
- Syncs deployment scripts to target

**Phase 2: Install Dependencies** (unless `--skip-deps`)
- .NET SDK 9.0
- GodotEnv → Godot 4.7 with Mono support
- uv (Python package manager)
- jq (JSON processing)

**Phase 3: Setup Python Environment**
- Creates/updates virtual environment (`.venv`)
- Installs BarBoxServices via `uv pip install -e .`

**Phase 4: Configure Environment**
- Generates backend `.env` with secure JWT secret
- Generates frontend `.env.local` with Box ID and backend URL
- Sets file permissions to 600 (owner-only read/write)

**Phase 5: Register Box** (unless `--skip-register`)
- Starts temporary backend
- Registers box via PUT /box/{box_id}
- Obtains deterministic API key
- Updates `.env.local` with API key
- Stops temporary backend

**Phase 6: Configure Services**
- Checks if systemd services installed
- If yes: reloads daemon and restarts services
- If no: shows manual installation instructions

**Configuration Files Generated:**

**Backend `.env`:**
```bash
ENV=prod
JWT_SECRET_KEY=<openssl-rand-base64-64>  # Secure 88+ character secret
JWT_ALGORITHM=HS256
JWT_ACCESS_TOKEN_HOURS=2
SQLITE_PATH=app.db
BCRYPT_ROUNDS=12
```

**Frontend `.env.local`:**
```bash
BARBOX_BOX_ID=<uuidgen>  # Unique box identifier
BARBOX_API_KEY=<hmac-derived>  # Deterministic API key from backend
BARBOX_BACKEND_URL=http://127.0.0.1:8000
BARBOX_BOX_NAME=barbox_<hostname>
BARBOX_VENUE_NAME=venue_<hostname>
```

#### update.sh - Legacy Update Script

**DEPRECATED** - use `./deploy.sh <target-ip> --update` instead.

**Issues with update.sh:**
- Uses `rsync --delete` (removes untracked files on target)
- Interactive prompt (not suitable for automation)
- Doesn't work properly with export deployments
- No validation or error handling

#### connect.sh - SSH Helper

Quick SSH connection utility for accessing target machines.

**Usage:**
```bash
./connect.sh <target-ip> [user]
```

**Default user:** `barbox`

**Example:**
```bash
./connect.sh 100.93.137.42
# Connects as: ssh barbox@100.93.137.42
```

### Target Machine Scripts

These scripts exist on the target machine and control service lifecycle.

#### start_all.sh - Start All Services

Starts backend first, waits 3 seconds, then starts frontend.

**Usage:**
```bash
cd ~/Desktop/barbox
./start_all.sh
```

**Output:**
```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  Starting BarBox Services
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

[BarBox] Starting backend...
[Backend] Backend started (PID: 12345)
[Backend] Backend listening on http://127.0.0.1:8000

[BarBox] Waiting for backend to initialize...

[BarBox] Starting frontend...
[Frontend] Starting BarBox 2025.01.15-1430
[Frontend] Frontend started (PID: 12346)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  BarBox Started Successfully
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

#### start_backend.sh - Start Backend Service

Starts FastAPI backend server in production mode.

**Usage:**
```bash
cd ~/Desktop/barbox
./start_backend.sh
```

**Features:**
- Lock file prevents concurrent starts (`/tmp/barbox-backend.lock`)
- PID file tracking (`/tmp/barbox-backend.pid`)
- Process validation (ensures it's actually our backend, not another fastapi process)
- Logs to `~/.local/share/barbox/logs/backend.log`
- Loads configuration from `BarBoxServices/.env`

**Port:** http://127.0.0.1:8000 (localhost only, not exposed to network)

**Startup Checks:**
- Checks for existing PID file and validates process is still running
- Uses `pgrep` to detect any running `fastapi.*bxctl` processes
- Verifies backend directory and virtual environment exist
- Creates log directory if missing

**Note:** Does NOT set `ENV` environment variable (must be loaded from `.env` file). Setting `ENV` via environment would override `.env` value.

#### start_frontend.sh - Start Frontend Game

Starts Godot export binary. **Export-only, does NOT support source mode.**

**Usage:**
```bash
cd ~/Desktop/barbox
./start_frontend.sh
```

**Requirements:**
- Export deployment (`current/BarBox.x86_64` must exist)
- Backend must be running (script warns if not detected)
- `.env.local` must exist in `~/Desktop/barbox/`
- Active display session (DISPLAY environment variable set)

**Features:**
- Version display (reads `current/VERSION` file)
- Checksum verification (optional, if checksums.txt exists)
- Display configuration (defaults to DISPLAY=:0)
- Lock file prevents concurrent starts
- Logs to `~/.local/share/barbox/logs/frontend.log`

**Behavior:**
- Symlinks `.env.local` into `current/` directory if needed
- Makes binary executable if permissions incorrect
- Waits 3 seconds and verifies process is running
- Returns error if startup fails

**Error if no export deployment:**
```
[Frontend] Export binary not found at /home/barbox/Desktop/barbox/current/BarBox.x86_64
```

This means you need to deploy an export build with `./deploy.sh <ip> --build <version>`.

#### stop_all.sh - Stop All Services

Stops both frontend and backend gracefully with fallback to force kill.

**Usage:**
```bash
cd ~/Desktop/barbox
./stop_all.sh
```

**Behavior:**
1. Sends SIGTERM first (graceful shutdown, allows cleanup)
2. Waits 1 second for process to exit
3. Sends SIGKILL if still running (force kill)
4. Cleans up PID files
5. Falls back to `pkill` if PID file missing

**Process Detection:**
- Frontend: `pgrep -f "BarBox\.x86_64"`
- Backend: `pgrep -f "fastapi.*bxctl"`

#### cleanup-old-releases.sh - Remove Old Releases

Removes old export releases while keeping N most recent versions.

**Usage:**
```bash
./cleanup-old-releases.sh [keep_count]
```

**Default:** Keeps 5 most recent releases plus current version

**Examples:**
```bash
./cleanup-old-releases.sh      # Keep 5 most recent
./cleanup-old-releases.sh 3    # Keep only 3 most recent
./cleanup-old-releases.sh 10   # Keep 10 most recent
```

**Safety Features:**
- Always protects current version (even if older than recent versions)
- Requires `current` symlink to exist (won't run if no export deployment)
- Shows disk usage before and after cleanup
- Lists which versions are kept vs deleted

**Output:**
```
[Cleanup] Current version: 2025.01.15-1430
[Cleanup] Keeping 5 most recent releases

✓ Keeping current: 2025.01.15-1430
✓ Keeping recent:  2025.01.15-1200
✓ Keeping recent:  2025.01.14-1800
✗ Removing old:    2025.01.13-1500
✗ Removing old:    2025.01.12-0900

[Cleanup] Removed 2 old release(s)
[Cleanup] Total releases directory size: 850MB
```

#### get-version.sh - Query Version Information

Displays deployed version information and deployment mode.

**Usage:**
```bash
cd ~/Desktop/barbox
./get-version.sh
```

**Export Deployment Output:**
```
BarBox Version Information

Deployment Mode: Export
Current Version: 2025.01.15-1430

Version Details:
Version: 2025.01.15-1430
Built: 2025-01-15 14:30:00
Commit: a1b2c3d
Frontend: BarBoxApp@a1b2c3d
Backend: BarBoxServices@e4f5g6h

Available Releases:
  * 2025.01.15-1430 (current)
    2025.01.15-1200
    2025.01.14-1800
```

**Use Cases:**
- Verify which version is currently deployed
- List available rollback versions
- Troubleshoot "which version has this bug?" questions

---

## Deployment Architecture

### Export Deployment Directory Structure

Export deployment uses a versioned release structure with atomic symlink switching:

```
~/Desktop/barbox/
├── releases/                           # All deployed versions (immutable)
│   ├── 2025.01.15-1430/               # Version directory
│   │   ├── BarBox.x86_64              # Binary (executable, ~50-100MB)
│   │   ├── BarBox.pck                 # Game data/scripts (~100-300MB)
│   │   ├── VERSION                    # Version metadata
│   │   └── checksums.txt              # SHA-256 integrity hashes
│   ├── 2025.01.15-1200/               # Previous version (kept for rollback)
│   └── 2025.01.14-1800/               # Older version (kept for rollback)
│
├── current -> releases/2025.01.15-1430/  # Symlink to active version (atomic switch)
│
├── .env.local                          # Frontend config (persistent across deployments)
│
├── BarBoxServices/                     # Backend code and state (persistent)
│   ├── src/bxctl/                     # Python source code
│   ├── .venv/                         # Python virtual environment
│   ├── .env                           # Backend config (persistent)
│   └── app.db                         # SQLite database (persistent)
│
├── start_all.sh                        # Service control scripts
├── start_backend.sh
├── start_frontend.sh
├── stop_all.sh
├── cleanup-old-releases.sh
└── get-version.sh
```

### Deployment Patterns

**Immutable Infrastructure:**
- Each release is a complete, self-contained deployment
- Old releases are never modified (immutable)
- State and configuration kept separate from releases

**Blue-Green Deployment:**
- `current` symlink points to active version (green)
- New version deployed alongside old version (blue)
- Atomic symlink switch makes new version active
- Old version remains available for instant rollback

**Version Management:**
- Each deployment creates new `/releases/<version>/` directory
- `/current` symlink always points to active version
- Old releases preserved for rollback (managed by `cleanup-old-releases.sh`)
- Disk usage: ~150-400MB per version

**Rollback Capability:**
```bash
cd ~/Desktop/barbox
./stop_all.sh
ln -sfn releases/2025.01.14-1800 current  # Atomic symlink update
./start_all.sh
```

Rollback takes seconds (just symlink + restart).

### Phase-Based Deployment Model

**Phase 0: Pre-Flight Validation** (not currently implemented)
- Check target disk space
- Verify SSH connectivity
- Validate build files exist

**Phase 1: Code Transfer**
- Sync BarBoxServices (backend)
- Transfer export build (binary + PCK)
- Sync deployment scripts

**Phase 2: Install Dependencies** (skip with `--skip-deps`)
- .NET SDK 9.0
- Godot 4.7 + Mono via GodotEnv
- Python uv package manager
- jq JSON processor

**Phase 3: Setup Python Environment**
- Create/update virtual environment
- Install backend dependencies

**Phase 4: Configuration Generation**
- Generate backend `.env` with secure JWT secret
- Generate frontend `.env.local` with Box ID
- Set file permissions to 600

**Phase 5: Box Registration** (skip with `--skip-register`)
- Start temporary backend
- Call registration endpoint
- Obtain API key
- Update `.env.local`

**Phase 6: Service Configuration**
- Check for systemd services
- Reload daemon if needed
- Show installation instructions if not configured

**Future Phase 7: Post-Deployment Verification** (planned)
- Verify backend health endpoint
- Run smoke tests
- Auto-rollback on failure

### State vs. Code Separation

**Code (Immutable):**
- `releases/` - Version history, never modified
- Export binaries and data packs
- Service control scripts

**State (Persistent):**
- `app.db` - Database (survives deployments)
- `.env`, `.env.local` - Configuration
- `.venv` - Python environment (recreated if needed)

**Logs (Temporary):**
- `~/.local/share/barbox/logs/` - Service logs (grows unbounded)

---

## Security Considerations

### Secrets Management

**JWT Secret Generation:**
```bash
# Backend .env contains:
JWT_SECRET_KEY=<openssl rand -base64 64>  # 88+ characters
```

- Generated via `openssl rand -base64 64` (cryptographically secure)
- Unique per deployment (not shared across boxes)
- Used for signing JWT tokens for player authentication
- File permissions set to 600 (owner-only read/write)

**API Key Derivation:**
- API keys are deterministically derived from `box_id` using HMAC-SHA256
- Same box_id always produces same API key (no key distribution needed)
- Reinstalls "just work" - call registration endpoint to get same key
- No "lost key" scenario - keys can always be regenerated

**File Permissions:**
```bash
chmod 600 ~/.env                  # Backend secrets
chmod 600 ~/.env.local            # Frontend API key
```

**Security Best Practices:**
- Never commit `.env` or `.env.local` to version control
- Excluded from rsync via `--exclude` flags in deploy.sh
- Backup configuration files separately from code

### Network Security

**Topology:**
```
┌─────────────────────────────────────┐
│  BarBox Terminal (Linux)            │
│                                      │
│  Backend: 127.0.0.1:8000            │  ← Localhost only (not exposed)
│  Frontend: connects to localhost    │
│                                      │
│  Management: SSH via Tailscale VPN  │  ← Secure remote access
└─────────────────────────────────────┘
```

**Backend Security:**
- Backend binds to `127.0.0.1:8000` (localhost only)
- Not exposed to network (prevents remote attacks)
- Frontend connects via localhost HTTP (unencrypted but local-only)

**Management Access:**
- SSH access via Tailscale VPN (not public internet)
- SSH key authentication required (no password login)
- Deployment scripts use SSH public key authentication

**No TLS on Backend:**
- Traffic between frontend and backend is unencrypted HTTP
- **Risk Assessment:** Low (localhost only), MITM possible only via local user compromise
- **Acceptable for arcade terminals** (single-user, physical security)

**Firewall Recommendations** (not configured by deployment):
```bash
# Example UFW configuration for production:
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow from any to any port 22  # SSH
sudo ufw enable
```

### Systemd Security Hardening

**Current Hardening (Basic):**

**Backend Service:**
```ini
NoNewPrivileges=true     # Prevents privilege escalation
PrivateTmp=true          # Isolated /tmp directory
```

**Frontend Service:**
```ini
NoNewPrivileges=true     # Prevents privilege escalation
```

**Advanced Hardening Options** (optional, test before enabling):

```ini
# Filesystem Isolation
ProtectSystem=strict                # Read-only /usr, /boot, /efi
ProtectHome=read-only               # Protect other users' home directories
ReadWritePaths=/home/barbox/Desktop/barbox  # Exception for app directory

# Kernel Restrictions
ProtectKernelTunables=true          # Read-only /proc/sys, /sys
ProtectKernelModules=true           # Cannot load kernel modules
ProtectControlGroups=true           # Read-only /sys/fs/cgroup

# Network Isolation
RestrictAddressFamilies=AF_INET AF_INET6  # Only IPv4/IPv6

# Capability Dropping
CapabilityBoundingSet=              # Drop all capabilities
AmbientCapabilities=                # No ambient capabilities
```

**⚠️ CAUTION:** Aggressive hardening may break Godot runtime. Test incrementally.

### Security Checklist for Production

Before deploying to production environments:

- [ ] Verify `.env` contains strong JWT secret (not `dev-*` pattern)
- [ ] Confirm backend binds only to `127.0.0.1` (check listen address)
- [ ] Enable automatic security updates: `sudo dpkg-reconfigure --priority=low unattended-upgrades`
- [ ] Configure firewall (UFW): Allow only SSH, deny all other incoming
- [ ] Set up disk encryption (LUKS) for database and configuration files
- [ ] Enable audit logging: `sudo apt install auditd && sudo systemctl enable auditd`
- [ ] Configure log rotation (see [Logging](#logging) section)
- [ ] Set up automated database backups (see [Operational Procedures](#backup-procedures))
- [ ] Review systemd service hardening options
- [ ] Document incident response procedures

---

## Operational Procedures

### Deployment Validation Checklist

**Before Deploying:**
- [ ] Build export locally and verify it runs (`godot --headless` or manual launch)
- [ ] Check target disk space: `ssh <target> 'df -h ~/Desktop/barbox'` (need ~500MB free)
- [ ] Verify backend database is backed up (if production)
- [ ] Note current version for rollback: `ssh <target> './get-version.sh'`
- [ ] Communicate deployment window to users (if production)

**During Deployment:**
- [ ] Monitor deployment output for errors (especially Phase 2-5)
- [ ] Verify checksum validation passes (Phase 1)
- [ ] Check services start successfully (Phase 6 or manual start)
- [ ] Watch for SSH connection drops or timeouts

**After Deployment:**
- [ ] Verify backend health: `curl http://127.0.0.1:8000/alive` returns `{"status":"ok"}`
- [ ] Test frontend launches without errors
- [ ] Check logs for startup errors:
  - `tail -50 ~/.local/share/barbox/logs/backend.log`
  - `tail -50 ~/.local/share/barbox/logs/frontend.log`
- [ ] Perform quick smoke test:
  - Create test session
  - Record score event
  - Check leaderboard displays
- [ ] Verify current version: `./get-version.sh`
- [ ] Document deployment in change log

### Emergency Rollback Procedure

If deployment fails or new version has critical bugs:

**1. SSH to Target Machine**
```bash
ssh barbox@<target-ip>
cd ~/Desktop/barbox
```

**2. Stop Services**
```bash
./stop_all.sh
```

**3. Check Available Versions**
```bash
./get-version.sh
# Note the previous version number
```

**4. Rollback to Previous Version**
```bash
ln -sfn releases/<previous-version> current
# Example: ln -sfn releases/2025.01.14-1800 current
```

**5. Restart Services**
```bash
./start_all.sh
```

**6. Verify Health**
```bash
# Check backend
curl http://127.0.0.1:8000/alive
# Should return: {"status":"ok"}

# Check version
./get-version.sh
# Should show previous version
```

**7. Check Logs for Issues**
```bash
tail -50 ~/.local/share/barbox/logs/backend.log
tail -50 ~/.local/share/barbox/logs/frontend.log
```

**Rollback takes ~30 seconds** (symlink update + service restart).

### Backup Procedures

#### Database Backup

**Manual Backup:**
```bash
ssh barbox@<target-ip>
cd ~/Desktop/barbox/BarBoxServices
cp app.db app.db.backup-$(date +%Y%m%d-%H%M%S)
```

**Automated Backup (Cron):**
```bash
# Add to crontab: crontab -e
# Backup database daily at 3 AM
0 3 * * * cd ~/Desktop/barbox/BarBoxServices && mkdir -p backups && cp app.db backups/app.db-$(date +\%Y\%m\%d-\%H\%M\%S)

# Cleanup old backups (keep 30 days)
0 4 * * * find ~/Desktop/barbox/BarBoxServices/backups -name "app.db-*" -mtime +30 -delete
```

**Restore from Backup:**
```bash
ssh barbox@<target-ip>
cd ~/Desktop/barbox/BarBoxServices

# Stop backend
../stop_all.sh

# Restore database
cp backups/app.db-YYYYMMDD-HHMMSS app.db

# Start backend
../start_all.sh
```

#### Configuration Backup

**Backup All Configurations:**
```bash
ssh barbox@<target-ip>
cd ~/Desktop/barbox

tar czf barbox-config-$(date +%Y%m%d).tar.gz \
  BarBoxServices/.env \
  .env.local \
  current/VERSION \
  --exclude='*.log'
```

**Restore Configuration:**
```bash
tar xzf barbox-config-YYYYMMDD.tar.gz
```

### Health Monitoring

**Check Service Status:**

**Via Systemd:**
```bash
systemctl --user status barbox-backend
systemctl --user status barbox-frontend
```

**Via Process List:**
```bash
ps aux | grep -E "(fastapi.*bxctl|BarBox\.x86_64)"
```

**Via PID Files:**
```bash
test -f /tmp/barbox-backend.pid && echo "Backend running (PID: $(cat /tmp/barbox-backend.pid))" || echo "Backend stopped"
test -f /tmp/barbox-frontend.pid && echo "Frontend running (PID: $(cat /tmp/barbox-frontend.pid))" || echo "Frontend stopped"
```

**Check Resource Usage:**

**Memory:**
```bash
# Total memory used by BarBox services
ps aux | grep -E "(fastapi|BarBox)" | awk '{sum+=$6} END {printf "%.2f MB\n", sum/1024}'

# Check against limits (backend: 512MB, frontend: 2GB)
```

**CPU:**
```bash
# CPU usage
top -bn1 | grep -E "(fastapi|BarBox)"
```

**Disk:**
```bash
# Releases directory size
du -sh ~/Desktop/barbox/releases/*

# Total barbox directory
du -sh ~/Desktop/barbox

# Available disk space
df -h ~/Desktop/barbox
```

**Check Logs for Errors:**

**Recent Backend Errors:**
```bash
grep -i error ~/.local/share/barbox/logs/backend.log | tail -20
```

**Recent Frontend Errors:**
```bash
grep -i error ~/.local/share/barbox/logs/frontend.log | tail -20
```

**Systemd Journal Errors:**
```bash
journalctl --user -u barbox-backend --priority=err -n 20
journalctl --user -u barbox-frontend --priority=err -n 20
```

---

## Logging

All service logs are stored in `~/.local/share/barbox/logs/` directory.

### Backend Logs

**Location:** `~/.local/share/barbox/logs/backend.log`

**View Real-Time:**
```bash
tail -f ~/.local/share/barbox/logs/backend.log
```

**View Recent Errors:**
```bash
grep -i error ~/.local/share/barbox/logs/backend.log | tail -50
```

**Systemd Logs** (if using systemd services):
```bash
# Real-time logs
journalctl --user -u barbox-backend -f

# Last 100 lines
journalctl --user -u barbox-backend -n 100

# Errors only
journalctl --user -u barbox-backend --priority=err
```

### Frontend Logs

**Location:** `~/.local/share/barbox/logs/frontend.log`

**View Real-Time:**
```bash
tail -f ~/.local/share/barbox/logs/frontend.log
```

**View Recent Errors:**
```bash
grep -i error ~/.local/share/barbox/logs/frontend.log | tail -50
```

**Systemd Logs** (if using systemd services):
```bash
# Real-time logs
journalctl --user -u barbox-frontend -f

# Last 100 lines
journalctl --user -u barbox-frontend -n 100
```

### Log Rotation

**⚠️ WARNING:** Logs are NOT automatically rotated and will grow unbounded.

**Manual Log Truncation:**
```bash
# Truncate logs (preserves file, clears content)
> ~/.local/share/barbox/logs/backend.log
> ~/.local/share/barbox/logs/frontend.log
```

**Automated Log Rotation (Recommended for Production):**

Create `/etc/logrotate.d/barbox`:
```bash
/home/barbox/.local/share/barbox/logs/*.log {
    daily                # Rotate daily
    rotate 7             # Keep 7 days of logs
    compress             # Compress old logs
    delaycompress        # Don't compress most recent rotation
    notifempty           # Don't rotate empty logs
    missingok            # Ignore missing log files
    copytruncate         # Copy then truncate (safe for active logs)
}
```

**Test Log Rotation:**
```bash
sudo logrotate -f /etc/logrotate.d/barbox
```

**Systemd Journal Rotation:**

Systemd journals are automatically rotated based on system settings. To check:
```bash
# Check journal size
journalctl --disk-usage

# Configure max journal size (optional)
sudo systemctl edit systemd-journald
# Add:
# [Journal]
# SystemMaxUse=500M
```

### Services Won't Stop (Systemd Auto-Restart)

**Symptom:** Running `./stop_all.sh` appears to work, but services restart after 5-10 seconds.

**Cause:** Systemd services configured with `Restart=always` automatically restart processes.

**Solution:** The `stop_all.sh` script automatically detects systemd-managed services and uses `systemctl --user stop`.

**Verify systemd detection:**
```bash
./stop_all.sh
# Should show: "Frontend is managed by systemd"
```

**Manual systemd stop:**
```bash
systemctl --user stop barbox-backend barbox-frontend
```

---

## Systemd Services

Systemd services provide automatic startup, restart on failure, and resource limits.

### Service Files

#### barbox-backend.service

**Configuration:**
```ini
[Unit]
Description=BarBox Backend Service
Documentation=https://github.com/johngroot/barbox
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
WorkingDirectory=/home/barbox/Desktop/barbox/BarBoxServices
EnvironmentFile=%h/Desktop/barbox/BarBoxServices/.env
Environment="PATH=/home/barbox/.local/bin:/usr/local/bin:/usr/bin:/bin"
ExecStart=/home/barbox/Desktop/barbox/BarBoxServices/.venv/bin/fastapi run src/bxctl/app/main.py
Restart=always
RestartSec=5
StandardOutput=journal
StandardError=journal

# Resource Limits
MemoryLimit=512M
CPUQuota=50%%

# Security Hardening
NoNewPrivileges=true
PrivateTmp=true

[Install]
WantedBy=default.target
```

**Resource Limits:**
- Max memory: 512MB (backend typically uses 100-200MB)
- Max CPU: 50% of one core

**Security Features:**
- `NoNewPrivileges=true` - Prevents privilege escalation
- `PrivateTmp=true` - Isolated temporary directory
- Runs as user `barbox` (not root)

#### barbox-frontend.service

**Configuration:**
```ini
[Unit]
Description=BarBox Frontend Game
Documentation=https://github.com/johngroot/barbox
After=barbox-backend.service graphical.target
Wants=barbox-backend.service

[Service]
Type=simple
WorkingDirectory=/home/barbox/Desktop/barbox
Environment="DISPLAY=:0"
Environment="PATH=/home/barbox/.config/godotenv/godot/bin:/home/barbox/.dotnet/tools:/home/barbox/.local/bin:/usr/local/bin:/usr/bin:/bin"
ExecStartPre=/bin/sleep 3
ExecStart=/home/barbox/Desktop/barbox/start_frontend.sh
Restart=always
RestartSec=10
StandardOutput=journal
StandardError=journal

# Resource Limits
MemoryLimit=2G
CPUQuota=150%%

# Security
NoNewPrivileges=true

[Install]
WantedBy=default.target
```

**Resource Limits:**
- Max memory: 2GB (Godot game can be memory-intensive)
- Max CPU: 150% (1.5 cores, allows multi-threading)

**Service Dependencies:**
- `After=barbox-backend.service` - Waits for backend to start
- `ExecStartPre=/bin/sleep 3` - Additional 3-second delay
- Requires active display session (DISPLAY=:0)

### Installing Services

**Manual Setup (on target machine):**

```bash
# 1. Create systemd user directory
mkdir -p ~/.config/systemd/user

# 2. Install service files with correct paths
cd ~/Desktop/barbox
sed 's|/home/barbox/Desktop/barbox|'"$PWD"'|g' barbox-backend.service > ~/.config/systemd/user/barbox-backend.service
sed 's|/home/barbox/Desktop/barbox|'"$PWD"'|g' barbox-frontend.service > ~/.config/systemd/user/barbox-frontend.service

# 3. Reload systemd configuration
systemctl --user daemon-reload

# 4. Enable auto-start on boot
systemctl --user enable barbox-backend barbox-frontend

# 5. Enable lingering (services start without login)
loginctl enable-linger $USER

# 6. Start services
systemctl --user start barbox-backend barbox-frontend
```

**Verify Installation:**
```bash
systemctl --user status barbox-backend
systemctl --user status barbox-frontend
```

### Managing Services

**Check Status:**
```bash
systemctl --user status barbox-backend
systemctl --user status barbox-frontend
```

**Start Services:**
```bash
systemctl --user start barbox-backend
systemctl --user start barbox-frontend
```

**Stop Services:**
```bash
systemctl --user stop barbox-backend barbox-frontend
```

**Restart Services:**
```bash
systemctl --user restart barbox-backend
systemctl --user restart barbox-frontend
```

**View Logs:**
```bash
journalctl --user -u barbox-backend -f
journalctl --user -u barbox-frontend -f
```

### Systemd Service Management Best Practices

**Always use:** `./stop_all.sh` (auto-detects systemd or manual mode)

**Switching from systemd to manual:**
```bash
systemctl --user stop barbox-backend barbox-frontend
systemctl --user disable barbox-backend barbox-frontend
./start_all.sh
```

**Switching from manual to systemd:**
```bash
./stop_all.sh
systemctl --user enable barbox-backend barbox-frontend
systemctl --user start barbox-backend barbox-frontend
```

**Disable Auto-Start:**
```bash
systemctl --user disable barbox-backend barbox-frontend
```

**Remove Services:**
```bash
systemctl --user stop barbox-backend barbox-frontend
systemctl --user disable barbox-backend barbox-frontend
rm ~/.config/systemd/user/barbox-backend.service
rm ~/.config/systemd/user/barbox-frontend.service
systemctl --user daemon-reload
```

### Manual vs. Systemd Decision Matrix

| Feature | Manual Scripts (`start_*.sh`) | Systemd Services |
|---------|-------------------------------|------------------|
| **Auto-start on boot** | ❌ No | ✅ Yes (with `enable-linger`) |
| **Restart on crash** | ❌ No | ✅ Yes (`Restart=always`) |
| **Resource limits** | ❌ No (unlimited) | ✅ Yes (Memory, CPU) |
| **Logging** | File (`~/.local/share/barbox/logs/`) | Journald (queryable, indexed) |
| **Service dependencies** | ❌ Manual timing (sleep) | ✅ Systemd dependencies (`After=`) |
| **Lock file protection** | ✅ Yes (prevents duplicates) | ❌ N/A (systemd handles) |
| **Startup validation** | ✅ Yes (PID check) | ✅ Yes (`Type=simple`) |
| **Best for** | Development, testing | Production, unattended operation |

**Recommendation:**
- **Development/Testing:** Use manual scripts for quick iteration and debugging
- **Production:** Use systemd services for reliability, auto-restart, and resource limits

---

## Troubleshooting

### Deployment Failures

#### "Export build not found: builds/releases/\<version\>"

**Cause:** Forgot to build export before deploying.

**Fix:**
```bash
cd BarBoxApp
sh scripts/build-export.sh <version>
```

Verify build exists:
```bash
ls -lh builds/releases/<version>/
# Should show: BarBox.x86_64, BarBox.pck, VERSION, checksums.txt
```

#### "Checksum verification failed"

**Cause:** PCK or binary corrupted during transfer.

**Fix:**
```bash
# Re-deploy (rsync will resume partial transfer)
./deploy.sh <target-ip> --build <version>
```

Checksums are verified after transfer to ensure integrity.

#### ".NET assemblies directory not found" or "assemblies incomplete"

**Cause:** .NET runtime libraries missing or incomplete in export build.

**Symptoms:**
- Build error: `ERROR: .NET assemblies directory not created`
- Deployment error: `[ERROR] .NET assemblies directory not found`
- Post-transfer error: `[ERROR] .NET assemblies transfer incomplete! Found 0 files on remote`
- Startup error: `[ERROR] .NET assemblies directory missing: ~/Desktop/barbox/current/data_BarBox_linuxbsd_x86_64`
- Game crash: `unable to find the .net assemblies directory`

**Fix:**

**1. Rebuild export with .NET assemblies:**
```bash
cd BarBoxApp
sh scripts/build-export.sh <version>
```

**2. Verify assemblies exist locally:**
```bash
ls builds/releases/<version>/data_BarBox_linuxbsd_x86_64/ | wc -l
# Should show ~202 files
```

**3. Check critical runtime files:**
```bash
ls builds/releases/<version>/data_BarBox_linuxbsd_x86_64/ | grep -E "libcoreclr.so|BarBox.dll|libhostfxr.so"
# Should show all 3 files
```

**4. Re-deploy with validated build:**
```bash
cd BarBoxServices/deployment
./deploy.sh <target-ip> --build <version>
```

**5. Verify on remote after deployment:**
```bash
ssh <target> "ls ~/Desktop/barbox/current/data_BarBox_linuxbsd_x86_64/ | wc -l"
# Should show ~202 files
```

**Prevention:**
The build and deployment scripts now validate assemblies at 4 stages:
1. Build validation (build-export.sh)
2. Pre-transfer validation (deploy.sh)
3. Post-transfer verification (deploy.sh)
4. Startup validation (start_frontend.sh)

See [.NET Assemblies Validation](#net-assemblies-validation) for details.

#### "Cannot connect via SSH"

**Causes:**
1. Tailscale not running
2. SSH key not authorized
3. Target machine unreachable

**Fix:**
```bash
# 1. Check Tailscale connectivity
ping <target-ip>

# 2. Check SSH connectivity
ssh -v <target-ip> "echo SSH OK"

# 3. Authorize SSH key
ssh-copy-id barbox@<target-ip>
```

#### "Insufficient disk space"

**Cause:** Disk full from old releases or large logs.

**Fix:**
```bash
# Check disk usage
ssh <target> 'df -h ~/Desktop/barbox'

# Clean up old releases
ssh <target> 'cd ~/Desktop/barbox && ./cleanup-old-releases.sh 3'

# Truncate logs
ssh <target> '> ~/.local/share/barbox/logs/backend.log && > ~/.local/share/barbox/logs/frontend.log'
```

### Service Startup Issues

#### Backend Won't Start

**Check logs first:**
```bash
tail -50 ~/.local/share/barbox/logs/backend.log
```

**Common Issues:**

| Error Message | Cause | Solution |
|---------------|-------|----------|
| "Address already in use (port 8000)" | Another process using port 8000 | `lsof -ti :8000 \| xargs kill -9` |
| ".env file not found" | Missing backend configuration | Re-run deployment without `--skip-deps` |
| "SQLITE_PATH: unable to open database" | Database locked or corrupted | `rm BarBoxServices/app.db-journal` then restart |
| "JWT_SECRET_KEY is required" | Invalid or missing `.env` | Regenerate: `openssl rand -base64 64 > .env` |
| "ModuleNotFoundError: No module named 'bxctl'" | Virtual environment not activated or corrupted | `cd BarBoxServices && rm -rf .venv && sh ../deployment/deploy.sh <ip> --skip-register` |

**Manual Backend Start for Debugging:**
```bash
cd ~/Desktop/barbox/BarBoxServices
source .venv/bin/activate
python -m fastapi dev src/bxctl/app/main.py  # Development mode with debug output
```

#### Frontend Won't Start

**Check logs:**
```bash
tail -50 ~/.local/share/barbox/logs/frontend.log
```

**Common Issues:**

| Error Message | Cause | Solution |
|---------------|-------|----------|
| "Export binary not found at .../current/BarBox.x86_64" | No export deployment or broken symlink | Deploy export: `./deploy.sh <ip> --build <version>` |
| "Cannot connect to backend" | Backend not running | Start backend first: `./start_backend.sh` |
| "DISPLAY not set" | No X11 session or wrong DISPLAY | `export DISPLAY=:0` then retry |
| "Permission denied" | Binary not executable | `chmod +x current/BarBox.x86_64` |
| "Backend appears to be running" warning during start | Backend check failed but continuing | This is a warning, not error. Check if backend actually running. |

**Verify Export Deployment:**
```bash
cd ~/Desktop/barbox
ls -l current  # Should show: current -> releases/YYYY.MM.DD-HHMM
ls -l current/BarBox.x86_64  # Should be executable
```

### Version Management Issues

#### List Deployed Versions

```bash
cd ~/Desktop/barbox
./get-version.sh
```

Or manually:
```bash
ls -lt releases/
readlink current
```

#### Check Current Version

```bash
readlink ~/Desktop/barbox/current
# Output: releases/2025.01.15-1430
```

#### Roll Back to Previous Version

See [Emergency Rollback Procedure](#emergency-rollback-procedure)

### Service Status Checks

#### Check if Services Running

**Via PID Files:**
```bash
test -f /tmp/barbox-backend.pid && echo "Backend running" || echo "Backend not running"
test -f /tmp/barbox-frontend.pid && echo "Frontend running" || echo "Frontend not running"
```

**Via Process Search:**
```bash
ps aux | grep -E "(fastapi.*bxctl|BarBox\.x86_64)"
```

**Via Systemd:**
```bash
systemctl --user is-active barbox-backend
systemctl --user is-active barbox-frontend
```

### Performance Issues

#### Diagnose Performance Problems

**1. Check CPU Usage:**
```bash
top -bn1 | grep -E "(fastapi|BarBox)" | head -5
```

**2. Check Memory Usage:**
```bash
ps aux | grep -E "(fastapi|BarBox)" | awk '{printf "%s\t%s\t%s MB\n", $2, $11, $6/1024}'
```

**3. Check Disk I/O:**
```bash
iostat -x 1 5
```

**4. Check System Resources:**
```bash
free -h
df -h
```

**Common Causes:**

| Symptom | Likely Cause | Solution |
|---------|--------------|----------|
| Backend using > 512MB RAM | Memory leak | Restart backend service |
| Frontend using > 2GB RAM | Texture/resource leak in game | Restart frontend, check Godot profiler |
| High CPU usage | Infinite loop or busy-wait | Check logs for errors, restart service |
| Slow database queries | Database file on slow disk | Move `app.db` to SSD |
| Disk space low | Old releases or large logs | Run `cleanup-old-releases.sh`, truncate logs |

### API Key Issues

#### API Key Not Working After Reinstall

**Cause:** API keys are deterministic (derived from box_id), so this shouldn't happen unless box_id changed.

**Verify Box ID:**
```bash
grep BARBOX_BOX_ID ~/Desktop/barbox/.env.local
```

**Re-register Box:**
```bash
# Get box_id from .env.local
BOX_ID=$(grep BARBOX_BOX_ID ~/Desktop/barbox/.env.local | cut -d'=' -f2)

# Re-register (will return same deterministic API key)
curl -X PUT "http://127.0.0.1:8000/box/$BOX_ID" \
  -H "Content-Type: application/json" \
  -d "{\"id\": \"$BOX_ID\", \"name\": \"My Box\", \"tag\": \"mybox\"}"

# Response contains api_key field - update .env.local if needed
```

---

## Complete Examples

### Example 1: First Production Deployment

Complete workflow for deploying to a new arcade terminal from scratch:

```bash
# === ON DEVELOPMENT MACHINE ===

# 1. Build export
cd BarBoxApp
sh scripts/build-export.sh 2025.01.15-prod-v1

# Verify build created
ls -lh builds/releases/2025.01.15-prod-v1/
# Should show: BarBox.x86_64 (executable), BarBox.pck, VERSION, checksums.txt

# 2. Deploy to target (first deployment, installs everything)
cd ../BarBoxServices/deployment
./deploy.sh 192.168.1.100 --build 2025.01.15-prod-v1

# === DEPLOYMENT RUNS (5-10 minutes) ===
# [INFO] Testing SSH connection...
# [INFO] Phase 1: Transferring Code
# [INFO] Phase 2: Installing Dependencies (.NET, Godot, Python...)
# [INFO] Phase 3: Setting Up Python Environment
# [INFO] Phase 4: Configuring Environment
# [INFO] Phase 5: Registering Box
# [INFO] Box registered successfully
# [INFO] API Key: abc123...
# [INFO] Phase 6: Service Configuration
# [INFO] Deployment Complete!

# === ON TARGET MACHINE (verify) ===

ssh barbox@192.168.1.100
cd ~/Desktop/barbox

# Check version
./get-version.sh
# Output:
#   Deployment Mode: Export
#   Current Version: 2025.01.15-prod-v1

# Verify services started
systemctl --user status barbox-backend
systemctl --user status barbox-frontend

# If services not running, start manually
./start_all.sh

# Check backend health
curl http://127.0.0.1:8000/alive
# Output: {"status":"ok"}

# Check logs for errors
tail -50 ~/.local/share/barbox/logs/backend.log
tail -50 ~/.local/share/barbox/logs/frontend.log
```

### Example 2: Updating to Fix a Bug

Deploying a new version with bug fix, with rollback if needed:

```bash
# === ON DEVELOPMENT MACHINE ===

# 1. Fix bug in code, commit changes
cd BarBoxApp
git commit -am "Fix racing timing system bug"

# 2. Build new version
sh scripts/build-export.sh 2025.01.16-bugfix-racing

# 3. Deploy update (dependencies already installed, skip them)
cd ../BarBoxServices/deployment
./deploy.sh 192.168.1.100 --build 2025.01.16-bugfix-racing --skip-deps --skip-register

# === DEPLOYMENT RUNS (1-2 minutes) ===
# Much faster than first deployment (skips dependency installation)

# === ON TARGET MACHINE (verify) ===

ssh barbox@192.168.1.100
cd ~/Desktop/barbox

# Verify new version deployed
./get-version.sh
# Should show: Current Version: 2025.01.16-bugfix-racing

# Test the bug fix...
# If bug still exists or new issues found:

# === ROLLBACK IMMEDIATELY ===

./stop_all.sh

# Check available versions
./get-version.sh
# Shows: Available Releases:
#          * 2025.01.16-bugfix-racing (current)
#            2025.01.15-prod-v1
#            2025.01.14-test

# Rollback to previous version
ln -sfn releases/2025.01.15-prod-v1 current

# Restart services
./start_all.sh

# Verify rollback
./get-version.sh
# Should show: Current Version: 2025.01.15-prod-v1

curl http://127.0.0.1:8000/alive
```

**Rollback took ~30 seconds** (symlink + restart).

### Example 3: PCK Update for Content Changes

Fast iteration for game content/script changes without C# recompilation:

```bash
# === ON DEVELOPMENT MACHINE ===

# 1. Changed game content (e.g., UI text, game data, GDScript)
# No C# code changes

# 2. Build PCK only (much faster than full export)
cd BarBoxApp
sh scripts/build-pck.sh 2025.01.16-content-update

# Verify PCK created
ls -lh builds/updates/2025.01.16-content-update/
# Should show: BarBox.pck

# 3. Deploy PCK update (very fast)
cd ../BarBoxServices/deployment
./deploy.sh 192.168.1.100 --pck-update 2025.01.16-content-update

# === DEPLOYMENT RUNS (30-60 seconds) ===
# Only PCK file transferred and replaced

# === ON TARGET MACHINE (verify) ===

ssh barbox@192.168.1.100
cd ~/Desktop/barbox

# Check version
./get-version.sh
# NOTE: VERSION file still shows original version (PCK update limitation)
# Output: Current Version: 2025.01.15-1430 (not updated to reflect PCK change)

# Verify services restarted
ps aux | grep -E "(fastapi|BarBox)"

# Test content changes in game
```

**⚠️ WARNING:** PCK updates modify release in-place. If you need to rollback the content change, you must deploy a full export build.

### Example 4: Emergency Rollback

Complete rollback procedure when critical issue discovered in production:

```bash
# === CRITICAL BUG DISCOVERED IN PRODUCTION ===

# SSH to target immediately
ssh barbox@192.168.1.100
cd ~/Desktop/barbox

# 1. Stop all services immediately
./stop_all.sh
# Output:
#   [BarBox] Stopping frontend (PID: 12346)...
#   [BarBox] Frontend stopped
#   [BarBox] Stopping backend (PID: 12345)...
#   [BarBox] Backend stopped

# 2. Check available versions for rollback
./get-version.sh
# Output:
#   Deployment Mode: Export
#   Current Version: 2025.01.16-broken
#
#   Available Releases:
#     * 2025.01.16-broken (current)
#       2025.01.15-prod-v1
#       2025.01.14-test

# 3. Identify last known good version: 2025.01.15-prod-v1

# 4. Perform rollback (atomic symlink update)
ln -sfn releases/2025.01.15-prod-v1 current

# 5. Verify symlink updated
readlink current
# Output: releases/2025.01.15-prod-v1

# 6. Restart services with rolled-back version
./start_all.sh
# Output:
#   [BarBox] Starting backend...
#   [Backend] Backend started (PID: 23456)
#   [BarBox] Starting frontend...
#   [Frontend] Starting BarBox 2025.01.15-prod-v1
#   [Frontend] Frontend started (PID: 23457)

# 7. Verify services healthy
curl http://127.0.0.1:8000/alive
# Output: {"status":"ok"}

# 8. Verify correct version running
./get-version.sh
# Output:
#   Current Version: 2025.01.15-prod-v1

# 9. Check logs for any startup issues
tail -50 ~/.local/share/barbox/logs/backend.log
tail -50 ~/.local/share/barbox/logs/frontend.log

# 10. Test critical functionality
# (manually verify game works correctly)

# === ROLLBACK COMPLETE ===
# Total time: ~30-60 seconds
# Services back online with last known good version
```

---

## Notes

### Deployment Modes Summary

- **Export deployment (recommended)** - Deploy pre-built binaries with full version management, instant rollback
- **PCK updates** - Fast updates for game content/script changes only (development use)

### Version Management

- Export builds stored in `releases/<version>/` (immutable)
- `current` symlink points to active version (atomic switchover)
- Old releases preserved for rollback (managed by `cleanup-old-releases.sh`)
- Each version is ~150-400MB (binary + PCK + metadata)
- Use `cleanup-old-releases.sh` to manage disk usage (default: keep 5 most recent)

### Resource Limits

**Systemd services enforce limits:**
- Backend: 512MB RAM, 50% CPU
- Frontend: 2GB RAM, 150% CPU (1.5 cores)

**Manual scripts have NO limits** - use systemd for production.

**Adjust limits in service files if needed:**
```ini
# Edit ~/.config/systemd/user/barbox-backend.service
MemoryLimit=1G      # Increase if needed
CPUQuota=100%       # Increase if needed
```

Then reload: `systemctl --user daemon-reload && systemctl --user restart barbox-backend`

### Security

- **API keys:** Deterministically derived from box_id (HMAC-SHA256), same box_id always produces same key
- **JWT secrets:** Generated securely via `openssl rand -base64 64` (88+ characters)
- **Backend binding:** Localhost only (`127.0.0.1:8000`), not exposed to network
- **File permissions:** `.env` and `.env.local` set to 600 (owner-only read/write)
- **Systemd hardening:** `NoNewPrivileges=true`, `PrivateTmp=true`

### Auto-Start Configuration

- Services run as user `barbox` (not root)
- Systemd user services (not system services)
- Requires `loginctl enable-linger barbox` for boot startup without login
- Frontend requires active display session (`DISPLAY=:0`)

### File Permissions

- `.env` and `.env.local`: 600 (owner read/write only, contains secrets)
- Export binaries: 755 (executable)
- Service files: 644 (readable, installed to `~/.config/systemd/user/`)
- Scripts: 755 (executable)

### Database Management

- SQLite database: `BarBoxServices/app.db`
- Persists across deployments (not included in releases)
- **NOT automatically backed up** - set up backup strategy (see [Operational Procedures](#backup-procedures))
- Use `cp app.db app.db.backup-$(date +%Y%m%d)` before major changes

### API Key System (Deterministic)

The API key system uses deterministic key derivation:

- **API keys are deterministically derived** from `box_id` using HMAC-SHA256
- **Same box_id always produces the same API key**
- **Reinstalls just work** - call the registration endpoint again to get your key
- **No "lost key" scenario** - keys can always be regenerated

This means:
- You don't need to save the API key on first registration
- If you reinstall, just run `deploy.sh` again and it will get the same API key
- The `PUT /box/{box_id}` endpoint always returns the API key

### Cross-References

- **Build Scripts:** `BarBoxApp/scripts/` (`build-export.sh`, `build-pck.sh`, `test-export.sh`)
- **Backend Documentation:** `BarBoxServices/README.md`, `BarBoxServices/CLAUDE.md`
- **Frontend Documentation:** `BarBoxApp/CLAUDE.md`
- **Architecture Decisions:** `BarBoxServices/docs/architecture/`

### Known Issues

1. **PCK updates modify releases in-place** - Loses version history, cannot rollback to previous PCK state. Future fix: copy-on-write with hardlinked binaries.

2. **No automatic rollback on deployment failure** - Must manually rollback if deployment fails. Future enhancement: `--auto-rollback` flag.

3. **Logs grow unbounded** - Must set up logrotate or manually truncate logs. See [Logging](#logging) section.

4. **No pre-flight validation** - Deployment doesn't check disk space, architecture compatibility before starting. Future enhancement: validation phase.

### Future Enhancements

- [ ] PCK update copy-on-write architecture (preserve version history)
- [ ] Automatic rollback on deployment failure
- [ ] Pre-flight validation (disk space, architecture, dependencies)
- [ ] Post-deployment health checks and smoke tests
- [ ] Deployment audit trail (who deployed what when)
- [ ] Enhanced VERSION metadata (git commits, deployment timestamps)
- [ ] Log rotation configuration included in deployment
- [ ] Resource monitoring and alerting

---

**For questions or issues, consult:**
- This README (comprehensive deployment guide)
- `BarBoxServices/CLAUDE.md` (backend development guide)
- `BarBoxApp/CLAUDE.md` (frontend development guide)
- Project repository: https://github.com/johngroot/barbox
