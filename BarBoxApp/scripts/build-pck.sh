#!/bin/bash
# Builds PCK-only update (faster than full build)

set -e  # Exit on error

VERSION=${1:-$(date +%Y.%m.%d-%H%M)-pck}
BUILD_DIR="../builds/updates/$VERSION"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"

# Resolve Godot binary: env override > PATH > GodotEnv (macOS) > GodotEnv (Linux)
if [[ -z "$GODOT_BIN" ]]; then
	if command -v godot &> /dev/null; then
		GODOT_BIN="$(command -v godot)"
	elif [[ -x "$HOME/Library/Application Support/godotenv/godot/bin/godot" ]]; then
		GODOT_BIN="$HOME/Library/Application Support/godotenv/godot/bin/godot"
	elif [[ -x "$HOME/.config/godotenv/godot/bin/godot" ]]; then
		GODOT_BIN="$HOME/.config/godotenv/godot/bin/godot"
	fi
fi

echo "Building PCK update v$VERSION..."

# Verify Godot binary exists
if [[ ! -x "$GODOT_BIN" ]]; then
	echo "ERROR: Godot binary not found"
	echo "Install via GodotEnv: godotenv godot install 4.7.0"
	echo "Or set GODOT_BIN environment variable"
	exit 1
fi

# Clean previous build
mkdir -p "$PROJECT_DIR/$BUILD_DIR"

# Build C# assemblies
echo "Building C# assemblies..."
cd "$PROJECT_DIR"
dotnet build BarBox.csproj -c Release

if [[ $? -ne 0 ]]; then
	echo "ERROR: C# build failed"
	exit 1
fi

# Export PCK only
echo "Exporting PCK..."
"$GODOT_BIN" --headless --path "$PROJECT_DIR" \
	--export-pack "Linux/X11" \
	"$BUILD_DIR/BarBox.pck"

if [[ ! -f "$PROJECT_DIR/$BUILD_DIR/BarBox.pck" ]]; then
	echo "ERROR: PCK export failed"
	exit 1
fi

# Version info
cd "$PROJECT_DIR/$BUILD_DIR"
echo "$VERSION" > VERSION
echo "$(date -Iseconds)" >> VERSION
echo "PCK_UPDATE" >> VERSION
if git rev-parse HEAD &>/dev/null; then
	echo "$(git rev-parse HEAD)" >> VERSION
else
	echo "NO_GIT_COMMIT" >> VERSION
fi

# Checksum
shasum -a 256 BarBox.pck > checksums.txt

echo ""
echo "✓ PCK update complete: $BUILD_DIR"
echo "  Size: $(ls -lh BarBox.pck | awk '{print $5}')"
echo ""
echo "Deploy with: cd BarBoxServices/deployment && ./deploy.sh <ip> --pck-update $VERSION"
