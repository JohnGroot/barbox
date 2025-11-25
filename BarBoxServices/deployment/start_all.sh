#!/bin/bash
set -e

# Start all BarBox services

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

GREEN='\033[0;32m'
BLUE='\033[0;34m'
NC='\033[0m'

log_info() {
	echo -e "${GREEN}[BarBox]${NC} $1"
}

log_step() {
	echo ""
	echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
	echo -e "${BLUE}  $1${NC}"
	echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
	echo ""
}

log_step "Starting BarBox Services"

# Start backend first
log_info "Starting backend..."
"$SCRIPT_DIR/start_backend.sh"

echo ""
log_info "Waiting for backend to initialize..."
sleep 3

# Then start frontend
log_info "Starting frontend..."
"$SCRIPT_DIR/start_frontend.sh"

echo ""
log_step "BarBox Started Successfully"

echo "Services running:"
echo "  Backend:  http://127.0.0.1:8000"
echo "  Frontend: Godot game window"
echo ""
echo "To stop all services: ./stop_all.sh"
echo ""
echo "Check status:"
echo "  Backend:  ps aux | grep fastapi"
echo "  Frontend: ps aux | grep Godot"
echo ""
