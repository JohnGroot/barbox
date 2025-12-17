#!/bin/bash
# Sync secrets from local .env file to a Fly.io environment
# Usage: ./scripts/fly-sync-secrets.sh [staging|production]

set -e

ENV="${1:-staging}"

case "$ENV" in
    staging)
        APP="barbox-backend-staging"
        ;;
    production)
        APP="barbox-backend"
        ;;
    *)
        echo "Usage: $0 [staging|production]"
        exit 1
        ;;
esac

if [ ! -f .env ]; then
    echo "Error: .env file not found in current directory"
    echo "Run this script from BarBoxServices root directory"
    exit 1
fi

echo "=== Syncing secrets to $APP ==="
echo ""

# Build secrets string from .env file
# Only sync secrets that should be in Fly.io (STRIPE_*, JWT_*, BCRYPT_*)
SECRETS=""
while IFS='=' read -r key value || [ -n "$key" ]; do
    # Skip comments and empty lines
    [[ "$key" =~ ^[[:space:]]*# ]] && continue
    [[ -z "$key" ]] && continue

    # Trim whitespace
    key=$(echo "$key" | xargs)
    value=$(echo "$value" | xargs)

    # Only sync specific prefixes (secrets that should be in Fly.io)
    if [[ "$key" =~ ^(STRIPE_|JWT_|BCRYPT_) ]]; then
        echo "  Setting $key..."
        SECRETS="$SECRETS $key=$value"
    fi
done < .env

if [ -n "$SECRETS" ]; then
    echo ""
    echo "Applying secrets to $APP..."
    fly secrets set -a "$APP" $SECRETS
    echo ""
    echo "OK: Secrets synced to $APP"
else
    echo "No matching secrets found in .env"
    echo "Looking for: STRIPE_*, JWT_*, BCRYPT_*"
fi
