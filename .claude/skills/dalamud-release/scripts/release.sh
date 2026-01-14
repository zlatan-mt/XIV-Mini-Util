#!/usr/bin/env bash
set -euo pipefail

# Dalamud Plugin Release Script
# Usage: ./release.sh [version] [changelog]
#   version: バージョン番号 (省略時: csprojから取得)
#   changelog: 変更履歴 (省略時: 前回リリースからのコミットを使用)

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]})/../../.." && pwd)"
CSPROJ="$ROOT_DIR/projects/XIV-Mini-Util/XivMiniUtil.csproj"
PLUGINMASTER="$ROOT_DIR/pluginmaster.json"

# バージョン取得
if [[ -n "${1:-}" ]]; then
  VERSION="$1"
else
  VERSION=$(grep -oP '(?<=<Version>)[^<]+' "$CSPROJ")
  echo "csprojからバージョンを取得: $VERSION"
fi

# 変更履歴
if [[ -n "${2:-}" ]]; then
  CHANGELOG="$2"
else
  LAST_TAG=$(git describe --tags --abbrev=0 2>/dev/null || echo "")
  if [[ -n "$LAST_TAG" ]]; then
    CHANGELOG=$(git log "$LAST_TAG"..HEAD --oneline --no-merges | head -10 | tr '\n' ' ')
  else
    CHANGELOG="初回リリース"
  fi
  echo "自動生成した変更履歴: $CHANGELOG"
fi

ZIP_FILE="$ROOT_DIR/XivMiniUtil.zip"

echo "=== Dalamud Plugin Release ==="
echo "Version: $VERSION"
echo "Changelog: $CHANGELOG"
echo ""

# 確認
read -p "リリースを続行しますか? [y/N] " -n 1 -r
echo
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
  echo "キャンセルしました"
  exit 0
fi

# 1. リリースビルド
echo "=== リリースビルド実行 ==="
bash "$ROOT_DIR/scripts/release-build.sh"

if [[ ! -f "$ZIP_FILE" ]]; then
  echo "エラー: $ZIP_FILE が見つかりません" >&2
  exit 1
fi

# 2. GitHubリリース作成
echo "=== GitHubリリース作成 ==="
TAG="v$VERSION"

# 既存タグの確認
if gh release view "$TAG" >/dev/null 2>&1; then
  echo "リリース $TAG は既に存在します。更新しますか?"
  read -p "[y/N] " -n 1 -r
  echo
  if [[ $REPLY =~ ^[Yy]$ ]]; then
    gh release delete "$TAG" --yes
    git tag -d "$TAG" 2>/dev/null || true
    git push origin ":refs/tags/$TAG" 2>/dev/null || true
  else
    echo "キャンセルしました"
    exit 0
  fi
fi

gh release create "$TAG" "$ZIP_FILE" \
  --title "$TAG" \
  --notes "$CHANGELOG"

echo "リリース作成完了: $TAG"

# 3. pluginmaster.json 更新
echo "=== pluginmaster.json 更新 ==="
TIMESTAMP=$(date +%s)
DOWNLOAD_URL="https://github.com/zlatan-mt/XIV-Mini-Util/releases/download/$TAG/XivMiniUtil.zip"

# jq がある場合は使用、なければ sed
if command -v jq &>/dev/null; then
  jq --arg ver "$VERSION" \
     --arg url "$DOWNLOAD_URL" \
     --arg changelog "$CHANGELOG" \
     --argjson ts "$TIMESTAMP" \
     '.[0] |= (
       .AssemblyVersion = $ver |
       .TestingAssemblyVersion = $ver |
       .DownloadLink = $url |
       .DownloadLinkInstall = $url |
       .DownloadLinkUpdate = $url |
       .DownloadLinkTesting = $url |
       .Changelog = $changelog |
       .LastUpdate = $ts
     )' "$PLUGINMASTER" > "${PLUGINMASTER}.tmp" && mv "${PLUGINMASTER}.tmp" "$PLUGINMASTER"
else
  # sed fallback (簡易版)
  sed -i \
    -e "s/\"AssemblyVersion\": \"[^\"]*\"/\"AssemblyVersion\": \"$VERSION\"/" \
    -e "s/\"TestingAssemblyVersion\": \"[^\"]*\"/\"TestingAssemblyVersion\": \"$VERSION\"/" \
    -e "s|\"DownloadLink\": \"[^\"]*\"|\"DownloadLink\": \"$DOWNLOAD_URL\"|" \
    -e "s|\"DownloadLinkInstall\": \"[^\"]*\"|\"DownloadLinkInstall\": \"$DOWNLOAD_URL\"|" \
    -e "s|\"DownloadLinkUpdate\": \"[^\"]*\"|\"DownloadLinkUpdate\": \"$DOWNLOAD_URL\"|" \
    -e "s|\"DownloadLinkTesting\": \"[^\"]*\"|\"DownloadLinkTesting\": \"$DOWNLOAD_URL\"|" \
    -e "s/\"LastUpdate\": [0-9]*/\"LastUpdate\": $TIMESTAMP/" \
    "$PLUGINMASTER"

  echo "注意: Changelogは手動で更新してください"
fi

echo "pluginmaster.json を更新しました"

# 4. コミット・プッシュ
echo "=== Git コミット・プッシュ ==="
git add "$PLUGINMASTER"
git commit -m "chore: release $TAG

$CHANGELOG

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"

git push

echo ""
echo "=== リリース完了 ==="
echo "GitHub Release: https://github.com/zlatan-mt/XIV-Mini-Util/releases/tag/$TAG"
echo ""
echo "Dalamudリポジトリ追加URL:"
echo "https://raw.githubusercontent.com/zlatan-mt/XIV-Mini-Util/main/pluginmaster.json"
