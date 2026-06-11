# XIV Mini Util 段階的リファクタリング計画

作成日: 2026-06-10
対象: `projects/XIV-Mini-Util` 全体 (約33,000行 / 119ファイル) + `tools/` + `docs/`

## 1. 現状分析(なぜ無駄が溜まったか)

| 領域 | 規模 | 問題 |
|---|---|---|
| TitleBackground + CharaSelect | 約17,500行 (全体の53%) | Phase2E〜2N の実験を積層したまま本体化。フェーズ名がファイル名・診断キーに残存 |
| `TitleScreenBackgroundService.cs` | 6,979行 / メンバー485 | 本体ロジック・診断・プローブ・セルフテスト・内部クラス5個が同居する God Class |
| `SettingsTab.cs` | 2,441行 | 全機能の設定UIが1クラス。`DrawLegacyCharaSelectDiagnostics` 等のレガシーUIも残存 |
| `CharaSelectService.cs` | 2,203行 | 同上の傾向 |
| `Configuration.cs` | 1,127行 / public 134 | TitleBackground 関連参照だけで293箇所。実験パラメータの墓場化 |
| `docs/notes`, `docs/design` | 計23ファイル | 歴代の実験計画・指示書・結果メモが現役ドキュメントと混在。`docs/gptplanv1.md` は他エージェント向けタスクプロンプトの置き忘れ |
| テスト | `tools/CharaSelectLogicTests` のみ | 自作 console runner。Shop/Desynth/Materia/Submarine はテストゼロ |

核心的な問題は「実験コード(診断・プローブ・フェーズ別検証)」と「製品コード」の境界がないこと。リファクタリングの主軸はこの分離。

## 2. 大原則(全フェーズ共通)

1. **挙動を変えない**。各フェーズは純粋な構造変更のみ。機能変更・改善は別PRに分離する。
2. **1フェーズ = 1〜数PR**。ビッグバン書き換えは禁止。各PRで `dotnet build`(Debug/Release)+ `CharaSelectLogicTests` が通ること。
3. **TitleBackground の禁止事項を絶対に侵さない**(`docs/title-background-character-select-delivery-notes.md` 参照):
   - SceneCamera 直接書き込み、per-frame カメラ補正、actor transform デフォルト書き込み、post-login ObjectTable のプレビューソース利用は移動中も復活させない
   - `custom:n4f4` を「実プリセット」と表現する文言を生まない
4. **診断出力の互換性を維持**。`/xmutbgdiag` 等の診断キー (`phase2N.mvpStatus` 等) は外部記録との突き合わせに使われているため、削除・改名はフェーズ7まで凍結。
5. 各フェーズの冒頭で「削除候補リスト」を作り、削除前にユーザー確認を取る(実機でしか検証できないコードがあるため)。

## 3. フェーズ計画

### Phase 0: セーフティネット構築(リファクタ前の足場)

**目的**: 「壊していないこと」を機械的に確認できる状態を作る。

- [x] `CharaSelectLogicTests` を現状のまま通ることを確認し、ベースライン記録
- [x] Release ビルド (`-p:DevPluginOutputDir=`) の出力ファイル一覧をスナップショット化(zip 内容の差分検知用)
- [x] 実機で `/xmu diag` `/xmutbgdiag` `/xmutbgcheck` の出力を取得し `docs/refactor-baseline/` に保存(キャラクタリゼーションテストの代わり)— 2026-06-11 取得 (v0.3.8)。旧 phase2M/2N キーと新 alias の併記、post-login leak なしを確認
- [x] `scripts/` にビルド+テスト一括検証スクリプトを追加(各フェーズの完了ゲートにする)

**完了条件**: 上記スクリプト一発で green が確認できる。
**リスク**: 低。コード変更なし。

### Phase 1: 死蔵物の棚卸しと削除(docs / ツール / 明白な遺物)

**目的**: コードを触る前にノイズを減らし、以降のフェーズの判断材料を整理する。

- [x] `docs/gptplanv1.md` を削除(他エージェント向けプロンプトの置き忘れ)
- [x] `docs/notes/`・`docs/design/` を「現役」「歴史的記録」に仕分けし、後者を `docs/archive/` へ移動。現役ドキュメントの索引 `docs/README.md` を作成
- [x] `tools/` の4プロジェクト (CompareShopSources, LodestoneColorantAudit, ShopDataSnapshot, CharaSelectLogicTests) の用途を確認し、一回限りの調査ツールを main から削除。現役ゲートの `CharaSelectLogicTests` のみ保持。履歴資料は `docs/archive/shop/` に保持 (2026-06-11)
- [x] `bin/` `obj/` が誤ってコミットされていないか `.gitignore` を点検

**完了条件**: docs 直下が「現役のみ」になり、各ドキュメントの位置づけが README から辿れる。
**リスク**: 低。ビルド対象外がほとんど。

### Phase 2: 実験コードの隔離 — TitleScreenBackgroundService 解体(最重要)

**目的**: 6,979行の God Class を「本体」「診断」「実験遺物」に三分割する。

進め方(すべて挙動維持の機械的抽出):

1. **内部クラスの分離**: ファイル末尾の `TitleBackgroundSelfTestSession` / `TitleBackgroundProbeSession` / `TitleBackgroundProbeCounters` / `TitleBackgroundCameraProbeSession` を独立ファイルへ抽出。2026-06-10 時点で、依存する settings snapshot 2件、native delegate、診断 snapshot / verdict 型も含めて抽出済み。`TitleScreenBackgroundService.cs` は 6,979 行から 6,623 行まで縮小。
2. **診断系の分離**: `/xmutbgdiag` `/xmutbgprobe` `/xmutbgcamprobe` `/xmutbgtest` のハンドラとレポート生成を partial へ分離。本体サービスは診断から参照される読み取り専用インターフェース (`ITitleBackgroundStateReader` 等) を公開する形に。2026-06-10 時点で、delivery summary の line builder は `TitleBackgroundDeliveryDiagnostic` へ移動済み。probe / camera probe 診断操作は `TitleScreenBackgroundService.Probes.cs` へ、self-test / reload 診断操作は `TitleScreenBackgroundService.SelfTest.cs` へ、timeline / generated curve / character placement 診断ヘルパーは `TitleScreenBackgroundService.TimelineDiagnostics.cs` へ、`/xmutbgdiag` レポート生成と遷移診断記録は `TitleScreenBackgroundService.Diagnostics.cs` へ partial 分離済み。
3. **本体の縮小**: 残った本体 (シーンオーバーライド適用・遷移ガード・sceneGeneration ゲート) を 1,500行以下を目標に整理。禁止事項に関わるガードロジックは**移動のみ**で書き換えない。2026-06-10 時点で native hook / detour / Phase2G curve 適用は `TitleScreenBackgroundService.NativeHooks.cs` へ、QuickCheck 統合は `TitleScreenBackgroundService.QuickCheck.cs` へ、camera profile 補助は `TitleScreenBackgroundService.CameraProfiles.cs` へ partial 分離済み。`TitleScreenBackgroundService.cs` は 1,862 行まで縮小し、Phase 2 完了条件の 2,000 行未満を達成
4. **CharacterPlacement / Delivery 診断ファイルの扱い判定**: ~~`TitleBackgroundPhase2MPlacementDiagnostic.cs` を削除する~~ → **削除中止(2026-06-10 参照調査により現役依存が判明)**。当初「結論記録のみの調査コード」として削除承認を得たが、実際には以下が依存している:
   - `TitleScreenBackgroundService.QuickCheck.cs` が `BuildSummary` を `/xmutbgcheck` 判定に使用
   - `TitleBackgroundDeliveryDiagnostic.EvaluateExperimentalApply` が actor placement の unsafe 判定ガード(禁止事項の実行時防衛)として使用
   - `CharaSelectLogicTests` が約150箇所参照。`phase2M.*` 診断キーは大原則4の凍結対象
   → Phase 7 で `TitleBackgroundCharacterPlacementDiagnostic` / `TitleBackgroundDeliveryDiagnostic` へ改名済み。旧診断キーは互換 alias として維持

**完了条件**: 本体サービスが2,000行未満(達成済み)。診断コードが本体の private 状態に直接依存しない(未達 — 現状は partial 分割で private 状態を共有しており、読み取り専用インターフェース化は**実機検証ゲート通過後**に着手する)。実機で `/xmutbgdiag` の主要キーが Phase 0 のベースラインと一致。
**リスク**: 中〜高。unsafe ポインタ操作を含むため、移動時の初期化順序・dispose 順序の事故に注意。**このフェーズだけは実機検証を必須ゲートにする**。partial 分割(2026-06-10 実施分)までは検証済みだが、実機キー一致確認が完了するまで Phase 4 以降の本体側変更には着手しない。

### Phase 3: Configuration 分割

**目的**: 1,127行の単一設定クラスを機能別セクションに分け、実験パラメータを整理する。

- [x] `Configuration` を機能別の partial に分割。シリアライズ互換のため、**プロパティ名と JSON 構造は変えない**。2026-06-11 時点で `Configuration` を `partial` 化し、TitleBackground は `Configuration.TitleBackground.cs`、CharaSelect は `Configuration.CharaSelect.cs`、Checklist は `Configuration.Checklist.cs`、Materia / Desynth / Shop / Submarine / Notification は `Configuration.CoreFeatures.cs` へ、各設定プロパティ、`ApplyFrom` のコピー処理、`NormalizeAndMigrate` の正規化処理を移動済み。トップレベル JSON round-trip テストを追加済み
- [x] 実験フェーズ専用で現在どこからも読まれていない設定項目を洗い出し(`grep` で参照ゼロのもの)、削除候補リスト化 → 削除。`CharaSelectSceneStageStrategyExperimentalEnabled` / `CharaSelectSceneStageStrategyOneShotProbeEnabled` を削除し、旧 JSON の未知プロパティ無視を `CharaSelectLogicTests` で確認 (2026-06-11)
- [x] 設定マイグレーション(`Version` フィールド)は不要と判定。削除したゼロ参照プロパティは旧 JSON の未知プロパティ無視テストで確認済み

**完了条件**: 既存ユーザーの設定ファイルがそのまま読める(手元の実設定で起動確認)。
**リスク**: 中。設定喪失はユーザー影響が直撃するため、旧 JSON の読み込みテストを CharaSelectLogicTests 形式で追加してから着手。

### Phase 4: UI 層分割 — SettingsTab / CharaSelectService

- [x] `SettingsTab.cs` (2,441行) を partial 分割。`SettingsTab.CharaSelect.cs` (CharaSelect + 撮影構成 UI + ヘルパー)、`SettingsTab.TitleBackground.cs` (タイトル背景 UI + ヘルパー)、`SettingsTab.Shop.cs` (ショップ検索 UI)、`SettingsTab.CoreFeatures.cs` (General / Desynth / Submarine / DutyReady / Checklist UI + ヘルパー) を作成。本体は 260 行まで縮小し、300行未満を達成。未使用の `IsStatusError` メソッドを削除(2026-06-11)
- [x] `CharaSelectService.cs` (2,203行) を partial 分割。ボイス診断・シーン構成診断メソッドを `CharaSelectService.Diagnostics.cs` (144行) へ、scene composition / TitleBackground bridge 操作を `CharaSelectService.SceneComposition.cs` (258行) へ、native hook / detour / hook dispose を `CharaSelectService.NativeHooks.cs` (430行) へ、emote 記録・再生を `CharaSelectService.Emotes.cs` (332行) へ、territory prefetch / level 解決を `CharaSelectService.Prefetch.cs` (223行) へ抽出。本体は 878 行まで縮小 (2026-06-11)
- [x] `DrawLegacyCharaSelectDiagnostics` の利用実態を確認し、Settings UI から削除。構造テストで旧 UI 名が残っていないことを確認 (2026-06-11)
- [x] `CharaSelectService.cs` の残余 (本体 hook / emote 記録 / プランナー連携) のさらなる分割。hook / emote / prefetch は抽出済み。命名整理は Phase 7 で別途実施

**完了条件**: SettingsTab 本体が300行未満。各セクションが独立ファイル。UIの見た目・操作が変わらないこと(実機スクリーンショット比較)。
**リスク**: 低〜中。ImGui は描画順がそのまま挙動なので、抽出時に呼び出し順を変えない。

### Phase 5: 横断的な共通化

**目的**: 分割で見えてきた重複を統合する(分割前にやると事故るので後置)。

- [x] 診断レポート生成の共通化: `Services/Common/DiagnosticReportBuilder` を追加し、旧 `phase2M.*` / `phase2N.*` から新 prefix alias を生成する横断処理を統一。ドメイン固有の大量 `key=value` 生成は可読性維持のため各診断クラスに残す (2026-06-11)
- [x] コマンド登録の整理: Plugin.cs の command name / handler / help message を `CommandRegistration` テーブルに集約し、登録と解除を同じ定義から実行。構造確認テストも追加 (2026-06-11)
- [x] `[Obsolete]`/`Legacy` マーカーの付いた API の参照を一掃: `[Obsolete]` 属性なし、`Legacy` 名のコードはすべて現役→クリーンアップ対象なし (2026-06-11 確認)

**完了条件**: 同一目的のヘルパーが2箇所以上に存在しない。
**リスク**: 低。Phase 2/4 完了後なら差分が小さい。

### Phase 6: テスト体制の整備

- [x] Phase 4 partial 分割の構造確認テスト追加: SettingsTab 各 partial ファイルの存在・メソッド定義・IsStatusError 削除を検証 (2026-06-11)
- [x] CharaSelectService.Diagnostics.cs の抽出確認テスト追加 (2026-06-11)
- [x] CharaSelectService.NativeHooks.cs / Emotes.cs / Prefetch.cs の抽出確認テスト追加 (2026-06-11)
- [x] SettingsTab ソーススキャンテストを SettingsTab*.cs glob 対応に更新 (2026-06-11)
- [x] 禁止事項スキャンを SettingsTab partial ファイル網羅に更新 (2026-06-11)
- [x] ゲーム非依存ロジックのテスト拡充: Configuration `ApplyFrom` のトップレベル互換・正規化、Shop の item id 正規化、ショップ名 fallback、NPC location validation を `CharaSelectLogicTests` に追加 (2026-06-11)
- [x] Dalamud/FFXIVClientStructs 依存でテスト不能な層と、テスト可能な純ロジック層の境界を `docs/refactor-baseline/test-boundaries.md` に明文化 (2026-06-11)

**完了条件**: 「ロジック変更ならテストが先に割れる」状態が主要機能で成立。
**リスク**: 低。

### Phase 7: 命名と診断キーの世代交代(最後に実施)

**目的**: フェーズ名 (`Phase2M`, `Phase2N` 等) を製品語彙に置き換える。外部互換を壊すため最終フェーズに隔離。

- [x] ファイル名・クラス名から実験フェーズ名を除去。`TitleBackgroundPhase2MPlacementDiagnostic` は `TitleBackgroundCharacterPlacementDiagnostic`、`TitleBackgroundPhase2NDeliveryDiagnostic` は `TitleBackgroundDeliveryDiagnostic` へ改名。保存設定の旧 JSON キー `TitleBackgroundPhase2MExperimentalApplyMode` は互換維持 (2026-06-11)
- [x] 診断キーは旧 `phase2M.*` / `phase2N.*` を1リリース維持し、新 `characterPlacement.*` / `delivery.*` alias を併記。互換性を `CharaSelectLogicTests` で確認 (2026-06-11)
- [x] バージョンを `0.3.8` に上げ、`CHANGELOG.md` / `pluginmaster.json` に診断キー alias とリファクタリング内容を明記 (2026-06-11)

**完了条件**: コードベースから歴史的フェーズ名が消え、現在の機能名だけで読める。
**リスク**: 中。外部記録(過去の診断ログ)との突き合わせ手順を docs に残すこと。

## 4. 実施順序とゲート

```
Phase 0 ─ Phase 1 ─ Phase 2 ─ Phase 3 ─ Phase 4 ─ Phase 5 ─ Phase 6 ─ Phase 7
 (足場)   (棚卸し)  (最重要)   (設定)     (UI)      (共通化)   (テスト)   (改名)
                      ↑実機検証必須      ↑実設定読込必須              ↑リリース告知必須
```

- 各フェーズ完了時に Phase 0 の検証スクリプト + 必要に応じ実機確認
- Phase 2 と 3 は依存がないため並行可能だが、レビュー負荷を考え直列推奨
- 途中で機能追加の要望が来たら、進行中フェーズを完了させてから本体に取り込む(リファクタブランチを長生きさせない)

## 5. やらないこと

- 機能の追加・変更・「ついでの改善」
- 依存ライブラリの追加(DI コンテナ導入なども不要。現状の手動 DI は規模に対して適正)
- TitleBackground の禁止事項リストに触れる変更(カメラ・actor 配置の再挑戦は本計画の対象外)
- テストフレームワーク(xUnit 等)への移行(Dalamud 依存の制約上、現 runner 方式が実利的)
