# Codex実装依頼メモ: XIV Mini Util キャラ選択/タイトル背景差し替え

作成日: 2026-05-06  
対象リポジトリ: `zlatan-mt/XIV-Mini-Util`  
前提commit: `3b60e08f72626462b1a66cf003be2d0e189bd931`  
目的: 現在のキャラ選択/ログイン背景固定実装を、調査結果に基づいて再設計・修正する。

---

## 1. TL;DR

現在の実装は、エモート複数スロット・DC名記録については概ね妥当。  
一方で、背景固定/差し替えについては、調査結果と比較すると実装レイヤーが違う。

現実装は主に以下を触っている。

- `AgentLobby.UpdateCharaSelectDisplay`
- `AgentLobby.OpenLoginWaitDialog`
- `AgentLobby.UpdateLoginPosition`
- `LayoutWorld.LoadPrefetchLayout`
- `ClientSelectData.TerritoryType`
- `ClientSelectData.ZoneId`
- `Lobby` sheet の position 逆引き
- `Level` sheet の最寄り row 解決

しかし、TitleEdit 型の背景差し替えで必要になるのは以下。

- native `CreateScene`
- native `FixOn`
- native `LobbyUpdate`
- `LobbyCurrentMap`
- raw `TerritoryPath`
- camera position
- focus position
- FOV

したがって、`LoadPrefetchLayout` / `UpdateLoginPosition` / `ClientSelectData` 差し替えを改良し続けるのではなく、背景差し替えは専用サービスに分離して実装する。

---

## 2. 現在の実装評価

### 2.1 良い点

#### エモート複数スロット

現状の `CharaSelectEmotePresetStore` は方向性が良い。

- `ContentId -> List<emoteId>` でキャラごとのpresetを保持
- `ContentId -> active index` で選択中スロットを保持
- 旧 `CharaSelectSelectedEmotes` は migration/fallback として保持
- 純粋ロジックに切り出されており、ゲーム不要テストが可能
- stale pointer 対策としてログイン中はキャラ選択由来 pointer/replay state を破棄している

この部分は基本的に維持してよい。

#### DC名記録

現状は「最後にログインしたDC名を記録する」までに留めており、UIにも表示置換は未実装である旨がある。  
このままでよい。

---

### 2.2 問題点

#### 背景固定機能が本命経路に届いていない

現実装は `CharaSelectService` 内で以下を行っている。

- `UpdateCharaSelectDisplay` の original 呼び出し前に `ClientSelectData.TerritoryType` / `ZoneId` を一時差し替え
- `UpdateLoginPosition` / `OpenLoginWaitDialog` の `position` を `Lobby` sheet から逆引き
- `LoadPrefetchLayout` で指定 territory の layout を読み込み
- X/Y/Z から最寄り `Level.RowId` / `Level.Type` を解決して `LoadPrefetchLayout` に渡す

しかし、これらは以下の理由で背景差し替えとして不十分。

- `LoadPrefetchLayout` は先読みであり、表示中の title/lobby scene の生成pathを置換しない
- `UpdateLoginPosition` の `position` はログイン待機位置系の値であり、任意マップ背景のcamera指定ではない
- `ClientSelectData.TerritoryType` / `ZoneId` はキャラ選択のキャラ情報側には届くが、scene生成側に届かない可能性が高い
- X/Y/Z だけでは camera position / focus position / FOV が決まらない
- `TerritoryTypeId` だけでは LVB の raw path が決まらず、TitleEdit 型の `TerritoryPath` 置換にならない

#### `CharaSelectService` に責務が集中しすぎている

現在 `CharaSelectService` は以下を同時に担当している。

- エモート再生
- エモート記録
- voice id 補正
- DC名記録
- login territory preload
- background override
- position resolver
- level resolver
- unsafe hooks

背景差し替えは危険度・責務が異なるため、`CharaSelectService` から分離すること。

---

## 3. 実装方針

### 3.1 背景差し替えは専用サービスへ分離する

追加する候補。

```text
projects/XIV-Mini-Util/Services/TitleBackground/
  TitleScreenBackgroundService.cs
  TitleBackgroundAddressResolver.cs
  TitleBackgroundPreset.cs
  TitleBackgroundConfig.cs
```

命名候補。

- `TitleScreenBackgroundService`
- `LobbyBackgroundOverrideService`

推奨: `TitleScreenBackgroundService`

---

### 3.2 まずタイトル画面だけを対象にする

最初からキャラ選択画面まで狙わない。  
Phase 1 は title screen のみ。

理由:

- TitleEdit でも `CreateScene` の `Title` 分岐で `TerritoryPath` を置換している
- CharaSelect 側は `CreateScene` で path 置換しておらず、camera固定だけしている
- title と chara select の遷移時に scene reload skip 回避が必要であり、範囲を広げると危険

---

### 3.3 設定モデル

既存の `Configuration` に以下を追加する。

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

Phase 1 では、BGM / Weather / Time は設定だけ用意してもよいが、実装は後回しでよい。

---

### 3.4 Preset DTO

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

---

## 4. 必要なhook

### 4.1 CreateScene

目的:

- title scene 生成時に、original に渡される path を `TitleBackgroundTerritoryPath` へ置換する

必要な処理:

```csharp
// 疑似コード
private nint CreateSceneDetour(byte* path, ...)
{
    if (_configuration.TitleBackgroundOverrideEnabled
        && _lastLobbyUpdateMapId == GameLobbyType.Title
        && !string.IsNullOrWhiteSpace(_configuration.TitleBackgroundTerritoryPath))
    {
        path = ConvertToUtf8Pointer(_configuration.TitleBackgroundTerritoryPath);
        var ret = _createSceneHook.Original(path, ...);
        _titleCameraNeedsSet = true;
        return ret;
    }

    return _createSceneHook.Original(path, ...);
}
```

注意:

- 具体的なdelegate signatureは現行client/API15で再確認する
- TitleEdit の GPL-3.0 コードをコピーしない
- signature scan は独自に書く
- raw path の lifetime に注意。original 呼び出し中だけ有効な UTF-8 buffer を保持する

---

### 4.2 FixOn

目的:

- 差し替えた territory をどこから見るか指定する
- camera position / focus position / FOV を差し替える

疑似コード:

```csharp
private nint FixOnDetour(nint self, float* camera, float* focus, float fovY)
{
    if (_titleCameraNeedsSet)
    {
        _titleCameraNeedsSet = false;

        Span<float> cameraOverride = stackalloc float[]
        {
            _configuration.TitleBackgroundCameraX,
            _configuration.TitleBackgroundCameraY,
            _configuration.TitleBackgroundCameraZ,
        };

        Span<float> focusOverride = stackalloc float[]
        {
            _configuration.TitleBackgroundFocusX,
            _configuration.TitleBackgroundFocusY,
            _configuration.TitleBackgroundFocusZ,
        };

        return _fixOnHook.Original(
            self,
            cameraOverridePointer,
            focusOverridePointer,
            _configuration.TitleBackgroundFovY);
    }

    return _fixOnHook.Original(self, camera, focus, fovY);
}
```

注意:

- 実際の引数型・戻り値は現行clientで確認する
- stackalloc pointerをdelegate越しに使う場合は lifetime に注意。必要なら固定配列/unsafe blockで慎重に実装する

---

### 4.3 LobbyUpdate

目的:

- `Title -> CharaSelect` / `CharaSelect -> Title` 遷移時に、ゲームが同じ lobby map と判断して scene reload を省略しないようにする
- `_lastLobbyUpdateMapId` を更新する

必要な enum:

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

疑似コード:

```csharp
private void LobbyUpdateDetour(GameLobbyType mapId, int time)
{
    if ((_lastLobbyUpdateMapId == GameLobbyType.Title && mapId == GameLobbyType.CharaSelect)
        || (_lastLobbyUpdateMapId == GameLobbyType.CharaSelect && mapId == GameLobbyType.Title))
    {
        // CurrentLobbyMap = Movie にして reload skip を避ける
        *_currentLobbyMap = (short)GameLobbyType.Movie;
    }

    _lastLobbyUpdateMapId = mapId;
    _lobbyUpdateHook.Original(mapId, time);
}
```

---

### 4.4 AddressResolver

`TitleBackgroundAddressResolver` で signature scan する対象候補。

最低限:

- `CreateScene`
- `FixOn`
- `LobbyUpdate`
- `LobbyCurrentMap`

Phase 2以降:

- `PlayMusic`
- `SetTime`
- `WeatherPtrBase`

注意:

- TitleEdit の resolver 実装をコピーしない
- signature は現行 FFXIV / Dalamud API15 で独自確認
- 失敗時は plugin 全体を落とさず、TitleBackground 機能だけ disable する

---

## 5. UI方針

### 5.1 既存の背景固定UIは降格する

現在の以下のUIは誤解を招く。

```text
キャラ選択画面に表示するエリアを固定する（実験）
表示地点を指定する（X/Y/Z）
```

変更案:

```text
キャラ選択背景の事前読み込み/診断を有効化する（実験・背景差し替え未保証）
```

または、背景差し替え本実装ができるまで一旦非表示/無効化する。

---

### 5.2 新規UI: Title Background

`Login / Character Select` 内に置くか、別カテゴリ `Title Background` を作る。

最小UI:

```text
[ ] タイトル画面背景を差し替える（実験）
TerritoryPath: [                         ]
Camera: X [ ] Y [ ] Z [ ]
Focus : X [ ] Y [ ] Z [ ]
FOV Y : [45.0]
[現在地とカメラを保存]  ※Phase 2
[適用]
[解除]
```

Phase 1では手入力でよい。  
Phase 2で「現在地とカメラを保存」を実装する。

---

## 6. Phase分割

### Phase 0: 現在の背景固定機能の整理

やること:

- `CharaSelectService` 内の background override を「未保証の診断」に降格
- UI文言を変更
- `UpdateLoginPosition` / `LoadPrefetchLayout` / `LevelResolver` が背景差し替え本体ではないことを docs に明記
- エモート機能には触らない

成果物:

- 誤解を招かないUI
- 背景差し替え実装前でもクラッシュしにくい状態

---

### Phase 1: Title screen background override

やること:

- `TitleScreenBackgroundService` 追加
- `TitleBackgroundAddressResolver` 追加
- `CreateScene` hook 追加
- `FixOn` hook 追加
- `LobbyUpdate` hook 追加
- `LobbyCurrentMap` 書き換え対応
- `Configuration` に `TitleBackground*` 設定追加
- UIに title background 設定追加
- 失敗時は title background 機能だけ disable

やらないこと:

- BGM差し替え
- 天候固定
- 時刻固定
- CharaSelect画面側の背景差し替え
- TitleEditコードのコピー

検証:

```powershell
dotnet run --project tools/CharaSelectLogicTests/CharaSelectLogicTests.csproj
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj
dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Release
```

実機確認:

- plugin loadでクラッシュしない
- title画面表示でクラッシュしない
- override OFFで通常背景
- override ON + valid TerritoryPath でタイトル背景が切り替わる
- camera/focus/FOV が反映される
- title <-> chara select 遷移で reload skip による無反映が起きない
- plugin unload時にhookが正しくdisposeされる

---

### Phase 2: 現在地からpreset保存

やること:

- ログイン中に現在の `TerritoryType.Bg` を取得して `TerritoryPath` に保存
- render camera の eye position を `Camera*` に保存
- camera方向から focus point を計算して `Focus*` に保存
- FOV を保存
- `[現在地とカメラを保存]` UI追加

注意:

- ここは FFXIVClientStructs / Dalamud の render camera API を現行環境で確認する
- 取れない場合は Phase 2 は未実装にしてよい

---

### Phase 3: Weather / Time / BGM

やること:

- `SetTime` hook/delegate
- weather pointer 書き換え
- `PlayMusic` hook
- BGM path 設定

注意:

- 一度だけ書いても戻される可能性があるため、短時間だけ再適用が必要
- Phase 1が安定してから実装する

---

## 7. 既存コードへの具体的な指示

### 7.1 残す

以下は基本的に残す。

```text
projects/XIV-Mini-Util/Services/CharaSelect/CharaSelectEmotePresetStore.cs
tools/CharaSelectLogicTests/Program.cs の emote preset テスト
Configuration の CharaSelectEmotePresets 系
DC名記録機能
voice id 補正
```

---

### 7.2 見直す

以下は背景差し替え本体としては使わない。

```text
CharaSelectOverrideTerritoryEnabled
CharaSelectOverrideTerritoryTypeId
CharaSelectOverridePositionEnabled
CharaSelectOverridePositionX/Y/Z
CharaSelectLevelResolver
CharaSelectLobbyPositionResolver
UpdateLoginPositionDetour
TryPatchOverrideDisplayData
ApplyOverrideTerritoryPrefetch
```

対応方針:

- すぐ削除しなくてよい
- まず UI を診断/未保証扱いにする
- 新しい `TitleScreenBackgroundService` が安定した後、削除または diagnostics に限定する

---

### 7.3 追加で直すべき点

`Plugin.cs` で `CharaSelectService` 作成後に `SyncFromConfiguration()` が呼ばれていない場合は追加する。

```csharp
_charaSelectService.SyncFromConfiguration();
```

または、`InitializeHooks()` 内の `OpenLoginWaitDialog` hook 初期enable条件を以下にする。

```csharp
if (_configuration.CharaSelectPreloadTerritoryEnabled
    || _configuration.CharaSelectOverrideTerritoryEnabled)
{
    _openLoginWaitDialogHook.Enable();
}
```

ただし、背景差し替えの本実装では `OpenLoginWaitDialog` に依存しないこと。

---

## 8. ライセンス注意

TitleEditPlugin は GPL-3.0。  
そのため、以下は禁止。

- TitleEdit のコードをそのままコピーする
- signature resolver のコードを丸写しする
- JSON preset や parser をそのまま移植する

許可する範囲。

- 仕組みの理解
- hook対象の参考
- 設計思想の参考
- 独自実装
- 現行 API15 / FFXIVClientStructs での再調査

---

## 9. Codexへの実装プロンプト

以下をそのままCodexに渡してよい。

```text
あなたはこのリポジトリの実装担当です。

目的:
XIV Mini Util のキャラ選択/ログイン背景固定機能を、調査結果に基づいて再設計してください。
現在の CharaSelectService 内の LoadPrefetchLayout / UpdateLoginPosition / ClientSelectData 差し替え方式は、背景差し替え本体ではないため、これ以上深追いしないでください。

重要:
- エモート複数スロット機能は概ねOKなので壊さないでください。
- 背景差し替えは CharaSelectService に詰め込まず、TitleScreenBackgroundService として分離してください。
- Phase 1 は title screen のみ対象にしてください。chara select画面側の背景差し替えは後回しです。
- TitleEditPlugin は GPL-3.0 なのでコードコピーは禁止です。仕組みの参考に留め、独自実装してください。
- 失敗時は plugin 全体を落とさず、TitleBackground 機能だけ disable してください。

実装方針:
1. 既存の「キャラ選択画面に表示するエリアを固定する（実験）」UIは、背景差し替え未保証の診断機能として文言を変更するか、一旦無効化してください。
2. `projects/XIV-Mini-Util/Services/TitleBackground/` を新設してください。
3. `TitleScreenBackgroundService` を追加してください。
4. `TitleBackgroundAddressResolver` を追加し、必要な native address を signature scan で解決してください。
5. Phase 1 では `CreateScene` / `FixOn` / `LobbyUpdate` / `LobbyCurrentMap` を対象にしてください。
6. `Configuration` に `TitleBackgroundOverrideEnabled`, `TitleBackgroundTerritoryPath`, camera position, focus position, FOV を追加してください。
7. UIに title background override 設定を追加してください。
8. `CreateScene` hook では title screen のときだけ `TerritoryPath` を差し替えてください。
9. `FixOn` hook では `_titleCameraNeedsSet` のときだけ camera/focus/FOV を差し替えてください。
10. `LobbyUpdate` hook では Title <-> CharaSelect 遷移時に scene reload skip を避けるため、必要に応じて CurrentLobbyMap を Movie にしてください。
11. BGM / weather / time は Phase 1 では実装しなくてよいです。設定だけ残してもよいですが、未実装と明記してください。

検証:
- `dotnet run --project tools/CharaSelectLogicTests/CharaSelectLogicTests.csproj`
- `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj`
- `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj -c Release`

実機確認項目:
- plugin loadでクラッシュしない
- title screen表示でクラッシュしない
- override OFFで通常背景
- override ON + valid TerritoryPathでタイトル背景が変わる
- camera/focus/FOVが反映される
- title <-> chara select 遷移で背景が古いまま残らない
- plugin unload時にhookがdisposeされる

作業後:
- 変更内容を docs/notes/title-background-implementation-result.md に記録してください。
- 何が未実装かを明記してください。
- 実機未確認のものは未確認と書いてください。
```

---

## 10. 完了条件

Phase 1 の完了条件。

- `TitleScreenBackgroundService` が `CharaSelectService` から独立している
- `CreateScene` / `FixOn` / `LobbyUpdate` / `LobbyCurrentMap` の責務が分離されている
- `TerritoryTypeId` ではなく raw `TerritoryPath` を使っている
- camera position / focus position / FOV を設定できる
- `LoadPrefetchLayout` / `UpdateLoginPosition` を背景差し替え本体として扱っていない
- エモート複数スロットが退行していない
- Debug / Release build が通る
- docs に未確認範囲が明記されている
