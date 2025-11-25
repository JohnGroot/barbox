#!/bin/bash
# Builds full export (binary + PCK) with version tag

set -e  # Exit on error

VERSION=${1:-$(date +%Y.%m.%d-%H%M)}
BUILD_DIR="builds/releases/$VERSION"
GODOT_BIN="/Applications/Godot.app/Contents/MacOS/Godot"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"

echo "Building BarBox v$VERSION..."

# Verify Godot binary exists
if [[ ! -x "$GODOT_BIN" ]]; then
	echo "ERROR: Godot binary not found at $GODOT_BIN"
	echo "Please update GODOT_BIN path in this script"
	exit 1
fi

# Clean previous build
rm -rf "$PROJECT_DIR/$BUILD_DIR"
mkdir -p "$PROJECT_DIR/$BUILD_DIR"

# Build C# project first
echo "Building C# project..."
cd "$PROJECT_DIR"
dotnet build BarBox.csproj -c Release

if [[ $? -ne 0 ]]; then
	echo "ERROR: C# build failed"
	exit 1
fi

# Export with Godot
echo "Exporting with Godot..."
$GODOT_BIN --headless --path "$PROJECT_DIR" \
	--export-release "Linux/X11" \
	"$BUILD_DIR/BarBox.x86_64"

# Verify outputs
if [[ ! -f "$PROJECT_DIR/$BUILD_DIR/BarBox.x86_64" ]]; then
	echo "ERROR: Binary export failed"
	exit 1
fi

if [[ ! -f "$PROJECT_DIR/$BUILD_DIR/BarBox.pck" ]]; then
	echo "ERROR: PCK export failed"
	exit 1
fi

# Make binary executable
chmod +x "$PROJECT_DIR/$BUILD_DIR/BarBox.x86_64"

# Create version file
cd "$PROJECT_DIR/$BUILD_DIR"
echo "$VERSION" > VERSION
echo "$(date -Iseconds)" >> VERSION
if git rev-parse HEAD &>/dev/null; then
	echo "$(git rev-parse HEAD)" >> VERSION
else
	echo "NO_GIT_COMMIT" >> VERSION
fi

# Create checksums
shasum -a 256 BarBox.x86_64 BarBox.pck > checksums.txt

echo ""
echo "✓ Build complete: $BUILD_DIR"
echo "  Binary: $(ls -lh BarBox.x86_64 | awk '{print $5}')"
echo "  PCK:    $(ls -lh BarBox.pck | awk '{print $5}')"
echo ""
echo "Deploy with: cd BarBoxServices/deployment && ./deploy.sh <ip> --build $VERSION"
