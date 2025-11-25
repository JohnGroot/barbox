#!/bin/bash
# Query deployed version information

BARBOX_ROOT="$HOME/Desktop/barbox"

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

echo -e "${GREEN}BarBox Version Information${NC}"
echo ""

# Check deployment mode
if [ -L "$BARBOX_ROOT/current" ]; then
	CURRENT_VERSION=$(readlink "$BARBOX_ROOT/current" | sed 's|releases/||')
	echo "Deployment Mode: ${GREEN}Export${NC}"
	echo "Current Version: ${YELLOW}$CURRENT_VERSION${NC}"
	echo ""

	if [ -f "$BARBOX_ROOT/current/VERSION" ]; then
		echo "Version Details:"
		cat "$BARBOX_ROOT/current/VERSION"
		echo ""
	fi

	# List available versions
	if [ -d "$BARBOX_ROOT/releases" ]; then
		echo "Available Releases:"
		ls -lt "$BARBOX_ROOT/releases" | tail -n +2 | while read -r line; do
			VERSION=$(echo "$line" | awk '{print $9}')
			if [ "$VERSION" == "$CURRENT_VERSION" ]; then
				echo "  * $VERSION (current)"
			else
				echo "    $VERSION"
			fi
		done
	fi
elif [ -d "$BARBOX_ROOT/BarBoxApp" ]; then
	echo "Deployment Mode: ${YELLOW}Source${NC}"
	echo ""

	cd "$BARBOX_ROOT/BarBoxApp"
	if [ -d ".git" ]; then
		echo "Git Information:"
		git log -1 --format="  Commit: %h%n  Author: %an%n  Date:   %ar%n  Message: %s"
	else
		echo "No version information available (source deployment without git)"
	fi
else
	echo "${YELLOW}No deployment found${NC}"
	exit 1
fi
