# XIV Mini Util 開発ガイド

このリポジトリは FFXIV 用 Dalamud プラグイン `XIV Mini Util` の開発用です。古い仕様駆動ツール前提のワークフローは現在使っていません。

## 基本方針

- 思考は英語、ユーザーへの返答は日本語で簡潔かつ丁寧に行う。
- 既存の C# / Dalamud / ImGui 実装パターンを優先し、不要な抽象化や無関係なリファクタは避ける。
- ユーザーの未コミット変更は勝手に戻さない。
- 変更前に対象ファイル、影響範囲、確認方法を短く確認する。
- コメントは必要最小限にし、コードだけでは意図が読み取りにくい箇所に限る。

## プロジェクト概要

- 対象: Dalamud API 15 / .NET 10
- メインプロジェクト: `projects/XIV-Mini-Util/XivMiniUtil.csproj`
- エントリーポイント: `projects/XIV-Mini-Util/Plugin.cs`
- 設定: `projects/XIV-Mini-Util/Configuration.cs`
- UI: `projects/XIV-Mini-Util/Windows/`
- 機能サービス: `projects/XIV-Mini-Util/Services/`
- データモデル: `projects/XIV-Mini-Util/Models/`

## 主な機能領域

- `Services/Materia/`: マテリア精製支援
- `Services/Desynth/`: 分解支援
- `Services/Shop/`: NPC 販売場所検索
- `Services/Market/`: Universalis API によるマーケット価格確認
- `Services/Checklist/`: 日課・週課チェックリスト
- `Services/Submarine/`: 潜水艦関連データ
- `Services/Notification/`: Discord / シャキ通知

## 開発コマンド

```bash
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Release
bash scripts/release-build.sh
```

- Windows ネイティブで Release パッケージまで検証する場合は
  `powershell -ExecutionPolicy Bypass -File scripts/release-build.ps1` を使う。
- Debug ビルドは `XivMiniUtil.Dev` として devPlugins に出力される。
- Stable / Testing ともに `Dalamud.NET.Sdk/15.0.0` と `DalamudApiLevel=15` を使う。旧 `0.3.0 / API14` を再生成する場合は `v0.3.0` タグなどAPI14設定のソースを使う。
- `DALAMUD_HOME` が必要な環境では Dalamud の Hooks ディレクトリを指定する。
- WSL / Linux では一部 Dalamud DLL がないため警告が抑制されている。実機確認が必要な変更は Windows / Dalamud 環境で確認する。

## 実装時の注意

- `GameUiService` と `AddonStateTracker` を使う既存の UI 操作方針に合わせる。
- ゲーム UI、自動操作、テレポ、通知に関わる変更は副作用を明示的に確認する。
- 非同期初期化やイベント通知は既存の `ShopDataCache.InitializeAsync()` や `OnSearchCompleted` の流れに合わせる。
- 設定項目を増やす場合は `Configuration.cs`、該当 UI タブ、利用サービスの整合を取る。
- 配布に影響する変更では `pluginmaster.json`、`CHANGELOG.md`、`docs/release/custom-plugin-distribution.md` も確認する。

## 検証

- まず変更に最も近い範囲を確認する。
- Title Background / Character Select のロジック確認は
  `dotnet run --project tools/CharaSelectLogicTests` を実行する。
- ビルド確認の基本は `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj`。
- リリース成果物に関わる場合は `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Release` を基本にする。PowerShellでzip化する場合は Release 出力の追加依存DLL有無と zip 内 manifest を確認する。
- ゲーム内挙動はロジックテストとビルドだけでは確認できないため、未確認範囲を明記する。

### 実機確認と診断

- 実機確認が必要な項目が発生しそうな場合は、実装と同時に必要情報を自動取得する診断経路を用意する。
- ユーザーに多数の項目の目視確認、診断キーの手動検索、同じ設定変更やログ採取の繰り返しを求めない。
- 原則として1回の操作または1回の対象フローで必要情報を収集し、要約レポートをファイル保存してクリップボードへコピーできる形にする。
- レポートは原因判定に必要な値、適用可否、失敗理由、取得時コンテキストを含め、詳細なフレームログは別ファイルへ分離して本文を過大にしない。
- pre-loginなど特定コンテキストでしか安全に読めない値はその場でスナップショット化し、後から安全に一括出力する。安全でないコンテキストで再読取しない。
- ユーザー操作が不可欠な場合も、画面に表示される項目名を使い、必要最小限の手順だけを案内する。

## Title Background UI・実機確認の恒久契約

この契約は、上記「実機確認と診断」の曖昧な「必要最小限の手順」より優先する。Title Background（キャラ選択背景）に関わる UI・確認フローは、以下を恒久的に満たすこと。

### 通常画面（ユーザーが普段見る画面）

- 常時表示量は、過去の Developer 表示時の 10 分の 1 以下にする。
- 操作部品は最大 4 個、定常時の説明・状態行は最大 6 行。
- `CollapsingHeader` / `TreeNode` / 表示モード切替 / 閉じたタブで隠すだけ、の「見かけ上の削減」は禁止。実際に量を削る。
- 通常利用に不要な診断・生設定・signature・resolver・legacy 比較・manual candidate・probe 操作・anchor 座標・nudge・layer 探索・FixOn toggle・camera profile compare・delivery mode・preset raw・Phase 番号・英語の内部診断文言は、通常画面から削除する。
- 通常画面の描画から Developer 系描画メソッドを呼ばない。

### 残す開発機能

- 残す必要がある開発機能は、責務ごとに別ページ／別ファイルへ物理的に分割する。通常画面からは呼び出さない。
- 単に折りたたみや別タブへ移すだけにせず、ファイルと責務を実際に分ける。
- バックエンドロジックは現役参照を確認せずに削除しない。UI は重複・自動レポートで代替済みのものを削除する。

### 実機確認フロー（1クリック契約）

- ユーザーに許される操作は「1クリック → ログアウト → ログイン → 自動コピーされたレポートを貼る」だけ。これ以外を要求しない。
- candidate 設定、probe 取得、有効化、古い run の整理、hook 再初期化、QuickCheck 開始、sample 追加、レポート統合、clipboard コピーは、すべてコード側が自動処理する。
- candidate 設定は必ず probe 取得より先に行い、candidate-mismatch を構造的に防ぐ。
- hook が未準備なら、コード側で安全な再初期化を 1 回行って再評価する。それでも不可なら、logout を要求せず run を失敗完了し、原因・hook 状態・candidate・territory・再初期化結果を含む失敗レポートを自動コピーする。手動の OFF→候補選択・probe クリア・ゲーム再起動を先に案内しない。
- 成功・失敗どちらも、追加操作を要求せず最終レポートを自動コピーする。
- 複数地点の測定が必要でも、各地点で同じ 1 クリックフローだけを使う。sample はセッション中に自動蓄積し、各 run の最終レポートに集約結果を含める。
- 新機能が追加操作を必要とする場合、ユーザーへ手順を増やす前に自動化経路を実装する。

### 禁止する案内（ユーザーへ要求してはいけない操作）

開発者向け設定の表示 / 候補の選び直し / probe の保存・ON・クリア / 診断を別ボタンでコピー / 診断キー探し / 設定リセット / コマンド入力 / 同じ run のための複数ボタン押下 / 多数のスクリーンショット・目視項目 / hookNotReady 時の手動再起動・再設定を先に要求すること。

### 安全境界（自動化しても緩めない）

- 安全ゲート、run-scoped 判定、pre-login snapshot、非永続 probe、`PersistentApplyEnabled` は緩めない。
- world 座標を FixOn／カメラ焦点へ流さない。実機確認前に ground-verified へ昇格させない。

### テストで固定する

- 通常画面の操作部品数・許可ラベル・Developer 描画の不呼び出し・折りたたみ/表示 toggle の不在・1クリック契約（主ボタンが単一サービスメソッドのみ呼ぶ・推奨設定が probe 取得より先・mismatch 時に開始しない・hookNotReady 自動再初期化・失敗時自動コピー・統合レポート・旧 run 非混入・安全境界）を自動テストで固定する。

## 出力ルール

- 返答は「何を変えたか / どう確認したか / 残課題」の順で簡潔にまとめる。
- ファイル名、コマンド名、設定名は確認しやすい形で示す。
- 長いログは貼らず、必要な要点だけを要約する。
