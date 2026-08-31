# XIV Mini Util 段階的リファクタリング計画

更新日: 2026-07-01

対象は現在の作業ツリーです。過去の完了報告ではなく、現行コード、参照元、テスト、Windows Release成果物を基準に判定します。

## 現在規模

| 領域 | 現在値 |
|---|---:|
| リポジトリ内ファイル | 約206 |
| C# | 166ファイル / 36,124行 |
| TitleBackground | 52ファイル / 18,277行 |
| Shop | 29ファイル / 5,275行 |
| CharaSelect | 17ファイル / 3,642行 |
| `TitleScreenBackgroundService` partial群 | 9ファイル / 8,811行 |
| LogicTests | 作業開始時点434件 + 今回追加5件 |

数値は空行・生成物の扱いで多少変動するため、完了判定には行数だけを使いません。

## 維持する契約

- `PersistentApplyEnabled=false` を維持する。
- 永続configのworld座標をFixOn、カメラ焦点、ground-verifiedへ流さない。
- candidate、territory、finite、run-scoped、scene generation、pre-login snapshotの安全ゲートを維持する。
- 診断キー、コマンド名、JSONキー、既定値、enum値、manifest、配布URL、バージョンを変更しない。
- 自動確認のsettings snapshot、recovery journal、復元順序を変更しない。
- 通常のTitle背景画面は最大4操作の最小構成を維持し、Developer UIを戻さない。
- hook、detour、イベントの解除とDispose順を維持する。

## 2026-07-01 実施状況

### R0 現在状態の固定

- [x] LogicTests
- [x] Debug build
- [x] Windows Release build
- [x] `latest.zip` と `XivMiniUtil.json`
- [x] `git diff --check`
- [x] 未コミット差分の機能別分類
- [x] 主要領域の行数と責務を再計測

開始時点の検証はすべて成功。既存失敗はありませんでした。

### R1 テストランナー分割

- [x] `Program.cs` を起動専用へ縮小
- [x] `TestRunner.cs` と `TestHelpers.cs` を分離
- [x] Configuration、CharaSelect、QuickCheck、TitleBackground safety、UI contract、Shopへ物理分割
- [x] 作業開始時点の未コミット差分を含む434件の名前、検証内容、実行順を維持
- [x] 新しいテストフレームワークや依存を追加しない

構造確認が必要な安全境界・ファイル配置はソース検査を維持しました。純粋ロジックはR4で実テストを追加しています。

### R2 Pluginライフサイクル分割

- [x] サービス構築
- [x] コマンド定義・登録・解除
- [x] コマンドハンドラ
- [x] UIイベント・clipboard
- [x] Dispose
- [x] Shop初期化のfire-and-forget例外をログで観測

サービス生成順、イベント登録順、イベント解除順、既存サービスのDispose順は変更していません。

### R3 TitleScreenBackgroundService境界整理

- [x] automatic check / report / recoveryの12フィールドを1個のstate holderへ集約
- [x] OneClickとQuickCheckから同じholderを利用
- [x] public API、診断キー、復元順序を維持
- [x] hook、pointer、detour、framework updateの意味は変更しない

今回は1責務だけを移しました。camera、timeline、probe、placement、hook lifecycleの残りは、実機検証なしに一括移動しません。

### R4 Shop

- [x] カララント文字列正規化をunsafe Addon探索から分離
- [x] item id / HQ判定をContextMenuから分離
- [x] cache buildのgeneration / cancellation管理をsheet構築から分離
- [x] 純粋ロジックとcoordinatorのテストを追加
- [x] coordinatorをDispose経路へ接続

Lumina sheet探索、Dalamud context menu、unsafe AtkValue読取は依存境界の内側に残しています。

### R5 小規模機能

- [x] Materia、Desynth、Checklist、Submarine、Notification、Market、Commonを確認
- [x] イベント解除、CancellationToken、Dispose、fire-and-forget例外を確認
- [x] Submarine Discord通知の未観測例外を安全ラッパーでログ化

小さいクラスの形式的分割やUI変更は行っていません。

### R6 開発経路と文書

- [x] `verify-refactor-phase.ps1` を LogicTests → Debug → 安全なWindows Release → 成果物 → diff check の順へ統一
- [x] `latest.zip` と `XivMiniUtil.json` の存在を検証
- [x] READMEの現行画面名を修正
- [x] この計画を現在状態へ更新
- [x] Stable 0.3.7 / source assembly 0.3.8は変更しない
- [x] ルート配布物と`docs/archive`は変更しない

## 完了ゲート

```powershell
powershell -ExecutionPolicy Bypass -File scripts\verify-refactor-phase.ps1
```

このスクリプトは次を順番に実行します。

1. `dotnet run --project tools\CharaSelectLogicTests`
2. `dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj`
3. `scripts\release-build.ps1`
4. `latest.zip` / `XivMiniUtil.json` の存在確認
5. `git diff --check`

## 実機確認が必要な境界

今回、native hook、pointer、scene generation、診断キー生成の意味は変更していません。ただし未コミットのTitle Background差分を含む作業ツリー全体については、ゲーム内の1クリック確認で以下を確認する必要があります。

- 「1クリック → ログアウト → ログイン → 自動コピー」の完走
- hook未準備時の1回再初期化と失敗レポート自動コピー
- pre-login snapshotがログイン後にnativeから再取得されないこと
- world座標がFixOn・カメラ焦点・ground-verifiedへ流れないこと

追加の手動probe操作、設定リセット、コマンド入力は要求しません。
