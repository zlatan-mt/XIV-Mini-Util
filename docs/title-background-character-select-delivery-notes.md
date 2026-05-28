# Title Background Character Select Delivery Notes

## 調査対象

- `TitleScreenBackgroundService`: `CreateSceneDetour` は CharaSelect の full scene path / territory / layer を差し替える。背景は出せるが、foreground だけを残す境界は見つからない。
- `TitleBackgroundCharaSelectCameraLogic`: Phase 2G の generated curve gate は維持。direct `SceneCamera` write や per-frame correction は使わない。
- `LoadLobbySceneDetour` / `LobbyUpdateDetour` / `UpdateLobbyUIStage`: scene generation と ready signal の観測点。foreground-only background replace の安全な hook point にはならない。
- `ObjectTable` / `PlayerObjects` / `CharacterManagerObjects`: Phase 2M の結果では zero-transform stub-only。actor source として採用しない。
- native / preview model source: `CharaSelectCharacterManager` / `UIStage` / DrawObject owner 方向は、公開 field または安全な signature source が未解決。

## 採用ルート

- MVP は `SceneOverrideOnly` / `CompatiblePresetOnly` を background delivery route として扱う。
- selected character が隠れる preset は background-only として warning する。
- `n4f4` / Il Mheg 相当は暫定で `expectedCompatibility=CharacterHidden`、`expectedBrightness=Dark`。
- `/xmutbgdiag` に `phase2N.deliveryVerdict` と `phase2N.nextAction` を追加し、1回で次の実機作業が分かるようにした。

## 捨てたルート

- ObjectTable zero-transform candidate への actor placement: stub-only のため reject。
- foreground preserve: full scene replacement から original CharaSelect stage だけを残す safe hook point が未確認。
- native preview model source write: source が未解決のため default-off でも write は未実装。
- lighting/time/weather write: safe public one-shot API が未確認のため、今回は warning と recommendation のみ。

## 既知制限

- Background-only mode では選択キャラクター本体は見えない可能性が高い。
- 暗い背景は preset / layer 互換性の問題として扱い、`PreferBrightPreset` / `PreferBrightLayer` の次アクションへ寄せる。
- `EnvironmentOverrideExperimental` / `DisableDarkeningExperimental` は config enum のみで、safe API が見つかるまで write しない。

## 次回実機確認

1. Character Select で `n4f4` preset を有効化して `/xmutbgdiag` を実行する。
2. `phase2N.deliveryVerdict` が `working-background-only` になるか確認する。
3. `phase2N.objectTableActorRejected=True` と `phase2N.actorPlacement.ready=False` を確認する。
4. `phase2N.lighting.expectedBrightness=Dark` と `phase2N.lighting.recommendedAction` を見る。
5. `delivery.detailDump=title-background-deliverydiag.txt` が保存されるか確認する。
