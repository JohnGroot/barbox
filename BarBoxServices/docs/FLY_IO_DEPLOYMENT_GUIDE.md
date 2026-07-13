# Fly.io Deployment Plan - BarBoxServices Backend

## Executive Summary

Quick-start guide to deploy the BarBoxServices FastAPI backend to Fly.io using SQLite on a persistent volume.

**Deployment Method:** Docker container with Fly.io persistent volume for SQLite database
**Timeline:** 1-2 hours for initial deployment
**Cost Estimate:** $3-5/month (shared-cpu-1x with 512MB RAM + 1GB volume)

> **Status note:** The deployment files this guide describes creating
> (`Dockerfile`, `.dockerignore`, `fly.toml`) already exist in
> `BarBoxServices/` — treat Phase 1 as reference for how they work, and verify
> the committed files against the snippets here before deploying.
>
> **App-name note:** The deployed app per the committed `fly.toml` is
> **`barbox-backend`**. Commands in later sections that reference
> `barbox-backend-prod` / `barbox-backend-staging` describe a proposed
> multi-environment naming convention that is not currently in use —
> substitute `barbox-backend` unless/until separate environments are created.

---

## Prerequisites

### Local Machine Setup
```bash
# Install Fly.io CLI
curl -L https://fly.io/install.sh | sh

# Login to Fly.io
fly auth login

# Verify installation
fly version
```

### Required Files to Create
1. `Dockerfile` - Container image definition
2. `.dockerignore` - Exclude unnecessary files from build
3. `fly.toml` - Fly.io configuration
4. `.env.production` - Production environment variables (template)

---

## Phase 1: Create Deployment Files

### File 1: `Dockerfile`

Location: `BarBoxServices/Dockerfile`

```dockerfile
# Use official Python 3.13 slim image
FROM python:3.13-slim

# Install uv package manager
RUN pip install uv

# Set working directory
WORKDIR /app

# Copy project files
COPY pyproject.toml uv.lock ./
COPY src/ ./src/

# Install dependencies
RUN uv venv .venv && \
    . .venv/bin/activate && \
    uv pip install .

# Expose port 8080 (Fly.io standard)
EXPOSE 8080

# Set environment variables
ENV PYTHONUNBUFFERED=1
ENV ENV=prod

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD python -c "import urllib.request; urllib.request.urlopen('http://localhost:8080/alive').read()"

# Run FastAPI with uvicorn
CMD [".venv/bin/uvicorn", "bxctl.web.main:app", "--host", "0.0.0.0", "--port", "8080"]
```

**Key Points:**
- Uses Python 3.13 slim (smaller image size)
- Installs `uv` for fast dependency resolution
- Binds to `0.0.0.0:8080` (Fly.io requirement)
- Includes health check using `/alive` endpoint
- Sets `ENV=prod` to disable test endpoints

---

### File 2: `.dockerignore`

Location: `BarBoxServices/.dockerignore`

```
# Python
__pycache__/
*.py[cod]
*$py.class
*.so
.Python
*.egg-info/
dist/
build/

# Virtual environments
.venv/
venv/
env/

# Database files (will be on volume)
*.db
*.db-shm
*.db-wal
app.db.ready

# IDE
.vscode/
.idea/
*.swp
*.swo

# Git
.git/
.gitignore

# Documentation
docs/
*.md
README.md

# Tests
test/
tests/

# Deployment scripts (not needed in container)
deployment/
scripts/

# Environment files (secrets managed via Fly.io)
.env
.env.*
```

---

### File 3: `fly.toml`

Location: `BarBoxServices/fly.toml`

```toml
# Fly.io configuration for BarBoxServices backend

app = "barbox-backend"
primary_region = "ord"  # Chicago - Central US location for lower latency to midwest arcade installations

[build]
  dockerfile = "Dockerfile"

[env]
  ENV = "prod"
  SQLITE_PATH = "/data/app.db"  # SQLite on persistent volume

[http_service]
  internal_port = 8080
  force_https = true
  auto_stop_machines = false  # Keep running (backend should always be available)
  auto_start_machines = true
  min_machines_running = 1

# Health check configuration
[[http_service.checks]]
  grace_period = "10s"
  interval = "30s"
  method = "GET"
  timeout = "5s"
  path = "/alive"

[[vm]]
  size = "shared-cpu-1x"
  memory = "512mb"

# Persistent volume for SQLite database
[[mounts]]
  source = "barbox_data"
  destination = "/data"
  initial_size = "1gb"
```

**Key Configuration:**
- `primary_region`: Set to nearest datacenter (Chicago `ord` for central US deployments)
- `SQLITE_PATH=/data/app.db`: Database stored on persistent volume
- `auto_stop_machines=false`: Backend stays running (no auto-sleep)
- Health check: Uses existing `/alive` endpoint
- Volume: 1GB persistent storage for SQLite database

---

### File 4: Environment Configuration

**For Local Development:** Create `BarBoxServices/.env`

```bash
# BarBoxServices Environment Configuration

# Environment mode (local, test, prod)
ENV=local

# Database configuration
SQLITE_PATH=app.db

# JWT Configuration
# Generate a secure secret with: openssl rand -base64 64
JWT_SECRET_KEY=dev-secret-UNSAFE-change-in-production
JWT_ALGORITHM=HS256
JWT_ACCESS_TOKEN_HOURS=2

# Bcrypt Configuration
BCRYPT_ROUNDS=12

# CORS Configuration
CORS_ORIGINS=*
CORS_ALLOW_CREDENTIALS=true
CORS_ALLOW_METHODS=*
CORS_ALLOW_HEADERS=*

# Database Management (dev only)
DROP_DB_ON_STARTUP=false

# Redis Configuration (optional, for future use)
REDIS_URL=
```

**For Production (Fly.io):** Use `fly secrets set` instead of `.env` file

```bash
# Generate strong JWT secret first
JWT_SECRET=$(openssl rand -base64 64)

# Set secrets on Fly.io
fly secrets set \
  ENV=prod \
  SQLITE_PATH=/data/app.db \
  JWT_SECRET_KEY="$JWT_SECRET" \
  JWT_ALGORITHM=HS256 \
  JWT_ACCESS_TOKEN_HOURS=2 \
  BCRYPT_ROUNDS=12 \
  CORS_ORIGINS=* \
  CORS_ALLOW_CREDENTIALS=true \
  CORS_ALLOW_METHODS=* \
  CORS_ALLOW_HEADERS=* \
  DROP_DB_ON_STARTUP=false \
  --app barbox-backend-prod
```

**Important Notes:**
- Never commit `.env` files with real secrets
- Production uses Fly.io secrets (not `.env` files)
- `SQLITE_PATH=/data/app.db` for Fly.io persistent volume
- Generate unique `JWT_SECRET_KEY` for each environment

---

## Phase 2: Initial Deployment

### Step 1: Generate Production Secrets

```bash
# Generate strong JWT secret
openssl rand -base64 64

# Example output (DO NOT USE - generate your own):
# your-unique-base64-secret-here-replace-before-production
```

### Step 2: Create Fly.io App

```bash
cd BarBoxServices

# Launch app (creates app and provisions resources)
fly launch --no-deploy

# This will:
# - Detect your Dockerfile
# - Create fly.toml (or use existing)
# - Ask you to choose region (select nearest datacenter)
# - Ask about PostgreSQL (select NO - we're using SQLite)
```

### Step 3: Create Persistent Volume

```bash
# Create 1GB volume for SQLite database
fly volumes create barbox_data --region ord --size 1

# Verify volume created
fly volumes list
```

**Important:** Volume must be created in the same region as your app.

### Step 4: Set Production Secrets

```bash
# Generate strong JWT secret
JWT_SECRET=$(openssl rand -base64 64)

# Set ALL production secrets (JWT + Stripe)
fly secrets set \
  JWT_SECRET_KEY="$JWT_SECRET" \
  STRIPE_SECRET_KEY="sk_live_YOUR_LIVE_KEY" \
  STRIPE_WEBHOOK_SECRET="whsec_YOUR_WEBHOOK_SECRET" \
  STRIPE_PRICE_5_CREDITS="price_..." \
  STRIPE_PRICE_10_CREDITS="price_..." \
  STRIPE_PRICE_25_CREDITS="price_..." \
  STRIPE_PRICE_50_CREDITS="price_..." \
  STRIPE_PRICE_100_CREDITS="price_..."

# Verify secrets (values will be redacted)
fly secrets list
```

**Important:** Replace the Stripe placeholders with your actual keys from the Stripe Dashboard. See the [Stripe Integration Configuration](#stripe-integration-configuration) section for details on obtaining these values.

### Step 5: Deploy Application

```bash
# Deploy to Fly.io
fly deploy

# Monitor deployment logs
fly logs
```

**Expected Output:**
```
==> Building image
...
==> Pushing image to Fly.io
...
==> Deploying barbox-backend
...
✓ Deployment successful!
```

### Step 6: Verify Deployment

```bash
# Check application status
fly status

# Test health check endpoint
curl https://barbox-backend.fly.dev/alive

# Expected response: {"status":"alive"}
```

---

## Phase 3: Database Initialization

Since this is the first deployment, the database will be empty. You need to initialize the schema:

### Option A: Automatic Schema Creation (Recommended)

The FastAPI lifespan manager automatically creates database schema on startup. No manual steps needed!

**How it works:**
- On app startup, `main.py` lifespan runs `Base.metadata.create_all()`
- Creates all tables from SQLAlchemy models
- Database file: `/data/app.db` on persistent volume

### Option B: Manual Verification

```bash
# SSH into the running container
fly ssh console

# Check database file exists
ls -lh /data/app.db

# Verify backend is running (curl not available in slim image, use Python)
python -c "import urllib.request; print(urllib.request.urlopen('http://localhost:8080/alive').read().decode())"

# Exit SSH session
exit
```

---

## Phase 4: Register First Box

After deployment, you'll need to register your first arcade box:

```bash
# Generate a UUID for your box
NEW_BOX_ID=$(uuidgen)
echo "Box ID: $NEW_BOX_ID"

# Register box
curl -X POST https://barbox-backend.fly.dev/box/ \
  -H "Content-Type: application/json" \
  -d "{
    \"id\": \"$NEW_BOX_ID\",
    \"name\": \"Test Box 1\",
    \"tag\": \"box1\"
  }"
```

**Response:** Returns box details including the generated API key.

**Save the API key!** You'll need it for the Godot client configuration.

---

## Common Operations

### View Application Logs
```bash
# Live tail logs
fly logs

# View recent logs
fly logs --recent
```

### Restart Application
```bash
fly apps restart barbox-backend
```

### Scale Resources
```bash
# Increase memory to 1GB
fly scale memory 1024

# Switch to 2x CPU
fly scale vm shared-cpu-2x
```

### Access Database
```bash
# SSH into container
fly ssh console

# Connect to SQLite
sqlite3 /data/app.db

# Run queries
sqlite> SELECT * FROM box;
sqlite> .quit
```

### Update Secrets
```bash
# Update JWT secret (will trigger redeploy)
fly secrets set JWT_SECRET_KEY="new-secret-here"

# Update multiple secrets at once
fly secrets set CORS_ORIGINS="https://yourdomain.com" JWT_ACCESS_TOKEN_HOURS="4"

# Rotate Stripe webhook secret (after regenerating in Stripe Dashboard)
fly secrets set STRIPE_WEBHOOK_SECRET="whsec_new_secret"

# Switch from test to live Stripe keys (production launch)
fly secrets set STRIPE_SECRET_KEY="sk_live_..."
```

---

## Deployment Checklist

### Pre-Deployment
- [ ] Install Fly.io CLI (`fly version`)
- [ ] Login to Fly.io (`fly auth login`)
- [ ] Generate JWT secret (`openssl rand -base64 64`)
- [ ] Create `Dockerfile`, `.dockerignore`, `fly.toml`

### Initial Deployment
- [ ] Create Fly.io app (`fly launch --no-deploy`)
- [ ] Create persistent volume (`fly volumes create barbox_data`)
- [ ] Set production secrets (`fly secrets set JWT_SECRET_KEY=...`)
- [ ] Deploy application (`fly deploy`)
- [ ] Verify health check (`curl https://barbox-backend.fly.dev/alive`)

### Post-Deployment
- [ ] Register first box via API
- [ ] Save box API key for Godot client
- [ ] Configure Godot client to point to `https://barbox-backend.fly.dev`
- [ ] Test player registration and login from Godot client

---

## Cost Breakdown

| Resource | Specs | Cost/Month |
|----------|-------|------------|
| **Compute** | shared-cpu-1x, 512MB | $3.00 |
| **Volume** | 1GB persistent storage | $0.15 |
| **Bandwidth** | 100GB included | $0 (likely) |
| **Total** | | **~$3.15/month** |

**Free Tier:** Fly.io offers $5/month credit on free tier - this deployment should be covered!

---

## Troubleshooting

### Issue: Health check failing
```bash
# Check application logs
fly logs

# Verify /alive endpoint responds (inside container)
fly ssh console
python -c "import urllib.request; print(urllib.request.urlopen('http://localhost:8080/alive').read().decode())"
```

**Solution:** Ensure FastAPI app is binding to `0.0.0.0:8080` in Dockerfile CMD.

### Issue: Database not persisting
```bash
# Verify volume is mounted
fly ssh console
ls -lh /data/
```

**Solution:** Ensure `fly.toml` mounts section matches volume name (`barbox_data`).

### Issue: Deployment stuck
```bash
# Cancel current deployment
fly deploy --detach=false

# Check machine status
fly status
```

**Solution:** Destroy and recreate machine if stuck:
```bash
fly machines list
fly machines destroy <machine-id>
fly deploy
```

### Issue: Out of memory
```bash
# Check resource usage
fly status

# Scale up memory
fly scale memory 1024
```

**Note:** 512MB is the recommended minimum for this FastAPI application. Do not reduce below this threshold as webhook processing and database operations can spike memory usage.

### Issue: App fails to start with Stripe error
```bash
# Check logs for startup validation errors
fly logs --recent
```

**Common errors:**
- `Production environment requires STRIPE_SECRET_KEY` → Set the secret: `fly secrets set STRIPE_SECRET_KEY="sk_live_..."`
- `Production environment requires STRIPE_WEBHOOK_SECRET` → Set the secret: `fly secrets set STRIPE_WEBHOOK_SECRET="whsec_..."`

### Issue: Stripe webhooks not received
```bash
# Verify webhook endpoint is reachable
curl -X POST https://your-app.fly.dev/payments/webhook \
  -H "Content-Type: application/json" \
  -d '{}'

# Expected: 400 error (signature validation failed, but endpoint is reachable)
```

**Solutions:**
1. Verify webhook URL in Stripe Dashboard matches your Fly.io URL
2. Ensure `checkout.session.completed` event is selected
3. Check webhook signing secret matches `STRIPE_WEBHOOK_SECRET`

### Issue: Webhook signature verification failed
```bash
# Check logs for signature errors
fly logs | grep -i "signature"
```

**Solution:** The webhook secret may be incorrect or rotated. Get the current signing secret from Stripe Dashboard → Developers → Webhooks, then update:
```bash
fly secrets set STRIPE_WEBHOOK_SECRET="whsec_correct_secret"
```

---

## Security Considerations

### Basic Security (Required)

- **JWT Secret:** Strong random secret (64+ characters)
  ```bash
  openssl rand -base64 64
  fly secrets set JWT_SECRET_KEY="<generated-secret>" --app barbox-backend-prod
  ```
- **HTTPS:** Enforced by Fly.io (`force_https = true` in fly.toml)
- **Rate Limiting:** Automatically enabled in production mode via slowapi

### CORS Configuration

**Development/Staging:**
```bash
fly secrets set CORS_ORIGINS="*" --app barbox-backend-staging
```

**Production (Recommended):**
```bash
# Restrict to specific domains
fly secrets set CORS_ORIGINS="https://yourdomain.com,https://app.yourdomain.com" --app barbox-backend-prod
```

**Configuration in `.env.production`:**
```bash
# Replace wildcard with actual domain(s)
CORS_ORIGINS=https://yourdomain.com
CORS_ALLOW_CREDENTIALS=true
CORS_ALLOW_METHODS=*
CORS_ALLOW_HEADERS=*
```

### JWT Secret Rotation

Rotate JWT secrets every 90 days for security:

```bash
# Generate new secret
NEW_SECRET=$(openssl rand -base64 64)

# Update secret (triggers automatic redeployment)
fly secrets set JWT_SECRET_KEY="$NEW_SECRET" --app barbox-backend-prod

# Verify deployment succeeded
fly logs --app barbox-backend-prod
fly status --app barbox-backend-prod
```

**Note:** Rotating the JWT secret will invalidate all existing user sessions. Plan rotations during maintenance windows.

### Security Headers Middleware

Add security headers to protect against common vulnerabilities. Add this middleware to `src/bxctl/web/main.py`:

```python
from fastapi import Request

@app.middleware("http")
async def add_security_headers(request: Request, call_next):
    response = await call_next(request)
    response.headers["X-Frame-Options"] = "DENY"
    response.headers["X-Content-Type-Options"] = "nosniff"
    response.headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains"
    response.headers["X-XSS-Protection"] = "1; mode=block"
    return response
```

**Headers explained:**
- `X-Frame-Options: DENY` - Prevents clickjacking attacks
- `X-Content-Type-Options: nosniff` - Prevents MIME type sniffing
- `Strict-Transport-Security` - Enforces HTTPS connections
- `X-XSS-Protection` - Enables browser XSS protection

### Rate Limiting Configuration

The backend uses `slowapi` for rate limiting, automatically enabled in production mode (`ENV=prod`).

**Default limits** (configured in application code):
- API endpoints: 100 requests per minute per IP
- Authentication: 10 requests per minute per IP

**To customize**, update environment variables:
```bash
fly secrets set RATE_LIMIT_PER_MINUTE="200" --app barbox-backend-prod
```

### Database Security

**Backup Strategy:**
```bash
# Create volume snapshot before deployments
fly volumes snapshots create barbox_data_prod --app barbox-backend-prod

# List all snapshots
fly volumes snapshots list barbox_data_prod --app barbox-backend-prod

# Restore from snapshot (creates new volume)
fly volumes create barbox_data_restore --snapshot-id snap_xxx --app barbox-backend-prod
```

**Recommended backup schedule:**
- Daily automated snapshots (set up via cron or GitHub Actions)
- Manual snapshot before each deployment
- Retain snapshots for 30 days

**Encryption:** Fly.io volumes are encrypted at rest by default.

### Monitoring & Logging

**Sentry Integration** (Error tracking):
```bash
# Create Sentry extension
fly ext sentry create --app barbox-backend-prod

# Set DSN in secrets
fly secrets set SENTRY_DSN="<sentry-dsn>" --app barbox-backend-prod
```

**Log Aggregation:**
```bash
# View live logs
fly logs --app barbox-backend-prod

# Export logs to external service (optional)
# Configure log shipping to Datadog, Papertrail, etc.
```

**Health Check Monitoring:**
- Internal: Fly.io health checks via `/alive` endpoint
- External: Use uptime monitoring service (UptimeRobot, Pingdom, etc.)
- Monitor URL: `https://barbox-backend-prod.fly.dev/alive`

### Secrets Management Best Practices

**Never commit secrets to git:**
- `.env` files are in `.gitignore`
- Use `fly secrets set` for production secrets
- Use GitHub Secrets for CI/CD automation

**Secrets rotation schedule:**
- JWT secrets: Every 90 days
- API keys: When compromised or team member leaves
- Database credentials: Every 180 days (if using PostgreSQL in future)

**Audit secrets:**
```bash
# List all secrets (values are redacted)
fly secrets list --app barbox-backend-prod

# Remove unused secrets
fly secrets unset OLD_SECRET_NAME --app barbox-backend-prod
```

---

## Stripe Integration Configuration

This section covers setting up Stripe for in-app credit purchases.

### Obtaining Stripe Keys

**API Keys (Stripe Dashboard → Developers → API Keys):**

| Key Type | Format | Usage |
|----------|--------|-------|
| **Test Secret Key** | `sk_test_...` | Local development and staging |
| **Live Secret Key** | `sk_live_...` | Production only |

**Webhook Signing Secret (Stripe Dashboard → Developers → Webhooks):**

1. Create a new webhook endpoint:
   - URL: `https://your-app.fly.dev/payments/webhook`
   - Events: `checkout.session.completed`
2. Copy the "Signing secret" (starts with `whsec_`)

### Secrets Reference

| Secret | Purpose | Where to Find |
|--------|---------|---------------|
| `STRIPE_SECRET_KEY` | API authentication | Stripe Dashboard → Developers → API Keys |
| `STRIPE_WEBHOOK_SECRET` | Webhook signature verification | Stripe Dashboard → Developers → Webhooks → Signing secret |
| `STRIPE_PRICE_*` | Credit pack pricing | Stripe Dashboard → Products → Price IDs |

### Creating Stripe Products

Each credit pack requires a Stripe Product with a Price. The Price ID is what you configure in secrets.

**Required metadata on each Price:**
```json
{
  "credits": "25",
  "bonus_credits": "5"
}
```

**Example credit packs:**

| Pack | Price | Credits | Bonus | Total |
|------|-------|---------|-------|-------|
| 5 Credits | $5.00 | 5 | 0 | 5 |
| 10 Credits | $10.00 | 10 | 0 | 10 |
| 25 Credits | $20.00 | 25 | 5 | 30 |
| 50 Credits | $35.00 | 50 | 15 | 65 |
| 100 Credits | $60.00 | 100 | 40 | 140 |

**Creating via Stripe CLI:**
```bash
# Create a product
stripe products create --name="25 Credit Pack" --description="25 credits + 5 bonus"

# Create a price with metadata
stripe prices create \
  --product="prod_xxx" \
  --unit-amount=2000 \
  --currency=usd \
  --metadata[credits]=25 \
  --metadata[bonus_credits]=5
```

### Production Validation

The backend automatically validates Stripe configuration on startup:

- **Missing `STRIPE_SECRET_KEY`** → App fails to start with clear error
- **Missing `STRIPE_WEBHOOK_SECRET`** → App fails to start with clear error
- **Invalid JWT secret** (starts with "dev-") → App fails to start

This ensures misconfigured deployments fail fast rather than accepting payments without proper security.

### Test vs Production Keys

| Environment | Secret Key | Webhook URL |
|-------------|------------|-------------|
| **Local** | `sk_test_...` | Use Stripe CLI: `stripe listen --forward-to localhost:8000/payments/webhook` |
| **Staging** | `sk_test_...` | `https://barbox-backend-staging.fly.dev/payments/webhook` |
| **Production** | `sk_live_...` | `https://barbox-backend.fly.dev/payments/webhook` |

**Important:** Never mix test and live keys in the same environment.

### Webhook Secret Rotation

When rotating webhook secrets:

1. Generate new webhook in Stripe Dashboard (keep old one active)
2. Update Fly.io secret: `fly secrets set STRIPE_WEBHOOK_SECRET="whsec_new_secret"`
3. Wait for deployment to complete
4. Delete old webhook in Stripe Dashboard

---

## Production Best Practices

### Custom Domain Setup

Map a custom domain to your Fly.io app for a professional production deployment:

```bash
# Add custom domain
fly certs add yourdomain.com --app barbox-backend-prod

# Add www subdomain (optional)
fly certs add www.yourdomain.com --app barbox-backend-prod

# Check certificate status
fly certs show yourdomain.com --app barbox-backend-prod

# List all certificates
fly certs list --app barbox-backend-prod
```

**DNS Configuration:**
Add these DNS records to your domain registrar:

```
Type   Name             Value
A      yourdomain.com   [Fly.io IP from `fly ips list`]
AAAA   yourdomain.com   [Fly.io IPv6 from `fly ips list`]
CNAME  www              yourdomain.com
```

**SSL Certificates:**
- Fly.io automatically provisions Let's Encrypt SSL certificates
- Certificates auto-renew 30 days before expiration
- HTTPS is enforced via `force_https = true` in fly.toml

### Database Backup & Recovery

**Automated Backup Strategy:**

Create a backup script that runs daily via cron or GitHub Actions:

```bash
#!/bin/bash
# backup-flyio-db.sh

APP_NAME="barbox-backend-prod"
VOLUME_NAME="barbox_data_prod"
DATE=$(date +%Y-%m-%d)

# Create snapshot with date tag
fly volumes snapshots create $VOLUME_NAME \
  --app $APP_NAME

echo "Backup created: $DATE"

# Clean up snapshots older than 30 days
fly volumes snapshots list $VOLUME_NAME --app $APP_NAME --json \
  | jq -r ".[] | select(.created_at < (now - 2592000)) | .id" \
  | xargs -I {} fly volumes snapshots delete {} --app $APP_NAME
```

**Recovery Procedure:**

```bash
# List available snapshots
fly volumes snapshots list barbox_data_prod --app barbox-backend-prod

# Create new volume from snapshot
fly volumes create barbox_data_recovered \
  --snapshot-id snap_xxx \
  --region ord \
  --size 1 \
  --app barbox-backend-prod

# Update fly.toml to use recovered volume
# Change mount source from "barbox_data_prod" to "barbox_data_recovered"

# Redeploy
fly deploy --app barbox-backend-prod
```

**Backup Verification:**
Test your backup/recovery process quarterly to ensure data can be restored.

### Application Monitoring

**Health Check Setup:**

The `/alive` endpoint provides basic health monitoring. Enhance it with version information:

```python
# In src/bxctl/web/main.py
@app.get("/alive")
async def alive():
    return {
        "status": "alive",
        "version": os.getenv("APP_VERSION", "unknown"),
        "environment": os.getenv("ENV", "unknown"),
        "timestamp": datetime.utcnow().isoformat()
    }
```

**External Monitoring Services:**

Set up uptime monitoring with a free service:

- **UptimeRobot** (free tier): https://uptimerobot.com/
  - Monitor: `https://barbox-backend-prod.fly.dev/alive`
  - Check interval: 5 minutes
  - Alert methods: Email, Slack, SMS

- **Pingdom** (paid): https://www.pingdom.com/
  - More detailed performance metrics
  - Multi-region checks

**Fly.io Metrics Dashboard:**
```bash
# View app metrics
fly dashboard --app barbox-backend-prod

# Access via web: https://fly.io/apps/barbox-backend-prod
```

### Log Management

**Viewing Logs:**
```bash
# Live tail (all instances)
fly logs --app barbox-backend-prod

# Recent logs only
fly logs --app barbox-backend-prod --recent

# Filter by instance
fly logs --app barbox-backend-prod --instance <instance-id>
```

**Log Retention:**
- Fly.io retains logs for 30 days
- For long-term retention, export to external service

**Log Aggregation (Optional):**

Export logs to external service for analysis:

```bash
# Example: Export to Papertrail
fly secrets set PAPERTRAIL_ENDPOINT="logs.papertrailapp.com:12345" \
  --app barbox-backend-prod

# Example: Export to Datadog
fly secrets set DATADOG_API_KEY="<api-key>" \
  --app barbox-backend-prod
```

Configure log shipping in your application or use Fly.io's log shipping features.

### Scaling & Performance

**Vertical Scaling** (Increase resources):
```bash
# Upgrade to 1GB RAM
fly scale memory 1024 --app barbox-backend-prod

# Upgrade to 2x CPU
fly scale vm shared-cpu-2x --app barbox-backend-prod

# Check current resources
fly status --app barbox-backend-prod
```

**Horizontal Scaling** (Not recommended for SQLite):
```bash
# SQLite doesn't support multi-instance writes
# Keep min_machines_running = 1 in fly.toml

# For future PostgreSQL migration:
# fly scale count 2 --app barbox-backend-prod
```

**Performance Monitoring:**
```bash
# View resource usage
fly metrics --app barbox-backend-prod

# View detailed machine stats
fly machine status <machine-id> --app barbox-backend-prod
```

### Incident Response Procedures

**Backend Service Degradation:**

1. Check health status:
   ```bash
   fly status --app barbox-backend-prod
   curl https://barbox-backend-prod.fly.dev/alive
   ```

2. View recent logs for errors:
   ```bash
   fly logs --app barbox-backend-prod --recent
   ```

3. Restart if needed:
   ```bash
   fly apps restart barbox-backend-prod
   ```

4. Rollback to previous version if restart doesn't help:
   ```bash
   fly releases rollback <version> --app barbox-backend-prod
   ```

**Database Corruption:**

1. Stop the app:
   ```bash
   fly scale count 0 --app barbox-backend-prod
   ```

2. Restore from latest snapshot (see Backup & Recovery section above)

3. Restart the app:
   ```bash
   fly scale count 1 --app barbox-backend-prod
   ```

**High Memory/CPU Usage:**

1. Check metrics:
   ```bash
   fly metrics --app barbox-backend-prod
   ```

2. Scale up if needed:
   ```bash
   fly scale memory 1024 --app barbox-backend-prod
   ```

3. Investigate slow queries in logs

### Maintenance Windows

**Planned Maintenance Best Practices:**

- Schedule during low-traffic hours (typically 2-6 AM local time)
- Notify users in advance (if public-facing)
- Create backup snapshot before maintenance
- Test changes in staging first
- Have rollback plan ready

**Maintenance Checklist:**
- [ ] Create volume snapshot
- [ ] Update staging environment first
- [ ] Run smoke tests on staging
- [ ] Deploy to production
- [ ] Verify health checks pass
- [ ] Monitor logs for 15 minutes
- [ ] Verify arcade machines can connect

---

## Next Steps

After successful deployment:

1. **Configure Godot Client** - See [Configuring Client Machines](#configuring-client-machines) below
2. **Test Full Flow** - Register players, create sessions, submit scores
3. **Monitor Costs** - Check Fly.io dashboard weekly

---

## Configuring Client Machines

This section covers how to configure BarBoxApp (Godot client) to connect to your Fly.io backend.

### Overview

The Godot client uses environment variables to configure backend connectivity. Configuration is loaded from `.env.local` files (machine-specific, gitignored).

| Environment | Backend URL | Use Case |
|-------------|-------------|----------|
| **Local Dev** | `http://localhost:8000` | Development with auto-started backend |
| **Local → Remote** | `https://barbox-backend.fly.dev` | Testing production from editor |
| **Test/Production** | `https://barbox-backend.fly.dev` | Physical arcade machines |

### Step 1: Register Box on Fly.io Backend

Each physical machine (or test environment) needs its own registered box:

```bash
# Generate unique box ID
TEST_BOX_ID=$(uuidgen)
echo "Box ID: $TEST_BOX_ID"

# Register box on production backend
curl -X PUT "https://barbox-backend.fly.dev/box/$TEST_BOX_ID" \
  -H "Content-Type: application/json" \
  -d "{
    \"id\": \"$TEST_BOX_ID\",
    \"name\": \"Test Box 1\",
    \"tag\": \"test_box_1\"
  }"
```

**Save the API key from the response!** You'll need it for client configuration.

Example response:
```json
{
  "id": "12345678-1234-1234-1234-123456789012",
  "name": "Test Box 1",
  "tag": "test_box_1",
  "api_key": "YOUR_API_KEY_HERE"
}
```

### Step 2: Configure Client Environment

Create or update `.env.local` in the `BarBoxApp/` directory:

**For Test/Production Machines:**
```bash
# BarBoxApp/.env.local

# Backend connection
BARBOX_BACKEND_URL=https://barbox-backend.fly.dev

# Box identity (from Step 1)
BARBOX_BOX_ID=12345678-1234-1234-1234-123456789012
BARBOX_API_KEY=YOUR_API_KEY_HERE

# Display names
BARBOX_BOX_NAME=test_box_1
BARBOX_VENUE_NAME=test_venue
```

**For Local Editor Testing Against Remote Backend:**
```bash
# BarBoxApp/.env.local

# Backend connection
BARBOX_BACKEND_URL=https://barbox-backend.fly.dev

# Box identity (register a test box for your dev machine)
BARBOX_BOX_ID=<your-dev-test-box-uuid>
BARBOX_API_KEY=<your-dev-test-box-api-key>

# IMPORTANT: Skip local backend auto-start
BARBOX_TEST_MODE=1

# Display names
BARBOX_BOX_NAME=dev_remote_test
BARBOX_VENUE_NAME=dev_venue
```

### Step 3: Verify Connection

**From command line:**
```bash
# Test backend is reachable
curl https://barbox-backend.fly.dev/alive
# Expected: {"status":"alive"}

# Test box authentication
curl -X GET "https://barbox-backend.fly.dev/box/<your-box-id>" \
  -H "X-Box-API-Key: <your-api-key>"
```

**From Godot:**
1. Launch the app in editor (or run exported build)
2. Check console for "Backend is available and healthy"
3. Test player registration flow

### Configuration Quick Reference

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `BARBOX_BACKEND_URL` | Yes | `http://localhost:8000` | Full backend URL |
| `BARBOX_BOX_ID` | Yes | Dev UUID | Box UUID from registration |
| `BARBOX_API_KEY` | Yes | Dev key | API key from registration |
| `BARBOX_BOX_NAME` | No | `default` | Human-readable box name |
| `BARBOX_VENUE_NAME` | No | `default` | Venue identifier |
| `BARBOX_TEST_MODE` | No | (unset) | Set to `1` to skip local backend auto-start |

### Switching Between Environments

**Tip:** Keep multiple config files and copy as needed:

```bash
# Save current config
cp BarBoxApp/.env.local BarBoxApp/.env.local.remote

# Create local dev config
cat > BarBoxApp/.env.local.dev << 'EOF'
BARBOX_BACKEND_URL=http://localhost:8000
BARBOX_BOX_ID=00000000-0000-0000-0000-000000000001
BARBOX_API_KEY=<dev-api-key>
BARBOX_BOX_NAME=dev_box
BARBOX_VENUE_NAME=dev_local
EOF

# Switch to local dev
cp BarBoxApp/.env.local.dev BarBoxApp/.env.local

# Switch to remote testing
cp BarBoxApp/.env.local.remote BarBoxApp/.env.local
```

### Troubleshooting Client Connection

**Issue: "Backend failed" error in Godot**
```bash
# Check backend is running
curl https://barbox-backend.fly.dev/alive

# Verify .env.local has correct URL
cat BarBoxApp/.env.local | grep BACKEND_URL
```

**Issue: Local backend auto-starts when testing remote**
- Add `BARBOX_TEST_MODE=1` to `.env.local`
- This tells BackendManager to skip auto-start

**Issue: 401 Unauthorized errors**
- Verify `BARBOX_API_KEY` matches the key from box registration
- Check `BARBOX_BOX_ID` is correct
- Re-register the box if keys were lost

**Issue: Box not found (404)**
- The box may not be registered on the backend
- Run the registration curl command from Step 1

---

## Automated Deployment (Future Enhancement)

### Overview

The current manual deployment process using `fly deploy` is production-ready and recommended for most use cases. However, for teams managing multiple arcade machines and frequent releases, automated deployment via CI/CD can provide additional benefits:

**Benefits of Automation:**
- Consistent deployment process across staging and production
- Version tracking and release management via git tags
- Coordinated backend + frontend deployments
- Automated rollback on health check failures
- Multi-machine deployment orchestration
- Audit trail of all deployments via GitHub Actions logs

**Trade-offs:**
- Additional complexity to set up and maintain
- Requires managing GitHub Secrets (Fly.io token, Tailscale OAuth)
- Learning curve for team members unfamiliar with GitHub Actions
- Potential for deployment pipeline issues

### When to Automate

**Stick with manual deployment if:**
- You have 1-5 arcade machines
- Deployments are infrequent (monthly or less)
- Team is comfortable with Fly.io CLI
- You prefer simplicity over automation

**Consider automation when:**
- Managing 10+ arcade machines
- Frequent releases (weekly or more)
- Need coordinated backend/frontend versioning
- Want instant rollback capabilities
- Team familiar with GitHub Actions

### CI/CD Integration Architecture

For complete CI/CD implementation details, see the **[CI/CD Integration Plan](CI_CD_INTEGRATION_PLAN.md)**.

**High-level architecture:**

1. **Version Synchronization:** Git tags (`v1.2.3`) coordinate backend and frontend releases
2. **Sequential Deployment:** Backend deploys first (backward-compatible), then frontend
3. **Health Check Gates:** Frontend deployment waits for backend `/alive` endpoint
4. **Machine Discovery:** JSON file tracks arcade machine IPs for deployment
5. **SSH via Tailscale:** Secure deployment without managing SSH keys

**Proposed workflows:**
- `build-and-test.yml` - Build Docker + Godot export, run tests (every push/PR)
- `deploy-staging.yml` - Auto-deploy to staging on `main` branch
- `deploy-production.yml` - Manual deploy to production on git tags (requires approval)

**Deployment flow:**
```
Git Tag v1.2.3
    ↓
Build Artifacts (Docker image + Godot export)
    ↓
Deploy Backend to Fly.io
    ↓
Wait for /alive Health Check
    ↓
Deploy Frontend to Arcade Machines (via Tailscale SSH)
    ↓
Verify Deployment + Run Smoke Tests
```

### Current Manual Process (Recommended)

**Backend deployment:**
```bash
cd BarBoxServices
fly deploy --app barbox-backend-prod
fly status --app barbox-backend-prod
curl https://barbox-backend-prod.fly.dev/alive
```

**Frontend deployment** (to arcade machines):
```bash
cd BarBoxApp
./scripts/build-export.sh v1.2.3

cd ../BarBoxServices/deployment
./deploy.sh <machine-ip> --build v1.2.3
```

This manual process provides full control and visibility into each deployment step, which is often preferable for small-scale operations.

### Rollback Procedures

**Backend rollback (Fly.io):**
```bash
# List recent releases
fly releases --app barbox-backend-prod

# Rollback to previous version
fly releases rollback v<number> --app barbox-backend-prod

# Verify rollback
fly status --app barbox-backend-prod
curl https://barbox-backend-prod.fly.dev/alive
```

**Frontend rollback (arcade machines):**
```bash
# SSH to machine
ssh barbox@<machine-ip>

# Navigate to install directory
cd ~/Desktop/barbox

# Stop services
./stop_all.sh

# Revert symlink to previous version
ln -sfn releases/v1.2.2 current

# Restart services
./start_all.sh

# Verify version
./get-version.sh
```

**Atomic rollback:** Frontend uses symlink switching (`current -> releases/<version>`) for instant rollback in ~5 seconds.

### Next Steps for Automation

If you decide to implement CI/CD automation:

1. Review the [CI/CD Integration Plan](CI_CD_INTEGRATION_PLAN.md)
2. Set up Tailscale OAuth for GitHub Actions
3. Create GitHub Environments (staging, production)
4. Implement build workflow first (no deployment)
5. Test staging deployment workflow
6. Roll out production deployment with approval gates

For questions or assistance with CI/CD setup, refer to the detailed integration plan document.

---

## File Locations Summary

| File | Path | Purpose |
|------|------|---------|
| Dockerfile | `BarBoxServices/Dockerfile` | Container image definition |
| .dockerignore | `BarBoxServices/.dockerignore` | Exclude files from build |
| fly.toml | `BarBoxServices/fly.toml` | Fly.io app configuration |
| .env.production | `BarBoxServices/.env.production` | Environment template (not committed) |

---

## Resources

- **Fly.io Docs:** https://fly.io/docs/
- **Fly.io Pricing:** https://fly.io/pricing/
- **FastAPI Deployment:** https://fastapi.tiangolo.com/deployment/
- **SQLite on Fly.io:** https://fly.io/docs/volumes/overview/

---

**Ready to deploy!** Follow the steps in Phase 2 to get your backend running on Fly.io.
