#!/usr/bin/env bash
set -e

# Change to the BarBoxServices directory (parent of scripts)
cd "$(dirname "$0")/.." || exit 1

echo "Starting BarBox backend service..."

# Start backend in background
uv run fastapi dev src/bxctl/app/main.py &
BACKEND_PID=$!

echo "Backend process started (PID: $BACKEND_PID)"
echo "Waiting for backend to be ready..."

# Wait for backend to respond to health check
TIMEOUT=30
for i in $(seq 1 $TIMEOUT); do
    if curl -s http://127.0.0.1:8000/alive > /dev/null 2>&1; then
        echo "✓ Backend ready at http://127.0.0.1:8000 (took ${i}s)"
        echo "  Documentation: http://127.0.0.1:8000/docs"
        echo "  API Spec: http://127.0.0.1:8000/redoc"

        # Keep script running to keep backend alive
        # Wait for backend process to exit (Ctrl+C will terminate both)
        wait $BACKEND_PID
        exit $?
    fi
    sleep 1
done

# Timeout occurred
echo "ERROR: Backend failed to start within ${TIMEOUT} seconds"
kill $BACKEND_PID 2>/dev/null || true
exit 1
