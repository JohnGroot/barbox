#!/bin/bash
# Test exported build locally before deployment

set -e  # Exit on error

VERSION=${1:-"test"}
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"

echo "Testing export build v$VERSION..."
echo ""

# Build export
"$SCRIPT_DIR/build-export.sh" "$VERSION"

BUILD_DIR="$PROJECT_DIR/builds/releases/$VERSION"

# Verify files exist
echo "Verifying build outputs..."
if [[ ! -f "$BUILD_DIR/BarBox.x86_64" ]]; then
	echo "❌ ERROR: Binary missing"
	exit 1
fi
echo "✓ Binary exists"

if [[ ! -f "$BUILD_DIR/BarBox.pck" ]]; then
	echo "❌ ERROR: PCK missing"
	exit 1
fi
echo "✓ PCK exists"

if [[ ! -x "$BUILD_DIR/BarBox.x86_64" ]]; then
	echo "❌ ERROR: Binary not executable"
	exit 1
fi
echo "✓ Binary is executable"

# Verify checksums
echo ""
echo "Verifying checksums..."
cd "$BUILD_DIR"
if ! shasum -c checksums.txt; then
	echo "❌ ERROR: Checksum mismatch"
	exit 1
fi
echo "✓ Checksums valid"

# Check file sizes (sanity check)
echo ""
echo "Checking file sizes..."
BINARY_SIZE=$(stat -f%z BarBox.x86_64 2>/dev/null || stat -c%s BarBox.x86_64)
PCK_SIZE=$(stat -f%z BarBox.pck 2>/dev/null || stat -c%s BarBox.pck)

echo "  Binary size: $((BINARY_SIZE / 1024 / 1024)) MB"
echo "  PCK size:    $((PCK_SIZE / 1024 / 1024)) MB"

if [[ $BINARY_SIZE -lt 10000000 ]]; then
	echo "⚠ WARNING: Binary seems too small (< 10MB)"
fi

if [[ $PCK_SIZE -lt 1000000 ]]; then
	echo "⚠ WARNING: PCK seems too small (< 1MB)"
fi

# Show version info
echo ""
echo "Version information:"
cat VERSION

echo ""
echo "✅ Export build validation passed"
echo ""
echo "Ready to deploy: cd BarBoxServices/deployment && ./deploy.sh <ip> --build $VERSION"
