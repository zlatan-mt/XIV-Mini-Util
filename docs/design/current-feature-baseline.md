# 現行機能ベースライン

## 目的

この文書は、`XIV Mini Util` の現行実装を次の機能設計の出発点として整理したものである。  
要件定義書ではなく、2026-04-20 時点の実装ベースの機能設計メモとして扱う。

## 対象範囲

- プラグイン本体: `projects/XIV-Mini-Util`
- UI入口: `Plugin.cs`, `MainWindow.cs`
- 主要設定: `Configuration.cs`
- 主要機能: Materia / Desynth / Shop Search / Universalis Market Search / Checklist / Submarines / Duty Ready Notification

## プラグイン概要

- FFXIV 用 Dalamud プラグイン
- 技術基盤は `Dalamud API 14` と `.NET 10`
- メインコマンドは `/xivminiutil` と `/xmu`
- メイン UI は `Home`, `Search`, `Checklist`, `Submarines`, `Settings` の 5 タブ構成

## 現在の機能一覧

### 1. マテリア精製支援

目的:
装備品の中から精製可能な対象を検出し、自動精製のオン・オフを提供する。

現状:
- `Home` タブと `Settings > General & Materia` から有効化できる
- 対象件数、状態、処理中かどうかを表示する
- `InventoryCacheService` により対象件数を事前集計する
- `AddonStateTracker` を使って関連アドオンの状態を追跡する

制約:
- `Configuration.MateriaFeatureEnabled` で機能自体を無効化できる
- README と既存メモ上では、公開版では慎重運用を前提としている
- UI 上は存在するが、公開判断時には「常時有効にする機能」ではなく「明示的に使う補助機能」として扱うのが妥当

### 2. アイテム分解支援

目的:
所持品内の分解可能アイテムを条件付きで処理し、誤操作のリスクを下げる。

現状:
- `Home` タブで分解対象件数、レベル範囲、分解モードを確認できる
- `分解開始` 押下時のみ実行する
- 実行前に確認ダイアログを出す
- 上位 10 件の候補をプレビューする
- 最高 IL 基準の警告しきい値を持つ
- 対象レベル範囲、分解対象数、ジョブ条件を設定できる

現在の仕様:
- 対象スコープは `所持品のみ`
- `DesynthTargetMode.All` と `DesynthTargetMode.Count` を持つ
- `Checklist` や `Shop` と違い、UI 上の即時操作型機能

制約:
- `Configuration.DesynthFeatureEnabled` で機能全体を無効化できる
- 誤分解リスクが高いため、次の機能追加でも「対象の見える化」と「確認導線」は維持すべき

### 3. NPC 販売場所検索

目的:
アイテムの販売 NPC と販売場所を検索し、チャット出力、結果ウィンドウ、マップピン、テレポに接続する。

現状:
- `Search` タブでアイテム名検索ができる
- コンテキストメニュー経由の検索も提供する
- ショップデータキャッシュを起動時に初期化する
- 設定から手動再構築できる
- 結果はチャット表示と専用ウィンドウ表示を切り替えられる
- エリア優先度を持ち、候補順位に反映する
- 自動テレポ設定を持つ
- 診断レポート出力コマンド `/xivminiutil diag` を持つ

現在の仕様:
- `ShopDataCache` が検索用データの構築と保持を担当する
- `ShopSearchService` が検索結果の組み立てと UI 連携を担当する
- `MapService`, `TeleportService`, `ChatService` が結果表示を補助する

制約:
- データ構築が完了するまで検索 UI は準備中表示になる
- 検索品質はキャッシュ品質とエリア優先度設定に依存する
- 右クリック起点の導線は UI アドオン依存なので、将来拡張時も分離を維持したい

### 4. 日課チェックリスト

目的:
Daily / Weekly の日課を手動管理し、ゲーム内通知と Discord 通知に接続する。

現状:
- `Checklist` タブから項目の追加、完了管理、削除ができる
- 項目ごとに `Daily` / `Weekly` を選べる
- 項目ごとに有効状態、ゲーム内通知、Discord 通知、通知時刻を設定できる
- フィルタ表示と一括リセットを提供する
- `Settings > Checklist` から機能の有効化、Discord 通知有効化、週次リセット曜日を設定できる

現在の仕様:
- 永続データは `Configuration.ChecklistItems` に保持する
- `ChecklistService` が追加、削除、完了更新、リセット、通知制御を担当する
- 通知配送には `DiscordService` を利用する

制約:
- 項目数が増えると一覧 UI の編集効率が落ちやすい
- 現在はテンプレート機能、カテゴリ機能、履歴機能はない
- 次機能で拡張するなら、データ構造の互換性を維持する migration 前提で考える必要がある

### 5. 潜水艦探索管理

目的:
キャラクターごとの潜水艦探索状態を記録し、帰還確認と Discord 通知に使う。

現状:
- `Submarines` タブでキャラクター単位にタブ表示する
- 各潜水艦の名称、ランク、状態、帰還時刻、残り時間を表示する
- 帰還済みだが未更新の状態も区別して表示する
- `Settings > Submarines` で機能の有効化、Webhook URL、通知有効化、テスト通知を設定できる

現在の仕様:
- `SubmarineDataStorage` が保存と取得を担当する
- `SubmarineService` がゲーム内情報の取得と通知連携を担当する
- `DiscordService` が Webhook 通知を担当する

制約:
- FC ハウスに入室しないとデータが揃わない
- キャラクターまたぎの保存とマージが実装上の重要点
- 直近のローカル変更では保存上書き回避とマージ整理が入っているため、今後の仕様変更時は保存形式の互換性に注意する

### 6. シャキ通知

目的:
コンテンツ突入確認画面の表示を検知し、Windows 側の通知音で気づけるようにする。

現状:
- `Settings > シャキ通知` から有効化と通知時間を設定できる
- 既定は無効で、ユーザーが明示的に有効化した場合のみ鳴る
- 通知時間は 3〜30 秒に制限する
- テスト再生ボタンで設定 OFF のまま音だけ確認できる

現在の仕様:
- `DutyReadyNotificationService` が `IFramework.Update` で確認アドオンの可視状態を監視する
- 候補アドオン名は `GameUiConstants.DutyReadyConfirmAddonNames` に集約する
- 音は確認画面が非表示になった時点、または設定秒数を超えた時点で停止する

制約:
- Windows のミュート、出力デバイス、通知音設定は上書きしない
- 自動受諾や辞退などのゲーム操作は行わない
- パッチ差分でアドオン名が変わった場合は候補名の追加が必要

### 7. 外部マーケット最安値確認

目的:
右クリックしたアイテムについて、Universalis API から現在 DC 内の最安マーケット出品を確認する。

現状:
- アイテム右クリックメニューに `Universalisで最安値確認` を追加する
- 現在ワールドが所属する DC を基準に検索する
- 結果はチャット Echo に最安 1 件だけ表示する
- 表示内容はアイテム名、税抜単価、サーバー、HQ/NQ、数量、確認時刻

現在の仕様:
- `UniversalisMarketService` が `https://universalis.app/api/v2/` を参照する
- `data-centers` は起動中メモリキャッシュする
- 同一 itemId + DC の実行中リクエストは多重実行しない
- 30 秒の簡易メモリキャッシュを持つ
- 最安判定は `pricePerUnit`, `total`, `quantity` の順で昇順にする

制約:
- Universalis はクラウドソースデータのためリアルタイム保証はしない
- 税込み相当表示、履歴、買い物リスト、専用ウィンドウは現時点では持たない
- 通信失敗やタイムアウト時は Warning ログと Echo エラーに留める

## 画面構成

### Home

- マテリア精製の状態表示と有効化
- 分解対象の概要表示
- 分解開始 / 停止
- 分解前確認ダイアログ
- 実行結果メッセージ表示

### Search

- アイテム名検索
- 検索結果一覧
- 検索結果から販売場所検索を実行

### Checklist

- 項目追加
- フィルタ
- Daily / Weekly リセット
- 項目編集テーブル

### Submarines

- キャラクター別タブ
- 潜水艦一覧
- 帰還時刻と残り時間表示

### Settings

- `General & Materia`
- `Desynthesis`
- `Shop Search`
- `Checklist`
- `シャキ通知`
- `Submarines`
- 設定エクスポート / インポート

## 主要サービス構成

- `MateriaExtractService`
  - マテリア精製の実行制御
- `DesynthService`
  - 分解処理の実行制御
- `InventoryService`
  - 所持品参照
- `InventoryCacheService`
  - Home タブ向けの対象集計とプレビュー
- `AddonStateTracker`
  - UI アドオンの状態監視
- `DutyReadyNotificationService`
  - コンテンツ突入確認画面の表示検知と Windows 通知音の制御
- `ShopDataCache`
  - ショップデータ構築と検索用キャッシュ
- `ShopSearchService`
  - 販売場所検索のユースケース本体
- `UniversalisMarketService`
  - Universalis API から現在 DC 内の最安出品を取得
- `ContextMenuService`
  - 右クリック導線の接続
- `MapService`
  - マップピン設定
- `TeleportService`
  - テレポ補助
- `ChecklistService`
  - 日課項目の操作と通知判定
- `SubmarineService`
  - 潜水艦情報の取得と通知
- `SubmarineDataStorage`
  - 潜水艦データの永続化
- `DiscordService`
  - Discord Webhook 通知

## 設定モデルの責務

`Configuration` は単なる設定保存先ではなく、次の責務を持つ。

- 各機能の有効 / 無効
- 分解条件や検索表示条件などの動作パラメータ保持
- Checklist 項目の永続化
- Discord 通知設定の保持
- シャキ通知の有効状態と通知時間の保持
- 設定 export / import
- 互換性維持のための normalize / migrate

今後の機能追加で永続データを増やす場合は、`Configuration.CurrentVersion` と `NormalizeAndMigrate()` を更新する前提で設計する。

## コマンドと外部導線

- `/xivminiutil`
  - メインウィンドウを開閉
- `/xmu`
  - エイリアス
- `/xivminiutil config`
  - Settings タブを開く
- `/xivminiutil diag`
  - ショップ診断レポートを出力
- コンテキストメニュー
  - アイテムから販売場所検索と Universalis 最安値確認を起動

## 現時点の設計上の特徴

- 1つのプラグインに複数ユーティリティ機能を同居させる構成
- 機能ごとに `Services` と `Windows/Components` を分ける構成
- 設定は 1 つの `Configuration` に集約している
- UI は ImGui ベースで、タブごとに責務分離している
- Discord 通知は Checklist と Submarine で共通基盤を使う

## 次機能設計の前提

### 維持したいこと

- タブ単位の責務分離
- 機能単位のサービス分離
- `Configuration` による一元設定管理
- 危険操作前の確認導線
- 公開版で有効化する機能と、内部的に実装済みだが慎重運用する機能の分離

### 次に検討しやすい論点

- Home タブの操作性改善
- 分解対象の可視化強化
- Checklist 項目のテンプレート化
- Shop Search の検索導線整理
- 潜水艦データの表示改善と通知条件追加
- 設定画面のカテゴリ再編

### 注意点

- 公開版ユーザー向けの安定性と、ローカル実装の試行中機能は分けて考える
- Checklist と潜水艦は永続データを持つため、変更時は migration 前提
- Shop Search はデータ構築時間とデータ品質が UX に直結する
- Materia / Desynth は外部からの見え方と誤操作リスクを最優先で設計する

## 参照ファイル

- `README.md`
- `projects/XIV-Mini-Util/Plugin.cs`
- `projects/XIV-Mini-Util/Configuration.cs`
- `projects/XIV-Mini-Util/Windows/MainWindow.cs`
- `projects/XIV-Mini-Util/Windows/Components/HomeTab.cs`
- `projects/XIV-Mini-Util/Windows/Components/SearchTab.cs`
- `projects/XIV-Mini-Util/Windows/Components/ChecklistTab.cs`
- `projects/XIV-Mini-Util/Windows/Components/SubmarineTab.cs`
- `projects/XIV-Mini-Util/Windows/Components/SettingsTab.cs`
- `projects/XIV-Mini-Util/Services/Notification/DutyReadyNotificationService.cs`
- `projects/XIV-Mini-Util/Services/Market/UniversalisMarketService.cs`
