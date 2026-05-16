<!-- Path: docs/notes/title-background-phase2e-lookaty-source.md -->
<!-- Description: Title Background Phase 2E LookAtY 収束源の調査メモ -->
<!-- Reason: character select camera の最終 LookAtY が 0.834 へ収束する入力源を絞るため -->
# Title Background Phase 2E LookAtY 収束源調査

作成日: 2026-05-16

## Phase 2D から確定した前提

- `phase2D.timelineStatus=complete` かつ `phase2D.timelineLatestFrame=600` で、キャラ選択中の camera timeline は有効。
- `runtimeRestore.restoredDistance=3.3` は frame 0 から 600 まで維持され、観測範囲では後段 overwrite は未観測。
- `/xmutbgdiag` 実行時の `current*` は通常ログイン後の ActiveCamera であり、キャラ選択中の `phase2D.timelineLatest` と final verdict として比較できない。
- `LobbyCamera.LastLookAtVector.Y` への one-shot write は readback 上は成功するが、最終 `ActiveCamera.SceneCamera.LookAtVector.Y` の決定源ではない。
- `LobbyCamera.SetTiltOffset(curve.Mid)` 単体も、最終画角を支配する効果は未観測。

## 再調査した実装

### FFXIVClientStructs API 15

`LobbyCamera` は `Camera` / `CameraBase` 由来の以下を expose している。

- `Distance` / `InterpDistance`
- `DirH` / `DirV`
- `LastLookAtVector`
- `TiltOffset`
- `UpdateState()` / `Update()`
- `CalculateSceneCameraPitch()` / `CalculateSceneCameraYaw()`
- `UpdateTiltOffset()`
- `GetCameraTargetObject()` / `GetTargetObject()`
- `SetTiltOffset(float)`

一方で、キャラ選択用の camera Y curve point は typed field としては expose されていない。

### TitleEdit current 系

古い TitleEdit 系は `FixOn(cameraPos, focusPos, fovY)` を中心に title screen camera を差し替えているが、character select camera の継続補正は薄い。

### RokasKil TitleEdit 系

新しい TitleEdit 系は character select camera 用に `LobbyCameraExpanded` を定義し、FFXIVClientStructs の `LobbyCamera` の外側に以下を読む。

| field | offset | 意味 |
| --- | ---: | --- |
| `Yaw` | `0x140` | camera yaw |
| `Pitch` | `0x144` | camera pitch |
| `Roll` | `0x148` | camera roll |
| `TitleScreenLocked` | `0x2C0` | title screen camera lock 状態らしい |
| `CameraCurveEnabled` | `0x2C2` | character select camera curve 有効状態 |
| `MidPoint` | `0x2D0` | LookAtY curve middle point |
| `LowPoint` | `0x2E0` | LookAtY curve low point |
| `HighPoint` | `0x2F0` | LookAtY curve high point |

さらに以下の native points を hook している。

| point | signature | 用途 |
| --- | --- | --- |
| `SetCameraCurveMidPoint` | `0F 57 C0 0F 2F C1 73 ?? F3 0F 11 89` | midpoint value を補正して負 Y も許容 |
| `CalculateCameraCurveLowAndHighPoint` | `F3 0F 10 81 ?? ?? ?? ?? F3 0F 11 89` | low/high point を head offset 付きで補正 |
| `CalculateLobbyCameraLookAtY` | `48 83 EC ?? F3 41 0F 10 01 0F 28 D1` | distance と 3 curve point から LookAtY を計算 |
| `LobbyCameraFixOn` | `C6 81 ?? ?? ?? ?? ?? 0F 28 CB 8B 02` | FixOn 後に camera tick を走らせる補助 |
| `LobbySceneLoaded` | `E8 ?? ?? ?? ?? 0F B7 CD 40 88 35` | scene load 後に yaw/pitch/distance を復元し、LookAtY 再計算を予約 |

重要なのは、`ForceSetLookAtY()` が `LastLookAtVector.Y` ではなく、以下の形で直接 `SceneCamera.LookAtVector.Y` を再設定している点。

```csharp
SceneCamera.LookAtVector.Y = CalculateLobbyCameraLookAtY(
    lobbyCamera,
    lobbyCamera->Distance,
    &lobbyCamera->LowPoint,
    &lobbyCamera->MidPoint,
    &lobbyCamera->HighPoint);
```

## Phase 2E 候補

### 1. `CalculateLobbyCameraLookAtY(distance, low, mid, high)`

最有力候補。Phase 2D では Distance が 3.3 で維持され、LookAtY だけが 0.834 へ収束している。RokasKil 実装も、キャラ選択中の LookAtY は distance と 3 curve point から native 関数で計算される前提で扱っている。

次フェーズではまず read-only hook / probe にして、frame 0〜240 の間に以下を記録する。

- call count
- input `distance`
- input `low/mid/high` の `Position` / `Value`
- return value
- 同 frame 近辺の `ActiveCamera.SceneCamera.LookAtVector.Y`

return value が 0.834 と一致するなら、書き込み対象は `LastLookAtVector.Y` ではなく、この native point の input curve または return value detour が本命になる。

### 2. `LobbyCameraExpanded.LowPoint/MidPoint/HighPoint`

`SetTiltOffset(curve.Mid)` 単体では効果が未観測だったため、`TiltOffset` ではなく、未 expose の curve point 群が本命候補。現在の実装は `curve.Low/Mid/High` を保持しているが、実際に書いているのは `SetTiltOffset(mid)` だけで、`LowPoint/MidPoint/HighPoint` そのものは観測・更新していない。

次フェーズでは direct write ではなく read-only probe から始め、0.834 収束と同時に curve point がどう変化するか確認する。`CameraCurveEnabled` も同時に見る。

### 3. `SetCameraCurveMidPoint` / `CalculateCameraCurveLowAndHighPoint`

curve point を作る native 経路。RokasKil 実装は midpoint と low/high の生成点を別々に hook しており、character head offset や negative Y 対応をここで入れている。

`CalculateLobbyCameraLookAtY` の return が 0.834 と一致した場合、次にこの 2 点を hook 候補として比較する。直接 `SceneCamera.Position` / `LookAtVector` / `FoV` を毎 frame 補正する方針にはしない。

## Phase 2E 実機結果

`CalculateLobbyCameraLookAtY` の read-only probe で、MiniUtil 実機環境でも native return value が character select camera の最終 `ActiveCamera.SceneCamera.LookAtVector.Y` と一致することを確認した。

- `calculateLobbyCameraLookAtYHookEnabled=True`
- `phase2E.calculateLobbyCameraLookAtY.callCount=3215`
- `verdict.phase2E.nativeReturnMatchesActiveLookAtY=observed`
- `verdict.phase2E.nativeReturnMatchesFinalStableLookAtY=observed`
- `phase2E.calculateLobbyCameraLookAtY.finalStableLookAtY=0.834`

安定後の observed call は以下。

| input / output | value |
| --- | ---: |
| `distance` | `3.3` |
| `lowPoint` | `(1.1, 1.393)` |
| `midPoint` | `(3.3, 0.834)` |
| `highPoint` | `(5.5, 0.655)` |
| `returnValue` | `0.834` |
| `activeLookAtY.after` | `0.834` |

この結果から、character select の最終 LookAtY は `CalculateLobbyCameraLookAtY(distance, low, mid, high)` の return で決まる。`distance=3.3` の安定状態では `midPoint.Value` がそのまま final LookAtY になる。

否定寄りになったもの:

- `LobbyCamera.LastLookAtVector.Y` への one-shot write
  - readback は成功するが、final LookAtY の決定源ではない。
- `LobbyCamera.SetTiltOffset(curve.Mid)`
  - final camera への効果は未観測。

## Phase 2F 対応関係

MiniUtil が保存している runtime curve は world Y 絶対値に近い値で、native curve point の `Value` は look-at basis からの相対 Y と見なすのが自然。

今回ログ:

| value | observed |
| --- | ---: |
| `characterPosition.Y` | `38.915` |
| `curveLow` | `40.35` |
| `curveMid` | `39.774` |
| `curveHigh` | `39.59` |
| `curveLow - characterY` | `1.435` |
| `curveMid - characterY` | `0.859` |
| `curveHigh - characterY` | `0.675` |
| native `lowPoint.Value` | `1.393` |
| native `midPoint.Value` | `0.834` |
| native `highPoint.Value` | `0.655` |

差分:

| point | saved curve delta | native value | delta |
| --- | ---: | ---: | ---: |
| low | `1.435` | `1.393` | `0.042` |
| mid | `0.859` | `0.834` | `0.025` |
| high | `0.675` | `0.655` | `0.020` |

完全一致しない理由は、`characterPosition.Y` が native curve の基準点そのものではないためと考える。差分量は約 `0.02` から `0.04` で、単なる丸めよりは大きいが、別の大きな座標系差ではない。

現時点の候補評価:

- character position と実際の look-at basis の差
  - 最有力。`curve - characterY` と native value が近いため、basis は `characterPosition.Y` 近傍だが完全一致しない位置にある。
  - draw object root、model transform、camera target basis のいずれかが `characterPosition.Y` から数 cm 相当ずれている可能性が高い。
- draw object / skeleton head position offset
  - 有力。low/mid/high がすべて native 側で小さく出ているため、native の基準 Y が `characterPosition.Y` より少し高い、または TitleEdit 側の head/basis 補正が入っている可能性がある。
  - ただし low の差が mid/high より大きく、単一の一定 offset だけでは完全には説明しきれない。
- TitleEdit 側の `cameraYOffset` / head offset 相当
  - 有力。TitleEdit が `SetCameraCurveMidPoint` と `CalculateCameraCurveLowAndHighPoint` を分けて補正していることと整合する。
  - mid は `curveMid - basisY`、low/high は別計算で生成される可能性があるため、Phase 2F では生成経路の確認が必要。
- 記録タイミング差
  - 可能性は残るが優先度は低い。Phase 2E では安定後 call が大量にあり、final stable LookAtY と return が一致しているため、主要因ではなさそう。

## Phase 2F 実装案比較

### A. `SetCameraCurveMidPoint` / `CalculateCameraCurveLowAndHighPoint` detour で生成値を補正

評価: 本命。

利点:

- TitleEdit に近い方式で、native curve point の生成経路を正す。
- `CalculateLobbyCameraLookAtY` は既存の最終計算として残せるため、distance 変化や interpolation の native 挙動を壊しにくい。
- mid と low/high を別の生成点で扱えるため、今回の `0.02` から `0.04` の非一定差にも対応しやすい。
- 成功後の診断は `CalculateLobbyCameraLookAtY` read-only probe で継続検証できる。

リスク:

- delegate ABI と引数意味の確認が必要。
- 生成関数が scene load 中に複数回呼ばれる場合、補正条件を character select かつ TitleBackground active に限定する必要がある。
- wrong basis のまま saved curve 絶対値を入れると、見た目は近いが微妙にずれる可能性がある。

推奨条件:

- 先に read-only probe で、生成関数の input/output と `LobbyCameraExpanded.LowPoint/MidPoint/HighPoint` の timeline を確認する。
- write 実装時も one-shot 相当、または生成関数 detour 内の限定補正にし、毎 frame `SceneCamera` へ書かない。

### B. scene ready 後に `LobbyCameraExpanded.LowPoint/MidPoint/HighPoint.Value` を one-shot write

評価: 次点。

利点:

- 実装が単純。
- Phase 2E で final LookAtY が curve point input に依存することは確定しているため、正しい timing なら効果が出る可能性が高い。
- `CalculateLobbyCameraLookAtY` return を変更しないので、最終計算式は native のまま維持できる。

リスク:

- scene ready 後にゲーム側が curve point を再生成すると潰れる。
- 現在の scene-ready signal は provisional で、curve point 生成完了後かどうかが未確定。
- one-shot write が効かなかった場合、再生成 timing の問題か、basis 換算の問題か、切り分けが難しい。

適用するなら:

- 先に `LowPoint/MidPoint/HighPoint` timeline を取り、frame 0 から 600 のどこで安定するかを確認する。
- `CalculateLobbyCameraLookAtY` call の直前/直後で point が変わらないことを確認してから試す。

### C. `CalculateLobbyCameraLookAtY` return を条件付きで差し替え

評価: 慎重扱い。

利点:

- Phase 2E により最終 LookAtY へ直接効くことはほぼ確実。
- curve point の生成 timing や後段再生成に左右されにくい。

リスク:

- 3215 call 観測のとおり高頻度関数であり、毎回の計算関数 detour 介入になる。
- distance と curve interpolation の native 挙動を bypass しやすい。
- 条件ミス時の影響範囲が広く、character select camera の自然なズーム/補間を壊す可能性がある。
- 本来の input curve が不正なまま残るため、別処理が curve point を参照する場合の整合が取れない。

使う条件:

- A/B が timing または ABI の理由で成立しない場合の fallback。
- その場合でも `distance=midPoint.Position` 近傍だけなど、条件を極小化する。

## Phase 2F 推奨

次に書く場所として最も安全なのは A、つまり `SetCameraCurveMidPoint` / `CalculateCameraCurveLowAndHighPoint` の生成経路。理由は、Phase 2E で final LookAtY の決定源が `CalculateLobbyCameraLookAtY` の input curve だと確定し、かつ saved curve と native point value の差が basis/head offset 由来に見えるため。

B は実装 spike としては有効だが、scene ready 後の再生成で潰れる可能性がある。C は最終結果には効くが、毎回呼ばれる計算関数への介入で設計リスクが高いため、本命にしない。

## 次の read-only probe 案

Phase 2F の write 設計を確定するには、次の probe を最小限で入れる。

### 1. `LobbyCameraExpanded` curve timeline probe

目的: `LowPoint/MidPoint/HighPoint` が scene load 直後から安定後までいつ、何に変わるかを確認する。

記録項目:

- frame: `0, 1, 2, 4, 8, 16, 30, 60, 90, 120, 180, 240, 300, 450, 600`
- `CameraCurveEnabled`
- `LowPoint.Position` / `LowPoint.Value`
- `MidPoint.Position` / `MidPoint.Value`
- `HighPoint.Position` / `HighPoint.Value`
- 同 frame の `ActiveCamera.Distance`
- 同 frame の `ActiveCamera.SceneCamera.LookAtVector.Y`
- 直近の `CalculateLobbyCameraLookAtY.returnValue`

判定:

- native point が early frame で一度だけ生成され、その後安定するなら B も試す価値あり。
- frame 後半まで再生成される、または `CalculateLobbyCameraLookAtY` call 前後で変わるなら A を優先する。

### 2. basis 差分 probe

目的: `curve - characterY` と native point value の差が、どの basis と一致するかを確認する。

最小記録項目:

- saved `characterPosition.Y`
- active/draw object position Y が安全に取れる場合の Y
- target object / head basis 相当が安全に取れる場合の Y
- `curveLow/Mid/High - observedBasisY`
- native `Low/Mid/High.Value`

注意:

- draw object や skeleton 参照が不安定なら、Phase 2F-1 では無理に広げない。
- まず curve timeline を優先し、basis probe は安全な参照点が見つかった場合だけ追加する。

## 現時点でしないこと

- curve point write 実装
- `CalculateLobbyCameraLookAtY` return の変更
- direct `SceneCamera` write
- 毎 frame 補正

Phase 2F-1 は read-only の curve timeline probe までに留める。write は、A の生成経路 ABI と native curve point timeline が揃ってから判断する。
