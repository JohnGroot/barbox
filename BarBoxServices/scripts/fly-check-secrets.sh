#!/bin/bash
# Compare Fly.io secrets between production and staging
# Usage: ./scripts/fly-check-secrets.sh

set -e

PROD_APP="barbox-backend"
STAGING_APP="barbox-backend-staging"

echo "=== Comparing Fly.io secrets ==="
echo ""

# Get secret names (not values - fly doesn't expose those)
PROD_SECRETS=$(fly secrets list -a "$PROD_APP" --json | jq -r '.[].name' | sort)
STAGING_SECRETS=$(fly secrets list -a "$STAGING_APP" --json | jq -r '.[].name' | sort)

echo "Production ($PROD_APP):"
echo "$PROD_SECRETS" | sed 's/^/  /'
echo ""

echo "Staging ($STAGING_APP):"
echo "$STAGING_SECRETS" | sed 's/^/  /'
echo ""

# Find differences
MISSING_IN_STAGING=$(comm -23 <(echo "$PROD_SECRETS") <(echo "$STAGING_SECRETS"))
EXTRA_IN_STAGING=$(comm -13 <(echo "$PROD_SECRETS") <(echo "$STAGING_SECRETS"))

EXIT_CODE=0

if [ -n "$MISSING_IN_STAGING" ]; then
    echo "WARNING: MISSING in staging (present in production):"
    echo "$MISSING_IN_STAGING" | sed 's/^/  X /'
    echo ""
    EXIT_CODE=1
fi

if [ -n "$EXTRA_IN_STAGING" ]; then
    echo "INFO: Extra in staging (not in production):"
    echo "$EXTRA_IN_STAGING" | sed 's/^/  + /'
    echo ""
fi

if [ -z "$MISSING_IN_STAGING" ] && [ -z "$EXTRA_IN_STAGING" ]; then
    echo "OK: Secrets are in sync!"
fi

exit $EXIT_CODE
