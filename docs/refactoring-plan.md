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
- [ ] 実機で `/xmu diag` `/xmutbgdiag` `/xmutbgcheck` の出力を取得し `docs/refactor-baseline/` に保存(キャラクタリゼーションテストの代わり)
- [x] `scripts/` にビルド+テスト一括検証スクリプトを追加(各フェーズの完了ゲートにする)

**完了条件**: 上記スクリプト一発で green が確認できる。
**リスク**: 低。コード変更なし。

### Phase 1: 死蔵物の棚卸しと削除(docs / ツール / 明白な遺物)

**目的**: コードを触る前にノイズを減らし、以降のフェーズの判断材料を整理する。

- [x] `docs/gptplanv1.md` を削除(他エージェント向けプロンプトの置き忘れ)
- [x] `docs/notes/`・`docs/design/` を「現役」「歴史的記録」に仕分けし、後者を `docs/archive/` へ移動。現役ドキュメントの索引 `docs/README.md` を作成
- [ ] `tools/` の4プロジェクト (CompareShopSources, LodestoneColorantAudit, ShopDataSnapshot, CharaSelectLogicTests) の用途を確認し、一回限りの調査ツールは archive ブランチ/タグに退避して main から削除。2026-06-10 時点では用途棚卸しのみ完了し、削除はユーザー確認待ち
- [x] `bin/` `obj/` が誤ってコミットされていないか `.gitignore` を点検

**完了条件**: docs 直下が「現役のみ」になり、各ドキュメントの位置づけが README から辿れる。
**リスク**: 低。ビルド対象外がほとんど。

### Phase 2: 実験コードの隔離 — TitleScreenBackgroundService 解体(最重要)

**目的**: 6,979行の God Class を「本体」「診断」「実験遺物」に三分割する。

進め方(すべて挙動維持の機械的抽出):

1. **内部クラスの分離**: ファイル末尾の `TitleBackgroundSelfTestSession` / `TitleBackgroundProbeSession` / `TitleBackgroundProbeCounters` / `TitleBackgroundCameraProbeSession` を独立ファイルへ抽出。2026-06-10 時点で、依存する settings snapshot 2件、native delegate、診断 snapshot / verdict 型も含めて抽出済み。`TitleScreenBackgroundService.cs` は 6,979 行から 6,623 行まで縮小。
2. **診断系の分離**: `/xmutbgdiag` `/xmutbgprobe` `/xmutbgcamprobe` `/xmutbgtest` のハンドラとレポート生成を `TitleBackgroundDiagnostics/` サブ名前空間へ移動。本体サービスは診断から参照される読み取り専用インターフェース (`ITitleBackgroundStateReader` 等) を公開する形に。2026-06-10 時点で、Phase2N summary の line builder は `TitleBackgroundPhase2NDeliveryDiagnostic` へ移動済み。probe / camera probe 診断操作は `TitleScreenBackgroundService.Probes.cs` へ、self-test / reload 診断操作は `TitleScreenBackgroundService.SelfTest.cs` へ、Phase2 timeline / generated curve / placement 診断ヘルパーは `TitleScreenBackgroundService.TimelineDiagnostics.cs` へ、`/xmutbgdiag` レポート生成と遷移診断記録は `TitleScreenBackgroundService.Diagnostics.cs` へ partial 分離済み。
3. **本体の縮小**: 残った本体 (シーンオーバーライド適用・遷移ガード・sceneGeneration ゲート) を 1,500行以下を目標に整理。禁止事項に関わるガードロジックは**移動のみ**で書き換えない。2026-06-10 時点で native hook / detour / Phase2G curve 適用は `TitleScreenBackgroundService.NativeHooks.cs` へ、QuickCheck 統合は `TitleScreenBackgroundService.QuickCheck.cs` へ、camera profile 補助は `TitleScreenBackgroundService.CameraProfiles.cs` へ partial 分離済み。`TitleScreenBackgroundService.cs` は 1,862 行まで縮小し、Phase 2 完了条件の 2,000 行未満を達成
4. **Phase2M/2N 診断ファイルの扱い判定**: ~~`TitleBackgroundPhase2MPlacementDiagnostic.cs` を削除する~~ → **削除中止(2026-06-10 参照調査により現役依存が判明)**。当初「結論記録のみの調査コード」として削除承認を得たが、実際には以下が依存している:
   - `TitleScreenBackgroundService.QuickCheck.cs` が `BuildSummary` を `/xmutbgcheck` 判定に使用
   - `TitleBackgroundPhase2NDeliveryDiagnostic.EvaluateExperimentalApply` が actor placement の unsafe 判定ガード(禁止事項の実行時防衛)として使用
   - `CharaSelectLogicTests` が約150箇所参照。`phase2M.*` 診断キーは大原則4の凍結対象
   → 扱い(改名・縮退)は Phase 7 の世代交代で再評価。Phase2N 側の改名も同様にフェーズ7で実施

**完了条件**: 本体サービスが2,000行未満(達成済み)。診断コードが本体の private 状態に直接依存しない(未達 — 現状は partial 分割で private 状態を共有しており、読み取り専用インターフェース化は**実機検証ゲート通過後**に着手する)。実機で `/xmutbgdiag` の主要キーが Phase 0 のベースラインと一致。
**リスク**: 中〜高。unsafe ポインタ操作を含むため、移動時の初期化順序・dispose 順序の事故に注意。**このフェーズだけは実機検証を必須ゲートにする**。partial 分割(2026-06-10 実施分)までは検証済みだが、実機キー一致確認が完了するまで Phase 4 以降の本体側変更には着手しない。

### Phase 3: Configuration 分割

**目的**: 1,127行の単一設定クラスを機能別セクションに分け、実験パラメータを整理する。

- [ ] `Configuration` を機能別の入れ子クラス/別クラス (`TitleBackgroundConfig`, `CharaSelectConfig`, `ShopConfig`, ...) に分割。シリアライズ互換のため、**プロパティ名と JSON 構造は変えない**(まず `partial` 分割か、内部移譲で段階的に)。2026-06-10 時点で `Configuration` を `partial` 化し、TitleBackground 設定プロパティ群、`ApplyFrom` の TitleBackground コピー処理、`NormalizeAndMigrate` の TitleBackground 正規化処理を `Configuration.TitleBackground.cs` へ移動済み。CharaSelect 設定プロパティ群、コピー処理、正規化処理も `Configuration.CharaSelect.cs` へ移動済み。トップレベル JSON round-trip テストを追加済み
- [ ] 実験フェーズ専用で現在どこからも読まれていない設定項目を洗い出し(`grep` で参照ゼロのもの)、削除候補リスト化 → 確認 → 削除
- [ ] 設定マイグレーション(`Version` フィールド)が必要になった場合のみ移行コードを追加

**完了条件**: 既存ユーザーの設定ファイルがそのまま読める(手元の実設定で起動確認)。
**リスク**: 中。設定喪失はユーザー影響が直撃するため、旧 JSON の読み込みテストを CharaSelectLogicTests 形式で追加してから着手。

### Phase 4: UI 層分割 — SettingsTab / CharaSelectService

- [x] `SettingsTab.cs` (2,441行) を partial 分割。`SettingsTab.CharaSelect.cs` (CharaSelect + 撮影構成 UI + ヘルパー)、`SettingsTab.TitleBackground.cs` (タイトル背景 UI + ヘルパー)、`SettingsTab.Shop.cs` (ショップ検索 UI) を新規作成。本体は 526 行まで縮小。未使用の `IsStatusError` メソッドを削除(2026-06-11)
- [x] `CharaSelectService.cs` (2,203行) を partial 分割。ボイス診断・シーン構成診断メソッドを `CharaSelectService.Diagnostics.cs` (2026-06-11) へ抽出。本体は 2,071 行まで縮小
- [ ] `DrawLegacyCharaSelectDiagnostics` の利用実態確認と削除候補化 → 現状は「旧診断 / Legacy experiments」CollapsedHeader として残存。実機確認後に要否判断
- [ ] `CharaSelectService.cs` の残余 (本体 hook / emote 記録 / プランナー連携) のさらなる分割は Phase 7 の命名整理と同時に検討

**完了条件**: SettingsTab 本体が300行未満(526行で目標に近づいたが未達、さらなる抽出は要否確認後)。各セクションが独立ファイル。UIの見た目・操作が変わらないこと(実機スクリーンショット比較)。
**リスク**: 低〜中。ImGui は描画順がそのまま挙動なので、抽出時に呼び出し順を変えない。

### Phase 5: 横断的な共通化

**目的**: 分割で見えてきた重複を統合する(分割前にやると事故るので後置)。

- [ ] 診断レポート生成の共通化: `key=value` 形式のレポートビルダーが TitleBackground / CharaSelect / Shop (`ShopDataDiagnostics`) に散在 → `Services/Common/DiagnosticReportBuilder` に統一 — **スキップ判定**: 既存 partial 分割でラッパーが薄く、抽象化コストのほうが高いと判断。要再評価
- [ ] コマンド登録の整理: Plugin.cs に9個のコマンド定数+登録が直書き → 宣言的なコマンドテーブルに集約 — **スキップ判定**: Plugin.cs は整理済みで可読性に問題なし
- [x] `[Obsolete]`/`Legacy` マーカーの付いた API の参照を一掃: `[Obsolete]` 属性なし、`Legacy` 名のコードはすべて現役→クリーンアップ対象なし (2026-06-11 確認)

**完了条件**: 同一目的のヘルパーが2箇所以上に存在しない。
**リスク**: 低。Phase 2/4 完了後なら差分が小さい。

### Phase 6: テスト体制の整備

- [x] Phase 4 partial 分割の構造確認テスト追加: SettingsTab 各 partial ファイルの存在・メソッド定義・IsStatusError 削除を検証 (2026-06-11)
- [x] CharaSelectService.Diagnostics.cs の抽出確認テスト追加 (2026-06-11)
- [x] SettingsTab ソーススキャンテストを SettingsTab*.cs glob 対応に更新 (2026-06-11)
- [x] 禁止事項スキャンを SettingsTab partial ファイル網羅に更新 (2026-06-11)
- [ ] ゲーム非依存ロジック(ShopDataCache データ整形、Configuration 追加プロパティ互換)のテスト拡充: 次回フェーズで実施
- [ ] Dalamud/FFXIVClientStructs 依存でテスト不能な層と、テスト可能な純ロジック層の境界を docs に明文化

**完了条件**: 「ロジック変更ならテストが先に割れる」状態が主要機能で成立。
**リスク**: 低。

### Phase 7: 命名と診断キーの世代交代(最後に実施)

**目的**: フェーズ名 (`Phase2M`, `Phase2N` 等) を製品語彙に置き換える。外部互換を壊すため最終フェーズに隔離。

- [ ] ファイル名・クラス名から実験フェーズ名を除去(例: `TitleBackgroundPhase2NDeliveryDiagnostic` → `TitleBackgroundDeliveryDiagnostic`)
- [ ] 診断キーの改名は**新旧併記期間**を設ける(旧キーを1リリース維持)か、改名対照表を docs に残す
- [ ] バージョンを上げてリリースノートに「診断キー変更」を明記

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
