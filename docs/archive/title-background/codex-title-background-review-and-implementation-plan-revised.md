<!-- Path: docs/notes/codex-title-background-review-and-implementation-plan-revised.md -->
<!-- Description: タイトル背景差し替え計画のレビュー反映版 -->
<!-- Reason: TitleEdit/HaselTweaks調査と現実装レビューを踏まえ、次の実装作業で迷わない粒度に落とすため -->
# Codex実装依頼メモ: タイトル背景差し替え レビュー反映版

作成日: 2026-05-06

元ファイル:

- `docs/notes/codex-title-background-review-and-implementation-plan.md`

関連調査:

- `docs/notes/chara-select-title-background-research.md`
- `docs/notes/chara-select-followup-implementation-result.md`

前提 commit:

- `3b60e08f72626462b1a66cf003be2d0e189bd931`

## 1. 結論

エモート複数スロットと DC 名記録は現状維持でよい。

一方、背景固定/差し替えは現実装の延長では実現しない可能性が高い。現実装は `LoadPrefetchLayout`、`UpdateLoginPosition`、`ClientSelectData.TerritoryType` 差し替えを中心にしているが、TitleEdit 型の背景差し替えは native title scene 生成経路の `CreateScene` に渡る raw `TerritoryPath` を置換する。

したがって、次の実装では以下を明確に分離する。

- `CharaSelectService`: エモート、voice、DC名、ログイン先 preload/診断のみ。
- `TitleScreenBackgroundService`: タイトル画面背景差し替えのみ。

Phase 1 では「タイトル画面」だけを対象にし、キャラ選択画面背景の任意差し替えは扱わない。

## 2. レビュー反映ポイント

### 2.1 `LoadPrefetchLayout` を背景差し替え本体にしない

`LayoutWorld.LoadPrefetchLayout` はログイン先 territory の preload には使えるが、タイトル/ロビー scene の生成 path を置換しない。背景差し替えの主経路として扱わない。

対応:

- 既存 UI の文言を「背景差し替え」から「事前読み込み/診断」に降格する。
- `CharaSelectOverrideTerritoryEnabled` 系は当面残してもよいが、TitleBackground とは独立させる。
- `UpdateLoginPosition` と `Lobby` sheet 逆引きは、背景差し替え本体としては使わない。

### 2.2 `TerritoryTypeId` だけでは不十分

TitleEdit 型の実装では `TerritoryType.Bg` に相当する raw path、つまり `ffxiv/.../level/...` 形式の `TerritoryPath` を `CreateScene` に渡す。

対応:

- 新設定は `TitleBackgroundTerritoryPath` を中心にする。
- `TerritoryTypeId` は現在地保存時に `TerritoryType.Bg` を解決するための入力に留める。
- UI の主入力は `TerritoryPath` とする。

### 2.3 X/Y/Z だけではカメラが決まらない

ユーザー要望の「任意マップの特定地点」は、単一の X/Y/Z では定義できない。背景として見せるには以下が必要。

- camera position
- focus position
- FOV
- territory path

対応:

- Phase 1 は手入力で camera/focus/FOV を設定できるようにする。
- Phase 2 で「現在地と現在カメラを保存」を追加する。
- `CharaSelectOverridePositionX/Y/Z` は TitleBackground の camera/focus とは別物として扱う。

### 2.4 GPL-3.0 コードをコピーしない

TitleEditPlugin は GPL-3.0。実装方針の参考にはできるが、コード、signature resolver、preset JSON、parser をそのまま移植しない。

対応:

- signature scan は現行 API15 環境で独自確認する。
- delegate signature は実装時に現行 client で確認する。
- 実装コメントにも TitleEdit からのコピーを示す表現は入れない。

### 2.5 失敗時は plugin 全体を落とさない

native hook と signature scan は壊れやすい。失敗時に plugin 全体を disable すると、既存のエモートや他機能まで巻き込む。

対応:

- `TitleScreenBackgroundService` の初期化失敗は `TitleBackgroundOverrideEnabled = false` にして service 内で閉じる。
- 例外は `IPluginLog` に出す。
- `CharaSelectService`、Materia、Desynth など既存機能は動かし続ける。

## 3. 現状整理

### 3.1 維持するもの

以下は今回の背景実装では壊さない。

- `CharaSelectEmotePresetStore`
- `CharaSelectEmotePresets`
- `CharaSelectActiveEmotePresetIndexes`
- `CharaSelectLastRecordedEmotes`
- voice id 補正
- DC 名記録
- `tools/CharaSelectLogicTests` の emote preset テスト

### 3.2 降格または後で削除候補

以下は「背景差し替え本体」ではないため、TitleBackground 実装の成功後に削除または診断用途へ縮小する。

- `CharaSelectOverrideTerritoryEnabled`
- `CharaSelectOverrideTerritoryTypeId`
- `CharaSelectOverridePositionEnabled`
- `CharaSelectOverridePositionX/Y/Z`
- `CharaSelectLevelResolver`
- `CharaSelectLobbyPositionResolver`
- `UpdateLoginPositionDetour`
- `TryPatchOverrideDisplayData`
- `ApplyOverrideTerritoryPrefetch`

ただし、Phase 1 開始時点で一気に削除しない。まず UI 文言変更と責務分離を行い、エモート退行を避ける。

## 4. 新規サービス設計

### 4.1 追加ディレクトリ

```text
projects/XIV-Mini-Util/Services/TitleBackground/
  GameLobbyType.cs
  TitleBackgroundAddressResolver.cs
  TitleBackgroundPreset.cs
  TitleBackgroundServiceState.cs
  TitleScreenBackgroundService.cs
```

`TitleBackgroundConfig.cs` は作らず、永続設定は既存 `Configuration` に追加する。設定正規化と import/export 連携を既存パターンに合わせるため。

### 4.2 `TitleScreenBackgroundService`

責務:

- title background 用 native address の解決。
- `CreateScene` / `FixOn` / `LobbyUpdate` hook の作成、enable/disable、dispose。
- title scene 生成時だけ `TerritoryPath` を置換。
- 直後の `FixOn` で camera/focus/FOV を置換。
- title/chara select 遷移時の scene reload skip 回避。
- hook 失敗時に title background 機能だけ無効化。

責務外:

- CharaSelect エモート再生。
- ログイン先 preload。
- `ClientSelectData` 差し替え。
- DC 名記録。
- BGM/weather/time の Phase 1 実装。

### 4.3 `TitleBackgroundAddressResolver`

最低限の address:

- `CreateScene`
- `FixOn`
- `LobbyUpdate`
- `LobbyCurrentMap`

後続 Phase:

- `RenderCamera` / `CameraBase`
- `PlayMusic`
- `SetTime`
- `WeatherPtrBase`

実装ルール:

- `IGameInteropProvider` / `ISigScanner` のどちらを使うかは既存 Dalamud API15 の注入可能サービスに合わせる。
- signature scan 失敗時は service initialization を失敗扱いにする。
- `IntPtr.Zero` address で hook を作らない。
- resolver は static global 状態にしすぎず、service の lifetime に合わせて保持する。

### 4.4 `GameLobbyType`

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

Title/chara select 遷移判定だけに使う。UI 表示や設定値には出さない。

### 4.5 `TitleBackgroundPreset`

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

Phase 1 では DTO は UI/設定転記用。外部 JSON preset 管理はまだ実装しない。

## 5. Configuration 追加

`Configuration` に以下を追加する。

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

- `TitleBackgroundTerritoryPath = TitleBackgroundTerritoryPath?.Trim() ?? string.Empty`
- `TitleBackgroundFovY` は Phase 1 では `0.01f` から `180f` 程度に clamp。
- camera/focus は既存 `SanitizeCoordinate()` を流用してよい。
- `WeatherId` / `TimeOffset` / `BgmPath` は Phase 1 未使用でも import/export に含めるか、追加だけして UI では「未使用」と明記する。

`ApplyFrom()`、`NormalizeAndMigrate()`、export/import の対象に忘れず含める。

## 6. Hook 詳細

### 6.1 CreateScene hook

目的:

- title scene 生成時だけ、original に渡す path を `TitleBackgroundTerritoryPath` に置換する。

発火条件:

- `TitleBackgroundOverrideEnabled == true`
- `_lastLobbyUpdateMapId == GameLobbyType.Title`
- `TitleBackgroundTerritoryPath` が空でない
- path validation が成功している

疑似コード:

```csharp
private int CreateSceneDetour(string path, uint p2, nint p3, uint p4, nint p5, int p6, uint p7)
{
    _titleCameraNeedsSet = false;

    if (ShouldOverrideTitleScene())
    {
        var overridePath = _configuration.TitleBackgroundTerritoryPath;
        var result = _createSceneHook.Original(overridePath, p2, p3, p4, p5, p6, p7);
        _titleCameraNeedsSet = true;
        return result;
    }

    return _createSceneHook.Original(path, p2, p3, p4, p5, p6, p7);
}
```

注意:

- 実際の delegate signature は現行 client で必ず確認する。
- `string` marshal で済むか、UTF-8 pointer が必要かは hook 作成時に検証する。
- UTF-8 pointer を使う場合、original 呼び出し中に buffer が生存するよう固定する。
- `CreateScene` detour 内で重い file I/O はしない。設定値と validation 結果を事前に持つ。

### 6.2 FixOn hook

目的:

- 差し替えた title scene の camera/focus/FOV を設定する。

発火条件:

- `_titleCameraNeedsSet == true`
- `TitleBackgroundOverrideEnabled == true`
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

- 実際の detour 引数が `float[]` か `float*` かは現行環境で確認する。
- `stackalloc` pointer を delegate 越しに渡す実装は避ける。必要なら fixed buffer で original 呼び出し中の lifetime を明確にする。
- `FixOn` が呼ばれなかった場合、一定時間後に `_titleCameraNeedsSet` を落とす仕組みを検討する。

### 6.3 LobbyUpdate hook

目的:

- `_lastLobbyUpdateMapId` を更新する。
- `Title <-> CharaSelect` 遷移時に scene reload skip を避ける。

疑似コード:

```csharp
private byte LobbyUpdateDetour(GameLobbyType mapId, int time)
{
    var currentMap = ReadCurrentLobbyMap();
    var isTitleToCharaSelect = currentMap == GameLobbyType.Title && mapId == GameLobbyType.CharaSelect;
    var isCharaSelectToTitle = currentMap == GameLobbyType.CharaSelect && mapId == GameLobbyType.Title;

    _lastLobbyUpdateMapId = mapId;

    if (_configuration.TitleBackgroundOverrideEnabled
        && (isTitleToCharaSelect || isCharaSelectToTitle))
    {
        WriteCurrentLobbyMap(GameLobbyType.Movie);
    }

    return _lobbyUpdateHook.Original(mapId, time);
}
```

注意:

- `_lastLobbyUpdateMapId` は「次の CreateScene が何の scene か」を判定するために必要。
- `CurrentLobbyMap` 書き換えは override enabled のときだけに限定する。
- 書き換えに失敗した場合はログを出し、original は必ず呼ぶ。

### 6.4 Validation

`TitleBackgroundTerritoryPath` は、可能なら `IDataManager.FileExists($"bg/{path}.lvb")` で検証する。

失敗時:

- UI に「LVB が見つからない」と表示。
- hook 内では override しない。
- 設定自体は消さない。

## 7. UI 計画

### 7.1 既存 CharaSelect 背景 UI の変更

現状の文言:

```text
キャラ選択画面に表示するエリアを固定する（実験）
表示地点を指定する
```

変更後:

```text
ログイン先エリアの事前読み込み/診断を有効化する（実験）
```

補足文:

```text
これは背景画像の任意差し替えではありません。タイトル背景の差し替えは下の「タイトル背景」設定を使います。
```

`ログイン背景position` 表示は、診断用途なら残してよい。

### 7.2 新規 UI: タイトル背景

`SettingsTab.DrawCharaSelectSettings()` の末尾か、別メソッド `DrawTitleBackgroundSettings()` に分離して表示する。

最小 UI:

```text
タイトル背景
[ ] タイトル画面背景を差し替える（実験）
TerritoryPath: [ ffxiv/.../level/...             ]
Camera X [      ] Y [      ] Z [      ]
Focus  X [      ] Y [      ] Z [      ]
FOV Y  [ 45.0 ]
[適用]
[解除]
状態: 有効 / 無効 / LVB未検出 / hook初期化失敗
```

Phase 1 では `現在地とカメラを保存` は表示しないか、disabled で「Phase 2」と明記する。

### 7.3 UI から service への操作

追加 public methods 候補:

```csharp
public void SetEnabled(bool enabled);
public void ApplyFromConfiguration();
public void ClearOverride();
public string GetStatusText();
public bool ValidateCurrentConfiguration(out string errorMessage);
```

設定値の保存は UI 側で直接 `Configuration` を書くか、service 経由にするか統一する。既存 `CharaSelectService` と同じパターンに寄せるなら、service 経由が望ましい。

## 8. Plugin.cs 配線

`Plugin` constructor に `TitleScreenBackgroundService` を追加する。

必要な注入候補:

- `IGameInteropProvider`
- `IDataManager`
- `IPluginLog`
- `Configuration`
- `IFramework` は Phase 1 では必須ではないが、camera pending timeout を入れるなら必要。

例:

```csharp
_titleScreenBackgroundService = new TitleScreenBackgroundService(
    gameInteropProvider,
    dataManager,
    pluginLog,
    _configuration);
```

`MainWindow` / `SettingsTab` に service を渡す。

Dispose:

- `Plugin.Dispose()` で `_titleScreenBackgroundService.Dispose()` を呼ぶ。
- hook dispose は service 内で冪等にする。

## 9. Phase 分割

### Phase 0: 既存背景 UI の誤解解消

やること:

- CharaSelect 背景固定 UI を preload/診断文言へ変更。
- docs に「背景差し替え本体ではない」と明記。
- エモート、voice、DC名は触らない。

検証:

```powershell
dotnet run --project tools\CharaSelectLogicTests\CharaSelectLogicTests.csproj
dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj -c Release
```

完了条件:

- UI 文言で「背景差し替え」と誤認しない。
- エモート系テストが通る。

### Phase 1: タイトル背景差し替え

やること:

- `Services/TitleBackground` 追加。
- `Configuration` に `TitleBackground*` 設定追加。
- `TitleScreenBackgroundService` を Plugin に配線。
- `CreateScene` hook 追加。
- `FixOn` hook 追加。
- `LobbyUpdate` hook 追加。
- `LobbyCurrentMap` 書き換え追加。
- UI に Title Background 設定追加。
- validation と status 表示追加。

やらないこと:

- BGM 差し替え。
- 天候固定。
- 時刻固定。
- 現在地/カメラ保存。
- キャラ選択画面側の背景差し替え。
- TitleEdit コードコピー。

ゲーム不要検証:

```powershell
dotnet run --project tools\CharaSelectLogicTests\CharaSelectLogicTests.csproj
dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj
dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj -c Release
```

Debug build が dev plugin に掴まれて失敗する場合:

- エラーが `XivMiniUtil.Dev.deps.json` file lock のみなら、Release build を compile gate とする。
- file lock は docs の未確認範囲に明記する。
- ロック回避目的で project 配下に `artifacts/obj` を作らない。

実機確認:

- plugin load でクラッシュしない。
- title screen 表示でクラッシュしない。
- override OFF で通常背景。
- override ON + valid `TerritoryPath` でタイトル背景が変わる。
- camera/focus/FOV が反映される。
- Title -> CharaSelect -> Title で背景が古いまま残らない。
- invalid `TerritoryPath` で plugin 全体が落ちない。
- plugin unload で hook が dispose される。

### Phase 2: 現在地とカメラ保存

やること:

- ログイン中の `ClientState.TerritoryType` から `TerritoryType.Bg` を取得。
- render camera の eye position を取得。
- render camera の向きから focus point を計算。
- FOV を取得、または UI 入力値を使う。
- `[現在地とカメラを保存]` UI を追加。

注意:

- API15 で render camera が typed API で取れるか再確認する。
- typed API がない場合、camera address scan は Phase 2 の resolver に追加する。
- 取れない場合は Phase 2 を未実装扱いにしてよい。

### Phase 3: BGM / Weather / Time

やること:

- `PlayMusic` hook または `BGMSystem` でタイトル BGM 差し替え。
- weather pointer または `WeatherManager` で天候固定。
- `SetTime` delegate で時刻固定。

注意:

- 一度の書き換えでは戻される可能性がある。
- 短時間だけ再適用するなら cancellation と dispose を厳密にする。
- Phase 1 が安定するまで着手しない。

### Phase 4: キャラ選択画面側の扱い判断

やること:

- TitleEdit 調査では CharaSelect 分岐は path 置換ではなく camera 初期化のみだった点を踏まえる。
- キャラ選択画面側も背景差し替えしたい場合、Title 分岐とは別の安全性評価を行う。
- キャラ表示、character list、emote replay と競合しないか実機で確認する。

Phase 1 の範囲外。

## 10. テスト追加方針

ゲーム不要テストで直接 native hook は検証できない。代わりに純粋ロジックを増やす。

追加候補:

- `TitleBackgroundPreset` の validation。
- `TitleBackgroundTerritoryPath` の trim/empty 判定。
- FOV clamp。
- camera/focus coordinate sanitize。
- `GameLobbyType` 遷移判定。
- invalid path のとき override しない判定。

`tools/CharaSelectLogicTests` に入れるか、名前を広げて `tools/PluginLogicTests` にするかは実装時に判断する。既存テストを壊さないなら、当面は `CharaSelectLogicTests` に TitleBackground 純粋ロジックテストを追加してもよい。

## 11. リスクと対策

### Signature drift

リスク:

- client update で signature が変わり hook 初期化に失敗する。

対策:

- TitleBackground だけ disable。
- status text に失敗 reason を出す。
- plugin 全体は継続。

### Hook signature mismatch

リスク:

- delegate signature が実際の native 関数と違うとクラッシュする。

対策:

- 実装前に現行 FFXIVClientStructs と Dalamud API15 の型公開を再確認。
- 最小 hook から一つずつ有効化。
- まず `LobbyUpdate` status logging、次に `CreateScene` path logging、最後に `FixOn` camera override の順で実機検証する。

### Invalid scene path

リスク:

- 存在しない LVB を渡して scene load が失敗する。

対策:

- `IDataManager.FileExists($"bg/{path}.lvb")` で事前検証。
- invalid なら hook 内で置換しない。

### Existing emote regression

リスク:

- Plugin 配線や SettingsTab 改修で CharaSelect エモートが退行する。

対策:

- `CharaSelectService` の emote 経路には触らない。
- 既存 `CharaSelectLogicTests` を必ず通す。
- UI 変更は TitleBackground セクション追加と既存文言変更に留める。

### File lock during Debug build

リスク:

- Dalamud/ゲームが Debug output を掴み、`dotnet build` が失敗する。

対策:

- Release build を compile gate として必ず通す。
- Debug build file lock は環境起因として記録。
- 生成物削除やプロセス終了を勝手に行わない。

## 12. 実装順序

1. Phase 0: 既存 CharaSelect 背景 UI を preload/診断表現に変更。
2. `Configuration` に `TitleBackground*` 設定追加、normalize/import/export 対応。
3. TitleBackground 純粋ロジック/validation を追加し、ゲーム不要テストを書く。
4. `TitleBackgroundAddressResolver` を追加する。ただし hook enable はまだしない。
5. `TitleScreenBackgroundService` を追加し、resolver 失敗時 disable の形を作る。
6. `LobbyUpdate` hook を追加し、status/logging だけで検証。
7. `CreateScene` hook を追加し、valid path のときだけ置換。
8. `FixOn` hook を追加し、camera/focus/FOV を反映。
9. Settings UI に Title Background セクションを追加。
10. docs に実装結果、未確認範囲、実機確認結果を記録。

## 13. 実装後に作る結果ファイル

```text
docs/notes/title-background-implementation-result.md
```

記載内容:

- 実装した Phase。
- 変更ファイル一覧。
- ゲーム不要テスト結果。
- build 結果。
- 実機確認結果。
- 未実装項目。
- known risk。
- 次にやること。

## 14. Codex 実装プロンプト

以下をそのまま実装依頼として使える。

```text
XIV Mini Util のタイトル背景差し替えを Phase 0 / Phase 1 の範囲で実装してください。

前提:
- エモート複数スロット機能は壊さないでください。
- 現在の LoadPrefetchLayout / UpdateLoginPosition / ClientSelectData 差し替え方式は、背景差し替え本体として深追いしないでください。
- 背景差し替えは CharaSelectService から分離し、TitleScreenBackgroundService として実装してください。
- Phase 1 は title screen のみ対象です。chara select 画面側の背景差し替え、BGM、weather、time、現在地カメラ保存は後回しです。
- TitleEditPlugin は GPL-3.0 なのでコードコピーは禁止です。仕組みの参考に留め、現行 API15 環境で独自実装してください。
- signature scan / hook 初期化に失敗した場合は plugin 全体を落とさず、TitleBackground 機能だけ disable してください。

実装:
1. 既存の「キャラ選択画面に表示するエリアを固定する（実験）」UIを、ログイン先エリア preload/診断の文言に変更してください。
2. Configuration に TitleBackgroundOverrideEnabled, TitleBackgroundTerritoryPath, camera position, focus position, FOV を追加してください。
3. Configuration の NormalizeAndMigrate / ApplyFrom / import-export に新設定を反映してください。
4. projects/XIV-Mini-Util/Services/TitleBackground/ を新設してください。
5. TitleBackgroundAddressResolver を追加し、CreateScene / FixOn / LobbyUpdate / LobbyCurrentMap を現行環境で独自に解決してください。
6. TitleScreenBackgroundService を追加し、CreateScene / FixOn / LobbyUpdate hook を管理してください。
7. CreateScene hook では title screen のときだけ TerritoryPath を差し替えてください。
8. FixOn hook では _titleCameraNeedsSet のときだけ camera/focus/FOV を差し替えてください。
9. LobbyUpdate hook では Title <-> CharaSelect 遷移時に scene reload skip を避けるため、CurrentLobbyMap を Movie にする処理を入れてください。
10. UI に「タイトル背景」セクションを追加し、enabled, TerritoryPath, camera, focus, FOV, status, apply/clear を操作できるようにしてください。
11. invalid TerritoryPath では hook 内で差し替えず、UI に validation error を表示してください。
12. docs/notes/title-background-implementation-result.md に実装結果と未確認範囲を記録してください。

検証:
- dotnet run --project tools\CharaSelectLogicTests\CharaSelectLogicTests.csproj
- dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj
- dotnet build projects\XIV-Mini-Util\XivMiniUtil.csproj -c Release

Debug build が XivMiniUtil.Dev.deps.json の file lock で失敗する場合は、Release build 成功を compile gate とし、file lock を docs に記録してください。

実機確認:
- plugin loadでクラッシュしない
- title screen表示でクラッシュしない
- override OFFで通常背景
- override ON + valid TerritoryPathでタイトル背景が変わる
- camera/focus/FOVが反映される
- Title -> CharaSelect -> Title で背景が古いまま残らない
- invalid TerritoryPathで plugin 全体が落ちない
- plugin unload時にhookがdisposeされる
```

## 15. Phase 1 完了条件

- `TitleScreenBackgroundService` が `CharaSelectService` から独立している。
- `CreateScene` / `FixOn` / `LobbyUpdate` / `LobbyCurrentMap` の責務が分離されている。
- `TerritoryTypeId` ではなく raw `TerritoryPath` を使っている。
- camera position / focus position / FOV を設定できる。
- invalid path では差し替えない。
- hook 初期化失敗時に plugin 全体が落ちない。
- `LoadPrefetchLayout` / `UpdateLoginPosition` を背景差し替え本体として扱っていない。
- エモート複数スロットが退行していない。
- ゲーム不要テストが通る。
- Release build が通る。
- docs に実装結果と未確認範囲が残っている。
