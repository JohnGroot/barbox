#!/bin/bash
set -e

# Lock AMD GPU DPM to high performance mode for consistent frame timing.
# Requires root. Exits 0 if no AMD GPU found (not an error).
# Deployed via barbox-gpu-performance.service (system-level oneshot at boot).

SYSFS_DPM_PATTERN="/sys/class/drm/card*/device/power_dpm_force_performance_level"

# Find AMD GPU sysfs entries
shopt -s nullglob
DPM_FILES=($SYSFS_DPM_PATTERN)
shopt -u nullglob

if [ ${#DPM_FILES[@]} -eq 0 ]; then
	echo "[GPU] No AMD GPU DPM sysfs entries found, skipping"
	exit 0
fi

for DPM_FILE in "${DPM_FILES[@]}"; do
	DEVICE_DIR=$(dirname "$DPM_FILE")
	DRIVER_LINK="$DEVICE_DIR/driver"

	# Verify this is an amdgpu device
	if [ -L "$DRIVER_LINK" ]; then
		DRIVER_NAME=$(basename "$(readlink "$DRIVER_LINK")")
		if [ "$DRIVER_NAME" != "amdgpu" ]; then
			echo "[GPU] $DPM_FILE: driver is '$DRIVER_NAME', not amdgpu, skipping"
			continue
		fi
	else
		echo "[GPU] $DPM_FILE: no driver symlink found, skipping"
		continue
	fi

	# Check current state (idempotent)
	CURRENT=$(cat "$DPM_FILE" 2>/dev/null || echo "unknown")
	if [ "$CURRENT" = "high" ]; then
		echo "[GPU] $DPM_FILE: already set to 'high'"
		continue
	fi

	echo "[GPU] $DPM_FILE: setting performance level from '$CURRENT' to 'high'"
	echo "high" > "$DPM_FILE"
	echo "[GPU] $DPM_FILE: done"
done
