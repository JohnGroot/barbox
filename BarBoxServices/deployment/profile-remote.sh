#!/bin/bash
set -e

# Remote profiling helper for the BarBox build/kiosk box.
# Attaches dotTrace/dotMemory (frontend) or py-spy (backend) to the running
# barbox-frontend/barbox-backend systemd --user unit over SSH, then rsyncs
# the resulting snapshot back to a local --out dir for analysis in Rider
# (frontend) or a browser/speedscope.app (backend .svg flamegraph).

TARGET_IP=""
TARGET_USER="barbox"
MODE=""
DURATION=30
MEMORY=false
OUT_DIR="profiling"

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

usage() {
	echo "Usage: $0 <target-ip> <frontend|backend> [duration-seconds] [options]"
	echo ""
	echo "Options:"
	echo "  --memory       dotmemory get-snapshot instead of dottrace (frontend only)"
	echo "  --out <dir>    local output dir for the pulled snapshot (default: profiling/)"
	echo "  --help, -h     Show this help message"
	echo ""
	echo "Examples:"
	echo "  $0 100.93.137.42 backend                # py-spy, 30s"
	echo "  $0 100.93.137.42 frontend 60             # dottrace attach, 60s"
	echo "  $0 100.93.137.42 frontend 30 --memory    # dotmemory snapshot"
	echo ""
	echo "Known BarBox machines:"
	echo "  100.93.137.42 - Linux test box"
	echo ""
	exit 1
}

# Parse arguments
while [[ $# -gt 0 ]]; do
	case $1 in
		--memory)
			MEMORY=true
			shift
			;;
		--out)
			if [[ -z "$2" || "$2" =~ ^- ]]; then
				log_error "--out requires a directory argument"
				exit 1
			fi
			OUT_DIR="$2"
			shift 2
			;;
		--help|-h)
			usage
			;;
		-*)
			log_error "Unknown option: $1"
			exit 1
			;;
		*)
			if [ -z "$TARGET_IP" ]; then
				TARGET_IP="$1"
			elif [ -z "$MODE" ]; then
				MODE="$1"
			elif [[ "$1" =~ ^[0-9]+$ ]]; then
				DURATION="$1"
			else
				log_error "Unexpected argument: $1"
				exit 1
			fi
			shift
			;;
	esac
done

[ -z "$TARGET_IP" ] && usage
[ -z "$MODE" ] && usage

if [[ "$MODE" != "frontend" && "$MODE" != "backend" ]]; then
	log_error "Second argument must be 'frontend' or 'backend', got: $MODE"
	exit 1
fi

if [[ "$MEMORY" == true && "$MODE" != "frontend" ]]; then
	log_error "--memory only applies to frontend (dotmemory is .NET-only)"
	exit 1
fi

SSH_OPTS=(-o BatchMode=yes -o ConnectTimeout=5)
UNIT="barbox-$MODE"
# The remote units run as `systemctl --user` (see barbox-*.service), not
# system units - both start_frontend.sh/stop_all.sh and this script attach
# as the unit's own user, which is also required for dotTrace/dotnet-trace's
# EventPipe attach and py-spy's ptrace attach (same-user only, cross-user
# attach isn't supported by either).
REMOTE_PATH_EXPORT='export PATH="$HOME/.dotnet/tools:$HOME/.local/bin:$PATH"'

log_step "Preflight: $TARGET_USER@$TARGET_IP"
if ! ssh "${SSH_OPTS[@]}" "$TARGET_USER@$TARGET_IP" true 2>/dev/null; then
	log_error "Cannot reach $TARGET_USER@$TARGET_IP via SSH"
	log_warn "Check Tailscale is connected and the box is powered on"
	exit 1
fi
log_info "SSH reachable"

log_step "Resolving $MODE PID"
if [ "$MODE" == "backend" ]; then
	# barbox-backend.service runs fastapi directly (no wrapper script), so
	# systemd's MainPID *is* the process to attach to.
	PID=$(ssh "${SSH_OPTS[@]}" "$TARGET_USER@$TARGET_IP" "systemctl --user show $UNIT.service -p MainPID --value" 2>/dev/null || true)
else
	# barbox-frontend.service's ExecStart is start_frontend.sh, which stays
	# resident for signal forwarding (see start_frontend.sh's trap/wait) -
	# its MainPID is the wrapper script, not the exported Godot binary.
	# Match the pgrep pattern stop_all.sh/start_frontend.sh already use to
	# find the real binary instead of walking child PIDs.
	PID=$(ssh "${SSH_OPTS[@]}" "$TARGET_USER@$TARGET_IP" "pgrep -f 'BarBox\\.x86_64' | head -n1" 2>/dev/null || true)
fi

if [ -z "$PID" ] || [ "$PID" == "0" ]; then
	log_error "Could not resolve a PID for $UNIT.service"
	log_warn "Check it's running: ssh $TARGET_USER@$TARGET_IP systemctl --user status $UNIT"
	exit 1
fi
log_info "$MODE PID: $PID"

log_step "Ensuring remote tooling"
if [ "$MODE" == "backend" ]; then
	ssh "${SSH_OPTS[@]}" "$TARGET_USER@$TARGET_IP" \
		"$REMOTE_PATH_EXPORT; command -v py-spy >/dev/null || (command -v uv >/dev/null && uv tool install py-spy || pip install --user py-spy)"
elif [ "$MEMORY" == true ]; then
	ssh "${SSH_OPTS[@]}" "$TARGET_USER@$TARGET_IP" \
		"$REMOTE_PATH_EXPORT; command -v dotmemory >/dev/null || dotnet tool install -g JetBrains.dotMemory.Console"
else
	ssh "${SSH_OPTS[@]}" "$TARGET_USER@$TARGET_IP" \
		"$REMOTE_PATH_EXPORT; command -v dottrace >/dev/null || dotnet tool install -g JetBrains.dotTrace.GlobalTools"
fi
log_info "Tooling present"

STAMP=$(date +%Y%m%d-%H%M%S)
REMOTE_DIR="/tmp/barbox-profiling"
ssh "${SSH_OPTS[@]}" "$TARGET_USER@$TARGET_IP" "mkdir -p $REMOTE_DIR"

log_step "Collecting (${DURATION}s)"
if [ "$MODE" == "backend" ]; then
	REMOTE_FILE="$REMOTE_DIR/backend-$STAMP.svg"
	ssh "${SSH_OPTS[@]}" "$TARGET_USER@$TARGET_IP" \
		"$REMOTE_PATH_EXPORT; py-spy record --subprocesses -p $PID -d $DURATION -o $REMOTE_FILE" || {
		log_error "py-spy attach failed"
		log_warn "Check /proc/sys/kernel/yama/ptrace_scope on the box - Ubuntu's default (1)"
		log_warn "restricts same-user attach to descendants. Remediate with 'sudo py-spy ...'"
		log_warn "or a temporary 'sudo sysctl kernel.yama.ptrace_scope=0'."
		exit 1
	}
elif [ "$MEMORY" == true ]; then
	ssh "${SSH_OPTS[@]}" "$TARGET_USER@$TARGET_IP" \
		"$REMOTE_PATH_EXPORT; dotmemory get-snapshot $PID --save-to-dir=$REMOTE_DIR"
	REMOTE_FILE=$(ssh "${SSH_OPTS[@]}" "$TARGET_USER@$TARGET_IP" "ls -t $REMOTE_DIR/*.dmw 2>/dev/null | head -n1")
else
	REMOTE_FILE="$REMOTE_DIR/frontend-$STAMP.dtp"
	# Sampling, not Timeline: Timeline snapshots are large and the frontend
	# unit has MemoryMax=4G - prefer the cheaper mode for first remote passes.
	ssh "${SSH_OPTS[@]}" "$TARGET_USER@$TARGET_IP" \
		"$REMOTE_PATH_EXPORT; dottrace attach $PID --profiling-type=Sampling --timeout=${DURATION}s --save-to=$REMOTE_FILE"
fi

if [ -z "$REMOTE_FILE" ]; then
	log_error "No snapshot file was produced on the remote box"
	exit 1
fi

log_step "Pulling snapshot"
mkdir -p "$OUT_DIR"
rsync -avz "$TARGET_USER@$TARGET_IP:$REMOTE_FILE" "$OUT_DIR/"
LOCAL_FILE="$OUT_DIR/$(basename "$REMOTE_FILE")"

if [ ! -s "$LOCAL_FILE" ]; then
	log_error "Downloaded snapshot is empty or missing: $LOCAL_FILE"
	exit 1
fi

# Best-effort cleanup of the remote temp file; don't fail the run over it.
ssh "${SSH_OPTS[@]}" "$TARGET_USER@$TARGET_IP" "rm -f $REMOTE_FILE" 2>/dev/null || true

log_info "Snapshot saved: $LOCAL_FILE ($(du -h "$LOCAL_FILE" | cut -f1))"
if [ "$MODE" == "backend" ]; then
	echo "  Open in a browser (SVG flamegraph), or drag into https://speedscope.app"
else
	echo "  Open in Rider (dotUltimate) to analyze"
fi
