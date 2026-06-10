<!-- Path: docs/notes/codex-title-background-final-implementation-plan.md -->
<!-- Description: タイトル背景差し替え実装の最終計画書 -->
<!-- Reason: レビュー済み指示を、実装担当Codexが迷わず安全に進められる実行順と制約へ整理するため -->
# タイトル背景差し替え 最終実装計画

作成日: 2026-05-06

対象リポジトリ: `C:\Project\apps\XIV-Mini-Util`

前提 commit:

- `3b60e08f72626462b1a66cf003be2d0e189bd931`

入力元:

- `docs/notes/chara-select-title-background-research.md`
- `docs/notes/codex-title-background-review-and-implementation-plan.md`
- `docs/notes/codex-title-background-review-and-implementation-plan-revised.md`
- `docs/notes/codex-title-background-final-instructions.md`

## 1. 実装目的

`XIV Mini Util` のログイン/キャラ選択関連機能のうち、背景差し替えだけを `CharaSelectService` から分離し、タイトル画面の背景を指定 `TerritoryPath` と camera/focus/FOV で差し替えられるようにする。

Phase 1 の対象は title screen のみ。chara select 画面側の背景差し替え、BGM、weather、time、現在地カメラ保存は Phase 1 では実装しない。

## 2. Codex が守る法則

この章を最優先ルールとする。

### 法則 1: エモート機能を壊さない

以下は今回の主対象ではない。必要がない限り触らない。

- `CharaSelectEmotePresetStore`
- `CharaSelectEmotePresets`
- `CharaSelectActiveEmotePresetIndexes`
- `CharaSelectLastRecordedEmotes`
- emote replay
- voice id 補正
- DC 名記録

変更後も `tools\CharaSelectLogicTests` を必ず通す。

### 法則 2: 背景差し替えを `CharaSelectService` に詰め込まない

`CharaSelectService` は以下に限定する。

- エモート再生/記録
- voice id 補正
- DC 名記録
- ログイン先 preload/診断

タイトル背景差し替えは `TitleScreenBackgroundService` に分離する。

### 法則 3: `LoadPrefetchLayout` を背景差し替え本体にしない

`LoadPrefetchLayout`、`UpdateLoginPosition`、`ClientSelectData.TerritoryType` 差し替えは title scene の raw path を置換しない。背景差し替えの本命経路として深追いしない。

既存 UI は「背景差し替え」ではなく「preload/診断」へ文言を降格する。

### 法則 4: raw `TerritoryPath` を主キーにする

TitleEdit 型の背景差し替えでは、`TerritoryTypeId` ではなく以下の形式の raw path を使う。

```text
ffxiv/.../level/...
```

`IDataManager.FileExists($"bg/{path}.lvb")` で validation できる形に正規化する。

### 法則 5: X/Y/Z だけで「場所」を表したことにしない

背景として自然に見せるには以下が必要。

- `TerritoryPath`
- camera position
- focus position
- FOV

単一の X/Y/Z は camera なのか focus なのかが曖昧なので、Phase 1 の UI は camera と focus を分ける。

### 法則 6: GPL-3.0 コードをコピーしない

TitleEditPlugin は GPL-3.0。仕組みの参考に留める。以下は禁止。

- TitleEdit のコードをそのままコピーする。
- signature resolver を丸写しする。
- preset JSON や parser を移植する。

現行 Dalamud API 15 / FFXIVClientStructs 環境で独自実装する。

### 法則 7: native hook は fail-closed

判定できない場合は override しない。通常処理へ戻す。

特に `CreateScene` は title scene か曖昧なら差し替えない。

### 法則 8: hook 失敗で plugin 全体を落とさない

signature scan、hook 作成、hook enable、`CurrentLobbyMap` 書き換えに失敗した場合:

- TitleBackground 機能だけ disable する。
- 既存設定値は消さない。
- status に理由を残す。
- plugin の他機能は動かし続ける。

### 法則 9: hook は段階的に有効化する

いきなり `CreateScene` / `FixOn` / `LobbyUpdate` の全差し替えを有効化しない。

推奨順:

1. resolver と status 表示だけ。
2. `LobbyUpdate` hook を logging/status 目的で有効化。
3. `CreateScene` hook を logging/status 目的で有効化。
4. valid `TerritoryPath` のときだけ `CreateScene` path override。
5. 最後に `FixOn` camera/focus/FOV override。

### 法則 10: 実装結果を docs に残す

実装後は必ず以下を作成または更新する。

```text
docs/notes/title-background-implementation-result.md
```

実機未確認のものは未確認と明記する。

## 3. なぜ現実装では背景が変わらないか

現実装が主に触っているもの:

- `AgentLobby.UpdateCharaSelectDisplay`
- `AgentLobby.OpenLoginWaitDialog`
- `AgentLobby.UpdateLoginPosition`
- `LayoutWorld.LoadPrefetchLayout`
- `ClientSelectData.TerritoryType`
- `ClientSelectData.ZoneId`
- `Lobby` sheet の position 逆引き
- `Level` sheet の最寄り row 解決

TitleEdit 型の背景差し替えで必要なもの:

- native `CreateScene`
- native `FixOn`
- native `LobbyUpdate`
- `LobbyCurrentMap`
- raw `TerritoryPath`
- camera position
- focus position
- FOV

したがって、既存の background override は title scene 生成経路に届いていない。これ以上 `LoadPrefetchLayout` や `UpdateLoginPosition` を改良しても、タイトル背景差し替えの本体にはならない。

## 4. Phase 範囲

### Phase 0: 既存 UI の誤解解消

目的:

- 既存 CharaSelect 背景 UI を「背景差し替え」ではなく「preload/診断」として扱う。

やること:

- `SettingsTab` の文言変更。
- 補足文を追加。
- エモート、voice、DC名の動作には触らない。

文言案:

```text
ログイン先エリアの事前読み込み/診断を有効化する（実験）
これは背景画像の任意差し替えではありません。タイトル背景の差し替えは下の「タイトル背景」設定を使います。
```

残してよいもの:

- `ログイン背景position` 診断表示
- prefetch owner の診断

背景差し替え本体として扱わないもの:

- `CharaSelectOverrideTerritoryEnabled`
- `CharaSelectOverrideTerritoryTypeId`
- `CharaSelectOverridePositionEnabled`
- `CharaSelectLevelResolver`
- `CharaSelectLobbyPositionResolver`
- `UpdateLoginPositionDetour`
- `TryPatchOverrideDisplayData`
- `ApplyOverrideTerritoryPrefetch`

### Phase 1: タイトル画面背景差し替え

目的:

- title screen の scene path と camera/focus/FOV を差し替える。

やること:

- `Services/TitleBackground` を追加。
- `Configuration` に `TitleBackground*` 設定を追加。
- `TitleScreenBackgroundService` を追加。
- `TitleBackgroundAddressResolver` を追加。
- `CreateScene` hook を追加。
- `FixOn` hook を追加。
- `LobbyUpdate` hook を追加。
- `LobbyCurrentMap` 書き換えを追加。
- UI に「タイトル背景」セクションを追加。
- validation と status 表示を追加。
- 結果 docs を追加。

やらないこと:

- chara select 画面側の背景差し替え。
- BGM 差し替え。
- 天候固定。
- 時刻固定。
- 現在地とカメラの保存。
- 外部 JSON preset 管理。
- TitleEdit コードコピー。

### Phase 2 以降

Phase 1 が安定してから検討する。

- Phase 2: 現在地とカメラ保存。
- Phase 3: BGM / weather / time。
- Phase 4: chara select 画面側の扱い判断。

## 5. 追加ファイル構成

```text
projects/XIV-Mini-Util/Services/TitleBackground/
  GameLobbyType.cs
  TitleBackgroundAddressResolver.cs
  TitleBackgroundPathHelper.cs
  TitleBackgroundPreset.cs
  TitleBackgroundServiceState.cs
  TitleScreenBackgroundService.cs
```

### `GameLobbyType.cs`

```csharp
internal enum GameLobbyType
{
    Movie = -1,
    Title = 0,
    CharaSelect = 1,
    Aetherial = 2,
    LaNoscea = 3,
    BlackShroud = 4,
    Thanalan = 5,
    Residence = 6,
}
```

UI や保存設定には出さない。hook 判定専用。

### `TitleBackgroundPathHelper.cs`

`TerritoryPath` の正規化と validation path 生成を一箇所に集約する。

```csharp
internal static class TitleBackgroundPathHelper
{
    public static string NormalizeTerritoryPathInput(string? input);
    public static string BuildLvbPath(string normalizedTerritoryPath);
    public static bool IsLikelyValidNormalizedTerritoryPath(string normalizedTerritoryPath);
}
```

正規化ルール:

- `null` は空文字。
- 前後空白を除去。
- `\` を `/` に変換。
- 先頭の `bg/` を除去。
- 末尾の `.lvb` を除去。
- 連続 `/` は可能なら単一化。
- 正規形は `ffxiv/.../level/...`。

invalid の扱い:

- hook 内で差し替えない。
- UI に validation error を表示。
- 設定値は勝手に消さない。

### `TitleBackgroundPreset.cs`

Phase 1 では DTO として用意する。外部 JSON 保存はまだしない。

```csharp
internal sealed class TitleBackgroundPreset
{
    public string Name { get; set; } = string.Empty;
    public string TerritoryPath { get; set; } = string.Empty;

    public float CameraX { get; set; }
    public float CameraY { get; set; }
    public float CameraZ { get; set; }

    public float FocusX { get; set; }
    public float FocusY { get; set; }
    public float FocusZ { get; set; }

    public float FovY { get; set; } = 45f;

    public byte WeatherId { get; set; }
    public ushort TimeOffset { get; set; }
    public string BgmPath { get; set; } = string.Empty;
}
```

### `TitleBackgroundServiceState.cs`

status 表示と docs 記録のため、状態を明示する。

候補:

```csharp
internal enum TitleBackgroundServiceState
{
    Disabled,
    Ready,
    InvalidConfiguration,
    AddressResolveFailed,
    HookCreateFailed,
    HookEnableFailed,
    RuntimeError,
}
```

### `TitleBackgroundAddressResolver.cs`

最低限解決する address:

- `CreateScene`
- `FixOn`
- `LobbyUpdate`
- `LobbyCurrentMap`

後続 Phase で追加する address:

- `RenderCamera` / `CameraBase`
- `PlayMusic`
- `SetTime`
- `WeatherPtrBase`

注意:

- `IntPtr.Zero` で hook を作らない。
- static global に寄せすぎない。
- resolver の失敗は TitleBackground 機能だけ disable。
- signature は現行環境で独自確認する。

### `TitleScreenBackgroundService.cs`

責務:

- address resolver の実行。
- hook 作成、enable、disable、dispose。
- title scene 生成時の `TerritoryPath` override。
- `FixOn` で camera/focus/FOV override。
- `LobbyUpdate` で `_lastLobbyUpdateMapId` 記録。
- `Title <-> CharaSelect` 遷移時の reload skip 回避。
- validation/status 提供。

責務外:

- CharaSelect エモート。
- voice id 補正。
- DC 名記録。
- login wait preload。
- BGM/weather/time。

public method 候補:

```csharp
public void SetEnabled(bool enabled);
public void ApplyFromConfiguration();
public void ClearOverride();
public string GetStatusText();
public bool ValidateCurrentConfiguration(out string errorMessage);
public void Dispose();
```

## 6. Configuration 変更

`Configuration` に追加する。

```csharp
public bool TitleBackgroundOverrideEnabled { get; set; } = false;
public string TitleBackgroundTerritoryPath { get; set; } = string.Empty;

public float TitleBackgroundCameraX { get; set; } = 0f;
public float TitleBackgroundCameraY { get; set; } = 0f;
public float TitleBackgroundCameraZ { get; set; } = 0f;

public float TitleBackgroundFocusX { get; set; } = 0f;
public float TitleBackgroundFocusY { get; set; } = 0f;
public float TitleBackgroundFocusZ { get; set; } = 0f;

public float TitleBackgroundFovY { get; set; } = 45f;

public byte TitleBackgroundWeatherId { get; set; } = 0;
public ushort TitleBackgroundTimeOffset { get; set; } = 0;
public string TitleBackgroundBgmPath { get; set; } = string.Empty;
```

正規化:

- `TitleBackgroundTerritoryPath` は `TitleBackgroundPathHelper.NormalizeTerritoryPathInput()` を通す。
- FOV は Phase 1 では `0.01f` から `180f` 程度に clamp。
- camera/focus は既存の coordinate sanitize 方針に合わせる。
- BGM/weather/time は Phase 1 未使用。設定値は消さない。

更新箇所:

- property 追加。
- `ApplyFrom()`。
- `NormalizeAndMigrate()`。
- export/import snapshot。
- 必要なら `CurrentVersion` bump。

## 7. Hook 設計

### 7.1 `LobbyUpdate` hook

目的:

- 直近の lobby map を記録する。
- `Title <-> CharaSelect` 遷移時に scene reload skip を避ける。

fail-closed:

- `CurrentLobbyMap` が読めない場合、書き換えない。
- original は必ず呼ぶ。
- 例外時は service state を `RuntimeError` にし、可能なら TitleBackground を disable。

疑似コード:

```csharp
private byte LobbyUpdateDetour(GameLobbyType mapId, int time)
{
    try
    {
        var currentMap = TryReadCurrentLobbyMap(out var value)
            ? value
            : (GameLobbyType?)null;

        _lastLobbyUpdateMapId = mapId;

        if (_configuration.TitleBackgroundOverrideEnabled
            && currentMap.HasValue
            && IsTitleCharaSelectTransition(currentMap.Value, mapId))
        {
            TryWriteCurrentLobbyMap(GameLobbyType.Movie);
        }
    }
    catch (Exception ex)
    {
        MarkRuntimeError(ex);
    }

    return _lobbyUpdateHook.Original(mapId, time);
}
```

### 7.2 `CreateScene` hook

目的:

- title scene 生成時だけ original に渡す path を差し替える。

発火条件:

- `TitleBackgroundOverrideEnabled == true`
- service state が `Ready`
- `TerritoryPath` validation 済み
- title scene と判定できる

title 判定は `_lastLobbyUpdateMapId` だけに依存しない。可能なら以下を併用する。

- `_lastLobbyUpdateMapId`
- `CurrentLobbyMap`
- original path
- 直近 hook 呼び出し順

判定できない場合は override しない。

疑似コード:

```csharp
private int CreateSceneDetour(string path, uint p2, nint p3, uint p4, nint p5, int p6, uint p7)
{
    _titleCameraNeedsSet = false;

    if (!ShouldOverrideCreateScene(path))
    {
        return _createSceneHook.Original(path, p2, p3, p4, p5, p6, p7);
    }

    var overridePath = _normalizedTerritoryPath;
    var result = _createSceneHook.Original(overridePath, p2, p3, p4, p5, p6, p7);
    _titleCameraNeedsSet = true;
    return result;
}
```

注意:

- 実際の delegate signature は実装時に確認する。
- `string` marshal で済むか、UTF-8 pointer が必要か確認する。
- UTF-8 pointer を使う場合は original 呼び出し中の buffer lifetime を保証する。
- detour 内で file I/O しない。

### 7.3 `FixOn` hook

目的:

- `CreateScene` path override 後の camera/focus/FOV を差し替える。

発火条件:

- `_titleCameraNeedsSet == true`
- service state が `Ready`
- camera/focus/FOV が valid

疑似コード:

```csharp
private nint FixOnDetour(nint self, float[] cameraPos, float[] focusPos, float fovY)
{
    if (!ShouldOverrideCamera())
    {
        return _fixOnHook.Original(self, cameraPos, focusPos, fovY);
    }

    _titleCameraNeedsSet = false;

    var cameraOverride = new[]
    {
        _configuration.TitleBackgroundCameraX,
        _configuration.TitleBackgroundCameraY,
        _configuration.TitleBackgroundCameraZ,
    };

    var focusOverride = new[]
    {
        _configuration.TitleBackgroundFocusX,
        _configuration.TitleBackgroundFocusY,
        _configuration.TitleBackgroundFocusZ,
    };

    return _fixOnHook.Original(
        self,
        cameraOverride,
        focusOverride,
        _configuration.TitleBackgroundFovY);
}
```

注意:

- 実際の引数型が `float[]` か `float*` か確認する。
- `stackalloc` pointer を delegate 越しに渡す実装は避ける。
- `FixOn` が呼ばれなかった場合、一定時間後に `_titleCameraNeedsSet` を落とす設計を検討する。

## 8. UI 設計

### 8.1 既存 UI の変更

`SettingsTab.DrawCharaSelectSettings()` の既存背景固定セクションを preload/診断扱いにする。

表示例:

```text
ログイン先エリアの事前読み込み/診断を有効化する（実験）
これは背景画像の任意差し替えではありません。タイトル背景の差し替えは下の「タイトル背景」設定を使います。
```

### 8.2 新規「タイトル背景」セクション

表示項目:

```text
タイトル背景
[ ] タイトル画面背景を差し替える（実験）
TerritoryPath: [ ffxiv/.../level/... ]
Camera X [ ] Y [ ] Z [ ]
Focus  X [ ] Y [ ] Z [ ]
FOV Y  [45.0]
[適用]
[解除]
状態: ...
```

Phase 1 では `現在地とカメラを保存` は実装しない。表示するなら disabled にして `Phase 2` と明記する。

UI 操作:

- checkbox: `TitleScreenBackgroundService.SetEnabled(bool)`。
- TerritoryPath 入力: normalize して保存。ただし invalid でも勝手に消さない。
- camera/focus/FOV 入力: sanitize/clamp して保存。
- `[適用]`: `ApplyFromConfiguration()`。
- `[解除]`: enabled false、path/camera/focus は消さないか、消す場合は明示的に `ClearOverride()` の仕様に合わせる。
- status: `GetStatusText()`。

## 9. Plugin 配線

`Plugin` に追加する。

```csharp
private readonly TitleScreenBackgroundService _titleScreenBackgroundService;
```

constructor で作成:

```csharp
_titleScreenBackgroundService = new TitleScreenBackgroundService(
    gameInteropProvider,
    dataManager,
    pluginLog,
    _configuration);
```

必要なら `IFramework` も渡す。

`MainWindow` / `SettingsTab` に渡す。

Dispose:

```csharp
_titleScreenBackgroundService.Dispose();
```

Dispose は冪等にする。

## 10. 実装順序

この順番で進める。

1. Phase 0 UI 文言変更。
2. `TitleBackgroundPathHelper` と純粋 validation ロジック追加。
3. `Configuration` に `TitleBackground*` 設定追加。
4. ゲーム不要テスト追加。
5. `TitleBackgroundAddressResolver` 追加。
6. `TitleScreenBackgroundService` の skeleton 追加。
7. resolver/status だけ配線。
8. `LobbyUpdate` hook を追加し、status/logging 目的で有効化。
9. `CreateScene` hook を追加し、まずは logging/status のみ。
10. valid path のときだけ `CreateScene` override。
11. `FixOn` hook を追加し、camera/focus/FOV override。
12. UI に「タイトル背景」セクション追加。
13. docs に結果記録。
14. テスト/build。

## 11. ゲーム不要テスト

native hook 自体はテストできないため、純粋ロジックをテストする。

追加テスト候補:

- `NormalizeTerritoryPathInput(null)` は空文字。
- `NormalizeTerritoryPathInput(" bg/ffxiv/a/level/a.lvb ")` は `ffxiv/a/level/a`。
- `BuildLvbPath("ffxiv/a/level/a")` は `bg/ffxiv/a/level/a.lvb`。
- invalid path は override 判定 false。
- FOV clamp。
- camera/focus coordinate sanitize。
- `Title <-> CharaSelect` 遷移判定。

既存 `tools/CharaSelectLogicTests` に追加してよい。名前が不自然なら後続で `PluginLogicTests` へ改名するが、Phase 1 では無理に広げない。

## 12. 検証コマンド

基本:

```powershell
dotnet run --project tools\CharaSelectLogicTests\CharaSelectLogicTests.csproj
dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj
dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj -c Release
```

Debug build が以下で失敗する場合:

```text
XivMiniUtil.Dev.deps.json is being used by another process
```

対応:

- Release build 成功を compile gate とする。
- file lock を docs に記録する。
- ロック回避目的で project 配下に `artifacts/obj` を作らない。
- ユーザー許可なしにゲーム/Dalamud プロセスを終了しない。

## 13. 実機確認項目

Phase 1 完了前に確認する。

- plugin load でクラッシュしない。
- title screen 表示でクラッシュしない。
- override OFF で通常背景。
- invalid `TerritoryPath` で plugin 全体が落ちない。
- valid `TerritoryPath` で title 背景が変わる。
- camera/focus/FOV が反映される。
- Title -> CharaSelect -> Title で古い背景が残らない。
- plugin unload で hook が dispose される。

ログ観点:

- hook 初期化成否。
- `LobbyUpdate` 呼び出し順。
- `LobbyUpdate mapId`。
- `CurrentLobbyMap` 読み書き結果。
- `CreateScene` の original path。
- override path。
- `FixOn` 呼び出し有無。
- camera/focus/FOV override 実行有無。
- invalid path 時の挙動。
- hook dispose 結果。

ログや docs では、キャラクター名、ワールド名、個人名、ローカルパスなどを伏せる。

## 14. 結果 docs

実装後に作成する。

```text
docs/notes/title-background-implementation-result.md
```

必須記載:

- 実装した Phase。
- 変更ファイル一覧。
- ゲーム不要テスト結果。
- build 結果。
- 実機確認結果。
- 未実装項目。
- known risk。
- 次にやること。

## 15. リスクと対策

### Signature drift

client update で signature が変わる可能性がある。

対策:

- TitleBackground だけ disable。
- status に失敗理由。
- plugin 全体は継続。

### Hook signature mismatch

delegate signature が違うとクラッシュする。

対策:

- 実装前に現行 API15 の型公開を再確認。
- hook は段階有効化。
- 判定できない場合は override しない。

### Invalid scene path

存在しない LVB を渡すと scene load が失敗する可能性がある。

対策:

- `IDataManager.FileExists($"bg/{path}.lvb")` で事前検証。
- invalid なら hook 内で差し替えない。

### Existing emote regression

UI/Plugin 配線変更で emote が退行する可能性がある。

対策:

- emote 経路には触らない。
- `CharaSelectLogicTests` を必ず通す。

### persisted enabled の起動リスク

保存済み設定で `TitleBackgroundOverrideEnabled == true` のまま起動する可能性がある。

対策:

- resolver/hook 初期化失敗時は TitleBackground だけ disable。
- path/camera/focus/FOV は消さない。
- status に理由を残す。

## 16. Phase 1 完了条件

Phase 1 は以下をすべて満たして完了。

- `TitleScreenBackgroundService` が `CharaSelectService` から独立している。
- `CreateScene` / `FixOn` / `LobbyUpdate` / `LobbyCurrentMap` の責務が分離されている。
- `TerritoryTypeId` ではなく raw `TerritoryPath` を使っている。
- `TerritoryPath` の normalize/validation helper がある。
- camera position / focus position / FOV を設定できる。
- invalid path では差し替えない。
- title 判定は fail-closed。
- hook 初期化失敗時に plugin 全体が落ちない。
- `LoadPrefetchLayout` / `UpdateLoginPosition` を背景差し替え本体として扱っていない。
- エモート複数スロットが退行していない。
- ゲーム不要テストが通る。
- Release build が通る。
- docs に実装結果と未確認範囲が残っている。

## 17. Codex に渡す最終実装プロンプト

```text
XIV Mini Util のタイトル背景差し替えを Phase 0 / Phase 1 の範囲で実装してください。

最優先ルール:
- エモート複数スロットを壊さない。
- CharaSelectService に背景差し替えをさらに詰め込まない。
- TitleScreenBackgroundService を新設して背景差し替えを分離する。
- LoadPrefetchLayout / UpdateLoginPosition / ClientSelectData 差し替えは背景差し替え本体として扱わない。
- Phase 1 は title screen のみ対象にする。
- TitleEditPlugin の GPL-3.0 コードはコピーしない。
- hook 失敗時は plugin 全体ではなく TitleBackground だけ disable する。
- hook は段階的に有効化する。
- CreateScene の title 判定は fail-closed にする。
- TerritoryPath の正規化と validation helper を作る。
- 実装結果と未確認範囲を docs に残す。

実装範囲:
1. 既存 CharaSelect 背景 UI を preload/診断文言へ変更する。
2. Configuration に TitleBackgroundOverrideEnabled, TitleBackgroundTerritoryPath, camera position, focus position, FOV, 将来用 BGM/weather/time を追加する。
3. Configuration の NormalizeAndMigrate / ApplyFrom / import-export に新設定を反映する。
4. Services/TitleBackground/ を新設する。
5. TitleBackgroundPathHelper を追加し、TerritoryPath の normalize / BuildLvbPath / validation を集約する。
6. TitleBackgroundAddressResolver を追加し、CreateScene / FixOn / LobbyUpdate / LobbyCurrentMap を現行 API15 環境で独自に解決する。
7. TitleScreenBackgroundService を追加し、resolver, status, hook lifecycle, fail-closed override を管理する。
8. LobbyUpdate hook で直近 mapId 記録と Title <-> CharaSelect の reload skip 回避を行う。
9. CreateScene hook で title scene と判定でき、valid TerritoryPath のときだけ path を差し替える。
10. FixOn hook で _titleCameraNeedsSet のときだけ camera/focus/FOV を差し替える。
11. UI に「タイトル背景」セクションを追加し、enabled, TerritoryPath, camera, focus, FOV, status, apply/clear を操作できるようにする。
12. docs/notes/title-background-implementation-result.md に実装結果、検証結果、未確認範囲を記録する。

Phase 1 でやらないこと:
- chara select 画面側の背景差し替え
- BGM 差し替え
- 天候固定
- 時刻固定
- 現在地とカメラ保存
- 外部 JSON preset 管理

検証:
- dotnet run --project tools\CharaSelectLogicTests\CharaSelectLogicTests.csproj
- dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj
- dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj -c Release

Debug build が XivMiniUtil.Dev.deps.json の file lock で失敗する場合は、Release build 成功を compile gate とし、file lock を docs に記録してください。
```

## 18. 2026-05-06 実装済み範囲と次回前提

### 実装済み

以下は実装済み。

- Phase 0: 既存 CharaSelect 背景系 UI を「ログイン先エリア preload / 診断」扱いへ文言変更。
- `Configuration` への `TitleBackground*` 設定追加。
- `TitleBackgroundPathHelper` 追加。
- `TitleBackgroundPreset` 追加。
- `TitleBackgroundServiceState` 追加。
- `GameLobbyType` / `GameLobbyTypeHelper` 追加。
- `TitleBackgroundAddressResolver` 追加。
- `TitleScreenBackgroundService` 追加。
- `Plugin` / `MainWindow` / `SettingsTab` への service 配線。
- UI に「タイトル背景」セクション追加。
- `/xmutbgdiag` / `/xmutbg` 診断コマンド追加。
- `IPluginLog` への TitleBackground 状態ログ追加。
- `tools/CharaSelectLogicTests` への TitleBackground 純粋ロジックテスト追加。
- `docs/notes/title-background-implementation-result.md` 作成。

検証済み:

- `dotnet run --project tools\CharaSelectLogicTests\CharaSelectLogicTests.csproj`
- `dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj -c Release`
- 実機 `/xmutbg` で signature 未投入時の fail-closed。
- 実機 `/xmutbg` で invalid `TerritoryPath` 時の fail-closed。

実機ログで確認済みの期待状態:

```text
TitleBackground status: 状態: 無効 (hook unavailable) - CreateScene signature is not configured.
enabled=False
hooksReady=False
addresses: createScene=zero, fixOn=zero, lobbyUpdate=zero, currentMap=zero
```

invalid path 時:

```text
state=InvalidConfiguration
enabled=False
validatedPath=none
validationError=TerritoryPath は ffxiv/.../level/... 形式で指定してください。
```

### 次回作業の前提

現時点では `TitleBackgroundAddressResolver` の signature は未投入。  
したがって、背景はまだ変わらない。これは異常ではなく、意図した fail-closed 状態。

次回は「背景差し替えを実際に動かす実装」ではなく、まず native hook の現行 client 対応を行う。

## 19. 次回からの実装計画

グラフィカル確認用:

- `docs/notes/codex-title-background-plan-dashboard.html`

### 進捗チェック

凡例:

- `[x]`: コード実装済み、ビルド/ゲーム不要テスト確認済み。
- `[~]`: 実装の受け口または一部コードは実装済み、実機または現行 client 情報待ち。
- `[ ]`: 未実装または実機確認後に着手。

現在の進捗:

- `[~]` Step 1: signature / ABI 独自確認
  - UI と設定に signature 入力欄を追加済み。
  - resolver は設定値から signature を読むよう実装済み。
  - 現行 client の実 signature / ABI は未確認。
- `[~]` Step 2: resolver-only / status-only 実機確認
  - `TitleBackgroundRuntimeMode.ResolveOnly` 実装済み。
  - `address再解決`、`signaturesConfigured`、`/xmutbg` 診断拡張は実装済み。
  - 実機で address non-zero / `resolverError=none` は未確認。
- `[~]` Step 3: `LobbyUpdate` hook logging-only
  - `HookLoggingOnly` mode と logging-only 分岐は実装済み。
  - signature 解決後の実機ログ確認は未実施。
- `[~]` Step 4: `LobbyUpdate` reload skip 回避
  - `Override` mode のときだけ `CurrentLobbyMap = Movie` を書く guard は実装済み。
  - 実機で Title <-> CharaSelect 遷移と副作用は未確認。
- `[~]` Step 5: `CreateScene` hook logging-only
  - `HookLoggingOnly` mode の `CreateScene observed` log は実装済み。
  - 実機で original path / title 判定材料は未確認。
- `[~]` Step 6: `CreateScene` path override
  - `Override` mode、valid path、title 判定時だけ path を置換するコードは実装済み。
  - 実機で背景差し替え成功は未確認。
- `[~]` Step 7: `FixOn` hook logging-only
  - `HookLoggingOnly` mode の `FixOn observed` log は実装済み。
  - 実機で呼び出し有無は未確認。
- `[~]` Step 8: `FixOn` camera / focus / FOV override
  - `_titleCameraNeedsSet` と `Override` mode guard による camera/focus/FOV 置換コードは実装済み。
  - 実機で反映結果は未確認。
- `[ ]` Step 9: runtime evidence で ship/no-ship 判断
  - 実機確認ログが揃ってから実施する。

直近の確認済み:

- `dotnet run --project tools\CharaSelectLogicTests\CharaSelectLogicTests.csproj`
- `dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj`
- `dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj -c Release`

### Step 1: 現行 client の signature / ABI を独自確認する

状態: `[~]` 受け口実装済み / 現行 client の実 signature と ABI は未確認。

目的:

- GPL-3.0 実装をコピーせず、現行 Dalamud API 15 / FFXIVClientStructs 環境で address と ABI を確定する。

確認対象:

- `CreateScene`
- `FixOn`
- `LobbyUpdate`
- `LobbyCurrentMap`

確認する型:

- `CreateScene`
  - path 引数が UTF-8 pointer か。
  - 戻り値が `int` でよいか。
  - 残り引数の個数と型。
  - calling convention。
- `FixOn`
  - `self` が `nint` でよいか。
  - camera / focus が `float*` でよいか。
  - FOV 引数の型が `float` でよいか。
  - 戻り値が `nint` でよいか。
  - calling convention。
- `LobbyUpdate`
  - `mapId` が `short` / `int` / enum のどれか。
  - `time` の型。
  - 戻り値が `byte` でよいか。
  - calling convention。
- `LobbyCurrentMap`
  - static address の指す型が `short` でよいか。
  - `Movie = -1` の書き込みが期待どおりか。

完了条件:

- address が `/xmutbg` で zero ではなくなる。
- resolverError が `none` になる。
- まだ override は行わず、status/logging だけで安全に動く。

### Step 2: resolver-only / status-only 実機確認

状態: `[~]` `ResolveOnly` と診断 UI は実装済み / 実機 address 解決は未確認。

実装:

- `TitleBackgroundAddressResolver` に確認済み signature を入れる。
- hook 作成を一時的に無効化できる保険を入れる場合は、設定か定数で resolver-only mode を用意する。

確認:

```text
/xmutbg
```

期待:

- `addresses` が non-zero。
- `hooksReady` は resolver-only なら false、hook 作成済みなら true。
- plugin load / unload でクラッシュしない。

記録:

- `docs/notes/title-background-implementation-result.md` に address 解決結果を追記する。
- 実機ログからローカルパス、キャラクター名、ワールド名を残さない。

### Step 3: `LobbyUpdate` hook logging-only

状態: `[~]` `HookLoggingOnly` の logging-only 分岐は実装済み / 実機 hook 発火は未確認。

実装:

- `LobbyUpdateDetour` を有効化。
- まず `CurrentLobbyMap` 書き換えはしない。
- `mapId`、`time`、`CurrentLobbyMap` read result を log に出す。

期待ログ:

```text
TitleBackground: LobbyUpdate observed. current=..., next=..., time=...
```

確認:

- title screen 表示。
- Title -> CharaSelect。
- CharaSelect -> Title。
- plugin unload。

完了条件:

- mapId が想定 enum と一致する。
- ABI mismatch らしいクラッシュや不正値が出ない。
- CharaSelect emote が退行しない。

### Step 4: `LobbyUpdate` reload skip 回避を有効化

状態: `[~]` `Override` mode 限定の書き換え guard は実装済み / 実機遷移確認は未実施。

実装:

- `TitleBackgroundOverrideEnabled == true` のときだけ、Title <-> CharaSelect 遷移で `CurrentLobbyMap = Movie` を書く。
- `TryReadCurrentLobbyMap()` / `TryWriteCurrentLobbyMap()` の失敗は warning に留め、original は必ず呼ぶ。

確認:

- override OFF では書き換えない。
- override ON かつ Title <-> CharaSelect 遷移だけ書き換える。
- `/xmutbg` の `lastLobbyUpdate` が更新される。

### Step 5: `CreateScene` hook logging-only

状態: `[~]` `HookLoggingOnly` の観測ログは実装済み / 実機 original path は未確認。

実装:

- `CreateSceneDetour` を有効化。
- まず path override はしない。
- original path、last lobby、current lobby map を log に出す。

期待ログ:

```text
TitleBackground: CreateScene observed. originalPath=..., lastLobby=..., currentMap=...
```

確認:

- title scene 生成時の original path が `ffxiv/.../level/...` 形式か。
- CharaSelect 側や movie 側で誤判定しないか。

完了条件:

- title 判定に使える条件が揃う。
- 判定できないケースは override しない。

### Step 6: `CreateScene` path override を有効化

状態: `[~]` `Override` mode 限定の path override は実装済み / 実機背景差し替えは未確認。

実装:

- `ShouldOverrideCreateScene()` が true のときだけ、original に渡す path を `_validatedTerritoryPath` に置換する。
- `IDataManager.FileExists("bg/{path}.lvb")` 済みの path だけ使う。
- detour 内で file I/O しない。

確認:

- override OFF: 通常背景。
- override ON + invalid path: override しない。
- override ON + valid path: title 背景が差し替わるか。
- CreateScene override log が出る。

失敗時:

- runtime error は TitleBackground だけ disable。
- original 呼び戻しを優先する。

### Step 7: `FixOn` hook logging-only

状態: `[~]` `HookLoggingOnly` の FOV 観測ログは実装済み / 実機呼び出し有無は未確認。

実装:

- `FixOnDetour` を有効化。
- まず camera / focus / FOV override はしない。
- `FixOn` が呼ばれた事実と FOV を log に出す。

確認:

- `CreateScene` override 後に `FixOn` が呼ばれるか。
- 呼ばれない場合、camera 反映方法の再調査へ戻る。

### Step 8: `FixOn` camera / focus / FOV override を有効化

状態: `[~]` `Override` mode 限定の camera/focus/FOV override は実装済み / 実機反映は未確認。

実装:

- `_titleCameraNeedsSet == true` のときだけ camera / focus / FOV を置換。
- `fixed` buffer を original 呼び出し中だけ保持する。
- override 後 `_titleCameraNeedsSet = false`。

確認:

- camera position 反映。
- focus position 反映。
- FOV 反映。
- title 以外で反映されない。

### Step 9: runtime evidence で ship/no-ship 判断

状態: `[ ]` 未実施。signature 解決、logging-only、override の実機 evidence が揃ってから判断する。

必要な実機確認:

- plugin load でクラッシュしない。
- title screen 表示でクラッシュしない。
- override OFF で通常背景。
- invalid `TerritoryPath` で plugin 全体が落ちない。
- valid `TerritoryPath` で title 背景が変わる。
- camera / focus / FOV が反映される。
- Title -> CharaSelect -> Title で古い背景が残らない。
- plugin unload で hook が dispose される。
- CharaSelect emote replay が退行していない。

確認後:

- `docs/notes/title-background-implementation-result.md` を更新。
- `dotnet run --project tools\CharaSelectLogicTests\CharaSelectLogicTests.csproj`
- `dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj`
- `dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj -c Release`

## 20. 最終実装内容

Phase 1 の最終到達点:

- タイトル画面だけを対象にする。
- CharaSelect 画面側の背景差し替えはしない。
- BGM / weather / time は設定値のみ保持し、動作はしない。
- 現在地とカメラ保存はしない。
- 外部 JSON preset 管理はしない。
- `TitleScreenBackgroundService` が `CharaSelectService` から独立している。
- `CreateScene` / `FixOn` / `LobbyUpdate` / `LobbyCurrentMap` の責務が分離されている。
- raw `TerritoryPath` を主キーにする。
- `IDataManager.FileExists("bg/{path}.lvb")` で validation する。
- camera position / focus position / FOV を UI から指定できる。
- invalid path では override しない。
- signature / hook / runtime error では TitleBackground だけ disable する。
- 保存済み path / camera / focus / FOV は勝手に消さない。
- 実行フラグだけ fail-closed で false に落とす。
- `/xmutbg` で診断を取れる。
- `IPluginLog` で hook lifecycle と override 実行点を追える。
- CharaSelect emote 複数スロットが退行していない。
- ゲーム不要テストと Release build が通る。
- 実機確認結果が docs に残っている。

Phase 2 以降:

- Phase 2: 現在地とカメラ保存。
- Phase 3: BGM / weather / time。
- Phase 4: chara select 画面側の扱い判断。
