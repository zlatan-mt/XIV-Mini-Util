# Title Background Character Select Delivery Notes

## 調査対象

- `TitleScreenBackgroundService`: `CreateSceneDetour` は CharaSelect の full scene path / territory / layer を差し替える。背景は出せるが、foreground だけを残す境界は見つからない。
- `TitleBackgroundCharaSelectCameraLogic`: Phase 2G の generated curve gate は維持。direct `SceneCamera` write や per-frame correction は使わない。
- `LoadLobbySceneDetour` / `LobbyUpdateDetour` / `UpdateLobbyUIStage`: scene generation と ready signal の観測点。foreground-only background replace の安全な hook point にはならない。
- `ObjectTable` / `PlayerObjects` / `CharacterManagerObjects`: 安定した CharaSelect preview model source は未確認。zero-transform stub や post-login world object は actor source として採用しない。
- native / preview model source: `CharaSelectCharacterManager` / `UIStage` / DrawObject owner 方向は、公開 field または安全な signature source が未解決。

## 採用ルート

- MVP は `SceneOverrideOnly` / `CompatiblePresetOnly` を background delivery route として扱う。
- selected character が隠れる compatibility entry は background-only として warning する。
- current custom n4f4 override target は `selectedPresetId=none` の custom override configuration であり、実 preset ではない。
- `custom:n4f4` は Phase 2N の synthetic compatibility entry。background-only usable として `safeToUse=True`、`characterExpectedVisible=False`、`expectedCompatibility=BackgroundOnly`、`expectedBrightness=Dark` にする。
- post-login `/xmutbgdiag` では現在ワールドの ObjectTable を Phase 2N native preview source 判定に使わず、pre-login capture / placement timeline の snapshot だけを信頼する。
- `/xmutbgdiag` に `phase2N.deliveryVerdict` と `phase2N.nextAction` を追加し、1回で次の実機作業が分かるようにした。
- `phase2N.mvpStatus=complete-background-only` は、背景が使えて transition が safe、かつ character model source が未検証または post-login ObjectTable 無効化済みの場合の MVP 完了判定として扱う。

## 捨てたルート

- ObjectTable candidate への actor placement: 安定した CharaSelect preview model source として確認できないため reject。
- foreground preserve: full scene replacement から original CharaSelect stage だけを残す safe hook point が未確認。
- native preview model source write: source が未解決のため default-off でも write は未実装。
- lighting/time/weather write: safe public one-shot API が未確認のため、今回は warning と recommendation のみ。

## 既知制限

- Background-only mode では選択キャラクター本体は見えない可能性が高い。
- post-login world ObjectTable は CharaSelect preview source として扱わない。
- 暗い背景は custom override target / layer 互換性の問題として扱い、明るい custom override target または `PreferBrightLayer` の次アクションへ寄せる。
- `EnvironmentOverrideExperimental` / `DisableDarkeningExperimental` は config enum のみで、safe API が見つかるまで write しない。

## 次フェーズ候補

- bright custom override target / layer candidate を追加する。
- 実機SS比較で明るさを確認する。
- `custom:n4f4` は Dark / BackgroundOnly の基準候補として維持する。
- 候補追加の手順と比較観点は [title-background-character-select-bright-candidates.md](title-background-character-select-bright-candidates.md) を参照する。

## 次回実機確認

1. Character Select で current custom n4f4 override target を有効化して `/xmutbgdiag` を実行する。
2. `phase2N.deliveryVerdict` が `working-background-only`、`phase2N.mvpStatus` が `complete-background-only` になるか確認する。
3. `phase2N.objectTableActorRejected=True` と `phase2N.actorPlacement.ready=False` を確認する。
4. `phase2N.lighting.expectedBrightness=Dark` と `phase2N.lighting.recommendedAction` を見る。
5. `delivery.detailDump=title-background-deliverydiag.txt` が保存されるか確認する。

期待 field:

```text
phase2N.characterVisibility.observed=not-observed
phase2N.nativePreviewSource.currentObjectTableIgnored=True
phase2N.nativePreviewSource.currentObjectTableIgnoredReason=post-login-world-object-table-not-valid-for-chara-select
phase2N.overrideCompatibility.source=custom-override
phase2N.overrideCompatibility.selectedPresetId=none
phase2N.overrideCompatibility.currentOverrideId=custom:n4f4
phase2N.overrideCompatibility.expectedCompatibility=BackgroundOnly
phase2N.overrideCompatibility.expectedBrightness=Dark
phase2N.overrideCompatibility.characterExpectedVisible=False
phase2N.compatibility.characterExpectedVisible=False
phase2N.lighting.recommendedAction=add-bright-override-candidate
phase2N.mvpStatus=complete-background-only
phase2N.mvpBlockingIssue=none
phase2N.mvpKnownLimitation=selected-character-model-hidden
```
