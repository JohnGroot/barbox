#!/bin/bash
set -e

# Build and Deploy - Builds locally then deploys to remote machine
# Usage: ./build-and-deploy.sh <target-ip> [options]

TARGET_IP="$1"
shift || true  # Shift to pass remaining args to deploy.sh

if [ -z "$TARGET_IP" ]; then
	echo "Usage: $0 <target-ip> [deploy.sh options]"
	echo ""
	echo "Examples:"
	echo "  $0 192.168.1.100"
	echo "  $0 100.93.137.42 --skip-deps"
	exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_SCRIPTS="$SCRIPT_DIR/../../BarBoxApp/scripts"

# Generate version tag
VERSION=$(date +%Y.%m.%d-%H%M)

echo "=========================================="
echo "  Build and Deploy v$VERSION"
echo "  Target: $TARGET_IP"
echo "=========================================="
echo ""

# Step 1: Build export
echo "[1/2] Building export..."
cd "$APP_SCRIPTS"
sh build-export.sh "$VERSION"

# Step 2: Deploy to target
echo ""
echo "[2/2] Deploying to $TARGET_IP..."
cd "$SCRIPT_DIR"
sh deploy.sh "$TARGET_IP" --build "$VERSION" "$@"

echo ""
echo "Build and deploy complete!"
