# アイテムショップ検索機能 - 実装ドキュメント

## 概要

FFXIVのアイテムを右クリックして、そのアイテムを販売しているNPCショップの場所を検索・表示する機能。

## 機能一覧

1. **コンテキストメニュー統合** - アイテム右クリックで「販売場所を検索」メニューを表示
2. **ショップデータキャッシュ** - 起動時にLuminaシートからショップデータを非同期で構築
3. **マップリンク生成** - 検索結果をチャットにマップリンク付きで表示
4. **検索結果ウィンドウ** - 複数件ある場合にImGuiウィンドウでリスト表示
5. **エリア優先度設定** - 三大都市などを優先して表示

## アーキテクチャ

### 主要コンポーネント

```
Plugin.cs
├── ShopDataCache          # ショップデータの読み込み・キャッシュ
├── ShopSearchService      # 検索ロジック・結果出力
├── ContextMenuService     # 右クリックメニュー統合
├── MapService             # マップピン設定
├── ChatService            # Echo投稿
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

## 既知の制限事項

1. **イベント限定NPC** - 位置情報がないNPCは除外される（例：レルムリボーン販売NPC）
2. **インスタンスダンジョン内NPC** - 位置情報がない場合がある
3. **SpecialShop構造の変動** - Luminaバージョンにより構造が変わる可能性あり（リフレクションで対応）

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
