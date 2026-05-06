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
- `GetCurrentSelectedEmoteDisplayName()` は active preset を優先し、なければ旧 selected を見る。

### UI案

`Login / Character Select` に以下を追加する。

- `現在の保存エモート: ...`
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

### 検証

- 1キャラに複数エモートを保存できる。
- 前へ/次へで表示名が変わる。
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

- `UpdateCharaSelectDisplay` で選択キャラ状態が取れた後、override が ON なら指定 territory の `Bg` を `LoadPrefetchLayout` に渡す。
- OFF時は `UnloadPrefetchLayout()`。
- `PreloadLoginTerritory()` は `ResolveCharaSelectTerritoryTypeId()` を使い、override ONなら指定ID、OFFならログイン先IDを使う形へ統合する。
- 読み込み失敗時は hook を落とさず、warning log のみにする。

### リスク

- `LoadPrefetchLayout` が実際のキャラ選択背景へ反映される保証はない。
- パッチ差分に弱い。
- ログイン直前に指定エリアを読み込んだままだと、本来のログイン先 preload と競合する可能性がある。

### 検証

- ID未指定時は何もしない。
- 指定IDでクラッシュしない。
- OFFで元に戻る、または少なくとも prefetch が unload される。
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
