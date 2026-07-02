# XIV Mini Util 現在実装・引き継ぎ資料

更新日時: 2026-07-02 13:39 +09:00  
対象作業ツリー: `C:\Project\apps\XIV-Mini-Util`  
対象ブランチ: `main`  
現在HEAD: `5d0fd53` (`Fix run-scoped Title Background diagnostics`)  
Git状態: `origin/main` より ahead 7、未コミット差分あり  
用途: 別AIが過去の完了報告を信用せず、現在のコードと実機結果から安全に作業を継続するための引き継ぎ

## 1. この資料の読み方

この資料は、2026-07-02時点の実ファイル、`git status`、`git diff`、統合検証結果、ユーザーが提供した実機レポートを再確認して作成した。

別AIは、作業を始める前にこの資料だけで判断せず、必ず次を再確認すること。

1. ルートの `AGENTS.md`
2. `git status --short --branch`
3. `git diff`
4. `README.md`
5. `docs/refactoring-plan.md`
6. `docs/refactor-baseline/`
7. `projects/XIV-Mini-Util/XivMiniUtil.csproj`
8. `tools/CharaSelectLogicTests`
9. `scripts/release-build.ps1`
10. `scripts/verify-refactor-phase.ps1`

優先順位は「ユーザーの最新指示 → 現在のコードと設定 → この資料 → 過去資料」とする。

## 2. 絶対に守る作業境界

- commit、push、branch操作は、ユーザーが明示しない限り行わない。
- ユーザーの未コミット変更を戻さない。
- `git reset --hard`、`git checkout --`、一括的なファイル復元を行わない。
- dirtyな作業ツリーを「整理」の名目で上書きしない。
- 新規依存、DIコンテナ、xUnit等のテストフレームワークを追加しない。
- unsafe hook、pointer、detourの意味や順序を、実機確認なしに変更しない。
- Configurationのpublicプロパティ名、JSONキー、既定値、enum値を変更しない。
- `[JsonProperty]` / `[JsonPropertyName]` を削除しない。
- コマンド名、診断キー、manifest、配布URL、バージョンを「ついで」に変更しない。
- ImGuiの描画順とIDを不要に変更しない。
- ルートのzipや配布物を生成・置換しない。Release成果物は `bin/Release` 配下だけを使う。
- `docs/archive` はリンク切れ修正以外で変更しない。
- dead codeと推測しただけで削除しない。削除前に通常参照、reflection、serialization、Dalamud command、ImGui、診断、テスト、旧JSON互換を確認する。

## 3. 現在のリポジトリ状態

### 3.1 Git

```text
branch: main
HEAD: 5d0fd53
upstream: origin/main
ahead: 7
behind: 0
working tree: dirty
```

HEADから見た直近7件は次のとおり。

```text
5d0fd53 Fix run-scoped Title Background diagnostics
4966042 Merge remote-tracking branch 'origin/main'
c517221 Add CharaSelect anchor and FixOn diagnostics
1a3fcb7 Fix pluginmaster to published v0.3.7
26c4cb6 Merge: CharaSelect compositing cleanup + report wording
b11a0e9 Clean up CharaSelect camera-aim dead code; report recognizes composited character
0b2e99f Merge: Character Select n4f4 background + character display
```

### 3.2 現在の規模

計測方法は `rg --files` とPowerShellの `Get-Content(...).Count`。以下はこの引き継ぎ文書を追加する直前の値。

| 領域 | 現在値 |
|---|---:|
| リポジトリ内ファイル | 226 |
| C# | 170ファイル |
| TitleBackground | 53ファイル / 18,365行 |
| Shop | 29ファイル / 5,277行 |
| CharaSelect | 17ファイル / 3,642行 |
| `TitleScreenBackgroundService` partial群 | 9ファイル / 8,836行 |
| `tools/CharaSelectLogicTests/Program.cs` | 5行 |
| LogicTests | 439件 |

`docs/refactoring-plan.md` の「約206ファイル / C# 166ファイル / TitleBackground 18,277行」は、2026-07-01時点の値であり、すでに古い。今後は再計測すること。

### 3.3 `TitleScreenBackgroundService` の残存状態

`TitleScreenBackgroundService.cs` のコンストラクタ前を行ベースで数えると、private field宣言は191、うちreadonly/const/static readonlyを除く可変field宣言は153。

automatic check関連の12状態は `TitleBackgroundAutomaticCheckRuntimeState` に移動済みだが、camera、probe、placement、timeline、hook lifecycle状態は依然として巨大service本体に多く残る。

partial分割だけで責務分離完了とは扱わないこと。

## 4. 未コミット差分の分類

### 4.1 変更済みファイル

```text
.gitignore
README.md
docs/refactoring-plan.md
projects/XIV-Mini-Util/Configuration.TitleBackground.cs
projects/XIV-Mini-Util/Plugin.cs
projects/XIV-Mini-Util/Services/Shop/ColorantItemResolver.cs
projects/XIV-Mini-Util/Services/Shop/ContextMenuService.cs
projects/XIV-Mini-Util/Services/Shop/ShopDataCache.cs
projects/XIV-Mini-Util/Services/Submarine/SubmarineService.cs
projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundAddressResolver.cs
projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundAutomaticCheckRecovery.cs
projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectAnchor.cs
projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundQuickCheck.cs
projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.NativeHooks.cs
projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.QuickCheck.cs
projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.TimelineDiagnostics.cs
projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.cs
projects/XIV-Mini-Util/Windows/Components/SettingsTab.CharaSelect.cs
projects/XIV-Mini-Util/Windows/Components/SettingsTab.TitleBackground.cs
projects/XIV-Mini-Util/Windows/Components/SettingsTab.cs
scripts/verify-refactor-phase.ps1
tools/CharaSelectLogicTests/Program.cs
```

### 4.2 新規・未追跡ファイル

```text
AGENTS.md
docs/refactor-baseline/2026-07-01-r0-current-state.md
projects/XIV-Mini-Util/Plugin.CommandHandlers.cs
projects/XIV-Mini-Util/Plugin.Commands.cs
projects/XIV-Mini-Util/Plugin.Lifecycle.cs
projects/XIV-Mini-Util/Plugin.ServiceConstruction.cs
projects/XIV-Mini-Util/Plugin.UiEvents.cs
projects/XIV-Mini-Util/Services/Shop/ColorantItemTextParser.cs
projects/XIV-Mini-Util/Services/Shop/ShopCacheBuildCoordinator.cs
projects/XIV-Mini-Util/Services/Shop/ShopItemIdentity.cs
projects/XIV-Mini-Util/Services/Submarine/SubmarineNotificationDispatcher.cs
projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundAutomaticCheckRuntimeState.cs
projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundKnownSignatures.cs
projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundWorldCoordinateCorrespondence.cs
projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.OneClickVerification.cs
projects/XIV-Mini-Util/Windows/Components/SettingsTab.TitleBackgroundDiagnostics.cs
scripts/release-build.ps1
tools/CharaSelectLogicTests/TestHelpers.cs
tools/CharaSelectLogicTests/TestRunner.cs
tools/CharaSelectLogicTests/Tests/
```

この引き継ぎ文書自身も新規未追跡ファイルになる。

### 4.3 差分量

tracked差分だけで22ファイル、約1,079行追加、約9,656行削除。大半の削除は `Program.cs`、`Plugin.cs`、Settings UIの責務別ファイルへの移動と、通常UIからの開発者向け表示除去による。

既存差分を「大量削除」とだけ見て戻してはいけない。

## 5. リファクタリング実装状況

## 5.1 R0: 現在状態の固定

実装済み。

- LogicTests、Debug、Windows Release、成果物、`git diff --check` の基準を確立。
- `docs/refactor-baseline/2026-07-01-r0-current-state.md` に開始状態を保存。
- 未コミット差分をTitle Background、UI、Configuration、Tests、Build、Repository guidanceへ分類。
- リファクタリング開始前の既存失敗はなし。

## 5.2 R1: LogicTestsランナー分割

実装済み。

### 構成

```text
tools/CharaSelectLogicTests/
  Program.cs                         5行
  TestRunner.cs
  TestHelpers.cs
  Tests/
    ConfigurationTests.cs           14件
    CharaSelectTests.cs              40件
    TitleBackgroundQuickCheckTests.cs 45件
    TitleBackgroundSafetyTests.cs    313件
    TitleBackgroundUiContractTests.cs 19件
    ShopTests.cs                     8件
```

合計439件。

### 現在の性質

- 新しいテストフレームワークや依存は追加していない。
- `Program.cs` は `TestRunner.Run()` を呼ぶだけ。
- テスト名、order、失敗出力を自作runnerで維持。
- Configuration round-trip、Shopの純粋変換、TitleBackgroundの純粋判定は実オブジェクトを使うテストがある。
- unsafe hook順序、診断キー、UI操作数、ファイル配置、安全境界はソース文字列検査を残している。
- 依然としてTitleBackground safetyテストの多くがソース文字列検査。今後の改善は、純粋判定を抽出できる箇所だけ段階的に行う。

### 軽微な文書上の不整合

`TestRunner.cs` のリスト容量は439で現在件数と一致するが、ファイル先頭コメントは「434 test names」と古い。動作影響なし。次回文書整理時に修正候補。

## 5.3 R2: `Plugin` ライフサイクル分割

実装済み。

### 現在のファイル

| ファイル | 行数 | 責務 |
|---|---:|---|
| `Plugin.cs` | 12 | エントリーポイントと名前 |
| `Plugin.ServiceConstruction.cs` | 219 | service/UI構築、イベント登録、非同期Shop初期化 |
| `Plugin.Commands.cs` | 85 | コマンド定義、登録、解除 |
| `Plugin.CommandHandlers.cs` | 227 | コマンドハンドラ |
| `Plugin.UiEvents.cs` | 59 | UIイベント、clipboard引き渡し |
| `Plugin.Lifecycle.cs` | 35 | Dispose |

### 保持している順序

- service生成順を維持。
- UI event登録順を維持。
- Dispose冒頭でコマンド解除とUIイベント解除。
- `TitleScreenBackgroundService.SelfTestCompleted` はservice破棄前に解除。
- 依存serviceを既存順で破棄。
- `ShopSearchService.OnSearchCompleted` を解除してから `ShopDataCache.Dispose()`。

### 改善

- コマンド13個を `CommandRegistration` の単一定義から登録・解除。
- `_ = InitializeShopDataAsync()` の例外を `InitializeShopDataAsync()` 内で捕捉し、`IPluginLog.Error` へ記録。
- clipboard処理をUI frameの責務へ分離。
- DIコンテナは追加していない。

## 5.4 R3: Title Background state holder

一部実装済み。R3全体は未完了。

### 実装済み

`TitleBackgroundAutomaticCheckRuntimeState` に次の12状態を集約。

- State
- Requested
- CompletionDueAt
- LoginObservedAt
- Status
- LastReport
- PendingClipboardText
- ReportAvailabilityInitialized
- ReportAvailable
- SettingsSnapshot
- RunId
- SettingsRestored

`TitleScreenBackgroundService.OneClickVerification.cs` と `TitleScreenBackgroundService.QuickCheck.cs` が同じholderを利用。

### 未実施

- world probe / coordinate sample state holder
- character placement state holder
- camera observation state holder
- timeline / probe diagnostic state holder
- hook lifecycle state holder
- report builderのservice private stateからの完全分離

native pointer、detour、framework update処理の大規模抽出は行っていない。

## 5.5 R4: Shop

実装済み。

### `ColorantItemTextParser`

`ColorantItemResolver` から次を純粋ロジックとして分離。

- Addon表示文字列のsuffix除去
- item label正規化
- 先頭marker除去
- 無視すべきUI文言判定
- `IH` / `HQ` tag除去

unsafe Addon探索とAtkValue読取は `ColorantItemResolver` に残す。

### `ShopItemIdentity`

`ContextMenuService` から次を分離。

- variant/HQ offsetを考慮したitem ID正規化
- Universalis用Normal/HQ判定

### `ShopCacheBuildCoordinator`

`ShopDataCache` から次を分離。

- generation採番
- current build Taskの再利用
- rebuild時の前run cancel
- `CancellationTokenSource` lifecycle
- current generation判定
- Dispose時cancel/dispose

`ShopDataCache.Dispose()` からcoordinatorを破棄する。

### `AtkValueType.String8` レビュー結果

外部レビューで `String8` が欠落する可能性を指摘されたため、現在の `ExtractString()` は `String`、`ManagedString`、`String8`、`WideString` を処理する。

2026-07-02に実際の `C:\Users\MonaT\AppData\Roaming\XIVLauncher\addon\Hooks\dev\FFXIVClientStructs.dll` をreflectionで確認した結果:

```text
String=8
ManagedString=40
String8=10
ConstString=10
WideString=9
assembly version=7.51.0.8455
```

現在ロードされるDLLでは `String8` と `ConstString` は同じenum値10のalias。switchに両方を書くと重複caseになるため、現在はobsolete alias側の `String8` caseを残している。

依存DLLが変わった場合は、名前だけで判断せず実DLLのenum値を再確認すること。

## 5.6 R5: 小規模機能

Materia、Desynth、Checklist、Submarine、Notification、Market、Commonを確認済み。

実変更はSubmarine通知のみ。

`SubmarineNotificationDispatcher.SendSafeAsync()` を追加し、Discord通知失敗をログへ記録しつつ、潜水艦追跡本体へ例外を伝播させない。

小クラスの形式的分割、UI改善、機能変更は混ぜていない。

## 5.7 R6: 開発経路・文書

実装済み。

- `scripts/verify-refactor-phase.ps1` を統合ゲート化。
- `scripts/release-build.ps1` をWindows Release package経路として追加。
- Release staging削除前に、対象が `bin/Release` 配下であることを絶対パスで検証。
- read-only属性を解除してからstagingだけを削除。
- `latest.zip` と `XivMiniUtil.json` の存在まで確認。
- READMEの現在の画面名を反映。
- Stableとsourceのバージョン差を維持。

## 6. Title Backgroundの現在実装

## 6.1 通常UI

ファイル: `projects/XIV-Mini-Util/Windows/Components/SettingsTab.TitleBackground.cs`

通常画面は最大4操作を維持。

1. `OFF`
2. `イル・メグ`
3. `この場所で確認を開始`
4. 通常時は `初期状態に戻す`、pre-loginのキャラ選択画面では `現在の構図を保存`

4番目は同じ `##TitleBackgroundReset` IDと1つの `ImGui.Button` 呼び出しを使い、操作数を増やしていない。

通常画面には次を出さない。

- Developer
- 生設定
- signature / resolver
- manual candidate
- probe手動操作
- anchor座標/nudge
- layer探索
- FixOn toggle
- camera profile比較
- delivery mode
- Phase名
- 内部英語診断
- 折りたたみで隠した旧UI

開発者向け診断は `SettingsTab.TitleBackgroundDiagnostics.cs` へ物理分離。

## 6.2 1クリック実機確認

入口:

```text
TitleScreenBackgroundService.StartOneClickTitleBackgroundVerification()
```

実行順:

1. 前回runのsettingsを一度だけ復元。
2. recovery journal残存を確認。
3. 現在settingsのsnapshot transactionを開始。
4. 前回レポート状態をclear。
5. `ApplySimpleAutoSetup()` で候補 `custom:n4f4` を先に確定。
6. ログイン中の現在地をセッション限定world probeへ取得。
7. finite、candidate、territory、world frameを検証。
8. configではなく非永続probeを有効化。
9. native integrationを再初期化。
10. hook未準備ならコード側で1回だけ再初期化。
11. それでも未準備なら、その場で失敗完了。
12. 成功時はrun-scoped QuickCheckを開始。
13. ユーザーはログアウトし、キャラ選択画面からログイン。
14. ログイン後にpre-login snapshotとrun-scoped診断から統合レポート作成。
15. settingsを復元。
16. native integrationをreload。
17. 最終レポートをファイル保存しclipboardキューへ積む。
18. PluginのUI Drawでclipboardへ自動コピー。

失敗時も追加操作を要求しない。

失敗レポートには最低限、reason、detail、service state、hookReady、candidate、active/saved/current territory、reinitResultを含める。

## 6.3 自動確認のsettings snapshotとrecovery journal

`TitleBackgroundAutomaticCheckSettingsSnapshot` は、背景、composition、候補、territory、runtime mode、lighting、camera framing、FixOn、anchor、world experimental、保存viewを退避・復元する。

recovery journal:

```text
file: title-background-auto-check-recovery.json
schema: 1
```

プラグイン異常終了や再起動後も、constructorの `TryRestoreInterruptedAutomaticCheck()` で復元を試みる。

復元順序やjournal schemaを軽率に変更しないこと。

## 6.4 既知signature

`TitleBackgroundKnownSignatures` へAPI15確認済みsignatureを集約。

通常の設定既定値はこの定数を参照する。1クリック経路では保存設定が空の場合に限り、resolver fallbackとして既知signatureを使う。

診断キーや設定プロパティ名は変更していない。

## 6.5 world座標とキャラクター配置

### セッション限定probe

1クリック経路が使うworld座標は、次のservice fieldにのみ保持される。

```text
_probeWorldAnchorEnabled
_probeWorldAnchorHasValue
_probeWorldAnchorCandidateId
_probeWorldAnchorPosition
_probeWorldAnchorTerritoryTypeId
```

`CaptureWorldProbeAnchorInMemory()` はConfigurationへ保存しない。

### 永続config経路

Configurationには次がある。

```text
TitleBackgroundCharaSelectAnchorTerritoryTypeId
TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled
```

ただし、永続config world座標の適用は次のstatic readonlyリリースゲートで停止している。

```text
TitleBackgroundExperimentalWorldPlacementLogic.PersistentApplyEnabled = false
```

この値をtrueにしてはいけない。

### gate

適用には最低限次が必要。

- experimental enabled
- position finite
- frame=`world`
- candidate非空かつ完全一致
- saved territory非ゼロ
- active territory非ゼロ
- territory完全一致
- run-scoped
- scene generation一致

### 配置優先順位

1. eligibleなセッション限定world probe
2. placement-supportedな既存anchor
3. camera-focus fallback

world座標はcharacter DrawObject位置にだけ流す。

world座標を次へ流してはいけない。

- FixOn camera position
- FixOn focus
- camera preset
- ground-verified判定

`runAnchorFrame=world` でも `runAnchorFrameGroundProvenance=False` を維持する。worldであることと地面検証済みであることは別。

## 6.6 world/lobby座標対応サンプル

`TitleBackgroundWorldCoordinateCorrespondence` はセッション限定のdiagnostics-onlyロジック。

各有効runから次を蓄積する。

- run ID
- candidate
- saved/active territory
- world probe position
- run target/source/frame/applied count
- FixOn observed focus
- pre-login camera/lookAt
- scene generation
- generation match
- capture context

判定:

- `insufficient-samples`
- `insufficient-elevation-variance`
- `inconsistent`
- `fixed-offset-candidate`
- `linear-y-candidate`

2件未満は不足。同一標高だけではYの傾きを計算しない。3件以上で線形fit残差を検証する。

この解析結果は座標変換を適用せず、ground-verifiedへ昇格させない。

## 6.7 カメラ初期構図と保存view

### 通常の初期カメラ

`custom:n4f4` + `CandidateRecommended` は、候補camera profileから初期yaw/pitch/distance/lookAtを構築し、scene load後に一度だけruntime cameraへ復元する。

マウス操作中は固定しない。初期適用後はユーザーが自由に回転できる。

### 現在の構図を保存

pre-loginキャラ選択画面でのみ通常UIの4番目が `現在の構図を保存` になる。

呼び出し:

```text
IsCharaSelectViewCaptureAvailable()
TryCaptureCharaSelectViewFromCurrentCamera(out status)
```

保存内容:

- SceneCamera position
- LookAt vector
- FOV Y
- 現在candidate ID

保存先:

```text
TitleBackgroundCharaSelectViewEnabled
TitleBackgroundCharaSelectViewCandidateId
TitleBackgroundCharaSelectViewCameraX/Y/Z
TitleBackgroundCharaSelectViewFocusX/Y/Z
TitleBackgroundCharaSelectViewFovY
```

保存時の処理:

1. post-loginなら拒否。
2. current lobby mapがCharaSelectでなければ拒否。
3. active camera snapshot取得。
4. candidate IDを正規化。
5. candidate空なら拒否。
6. camera/focus/FOVが利用可能か検証。
7. passive observationをOFF。
8. Configurationへ保存。
9. native integrationをreload。

次回scene generationのFixOnで、候補一致時にcamera+focus+FOVを1回だけ上書きする。

`_lastViewOverrideAppliedGeneration` により、同一scene generationでの重複適用を抑止する。

### 重要なUI制約

自動確認がbusyの間、キャラ選択画面の `現在の構図を保存` はdisabled。

理由は、自動確認中に保存するとsettings snapshot/recoveryと競合するため。

保存viewの確認は1クリック自動確認とは別に行う。

1. 自動確認を実行しない。
2. キャラ選択画面でマウス調整。
3. `現在の構図を保存`。
4. `構図を保存しました。次回から自動で再現します。` を確認。
5. ログイン。
6. 再ログアウト。
7. マウスを触らず保存構図か確認。

## 7. Configuration互換性

### 維持事項

- publicプロパティ名は変更しない。
- JSONはトップレベル構造を維持。
- enum値を変更しない。
- Newtonsoft.JsonとSystem.Text.Jsonの両方を使う互換属性を保持。
- `TitleBackgroundPhase2MExperimentalApplyMode` の旧JSONキーを保持。

### 今回追加されたworld関連設定

```text
TitleBackgroundCharaSelectAnchorTerritoryTypeId = 0
TitleBackgroundCharaSelectAnchorWorldExperimentalEnabled = false
```

normalizationはfail-closed。

次のいずれかならworld experimental enabledをfalseへ戻す。

- territory IDが0
- frameがworldでない
- candidate空
- X/Y/Zのいずれかが非有限

### 保存view設定

既定はOFF。candidate、camera、focus、FOVが不正ならstartup normalizationで無効化する。

## 8. 実機確認結果

## 8.1 最初のhook未準備失敗

ユーザー報告時刻: 2026-07-01 22:26:54 +09:00

```text
completion=partial
result=FAILED
reason=hook-not-ready
detail=scene hook unavailable
serviceState=HookCreateFailed
hookReady=False
reinitResult=still-not-ready
settingsRestored=True
runtimeReloaded=True
```

評価:

- hook未準備時にコード側で再初期化した。
- それでも失敗したためlogoutを要求せず失敗完了した。
- 失敗レポートが自動コピーされた。
- settingsとruntimeは復元された。
- 1クリック失敗契約は実機で確認できた。

## 8.2 成功run 1

```text
completedAt=2026-07-01 22:38:20 +09:00
runId=098d13213748440ebfd01ca5ed83235e
completion=complete
Background=applied
Login transition=safe
Post-login leak=none
characterPlace.runAppliedFrameCount=3144
characterPlace.runTarget=(-48.172, 104.917, -865.519)
characterPlace.runSource=world-experimental
characterPlace.runAnchorFrame=world
characterPlace.runAnchorFrameGroundProvenance=False
characterPlace.worldExperimentalSource=probe
characterPlace.worldExperimentalGate=eligible
characterPlace.persistentApplyEnabled=False
fixOn.hookInstalled=True
fixOn.calls=1
preLoginCameraGenerationMatchesFixOn=True
settingsRestored=True
runtimeReloaded=True
environment.dayTimeHours=2.864
environment.brightnessHint=night-dark
```

ユーザー目視:

- 背景場所は期待どおり変化。
- キャラクターは表示。
- 背景は暗い。

暗さはレポート上、ゲーム内時刻2.864時、`night-dark`。少なくともこのrunでは夜環境が主要因。

## 8.3 成功run 2

```text
completedAt=2026-07-01 22:51:34 +09:00
runId=5973307dbe6a4de3adcc86966ce34292
completion=complete
Background=applied
Login transition=safe
Post-login leak=none
characterPlace.runAppliedFrameCount=3064
characterPlace.runTarget=(-32.669, 105.567, -875.252)
characterPlace.runSource=world-experimental
characterPlace.runAnchorFrame=world
characterPlace.runAnchorFrameGroundProvenance=False
characterPlace.worldExperimentalSource=probe
characterPlace.worldExperimentalGate=eligible
characterPlace.persistentApplyEnabled=False
fixOn.hookInstalled=True
fixOn.calls=1
preLoginCameraGenerationMatchesFixOn=True
settingsRestored=True
runtimeReloaded=True
environment.dayTimeHours=7.401
environment.brightnessHint=daylight
```

このrunでも背景適用、login transition安全、post-login leakなし、world experimental probe由来配置を確認。

## 8.4 保存viewの確認状況

最新の自動確認レポートは次の値。

```text
view.enabled=False
view.candidate=none
view.camera=none
view.focus=none
view.fovY=none
view.overrideAppliedCount=0
view.overrideLastSource=not-run
fixOn.exp.gateReason=passive-precedence
fixOn.exp.applied=False
```

ユーザー提供画像は手動で視点移動した後の画像だった。

したがって、次は確認済み。

- UIに `現在の構図を保存` が表示される。
- マウスでカメラを動かせる。
- コード上はcapture/persist/replay経路が接続されている。

次は未確認。

- 保存成功メッセージ後、再ログアウト時にマウス操作なしで同じ構図が自動再現されること。
- `view.enabled=True`
- `view.overrideAppliedCount>0`
- `view.overrideLastSource=view`

保存viewの実機round-tripは完了扱いにしないこと。

## 9. 現時点の品質判定

### 確認済み

- 背景差し替え成功。
- キャラクター表示・配置処理の実行。
- login transition安全。
- post-login leakなし。
- probeはセッション限定。
- persistent world applyはfalse。
- candidate/territory/generation gateが成立したrunだけ配置。
- pre-login camera generationとFixOn generation一致。
- 自動確認のsettings復元。
- 自動確認後のruntime reload。
- hook未準備失敗時の自動再初期化1回。
- hook未準備失敗時の自動レポートコピー。
- 成功時の自動レポートコピー。

### 未確認・未完了

- world座標が実際の地面座標として正しいこと。
- `world` frameのground provenance。
- 複数標高サンプルによるworld/lobby変換方式の確定。
- 保存viewの実機round-trip。
- 暗さを製品側で補正する仕様。現在はゲーム内時刻・天候に従う。
- R3の残りのstate holder分割。

### 明示的に「問題ではない」もの

- `delivery.deliveryVerdict=working-background-only` は、背景経路が成立していることを示す。
- `character-placement-applied-unverified` は、配置処理は動いたがground検証が未完了という意味。
- `runAnchorFrameGroundProvenance=False` は安全側の正しい状態。
- `PersistentApplyEnabled=false` は未完成ではなく、解除してはいけないリリースゲート。

## 10. 現在の検証結果

実行日時: 2026-07-02 13:39 +09:00

実行:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\verify-refactor-phase.ps1
```

結果:

| 検証 | 結果 |
|---|---|
| `dotnet run --project tools\CharaSelectLogicTests` | 成功、439件 |
| Debug build | 成功、警告0、エラー0 |
| Windows Release build | 成功、警告0、エラー0 |
| `latest.zip` | 存在、545,293 bytes |
| `XivMiniUtil.json` | 存在、719 bytes |
| `git diff --check` | 成功 |

成果物:

```text
projects/XIV-Mini-Util/bin/Release/XivMiniUtil/latest.zip
projects/XIV-Mini-Util/bin/Release/XivMiniUtil.json
```

LF→CRLF警告は出るが、`git diff --check` のexit codeは0。無関係な改行一括変換を行わないこと。

## 11. バージョンと配布

source project:

```text
AssemblyVersion=0.3.8.0
Version=0.3.8
DalamudApiLevel=15
SDK=Dalamud.NET.Sdk/15.0.0
Target runtime=.NET 10
```

stable `pluginmaster.json`:

```text
AssemblyVersion=0.3.7.0
TestingAssemblyVersion=0.3.7.0
DownloadLink=v0.3.7/XivMiniUtil.zip
```

この差はリリース判断事項。勝手に0.3.8へ統一しない。

## 12. 次に行うべき作業

優先順位順。

### 12.1 保存view round-tripを1回だけ実機確認

自動確認とは別に行う。

1. キャラ選択画面でマウス調整。
2. `Settings` → `ログイン背景` → `現在の構図を保存`。
3. 保存成功メッセージを確認。
4. ログイン。
5. 再ログアウト。
6. マウスを触らず初期構図を確認。

成功なら、可能であれば次の診断値も確認する。

```text
view.enabled=True
view.candidate=custom:n4f4
view.overrideAppliedCount>0
view.overrideLastSource=view
```

ただしユーザーへ診断キー手動検索を要求しない。必要なら自動レポート経路へ含める。

### 12.2 world/lobby対応の複数標高サンプル

現在の1クリックフローだけで異なる標高の地点を測る。手動probe操作を追加しない。

最低2地点、線形残差を見るなら3地点以上。

結果が出ても、即座に変換を適用したりground-verifiedへ昇格させない。まずレポートと実機目視をレビューする。

### 12.3 R3を続ける場合

1回につき1責務だけ。

推奨順:

1. world probe / coordinate sample state
2. 保存view / camera observation state
3. character placement state
4. timeline diagnostic state
5. hook lifecycle state

native hook本体、pointer、detour、framework updateを先に動かさない。

## 13. 次AI向けの検証チェックリスト

コードを変更した場合:

```powershell
dotnet run --project tools\CharaSelectLogicTests
dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj
powershell -ExecutionPolicy Bypass -File scripts\release-build.ps1
Test-Path projects\XIV-Mini-Util\bin\Release\XivMiniUtil\latest.zip
Test-Path projects\XIV-Mini-Util\bin\Release\XivMiniUtil.json
git diff --check
git status --short
```

TitleBackgroundのhook、pointer、scene generation、diagnostic key生成、FixOn camera/focus、character placementを変更した場合は、自動検証だけで完了扱いにしない。

最低限の自己レビュー観点:

1. 挙動・Configuration/JSON互換性
2. unsafe lifecycle・Dispose・イベント解除
3. Title Background安全境界
4. テストの有効性とソース文字列検査への過剰依存
5. Git差分・Release成果物・文書整合

## 14. 参照ファイル

### 最優先

- `AGENTS.md`
- `README.md`
- `docs/refactoring-plan.md`
- `docs/refactor-baseline/2026-07-01-r0-current-state.md`
- `docs/refactor-baseline/test-boundaries.md`
- `projects/XIV-Mini-Util/XivMiniUtil.csproj`

### Plugin

- `projects/XIV-Mini-Util/Plugin.cs`
- `projects/XIV-Mini-Util/Plugin.ServiceConstruction.cs`
- `projects/XIV-Mini-Util/Plugin.Commands.cs`
- `projects/XIV-Mini-Util/Plugin.CommandHandlers.cs`
- `projects/XIV-Mini-Util/Plugin.UiEvents.cs`
- `projects/XIV-Mini-Util/Plugin.Lifecycle.cs`

### Title Background

- `projects/XIV-Mini-Util/Configuration.TitleBackground.cs`
- `projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.cs`
- `projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.NativeHooks.cs`
- `projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.QuickCheck.cs`
- `projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.OneClickVerification.cs`
- `projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.TimelineDiagnostics.cs`
- `projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundAutomaticCheckRuntimeState.cs`
- `projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundAutomaticCheckRecovery.cs`
- `projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundCharaSelectAnchor.cs`
- `projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundWorldCoordinateCorrespondence.cs`
- `projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundKnownSignatures.cs`
- `projects/XIV-Mini-Util/Windows/Components/SettingsTab.TitleBackground.cs`
- `projects/XIV-Mini-Util/Windows/Components/SettingsTab.TitleBackgroundDiagnostics.cs`

### Shop / Submarine

- `projects/XIV-Mini-Util/Services/Shop/ColorantItemResolver.cs`
- `projects/XIV-Mini-Util/Services/Shop/ColorantItemTextParser.cs`
- `projects/XIV-Mini-Util/Services/Shop/ShopDataCache.cs`
- `projects/XIV-Mini-Util/Services/Shop/ShopCacheBuildCoordinator.cs`
- `projects/XIV-Mini-Util/Services/Shop/ContextMenuService.cs`
- `projects/XIV-Mini-Util/Services/Shop/ShopItemIdentity.cs`
- `projects/XIV-Mini-Util/Services/Submarine/SubmarineService.cs`
- `projects/XIV-Mini-Util/Services/Submarine/SubmarineNotificationDispatcher.cs`

### Tests / Build

- `tools/CharaSelectLogicTests/TestRunner.cs`
- `tools/CharaSelectLogicTests/TestHelpers.cs`
- `tools/CharaSelectLogicTests/Tests/`
- `scripts/verify-refactor-phase.ps1`
- `scripts/release-build.ps1`

## 15. 最終状態

- この時点でcommit、push、branch操作は行っていない。
- ユーザー既存差分を戻していない。
- ソースコードは最新統合検証に成功。
- 背景差し替えと1クリック自動確認は実機成功。
- character placementは実行済みだがground未確認。
- 保存viewのコード経路は実装済みだが、再ログアウト時の自動再現は実機未確認。
- 次AIは、保存view round-tripの確認を最優先にし、その後必要ならR3を1責務ずつ継続する。
