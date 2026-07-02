# R0 current-state baseline

取得日: 2026-07-01

## 開始時検証

- `dotnet run --project tools\CharaSelectLogicTests`: 成功
- Debug build: 成功、警告0、エラー0
- `scripts\release-build.ps1`: 成功、警告0、エラー0
- `projects/XIV-Mini-Util/bin/Release/XivMiniUtil/latest.zip`: 生成確認
- `projects/XIV-Mini-Util/bin/Release/XivMiniUtil.json`: 生成確認
- `git diff --check`: 成功

リファクタリング前に分離すべき既存失敗はありませんでした。

## 開始時の未コミット差分

- Title Background: world座標対応診断、QuickCheck、1クリック確認、recovery snapshot
- UI: 通常Title背景画面の圧縮、CharaSelect表示整理、開発診断ページ分離
- Configuration: Title Backgroundのanchor territory / experimental flag
- Tests: 上記の安全境界・UI・1クリック契約の構造/純粋ロジック検証
- Build: Windows Release package検証スクリプト
- Repository guidance: ルート`AGENTS.md`

これらはユーザーの既存作業として保持し、巻き戻し・別実装への置換を行わない。

## 開始時の主要責務

| 対象 | 開始時状態 |
|---|---|
| `tools/CharaSelectLogicTests/Program.cs` | 434テストとhelperを単一ファイルで実行 |
| `Plugin.cs` | サービス構築、command、UIイベント、clipboard、Disposeが同居 |
| `TitleScreenBackgroundService` | automatic checkを含む多数の可変フィールドが本体へ集中 |
| `ColorantItemResolver` | unsafe Addon探索と純粋文字列正規化が同居 |
| `ShopDataCache` | sheet構築とgeneration/cancellation管理が同居 |
| `ContextMenuService` | Dalamud menu処理とitem id/HQ正規化が同居 |

## 現在の計測方法

行数はPowerShellの`[System.IO.File]::ReadAllLines(path).Length`で数える。空行を含むため、過去資料の`Measure-Object -Line`値とは直接比較しない。
