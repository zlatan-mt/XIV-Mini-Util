<!-- Path: docs/notes/shop-snapshot-tools-implementation.md -->
<!-- Description: NPC販売データ追従確認用ローカルツール群の実装内容まとめ -->
<!-- Reason: ShopDataSnapshot / LodestoneColorantAudit / CompareShopSources の責務と出力仕様を短く参照できるようにするため -->
<!-- RELEVANT FILES: tools/ShopDataSnapshot/Program.cs, tools/LodestoneColorantAudit/Program.cs, tools/CompareShopSources/Program.cs, docs/notes/shop-data-snapshot-plan.md -->

# Shop Snapshot Tools 実装内容

## 目的

ゲーム内 Dalamud プラグインを起動できない状態でも、NPC販売場所検索の追従状況を確認するためのローカル開発用ツール群。

主な用途は、ローカル `sqpack` から抽出した販売データと Lodestone の公式表示を突き合わせ、カララント販売更新の追従漏れを見つけること。

## 全体方針

- 既存プラグイン本体の挙動は変更しない。
- `ShopDataCache` は直接参照しない。
- `IDataManager` / `IPluginLog` / `Configuration` など Dalamud 実行環境には依存しない。
- 生成物は `artifacts/` 配下に出し、git 管理しない。
- Lodestone は検算ソースとして使い、NPC座標やショップ紐付けの正規ソースにはしない。

## ツール構成

### ShopDataSnapshot

場所:

- `tools/ShopDataSnapshot/ShopDataSnapshot.csproj`
- `tools/ShopDataSnapshot/Program.cs`
- `tools/ShopDataSnapshot/Core/`

役割:

- ローカル FFXIV `sqpack` を Lumina 単体で読む。
- `GilShopItem` / `GilShop` / `SpecialShop` / `ENpcBase` / `ENpcResident` / `Level` / `Map` / `TerritoryType` 周辺から販売データを抽出する。
- `artifacts/shop-snapshot/shop-snapshot.json` を生成する。

主な出力:

- `generatedAt`
- `gamePath`
- `resolvedDataPath`
- `language`
- `luminaVersion`
- `recordCount`
- `summary`
- `records`

カララント判定:

- `Stain` sheet 由来を優先する。
- `Stain` typed sheet で取れない場合のみ raw fallback を使う。
- `Stain` で取れないものだけ名前 fallback を使う。
- `colorantDetection` は `stainSheet` / `nameFallback` / `none`。
- `colorantItems` は `stainSheet + nameFallback` の合算。
- 公式比較では `stainSheetColorantItemsInSnapshot` を見る。

診断 summary:

- `totalRecords`
- `uniqueItems`
- `colorantItems`
- `stainSheetColorantItemsInSnapshot`
- `stainSheetItemIds`
- `stainRawFallbackUsed`
- `nameFallbackColorantItems`
- `gilShopRecords`
- `specialShopRecords`
- `missingNpcLocationRecords`
- `missingNpcLocationUniqueNpcs`
- `missingNpcLocationUniqueShops`
- `missingNpcLocationByShopType`
- `mapInfoLoadedCount`
- `territoryNameLoadedCount`
- `missingNpcLocationSamples`

安定ソート:

```text
itemId -> shopType -> shopId -> npcName -> territoryId -> shopName -> mapId -> mapX -> mapY
```

### LodestoneColorantAudit

場所:

- `tools/LodestoneColorantAudit/LodestoneColorantAudit.csproj`
- `tools/LodestoneColorantAudit/Program.cs`

役割:

- Lodestone の item URL を直接指定して HTML を取得する。
- HTML を `artifacts/shop-snapshot/cache/lodestone/` にキャッシュする。
- item名、正規化item名、SHOP販売価格、販売状態、parse状態を JSON に出す。

販売状態:

- `available`: `SHOP販売価格` が取れた。
- `none`: 正常な Lodestone item page で、明示的な販売なし表記が取れた。
- `unknown`: 価格も明示的な販売なし表記も取れない、または正常な item page と断定できない。

安全設計:

- `SHOP販売価格` がないだけでは `none` にしない。
- 価格が取れた場合は `available` を優先する。
- `saleEvidence` は `available` / `none` の根拠だけを入れる。
- `unknown` の `saleEvidence` は `null`。
- fetch失敗時に cache があれば `parseStatus: cached` として続行する。
- cache使用警告と販売状態未判定警告は結合して `parseWarning` に残す。

summary:

- `totalRecords`
- `availableSales`
- `noSales`
- `unknown`
- `parseFailed`
- `cachedRecords`

### CompareShopSources

場所:

- `tools/CompareShopSources/CompareShopSources.csproj`
- `tools/CompareShopSources/Program.cs`

役割:

- `shop-snapshot.json` と `lodestone-colorants.json` を比較する。
- `artifacts/shop-snapshot/colorant-diff.json` と `colorant-diff.md` を生成する。
- `nameFallback` は公式差分の P0 判定に混ぜず、参考候補として分離する。

比較キー:

1. `normalizedItemName` 完全一致
2. `itemName` 完全一致

分類:

```text
P0: Lodestone販売あり / snapshot stainSheet販売なし
P1: Lodestone item名を snapshot itemId へ逆引き不可
P2: snapshot stainSheet販売あり / Lodestone販売なし
P3: snapshot nameFallback販売あり / Lodestone照合未確認
P4: Lodestone販売あり / snapshot販売あり / location missing
```

summary:

- `p0LodestoneAvailableSnapshotMissing`
- `p1LodestoneItemUnmapped`
- `p2SnapshotAvailableLodestoneNone`
- `p2SnapshotAvailableLodestoneNoneDistinctItems`
- `p3NameFallbackReference`
- `p4AvailableButLocationMissing`
- `p4AvailableButLocationMissingDistinctItems`
- `lodestoneUnknownRecords`
- `lodestoneUnknownDistinctItems`
- `lodestoneCachedRecords`

## 現在の代表結果

ShopDataSnapshot:

```text
totalRecords: 33998
uniqueItems: 11357
colorantItems: 4
stainSheetColorantItemsInSnapshot: 3
stainSheetItemIds: 43
stainRawFallbackUsed: True
nameFallbackColorantItems: 1
gilShopRecords: 23492
specialShopRecords: 10506
missingNpcLocationRecords: 19975
missingNpcLocationUniqueNpcs: 144
missingNpcLocationUniqueShops: 671
```

LodestoneColorantAudit:

```text
totalRecords: 3
availableSales: 1
noSales: 0
unknown: 2
parseFailed: 0
cachedRecords: 0
```

CompareShopSources:

```text
p0LodestoneAvailableSnapshotMissing: 0
p1LodestoneItemUnmapped: 0
p2SnapshotAvailableLodestoneNone: 0
p2SnapshotAvailableLodestoneNoneDistinctItems: 0
p3NameFallbackReference: 1
p4AvailableButLocationMissing: 2
p4AvailableButLocationMissingDistinctItems: 1
lodestoneUnknownRecords: 2
lodestoneUnknownDistinctItems: 2
lodestoneCachedRecords: 0
```

## 注意点

- `unknown` は失敗ではなく、販売有無を断定しない安全側の状態。
- Lodestone が明示的な販売なし表記を出さない場合、`none` ではなく `unknown` になる。
- `P2 = 0` でも `lodestoneUnknownRecords > 0` なら「販売なし矛盾なし」ではなく「未判定が残っている」と読む。
- `missingNpcLocationRecords` は多いが、販売有無追跡の v1 blocker にはしない。
- 将来の堅牢化候補は fixture HTML による `available` / `none` / `unknown` / `cached+unknown` の固定テスト。
