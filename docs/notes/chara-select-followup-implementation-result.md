<!-- Path: docs/notes/chara-select-followup-implementation-result.md -->
<!-- Description: キャラ選択follow-up実装の結果と検証PDCA -->
<!-- Reason: 実装内容、ゲーム不要テスト、自己レビュー修正、未確認範囲を追跡するため -->
# キャラ選択 follow-up 実装結果

作成日: 2026-05-06

## 実装内容

### エモート複数ストック

- `CharaSelectEmotePresets` を追加し、`ContentId -> List<emoteId>` でキャラごとの保存エモートを持つようにした。
- `CharaSelectActiveEmotePresetIndexes` を追加し、キャラごとの active slot を保存するようにした。
- `CharaSelectLastRecordedEmotes` を追加し、ログイン中に記録したエモートを `ContentId` ごとに一時保持するようにした。
- 旧 `CharaSelectSelectedEmotes` は migration source / fallback として残し、新規保存先は preset 側へ寄せた。
- preset 操作を `CharaSelectEmotePresetStore` に分離し、ゲーム不要テストで検証できるようにした。
- UIに `[前へ] [次へ] [再生] [現在スロットへ保存] [追加保存] [削除]` を追加した。

### 指定エリア表示 override

- `CharaSelectOverrideTerritoryEnabled` と `CharaSelectOverrideTerritoryTypeId` を追加した。
- override 表示用 prefetch と login wait preload を `CharaSelectPrefetchOwner` で区別するようにした。
- 同じ owner / territory / `Bg` の場合は `LoadPrefetchLayout` を再実行しない。
- login wait preload 後は、override 側がログイン先 preload を上書きしないようにした。
- UIに TerritoryTypeId 入力、現在ログイン先の使用、読み込み、固定解除を追加した。

### DC名記録

- `CharaSelectLastDataCenterName` と `CharaSelectShowLastDataCenterNameEnabled` を追加した。
- 設定ON時、ログイン中にローカルプレイヤーの current world から DC 名を保存する。
- 起動時 `DataCenter` 表示の直接置換は、対象 addon/text node の実機確認が必要なため今回の実装では有効化していない。UIにも「表示置換は対象addon確認後」と明記した。

## PDCA

### PDCA 1

- Plan: preset 操作をゲーム非依存クラスに分離し、テストを追加する。
- Do: `CharaSelectEmotePresetStore` と preset テストを追加した。
- Check: `dotnet run --project tools/CharaSelectLogicTests/CharaSelectLogicTests.csproj` が `IClientState.LocalPlayer` で失敗。
- Act: `_objectTable.LocalPlayer` 経由に修正し、型推論失敗のテスト期待値を `uint[]` に修正した。

### PDCA 2

- Plan: 本体配線後にゲーム不要テストと Debug build を通す。
- Do: `CharaSelectService`、`Configuration`、`SettingsTab` を接続した。
- Check: テスト成功、Debug build 成功。
- Act: 次の自己レビューへ進めた。

### PDCA 3

- Plan: UI操作と設定正規化を見直す。
- Do: `読み込み` ボタンが override を有効化するようにした。DC名設定文言を保存実態に合わせた。
- Check: テスト成功、Debug build 成功。
- Act: DC名保存は設定ON時のみ行うようにした。

### PDCA 4

- Plan: 旧設定fallbackと削除動作を見直す。
- Do: preset削除時に旧 `CharaSelectSelectedEmotes` も必ず削除するよう修正した。
- Check: fallback復活防止テストを追加し、テスト成功、Debug build 成功。
- Act: prefetch owner 優先順位のレビューへ進めた。

### PDCA 5

- Plan: login wait preload と override preload の競合を見直す。
- Do: owner が `LoginWait` の場合、override prefetch を再適用しないようにした。
- Check: テスト成功、Debug build 成功、Release build 成功。
- Act: DC名保存ON直後に次フレームで保存試行するよう poll カウンタを調整した。

## 自己レビュー結果

1. API/コンパイルレビュー
   - `IClientState.LocalPlayer` は API15 で使えないため、既存注入済みの `_objectTable.LocalPlayer` に修正した。

2. preset削除レビュー
   - preset削除後に旧設定fallbackでエモートが復活する問題を修正した。

3. 設定正規化レビュー
   - import JSON で preset list が null の場合でも落ちないよう比較処理を防御した。

4. prefetch競合レビュー
   - login wait preload を override より優先し、ログイン直前の読み込み競合を避けるようにした。

5. UI/文言レビュー
   - DC名表示置換は未実機確認のため、UI文言を「記録」に寄せ、直接置換は未有効化であることを明記した。

## 検証

- `dotnet run --project tools/CharaSelectLogicTests/CharaSelectLogicTests.csproj`
  - 結果: 成功。`CharaSelect logic tests passed.`
- `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj`
  - 結果: 成功。0 warnings / 0 errors。
- `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Release`
  - 結果: 成功。0 warnings / 0 errors。
- `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Release`
  - 結果: 成功。0 warnings / 0 errors。

## 実機で再確認する点

- キャラごとに複数presetが混ざらず保存されること。
- 記録直後は「最後に記録したエモート」だけが変わり、保存ボタン後に active preset が変わること。
- preset切替直後に表示中キャラへ切替先エモートが再生されること。
- delayed replay 待機中に preset を切り替えても、古いエモートが後から再生されないこと。
- override territory がキャラ選択画面の背景に実際に反映されるか。
- login wait preload が override より優先され、ログイン待機列でクラッシュしないこと。
- 起動時 `DataCenter` text node の候補を実機で特定すること。

## 2026-05-06 実機フィードバック後の修正

### 報告内容

- `キャラ選択画面に表示するエリアを固定する（実験）` の checkbox が入らない。
- `[現在のログイン先を使う]`、`[読み込み]`、`[固定解除]` が機能していない。
- `最後にログインしたDC名を記録する（実験）` はONにでき、記録DC名も合っているが、効果が分かりにくい。
- `[現在スロットへ保存]` でゲームがクラッシュする。

### 原因

- override checkbox は `TerritoryTypeId != 0` のときだけONにする実装だったため、ID未指定状態では即OFFへ戻っていた。
- `[現在のログイン先を使う]` はキャラ選択中の `_currentEntry.TerritoryTypeId` だけを見ており、ログイン中の `IClientState.TerritoryType` を見ていなかった。
- ログイン後もキャラ選択画面の `_currentEntry.Character` を保持していたため、設定UIから preset 保存後に stale な `Character*` へ再生をかける可能性があった。
- DC名機能は現時点では「保存」までで、起動時表示の直接置換は未実装だったが、UIだけではその境界が弱かった。

### 修正内容

- `ActiveContentId` はログイン中なら `_playerState.ContentId` を優先するようにした。
- ログイン中の framework update では、キャラ選択画面由来の pointer / replay state を破棄するようにした。
- preset 保存・追加後の即時再生は、ログイン中には行わないようにした。
- override checkbox は ID 未指定でもONにできるようにした。実際の読み込みは `TerritoryTypeId` がある場合だけ行う。
- `[現在のログイン先を使う]` は、ログイン中なら `IClientState.TerritoryType`、未ログイン中なら `_currentEntry.TerritoryTypeId` を使うようにした。
- `LoadPrefetchLayout` 周辺を try/catch で囲み、失敗時は warning log に留めるようにした。
- DC名機能のUI説明に、表示置換は対象 addon 確認後であることを明記した。

### 再検証

- `dotnet run --project tools/CharaSelectLogicTests/CharaSelectLogicTests.csproj`
  - 結果: 成功。`CharaSelect logic tests passed.`
- `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj`
  - 結果: 成功。0 warnings / 0 errors。
- `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Release`
  - 結果: 成功。0 warnings / 0 errors。

## 2026-05-06 UpdateLoginPosition 経路への追加修正

### 報告内容

- `UpdateCharaSelectDisplay` の一時差し替え後も、ログイン画面背景は変わらなかった。
- 実機ログでは `XivMiniUtil.Dev.dll` が `2026-05-06 14:41:34 JST` の Debug DLL として読み込まれていた。

### 原因

- キャラ選択表示の再生成ではなく、ログイン背景は `AgentLobby.UpdateLoginPosition(int)` / `OpenLoginWaitDialog(int position)` の `position` 経路で決まっている可能性が高い。
- `OpenLoginWaitDialog` hook は事前読み込みON時だけ有効だったため、固定エリアONだけでは position 差し替えが走らない可能性があった。

### 追加修正

- `AgentLobby.UpdateLoginPosition` hook を追加し、固定エリアON時は `Lobby` sheet から固定先 Territory に対応する row を探して `position` として渡すようにした。
- `OpenLoginWaitDialog` でも同じ `position` 解決を通してから original を呼ぶようにした。
- `OpenLoginWaitDialog` hook は、事前読み込みONまたは固定エリアONのどちらかで有効になるようにした。
- UIに `ログイン背景position: last=..., override=...` を追加し、実機で hook 呼び出しと解決結果を確認できるようにした。
- `CharaSelectLobbyPositionResolver` を追加し、ゲーム不要テストで Lobby row 解決を検証した。

### 再検証

- `dotnet run --project tools/CharaSelectLogicTests/CharaSelectLogicTests.csproj`
  - 結果: 成功。`CharaSelect logic tests passed.`
- `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj`
  - 結果: 成功。0 warnings / 0 errors。devPlugins の `XivMiniUtil.Dev.dll` は `2026-05-06 14:56:22` に更新。
- `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Release`
  - 結果: 成功。0 warnings / 0 errors。

## 2026-05-06 指定地点対応

### 実機確認結果

- エモートスロット保存と切り替えは動作した。
- エリア読み込みは動作したが、ログイン画面の表示位置は未変更だった。
- DC名の記録は期待通り。

### 追加実装

- `CharaSelectOverridePositionEnabled`、`CharaSelectOverridePositionX/Y/Z` を追加した。
- UIに `表示地点を指定する（X/Y/Z）`、X/Y/Z入力、`現在位置を使う`、`地点解除` を追加した。
- `Level` sheet の `Territory` / `X` / `Y` / `Z` を使い、指定座標に最も近い `Level` row を解決する `CharaSelectLevelResolver` を追加した。
- override prefetch では、解決した `Level.RowId` と `Level.Type` を `LoadPrefetchLayout` の `levelId` / `layerEntryType` に渡すようにした。

### 注意

- `LoadPrefetchLayout` は X/Y/Z を直接受け取らず、`levelId` / `layerEntryType` を受け取るAPIだった。
- そのため今回の実装は「指定座標そのものへカメラを置く」ではなく、「指定座標に最も近い Level row を選んで読み込む」方式。
- ログイン画面の実際のカメラ位置が変わるかは、実機での再確認が必要。

### 再検証

- `dotnet run --project tools/CharaSelectLogicTests/CharaSelectLogicTests.csproj`
  - 結果: 成功。`CharaSelect logic tests passed.`
- `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj`
  - 結果: 成功。0 warnings / 0 errors。
- `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Release`
  - 結果: 成功。0 warnings / 0 errors。

## 2026-05-06 ログイン画面が変わらない報告後の修正

### 報告内容

- 設定値は保存できるが、ログイン画面背景は以前と同じで全く変わらない。
- エモートは動作確認済みで完了扱い。

### 原因

- 既存修正は `LoadPrefetchLayout` による先読みが中心で、表示中のキャラ選択背景を切り替える経路には入っていなかった。
- `OpenLoginWaitDialog(position)` の `position` はログイン待機位置であり、マップ座標ではなかった。
- 設定変更後に現在選択中キャラの `UpdateCharaSelectDisplay` を再実行していなかったため、仮に表示差し替え経路があっても即時反映されなかった。

### 追加修正

- `UpdateCharaSelectDisplay` の original 呼び出し前だけ、選択キャラの `ClientSelectData.TerritoryType` / `ZoneId` を固定エリア・指定地点に対応する値へ一時差し替えするようにした。
- original 呼び出し後は entry を取り直し、`ContentId` が一致する場合だけ元の `ClientSelectData` へ戻すようにした。古い pointer へ復元しない。
- 固定エリア、TerritoryTypeId、指定地点、固定解除の変更時に、現在選択中キャラの `UpdateCharaSelectDisplay` を明示的に呼び直すようにした。
- 指定地点は引き続き X/Y/Z を直接カメラへ渡すのではなく、同一 Territory 内の最寄り `Level.RowId` を `ZoneId` として使う方式。

### PDCA / 自己レビュー

- Review 1: original 実行中にキャラ一覧が再構成される可能性を考慮し、差し替え前 pointer へ直接復元しない実装へ変更。
- Review 2: `SelectedCharacterIndex` の異常値で更新を呼ばないよう、正規化後 index を 0-39 に制限。
- Review 3: 設定保存、先読み、表示更新の責務を確認。追加コード修正なし。
- Review 4: docs が先読み前提のままだったため、本節を追加して実装方針を更新。
- Review 5: 実機未確認範囲を再確認。ゲーム不要テストではクラッシュ防止とビルド整合までを確認し、実際の背景反映は次回実機確認対象。

### 再検証

- `dotnet run --project tools/CharaSelectLogicTests/CharaSelectLogicTests.csproj`
  - 結果: 成功。`CharaSelect logic tests passed.`
- `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj`
  - 結果: 成功。0 warnings / 0 errors。
