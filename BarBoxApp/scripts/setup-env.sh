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

    print_info "This script will create .env.local with your development configuration"
    echo ""

    # Generate BARBOX_BOX_ID
    print_info "Generating unique Box ID..."
    BOX_ID=$(generate_uuid)
    print_success "Generated Box ID: $BOX_ID"
    echo ""

    # Get BARBOX_LOCATION_ID
    DEFAULT_LOCATION=$(get_default_location_id)
    print_info "Location ID identifies this physical terminal/machine"
    read -p "Enter Location ID (default: $DEFAULT_LOCATION): " LOCATION_ID
    LOCATION_ID=${LOCATION_ID:-$DEFAULT_LOCATION}
    print_success "Location ID: $LOCATION_ID"
    echo ""

    # Get BARBOX_BACKEND_URL
    DEFAULT_BACKEND="http://localhost:8000"
    print_info "Backend URL is where the BarBoxServices API is running"
    read -p "Enter Backend URL (default: $DEFAULT_BACKEND): " BACKEND_URL
    BACKEND_URL=${BACKEND_URL:-$DEFAULT_BACKEND}
    print_success "Backend URL: $BACKEND_URL"
    echo ""

    # Create .env.local
    print_info "Creating .env.local file..."
    cat > "$ENV_FILE" <<EOF
# BarBox Development Environment Configuration
# Generated on $(date)
# DO NOT commit this file to version control

# Required: Unique identifier for this physical box terminal
# Generated: $(date -u +"%Y-%m-%dT%H:%M:%SZ")
BARBOX_BOX_ID=$BOX_ID

# Location identifier for this development machine
BARBOX_LOCATION_ID=$LOCATION_ID

# Backend service URL
BARBOX_BACKEND_URL=$BACKEND_URL
EOF

    if [ $? -eq 0 ]; then
        print_success "Created .env.local at: $ENV_FILE"
        echo ""

        # Display summary
        echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
        echo "  Configuration Summary"
        echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
        echo ""
        echo "  Box ID:      $BOX_ID"
        echo "  Location:    $LOCATION_ID"
        echo "  Backend URL: $BACKEND_URL"
        echo ""
        echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
        echo ""

        # Next steps
        print_success "Setup complete!"
        echo ""
        print_info "Next steps:"
        echo ""
        echo "  1. Start the backend service:"
        echo "     ${BLUE}cd ../BarBoxServices && sh scripts/dev.sh${NC}"
        echo ""
        echo "  2. Launch Godot editor:"
        echo "     ${BLUE}godot --path . --editor${NC}"
        echo ""
        echo "  3. Check the console for initialization logs:"
        echo "     Look for: ${GREEN}[LocationManager] Loaded environment from: res://.env.local${NC}"
        echo ""

        # Optional: Ask if they want to start backend
        echo ""
        read -p "Do you want to start the backend service now? (y/N): " -n 1 -r
        echo ""
        if [[ $REPLY =~ ^[Yy]$ ]]; then
            BACKEND_DIR="$(cd "$PROJECT_ROOT/../BarBoxServices" 2>/dev/null && pwd)"
            if [ -d "$BACKEND_DIR" ]; then
                print_info "Starting backend service..."
                cd "$BACKEND_DIR"
                if [ -f "scripts/dev.sh" ]; then
                    exec sh scripts/dev.sh
                else
                    print_error "Backend startup script not found at: $BACKEND_DIR/scripts/dev.sh"
                    exit 1
                fi
            else
                print_error "BarBoxServices directory not found at: $BACKEND_DIR"
                print_info "Please navigate to BarBoxServices manually and run: sh scripts/dev.sh"
                exit 1
            fi
        fi
    else
        print_error "Failed to create .env.local"
        exit 1
    fi
}

# Run main function
main
