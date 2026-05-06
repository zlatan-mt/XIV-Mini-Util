<!-- Path: docs/design/chara-select-followup-implementation-plan.md -->
<!-- Description: キャラ選択画面まわりの追加要望に対する実装計画 -->
<!-- Reason: 既存修正後に、ストックエモート、指定エリア表示、DC名表示変更を段階実装するため -->
# キャラ選択画面 follow-up 実装計画

作成日: 2026-05-06

## 背景

初回実装後の実機確認で、キャラ選択画面のエモート再生自体は動作した。追加で以下の要望がある。

- エモートを複数ストックし、ボタン一つで切り替えたい。
- ログイン先エリアの事前読み込みではなく、指定したエリアをキャラクター画面に表示したい。
- ゲーム起動時の `DataCenter` 表示を、最後にログインしたデータセンター名にしたい。

## 実装前提

- HaselTweaks のコードはコピーしない。
- 既存の `CharaSelectService` に寄せるが、危険度が違うため機能ごとに段階分割する。
- `PreloadTerritory` は「ログイン先の事前読み込み」と「表示したいエリア固定」を別設定として扱う。
- 実機確認が必要な機能は build 成功だけで完了扱いにしない。

---

## Phase 1: エモート複数ストック

### 目的

キャラごとに複数の保存エモートを持ち、UIボタンで現在使うエモートを切り替えられるようにする。

### 設定案

既存:

```csharp
public Dictionary<ulong, uint> CharaSelectSelectedEmotes { get; set; } = new();
```

追加:

```csharp
public Dictionary<ulong, List<uint>> CharaSelectEmotePresets { get; set; } = new();
public Dictionary<ulong, int> CharaSelectActiveEmotePresetIndexes { get; set; } = new();
```

互換:

- 既存の `CharaSelectSelectedEmotes` は migration source として残す。
- `NormalizeAndMigrate()` で、旧 selected emote があり presets が空なら `[selected]` に移す。
- migration 後の通常読み書きは `CharaSelectEmotePresets` / `CharaSelectActiveEmotePresetIndexes` に寄せる。
- `CharaSelectSelectedEmotes` は import 互換と旧設定救済用に残すが、新規保存先にはしない。
- `GetCurrentSelectedEmoteDisplayName()` と `CurrentSelectedEmoteId` は active preset を優先し、なければ旧 selected を見る。

### 記録・保存フロー

現実装では `ExecuteEmoteDetour` -> `SaveExecutedEmote()` が `CharaSelectSelectedEmotes` に直接保存しているため、preset 導入時に保存先を分離する。

- `SaveExecutedEmote(uint emoteId)` は、実行された recordable emote をログイン中キャラの `ContentId` ごとに一時保持するだけにする。
- 設定追加:

```csharp
public Dictionary<ulong, uint> CharaSelectLastRecordedEmotes { get; set; } = new();
```

- `CharaSelectLastRecordedEmotes` は `ContentId -> emoteId` とし、`ActiveContentId` に対応する最後の記録だけをUIに表示する。
- UIの `[現在スロットへ保存]` は `ActiveContentId` の last recorded emote を active preset index に上書き保存する。
- UIの `[追加保存]` は `ActiveContentId` の last recorded emote を preset 末尾に追加し、追加した index を active にする。
- preset が空で `[現在スロットへ保存]` された場合は、上書き対象がないため追加保存と同じ扱いにする。
- 記録停止時は一時値を消さない。ユーザーが記録停止後に保存ボタンを押せるようにする。
- `ClearSelectedEmote()` は active preset の削除へ置き換える。全削除は別ボタンを追加する場合だけ実装する。
- `ReplaySelectedEmote()`、delayed replay、`CharaSelectReplayTracker` は active preset の emote id を参照する。
- `SelectPreviousEmote()` / `SelectNextEmote()` / preset 保存・削除で active emote が変わった場合は、`ClearReplayState()` 後に必要なら `ReplaySelectedEmote()` を呼ぶ。現在の `CharaSelectReplayTracker` は `ContentId` / `emoteId` / `Character*` で判定しているため、emote id 変更を再生条件に含める。
- delayed replay が pending の間に active preset を切り替えた場合、古い delayed replay を残さないように `ClearReplayState()` で取り消す。
- `SaveLoggedInVoiceId()` は現行通り記録成功時に実行し、voice id 保存と emote preset 保存は分けて扱う。
- Change Pose は現行の `GetChangePoseEmoteId()` 変換を通した後の emote id を last recorded と preset に保存する。

### UI案

`Login / Character Select` に以下を追加する。

- `現在の保存エモート: ...`
- `最後に記録したエモート: ...`
- `[前へ] [次へ] [再生]`
- `[記録開始] [記録停止] [現在スロットへ保存] [追加保存] [削除]`

最初の実装では、ボタンだけで切り替える。ドラッグ並び替えや名称変更は後回し。

### サービス変更

- `GetCurrentEmoteList()`
- `GetActivePresetIndex()`
- `SelectPreviousEmote()`
- `SelectNextEmote()`
- `SaveEmoteToActiveSlot(uint emoteId)`
- `AppendEmotePreset(uint emoteId)`
- `RemoveActiveEmotePreset()`
- `GetLastRecordedEmoteDisplayName()` (`ActiveContentId` 用)
- `SaveLastRecordedEmoteToActiveSlot()` (`ActiveContentId` 用)
- `AppendLastRecordedEmotePreset()` (`ActiveContentId` 用)

### 設定正規化

- preset 内の `0` と recordable ではない emote は削除する。
- 同一キャラ内の重複 emote は初期実装では削除する。将来「同じエモートを複数スロットに置く」用途が出るまでは、UIの混乱を避ける。
- active index が範囲外なら `0` に丸める。preset が空なら active index 設定を削除する。
- 旧 `CharaSelectSelectedEmotes` から preset へ移した後も、旧 dictionary は即削除しない。将来の rollback と import 互換のため保持する。
- `CharaSelectLastRecordedEmotes` も `0` key / `0` emote / recordable ではない emote を削除する。
- `ApplyFrom()` は presets、active indexes、last recorded emotes をすべてコピーし、最後に `NormalizeAndMigrate()` を通す。

### ゲーム非依存テスト

既存の `tools/CharaSelectLogicTests` は、再生済み判定、EmoteMode変換、voice id 反映を検証している。preset 導入時は同じツールに以下を追加する。

- active index が範囲外の場合に正規化されること。
- active emote id が変わると `CharaSelectReplayTracker.ShouldReplay()` が true になること。
- preset 切り替え時に replay state を clear するサービス補助ロジックを、ゲーム非依存に切り出して検証すること。
- last recorded emote が `ContentId` ごとに分離されること。
- Change Pose 変換後の emote id が last recorded / preset に保存されること。

### 検証

- 1キャラに複数エモートを保存できる。
- 前へ/次へで表示名が変わる。
- 記録直後は「最後に記録したエモート」だけが変わり、保存ボタンを押すまで active preset は変わらない。
- `[現在スロットへ保存]` で active preset が上書きされる。
- `[追加保存]` で preset が増え、追加分が active になる。
- preset 切り替え直後、現在表示中キャラに切り替え先エモートが再生される。
- delayed replay 待機中に preset を切り替えても、切り替え前エモートが後から再生されない。
- キャラ選択画面で active preset のエモートだけ再生される。
- 別キャラの preset と混ざらない。
- import/export 後も preset と active index が維持される。

---

## Phase 2: 指定エリアをキャラ選択画面に表示

### 目的

ログイン先 territory ではなく、ユーザーが選んだ territory の layout をキャラ選択画面で表示する。

### 設定案

```csharp
public bool CharaSelectOverrideTerritoryEnabled { get; set; } = false;
public ushort CharaSelectOverrideTerritoryTypeId { get; set; } = 0;
```

既存の `CharaSelectPreloadTerritoryEnabled` は残すが、役割を明確化する。

- `CharaSelectPreloadTerritoryEnabled`: ログイン待機中にログイン先を事前読み込み。
- `CharaSelectOverrideTerritoryEnabled`: キャラ選択画面で指定 territory を読み込み。

### UI案

`Login / Character Select` に以下を追加する。

- `[ ] キャラ選択画面に表示するエリアを固定する（実験）`
- `TerritoryTypeId: [____]`
- `現在のエリア名: ...`
- `[現在のログイン先を使う] [読み込み] [解除]`

初期実装では TerritoryTypeId 直接入力にする。エリア検索UIは後続でよい。

### 実装方針

- `UpdateCharaSelectDisplay` で選択キャラ状態が取れた後、override が ON なら指定 territory の `Bg` を解決する。
- 同じ territory / `Bg` がすでに読み込み済みなら `LoadPrefetchLayout` は呼ばない。
- override OFF時は、override 機能が読み込んだ prefetch だけ `UnloadPrefetchLayout()` する。
- `PreloadLoginTerritory()` はログイン待機列用として残し、ログイン待機時は常にログイン先 territory を優先する。
- `ResolveCharaSelectOverrideTerritoryTypeId()` と `ResolveLoginTerritoryTypeId()` を分け、override と login preload の用途を混ぜない。
- 読み込み失敗時は hook を落とさず、warning log のみにする。

### prefetch 所有権

`LoadPrefetchLayout` は global state に近いため、どの機能が最後に読み込んだかを `CharaSelectService` 内で追跡する。

```csharp
private enum CharaSelectPrefetchOwner
{
    None,
    OverrideDisplay,
    LoginWait,
}

private CharaSelectPrefetchOwner _prefetchOwner;
private ushort _loadedPrefetchTerritoryTypeId;
private string _loadedPrefetchBg = string.Empty;
```

- override 表示で読み込んだ場合は `_prefetchOwner = OverrideDisplay` にする。
- login wait preload で読み込んだ場合は `_prefetchOwner = LoginWait` にする。
- override OFF、設定解除、territory 変更では、owner が `OverrideDisplay` の場合だけ unload する。
- preload OFF では、owner が `LoginWait` の場合だけ unload する。
- Dispose では、この service が読み込んだ prefetch を残さないため、owner が `None` 以外なら unload する。
- `OpenLoginWaitDialogDetour` では、owner に関係なくログイン先 territory を読み込む。これはログイン待機列の実用性を優先するため。
- login wait preload 後は `_prefetchOwner = LoginWait` として扱い、override の OFF 操作ではその preload を unload しない。
- `CharaSelectPreloadTerritoryEnabled` が OFF の場合、`OpenLoginWaitDialogDetour` は login wait preload を行わない。

### 再読み込み条件

以下の場合だけ `TryUnloadPrefetchLayout()` -> `LoadPrefetchLayout()` を行う。

- owner が `None`。
- owner が異なる。
- territory type id が変わった。
- `TerritoryType.Bg` が変わった。

同じ owner / territory / `Bg` の場合は何もしない。`UpdateCharaSelectDisplay` の頻繁な呼び出し、初期表示ポーリング、delayed replay と競合させないため。

### UI操作と反映タイミング

- `[読み込み]` は override 設定を保存して、次の `UpdateCharaSelectDisplay` を待たず即時に `ApplyOverrideTerritoryPrefetch()` を試す。
- `[現在のログイン先を使う]` は `_currentEntry?.TerritoryTypeId` を override ID に保存する。`0` の場合は何もしない。
- `[解除]` は override ID を `0` にし、owner が `OverrideDisplay` の場合だけ unload する。
- checkbox OFF も `[解除]` と同じ unload 条件にする。
- `CharaSelectPreloadTerritoryEnabled` checkbox OFF は、owner が `LoginWait` の場合だけ unload する。override 表示用 prefetch は触らない。

### リスク

- `LoadPrefetchLayout` が実際のキャラ選択背景へ反映される保証はない。
- パッチ差分に弱い。
- ログイン直前に指定エリアを読み込んだままだと、本来のログイン先 preload と競合する可能性がある。login wait ではログイン先 preload を優先して上書きする。
- `UnloadPrefetchLayout()` が global unload である場合、owner 判定をしても他機能が読み込んだ layout を消す可能性がある。この機能内で読み込んだ直後以外の unload は避ける。

### 検証

- ID未指定時は何もしない。
- 指定IDでクラッシュしない。
- OFFで元に戻る、または少なくとも prefetch が unload される。
- 同じIDを複数回適用しても `LoadPrefetchLayout` が連続実行されない。
- override 読み込み後にログイン待機列へ進むと、ログイン先 territory preload が優先される。
- login wait preload 後に override OFF しても、login wait 側の preload を unload しない。
- ログイン待機列が出ても落ちない。

---

## Phase 3: 起動時 `DataCenter` 表示の置き換え

### 目的

ゲーム起動時の `DataCenter` 文言を、最後にログインしたデータセンター名、例 `Mana`、に置き換える。

### 調査対象

- `AgentLobby`
- Lobby UI stage
- タイトル/データセンター選択 addon
- `LobbyDataCenterWorldEntry`
- `AgentLobby.DataCenter`
- `AgentLobby.CurrentWorldName` / world list / DC list

### 設定案

```csharp
public bool CharaSelectShowLastDataCenterNameEnabled { get; set; } = false;
public string CharaSelectLastDataCenterName { get; set; } = string.Empty;
```

最後にログインしたDC名の取得候補:

- ログイン中に `IClientState.LocalPlayer.CurrentWorld` または home/current world から DC を解決する。
- Lumina の World / WorldDCGroupType 系シートを使って DC 名を引く。
- 取得できたら config に保存する。

### 表示変更候補

1. addon text node を探して `DataCenter` 文言を書き換える。
2. `AgentLobby` の表示用 Utf8String を更新できるならそこを使う。
3. addon refresh タイミングに合わせて毎フレームではなく必要時だけ再適用する。

### 実装順

1. まず `/xmu diag` などではなく、plugin log の debug-only で該当 addon/text node を調査する。
2. 表示対象が安定していることを確認する。
3. 設定ON時だけ置換する。
4. 失敗時は表示変更を諦め、ゲーム側表示を壊さない。

### リスク

- addon 構造変更に弱い。
- 多言語表示や未ログイン状態で対象文言が変わる可能性がある。
- UI text node 直接変更は、更新タイミングによって上書きされる可能性がある。

### 検証

- 起動直後に `DataCenter` が最後のDC名に変わる。
- 最後のDC名が未保存なら何もしない。
- データセンター選択画面へ移動してもクラッシュしない。
- 設定OFFで標準表示に戻る。

---

## 推奨順序

1. エモート複数ストック
2. 指定エリア表示 override
3. `DataCenter` 表示置き換え

理由:

- エモート複数ストックは既存データ構造とUI拡張が中心で、比較的低リスク。
- 指定エリア表示は layout preload の副作用があり、実機確認が必要。
- `DataCenter` 表示置き換えは addon 直接操作になる可能性が高く、最も調査依存が大きい。

## 受け入れ基準

- 各Phaseで `dotnet build projects/XIV-Mini-Util/XivMiniUtil.csproj` が成功する。
- 実機確認項目を `docs/notes/` に記録する。
- 不安定な機能はデフォルトOFF、UI上も実験扱いにする。
