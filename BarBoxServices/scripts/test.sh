#! /usr/bin/env bash
set -euo pipefail

# Delegate to the isolated test runner for proper test isolation
# This ensures each test gets a clean database state
exec "$(dirname "$0")/test-isolated.sh"
