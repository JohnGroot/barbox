#!/bin/bash
set -e

# BarBox Unified Deployment Script
# Deploys BarBoxApp and BarBoxServices to a remote Linux machine and handles setup

# Configuration
TARGET_IP=""
TARGET_USER="barbox"
TARGET_PATH="\$HOME/Desktop/barbox"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$(dirname "$SCRIPT_DIR")")"  # Go up two levels from BarBoxServices/deployment/

# Flags
SKIP_DEPS=false
SKIP_REGISTER=false
BUILD_VERSION=""
PCK_VERSION=""
DEPLOY_MODE="export"  # export or pck_update (source mode removed)

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
BLUE='\033[0;34m'
NC='\033[0m'

log_info() {
	echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
	echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
	echo -e "${RED}[ERROR]${NC} $1"
}

log_step() {
	echo ""
	echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
	echo -e "${BLUE}  $1${NC}"
	echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
	echo ""
}

# Parse arguments
while [[ $# -gt 0 ]]; do
	case $1 in
		--build)
			if [[ -z "$2" || "$2" =~ ^- ]]; then
				log_error "--build requires a version argument"
				exit 1
			fi
			BUILD_VERSION="$2"
			DEPLOY_MODE="export"
			shift 2
			;;
		--pck-update)
			if [[ -z "$2" || "$2" =~ ^- ]]; then
				log_error "--pck-update requires a version argument"
				exit 1
			fi
			PCK_VERSION="$2"
			DEPLOY_MODE="pck_update"
			SKIP_DEPS=true
			shift 2
			;;
		--skip-deps)
			SKIP_DEPS=true
			shift
			;;
		--skip-register)
			SKIP_REGISTER=true
			shift
			;;
		--help|-h)
			echo "Usage: $0 <target-ip> [options]"
			echo ""
			echo "Deployment Modes:"
			echo "  (no flags)              Auto-deploy latest build from builds/releases/"
			echo "  --build <version>       Deploy specific export build"
			echo "  --pck-update <version>  Deploy PCK-only update from builds/updates/<version>"
			echo ""
			echo "Options:"
			echo "  --skip-deps     Skip dependency installation"
			echo "  --skip-register Skip box registration"
			echo "  --help, -h      Show this help message"
			echo ""
			echo "Examples:"
			echo "  $0 100.93.137.42                          # Auto-deploy latest"
			echo "  $0 100.93.137.42 --build 2025.11.25-1431  # Deploy specific version"
			echo "  $0 100.93.137.42 --pck-update 2025.11.25-1445"
			echo "  $0 100.93.137.42 --skip-deps              # Skip dependency install"
			echo ""
			echo "Note: Source mode deployment has been removed. Export builds only."
			exit 0
			;;
		-*)
			log_error "Unknown option: $1"
			exit 1
			;;
		*)
			if [ -z "$TARGET_IP" ]; then
				TARGET_IP="$1"
			else
				log_error "Unexpected argument: $1"
				exit 1
			fi
			shift
			;;
	esac
done

# Validate arguments
if [ -z "$TARGET_IP" ]; then
	log_error "Usage: $0 <target-ip> [options]"
	echo ""
	echo "Examples:"
	echo "  $0 100.93.137.42                          # Auto-deploy latest build"
	echo "  $0 100.93.137.42 --build 2025.11.25-1431  # Deploy specific version"
	echo "  $0 100.93.137.42 --pck-update 2025.11.25-1445"
	exit 1
fi

# Auto-detect latest build if not specified
if [ -z "$BUILD_VERSION" ] && [ -z "$PCK_VERSION" ]; then
	log_info "No version specified, finding latest build..."
	LATEST_BUILD=$(ls -1 "$PROJECT_ROOT/builds/releases/" 2>/dev/null | sort -r | head -n1)

	if [ -z "$LATEST_BUILD" ]; then
		log_error "No builds found in builds/releases/"
		log_warn "Build an export first: cd BarBoxApp && ./scripts/build-export.sh"
		exit 1
	fi

	BUILD_VERSION="$LATEST_BUILD"
	DEPLOY_MODE="export"
	log_info "Auto-detected latest build: $BUILD_VERSION"
fi

TARGET="$TARGET_USER@$TARGET_IP"

# Validate export builds exist if using export deployment
if [[ "$DEPLOY_MODE" == "export" ]]; then
	BUILD_PATH="$PROJECT_ROOT/builds/releases/$BUILD_VERSION"
	if [[ ! -d "$BUILD_PATH" ]]; then
		log_error "Export build not found: $BUILD_PATH"
		log_warn "Build the export first: cd BarBoxApp && sh scripts/build-export.sh $BUILD_VERSION"
		exit 1
	fi
	if [[ ! -f "$BUILD_PATH/BarBox.x86_64" ]]; then
		log_error "Binary not found in build: $BUILD_PATH/BarBox.x86_64"
		exit 1
	fi
	if [[ ! -f "$BUILD_PATH/BarBox.pck" ]]; then
		log_error "PCK not found in build: $BUILD_PATH/BarBox.pck"
		exit 1
	fi

	# Validate .NET assemblies directory
	if [[ ! -d "$BUILD_PATH/data_BarBox_linuxbsd_x86_64" ]]; then
		log_error ".NET assemblies directory not found: $BUILD_PATH/data_BarBox_linuxbsd_x86_64"
		log_warn "Rebuild with: cd BarBoxApp && sh scripts/build-export.sh"
		exit 1
	fi

	ASSEMBLY_COUNT=$(ls "$BUILD_PATH/data_BarBox_linuxbsd_x86_64" 2>/dev/null | wc -l)
	if [[ $ASSEMBLY_COUNT -lt 50 ]]; then
		log_error ".NET assemblies incomplete: $ASSEMBLY_COUNT files (expected ~200)"
		exit 1
	fi

	log_info "Validated export build: $BUILD_VERSION (assemblies: $ASSEMBLY_COUNT files)"
elif [[ "$DEPLOY_MODE" == "pck_update" ]]; then
	PCK_PATH="$PROJECT_ROOT/builds/updates/$PCK_VERSION"
	if [[ ! -f "$PCK_PATH/BarBox.pck" ]]; then
		log_error "PCK update not found: $PCK_PATH/BarBox.pck"
		log_warn "Build the PCK first: cd BarBoxApp && sh scripts/build-pck.sh $PCK_VERSION"
		exit 1
	fi
	log_info "Validated PCK update: $PCK_VERSION"
fi

log_step "BarBox Deployment"

echo "Configuration:"
echo "  Target:        $TARGET"
echo "  Remote Path:   $TARGET_PATH"
echo "  Deploy Mode:   $DEPLOY_MODE"
if [[ "$DEPLOY_MODE" == "export" ]]; then
	echo "  Build Version: $BUILD_VERSION"
elif [[ "$DEPLOY_MODE" == "pck_update" ]]; then
	echo "  PCK Version:   $PCK_VERSION"
fi
echo "  Skip Deps:     $SKIP_DEPS"
echo "  Skip Register: $SKIP_REGISTER"
echo ""

# Test SSH connection
log_info "Testing SSH connection to $TARGET..."
if ! ssh -o ConnectTimeout=5 -o BatchMode=yes -o PreferredAuthentications=publickey "$TARGET" "echo SSH connection successful" 2>/dev/null; then
	log_error "Cannot connect to $TARGET using SSH key authentication"
	log_warn "Make sure:"
	echo "  1. Tailscale is running and target is reachable"
	echo "  2. SSH server is running on target"
	echo "  3. SSH key is authorized - run: ssh-copy-id $TARGET"
	exit 1
fi

log_info "SSH connection successful"

# ========================
# Phase 1: Transfer Code
# ========================
log_step "Phase 1: Transferring Code"

# Create target directory
ssh "$TARGET" "mkdir -p $TARGET_PATH"

# Deploy BarBoxServices
log_info "Syncing BarBoxServices..."
rsync -avz --progress \
	--exclude='.venv' \
	--exclude='__pycache__' \
	--exclude='*.pyc' \
	--exclude='*.pyo' \
	--exclude='*.pyd' \
	--exclude='.pytest_cache' \
	--exclude='app.db' \
	--exclude='.DS_Store' \
	--exclude='.idea' \
	--exclude='.claude' \
	--exclude='.vscode' \
	--exclude='.env' \
	--exclude='.env.local' \
	--exclude='CLAUDE.md' \
	--exclude='claude.md' \
	--exclude='*.log' \
	--exclude='*.dmp' \
	--exclude='claude_code_temp' \
	--exclude='claude_code_cache' \
	--exclude='GodotDocs_4_6' \
	"$PROJECT_ROOT/BarBoxServices/" \
	"$TARGET:$TARGET_PATH/BarBoxServices/"

# Deploy Frontend (mode-specific)
if [[ "$DEPLOY_MODE" == "export" ]]; then
	log_info "Deploying export build v$BUILD_VERSION..."

	# Create release directory structure
	ssh "$TARGET" "mkdir -p $TARGET_PATH/releases/$BUILD_VERSION"

	# Transfer build artifacts
	log_info "Transferring binary, PCK, and .NET assemblies..."
	rsync -avz --progress \
		"$BUILD_PATH/BarBox.x86_64" \
		"$BUILD_PATH/BarBox.pck" \
		"$BUILD_PATH/VERSION" \
		"$BUILD_PATH/checksums.txt" \
		"$BUILD_PATH/data_BarBox_linuxbsd_x86_64" \
		"$TARGET:$TARGET_PATH/releases/$BUILD_VERSION/"

	# Make binary executable
	ssh "$TARGET" "chmod +x $TARGET_PATH/releases/$BUILD_VERSION/BarBox.x86_64"

	# Update current symlink
	ssh "$TARGET" "cd $TARGET_PATH && ln -sfn releases/$BUILD_VERSION current"

	# Verify checksums
	log_info "Verifying checksums..."
	ssh "$TARGET" "cd $TARGET_PATH/current && shasum -c checksums.txt" || {
		log_error "Checksum verification failed!"
		exit 1
	}

	# Verify .NET assemblies transferred successfully
	log_info "Verifying .NET assemblies transfer..."
	REMOTE_ASSEMBLY_COUNT=$(ssh "$TARGET" "ls $TARGET_PATH/releases/$BUILD_VERSION/data_BarBox_linuxbsd_x86_64 2>/dev/null | wc -l" || echo "0")

	if [[ "$REMOTE_ASSEMBLY_COUNT" -lt 50 ]]; then
		log_error ".NET assemblies transfer incomplete!"
		log_error "Found $REMOTE_ASSEMBLY_COUNT files on remote (expected ~200)"
		exit 1
	fi

	log_info "✓ .NET assemblies verified on remote: $REMOTE_ASSEMBLY_COUNT files"
	log_info "Export build deployed: $BUILD_VERSION"

elif [[ "$DEPLOY_MODE" == "pck_update" ]]; then
	log_info "Deploying PCK update v$PCK_VERSION..."

	# Verify current symlink exists
	if ! ssh "$TARGET" "test -L $TARGET_PATH/current"; then
		log_error "No current release found. Cannot update PCK."
		log_warn "Deploy a full export build first: $0 $TARGET_IP --build <version>"
		exit 1
	fi

	# Verify frontend config exists
	if ! ssh "$TARGET" "test -f $TARGET_PATH/.env.local"; then
		log_error "Frontend configuration not found: $TARGET_PATH/.env.local"
		log_warn "Run full deployment to create configuration first."
		exit 1
	fi

	# Transfer PCK to current release
	log_info "Transferring PCK..."
	scp "$PCK_PATH/BarBox.pck" "$TARGET:$TARGET_PATH/current/"

	# Update checksums for new PCK
	log_info "Updating checksums..."
	ssh "$TARGET" "cd $TARGET_PATH/current && shasum -a 256 BarBox.pck > checksums-pck.txt"

	log_info "PCK update deployed: $PCK_VERSION"
fi

# Deploy scripts
log_info "Syncing deployment scripts..."
rsync -avz --progress \
	"$SCRIPT_DIR/start_backend.sh" \
	"$SCRIPT_DIR/start_frontend.sh" \
	"$SCRIPT_DIR/start_all.sh" \
	"$SCRIPT_DIR/stop_all.sh" \
	"$SCRIPT_DIR/barbox-backend.service" \
	"$SCRIPT_DIR/barbox-frontend.service" \
	"$SCRIPT_DIR/set_gpu_performance.sh" \
	"$SCRIPT_DIR/barbox-gpu-performance.service" \
	"$TARGET:$TARGET_PATH/"

# Make scripts executable
ssh "$TARGET" "chmod +x $TARGET_PATH/*.sh"

log_info "Code transfer complete"

# ========================
# Phase 2: Install Dependencies (if not skipped)
# ========================
if [ "$SKIP_DEPS" = false ]; then
	log_step "Phase 2: Installing Dependencies"

	ssh "$TARGET" bash << 'DEPS_EOF'
		set -e

		# Ensure PATH includes all tool directories upfront
		export PATH="$HOME/.config/godotenv/godot/bin:$HOME/.dotnet/tools:$HOME/.local/bin:$PATH"

		# Check for .NET
		if ! command -v dotnet &> /dev/null; then
			echo "[INFO] Installing .NET SDK 9.0..."
			wget -q https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
			sudo dpkg -i /tmp/packages-microsoft-prod.deb
			rm /tmp/packages-microsoft-prod.deb
			sudo apt-get update
			sudo apt-get install -y dotnet-sdk-9.0
		else
			echo "[INFO] .NET SDK already installed: $(dotnet --version)"
		fi

		# Check for GodotEnv
		if ! command -v godotenv &> /dev/null; then
			echo "[INFO] Installing GodotEnv..."
			dotnet tool install --global Chickensoft.GodotEnv
			if [[ ":$PATH:" != *":$HOME/.dotnet/tools:"* ]]; then
				echo 'export PATH="$HOME/.dotnet/tools:$PATH"' >> ~/.bashrc
			fi
		else
			echo "[INFO] GodotEnv already installed"
		fi

		# Check for Godot 4.7.0
		CURRENT_VERSION=$(godot --version 2>&1 | head -n1 | grep -o '[0-9]\+\.[0-9]\+\.[0-9]\+' | head -n1 || echo "")
		if [ "$CURRENT_VERSION" != "4.7.0" ]; then
			echo "[INFO] Installing Godot 4.7.0 with Mono support..."
			godotenv godot install 4.7.0
			godotenv godot use 4.7.0

			# Add godot bin directory to .bashrc for persistent PATH
			if [[ ":$PATH:" != *":$HOME/.config/godotenv/godot/bin:"* ]]; then
				echo 'export PATH="$HOME/.config/godotenv/godot/bin:$PATH"' >> ~/.bashrc
			fi
		else
			echo "[INFO] Godot 4.7.0 already installed"
		fi

		# Check for Godot export templates (needed for builds)
		TEMPLATE_VERSION="4.7.stable.mono"
		TEMPLATE_DIR="$HOME/.local/share/godot/export_templates/$TEMPLATE_VERSION"
		if [ -d "$TEMPLATE_DIR" ]; then
			echo "[INFO] Godot export templates already installed"
		else
			echo "[INFO] Installing Godot 4.7 export templates (~1.2 GB download)..."
			TEMPLATE_URL="https://github.com/godotengine/godot/releases/download/4.7-stable/Godot_v4.7-stable_mono_export_templates.tpz"
			curl -L -o /tmp/godot_export_templates.tpz "$TEMPLATE_URL"
			mkdir -p "$TEMPLATE_DIR"
			unzip -o /tmp/godot_export_templates.tpz -d /tmp/godot_templates_extract
			mv /tmp/godot_templates_extract/templates/* "$TEMPLATE_DIR/"
			rm -rf /tmp/godot_export_templates.tpz /tmp/godot_templates_extract
			echo "[INFO] Export templates installed"
		fi

		# Check for uv
		if ! command -v uv &> /dev/null; then
			echo "[INFO] Installing uv package manager..."
			curl -LsSf https://astral.sh/uv/install.sh | sh
			if [[ ":$PATH:" != *":$HOME/.local/bin:"* ]]; then
				echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.bashrc
			fi
		else
			echo "[INFO] uv already installed: $(uv --version)"
		fi

		# Check for jq
		if ! command -v jq &> /dev/null; then
			echo "[INFO] Installing jq..."
			sudo apt-get install -y jq
		else
			echo "[INFO] jq already installed"
		fi

		echo "[INFO] Dependencies installed successfully"
DEPS_EOF

	log_info "Dependencies installed"
else
	log_info "Skipping dependency installation (--skip-deps)"
fi

# ========================
# Phase 3: Setup Python Environment
# ========================
log_step "Phase 3: Setting Up Python Environment"

ssh "$TARGET" bash << PYTHON_EOF
	set -e
	cd "$TARGET_PATH/BarBoxServices"
	export PATH="\$HOME/.local/bin:\$PATH"

	if [ -d ".venv" ]; then
		echo "[INFO] Virtual environment exists, updating dependencies..."
		source .venv/bin/activate
		uv pip install -e . --quiet
	else
		echo "[INFO] Creating Python virtual environment..."
		uv venv .venv
		source .venv/bin/activate
		uv pip install -e .
	fi

	echo "[INFO] Python environment ready"
PYTHON_EOF

log_info "Python environment configured"

# ========================
# Phase 4: Configure Environment Files
# ========================
log_step "Phase 4: Configuring Environment"

# Frontend config always at root level (export-only deployment)
FRONTEND_ENV_PATH="$TARGET_PATH/.env.local"

# Generate backend .env if it doesn't exist
ssh "$TARGET" bash << ENV_EOF
	set -e
	BACKEND_ENV="$TARGET_PATH/BarBoxServices/.env"

	if [ -f "\$BACKEND_ENV" ]; then
		echo "[INFO] Backend .env already exists"

		# Ensure it has secure JWT secret
		if grep -q "^JWT_SECRET_KEY=dev-" "\$BACKEND_ENV" 2>/dev/null; then
			echo "[WARN] Insecure JWT secret detected, regenerating..."
			JWT_SECRET=\$(openssl rand -base64 64 | tr -d '\n')
			sed -i.bak "s/^JWT_SECRET_KEY=.*/JWT_SECRET_KEY=\$JWT_SECRET/" "\$BACKEND_ENV"
			echo "[INFO] JWT secret regenerated"
		fi

		# Ensure it has a secure box registration secret (required at boot when
		# ENV=prod - a missing/dev- value here crashes the backend on startup,
		# not just box registration)
		if grep -q "^BOX_REGISTRATION_SECRET=dev-" "\$BACKEND_ENV" 2>/dev/null; then
			echo "[WARN] Insecure box registration secret detected, regenerating..."
			REG_SECRET=\$(openssl rand -base64 32 | tr -d '\n')
			sed -i.bak "s/^BOX_REGISTRATION_SECRET=.*/BOX_REGISTRATION_SECRET=\$REG_SECRET/" "\$BACKEND_ENV"
			echo "[INFO] Box registration secret regenerated"
		elif ! grep -q "^BOX_REGISTRATION_SECRET=" "\$BACKEND_ENV" 2>/dev/null; then
			echo "[INFO] Adding box registration secret to existing .env..."
			REG_SECRET=\$(openssl rand -base64 32 | tr -d '\n')
			echo "BOX_REGISTRATION_SECRET=\$REG_SECRET" >> "\$BACKEND_ENV"
			echo "[INFO] Box registration secret added"
		fi

		# Ensure Stripe keys are present
		if ! grep -q "^STRIPE_SECRET_KEY=" "\$BACKEND_ENV" 2>/dev/null; then
			echo "[INFO] Adding Stripe configuration to existing .env..."
			cat >> "\$BACKEND_ENV" << STRIPE_EOF

# Stripe Configuration (test keys)
STRIPE_SECRET_KEY=sk_test_YOUR_STRIPE_TEST_KEY_HERE
STRIPE_WEBHOOK_SECRET=whsec_YOUR_TEST_WEBHOOK_SECRET_HERE
STRIPE_PRICE_5_CREDITS=price_1ScHbzBaj7zkZdiEtqOc99Hq
STRIPE_PRICE_10_CREDITS=price_1ScHcPBaj7zkZdiEmu6KQynb
STRIPE_PRICE_25_CREDITS=price_1ScHcYBaj7zkZdiEiU0fkea1
STRIPE_PRICE_50_CREDITS=price_1ScHcfBaj7zkZdiERTcU7ye5
STRIPE_PRICE_100_CREDITS=price_1ScHckBaj7zkZdiEqAhFumo0
STRIPE_EOF
			echo "[INFO] Stripe configuration added"
		else
			echo "[INFO] Stripe configuration already present"
		fi
	else
		echo "[INFO] Creating backend .env..."
		JWT_SECRET=\$(openssl rand -base64 64 | tr -d '\n')
		REG_SECRET=\$(openssl rand -base64 32 | tr -d '\n')

		cat > "\$BACKEND_ENV" << EOF
# BarBox Backend Configuration
# Auto-generated by deploy.sh on \$(date)

ENV=prod
JWT_SECRET_KEY=\$JWT_SECRET
JWT_ALGORITHM=HS256
JWT_ACCESS_TOKEN_HOURS=2
SQLITE_PATH=app.db
BCRYPT_ROUNDS=12
BOX_REGISTRATION_SECRET=\$REG_SECRET

# Stripe Configuration (test keys)
STRIPE_SECRET_KEY=sk_test_YOUR_STRIPE_TEST_KEY_HERE
STRIPE_WEBHOOK_SECRET=whsec_YOUR_TEST_WEBHOOK_SECRET_HERE
STRIPE_PRICE_5_CREDITS=price_1ScHbzBaj7zkZdiEtqOc99Hq
STRIPE_PRICE_10_CREDITS=price_1ScHcPBaj7zkZdiEmu6KQynb
STRIPE_PRICE_25_CREDITS=price_1ScHcYBaj7zkZdiEiU0fkea1
STRIPE_PRICE_50_CREDITS=price_1ScHcfBaj7zkZdiERTcU7ye5
STRIPE_PRICE_100_CREDITS=price_1ScHckBaj7zkZdiEqAhFumo0
EOF

		chmod 600 "\$BACKEND_ENV"
		echo "[INFO] Backend .env created with secure credentials"
	fi
ENV_EOF

# Generate frontend .env.local if it doesn't exist
ssh "$TARGET" bash << FRONTEND_EOF
	set -e
	FRONTEND_ENV="$FRONTEND_ENV_PATH"

	if [ -f "\$FRONTEND_ENV" ]; then
		echo "[INFO] Frontend .env.local already exists"
		# Extract existing box ID
		BOX_ID=\$(grep "^BARBOX_BOX_ID=" "\$FRONTEND_ENV" | cut -d'=' -f2)
		echo "[INFO] Existing Box ID: \$BOX_ID"
	else
		echo "[INFO] Creating frontend .env.local..."
		BOX_ID=\$(uuidgen)

		cat > "\$FRONTEND_ENV" << EOF
# BarBox Configuration
# Generated on \$(date)

BARBOX_BOX_ID=\$BOX_ID
BARBOX_API_KEY=PENDING_REGISTRATION
BARBOX_BACKEND_URL=http://127.0.0.1:8000
BARBOX_BOX_NAME=barbox_\$(hostname)
BARBOX_VENUE_NAME=venue_\$(hostname)
EOF

		chmod 600 "\$FRONTEND_ENV"
		echo "[INFO] Frontend .env.local created with Box ID: \$BOX_ID"
	fi
FRONTEND_EOF

log_info "Environment configured"

# ========================
# Phase 5: Register Box (if not skipped)
# ========================
if [ "$SKIP_REGISTER" = false ]; then
	log_step "Phase 5: Registering Box"

	# Check if API key is already set
	API_KEY=$(ssh "$TARGET" "grep '^BARBOX_API_KEY=' $FRONTEND_ENV_PATH 2>/dev/null | cut -d'=' -f2" || echo "")

	if [ -n "$API_KEY" ] && [ "$API_KEY" != "PENDING_REGISTRATION" ] && [ "$API_KEY" != "YOUR_API_KEY_HERE" ]; then
		log_info "API key already configured, skipping registration"
		log_info "Current API key: ${API_KEY:0:20}..."
	else
		log_info "Stopping any existing backend processes..."
		ssh "$TARGET" bash << 'STOP_BACKEND_EOF'
			# Stop systemd backend if running
			if systemctl --user is-active barbox-backend.service >/dev/null 2>&1; then
				echo "[INFO] Stopping systemd backend service"
				systemctl --user stop barbox-backend.service
			fi

			# Kill any fastapi processes on port 8000
			if lsof -ti :8000 >/dev/null 2>&1; then
				echo "[INFO] Killing processes on port 8000"
				lsof -ti :8000 | xargs kill -9 2>/dev/null || true
				sleep 1
			fi

			# Verify port is free
			if lsof -ti :8000 >/dev/null 2>&1; then
				echo "[ERROR] Failed to free port 8000"
				exit 1
			fi

			echo "[INFO] Port 8000 is now free"
STOP_BACKEND_EOF

		log_info "Starting backend for registration..."

		# Start backend temporarily
		ssh "$TARGET" bash << REG_START_EOF
			set -e
			cd $TARGET_PATH/BarBoxServices
			source .venv/bin/activate

			# Start backend in background
			nohup python -m fastapi run src/bxctl/web/main.py > /tmp/barbox-backend-reg.log 2>&1 &
			echo $! > /tmp/barbox-backend-reg.pid

			# Wait for backend to be ready
			echo "[INFO] Waiting for backend to start..."
			for i in {1..30}; do
				if curl -s -f http://127.0.0.1:8000/alive > /dev/null 2>&1; then
					echo "[INFO] Backend is ready"
					exit 0
				fi
				sleep 1
			done

			echo "[ERROR] Backend failed to start. Check /tmp/barbox-backend-reg.log"
			cat /tmp/barbox-backend-reg.log | tail -20
			exit 1
REG_START_EOF

		# Register box and get API key
		log_info "Registering box with backend..."

		API_KEY=$(ssh "$TARGET" bash << REG_EOF
			set -e

			# Read box info from .env.local
			BOX_ID=\$(grep "^BARBOX_BOX_ID=" $FRONTEND_ENV_PATH | cut -d'=' -f2)
			BOX_NAME=\$(grep "^BARBOX_BOX_NAME=" $FRONTEND_ENV_PATH | cut -d'=' -f2)

			# This box_id is always new (freshly uuidgen'd in Phase 4), so the
			# backend's create path requires X-Registration-Secret - read it
			# from the same backend .env this loopback call is targeting.
			REG_SECRET=\$(grep "^BOX_REGISTRATION_SECRET=" $TARGET_PATH/BarBoxServices/.env | cut -d'=' -f2)

			# Call registration endpoint (PUT /box/{box_id} always returns API key with deterministic keys)
			RESPONSE=\$(curl -s -X PUT "http://127.0.0.1:8000/box/\$BOX_ID" \\
				-H "Content-Type: application/json" \\
				-H "X-Registration-Secret: \$REG_SECRET" \\
				-d "{\\"id\\": \\"\$BOX_ID\\", \\"name\\": \\"\$BOX_NAME\\", \\"tag\\": \\"\$(hostname)\\"}")

			# Extract API key from response
			API_KEY=\$(echo "\$RESPONSE" | jq -r '.api_key // empty')

			if [ -n "\$API_KEY" ] && [ "\$API_KEY" != "null" ]; then
				echo "\$API_KEY"
			else
				echo "[ERROR] Failed to get API key from response: \$RESPONSE" >&2
				exit 1
			fi
REG_EOF
		)

		if [ -n "$API_KEY" ] && [ "$API_KEY" != "null" ]; then
			log_info "Box registered successfully"
			log_info "API Key: ${API_KEY:0:20}..."

			# Update .env.local with API key
			ssh "$TARGET" "sed -i 's/^BARBOX_API_KEY=.*/BARBOX_API_KEY=$API_KEY/' $FRONTEND_ENV_PATH"
			log_info "API key saved to .env.local"
		else
			log_error "Failed to register box"
			log_warn "You may need to manually register using PUT /box/{box_id}"
		fi

		# Stop temporary backend
		log_info "Stopping registration backend..."
		ssh "$TARGET" bash << 'REG_STOP_EOF'
			if [ -f /tmp/barbox-backend-reg.pid ]; then
				PID=$(cat /tmp/barbox-backend-reg.pid)
				kill $PID 2>/dev/null || true
				rm -f /tmp/barbox-backend-reg.pid
			fi
REG_STOP_EOF
	fi
else
	log_info "Skipping box registration (--skip-register)"
fi

# ========================
# Phase 6: Setup Systemd Services (Optional)
# ========================
log_step "Phase 6: Service Configuration"

# Check if services are already enabled
BACKEND_ENABLED=$(ssh "$TARGET" "systemctl --user is-enabled barbox-backend.service 2>/dev/null || echo 'not-found'")

if [ "$BACKEND_ENABLED" = "enabled" ]; then
	log_info "Systemd services already configured - updating service files..."
	# Update service files to ensure they're current
	ssh "$TARGET" "cp $TARGET_PATH/barbox-backend.service ~/.config/systemd/user/ && cp $TARGET_PATH/barbox-frontend.service ~/.config/systemd/user/"
	log_info "Reloading daemon configuration and restarting services..."
	ssh "$TARGET" "systemctl --user daemon-reload && systemctl --user restart barbox-backend.service && sleep 2 && systemctl --user restart barbox-frontend.service"
	log_info "Services restarted"
else
	log_info "Systemd services not configured"
	log_info "To set up auto-start, run on the target machine:"
	echo ""
	echo "  mkdir -p ~/.config/systemd/user"
	echo "  sed 's|/home/barbox/Desktop/barbox|$TARGET_PATH|g' $TARGET_PATH/barbox-backend.service > ~/.config/systemd/user/barbox-backend.service"
	echo "  sed 's|/home/barbox/Desktop/barbox|$TARGET_PATH|g' $TARGET_PATH/barbox-frontend.service > ~/.config/systemd/user/barbox-frontend.service"
	echo "  systemctl --user daemon-reload"
	echo "  systemctl --user enable barbox-backend barbox-frontend"
	echo "  systemctl --user start barbox-backend barbox-frontend"
	echo ""
fi

# ========================
# Deployment Complete
# ========================
log_step "Deployment Complete!"

echo "Configuration:"
# Use stored FRONTEND_ENV_PATH variable for consistency
if ssh "$TARGET" "test -f $FRONTEND_ENV_PATH"; then
	echo "  Box ID:      $(ssh "$TARGET" "grep BARBOX_BOX_ID $FRONTEND_ENV_PATH | cut -d'=' -f2")"
	echo "  API Key:     $(ssh "$TARGET" "grep BARBOX_API_KEY $FRONTEND_ENV_PATH | cut -d'=' -f2 | cut -c1-20")..."
	echo "  Backend URL: $(ssh "$TARGET" "grep BARBOX_BACKEND_URL $FRONTEND_ENV_PATH | cut -d'=' -f2")"
else
	log_warn "Frontend configuration not found (skipped configuration phase?)"
fi
echo ""
echo "To start services manually:"
echo "  ssh $TARGET"
echo "  cd $TARGET_PATH && ./start_all.sh"
echo ""
echo "To check status:"
echo "  ssh $TARGET 'ps aux | grep -E \"(fastapi|Godot)\"'"
echo ""
echo "GPU performance service (one-time setup, requires sudo):"
echo "  sudo cp $TARGET_PATH/barbox-gpu-performance.service /etc/systemd/system/"
echo "  sudo systemctl daemon-reload"
echo "  sudo systemctl enable --now barbox-gpu-performance"
echo ""
