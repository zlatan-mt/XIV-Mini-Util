<!-- Path: docs/notes/title-background-character-select-camera-summary.md -->
<!-- Description: Title Background character-select camera Phase 2G-2L summary -->
<!-- Reason: generated curve override を採用した理由と長期診断ポリシーを残すため -->

# Title Background Character-Select Camera Summary

## 結論

Title Background の character-select camera は、Phase 2G の post-original generated curve override を現在の採用実装とする。

成功条件は次の条件。

- `phase2G.generationOverride.setMid.appliedCount == phase2G.generationOverride.setMid.attemptCount`
- `phase2G.generationOverride.lowHigh.appliedCount == phase2G.generationOverride.lowHigh.attemptCount`
- `verdict.phase2G.generatedCurveOverrideEffective=observed`
- `verdict.phase2G.finalLookAtYMatchesGeneratedCurve=observed`
- `transition.verdict.loginTransitionSafety=safe`

Phase 2G generated curve success だけでは完了扱いにしない。ログイン遷移後に CharaSelect の active scene override / adapter / session が残らず、Phase 2G apply や sceneReady accepted が発生していないことを必須条件にする。

`verdict.phase2G.finalYawPitchDistanceMatchesPreset=not-observed` は、`transition.verdict.loginTransitionSafety=safe` の場合だけ非ブロック扱いにする。`verdict.phase2G.finalYawPitchDistanceMatchesPreset.blocking=False` を通常診断に出し、旧名の `verdict.phase2G.finalCameraStateMatchesPreset` は互換出力として当面残す。

## direct camera maintenance を採用しない理由

direct camera maintenance は、ゲーム側の後段 camera 更新と競合しやすい。

特に `SceneCamera.Position` / `SceneCamera.LookAtVector` への直接 write、または `Framework.Update` での yaw/pitch/distance 維持は、native の camera 生成・補間・scene 更新と競合する。実機診断でも、one-shot direct write は readback できても最終 LookAtY の決定源ではないことが確認された。

そのため、次の方針を禁止事項として扱う。

- `Framework.Update` による camera maintenance
- `SceneCamera.Position` への直接 write
- `SceneCamera.LookAtVector` への直接 write
- yaw/pitch/distance の per-frame correction
- yaw/pitch/distance mismatch を self-test blocker に戻すこと

## generated curve override を採用する理由

Phase 2E で、character-select の最終 LookAtY は `CalculateLobbyCameraLookAtY(distance, low, mid, high)` の return と一致することを確認した。つまり、最終 LookAtY は direct `SceneCamera` write ではなく、generated curve points を入力とする native 計算で決まる。

Phase 2G はこの決定源に合わせて、native generation の後に generated curve points だけを pin する。

- `SetCameraCurveMidPoint` の original 後に Mid を補正する。
- `CalculateCameraCurveLowAndHighPoint` の original 後に Low/High を補正する。
- native original は先に呼び、side effect は維持する。
- 書き換える範囲は generated curve points のみに限定する。

このため、Phase 2G は direct camera maintenance よりも native camera path に沿っており、現在の採用実装とする。

## 現在の診断ポリシー

通常 `/xmutbgdiag` は短い Phase 2G summary に限定する。

通常診断に残す主な項目:

- `hooksEnabled`
- `calculateLobbyCameraLookAtYHookEnabled`
- `setCameraCurveMidPointHookEnabled`
- `calculateCameraCurveLowAndHighPointHookEnabled`
- `sceneReadySignal.acceptedCount`
- `phase2G.generationOverride.*.attemptCount`
- `phase2G.generationOverride.*.appliedCount`
- `phase2G.generationOverride.lastStatus`
- `phase2G.generationOverride.lastSkippedReason`
- `verdict.phase2G.generatedCurveOverrideEffective`
- `verdict.phase2G.finalLookAtYMatchesGeneratedCurve`
- `verdict.phase2G.finalYawPitchDistanceMatchesPreset`
- `verdict.phase2G.finalYawPitchDistanceMatchesPreset.blocking=False`
- `verdict.phase2G.finalCameraStateMatchesPreset`
- `transition.sceneOverride.active`
- `transition.phase2G.appliedAfterLogin`
- `transition.verdict.postLoginPhase2GStillApplying`
- `transition.verdict.postLoginSceneReadyAccepted`
- `transition.verdict.staleCharaSelectStateAfterLogin`
- `transition.verdict.loginTransitionSafety`

詳細な timeline/call traces は failure-only diagnostics に残す。通常診断には出さない。

failure-only に残す主な項目:

- `phase2E.calculateLobbyCameraLookAtY.call[*]`
- `phase2F.setCameraCurveMidPoint.call[*]`
- `phase2F.calculateCameraCurveLowAndHighPoint.call[*]`
- `phase2C.timeline[*]`
- `phase2D.timeline[*]`
- `phase2F.timeline[*]`

## 重要 commit

- `be04edb` Phase 2G generated curve override
  - post-original generated curve override を追加。
  - direct camera maintenance ではなく generated curve points を main path にした。

- `c5ba3ee` Phase 2H cleanup title background diagnostics
  - 通常 `/xmutbgdiag` を短縮。
  - 詳細 timeline/call traces を failure-only 側に残した。
  - `finalCameraStateMatchesPreset=not-observed` を非ブロック扱いにした。

- `90c46de` Phase 2I retire direct LookAtY diagnostics
  - obsolete な direct LookAtY diagnostic/apply path を削除。
  - `finalYawPitchDistanceMatchesPreset` と blocking flag を追加。
  - `finalCameraStateMatchesPreset` は互換出力として残した。

- `a6bbf5f` Phase 2J clarify title background diagnostics policy
  - long-term diagnostic helper/test names に整理。
  - normal diagnostics と failure-only diagnostics の境界をコメント化。
  - generated-curve success と yaw/pitch/distance mismatch 非ブロック方針を明文化。

- Phase 2L login-transition cleanup and diagnostic correction
  - active scene override と historical lastOverride diagnostics を分離。
  - ログイン遷移後の active CharaSelect session cleanup と Phase 2G gate を強化。
  - 初回 `/xmutbgdiag` の累積 delta では post-login leak verdict を立てない。

## 今後の判断基準

今後の cleanup や不具合調査では、まず Phase 2G の成功条件を見る。

- generated curve override が attempt された分だけ applied されているか。
- generated curve override が final curve に反映されているか。
- final LookAtY が generated curve の native 計算結果に一致しているか。
- ログイン後に active CharaSelect scene override / adapter / session が残っていないか。
- ログイン後に Phase 2G apply や sceneReady accepted が発生していないか。

Yaw/Pitch/Distance が preset と最終一致しないことだけでは blocker にしない。ただし、これは `transition.verdict.loginTransitionSafety=safe` が確認できている場合に限る。そこを直す場合も、direct `SceneCamera` write や per-frame correction に戻さず、native source と generation path の追加調査から始める。
