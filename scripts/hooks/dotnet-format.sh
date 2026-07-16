#!/usr/bin/env bash
# Pre-commit hook: runs analyzers that regular builds skip (see BarBoxApp/Directory.Build.props).
set -uo pipefail
command -v dotnet >/dev/null || { echo "dotnet not on PATH; install the .NET SDK"; exit 1; }
export RunAnalyzersDuringBuild=true
# Paths from pre-commit are repo-root-relative; dotnet format --include is
# CWD-relative (NOT workspace-relative, and absolute paths silently match nothing),
# so pass them through unchanged — hooks run from the repo root.
args=(BarBoxApp/BarBox.csproj --severity warn --include "$@" --exclude BarBoxApp/addons/ShapesRenderer/)
if ! dotnet format "${args[@]}" --verify-no-changes --verbosity minimal; then
  dotnet format "${args[@]}" --verbosity minimal   # apply auto-fixes for the dev to re-stage
  echo "dotnet format found violations; auto-fixes applied where possible — review and re-stage."
  exit 1
fi
