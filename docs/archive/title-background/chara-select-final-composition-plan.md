# Character Select Final Composition Plan

## Goal

Character Select 画面で、選択キャラクター本体を残したまま、指定した場所、立ち位置、明るさ、保存済みエモートを確認できる状態にする。ログイン後は scene / camera / actor / lighting の状態を残さず、設定 OFF で安全に戻せることを必須条件にする。

## Current Findings

- background-only full scene override は Old Sharlayan の背景表示に成功したが、キャラ本体は表示されない。
- Final Composition mode では background-only route との競合は解消済みで、`titleBackgroundConflict=False` まで確認済み。
- Character Select の selected character pointer は取得できている。
- 保存済みエモート replay は attempted/applied まで到達している。
- `ClientSelectData.TerritoryType` / `ZoneId` patch と prefetch は attempted/applied まで到達している。
- 実機 SS では visible stage/background が default のままだったため、ClientSelectData patch は visible stage source そのものではない可能性が高い。

## Phase3C Added Framework

- `Stage strategy` を追加し、通常 UI では「診断のみ」または「現在の安全 route」を選べるようにした。
- default は `ObserveOnly` で、stage/background を変える write は行わない。
- `ClientSelectDataTerritoryPatch` は既存の安全 route として明示選択した場合だけ使う。
- `visualLocation` verdict を追加し、手動 SS で「場所=変わらない」を選ぶと `territory-patch-did-not-change-visible-stage` として扱う。
- Character Select 中の read-only stage probe summary を last observation に保持し、ログイン後の `/xmucdiag` で確認できるようにした。
- 保存するのは bool / id / name / timestamp / enum / string / int だけで、Character pointer は保存しない。

## Diagnostics To Read

- `charaSelectScene.visualLocation.routeVerdict`
- `charaSelectScene.visualLocation.nextAction`
- `charaSelectStageStrategy.selected`
- `charaSelectStageStrategy.reason`
- `charaSelectStageProbe.clientSelectData.patchApplied`
- `charaSelectStageProbe.clientSelectData.restoreApplied`
- `charaSelectStageProbe.lobbySheet.matchCount`
- `charaSelectStageProbe.layoutPrefetch.verdict`
- `charaSelectStageProbe.routeVerdict`
- `charaSelectScene.nextAction`

## Next Real Game Check

1. 「キャラ選択画面の撮影構成（最終目的モード）」を ON。
2. 「背景だけ差し替え実験」が OFF であることを確認。
3. `Scene profile` は `Old Sharlayan outdoor test`。
4. `Stage strategy` はまず「診断のみ」。次に必要なら「現在の安全 route」。
5. 「保存済みエモートを再生する」を ON。
6. Character Select に戻る。
7. SS で、キャラ本体、場所、エモート、明るさを見る。
8. UI の SS 判定を入力する。場所が default のままなら「場所=変わらない」。
9. ログイン後に `/xmucdiag` を実行する。
10. `visualLocation.routeVerdict` と `charaSelectStageProbe.routeVerdict` で次フェーズを決める。

## Branching

- `character visible + location changed + emote played`: Phase3D one-shot placement へ進む。
- `character visible + location unchanged`: visible stage source discovery を継続し、AgentLobby / Lobby row / UIStage / LayoutWorld / title lobby scene source の安全な hook point を探す。
- `character invisible`: foreground route を再調査する。
- `emote not played`: emote replay timing / selected contentId consistency を直す。
- `brightness=Dark`: 明るい scene profile / stage / layer の探索を優先する。

## Prohibitions

- camera direct write はしない。
- Phase3C では actor position / rotation write をしない。
- lighting / environment write はしない。
- ObjectTable を actor source にしない。
- per-frame correction はしない。
- post-login write はしない。
- Character Select 中の pointer をログイン後に再利用しない。

## Forward Plan

Phase3D は、Character Select final composition mode でキャラ本体が見え、stage/background/location が acceptable で、emote replay が見た目で確認でき、post-login leak がない場合だけ進める。対象は selected Character Select character pointer のみに限定し、contentId / selected index match、Character Select state、one-shot only、post-login no-op を必須にする。

Phase3E は lighting write ではなく、明るい profile / visible stage source の選定、manual SS brightness の記録、profile registry への昇格、read-only lighting diagnostics の順で進める。Old Sharlayan は現時点では `ExpectedBrightness=Unknown` のままとする。
