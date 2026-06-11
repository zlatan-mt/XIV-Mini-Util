# Integrated Composition 実装メモ

## 対象コミット (3件)

| コミット | 内容 |
|---|---|
| `2666f45` | Integrated composition 診断フラグ追加・QuickCheck NG 理由拡張 |
| `f42ffdd` | TitleBackgroundIntegratedCompositionEnabled を実処理に接続 |
| `ad288dc` | Config migration・NG 優先順位・shouldArmAdapter 一貫性修正 |

---

## 問題の経緯

旧「撮影構成を有効にする」(CharaSelectSceneCompositionEnabled) は
`UpdateCharaSelectDisplayDetour` → `TryPatchOverrideDisplayData` で
既存 scene に territory patch を IN-PLACE 適用できた。

Title Background (`TitleBackgroundOverrideEnabled`) は
`CreateSceneDetour` でのみ override が適用される。
すでに CharaSelect にいる状態で Title Background を ON にしても
CreateScene が発火しないため background.applied=False のままになる。

---

## 修正内容

### 1. TitleBackgroundIntegratedCompositionEnabled フラグ (2666f45)

`Configuration.cs` に `TitleBackgroundIntegratedCompositionEnabled` プロパティを追加。  
`SetEnabled(true)` で `CameraOverrideEnabled` と同時に自動 ON。

QuickCheck input に診断フィールドを追加:
- `TitleBackgroundOverrideEnabledAtCheck`
- `TitleBackgroundCameraOverrideEnabledAtCheck`
- `LegacySceneCompositionEnabledAtCheck`
- `TitleBackgroundIntegratedCompositionEnabledAtCheck`
- `ShouldArmAdapterAtCheck`
- `ShouldArmAdapterReasonAtCheck`

`BuildShouldArmAdapterReason` を追加 (TitleBackgroundCharaSelectCameraLogic)。

---

### 2. 実処理への接続 (f42ffdd)

**根本原因**: CharaSelect にいる状態で Title Background を有効にしても
CreateScene が発火しない → `RequestCharaSelectReload()` で強制 reload が必要。

**対処**:
```
TryInvokeIntegratedCompositionRoute()
  └─ RequestCharaSelectReload(startedBySelfTest: false)
       └─ CurrentLobbyMap = None  → ゲームが scene を再作成
            → CreateSceneDetour → n4f4 override 適用
              → _quickCheckOverrideAppliedCount++
```

呼び出しタイミング:
- `SetEnabled(true)` の `ReloadNativeIntegration()` 直後
- `StartQuickCheck()` の baseline カウンター記録前

`RouteInvoked = (reason == "reload requested")` — 実際に副作用が起きた場合のみ true。

新 QuickCheck フィールド:
- `IntegratedCompositionRouteInvoked`
- `IntegratedCompositionRouteReason`
- `CameraFramingApplied` (phase2GApplyCount > 0)
- `SceneOverrideApplyObserved` (overrideAppliedCount > 0)

`BuildShouldArmAdapterReason` に optional params 追加:
- `integratedCompositionEnabled`
- `candidateValid`
- `hookReady`
- `sceneRouteReady`

新 detail lines (quickCheck.integratedCompositionRouteInvoked など 8 行)。

新 NG 理由 (background block 内):
1. route invoked but override not observed
2. flag enabled but route not invoked (RouteReason 非空の場合)
3. camera framing applied but override not observed

CharaSelectLogicTests: 4 ケース追加。

---

### 3. Config migration・矛盾修正 (ad288dc)

**実機ログで確認された問題**:
```
quickCheck.integratedCompositionEnabled=False  ← 旧 config が未補正
quickCheck.shouldArmAdapter=True               ← 3パラム版
quickCheck.shouldArmAdapter.reason=integratedCompositionDisabled  ← 矛盾！
reason=camera framing applied but scene override was not observed  ← 優先順位誤り
```

#### 修正 A: NormalizeAndMigrateFlags (static, TitleBackgroundCharaSelectCameraLogic)
```csharp
public static bool NormalizeAndMigrateFlags(
    bool overrideEnabled, bool cameraOverrideEnabled, bool integratedCompositionEnabled,
    out bool normalizedCameraOverride, out bool normalizedIntegratedComposition)
```
`overrideEnabled=True` なら `camera` / `integrated` を両方 True に補正。  
`NormalizeConfiguration()` から呼び出し、変更があれば `Save()` と `RecordTransitionEvent`。

#### 修正 B: shouldArmAdapter の導出統一
```csharp
// Before: 2つの独立した関数が別結果を返していた
var shouldArmAdapter = ShouldArmAdapter(3 params);  // ← 削除
var shouldArmAdapterReason = BuildShouldArmAdapterReason(7 params);

// After: reason から一方向に導出
var shouldArmAdapterReason = BuildShouldArmAdapterReason(7 params);
var shouldArmAdapter = shouldArmAdapterReason == "none";  // 常に一致
```
`ConfigureCharaSelectCameraAdapter()` の実際のアダプター arming は
3パラム版 `ShouldArmAdapter` のまま（camera adapter は integrated に非依存）。

#### 修正 C: NG 優先順位
```
1. candidateId = none
2. candidateFieldsValid = false
3. backgroundMode = Disabled
4. not mutation mode
5. TitleBackgroundOverrideEnabled=False   → "Character Select Background is disabled"
6. CameraOverrideEnabled=False            → "Title Background camera override is disabled"
7. IntegratedCompositionEnabled=False     → "integrated character composition is disabled"  ← NEW
8. ShouldArmAdapter=False                 → "adapter was not armed: <reason>"
9. (background not applied block)
   a. RouteInvoked=True && !Override      → "route was invoked but override not observed"
   b. !RouteInvoked && RouteReason≠""     → "flag enabled but route not invoked"
   c. CameraFraming && !Override          → "camera framing applied but override not observed"
   d. fallthrough                         → "background was not applied"
```

#### 修正 D: SettingsTab 警告表示
`TitleBackgroundOverrideEnabled=True && IntegratedCompositionEnabled=False` の場合:
```
Integrated composition is OFF. Re-enable Character Select Background or reset Title Background settings.
```

CharaSelectLogicTests: fix case 1〜5 追加 (migration, no-op, shouldArmAdapter derivation, NG priority, happy path OK)。

---

## アーキテクチャ制約 (変更禁止)

- Framework.Update でカメラを毎フレーム直接書き込まない
- SceneCamera.Position / LookAtVector / FoV を毎フレーム書き込まない
- ObjectTable zero-transform stubs を actor source に使わない
- UI 上の旧撮影構成トグルと Title Background を同時 ON にしない
- CharaSelectSceneCompositionEnabled を裏で true にするだけの実装禁止

---

## QuickCheck 確認手順

1. plugin 再起動 (NormalizeConfiguration で config 補正される)
2. CharaSelect に移動
3. UI: Enable Character Select Background = ON、候補 custom:n4f4
4. Start QuickCheck → ログイン → Run Check
5. title-background-quickcheck.txt で以下を確認:

```
quickCheck.integratedCompositionEnabled=True     ← 補正済み
quickCheck.integratedCompositionAutoEnabled=True ← migration が走った
quickCheck.integratedCompositionRouteInvoked=True
quickCheck.integratedCompositionRoute.reason=reload requested
quickCheck.shouldArmAdapter=True
quickCheck.shouldArmAdapter.reason=none          ← 矛盾なし
quickCheck.overrideAppliedCount=1 (以上)
quickCheck.charaSelectObserved=True
background.applied=True
result=OK (または WARN)
```
