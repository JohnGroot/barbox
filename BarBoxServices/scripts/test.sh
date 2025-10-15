#! /usr/bin/env bash
set -euo pipefail
echo "Running all HTTP integration tests..."
hurl test/*.hurl
