#!/usr/bin/env bash
set -e

# BarBox Development Environment Setup Script
# Creates .env.local with proper configuration for local development

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$PROJECT_ROOT/.env.local"
ENV_EXAMPLE="$PROJECT_ROOT/.env.example"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Print colored output
print_info() {
    echo -e "${BLUE}ℹ ${NC}$1"
}

print_success() {
    echo -e "${GREEN}✓${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}⚠${NC} $1"
}

print_error() {
    echo -e "${RED}✗${NC} $1"
}

# Generate a new UUID (cross-platform)
generate_uuid() {
    if command -v uuidgen &> /dev/null; then
        # macOS and some Linux distros
        uuidgen | tr '[:upper:]' '[:lower:]'
    elif command -v python3 &> /dev/null; then
        # Fallback to Python
        python3 -c "import uuid; print(str(uuid.uuid4()))"
    elif command -v python &> /dev/null; then
        # Fallback to Python 2
        python -c "import uuid; print(str(uuid.uuid4()))"
    else
        # Manual fallback - random hex values
        local uuid=$(cat /dev/urandom | tr -dc 'a-f0-9' | fold -w 32 | head -n 1)
        echo "${uuid:0:8}-${uuid:8:4}-${uuid:12:4}-${uuid:16:4}-${uuid:20:12}"
    fi
}

# Get machine name in lowercase with underscores
get_default_location_id() {
    local machine_name
    if command -v hostname &> /dev/null; then
        machine_name=$(hostname -s 2>/dev/null || hostname)
    else
        machine_name="unknown"
    fi
    echo "$machine_name" | tr '[:upper:]' '[:lower:]' | tr '-' '_'
}

# Fetch development credentials from running backend's /test/seed endpoint
# This ensures we use the ACTUAL derived API key, not a copied algorithm
fetch_dev_credentials() {
    local seed_response
    seed_response=$(curl -s -X POST http://127.0.0.1:8000/test/seed 2>/dev/null)

    if [[ -z "$seed_response" ]]; then
        return 1
    fi

    # Parse JSON response for box_id and box_api_key
    # Response format: {"status":"success","data":{"box_id":"...","box_api_key":"..."}}
    DEV_BOX_ID=$(echo "$seed_response" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d['data']['box_id'])" 2>/dev/null)
    DEV_API_KEY=$(echo "$seed_response" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d['data']['box_api_key'])" 2>/dev/null)

    if [[ -n "$DEV_BOX_ID" ]] && [[ -n "$DEV_API_KEY" ]]; then
        return 0
    fi
    return 1
}

# Start backend temporarily to fetch credentials
start_backend_for_seed() {
    local backend_dir="$PROJECT_ROOT/../BarBoxServices"

    # Start backend in background using subshell
    (cd "$backend_dir" && source .venv/bin/activate && uv run fastapi dev src/bxctl/app/main.py &>/dev/null) &
    local backend_pid=$!

    # Wait for backend to be ready (max 30 seconds)
    local timeout=30
    for i in $(seq 1 $timeout); do
        if curl -s http://127.0.0.1:8000/alive > /dev/null 2>&1; then
            echo "$backend_pid"
            return 0
        fi
        sleep 1
    done

    kill $backend_pid 2>/dev/null || true
    return 1
}

# Stop backend process and its children
stop_backend() {
    local pid=$1
    if [[ -n "$pid" ]]; then
        kill $pid 2>/dev/null || true
        # Also kill any child processes (FastAPI/uvicorn)
        pkill -P $pid 2>/dev/null || true
        # Give processes time to clean up
        sleep 1
        # Force kill if still running
        kill -9 $pid 2>/dev/null || true
        pkill -9 -P $pid 2>/dev/null || true
    fi
}

# Main setup function
main() {
    echo ""
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo "  BarBox Development Environment Setup"
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""

    # Check if .env.local already exists
    if [ -f "$ENV_FILE" ]; then
        print_warning ".env.local already exists at: $ENV_FILE"
        echo ""
        read -p "Do you want to overwrite it? (y/N): " -n 1 -r
        echo ""
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            print_info "Setup cancelled. Existing .env.local unchanged."
            exit 0
        fi
        echo ""
    fi

    # Check if .env.example exists
    if [ ! -f "$ENV_EXAMPLE" ]; then
        print_error ".env.example not found at: $ENV_EXAMPLE"
        print_error "Please ensure you're running this from the project directory"
        exit 1
    fi

    print_info "This script will create .env.local with development credentials"
    print_info "(Credentials are fetched from the backend's /test/seed endpoint)"
    echo ""

    # Fetch development credentials from backend
    print_info "Fetching development credentials from backend..."

    # Check if backend is already running
    BACKEND_WAS_RUNNING=false
    BACKEND_PID=""
    if curl -s http://127.0.0.1:8000/alive > /dev/null 2>&1; then
        print_info "Backend already running, fetching credentials..."
        BACKEND_WAS_RUNNING=true
    else
        print_info "Starting backend temporarily to fetch credentials..."
        BACKEND_PID=$(start_backend_for_seed)
        if [[ -z "$BACKEND_PID" ]]; then
            print_error "Failed to start backend. Please ensure BarBoxServices is set up."
            print_info "Run: cd ../BarBoxServices && python3 -m venv .venv && source .venv/bin/activate && uv pip install -e . && cp .env.example .env"
            exit 1
        fi
        print_success "Backend started (will be stopped after setup)"
    fi

    # Fetch credentials from /test/seed endpoint
    if fetch_dev_credentials; then
        print_success "Box ID: $DEV_BOX_ID"
        print_success "API Key: ${DEV_API_KEY:0:8}..."
    else
        print_error "Failed to fetch credentials from backend"
        [[ "$BACKEND_WAS_RUNNING" == "false" ]] && stop_backend "$BACKEND_PID"
        exit 1
    fi

    # Stop backend if we started it
    if [[ "$BACKEND_WAS_RUNNING" == "false" ]] && [[ -n "$BACKEND_PID" ]]; then
        print_info "Stopping temporary backend..."
        stop_backend "$BACKEND_PID"
    fi

    # Set variables from fetched credentials
    BOX_ID="$DEV_BOX_ID"
    API_KEY="$DEV_API_KEY"
    BOX_NAME="dev_box"
    VENUE_NAME="dev_local"
    LOCATION_ID=$(get_default_location_id)
    BACKEND_URL="http://localhost:8000"

    print_success "Location ID: $LOCATION_ID"
    echo ""

    # Create .env.local
    print_info "Creating .env.local file..."
    cat > "$ENV_FILE" <<EOF
# BarBox Development Environment Configuration
# Generated on $(date) by setup-env.sh
# DO NOT commit this file to version control

# Box ID: Matches backend auto-seeded test box
BARBOX_BOX_ID=$BOX_ID

# API Key: Fetched from backend /test/seed endpoint
BARBOX_API_KEY=$API_KEY

# Box/Venue names for display and data scoping
BARBOX_BOX_NAME=$BOX_NAME
BARBOX_VENUE_NAME=$VENUE_NAME

# Location ID: Identifies this physical terminal
BARBOX_LOCATION_ID=$LOCATION_ID

# Backend URL: Local development server
BARBOX_BACKEND_URL=$BACKEND_URL
EOF

    if [ $? -eq 0 ]; then
        print_success "Created .env.local at: $ENV_FILE"
        echo ""

        # Display summary
        echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
        echo "  Development Configuration Summary"
        echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
        echo ""
        echo "  Box ID:      $BOX_ID"
        echo "  API Key:     ${API_KEY:0:8}..."
        echo "  Box Name:    $BOX_NAME"
        echo "  Venue Name:  $VENUE_NAME"
        echo "  Location:    $LOCATION_ID"
        echo "  Backend URL: $BACKEND_URL"
        echo ""
        echo "  These credentials match the backend's auto-seeded test data."
        echo ""
        echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
        echo ""

        # Build C# project
        print_info "Building C# project..."
        if dotnet build "$PROJECT_ROOT" --verbosity quiet 2>&1; then
            print_success "C# project built successfully"
        else
            print_warning "C# build had issues - Godot may show errors on first open"
            print_info "Run 'dotnet build' manually if needed"
        fi
        echo ""

        # Setup complete
        print_success "Setup complete!"
        echo ""
        print_info "Next steps:"
        echo ""
        echo "  1. Open Godot and run the project:"
        echo "     ${BLUE}godotenv godot --path . --editor${NC}"
        echo ""
        echo "  2. Press F5 to run - backend will auto-start"
        echo ""
        echo "  Or start the backend manually first:"
        echo "     ${BLUE}cd ../BarBoxServices && sh scripts/dev.sh${NC}"
        echo ""
    else
        print_error "Failed to create .env.local"
        exit 1
    fi
}

# Run main function
main
