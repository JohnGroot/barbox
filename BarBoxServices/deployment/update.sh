#!/bin/bash
set -e

# Quick Update Script - Deploy changes and restart services

TARGET_IP="${1}"
TARGET_USER="${2:-barbox}"
TARGET_PATH="${3:-~/Desktop/barbox}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$(dirname "$SCRIPT_DIR")")"  # Go up two levels from BarBoxServices/deployment/

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

log_info() {
	echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
	echo -e "${YELLOW}[WARN]${NC} $1"
}

log_step() {
	echo ""
	echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
	echo -e "${BLUE}  $1${NC}"
	echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
	echo ""
}

if [ -z "$TARGET_IP" ]; then
	echo "Usage: $0 <target-ip> [user] [path]"
	echo ""
	echo "Examples:"
	echo "  $0 100.93.137.42"
	echo "  $0 100.93.137.42 barbox ~/Desktop/barbox"
	echo ""
	exit 1
fi

log_step "BarBox Quick Update"

echo "Target: $TARGET_USER@$TARGET_IP:$TARGET_PATH"
echo ""
read -p "Press Enter to continue or Ctrl+C to cancel..."

# Stop services on target
log_step "Step 1: Stopping services on target"
ssh "$TARGET_USER@$TARGET_IP" "cd $TARGET_PATH && ./stop_all.sh" || log_warn "Services may not have been running"

# Deploy updated code
log_step "Step 2: Deploying updated code"

log_info "Syncing BarBoxServices..."
rsync -avz --delete --progress \
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
	--exclude='GodotDocs_4_4_1' \
	"$PROJECT_ROOT/BarBoxServices/" \
	"$TARGET_USER@$TARGET_IP:$TARGET_PATH/BarBoxServices/"

log_info "Syncing BarBoxApp..."
rsync -avz --delete --progress \
	--exclude='.godot' \
	--exclude='.import' \
	--exclude='.export' \
	--exclude='.export_presets.cfg' \
	--exclude='godot.env' \
	--exclude='.mono' \
	--exclude='data_*' \
	--exclude='mono_crash.*' \
	--exclude='bin' \
	--exclude='obj' \
	--exclude='*.dll' \
	--exclude='*.exe' \
	--exclude='*.mdb' \
	--exclude='*.pdb' \
	--exclude='*.user' \
	--exclude='*.suo' \
	--exclude='*.sln.docstates' \
	--exclude='.DS_Store' \
	--exclude='Thumbs.db' \
	--exclude='ehthumbs.db' \
	--exclude='Desktop.ini' \
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
	--exclude='GodotDocs_4_4_1' \
	"$PROJECT_ROOT/BarBoxApp/" \
	"$TARGET_USER@$TARGET_IP:$TARGET_PATH/BarBoxApp/"

# Update Python dependencies if needed
log_step "Step 3: Updating dependencies"
ssh "$TARGET_USER@$TARGET_IP" bash << UPDATE_DEPS_EOF
	export PATH="\$HOME/.local/bin:\$PATH"
	cd $TARGET_PATH/BarBoxServices
	source .venv/bin/activate
	uv pip install -e . --quiet
UPDATE_DEPS_EOF

# Restart services
log_step "Step 4: Restarting services"

# Check if using systemd or manual start
if ssh "$TARGET_USER@$TARGET_IP" "systemctl --user is-enabled barbox-backend.service" 2>/dev/null; then
	log_info "Using systemd to restart services..."
	ssh "$TARGET_USER@$TARGET_IP" "systemctl --user restart barbox-backend.service && sleep 2 && systemctl --user restart barbox-frontend.service"
	log_info "Services restarted via systemd"
else
	log_info "Using manual start scripts..."
	ssh "$TARGET_USER@$TARGET_IP" "cd $TARGET_PATH && nohup ./start_all.sh > /tmp/barbox-start.log 2>&1 &"
	log_info "Services started manually"
fi

log_step "Update Complete!"

echo "Services should now be running with the latest code"
echo ""
echo "To check status:"
echo "  ssh $TARGET_USER@$TARGET_IP"
echo ""
if ssh "$TARGET_USER@$TARGET_IP" "systemctl --user is-enabled barbox-backend.service" 2>/dev/null; then
	echo "  systemctl --user status barbox-backend barbox-frontend"
else
	echo "  ps aux | grep -E '(fastapi|Godot)'"
fi
echo ""
