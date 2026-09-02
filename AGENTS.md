# AGENTS.md — XIV Mini Util

FFXIV 用 Dalamud プラグイン `XIV Mini Util` の開発リポジトリ。古い仕様駆動ツール前提のワークフローは使わない。

## 1. Project boundary

- 対象: Dalamud API 15 / .NET 10、`Dalamud.NET.Sdk/15.0.0`。
- メインプロジェクト: `projects/XIV-Mini-Util/XivMiniUtil.csproj`（エントリ `Plugin.cs`、設定 `Configuration.cs`、UI `Windows/`、機能 `Services/`、モデル `Models/`）。
- 個人利用のプラグイン。一般公開・大規模運用・SLA を前提にしない。

## 2. Source of truth / precedence

1. 実コード、実テスト、Git 履歴、GitHub 上の実状態
2. この `AGENTS.md`
3. `CLAUDE.md`（Claude Code 向け補足。矛盾時は本書優先）
4. `docs/`（設計・リリース・タスク資料。常時読む必要はない）

## 3. Repository identity guard

- canonical は `zlatan-mt/XIV-Mini-Util`（`https://github.com/zlatan-mt/XIV-Mini-Util.git`）だけ。
- 実装・調査・GitHub 確認の前に次を実行する。

  ```powershell
  git remote get-url origin
  gh repo view --json nameWithOwner
  git rev-parse origin/main
  ```

- `origin` と `nameWithOwner` が canonical と一致し、対象 main SHA を取得できない場合は作業を開始しない。別 repository の履歴・branch・PR・checkout を base や reference にしない。

## 4. Required routing

- **通常タスク**: まず変更対象ファイル・影響範囲・確認方法を短く把握する。既存の C# / Dalamud / ImGui 実装パターン（`GameUiService` + `AddonStateTracker`、`ShopDataCache.InitializeAsync()`、`OnSearchCompleted` 等）に合わせる。
- **ビルド／リリース**: §6 のコマンド。配布に影響する変更は `pluginmaster.json`、`CHANGELOG.md`、`docs/release/custom-plugin-distribution.md` を確認する。
- **Title Background / Character Select を触る場合のみ**: `docs/agent-guides/title-background.md` と `/title-background` skill を先に読む。Sonnet/Codex へ委譲する subagent プロンプトを書く前、実機レポート判読前も同じ。それ以外のタスクではこの契約を読む必要はない。
- **review 判断基準**: 共通の Web Sol review policy は Sol Review Bridge が review prompt へ自動注入するため、このリポジトリの agent がファイル参照する必要はない。XIV 固有の差分だけを §7 に定義する。
- **主な機能領域**: `Services/Materia`（マテリア精製）、`Services/Desynth`（分解）、`Services/Shop`（NPC 販売検索）、`Services/Market`（Universalis 価格）、`Services/Checklist`（日課・週課）、`Services/Submarine`（潜水艦）、`Services/Notification`（Discord / シャキ通知）。

## 5. Core invariants

- ユーザーの未コミット変更を勝手に戻さない。
- ゲーム UI、自動操作、テレポ、通知に関わる変更は副作用を明示して確認する。
- 非同期初期化・イベント通知は既存の流れ（`InitializeAsync()` / `OnSearchCompleted` 等）に合わせる。
- 設定項目を増やす場合は `Configuration.cs`・該当 UI タブ・利用サービスの整合を取る。
- 不要な抽象化や無関係なリファクタ、依存追加をしない。コメントはコードで意図が読めない箇所に限る。
- 実機確認が必要になりそうな項目は、実装と同時に必要情報を自動取得する診断経路を用意する。原則として1回の操作または1回の対象フローで必要情報を収集し、要約レポートをファイル保存してクリップボードへコピーできる形にする。ユーザーに多数の目視確認・診断キー手動検索・同じ設定変更やログ採取の反復を求めない。

## 6. Validation entry points

```bash
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj              # 基本
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Release   # リリース成果物に関わる場合
dotnet run --project tools/CharaSelectLogicTests                    # Title Background / Character Select ロジック
bash scripts/release-build.sh                                       # WSL/Linux 補助
```

- Windows ネイティブで Release パッケージまで検証する場合は `powershell -ExecutionPolicy Bypass -File scripts/release-build.ps1`。
- Debug ビルドは `XivMiniUtil.Dev` として devPlugins に出力。Stable / Testing とも `Dalamud.NET.Sdk/15.0.0` / `DalamudApiLevel=15`。
- WSL / Linux では一部 Dalamud DLL がなく警告が抑制される。ゲーム内挙動はロジックテストとビルドだけでは確認できないため、未確認範囲を必ず明記する。

## 7. Safety / prohibited actions（XIV 固有の review 差分含む）

- 実機安全境界（安全ゲート、run-scoped 判定、pre-login snapshot、非永続 probe、`PersistentApplyEnabled`、world 座標を FixOn／カメラ焦点へ流さない）を自動化を理由に緩めない。詳細は `docs/agent-guides/title-background.md`。
- 配布物（`pluginmaster.json` / Release zip 内 manifest / 追加依存 DLL）の破損を伴う変更は MUST FIX 相当。
- ゲーム UI 自動操作・テレポ・通知の意図しない副作用、ユーザーデータ（設定・保存 view・プリセット）の破損は MUST FIX 相当。
- credential / token を表示・保存・コミットしない。

## 8. Title Background UI・実機確認の恒久契約

この節の詳細は `docs/agent-guides/title-background.md` にある（フィーチャー横断契約なので nested `AGENTS.md` ではなく通常ドキュメント）。Title Background / Character Select（`Services/TitleBackground/`・`Services/CharaSelect/`・`Windows/Components/SettingsTab.TitleBackground.cs` / `.CharaSelect.cs`・`Configuration.TitleBackground.cs` / `.CharaSelect.cs`・`tools/CharaSelectLogicTests/`）を触るなら、そのガイドと `/title-background` skill を先に読む。恒久契約は root の「実機確認は必要最小限」より優先する。要点だけ:

- 通常画面の常時表示量は旧 Developer 表示の 10 分の 1 以下、操作部品最大 4 個、状態行最大 6 行。折りたたみ等の見かけ上の削減は禁止。
- 実機確認フローはユーザー操作を「1クリック → ログアウト → ログイン → 自動コピーされたレポートを貼る」だけに限定する。
- 安全ゲート・run-scoped 判定・pre-login snapshot・非永続 probe・`PersistentApplyEnabled` を緩めない。world 座標を FixOn／カメラ焦点へ流さない。

## 9. 出力ルール

- 返答は「何を変えたか / どう確認したか / 残課題」の順で簡潔に。
- ファイル名・コマンド名・設定名は確認しやすい形で示す。長いログは貼らず要点を要約する。
