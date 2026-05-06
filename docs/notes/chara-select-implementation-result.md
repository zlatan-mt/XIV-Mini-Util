<!-- Path: docs/notes/chara-select-implementation-result.md -->
<!-- Description: キャラ選択エモート機能の実装結果と検証状況を記録する -->
<!-- Reason: 実装後の到達点、未確認範囲、レビュー結果を追跡できるようにするため -->
# キャラ選択エモート実装結果

作成日: 2026-05-06

## 実装内容

- `Configuration` を version 4 に更新し、キャラ選択画面用の設定を追加した。
- `Services/CharaSelect/CharaSelectService.cs` を追加し、キャラ選択状態、保存エモート再生、エモート記録、ログイン先テリトリー事前読み込みを管理するようにした。
- `Services/CharaSelect/CharaSelectCharacterState.cs` を追加し、選択中キャラの `Character*`、`ContentId`、`TerritoryTypeId`、`ClassJobId` を保持するようにした。
- `Plugin.cs` で `IGameInteropProvider` を注入し、`CharaSelectService` を生成・破棄するようにした。
- `MainWindow.cs` と `SettingsTab.cs` に `Login / Character Select` 設定カテゴリを追加した。
- 設定インポート後に `CharaSelectService.SyncFromConfiguration()` を呼び、preload hook の有効状態を同期するようにした。
- `README.md` と `CHANGELOG.md` にユーザー向けの変更点を追記した。

## 追加設定

- `CharaSelectEmoteEnabled`
- `CharaSelectPreloadTerritoryEnabled`
- `CharaSelectSelectedEmotes`

`CharaSelectSelectedEmotes` は `ContentId -> emoteId` の dictionary として保存する。import/export は既存の `ApplyFrom()` 経由で対象に含める。

## hook

- `AgentLobby.UpdateCharaSelectDisplay`
  - 選択キャラの `ContentId` とキャラクターオブジェクトを更新する。
  - 保存済みエモートがあり、設定ONの場合に再生する。
- `EmoteManager.ExecuteEmote`
  - 記録モード中かつログイン中に成功したエモートを保存する。
  - Change Pose は `PlayerState.SelectedPoses[0]` から `[91, 92, 93, 107, 108, 218, 219]` へ変換する。
- `AgentLobby.OpenLoginWaitDialog`
  - 設定ON時のみ hook を有効化し、ログイン待機ダイアログ表示後にログイン先 `TerritoryType.Bg` を prefetch する。

## 検証結果

### Phase 0

- コマンド: `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj`
- 結果: 成功。0 warnings / 0 errors。

### Phase 1

- 対象: `Configuration.cs`
- コマンド: `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj`
- 結果: 成功。0 warnings / 0 errors。

### Phase 2-3

- 対象: `CharaSelectService` 骨格、`Plugin` / `MainWindow` / `SettingsTab` 配線
- コマンド: `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj`
- 初回結果: `IClientState.LocalContentId` が API15 に存在せず失敗。
- 修正: `IPlayerState.ContentId` を使用するように変更。
- 再実行結果: 成功。0 warnings / 0 errors。

### Phase 4-7

- 対象: hook、再生、記録、Change Pose、PreloadTerritory
- コマンド: `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj`
- 初回結果: static wrapper と pointer nullable の扱いで失敗。
- 修正:
  - `LayoutWorld.UnloadPrefetchLayout()` と `CharaSelectCharacterList.GetCurrentCharacter()` を static wrapper 経由に変更。
  - `Character*` の null 判定を nullable conditional から明示変数へ変更。
- 再実行結果: 成功。0 warnings / 0 errors。

### 最終 build

- コマンド: `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Debug`
- 結果: 成功。0 warnings / 0 errors。
- コマンド: `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Release`
- 結果: 成功。0 warnings / 0 errors。

## 未確認範囲

- Dalamud 実機上の `/xmu config` 表示。
- キャラ選択画面での実際のエモート再生。
- ログイン中のエモート記録。
- `OpenLoginWaitDialog` 実行時の `LoadPrefetchLayout` 副作用。

これらはゲームクライアント上の状態が必要なため、ローカル build では確認できない。

## 自己レビュー結果

1. API15差分レビュー
   - `IClientState.LocalContentId` を使わず、既存の `SubmarineService` と同じ `IPlayerState.ContentId` 方針に修正済み。

2. hook安全性レビュー
   - detour は original 呼び出しを先に行い、追加処理は try/catch で警告ログに留める形にした。
   - hook 初期化途中で失敗した場合、作成済み hook を破棄するように補強した。

3. 設定保存レビュー
   - UIからのON/OFF、記録、解除、import/export 経路で `Configuration.Save()` と `ApplyFrom()` に乗ることを確認した。
   - import 後に preload hook 状態が同期されない問題を修正した。

4. 個人情報ログレビュー
   - ContentId、キャラクター名、Webhook URL は通常ログに出さない。ログは機能名と例外中心にした。

5. PreloadTerritoryレビュー
   - UI文言は背景差し替えではなく Layout 事前ロードとして記述した。
   - OFF時と Dispose時に `UnloadPrefetchLayout()` を呼ぶようにした。
   - Dispose中の unload / hook dispose 失敗が他の破棄を止めないようにした。

## 残課題

- 実機確認で hook signature と実際の呼び出しタイミングを検証する。
- エモートごとの再生可否を見て除外リストを調整する。
- `PreloadTerritory` の housing territory 正規化は、実機で territory ID の妥当性を確認する。
- Tポーズが目立つ場合、ActionTimeline/TMB の事前ロードを別Phaseで追加する。

## 2026-05-06 実機確認後の修正

### 確認結果

- `/xmu config`: OK。
- ログイン中のエモート記録: OK。
- OFF時の挙動: OK。
- 解除: OK。
- PreloadTerritory: OK。
- キャラ選択画面での再生:
  - エモートは再生されるが、声が男性になっていた。
  - 最初から表示されるキャラクターでは再生されず、別キャラを一度表示して戻ると再生されることがあった。

### 修正内容

- `EmoteMode.RowId` を `CharacterModes` として直接 cast していた処理をやめ、`CharacterModes.EmoteLoop` と `modeParam = EmoteMode.RowId` の組み合わせに変更した。
- 同じ `ContentId` でもキャラクターオブジェクトが変わった場合は状態を更新するようにした。
- `ContentId`、`emoteId`、`Character*` の再生済み状態を保持し、初期表示キャラでも必要なら再生を再投入するようにした。

### 検証

- コマンド: `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj`
- 結果: 成功。0 warnings / 0 errors。

### 追加要望の計画

- 詳細計画: `docs/design/chara-select-followup-implementation-plan.md`

## 2026-05-06 開発DLL確認コマンド追加

### 背景

画面表示が変わらない修正では、ゲーム内で本当に新しい開発用DLLが読み込まれたか確認しづらい。

### 実装内容

- `/xmu version` と `/xivminiutil version` を追加した。
- チャットに以下を表示する。
  - assembly name
  - assembly version
  - dev plugin 判定
  - plugin load 時刻
  - 読み込み中DLLの実パス
  - DLLの最終更新時刻

### 使い方

```text
/xmu version
```

Debug版が読み込まれていれば、DLLパスは通常 `XivMiniUtil.Dev` 配下になる。

## 2026-05-06 キャラ選択エモート初回再生・声ID修正

### 追加確認結果

- DLL再読込後、最初のログインでは声が再生されない。
- DLL再読込後、最初から選択されているキャラクターではエモートが再生されない。
- 一度別キャラクターを読み込み、再度表示し直すとエモートは動作する。
- 声はキャラクターに関係なく男性声のまま変化しない。

### 原因

- 初期表示キャラは `AgentLobby.UpdateCharaSelectDisplay` が必ず発火するとは限らず、選択済みキャラの初回状態を拾えない経路があった。
- 同じ `ContentId` / 同じ `Character*` と判定した場合、声ID反映と再生再投入を十分に行えていなかった。
- `EmoteMode.RowId` を `CharacterModes` として扱うと、Excel の `ConditionMode` と mode parameter の役割が混ざる可能性があった。

### 修正内容

- `IFramework.Update` で未ログイン中の `AgentLobby.SelectedCharacterIndex` を定期ポーリングし、初期表示キャラでも状態更新を試行するようにした。
- ポーリングで初期表示キャラを拾った場合、描画準備前の再生取りこぼしを避けるため、15 frame 後に1回だけ同じエモートを再投入するようにした。
- 選択中キャラが同一でも、毎回 `CharaSelectCharacterEntry.ClientSelectData.VoiceId` を `Character.Vfx.VoiceId` に反映するようにした。
- `ClientSelectData.CustomizeData`、`Race`、`Sex`、`Tribe` を後から `Character.DrawData.CustomizeData` へ反映すると音声が無音化したため、この変更は撤回した。
- `VoiceId` 反映後に `Vfx.LoadCharacterSound()` を呼び、キャラ選択画面の表示キャラへ音声リソースを明示的に読み込ませるようにした。
- raw `ClientSelectData.VoiceId` を音声リソースIDとして直接渡すのをやめ、`Race`、`Tribe`、`Sex` に一致する `CharaMakeType.VoiceStruct` から再生用 voice id を解決するようにした。
- `ClientSelectData.Race` / `Tribe` / `Sex` はキャラ選択時に `0/0/4` など無効値になることがあるため、voice table 解決には `ClientSelectData.CustomizeData.Race` / `Tribe` / `Sex` を使うようにした。
- ログイン中にエモートを記録すると、ローカルプレイヤーの `Character.Vfx.VoiceId` を `ContentId` ごとに保存し、キャラ選択画面ではその保存済み voice id を最優先で使うようにした。
- 初期選択キャラの初回だけ声が出ない対策として、`PlayEmote()` のタイムライン開始直前にも保存済み voice id を再適用し、音声リソースロードを再実行するようにした。
- ログアウト直後の `GAME START` から初期選択キャラが表示される場合、表示直後の即時再生では音声ロードが間に合わないため、初期ポーリング経由では即時再生せず、60 frame 遅延中に voice load を複数回行ってからエモートを再生するようにした。
- 初期表示でも `UpdateCharaSelectDisplay` hook が先に発火すると即時再生していたため、初回エントリは hook / poll のどちらから来ても遅延再生に回し、遅延待機中の同一エントリ更新では即時再生しないようにした。
- 再生済み判定を `ContentId`、`emoteId`、`Character*` で管理し、初期表示・再生成・強制再生の判定を `CharaSelectReplayTracker` に分離した。
- エモート再生の mode 変換を `CharaSelectEmotePlaybackPlanner` に分離し、`EmoteMode.ConditionMode` と `EmoteMode.RowId` を分けて扱うようにした。
- 声ID反映を `CharaSelectCharacterApplier` に分離し、ゲームを起動しないテストで検証できるようにした。

### ゲームを利用しないテスト

- 追加: `tools/CharaSelectLogicTests`
- 対象:
  - 初回表示キャラは再生対象になること。
  - 同一キャラ・同一エモートは無限再生しないこと。
  - 同一 `ContentId` でも `Character*` が変われば再生対象になること。
  - forced replay は前回状態を無視すること。
  - 不正な `ContentId` / `emoteId` / `Character*` は再生しないこと。
  - `EmoteMode.ConditionMode` と `RowId` が正しく mode / parameter に分かれること。
  - 選択中ロビーエントリの `VoiceId` が表示キャラクターへ反映されること。
  - raw `VoiceId` が `VoiceStruct` 経由で再生用 voice id に変換されること。

### 検証

- コマンド: `dotnet run --project tools/CharaSelectLogicTests/CharaSelectLogicTests.csproj`
- 結果: 成功。`CharaSelect logic tests passed.`
- コマンド: `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj`
- 結果: 成功。0 warnings / 0 errors。
- コマンド: `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Release`
- 結果: 成功。0 warnings / 0 errors。

### 実機で再確認する点

- DLL再読込後、最初から選択されているキャラクターでエモートが再生されること。
- DLL再読込後、最初のログイン前キャラ選択で声が該当キャラクターの声になること。
- 別キャラへ切り替えたあと戻っても、声とエモートが表示中キャラに追従すること。

### 追加診断コマンド

- `/xmuc`
  - キャラ選択画面の選択 index、ロビー側 `Race` / `Tribe` / `Sex` / raw `VoiceId`、解決後 voice id、表示キャラ側 `Vfx.VoiceId`、`CharaMakeType.VoiceStruct` 候補をチャットに出力する。
  - 男性声固定の原因を、raw voice / voice table / 表示キャラ状態のどこで外しているか切り分けるために追加した。
