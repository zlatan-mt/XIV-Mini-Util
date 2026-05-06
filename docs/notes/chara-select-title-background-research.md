<!-- Path: docs/notes/chara-select-title-background-research.md -->
<!-- Description: キャラ選択/ログイン画面背景変更に関するTitleEdit/HaselTweaks調査メモ -->
<!-- Reason: 現実装で背景が変わらない原因と、実装に必要な低レベルhook方針を残すため -->
# キャラ選択/ログイン背景変更 調査メモ

作成日: 2026-05-06

## 調査目的

`XIV Mini Util` のキャラ選択 follow-up 実装では、ログイン/キャラ選択画面に表示するエリアを固定する目的で、以下の方向を試した。

- `AgentLobby.UpdateCharaSelectDisplay` 前後で `ClientSelectData.TerritoryType` / `ZoneId` を一時差し替えする。
- `AgentLobby.OpenLoginWaitDialog` / `UpdateLoginPosition` に渡す position を差し替える。
- `LayoutWorld.LoadPrefetchLayout` で指定 territory の layout を事前読み込みする。
- `TerritoryTypeId` と任意座標から近い `Level` row を解決し、prefetch に渡す。

しかし実機上、エモートは動作した一方で、ログイン画面/キャラ選択画面の背景は変わらなかった。

ユーザー報告では、診断表示が以下の状態だった。

```text
ログイン背景position=12 override=なし
```

このため、HaselTweaks と TitleEditPlugin の実装を確認し、どこで背景を切り替えているか、現実装とどう違うかを調査した。

## 調査対象

### HaselTweaks

- リポジトリ: `https://github.com/Haselnussbomber/HaselTweaks`
- ローカル調査先: `C:\Users\MonaT\AppData\Local\Temp\HaselTweaks-inspect`
- 確認時点の commit: `8c290f1 Update ClientStructs`
- 主な対象ファイル:
  - `HaselTweaks/Tweaks/EnhancedLoginLogout.cs`
  - `HaselTweaks/Tweaks/Configs/EnhancedLoginLogoutConfig.cs`

### TitleEditPlugin

- リポジトリ: `https://github.com/lmcintyre/TitleEditPlugin`
- ローカル調査先: `C:\Users\MonaT\AppData\Local\Temp\TitleEditPlugin-inspect`
- 確認時点の commit: `19d672e WIP but crashes all the time`
- ライセンス: GPL-3.0
- 主な対象ファイル:
  - `TitleEdit/TitleEdit.cs`
  - `TitleEdit/TitleEditAddressResolver.cs`
  - `TitleEdit/TitleEditPlugin.cs`
  - `TitleEdit/TitleEditScreen.cs`
  - `TitleEdit/GameLobbyType.cs`
  - `TitleEdit/LvbFile.cs`
  - `TitleEdit/titlescreens/*.json`

## HaselTweaks の実装

HaselTweaks の `Enhanced Login/Logout` は、主に以下を行う。

- キャラ選択画面で指定エモートを再生する。
- ログイン待機時にログイン先 territory の layout を事前読み込みする。

確認できた hook は以下。

- `AgentLobby.UpdateCharaSelectDisplay`
- `CharaSelectCharacterList.CleanupCharacters`
- `EmoteManager.ExecuteEmote`
- `AgentLobby.OpenLoginWaitDialog`

重要なのは、HaselTweaks には TitleEdit のような任意背景差し替え hook がない点。

`OpenLoginWaitDialogDetour(AgentLobby* agent, int position)` では original を同じ `position` で呼び、その後 `_config.PreloadTerritory` が有効なら `_currentEntry` のログイン先 territory を prefetch する。

prefetch の呼び出しは概ね以下の形。

```csharp
LayoutWorld.UnloadPrefetchLayout();
LayoutWorld.Instance()->LoadPrefetchLayout(
    2,
    bg,
    40,
    0,
    territoryTypeId,
    GameMain.Instance()->ActiveFestivals.GetPointer(0),
    0);
```

ここで `40` は `LayerEntryType.PopRange` 相当。

### HaselTweaks の housing 正規化

HaselTweaks は housing area の territory をログイン先として扱うため、内部/外部/区画違いを代表 territory に正規化していた。

- Mist: `282, 283, 284, 384, 423, 573, 608 => 339`
- Lavender Beds: `342, 343, 344, 385, 425, 574, 609 => 340`
- Goblet: `345, 346, 347, 386, 424, 575, 610 => 341`
- Shirogane: `649, 650, 651, 652, 653, 654, 655 => 641`
- Empyreum: `980, 981, 982, 983, 984, 985, 999 => 979`

現実装の `NormalizeHousingTerritory()` は、この mapping と一致していない箇所がある。

## HaselTweaks と現実装の差分

現実装の `TryLoadPrefetchLayout()` は、override 座標指定を意識して `Level` row を解決し、以下のように呼ぶ。

```csharp
layoutWorld->LoadPrefetchLayout(
    0,
    bg,
    resolvedLevel.Type,
    resolvedLevel.RowId,
    territoryTypeId,
    null,
    0);
```

HaselTweaks とは以下が違う。

- type が `2` ではなく `0`。
- layerEntryType が固定 `40` ではなく、`Level.Type`。
- levelId が `0` ではなく、座標から近い `Level.RowId`。
- festivals pointer が `GameMain.Instance()->ActiveFestivals.GetPointer(0)` ではなく `null`。
- HaselTweaks はログイン先 preload に限定しており、任意背景差し替えはしていない。

このため、HaselTweaks を元にするなら、現在の座標/Level 解決型 prefetch は少なくとも HaselTweaks 由来の実装ではない。

## TitleEdit の実装

TitleEdit は HaselTweaks とは根本的に違い、ログイン/タイトル画面の scene 生成そのものへ割り込んでいる。

### 設定モデル

`TitleEditScreen` は以下のような情報を持つ。

```csharp
public class TitleEditScreen
{
    public string Name;
    public string Logo;
    public bool DisplayLogo;
    public string TerritoryPath;
    public Vector3 CameraPos;
    public Vector3 FixOnPos;
    public float FovY;
    public byte WeatherId;
    public ushort TimeOffset;
    public string BgmPath;
}
```

プリセット JSON 例:

```json
{
  "Name": "TE_Central Shroud",
  "TerritoryPath": "ffxiv/fst_f1/fld/f1f1/level/f1f1",
  "Logo": "A Realm Reborn",
  "DisplayLogo": "True",
  "CameraPos": {
    "X": 189.352264,
    "Y": -29.25525,
    "Z": 375.484741
  },
  "FixOnPos": {
    "X": 199.312561,
    "Y": -28.4381962,
    "Z": 375.8382
  },
  "FovY": 45.0,
  "WeatherId": 1,
  "TimeOffset": 640,
  "BgmPath": "music/ffxiv/orchestrion/bgm_orch_391.scd"
}
```

背景変更に必要なのは `TerritoryTypeId` ではなく、`TerritoryType.Bg` に相当する raw path、つまり `TerritoryPath`。

### アドレス解決

`TitleEditAddressResolver` は signature scan で以下を取る。

- `LoadLogoResource`
- `CameraBase`
- `SetTime`
- `CreateScene`
- `FixOn`
- `PlayMusic`
- `BgmControl`
- `WeatherPtrBase`
- `LobbyUpdate`
- `LobbyCurrentMap`

主な役割:

- `CreateScene`: タイトル/ロビー scene の生成関数。
- `FixOn`: ロビーカメラの camera/focus/FOV 設定。
- `PlayMusic`: タイトル BGM 差し替え。
- `SetTime`: タイトル背景内の時刻固定。
- `WeatherPtrBase`: 天候値の直接書き換え。
- `LobbyUpdate`: タイトル/キャラ選択などのロビーマップ遷移。
- `LobbyCurrentMap`: 現在の lobby map 判定値。

`FFXIVClientStructs` API 15 では、`AgentLobby.UpdateLoginPosition`、`LayoutWorld.LoadPrefetchLayout`、`BGMSystem.SetBGM`、`WeatherManager.GetCurrentWeather` などは typed API がある。一方、TitleEdit が使う `CreateScene` / `FixOn` / `LobbyUpdate` / `LobbyCurrentMap` は、確認した範囲では同等の typed API として露出していなかった。

そのため、TitleEdit 型の背景差し替えを実装するなら、少なくとも一部は signature scan が必要になる可能性が高い。

### LobbyUpdate hook

`GameLobbyType` は以下。

```csharp
public enum GameLobbyType
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

`LobbyUpdateDetour(GameLobbyType mapId, int time)` は、遷移方向を見ている。

- `Title -> CharaSelect`
- `CharaSelect -> Title`

該当遷移時は、ゲームが同じ lobby map と判断して scene reload を省略しないよう、`CurrentLobbyMap` に `Movie` を書く。

```csharp
TitleEditAddressResolver.CurrentLobbyMap = (short)GameLobbyType.Movie;
```

つまり TitleEdit は、単に path を差し替えるだけでなく、タイトル/キャラ選択間の再ロード抑止も回避している。

### CreateScene hook

`HandleCreateScene(string p1, ...)` は `_lastLobbyUpdateMapId` で分岐する。

#### CharaSelect の場合

`_lastLobbyUpdateMapId == GameLobbyType.CharaSelect` の場合:

- original の `p1` をそのまま使う。
- scene 生成後、ロビーカメラを固定値で `FixOn` する。

概念的には以下。

```csharp
var returnVal = _createSceneHook.Original(p1, ...);
FixOn(new Vector3(0, 0, 0), new Vector3(0, 0.8580103f, 0), 1);
return returnVal;
```

ここではカスタム `TerritoryPath` に置換していない。

#### Title の場合

`_lastLobbyUpdateMapId == GameLobbyType.Title` の場合:

- 選択済み JSON から `_currentScreen` を読み込む。
- `p1` を `_currentScreen.TerritoryPath` に置換する。
- original を呼ぶ。
- 次の `FixOn` 呼び出しで camera/focus/FOV を置き換えるため `_titleCameraNeedsSet = true` にする。
- weather/time を一定時間強制する。

概念的には以下。

```csharp
RefreshCurrentTitleEditScreen();
p1 = _currentScreen.TerritoryPath;
var returnVal = _createSceneHook.Original(p1, ...);
_titleCameraNeedsSet = true;
ForceWeather(_currentScreen.WeatherId, 5000);
ForceTime(_currentScreen.TimeOffset, 5000);
return returnVal;
```

これが TitleEdit の背景差し替えの中心。

### FixOn hook

`HandleFixOn(...)` は `_titleCameraNeedsSet` が true のときだけ、ゲームが渡してきた camera/focus/FOV を `_currentScreen` の値に置き換える。

```csharp
return _fixOnHook.Original(
    self,
    FloatArrayFromVector3(_currentScreen.CameraPos),
    FloatArrayFromVector3(_currentScreen.FixOnPos),
    _currentScreen.FovY);
```

これにより、差し替えた territory をどの位置/角度/FOV で見るかが固定される。

### BGM / 天候 / 時刻

TitleEdit は背景 path だけでなく、見た目と音も別途補正している。

- BGM:
  - `PlayMusic` hook で `_System_Title.scd` を検出し、`_currentScreen.BgmPath` に差し替える。
- 天候:
  - `WeatherPtrBase` から得たポインタに weather id を直接書き込む。
  - 一度では戻される可能性があるため、短時間ループで強制している。
- 時刻:
  - `SetTime` delegate を短時間ループで呼び続ける。

### カスタム title 作成 UI

TitleEdit の作成 UI は、現在地から以下を保存する。

- `TerritoryPath`: `ClientState.TerritoryType` の `TerritoryType.Bg`
- `CameraPos`: render camera の eye position
- `FixOnPos`: render camera の向きから計算した注視点
- `FovY`
- `WeatherId`
- `TimeOffset`
- `BgmPath`

重要なのは、任意マップの `X:Y:Z` だけでは不十分な点。

背景として自然に表示するには、少なくとも以下が必要。

- どの LVB をロードするか: `TerritoryPath`
- カメラをどこに置くか: `CameraPos`
- どこを見るか: `FixOnPos`
- どの画角にするか: `FovY`

`X:Y:Z` を指定するだけでは、カメラ位置なのか注視点なのか、どの向きから見るのかが決まらない。

## 現実装で背景が変わらない理由

現実装は、主に以下を触っている。

- `AgentLobby.UpdateCharaSelectDisplay`
- `AgentLobby.OpenLoginWaitDialog`
- `AgentLobby.UpdateLoginPosition`
- `LayoutWorld.LoadPrefetchLayout`
- `ClientSelectData.TerritoryType`
- `ClientSelectData.ZoneId`
- `Lobby` sheet からの position 逆引き

一方、TitleEdit が背景差し替えに使っているのは以下。

- native `CreateScene`
- native `FixOn`
- native `LobbyUpdate`
- static `LobbyCurrentMap`
- `TerritoryPath`

つまり、現在の実装は「キャラ選択キャラクター情報」や「ログイン先 layout preload」には届いているが、実際にタイトル/ロビー背景 scene を生成する関数には届いていない。

`ログイン背景position=12 override=なし` は、`UpdateLoginPosition` / `OpenLoginWaitDialog` 系の hook が呼ばれていることは示すが、TitleEdit が使う `CreateScene` path 差し替えが起きたことは示さない。

そのため、position 差し替えや Lobby sheet 逆引きだけでは背景が変わらない。

## 実装する場合の推奨方針

### CharaSelectService に詰め込まない

エモート再生、ログイン先 preload、タイトル背景差し替えは危険度と責務が違う。

背景差し替えを本実装するなら、`CharaSelectService` ではなく別サービスに分ける。

候補名:

- `TitleScreenBackgroundService`
- `LobbyBackgroundOverrideService`

### 最小構成

まずはタイトル画面背景だけを対象にする。

Phase 1:

- 設定に以下を追加する。
  - enabled
  - `TerritoryPath`
  - `CameraPos`
  - `FixOnPos`
  - `FovY`
  - `WeatherId`
  - `TimeOffset`
  - `BgmPath`
- `CreateScene` hook で `GameLobbyType.Title` のときだけ `TerritoryPath` を差し替える。
- `FixOn` hook で `_titleCameraNeedsSet` のときだけカメラを差し替える。
- `LobbyUpdate` hook で `Title <-> CharaSelect` 遷移時の reload skip を避ける。
- BGM/天候/時刻は初期実装では任意、または後続 Phase に分ける。

Phase 2:

- 現在地から title preset を保存する UI を追加する。
- 現在地の `TerritoryType.Bg` を `TerritoryPath` に保存する。
- render camera から `CameraPos` と `FixOnPos` を保存する。

Phase 3:

- 天候、時刻、BGM を追加する。
- LVB から利用可能天候を読む場合は `LvbFile` 相当の parser が必要。

### 設定 UI

ユーザー要望の「マップの、特定のこの場所」を実現する UI は、単なる `X:Y:Z` 入力ではなく以下が現実的。

- ログイン中に目的地へ移動する。
- 見せたい方向へカメラを向ける。
- `[現在の場所とカメラを保存]` を押す。
- 保存された `TerritoryPath` / camera / focus / FOV をタイトル背景として使う。

手入力 UI を残す場合も、以下の意味を分ける必要がある。

- camera position X/Y/Z
- focus position X/Y/Z
- FOV
- territory path または territory type id

## ライセンス上の注意

TitleEditPlugin は GPL-3.0。

そのため、TitleEdit のコードをそのままコピーすると、`XIV Mini Util` 側の配布条件に影響する可能性がある。

今回の調査結果は、以下の範囲に留めるのが安全。

- 仕組みの理解
- hook 対象と責務の整理
- 現実装との差分分析
- 独自実装の設計方針

実装時は、TitleEdit のコードを直接移植せず、現在の Dalamud API 15 / FFXIVClientStructs で確認できる typed API を優先し、必要な native address のみ独自に解決する。

## 現時点の結論

- HaselTweaks は任意背景差し替えをしていない。やっているのはログイン先 territory の prefetch とエモート再生。
- TitleEdit は native `CreateScene` で `TerritoryPath` を差し替えているため、現実装とは触っている層が違う。
- 現在の `UpdateLoginPosition` / Lobby sheet 逆引き / `LoadPrefetchLayout` では、TitleEdit 型の背景差し替えは実現できない。
- 任意マップの特定地点を背景にするには、`TerritoryPath`、camera position、focus position、FOV が必要。
- 実装するなら、`CharaSelectService` から分離した title/lobby background 専用サービスとして、低レベル hook を限定的に導入するのが妥当。

## 次に確認すべきこと

1. `FFXIVClientStructs` API 15 で `CreateScene` / `FixOn` / `LobbyUpdate` 相当が公開されていないかを再確認する。
2. 公開 API がない場合、signature scan を使う範囲を最小化する。
3. TitleEdit の GPL-3.0 コードをコピーせず、独自実装できる設計にする。
4. まず title 画面だけを対象にして、キャラ選択画面側の差し替えは後続で判断する。
5. 既存の `CharaSelectService` 内の `UpdateLoginPosition` / Lobby position resolver は、背景差し替え目的には効果が薄いため、削除または診断専用化を検討する。
