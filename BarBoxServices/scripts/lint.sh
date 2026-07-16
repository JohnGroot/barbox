#!/usr/bin/env bash
# Static checks: formatting, lint, and type checking.
# Run from repo root or scripts/; paths are resolved relative to this file.
cd "$(dirname "$0")/.."

status=0

echo "==> ruff format --check"
uv run ruff format --check src/ alembic/ || status=1

echo "==> ruff check"
uv run ruff check src/ alembic/ || status=1

echo "==> ty check"
uv run ty check src/ || status=1

exit "$status"
