#!/usr/bin/env bash
# Change to the BarBoxServices directory (parent of scripts)
cd "$(dirname "$0")/.." || exit 1
uv run fastapi dev src/bxctl/web/main.py
