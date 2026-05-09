<!-- Path: docs/notes/title-background-implementation-result.md -->
<!-- Description: タイトル背景差し替え Phase 0 / Phase 1 実装結果 -->
<!-- Reason: native hook を含む変更の実装範囲、検証結果、未確認範囲を残すため -->
# タイトル背景差し替え 実装結果

作成日: 2026-05-06

## 実装した Phase

- Phase 0: 既存 CharaSelect 背景系 UI を「ログイン先エリア preload / 診断」扱いへ文言変更。
- Phase 1: `TitleScreenBackgroundService`、設定、UI、純粋ロジック、fail-closed hook lifecycle を追加。

## 変更ファイル

- `projects/XIV-Mini-Util/Configuration.cs`
- `projects/XIV-Mini-Util/Plugin.cs`
- `projects/XIV-Mini-Util/Windows/MainWindow.cs`
- `projects/XIV-Mini-Util/Windows/Components/SettingsTab.cs`
- `projects/XIV-Mini-Util/Services/TitleBackground/GameLobbyType.cs`
- `projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundAddressResolver.cs`
- `projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundPathHelper.cs`
- `projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundPreset.cs`
- `projects/XIV-Mini-Util/Services/TitleBackground/TitleBackgroundServiceState.cs`
- `projects/XIV-Mini-Util/Services/TitleBackground/TitleScreenBackgroundService.cs`
- `tools/CharaSelectLogicTests/Program.cs`
- `docs/notes/title-background-implementation-result.md`

## 実装内容

- `TitleBackground*` 設定を追加し、`ApplyFrom()` / `NormalizeAndMigrate()` / import-export snapshot に反映した。
- `TerritoryPath` は `TitleBackgroundPathHelper` で `bg/` prefix、`.lvb` suffix、`\` 区切りを正規化する。
- camera / focus 座標と FOV は `TitleBackgroundPreset` の純粋ロジックで sanitize / clamp する。
- `TitleScreenBackgroundService` を `CharaSelectService` から独立して追加した。
- `CreateScene` / `FixOn` / `LobbyUpdate` / `LobbyCurrentMap` の address resolver と hook lifecycle を分離した。
- invalid `TerritoryPath` または LVB 未検出時は override しない。
- invalid `TerritoryPath` または LVB 未検出時は設定値を残し、`TitleBackgroundOverrideEnabled` だけ false に落とす。
- address resolve / hook create / hook enable / runtime error は TitleBackground 機能だけを disable し、他機能を継続する。
- UI から enabled / TerritoryPath / camera / focus / FOV / apply / clear / status を操作できるようにした。
- import 後に `CharaSelectService.SyncFromConfiguration()` と `TitleScreenBackgroundService.ApplyFromConfiguration()` を両方呼ぶようにした。
- `TitleScreenBackgroundService` の状態変化、validation、resolver/hook lifecycle、override 実行点を `IPluginLog` に出すようにした。
- `/xmutbgdiag` / `/xmutbg` で TitleBackground 診断をチャットと plugin log に出せるようにした。

## ゲーム不要テスト結果

実行:

```powershell
dotnet run --project tools\CharaSelectLogicTests\CharaSelectLogicTests.csproj
```

結果:

- 成功。
- 既存 CharaSelect emote / voice / resolver テストに加え、TitleBackground の path normalize、validation、FOV clamp、座標 sanitize、Title <-> CharaSelect 遷移判定を確認した。
- 追記: Debug build と並列に走らせた初回は `obj\Debug\XivMiniUtil.Dev.dll` file lock で失敗。単独再実行では成功。

## build 結果

実行:

```powershell
dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj
dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj -c Release
```

結果:

- Debug build 成功。ただし devPlugins への `manifest.json` コピーで `XivMiniUtil.Dev.json` file lock 警告が 1 件出た。compile 自体は成功。
- Release build 成功。警告 0 / エラー 0。

## 実機確認結果

未確認。

未確認項目:

- plugin load でクラッシュしないこと。
- title screen 表示でクラッシュしないこと。
- override OFF で通常背景になること。
- invalid `TerritoryPath` で plugin 全体が落ちないこと。
- valid `TerritoryPath` で title 背景が変わること。
- camera / focus / FOV が反映されること。
- Title -> CharaSelect -> Title で古い背景が残らないこと。
- plugin unload で hook が dispose されること。
- `/xmutbg` 実行で診断情報がチャットと plugin log に出ること。

## hook / signature 状態

`TitleBackgroundAddressResolver` は `CreateScene` / `FixOn` / `LobbyUpdate` / `LobbyCurrentMap` を解決する構造を追加済み。

ただし、Phase 1 実装時点では GPL-3.0 実装の signature resolver をコピーせず、現行 client で独自検証した signature をまだ入れていない。そのため resolver は fail-closed し、TitleBackground 機能だけを disabled にする。

UI status には address 解決失敗理由を表示する。保存済みの `TitleBackgroundTerritoryPath`、camera、focus、FOV、BGM/weather/time 将来用設定は消さない。

## 未実装項目

- chara select 画面側の背景差し替え。
- BGM 差し替え。
- 天候固定。
- 時刻固定。
- 現在地とカメラ保存。
- 外部 JSON preset 管理。
- 現行 client で独自確認した native signature の反映。

## known risk

- `CreateScene` / `FixOn` delegate signature は実機で確認が必要。
- `LobbyUpdate` / `CreateScene` / `FixOn` の native ABI は実機で確認が必要。`GameLobbyType` は `LobbyCurrentMap` の `short` 読み書きに合わせて `short` underlying type にした。
- `FixOn` の camera / focus pointer 引数の実型が違う場合、signature 反映後に runtime crash の可能性がある。
- `LobbyCurrentMap` の static address 書き換えは、address drift 時に誤った場所を書き換える危険があるため、signature 独自確認が終わるまで有効化しない。
- `IDataManager.FileExists("bg/{path}.lvb")` は build では検証できないため、実機 Dalamud 環境での確認が必要。
- hook enable が途中で失敗した場合は、部分的に作成/有効化された hook を dispose するようにした。
- detour の runtime error 経路では original 呼び戻しを優先するため、hook 内で設定保存を行わないようにした。

## 次にやること

1. 現行 client / Dalamud API 15 環境で `CreateScene` / `FixOn` / `LobbyUpdate` / `LobbyCurrentMap` の signature と delegate signature を独自確認する。
2. resolver に確認済み signature を反映し、まず resolver/status のみ実機確認する。
3. `LobbyUpdate` logging、`CreateScene` logging、valid path override、`FixOn` camera override の順で段階的に有効化する。
4. 実機ログではキャラクター名、ワールド名、個人名、ローカルパスを伏せる。

## 2026-05-06 追記: 段階投入モード

実装:

- `TitleBackgroundRuntimeMode` を追加した。
- default は `ResolveOnly` とし、signature を反映して address 解決に成功しても hook を作成しない。
- `HookLoggingOnly` では `LobbyUpdate` / `CreateScene` / `FixOn` を観測ログだけに限定する。
- `Override` のときだけ `CurrentLobbyMap` 書き換え、`CreateScene` path override、`FixOn` camera override を許可する。
- `/xmutbg` 診断に `runtimeMode`、last observed `CreateScene` path、last observed `FixOn` FOV を追加した。
- UI に実行モード選択を追加した。

確認:

```powershell
dotnet run --project tools\CharaSelectLogicTests\CharaSelectLogicTests.csproj
dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj
dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj -c Release
```

結果:

- CharaSelect logic tests 成功。
- Debug build 成功。警告 0 / エラー 0。
- Release build 成功。警告 0 / エラー 0。

次の実機確認:

1. まず `ResolveOnly` のまま signature を反映する。
2. `/xmutbg` で `addresses` が non-zero、`resolverError=none`、`runtimeMode=ResolveOnly` になることを確認する。
3. `ResolveOnly` では hook が作成されず、背景差し替えも起きないことを確認する。
4. その後 `HookLoggingOnly` に切り替え、観測ログだけを確認する。

## 2026-05-09 追記: signature 入力と resolver 再解決

実装:

- `TitleBackgroundAddressResolver` が `Configuration` の signature 設定から address 解決するようにした。
- `TitleBackgroundCreateSceneSignature` / `TitleBackgroundFixOnSignature` / `TitleBackgroundLobbyUpdateSignature` / `TitleBackgroundLobbyCurrentMapSignature` を追加した。
- signature は保存時と resolver 実行時に trim する。
- UI に native signature 入力欄を追加した。
- UI に `address再解決` ボタンを追加し、plugin 再起動なしで `ResolveOnly` の address 解決をやり直せるようにした。
- UI に `signatureをクリア` ボタンを追加した。
- `/xmutbg` 診断に `signaturesConfigured` を追加し、各 signature が設定済みかだけを表示する。signature 文字列そのものは診断に出さない。

確認:

```powershell
dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj
dotnet run --project tools\CharaSelectLogicTests\CharaSelectLogicTests.csproj
dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj -c Release
```

結果:

- Debug build 成功。警告 0 / エラー 0。
- CharaSelect logic tests 成功。
- Release build 成功。警告 0 / エラー 0。
- Debug build と logic tests を並列実行した初回は `obj\Debug\XivMiniUtil.Dev.dll` file lock で logic tests 側が失敗した。単独再実行では成功。

次の実機確認:

1. 実行モードを `address解決のみ` のままにする。
2. native signature 欄へ現行 client で独自確認した signature を入力する。
3. `address再解決` を押す。
4. `/xmutbg` で `signaturesConfigured` がすべて `yes`、`addresses` がすべて non-zero、`resolverError=none` になるか確認する。
5. `ResolveOnly` では `hooksReady=False` のまま、背景差し替えが起きないことを確認する。

## 2026-05-09 追記: signature 調査補助ログ

補足:

- signature は場所ID、カットシーンID、TerritoryTypeId、map id ではない。
- signature はゲーム実行ファイル内の関数や静的変数を探すための機械語バイト列の目印。
- そのため、ゲーム内エリア名やカットシーン番号から自動的に導ける値ではない。

実装:

- resolver が最初の未設定 signature で即終了せず、4項目すべてを試すようにした。
- `/xmutbg` に `signatureNote` を追加し、signature が ID ではないことを明記した。
- `/xmutbg` に `signatureScan` 行を追加した。
- `signatureScan` では、各対象について `name`、`kind`、`status`、`address`、`message` を出す。
- signature 文字列そのものはログに出さない。

期待される未設定時の例:

```text
signatureNote=signature is a machine-code byte pattern, not a cutscene id, territory id, or map id.
signatureScan: name=CreateScene, kind=text, status=not-configured, address=zero, message=CreateScene signature is not configured.
signatureScan: name=FixOn, kind=text, status=not-configured, address=zero, message=FixOn signature is not configured.
signatureScan: name=LobbyUpdate, kind=text, status=not-configured, address=zero, message=LobbyUpdate signature is not configured.
signatureScan: name=LobbyCurrentMap, kind=static, status=not-configured, address=zero, message=LobbyCurrentMap signature is not configured.
```

## 2026-05-09 追記: CharaSelect 背景差し替えへの方針更新

目的変更:

- 以前の Phase 1 は title screen のみを対象にしていた。
- 2026-05-09 の実装では、キャラクター選択画面背景差し替えを主対象に変更した。
- HaselTweaks 型の emote / pet / queue preload は `CharaSelectService` 側に残し、背景差し替えは `TitleScreenBackgroundService` 側に分離したままにする。

実装:

- `TitleBackgroundRuntimeMode` は `Disabled` / `ResolveOnly` / `CharaSelectOnly` / `TitleAndCharaSelect` に変更した。
- `GameLobbyType` は現行 TitleEdit の事実情報に合わせ、`None = -1`、`Title = 0`、`CharaSelect = 1` とした。旧 `Movie = -1` は使わない。
- `CreateScene` / `LobbyUpdate` は `TryScanText` の match address を直接 hook 対象にせず、`E8 rel32` から resolved target を計算する。
- `ResolveOnly` では address 解決と read-only 診断だけを行い、hook は作成しない。
- `LoadLobbyScene` と `LobbyUpdate` で直近 lobby state を記録し、`EffectiveLobbyType == CharaSelect` の `CreateScene` だけ preset の scene 情報へ差し替える。
- override 直前に `LayoutWorld.UnloadPrefetchLayout()` を呼び、ログイン待機 preload との競合を避ける。
- `LobbyCurrentMap` は `ResolveOnly` では絶対に書き込まない。runtime override が ready で title/chara select 遷移を検出した場合だけ `None` 書き込みを試す。
- `/xmutbg` は `CreateScene.match` と `CreateScene.resolvedTarget`、`LobbyUpdate.match` と `LobbyUpdate.resolvedTarget`、`LobbyCurrentMap.writeAttempted` を出す。

自己レビュー修正:

- `ResolveOnly` で `TitleBackgroundOverrideEnabled` が残っていても hook unavailable 扱いにしないようにした。
- detour 内の例外処理で native original を二重呼び出しし得る構造を修正した。
- `LobbyUpdateDetour` に必要時の `CurrentLobbyMap = None` 書き込みを追加した。
- 診断出力の `LobbyCurrentMap.writeAttempted` を固定 `False` ではなく実状態表示にした。

確認:

```powershell
dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj
dotnet run --project tools\CharaSelectLogicTests\CharaSelectLogicTests.csproj
dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj -c Release
```

結果:

- Debug build 成功。警告 0 / エラー 0。
- CharaSelect logic tests 成功。dev plugin DLL file lock の retry warning が 1 件出たが、テスト結果は成功。
- Release build 成功。警告 0 / エラー 0。

未確認:

- 実機 Dalamud 上の `/xmutbg` 全 address non-zero。
- DC 選択後のキャラ一覧画面で背景が preset に切り替わること。
- キャラ選択後の login wait / queue 中に背景が変わらないこと。
- ログイン後の通常 gameplay に影響しないこと。
- plugin unload 時の hook dispose 実動作。

## 2026-05-09 追記: Phase 1 safety 修正

- `FixOn` camera hook は optional とし、既定では作成・有効化しない。
- CharaSelect scene override は `CreateScene` / `LobbyUpdate` / `LoadLobbyScene` / `LobbyCurrentMap` だけを必須にし、`FixOn` 未解決でも ready になれるようにした。
- `UpdateLobbyUIStage` は diagnostic-only とし、解決失敗しても scene readiness をブロックしない。
- `TitleAndCharaSelect` は未実装扱いにし、実装完了まで UI から選択不可にした。
- Camera / Focus / FOV override は Phase 2 に延期し、Focus fields は Phase 1 では予約値として扱う。
- Phase 1 acceptance は CharaSelect scene path override のみ。HaselTweaks 相当の emote / preload 機能とは分離したままにする。
