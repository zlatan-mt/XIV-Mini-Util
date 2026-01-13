#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HOOKS_ROOT_DEFAULT="/mnt/c/Users/MonaT/AppData/Roaming/XIVLauncher/addon/Hooks"
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

DOTNET_BIN="$ROOT_DIR/.dotnet_home/.dotnet/dotnet"
if [[ ! -x "$DOTNET_BIN" ]]; then
  DOTNET_BIN="dotnet"
fi

PROJECT="$ROOT_DIR/projects/XIV-Mini-Util/XivMiniUtil.csproj"
DEV_PLUGIN_OUTPUT_DIR="$ROOT_DIR/.devplugins/XivMiniUtil/"
BUILD_CONFIG="Release"

echo "Using hooks: $LATEST_HOOKS_DIR"

DALAMUD_HOME="$LATEST_HOOKS_DIR" \
DOTNET_CLI_TELEMETRY_OPTOUT=1 \
"$DOTNET_BIN" build "$PROJECT" -c "$BUILD_CONFIG" -p:DevPluginOutputDir="$DEV_PLUGIN_OUTPUT_DIR"

ZIP_PATH="$ROOT_DIR/XivMiniUtil.zip"
DLL_PATH="$ROOT_DIR/projects/XIV-Mini-Util/bin/Release/XivMiniUtil.dll"
JSON_PATH="$ROOT_DIR/projects/XIV-Mini-Util/bin/Release/XivMiniUtil.json"

if [[ ! -f "$DLL_PATH" || ! -f "$JSON_PATH" ]]; then
  echo "Build outputs not found. Expected:" >&2
  echo "  $DLL_PATH" >&2
  echo "  $JSON_PATH" >&2
  exit 1
fi

zip -j "$ZIP_PATH" "$DLL_PATH" "$JSON_PATH"
echo "Created: $ZIP_PATH"
