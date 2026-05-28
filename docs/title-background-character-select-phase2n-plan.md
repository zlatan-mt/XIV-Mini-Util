# XIV-Mini-Util Phase 2N 計画書
## Character Select Background Delivery MVP

対象 repo: `C:\Project\apps\XIV-Mini-Util`

この計画書は、Codex goal プロンプトに全文を詰め込まないための実装仕様書です。Codex には、このファイルを読ませて実装させます。

---

## 1. 最終目的

ログイン前の Character Select / Login 画面で、最短でユーザーが使える「背景変更機能」を実装します。

到達したい状態:

1. Character Select 画面の背景を差し替えられる
2. Character Select UI は通常通り使える
3. 可能なら選択中キャラクター本体も見える
4. キャラ本体を維持できない場合は、background-only mode として明確に warning する
5. 背景が暗い問題に対して lighting / brightness 診断と安全な mitigation を持つ
6. ログイン後に CharaSelect state / hook / scene override が漏れない
7. `/xmutbgdiag` 1回で状態・制限・次アクションが分かる

---

## 2. 現在の確定事実

### 成功していること

背景 override は動いています。

- `overrideTerritoryPath=ex3/01_nvt_n4/fld/n4f4/level/n4f4`
- `overrideTerritoryId=816`
- `overrideLayerFilterKey=51`
- Il Mheg / n4f4 相当の背景が表示される
- Character Select UI も表示される

Phase 2G camera curve override は成功寄りです。

- `phase2G.generationOverride.enabled=True`
- `phase2G.generationOverride.writeTiming=post-original`
- `phase2G.generationOverride.setMid.appliedCount == attemptCount`
- `phase2G.generationOverride.lowHigh.appliedCount == attemptCount`
- `verdict.phase2G.generatedCurveOverrideEffective=observed`
- `verdict.phase2G.finalLookAtYMatchesGeneratedCurve=observed`

login transition safety も現状 safe です。

- `postLoginPhase2GStillApplying=False`
- `postLoginSceneReadyAccepted=False`
- `staleCharaSelectStateAfterLogin=False`
- `loginTransitionSafety=safe`

### 失敗していること

- キャラ本体が表示されない
- スクリーンショット上も、背景と UI は見えるが中央に選択キャラ本体が出ていない
- 背景が暗く見える

### Phase 2M の重要ログ

ObjectTable の候補は実キャラ actor ではありません。

```text
phase2M.objectTable.totalScanned=8
phase2M.objectTable.playerLikeCount=8
phase2M.objectTable.battleCharaCount=8
phase2M.actorCandidate.zeroPositionCandidateCount=8
phase2M.actorCandidate.nonZeroPositionCandidateCount=0
phase2M.actorCandidate.namedCandidateCount=0
phase2M.actorCandidate.drawObjectNonNullCount=0
phase2M.actorCandidate.modelLikeNonNullCount=0
phase2M.actorCandidate.sourceBreakdown=ObjectTable:8
phase2M.actorCandidate.transformValidity=all-zero-transform
phase2M.actorCandidate.identityConfidence=none
phase2M.actorCandidate.stubLikelihood=high
phase2M.actorCandidate.resolution=stub-only
phase2M.nextAction=inspect-native-source
```

解釈:

- ObjectTable index `200..207` は zero-transform stub
- `Position=(0,0,0)` / drawObject null / model null
- actor placement 対象にしてはいけない
- CharaSelect 実表示モデルは ObjectTable 以外、または full scene override によって表示パイプラインが壊れている

---

## 3. 基本方針

今回からは「さらに診断を細かく増やす」だけではなく、ユーザーが使える機能に到達することを優先します。

優先順位:

1. 既存・履歴・TitleEdit 的な実装方針を再確認する
2. Character Select background mode を明確化する
3. native / preview model source を探す
4. source が見つかれば安全な preview / experimental route を作る
5. source が見つからなければ background-only fallback と compatibility warning を実装する
6. 暗さ問題を lighting / preset / layer の観点で扱う
7. 最後に `deliveryVerdict` と `nextAction` を出す

---

## 4. 追加する設定

### Background mode

例:

```csharp
public enum TitleBackgroundCharacterSelectBackgroundMode
{
    Disabled,
    DiagnosticsOnly,
    SceneOverrideOnly,
    PreserveCharaSelectForeground,
    NativePreviewModelSource,
    CompatiblePresetOnly
}
```

| Mode | 意味 |
|---|---|
| Disabled | Character Select background 機能を無効化 |
| DiagnosticsOnly | read-only 診断のみ |
| SceneOverrideOnly | 現在の full scene override 方式 |
| PreserveCharaSelectForeground | foreground / model / platform を維持し背景だけ差し替える試行 |
| NativePreviewModelSource | native / preview model source が見つかった場合の実験ルート |
| CompatiblePresetOnly | 互換性のある preset のみ使う fallback ルート |

### Lighting mode

例:

```csharp
public enum TitleBackgroundCharacterSelectLightingMode
{
    Default,
    DiagnosticsOnly,
    PreferBrightPreset,
    PreferBrightLayer,
    EnvironmentOverrideExperimental,
    DisableDarkeningExperimental
}
```

| Mode | 意味 |
|---|---|
| Default | 現状維持 |
| DiagnosticsOnly | lighting / env / fade を read-only 診断 |
| PreferBrightPreset | preset metadata から明るい候補を推奨 |
| PreferBrightLayer | layerFilterKey 差し替え候補を扱う |
| EnvironmentOverrideExperimental | safe public API がある場合だけ env/time/weather を one-shot 変更 |
| DisableDarkeningExperimental | safe public API がある場合だけ overlay/fade/dim を無効化 |

default は安全側。experimental は必ず default off。

---

## 5. 禁止事項

通常 path では禁止:

- `Framework.Update` camera maintenance の復活
- `SceneCamera.Position` direct write
- `SceneCamera.LookAtVector` direct write
- per-frame yaw/pitch/distance correction
- actor `Position` / `Rotation` default write
- ObjectTable zero-transform stub を actor として採用
- Phase 2G rollback
- 不確かな offset 直読み
- login transition 後の write / state leak

experimental path でも禁止:

- SceneCamera direct write
- per-frame correction
- sceneGeneration gate なしの write
- login transition 後の write
- default-on の危険挙動
- ambiguous / stub-only source への actor write

許可:

- diagnostics 追加
- config 追加
- safe public API read
- FFXIVClientStructs 公開 field の guarded read
- sceneGeneration-gated one-shot write
- default-off experimental write
- preset compatibility metadata
- lighting warning / recommendation
- tests / docs / diag file 追加

---

## 6. 実装タスク

### 6.1 既存・履歴調査

確認対象:

- `TitleScreenBackgroundService`
- `TitleBackgroundCharaSelectCameraLogic`
- `CreateSceneDetour`
- `LoadLobbySceneDetour`
- `LobbyUpdateDetour`
- `UpdateLobbyUIStage`
- `sceneReadySignal`
- camera curve / Phase 2G 周辺
- Configuration
- git history の title background / title edit / lobby / camera 関連

検索キーワード:

```text
CharaSelect
CharacterSelect
SelectCharacter
CharacterList
Lobby
LobbyUI
UIStage
Title
TitleScreen
CharacterManager
ClientObjectManager
GameObjectManager
DrawObject
Human
Model
Skeleton
Render
Preview
Mannequin
Env
Environment
Weather
Time
Sky
Fog
Light
Lighting
Color
Exposure
PostProcess
Fade
ScreenFade
Layer
```

成果物:

- `docs/title-background-character-select-delivery-notes.md`

記載内容:

- 調査したもの
- foreground preserve が可能そうか
- native preview model source が見つかったか
- 採用ルート
- 捨てたルートと理由
- 既知制限
- 次回実機確認手順

### 6.2 Foreground preserve 調査・実装

現在の original / override:

```text
original: ffxiv/zon_z1/chr/z1c1/level/z1c1
override: ex3/01_nvt_n4/fld/n4f4/level/n4f4
territoryId: 816
layerFilterKey: 51
```

診断項目:

```text
phase2N.foregroundPreserve.available
phase2N.foregroundPreserve.reason
phase2N.foregroundPreserve.originalScenePath
phase2N.foregroundPreserve.overrideScenePath
phase2N.foregroundPreserve.canKeepOriginalCharaStage
phase2N.foregroundPreserve.canOverrideBackgroundOnly
phase2N.foregroundPreserve.blocker
phase2N.foregroundPreserve.hookPoint
phase2N.foregroundPreserve.applied
phase2N.foregroundPreserve.skippedReason
```

safe hook point が見つかれば `PreserveCharaSelectForeground` mode を実装します。
無理なら unsupported reason を出します。

### 6.3 Native / preview model source 探索

ObjectTable は stub-only として reject します。

探索対象:

- CharacterManager
- ClientObjectManager
- GameObjectManager
- UIStage
- LobbyUI
- CharacterSelect UI
- CharacterList
- DrawObject owner scan
- Human / Model / Skeleton / Render
- Preview / Mannequin 的 source

診断項目:

```text
phase2N.nativePreviewSource.searched
phase2N.nativePreviewSource.source[*].name
phase2N.nativePreviewSource.source[*].available
phase2N.nativePreviewSource.source[*].readStatus
phase2N.nativePreviewSource.source[*].candidateCount
phase2N.nativePreviewSource.source[*].nonZeroTransformCount
phase2N.nativePreviewSource.source[*].drawObjectNonNullCount
phase2N.nativePreviewSource.source[*].modelLikeNonNullCount
phase2N.nativePreviewSource.source[*].error
phase2N.nativePreviewSource.bestSource
phase2N.nativePreviewSource.bestCandidate
phase2N.nativePreviewSource.resolution
```

resolution:

```text
not-found
found-single
found-ambiguous
found-but-no-transform
found-but-no-drawobject
unsupported
```

candidate fields:

```text
source
index
address
objectId
entityId
kind
name
position
rotation
drawObject pointer
model pointer
skeleton/render pointer
visible/draw flags
score
scoreReason
```

safe public API / FFXIVClientStructs 公開 field のみ使用します。
offset 推測は禁止です。

### 6.4 ObjectTable stub reject

stub-only の場合は明示的に actor source から除外します。

出力:

```text
phase2N.objectTableActorRejected=True
phase2N.objectTableActorRejected.reason=zero-transform-stub-only
phase2N.actorPlacement.ready=False
phase2N.actorPlacement.blocker=stub-only-object-table
```

### 6.5 Preset compatibility

追加 enum 例:

```csharp
public enum TitleBackgroundCharacterSelectCompatibility
{
    Unknown,
    Compatible,
    BackgroundOnly,
    CharacterHidden,
    TooDark,
    Unsupported
}

public enum TitleBackgroundCharacterSelectExpectedBrightness
{
    Unknown,
    Bright,
    Normal,
    Dark,
    TooDark
}
```

preset metadata:

```text
presetId
displayName
territoryPath
territoryId
layerFilterKey
expectedCompatibility
expectedBrightness
knownIssue
recommendedMode
notes
```

n4f4 / Il Mheg 相当は暫定で:

```text
expectedCompatibility=CharacterHidden or BackgroundOnly
expectedBrightness=Dark
knownIssue=ObjectTable candidates are stub-only; selected character model not visible with full scene override
recommendedMode=CompatiblePresetOnly or PreserveCharaSelectForeground if available
```

出力:

```text
phase2N.presetCompatibility.currentPresetId
phase2N.presetCompatibility.expectedCompatibility
phase2N.presetCompatibility.expectedBrightness
phase2N.presetCompatibility.warning
phase2N.presetCompatibility.recommendedMode
phase2N.presetCompatibility.knownIssue
phase2N.presetCompatibility.safeToUse
phase2N.presetCompatibility.characterExpectedVisible
```

### 6.6 Lighting / brightness

read-only で安全に診断:

- Env
- Weather
- Time
- Sky
- Fog
- Light
- Exposure
- PostProcess
- Fade
- Layer

出力:

```text
phase2N.lighting.mode
phase2N.lighting.diagnostic.available
phase2N.lighting.diagnostic.currentWeather
phase2N.lighting.diagnostic.currentTime
phase2N.lighting.diagnostic.envSet
phase2N.lighting.diagnostic.fog
phase2N.lighting.diagnostic.exposure
phase2N.lighting.diagnostic.overlay
phase2N.lighting.diagnostic.layerFilterKey
phase2N.lighting.diagnostic.error
phase2N.lighting.lastStatus
phase2N.lighting.lastSkippedReason
phase2N.lighting.expectedBrightness
phase2N.lighting.recommendedAction
```

safe write API がある場合だけ experimental one-shot。
無ければ `PreferBrightPreset` / `PreferBrightLayer` の warning と recommendation を実装します。

### 6.7 Delivery verdict

`/xmutbgdiag` に summary を出します。

必須 field:

```text
phase2N.featureGoal=character-select-background
phase2N.backgroundMode
phase2N.backgroundMode.reason
phase2N.characterVisibility.expected
phase2N.characterVisibility.observed
phase2N.characterVisibility.blocker
phase2N.nativePreviewSource.resolution
phase2N.nativePreviewSource.bestSource
phase2N.nativePreviewSource.bestCandidate
phase2N.foregroundPreserve.available
phase2N.foregroundPreserve.reason
phase2N.presetCompatibility.expectedCompatibility
phase2N.presetCompatibility.expectedBrightness
phase2N.lighting.mode
phase2N.lighting.recommendedAction
phase2N.deliveryVerdict
phase2N.nextAction
phase2N.nextAction.reason
```

verdict:

```text
working
working-background-only
blocked-character-source-not-found
blocked-incompatible-scene
blocked-lighting-unsupported
needs-one-more-experimental-run
unsafe
```

nextAction:

```text
use-background-only
try-compatible-preset
try-preserve-foreground
try-native-preview-source
try-bright-layer
inspect-titleedit-original
stop-managed-diagnostics
unsafe-stop
```

### 6.8 Optional delivery diag

可能なら追加:

```text
title-background-deliverydiag.txt
```

含める内容:

- Phase 2N summary
- background mode
- preset compatibility
- character visibility
- native preview source result
- foreground preserve feasibility
- lighting diagnostics
- deliveryVerdict
- nextAction

`/xmutbgdiag` でファイル名を出します。

---

## 7. Tests

`tools/CharaSelectLogicTests` に追加。

必須:

- ObjectTable all-zero + drawObject null -> stub-only
- stub-only -> ObjectTable reject
- stub-only -> ActorPlacementOneShot not ready
- valid native single -> NativePreviewModelSource ready
- multiple valid native -> ambiguous
- no native source -> fallback route
- dark preset -> warning / recommendedAction
- background success + source missing -> working-background-only or blocked-character-source-not-found
- default mode no actor/camera write
- login transition 後 no-op
- sceneGeneration mismatch no-op

---

## 8. 検証

実行:

```powershell
dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj
dotnet run --project tools\CharaSelectLogicTests\CharaSelectLogicTests.csproj
git diff --check
```

grep / manual check:

- `SceneCamera.Position` direct write なし
- `SceneCamera.LookAtVector` direct write なし
- `Framework.Update` camera maintenance 復活なし
- per-frame correction なし
- actor `Position` / `Rotation` default write なし
- ObjectTable stub を actor 採用していない

---

## 9. 自己レビュー

3回実施。

### Review 1: 目的到達

- 背景変更機能に近づいたか
- fallback / warning があるか
- 暗さ対策があるか
- 診断だけで終わっていないか

### Review 2: 安全性

- 禁止事項違反がないか
- post-login leak がないか
- experimental が default off か

### Review 3: 次回検証

- `/xmutbgdiag` 1回で verdict / nextAction が分かるか
- 人間が次に何を試すべきか明確か

---

## 10. Commit / push

commit message:

```text
Implement phase2n character select background delivery
```

main に commit / push。

---

## 11. 完了報告に含めること

- commit hash
- 変更ファイル
- 追加 config
- background mode
- lighting mode
- native source 探索結果
- foreground preserve 可否
- n4f4 互換性判定
- 暗さ対応
- default behavior が安全な根拠
- grep 結果
- build / test / diff-check 結果
- 次の実機手順
- 次ログで見る field
