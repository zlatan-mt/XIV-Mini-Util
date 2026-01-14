---
name: dalamud-release
description: |
  Dalamudプラグインのリリース処理を自動化する。GitHubにリリースを作成し、pluginmaster.jsonを更新してプラグインを配布可能にする。
  使用タイミング: (1) 新バージョンをリリースしたい時 (2) GitHubリリースを作成したい時 (3) プラグインをDalamudリポジトリとして公開したい時
---

# Dalamud Plugin Release

Dalamudプラグインを GitHub に公開し、Dalamud のカスタムリポジトリとして配布可能にする。

## 前提条件

- `gh` CLI がインストール済みで認証済み
- WSL/bash 環境が利用可能
- プロジェクトルートに `scripts/release-build.sh` が存在

## リリース手順

### 1. バージョン確認

csproj からバージョンを確認:
```bash
grep -oP '(?<=<Version>)[^<]+' projects/XIV-Mini-Util/XivMiniUtil.csproj
```

### 2. リリースビルド

```bash
bash scripts/release-build.sh
```

成功すると `XivMiniUtil.zip` がプロジェクトルートに生成される。

### 3. GitHub リリース作成

```bash
VERSION="0.x.x"  # csprojのバージョンに合わせる
gh release create "v${VERSION}" XivMiniUtil.zip \
  --title "v${VERSION}" \
  --notes "リリースノート"
```

### 4. pluginmaster.json 更新

以下のフィールドを新バージョンに更新:
- `AssemblyVersion`
- `TestingAssemblyVersion`
- `DownloadLink`, `DownloadLinkInstall`, `DownloadLinkUpdate`, `DownloadLinkTesting` のバージョン番号
- `Changelog`
- `LastUpdate` (Unix timestamp: `date +%s`)

### 5. コミット・プッシュ

```bash
git add pluginmaster.json
git commit -m "chore: release v${VERSION}"
git push
```

## 自動化スクリプト

完全自動化は `scripts/release.sh` を使用:
```bash
bash .claude/skills/dalamud-release/scripts/release.sh [version] [changelog]
```

引数省略時:
- version: csprojから自動取得
- changelog: 前回リリースからのコミットメッセージを使用

## Dalamudリポジトリとして追加

ユーザーは以下のURLをDalamudの「試験版プラグインリポジトリ」に追加:
```
https://raw.githubusercontent.com/zlatan-mt/XIV-Mini-Util/main/pluginmaster.json
```
