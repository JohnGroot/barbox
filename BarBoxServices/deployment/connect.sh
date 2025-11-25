#!/bin/bash

# Quick SSH connection helper for BarBox deployment

TARGET_IP="${1}"
TARGET_USER="${2:-barbox}"

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

if [ -z "$TARGET_IP" ]; then
	echo "Usage: $0 <target-ip> [user]"
	echo ""
	echo "Examples:"
	echo "  $0 100.93.137.42"
	echo "  $0 100.93.137.42 barbox"
	echo ""
	echo "Known BarBox machines:"
	echo "  100.93.137.42 - Linux test box"
	echo ""
	exit 1
fi

echo -e "${GREEN}Connecting to:${NC} $TARGET_USER@$TARGET_IP"
echo ""

# Test connection first
if ! ping -c 1 -W 2 "$TARGET_IP" > /dev/null 2>&1; then
	echo -e "${YELLOW}Warning:${NC} Cannot ping $TARGET_IP (may be normal for some networks)"
	echo ""
fi

# Connect
ssh "$TARGET_USER@$TARGET_IP"
