<!-- Path: docs/item-shop-search-implementation.md -->
<!-- Description: アイテムショップ検索の実装と設計を整理する -->
<!-- Reason: 機能の全体像と変更履歴を共有するため -->
<!-- RELEVANT FILES: projects/XIV-Mini-Util/Services/ShopSearchService.cs, projects/XIV-Mini-Util/Windows/ShopSearchResultWindow.cs, projects/XIV-Mini-Util/Services/TeleportService.cs -->
# アイテムショップ検索機能 - 実装ドキュメント

## 概要

FFXIVのアイテムを右クリックして、そのアイテムを販売しているNPCショップの場所を検索・表示する機能。

## 機能一覧

1. **コンテキストメニュー統合** - アイテム右クリックで「販売場所を検索」メニューを表示
2. **ショップデータキャッシュ** - 起動時にLuminaシートからショップデータを非同期で構築
3. **マップリンク生成** - 検索結果をチャットにマップリンク付きで表示
4. **検索結果ウィンドウ** - 複数件ある場合にImGuiウィンドウでリスト表示
5. **エリア優先度設定** - 三大都市などを優先して表示
6. **表示件数制限** - 検索結果は上位10件のみ表示
7. **テレポ支援** - 上位10件にテレポボタンを表示
8. **自動テレポ** - 検索時/マップピン時に最寄りエーテライトへ自動テレポ（任意）

## アーキテクチャ

### 主要コンポーネント

```
Plugin.cs
├── ShopDataCache          # ショップデータの読み込み・キャッシュ
├── ShopSearchService      # 検索ロジック・結果出力
├── ContextMenuService     # 右クリックメニュー統合
├── MapService             # マップピン設定
├── ChatService            # Echo投稿
├── TeleportService        # 最寄りエーテライト検索とテレポ実行
└── ShopSearchResultWindow # 検索結果UI
```

### データフロー

```
[アイテム右クリック]
    ↓
[ContextMenuService] → アイテムID取得
    ↓
[ShopSearchService] → ShopDataCacheから販売場所検索
    ↓
[MapService] → マップピン設定
[TeleportService] → 設定ON時に最寄りエーテライトへテレポ
[ChatService] → チャット投稿
[ShopSearchResultWindow] → 4件以上で表示
```

## ShopDataCache - ショップデータ構築

### 対応シート

| シート名 | 用途 |
|---------|------|
| GilShop | ギルで購入できるショップ |
| GilShopItem | GilShopで販売されているアイテム（SubrowSheet） |
| SpecialShop | 特殊通貨・アイテム交換ショップ（染料等） |
| ENpcBase | NPCの基本情報（ショップ参照を含む） |
| ENpcResident | NPC名 |
| Level | NPCの位置情報 |
| TerritoryType | エリア情報 |
| Map | マップ情報・座標変換 |
| Item | アイテム情報 |

### データ構築プロセス

1. **NPC位置マッピング構築** (`BuildNpcLocationMapping`)
   - Levelシートを走査
   - `Level.Type == 8` でNPCエントリを識別
   - NPC ID → 位置情報（エリア、座標）のマッピングを作成

2. **NPC→ショップマッピング構築** (`BuildNpcToShopMappings`)
   - GilShop、SpecialShopのRowIdをHashSetに収集
   - ENpcBaseシートを走査
   - `ENpcData[]`配列から各ショップへの参照を検出
   - RowIdが直接GilShop/SpecialShopに存在するかチェック
   - NPC位置情報と紐付けてマッピング作成

3. **GilShopItem処理**
   - `GetSubrowExcelSheet<GilShopItem>()`でSubrowSheet取得
   - 親行（SubrowCollection）のRowIdがGilShop ID
   - 各アイテムIDをNPCマッピングと照合して逆引きインデックス構築

4. **SpecialShop処理** (`ProcessSpecialShops`)
   - SpecialShopシートを走査
   - リフレクションで`Item`配列から受け取りアイテムを抽出
   - コスト情報も取得して条件表示に使用

### フィルタリング

- **位置情報なしの除外**: `TerritoryTypeId == 0` または `AreaName` が空のNPCはスキップ
- **空エントリ除外**: 販売場所が0件のアイテムはインデックスから削除

### 座標変換

```csharp
// FFXIV座標変換式
var scale = sizeFactor / 100f;
var c = 41f / scale;
var adjusted = (rawPosition * scale + 1024f) / 2048f;
var result = c * adjusted + 1f;
```

## ENpcData構造

ENpcBase.ENpcData[]は`Lumina.Excel.RowRef`型で、以下のプロパティを持つ：

- `RowId: UInt32` - 参照先のRowId（GilShopやSpecialShopのID）
- `IsUntyped: Boolean`
- `Language: Nullable<Language>`

RowIdがGilShopまたはSpecialShopのRowId範囲に含まれる場合、そのNPCはショップを持つ。

### RowId範囲（参考値）

- GilShop: 262144 - 263292 (0x40000 - 0x40484)
- SpecialShop: 別範囲（動的に取得）

## SpecialShop構造

SpecialShop.ItemStruct のプロパティはリフレクションで動的に解析：

- 受け取りアイテム: `ItemReceive`, `ReceiveItems`, `OutputItem`, `Item`, `Receive` のいずれか
- コストアイテム: `ItemCost`, `CostItems`, `InputItem`, `Cost`, `CurrencyCost` のいずれか

## 設定項目

### Configuration

```csharp
public List<uint> ShopSearchAreaPriority { get; set; } = new()
{
    128,  // リムサ・ロミンサ
    130,  // ウルダハ
    132,  // グリダニア
};

public bool ShopSearchAutoTeleportEnabled { get; set; } = false;
```

## ログ出力例

```
ショップデータ構築開始...
シート取得: Item=True, GilShop=True, SpecialShop=True, ENpcBase=True
NPC-Shop マッピング構築開始...
NPC位置情報: 23801件
GilShopシート: 1109件のショップを検出
GilShop RowId範囲: 262144 - 263292
SpecialShopシート: XXX件のショップを検出
SpecialShop RowId範囲: X - Y
ENpcBase走査開始...
GilShopNPC発見: 道具屋 アドミランダ -> Shop 262766 (アイテムの購入) @ グリダニア：旧市街
NPC走査完了: 走査=58497 / 名前あり=29905 / GilShop参照=1328件 / SpecialShop参照=XXX件
NPC-Shop マッピング構築完了: GilShop=771件, SpecialShop=XXX件
GilShopItemシート取得成功
GilShopItem走査完了: 合計=16319, noItemId=0, noShopId=0, noNpcMatch=4369
SpecialShop走査完了: 追加=XXX件, スキップ=XXX件
ショップデータ初期化完了: アイテム 5696件 / 販売場所 16791件 / スキップ 4369件 / 位置不明除外 XXX件
```

## NPC位置データソース

### 1. Level Sheet (プライマリ)

```csharp
// Level.Type == 8 がENpc（NPC）を示す
foreach (var level in levelSheet)
{
    if (level.Type != 8) continue;
    var objectId = level.Object.RowId;
    // Territory, Map, X, Z から位置情報を取得
}
```

### 2. LGBファイル (セカンダリ)

Level Sheetに登録されていないNPC（主にオーシャンフィッシング関連など）の位置を補完。

```csharp
// planevent.lgb と bg.lgb を解析
var lgbPaths = new[]
{
    $"bg/{bgPath}/level/planevent.lgb",
    $"bg/{bgPath}/level/bg.lgb"
};

foreach (var layer in lgbFile.Layers)
{
    foreach (var instanceObj in layer.InstanceObjects)
    {
        if (instanceObj.AssetType != LayerEntryType.EventNPC) continue;

        var eventNpc = (LayerCommon.ENPCInstanceObject)instanceObj.Object;
        var npcId = eventNpc.ParentData.ParentData.BaseId;
        var pos = instanceObj.Transform.Translation;
        // X, Z 座標を使用
    }
}
```

### 参考実装

[ItemVendorLocation](https://github.com/electr0sheep/ItemVendorLocation) プラグインを参考にLGB解析を実装。

## チャットリンクからのアイテム検索

### Agent経由でのItemId取得

チャットログ内のアイテムリンクを右クリックした際、`MenuTargetDefault`からはItemIdを直接取得できないため、FFXIVClientStructsのAgentを使用。

```csharp
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

private unsafe uint GetItemIdFromAgent(string addonName)
{
    switch (addonName)
    {
        case "ChatLog":
            var agentChatLog = AgentChatLog.Instance();
            if (agentChatLog != null)
                return agentChatLog->ContextItemId;
            break;

        case "RecipeNote":
            var agentRecipeNote = AgentRecipeNote.Instance();
            if (agentRecipeNote != null)
                return agentRecipeNote->ContextMenuResultItemId;
            break;

        case "ItemSearch":
            var agentContext = AgentContext.Instance();
            if (agentContext != null)
                return (uint)agentContext->UpdateCheckerParam;
            break;
    }
    return 0;
}
```

### フォールバック

Agent経由で取得できない場合、`IGameGui.HoveredItem`をフォールバックとして使用。

```csharp
var hoveredItem = _gameGui.HoveredItem;
if (hoveredItem > 0)
{
    var itemId = (uint)(hoveredItem % 500000); // HQ正規化
    return itemId;
}
```

## マップリンク表示

### クリック可能なマップリンク生成

```csharp
var payload = _mapService.CreateMapLink(location);
if (payload != null)
{
    builder.AddUiForeground(0x01F4); // マップリンク用の色 (500)
    builder.Add(payload);
    builder.AddText($"({location.MapX:0.0}, {location.MapY:0.0})");
    builder.Add(RawPayload.LinkTerminator); // 必須: リンク終端
    builder.AddUiForegroundOff();
}
```

**重要**: `RawPayload.LinkTerminator`を追加しないとリンクがクリックできない。

## 検索結果UIとパフォーマンス

### 表示件数とテレポボタン

- 検索結果は上位10件のみ表示する
- テレポボタンも上位10件のみ表示する
- 10件未満の場合はその範囲で全て表示する

### 描画負荷の抑制

- 表示対象10件の最寄りエーテライト情報を `SetResult` で事前キャッシュする
- 解放済みエーテライト情報を `TeleportService` でキャッシュする
- SelectableのID重複を避け、`AllowItemOverlap` を使ってボタン操作を阻害しない

## 設定項目

### ShopSearchEchoEnabled

チャットへのEcho投稿の有効/無効を切り替え。

```csharp
public bool ShopSearchEchoEnabled { get; set; } = true;
```

## 既知の制限事項

1. **イベント限定NPC** - 位置情報がないNPCは除外される（例：レルムリボーン販売NPC）
2. **インスタンスダンジョン内NPC** - 位置情報がない場合がある
3. **SpecialShop構造の変動** - Luminaバージョンにより構造が変わる可能性あり（リフレクションで対応）
4. **LGBファイル未登録NPC** - Level SheetにもLGBファイルにも位置情報がないNPCは検索不可

## ファイル構成

```
projects/XIV-Mini-Util/
├── Models/
│   └── DomainModels.cs         # ShopLocationInfo, SearchResult
├── Services/
│   ├── ShopDataCache.cs        # ショップデータ構築・キャッシュ
│   ├── ShopSearchService.cs    # 検索ロジック
│   ├── ContextMenuService.cs   # 右クリックメニュー
│   ├── MapService.cs           # マップピン設定
│   └── ChatService.cs          # Echo投稿
├── Windows/
│   ├── ShopSearchResultWindow.cs # 検索結果UI
│   └── ConfigWindow.cs         # 設定UI（優先度設定含む）
└── Plugin.cs                   # サービス統合
```

## 関連仕様書

- `.kiro/specs/item-shop-search/requirements.md` - 要件定義
- `.kiro/specs/item-shop-search/design.md` - 技術設計
- `.kiro/specs/item-shop-search/tasks.md` - 実装タスク

## 更新履歴

- 2025-12-30: 初期実装完了
- 2025-12-30: NPC位置情報の取得を実装
- 2025-12-30: 位置不明NPCのフィルタリングを追加
- 2025-12-30: SpecialShop対応を追加
- 2025-12-30: マップリンクのクリック対応（UiForeground + LinkTerminator）
- 2025-12-30: Echo表示設定（ShopSearchEchoEnabled）を追加
- 2025-12-30: チャットリンク右クリック対応（Agent経由のItemId取得）
- 2025-12-30: LGBファイル解析によるNPC位置補完を追加
- 2026-01-02: 手動NPC位置データ機能を追加（万能ルアー問題対応）
- 2026-01-02: コンテキストメニューのPrefix警告を修正（PrefixChar='M'）
- 2026-01-02: 検索診断ログ機能を追加（LogSearchDiagnostics）
- 2026-01-02: 検索結果ウィンドウ表示設定を追加（ShopSearchWindowEnabled）
- 2026-01-02: テレポ機能を追加（TeleportService）
- 2026-01-02: 検索結果ウィンドウUIを改善（アイテム情報上部表示、テレポボタン各行配置）
- 2026-01-02: 上位10件表示とテレポボタン数の上限設定を追加
- 2026-01-02: テレポ関連のキャッシュ導入で描画負荷を軽減
- 2026-01-02: 検索時/マップピン時の自動テレポ設定を追加

## 手動NPC位置データ

### 概要

一部のNPCはLevel SheetにもLGBファイルにも位置情報が登録されていません。
これらのNPCについては `ShopDataCache.ManualNpcLocations` に手動で位置データを追加しています。

### 登録済みNPC

| NPC ID | NPC名 | 場所 | 座標 | 備考 |
|--------|-------|------|------|------|
| 1005422 | よろず屋 | リムサ・ロミンサ：下甲板層 | (3.3, 12.9) | オーシャンフィッシング関連 |

### 追加方法

`ShopDataCache.cs` の `ManualNpcLocations` に以下の形式で追加：

```csharp
private static readonly Dictionary<uint, (uint TerritoryId, string AreaName, float X, float Y)> ManualNpcLocations = new()
{
    { NPC_ID, (TERRITORY_ID, "エリア名", X座標, Y座標) },
};
```

**TerritoryId参考値**:
- 128: リムサ・ロミンサ：上甲板層
- 129: リムサ・ロミンサ：下甲板層
- 130: ウルダハ：ナル回廊
- 131: ウルダハ：ザル回廊
- 132: グリダニア：新市街
- 133: グリダニア：旧市街

## 解決済みの課題

### 万能ルアー（Versatile Lure）検索問題

**症状**: 万能ルアーを販売しているNPCの一部が検索結果に表示されない

**原因**: NPC ID 1005422（リムサ・ロミンサ下甲板のよろず屋）がLevel Sheet / LGBのどちらにも位置情報がなかった

**対応**: `ManualNpcLocations` に手動で位置データを追加

**参考リンク**:
- [consolegameswiki - Versatile Lure](https://ffxiv.consolegameswiki.com/wiki/Versatile_Lure)
- [ItemVendorLocation Plugin](https://github.com/electr0sheep/ItemVendorLocation)
