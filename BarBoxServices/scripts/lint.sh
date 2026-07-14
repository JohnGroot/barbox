#!/usr/bin/env bash
# Static checks: formatting, lint, and type checking.
# Run from repo root or scripts/; paths are resolved relative to this file.
#
# ruff check (select=ALL) and ty check both currently report a backlog of
# pre-existing findings across the codebase - this script surfaces them for
# visibility, it doesn't gate on a clean run. ruff format --check is clean
# and should stay that way; treat a failure there as a real regression.
cd "$(dirname "$0")/.."

status=0

echo "==> ruff format --check"
uv run ruff format --check src/ alembic/ || status=1

echo "==> ruff check"
uv run ruff check src/ alembic/ || status=1

echo "==> ty check"
uv run ty check src/ || status=1

exit "$status"
