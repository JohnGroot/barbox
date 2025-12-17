#!/bin/bash
# Fly.io deployment wrapper with environment selection

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"

usage() {
    echo "Usage: $0 <environment>"
    echo ""
    echo "Environments:"
    echo "  staging     Deploy to barbox-backend-staging"
    echo "  production  Deploy to barbox-backend"
    exit 1
}

if [ -z "$1" ]; then
    usage
fi

ENV="$1"

case "$ENV" in
    staging)
        APP="barbox-backend-staging"
        CONFIG="fly.staging.toml"
        ;;
    production|prod)
        APP="barbox-backend"
        CONFIG="fly.toml"
        ;;
    *)
        echo "Error: Unknown environment '$ENV'"
        usage
        ;;
esac

cd "$PROJECT_DIR"

echo "=== Fly.io Deployment ==="
echo "Environment: $ENV"
echo "App: $APP"
echo "Config: $CONFIG"
echo ""

# Confirmation for production
if [ "$ENV" = "production" ] || [ "$ENV" = "prod" ]; then
    echo "⚠️  WARNING: You are deploying to PRODUCTION"
    read -p "Are you sure? (yes/no): " confirm
    if [ "$confirm" != "yes" ]; then
        echo "Deployment cancelled."
        exit 0
    fi
fi

echo ""
echo "Deploying..."
fly deploy --config "$CONFIG"

echo ""
echo "✅ Deployment complete!"
echo ""
echo "Verify with:"
echo "  curl https://${APP}.fly.dev/alive"
