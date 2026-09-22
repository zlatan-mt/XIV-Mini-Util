#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Default assumes WSL with the Windows user profile mounted under /mnt/c.
# Override with DALAMUD_HOOKS_ROOT if your XIVLauncher path differs.
HOOKS_ROOT_DEFAULT="/mnt/c/Users/${WIN_USER:-$USER}/AppData/Roaming/XIVLauncher/addon/Hooks"
HOOKS_ROOT="${DALAMUD_HOOKS_ROOT:-$HOOKS_ROOT_DEFAULT}"

if [[ ! -d "$HOOKS_ROOT" ]]; then
  echo "Hooks root not found: $HOOKS_ROOT" >&2
  echo "Set DALAMUD_HOOKS_ROOT to the correct path." >&2
  exit 1
fi

LATEST_HOOKS_DIR="$(ls -td "$HOOKS_ROOT"/*/ 2>/dev/null | head -n 1 || true)"
if [[ -z "$LATEST_HOOKS_DIR" ]]; then
  echo "No hooks directories found under: $HOOKS_ROOT" >&2
  exit 1
fi

DOTNET_BIN="${DOTNET_BIN:-dotnet}"

PROJECT="$ROOT_DIR/projects/XIV-Mini-Util/XivMiniUtil.csproj"
BUILD_CONFIG="Release"

echo "Using hooks: $LATEST_HOOKS_DIR"

DALAMUD_HOME="$LATEST_HOOKS_DIR" \
DOTNET_CLI_TELEMETRY_OPTOUT=1 \
"$DOTNET_BIN" build "$PROJECT" -c "$BUILD_CONFIG" -p:DevPluginOutputDir=

ZIP_PATH="$ROOT_DIR/XivMiniUtil.zip"
PACKAGE_PATH="$ROOT_DIR/projects/XIV-Mini-Util/bin/Release/XivMiniUtil/latest.zip"

if [[ ! -f "$PACKAGE_PATH" ]]; then
  echo "Release package not found: $PACKAGE_PATH" >&2
  exit 1
fi

cp "$PACKAGE_PATH" "$ZIP_PATH"
echo "Created: $ZIP_PATH"
