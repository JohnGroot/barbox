#!/bin/bash
# Remove old releases, keeping N most recent

KEEP_RELEASES=${1:-5}
BARBOX_ROOT="$HOME/Desktop/barbox"
RELEASES_DIR="$BARBOX_ROOT/releases"

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

log_info() {
	echo -e "${GREEN}[Cleanup]${NC} $1"
}

log_warn() {
	echo -e "${YELLOW}[Cleanup]${NC} $1"
}

log_error() {
	echo -e "${RED}[Cleanup]${NC} $1"
}

# Check if releases directory exists
if [ ! -d "$RELEASES_DIR" ]; then
	log_info "No releases directory found at $RELEASES_DIR"
	log_info "Nothing to clean up"
	exit 0
fi

# Get current version (don't delete)
if [ ! -L "$BARBOX_ROOT/current" ]; then
	log_warn "No 'current' symlink found"
	log_warn "Cannot determine active release - aborting for safety"
	exit 1
fi

CURRENT_VERSION=$(readlink "$BARBOX_ROOT/current" | sed 's|releases/||')
log_info "Current version: $CURRENT_VERSION"
log_info "Keeping $KEEP_RELEASES most recent releases"
echo ""

# List all releases sorted by modification time (newest first)
ALL_RELEASES=$(ls -t "$RELEASES_DIR")
COUNT=0
DELETED_COUNT=0

for release in $ALL_RELEASES; do
	COUNT=$((COUNT + 1))

	# Always keep current version
	if [ "$release" == "$CURRENT_VERSION" ]; then
		log_info "✓ Keeping current: $release"
		continue
	fi

	# Keep N most recent
	if [ $COUNT -le $KEEP_RELEASES ]; then
		log_info "✓ Keeping recent:  $release"
	else
		log_warn "✗ Removing old:    $release"
		rm -rf "$RELEASES_DIR/$release"
		DELETED_COUNT=$((DELETED_COUNT + 1))
	fi
done

echo ""
if [ $DELETED_COUNT -eq 0 ]; then
	log_info "No releases removed (keeping $KEEP_RELEASES most recent)"
else
	log_info "Removed $DELETED_COUNT old release(s)"
fi

# Show disk usage
if command -v du &> /dev/null; then
	TOTAL_SIZE=$(du -sh "$RELEASES_DIR" 2>/dev/null | cut -f1)
	log_info "Total releases directory size: $TOTAL_SIZE"
fi
