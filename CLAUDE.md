# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

XIV Mini Util - FFXIV用Dalamudプラグイン。マテリア精製・分解支援、NPC販売場所検索機能を提供。

- **Dalamud API**: 14
- **SDK**: `Dalamud.NET.Sdk/14.0.1`
- **Target**: .NET 10

## Build Commands

```bash
# Debug build (devPluginsに自動コピー)
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj

# Release build
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Release

# Release build + zip (リリース用)
bash scripts/release-build.sh
```

**環境変数**: `DALAMUD_HOME` に Dalamud の Hooks ディレクトリを指定（未設定時は自動検出）

## Architecture

```
Plugin.cs (エントリーポイント・DI構成)
├── Services/
│   ├── Common/          # 共通サービス (Inventory, GameUi, Map, Teleport, Job, Chat)
│   ├── Materia/         # マテリア精製 (MateriaExtractService)
│   ├── Desynth/         # 分解 (DesynthService)
│   ├── Shop/            # ショップ検索 (ShopDataCache, ShopSearchService, ContextMenu)
│   ├── Submarine/       # 潜水艦追跡 (SubmarineService, SubmarineDataStorage)
│   └── Notification/    # Discord通知 (DiscordService)
├── Windows/
│   ├── MainWindow.cs    # メインUI (タブ構成)
│   ├── Components/      # タブコンポーネント (Home, Search, Submarine, Settings)
│   └── ShopSearchResultWindow.cs
├── Models/              # データモデル (Common, Desynth, Shop, Submarine)
└── Configuration.cs     # プラグイン設定
```

### Key Patterns

- **Addon操作**: `GameUiService` + `AddonStateTracker` でゲームUIを監視・操作
- **非同期初期化**: `ShopDataCache.InitializeAsync()` でショップデータを構築
- **イベント駆動**: `OnSearchCompleted` イベントで検索結果をウィンドウに通知

## Commands

- `/xivminiutil` (`/xmu`) - メインウィンドウ
- `/xmu config` - 設定タブ
- `/xmu diag` - 診断レポート出力

## Release Workflow

```bash
# 1. バージョン更新 (XivMiniUtil.csproj の Version)
# 2. リリーススキル実行
/dalamud-release
```

または手動:
```bash
bash .claude/skills/dalamud-release/scripts/release.sh [version] [changelog]
```

## Kiro Spec-Driven Development

### Paths
- Steering: `.kiro/steering/`
- Specs: `.kiro/specs/`

### Workflow
1. `/kiro:spec-init "description"` - 仕様初期化
2. `/kiro:spec-requirements {feature}` - 要件定義
3. `/kiro:spec-design {feature}` - 設計
4. `/kiro:spec-tasks {feature}` - タスク生成
5. `/kiro:spec-impl {feature}` - 実装
6. `/kiro:spec-status {feature}` - 進捗確認

### Rules
- 3-phase approval: Requirements → Design → Tasks → Implementation
- Markdown出力は日本語
- `-y` は意図的なfast-track時のみ

## Skills

- `/dalamud-release` - GitHubリリース自動化
- `/codex-review` - Codex CLIによる自動レビュー
