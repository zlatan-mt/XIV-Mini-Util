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

## 推奨

Phase 2E の次実装は `CalculateLobbyCameraLookAtY` の read-only probe を第一候補にする。ここで return value と最終 `SceneCamera.LookAtVector.Y=0.834` の一致を確認してから、curve input 側を補正するか、return value detour にするかを決める。

`LobbyCamera.LastLookAtVector.Y` one-shot write は、readback 成功の診断として残す価値はあるが、最終画角制御の本線から外す。
