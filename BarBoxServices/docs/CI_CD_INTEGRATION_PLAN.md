# BarBox CI/CD Integration Plan

**Status:** Reference Architecture (Not Yet Implemented)
**Purpose:** Complete guide for future automated deployment implementation
**Last Updated:** 2025-11-26

---

## Executive Summary

This document provides a complete reference architecture for implementing automated CI/CD for the BarBox platform using GitHub Actions. The current manual deployment process is production-ready and recommended for most use cases. This plan should be consulted when the team is ready to automate deployments.

**Key Decision:** Automation adds value when managing 10+ arcade machines with weekly releases. For smaller deployments, manual processes are simpler and more maintainable.

---

## Architecture Overview

### Version Synchronization Strategy

**Problem:** Keeping Godot game clients in sync with backend API changes across multiple arcade machines.

**Solution:** Git tags as single source of truth for coordinated releases

```
Developer creates git tag: v1.2.3
    ↓
GitHub Actions triggered
    ↓
Build Phase
    ├─> Backend: Docker image tagged v1.2.3
    └─> Frontend: Godot export tagged v1.2.3
    ↓
Deploy Phase (Sequential)
    ├─> 1. Deploy backend to Fly.io (backward-compatible)
    ├─> 2. Wait for /alive health check
    ├─> 3. Deploy frontend to arcade machines
    └─> 4. Verify all machines updated successfully
    ↓
Success: Version v1.2.3 running everywhere
Failure: Atomic rollback via symlink revert
```

### Core Principles

1. **Backend First:** Always deploy backend before frontend (backward-compatible APIs)
2. **Health Check Gates:** Frontend deployment blocked until backend `/alive` returns 200
3. **Atomic Operations:** Symlink switching enables instant rollback on machines
4. **Audit Trail:** All deployments logged in GitHub Actions with timestamps and deployer
5. **Environment Separation:** Staging (auto-deploy) → Production (manual approval)

---

## Proposed GitHub Actions Workflows

### Workflow 1: Build and Test

**File:** `.github/workflows/build-and-test.yml`

**Triggers:**
- Every push to any branch
- Every pull request

**Jobs:**

#### Job 1: Build Backend
```yaml
build-backend:
  runs-on: ubuntu-latest
  steps:
    - Checkout code
    - Set up Docker Buildx
    - Build Docker image (Python 3.13 + FastAPI)
    - Tag with commit SHA and branch name
    - Push to GitHub Container Registry (ghcr.io)
    - Upload build metadata as artifact
```

#### Job 2: Build Frontend
```yaml
build-frontend:
  runs-on: ubuntu-latest
  steps:
    - Checkout code
    - Set up .NET 9.0 SDK
    - Install Godot 4.7 headless
    - Build C# project (dotnet build BarBox.csproj -c Release)
    - Export Godot project (Linux x86_64)
    - Create VERSION file with git commit hash
    - Generate SHA-256 checksums
    - Upload artifacts (BarBox.x86_64, BarBox.pck, VERSION, checksums.txt)
```

#### Job 3: Integration Tests
```yaml
test:
  runs-on: ubuntu-latest
  needs: [build-backend]
  steps:
    - Checkout code
    - Download backend Docker image
    - Start backend container
    - Run Hurl integration tests (test/*.hurl)
    - Generate test report
    - Comment results on PR (if applicable)
```

**Artifacts:**
- Backend Docker image: `ghcr.io/johngroot/barbox-backend:sha-abc123`
- Frontend export: `BarBox.x86_64`, `BarBox.pck`
- VERSION file: Version, timestamp, commit hash
- Checksums: SHA-256 hashes for integrity verification

---

### Workflow 2: Deploy Staging

**File:** `.github/workflows/deploy-staging.yml`

**Triggers:**
- Push to `main` branch only

**Environment:** `staging` (no approval required)

**Jobs:**

#### Job 1: Deploy Backend to Fly.io Staging
```yaml
deploy-backend-staging:
  runs-on: ubuntu-latest
  environment: staging
  steps:
    - Checkout code
    - Download backend Docker image from ghcr.io
    - Install flyctl CLI
    - Deploy to Fly.io staging app
      fly deploy --config fly.staging.toml --image ghcr.io/...
    - Wait for deployment to complete
    - Health check: curl https://barbox-backend-staging.fly.dev/alive
    - Export backend version for frontend deployment
```

#### Job 2: Deploy Frontend to Staging Machine
```yaml
deploy-frontend-staging:
  runs-on: ubuntu-latest
  needs: deploy-backend-staging
  environment: staging
  steps:
    - Checkout code
    - Download frontend artifacts
    - Connect to Tailscale network (via tailscale/github-action@v4)
    - Read machine config from .github/machines-staging.json
    - For each staging machine:
        - SCP upload artifacts to machine
        - SSH execute deployment script
        - Verify symlink points to new version
    - Disconnect from Tailscale
```

#### Job 3: Smoke Tests
```yaml
smoke-test-staging:
  runs-on: ubuntu-latest
  needs: deploy-frontend-staging
  steps:
    - Test player registration
    - Test session creation
    - Test event submission
    - Test leaderboard query
    - Report results (success/failure)
```

**Machine Config Format** (`.github/machines-staging.json`):
```json
{
  "machines": [
    {
      "name": "staging_box",
      "tailscale_ip": "100.x.x.x",
      "box_id": "staging-uuid",
      "venue": "dev_arcade",
      "enabled": true
    }
  ]
}
```

---

### Workflow 3: Deploy Production

**File:** `.github/workflows/deploy-production.yml`

**Triggers:**
- Git tags matching `v*.*.*` pattern (e.g., `v1.2.3`)

**Environment:** `production` (requires manual approval from designated reviewers)

**Jobs:**

#### Job 1: Deploy Backend to Fly.io Production
```yaml
deploy-backend-production:
  runs-on: ubuntu-latest
  environment: production  # Requires manual approval
  steps:
    - Checkout code at tag
    - Download backend Docker image
    - Install flyctl CLI
    - Deploy to Fly.io production app
      fly deploy --config fly.production.toml --image ghcr.io/...
    - Wait for deployment
    - Health check with version verification
      curl https://barbox-backend-prod.fly.dev/alive
      (verify version matches tag)
    - Export backend version for next job
```

#### Job 2: Deploy Frontend to Production Machines
```yaml
deploy-frontend-production:
  runs-on: ubuntu-latest
  needs: deploy-backend-production
  environment: production
  steps:
    - Checkout code at tag
    - Download frontend artifacts
    - Connect to Tailscale network
    - Read machine config from .github/machines-prod.json
    - For each production machine (where enabled=true):
        - SCP upload artifacts
        - SSH execute deployment script
        - Verify symlink updated
        - Verify services restarted successfully
        - Log deployment success/failure per machine
    - Generate deployment report (all machines status)
    - Disconnect from Tailscale
```

#### Job 3: Create GitHub Release
```yaml
create-release:
  runs-on: ubuntu-latest
  needs: deploy-frontend-production
  steps:
    - Checkout code at tag
    - Generate changelog from commits since last tag
    - Attach build artifacts (Docker image, Godot export, VERSION)
    - Create GitHub Release with changelog
    - Tag Docker image as "latest" and version tag
```

**Production Machine Registry** (`.github/machines-prod.json`):
```json
{
  "machines": [
    {
      "name": "besties_box_1",
      "tailscale_ip": "100.93.137.42",
      "box_id": "12345678-1234-1234-1234-123456789012",
      "venue": "best_intentions",
      "enabled": true
    },
    {
      "name": "besties_box_2",
      "tailscale_ip": "100.93.137.43",
      "box_id": "22345678-1234-1234-1234-123456789012",
      "venue": "best_intentions",
      "enabled": true
    }
  ]
}
```

---

## Machine Management Strategy

### Discovery Mechanism: JSON File vs Tailscale API

**Phase 1: JSON File (Recommended Start)**

**Pros:**
- Simple to implement and understand
- Version-controlled (git tracks changes)
- Human-readable and easily auditable
- No API integration complexity
- Works for fleets < 20 machines

**Cons:**
- Manual maintenance when adding/removing machines
- No real-time status information
- Requires commit for every machine change

**File structure:**
```json
{
  "machines": [
    {
      "name": "box_identifier",
      "tailscale_ip": "100.x.x.x",
      "box_id": "uuid",
      "venue": "venue_name",
      "enabled": true
    }
  ]
}
```

**Phase 2: Tailscale API (Future Upgrade)**

Upgrade when fleet grows beyond 20 machines or manual maintenance becomes burdensome.

**Pros:**
- Automatic machine discovery via Tailscale tags
- Real-time status (online/offline)
- No manual registry maintenance
- Scale to hundreds of machines

**Cons:**
- More complex integration
- API rate limits
- Requires managing Tailscale API key
- Less visible audit trail

**Implementation:**
```yaml
- name: Discover machines via Tailscale API
  run: |
    curl -H "Authorization: Bearer ${{ secrets.TAILSCALE_API_KEY }}" \
      https://api.tailscale.com/api/v2/tailnet/<tailnet>/devices \
      | jq '.devices[] | select(.tags[] | contains("tag:arcade-machine"))'
```

### Deployment Coordination

**Sequential Deployment (Recommended):**
```yaml
deploy-all:
  steps:
    - name: Deploy to machines sequentially
      run: |
        for machine in $(jq -r '.machines[] | select(.enabled) | .tailscale_ip' machines-prod.json); do
          echo "Deploying to $machine..."
          ssh barbox@$machine "cd ~/Desktop/barbox && ./deploy-frontend.sh $VERSION"
          if [ $? -ne 0 ]; then
            echo "Deployment failed for $machine"
            exit 1
          fi
        done
```

**Parallel Deployment (Advanced):**
```yaml
deploy-all:
  strategy:
    matrix:
      machine: ${{ fromJson(readFile('.github/machines-prod.json')).machines }}
    fail-fast: false  # Continue deploying to other machines on failure
  steps:
    - name: Deploy to ${{ matrix.machine.name }}
      run: |
        ssh barbox@${{ matrix.machine.tailscale_ip }} \
          "cd ~/Desktop/barbox && ./deploy-frontend.sh $VERSION"
```

**Trade-offs:**
- **Sequential:** Easier to debug, clear ordering, slower for large fleets
- **Parallel:** Faster, scales well, harder to debug partial failures

---

## Security Implementation

### GitHub Secrets Required

**Repository Secrets:**
- `TAILSCALE_OAUTH_CLIENT_ID` - Tailscale OAuth client ID for GitHub Actions
- `TAILSCALE_OAUTH_SECRET` - Tailscale OAuth secret
- `FLY_API_TOKEN` - Fly.io API token (from `fly auth token`)

**Environment Secrets (Staging):**
- None (uses repository secrets)

**Environment Secrets (Production):**
- None (uses repository secrets, but environment has approval gates)

### SSH Security via Tailscale

**Why Tailscale over SSH keys:**
- No SSH keys to rotate or secure in GitHub Secrets
- Ephemeral GitHub Actions runners auto-cleanup
- Tag-based access control (`tag:github-actions`)
- Works across NAT/firewalls without port forwarding
- Full audit trail in Tailscale admin console

**Setup:**
1. Create Tailscale OAuth client in admin console
2. Tag arcade machines: `tag:arcade-machine`
3. Tag GitHub Actions runner: `tag:github-actions`
4. Configure ACL to allow `github-actions` → `arcade-machine` SSH

**Example ACL:**
```json
{
  "acls": [
    {
      "action": "accept",
      "src": ["tag:github-actions"],
      "dst": ["tag:arcade-machine:22"]
    }
  ],
  "tagOwners": {
    "tag:github-actions": ["autogroup:admin"],
    "tag:arcade-machine": ["autogroup:admin"]
  }
}
```

**GitHub Actions Integration:**
```yaml
- name: Connect to Tailscale
  uses: tailscale/github-action@v4
  with:
    oauth-client-id: ${{ secrets.TAILSCALE_OAUTH_CLIENT_ID }}
    oauth-secret: ${{ secrets.TAILSCALE_OAUTH_SECRET }}
    tags: tag:github-actions

- name: Deploy via SSH
  run: |
    ssh barbox@100.x.x.x "cd ~/Desktop/barbox && ./deploy.sh"
```

### Environment-Based Approvals

**GitHub Environments:**

**Staging Environment:**
- No approval required
- Automatic deployment on `main` branch push
- Deployment branches: `main` only

**Production Environment:**
- **Requires approval** from designated reviewers
- Manual deployment trigger via git tags
- Deployment branches: Tags only
- Reviewers: 1+ team members must approve

**Setting up environments:**
1. GitHub repo → Settings → Environments → New environment
2. Add environment protection rules
3. Add required reviewers
4. Restrict deployment branches

---

## Cost Analysis

### Monthly Costs (Assuming Chicago `ord` Region)

**Fly.io:**
- Staging app: $3.15/month (512MB RAM, 1GB volume)
- Production app: $5.30/month (512MB RAM, 1GB volume)
- **Subtotal:** $8.45/month

**GitHub Actions:**
- 2,000 minutes/month free tier
- Estimated usage: ~200 minutes/month (10 deployments × 20 min each)
- Additional minutes: $0.008/minute (if over free tier)
- **Subtotal:** $0 (within free tier)

**Tailscale:**
- Personal use: Free (up to 100 devices, 3 users)
- Team plan: $6/user/month (if needed for team collaboration)
- **Subtotal:** $0 (assuming personal use)

**Total Estimated Cost:** ~$8.45/month

**Cost Comparison:**
- Manual deployment: $0/month (local machines only)
- Automated deployment: $8.45/month (staging + production cloud infrastructure)
- **Premium:** $8.45/month for automation benefits

**Free Tier Coverage:**
- Fly.io offers $5/month free credit
- **Actual cost after free tier:** ~$3.45/month

### Cost Optimization

**Keep costs low:**
- Use shared-cpu-1x instances (smallest Fly.io machine)
- Keep 512MB RAM unless performance requires upgrade
- Use 1GB volumes (sufficient for SQLite)
- Stay within GitHub Actions free tier (2,000 min/month)
- Use Tailscale free tier (personal use)

**Scale-up cost projection:**
- 1GB RAM: +$2/month per app
- 2x CPU: +$5/month per app
- 5GB volume: +$0.75/month per app
- GitHub Actions overages: $0.008/minute

---

## Implementation Roadmap

### Week 1: Foundation (Staging Only)

**Goals:**
- Automated staging deployment working end-to-end
- Build artifacts created and tested
- Team familiar with workflow

**Tasks:**

**Day 1: Setup Infrastructure**
- [ ] Create Tailscale OAuth client
- [ ] Add GitHub Secrets (Tailscale OAuth, Fly.io token)
- [ ] Create Fly.io staging app (`barbox-backend-staging`)
- [ ] Create staging volume (`fly volumes create barbox_data_staging --region ord`)
- [ ] Set staging secrets (`fly secrets set JWT_SECRET_KEY=...`)

**Day 2: Build Workflow**
- [ ] Create `.github/workflows/build-and-test.yml`
- [ ] Test backend Docker build
- [ ] Test frontend Godot export
- [ ] Verify artifacts uploaded to GitHub
- [ ] Run Hurl integration tests

**Day 3: Staging Deployment Workflow**
- [ ] Create `.github/workflows/deploy-staging.yml`
- [ ] Create `.github/machines-staging.json` with one machine
- [ ] Test backend deployment to Fly.io staging
- [ ] Test frontend deployment to staging machine
- [ ] Verify health checks pass

**Day 4: Testing & Refinement**
- [ ] Run full staging deployment end-to-end
- [ ] Test rollback procedures
- [ ] Add smoke tests
- [ ] Document any issues encountered

**Day 5: Team Training**
- [ ] Demo staging deployment to team
- [ ] Document workflow usage
- [ ] Create troubleshooting guide

---

### Week 2: Production Deployment

**Goals:**
- Production deployment with approval gates
- All production machines deploy successfully
- Rollback procedures tested

**Tasks:**

**Day 1-2: Production Infrastructure**
- [ ] Create Fly.io production app (`barbox-backend-prod`)
- [ ] Create production volume (`fly volumes create barbox_data_prod --region ord`)
- [ ] Set production secrets (JWT, CORS, etc.)
- [ ] Create `.github/machines-prod.json` with all machines
- [ ] Set up GitHub Environment `production` with approval gates

**Day 3: Production Workflow**
- [ ] Create `.github/workflows/deploy-production.yml`
- [ ] Test git tag trigger (on non-production tag)
- [ ] Verify manual approval required
- [ ] Test backend deployment to Fly.io production

**Day 4: Multi-Machine Deployment**
- [ ] Test frontend deployment to all production machines
- [ ] Verify version consistency across machines
- [ ] Test partial failure handling (one machine fails)
- [ ] Document machine-specific issues

**Day 5: Release Process**
- [ ] Create first production release via git tag
- [ ] Test GitHub Release creation
- [ ] Verify changelog generation
- [ ] Document release process for team

---

### Week 3: Monitoring & Documentation

**Goals:**
- Enhanced monitoring and observability
- Complete documentation
- Team trained on operations

**Tasks:**

**Day 1: Health Check Enhancement**
- [ ] Update `/alive` endpoint with version metadata
- [ ] Add environment information
- [ ] Add deployment timestamp
- [ ] Test version verification in workflows

**Day 2: Smoke Test Suite**
- [ ] Create `BarBoxServices/scripts/smoke-test.sh`
- [ ] Test player registration
- [ ] Test session creation and events
- [ ] Test leaderboard queries
- [ ] Integrate into deploy workflows

**Day 3: Documentation**
- [ ] Update CI_CD_INTEGRATION_PLAN.md with learnings
- [ ] Create operational runbook
- [ ] Document common failure scenarios
- [ ] Create troubleshooting guide

**Day 4: Monitoring Setup**
- [ ] Set up Sentry for error tracking
- [ ] Configure external uptime monitoring
- [ ] Set up deployment notifications (Slack/Discord)
- [ ] Test alert workflows

**Day 5: Team Training**
- [ ] Demo full deployment workflow
- [ ] Train team on GitHub Actions UI
- [ ] Practice rollback procedures
- [ ] Q&A session

---

### Week 4+: Optimization & Maintenance

**Optional Enhancements:**

- [ ] Docker layer caching for faster builds
- [ ] Security scanning (Trivy for containers)
- [ ] Automated database backups via GitHub Actions
- [ ] Deployment metrics and analytics
- [ ] Advanced monitoring dashboards
- [ ] Multi-region deployment (if needed)

---

## Trade-offs and Alternatives Considered

### Machine Discovery: JSON File vs Tailscale API

**Decision:** Start with JSON file, upgrade to Tailscale API when fleet > 20 machines

**Rationale:**
- JSON file is simpler to implement and understand
- Version control provides audit trail
- Tailscale API adds complexity for marginal benefit at small scale
- Easy migration path when needed

### Deployment Strategy: In-Place vs Blue-Green

**Decision:** In-place deployment with symlink switching

**Rationale:**
- Blue-green requires 2x disk space per machine
- Symlink switching provides fast rollback (~5 seconds)
- Arcade machines have limited disk space
- Brief service restart (< 10 seconds) is acceptable downtime

**Trade-off:** No zero-downtime deployment, but arcade usage patterns make this acceptable

### CI/CD Platform: GitHub Actions vs Alternatives

**Alternatives Considered:**
- GitLab CI: More powerful, requires migrating from GitHub
- Jenkins: Self-hosted complexity, overkill for scale
- CircleCI: Additional service to manage, cost
- Travis CI: Similar to GitHub Actions, no advantage

**Decision:** GitHub Actions

**Rationale:**
- Native GitHub integration (already using GitHub)
- Free tier sufficient for expected usage
- Simple YAML syntax
- Rich ecosystem of community actions (Tailscale, Fly.io)
- No additional service to manage

### Secrets Management: GitHub Secrets vs External Vault

**Decision:** GitHub Secrets for initial implementation

**Rationale:**
- Sufficient security for current use case
- No additional infrastructure
- Simple workflow integration
- Can migrate to HashiCorp Vault later if needed

**Future consideration:** External vault (HashiCorp Vault, AWS Secrets Manager) when:
- Team grows > 10 people
- Multiple repos need shared secrets
- Compliance requirements mandate it

---

## Success Metrics

### Deployment Metrics

**Target KPIs:**
- Deployment success rate: > 95%
- Deployment time (backend): < 3 minutes
- Deployment time (frontend, 10 machines): < 10 minutes
- Rollback time: < 30 seconds
- Failed deployment detection: < 2 minutes

**Monitoring:**
- GitHub Actions workflow success/failure rates
- Average deployment duration
- Number of rollbacks per month
- Time to detect and resolve deployment failures

### Operational Metrics

**Target KPIs:**
- Zero manual SSH deployments
- Zero secrets committed to git
- 100% deployment audit trail
- < 5 minute incident response time

**Monitoring:**
- GitHub Actions logs
- Tailscale audit logs
- Fly.io deployment history
- Sentry error rates post-deployment

---

## Rollback Procedures

### Automated Rollback (Future Enhancement)

Automated rollback on health check failure:

```yaml
- name: Deploy and verify
  run: |
    ./deploy.sh $VERSION
    sleep 10
    if ! curl -f https://backend/alive; then
      echo "Health check failed, rolling back"
      ./rollback.sh $(cat previous-version.txt)
      exit 1
    fi
```

### Manual Rollback (Current Approach)

**Backend rollback (Fly.io):**
```bash
# List releases
fly releases --app barbox-backend-prod

# Rollback to previous version
fly releases rollback v<number> --app barbox-backend-prod

# Verify
fly status --app barbox-backend-prod
curl https://barbox-backend-prod.fly.dev/alive
```

**Frontend rollback (all machines):**

Create `.github/workflows/rollback-machines.yml`:
```yaml
name: Rollback Machines
on:
  workflow_dispatch:
    inputs:
      version:
        description: 'Version to rollback to (e.g., v1.2.2)'
        required: true

jobs:
  rollback:
    runs-on: ubuntu-latest
    steps:
      - name: Connect to Tailscale
        uses: tailscale/github-action@v4
        with:
          oauth-client-id: ${{ secrets.TAILSCALE_OAUTH_CLIENT_ID }}
          oauth-secret: ${{ secrets.TAILSCALE_OAUTH_SECRET }}
          tags: tag:github-actions

      - name: Rollback all machines
        run: |
          for machine in $(jq -r '.machines[].tailscale_ip' machines-prod.json); do
            ssh barbox@$machine << EOF
              cd ~/Desktop/barbox
              ./stop_all.sh
              ln -sfn releases/${{ github.event.inputs.version }} current
              ./start_all.sh
              ./get-version.sh
          EOF
          done
```

**Trigger rollback:**
1. Go to GitHub Actions → Rollback Machines workflow
2. Click "Run workflow"
3. Enter version to rollback to (e.g., `v1.2.2`)
4. Click "Run workflow"
5. Monitor logs for success/failure

---

## Migration from Manual to Automated

### Phase 1: Parallel Run (2 weeks)

- Keep manual deployments as primary method
- Run automated deployments to staging only
- Compare outcomes (deployment time, reliability)
- Document any discrepancies
- Build team confidence in automation

### Phase 2: Staging Cutover (1 week)

- Switch to automated staging deployments
- Manual deployments only for emergencies
- Monitor for issues
- Refine workflows based on feedback

### Phase 3: Production Pilot (1 week)

- First production deployment with team member observing
- Verify rollback procedures work as expected
- Document any issues
- Refine approval process

### Phase 4: Full Cutover (Ongoing)

- All deployments via GitHub Actions
- Archive manual deployment scripts (keep for emergencies)
- Train all team members on new process
- Establish on-call rotation for deployment approvals

---

## Appendix: Example Workflow Files

### Example: build-and-test.yml

```yaml
name: Build and Test

on:
  push:
    branches: ['**']
  pull_request:
    branches: ['**']

jobs:
  build-backend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Login to GitHub Container Registry
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Build and push Docker image
        uses: docker/build-push-action@v5
        with:
          context: ./BarBoxServices
          push: true
          tags: |
            ghcr.io/${{ github.repository }}/backend:sha-${{ github.sha }}
            ghcr.io/${{ github.repository }}/backend:${{ github.ref_name }}
          cache-from: type=gha
          cache-to: type=gha,mode=max

  build-frontend:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0'

      - name: Install Godot
        run: |
          wget https://github.com/godotengine/godot/releases/download/4.7-stable/Godot_v4.7-stable_linux.x86_64.zip
          unzip Godot_v4.7-stable_linux.x86_64.zip
          chmod +x Godot_v4.7-stable_linux.x86_64

      - name: Build Godot export
        run: |
          cd BarBoxApp
          dotnet build BarBox.csproj -c Release
          ../Godot_v4.7-stable_linux.x86_64 --headless --export-release "Linux/X11" builds/BarBox.x86_64

      - name: Upload artifacts
        uses: actions/upload-artifact@v4
        with:
          name: frontend-build
          path: |
            builds/BarBox.x86_64
            builds/BarBox.pck
            builds/VERSION
            builds/checksums.txt

  test:
    runs-on: ubuntu-latest
    needs: build-backend
    steps:
      - uses: actions/checkout@v4

      - name: Login to GitHub Container Registry
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Start backend container
        run: |
          docker run -d -p 8000:8000 \
            -e ENV=test \
            ghcr.io/${{ github.repository }}/backend:sha-${{ github.sha }}

      - name: Install Hurl
        run: |
          curl -LO https://github.com/Orange-OpenSource/hurl/releases/download/4.1.0/hurl_4.1.0_amd64.deb
          sudo dpkg -i hurl_4.1.0_amd64.deb

      - name: Run integration tests
        run: |
          cd BarBoxServices
          hurl --test test/*.hurl
```

### Example: deploy-staging.yml

```yaml
name: Deploy Staging

on:
  push:
    branches: [main]

jobs:
  deploy-backend:
    runs-on: ubuntu-latest
    environment: staging
    steps:
      - uses: actions/checkout@v4

      - name: Install flyctl
        uses: superfly/flyctl-actions/setup-flyctl@master

      - name: Deploy to Fly.io staging
        run: |
          cd BarBoxServices
          flyctl deploy --config fly.staging.toml --image ghcr.io/${{ github.repository }}/backend:sha-${{ github.sha }}
        env:
          FLY_API_TOKEN: ${{ secrets.FLY_API_TOKEN }}

      - name: Health check
        run: |
          sleep 10
          curl -f https://barbox-backend-staging.fly.dev/alive

  deploy-frontend:
    runs-on: ubuntu-latest
    needs: deploy-backend
    environment: staging
    steps:
      - uses: actions/checkout@v4

      - uses: actions/download-artifact@v4
        with:
          name: frontend-build
          path: builds/

      - name: Connect to Tailscale
        uses: tailscale/github-action@v4
        with:
          oauth-client-id: ${{ secrets.TAILSCALE_OAUTH_CLIENT_ID }}
          oauth-secret: ${{ secrets.TAILSCALE_OAUTH_SECRET }}
          tags: tag:github-actions

      - name: Deploy to staging machines
        run: |
          VERSION=$(date +%Y.%m.%d-%H%M)
          for machine in $(jq -r '.machines[].tailscale_ip' .github/machines-staging.json); do
            scp -r builds/* barbox@$machine:~/Desktop/barbox/builds/$VERSION/
            ssh barbox@$machine "cd ~/Desktop/barbox && ./deploy.sh --build $VERSION"
          done
```

---

## Conclusion

This CI/CD integration plan provides a complete roadmap for automating BarBox deployments. The architecture is designed for simplicity and reliability while providing the automation benefits needed for managing multiple arcade machines.

**Key takeaway:** Automation is valuable at scale (10+ machines, weekly releases) but adds complexity. The current manual deployment process is production-ready and recommended until automation benefits outweigh the maintenance overhead.

For questions or implementation assistance, consult this document and the [Fly.io Deployment Guide](FLY_IO_DEPLOYMENT_GUIDE.md).
