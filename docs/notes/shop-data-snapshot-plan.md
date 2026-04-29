<!-- Path: docs/notes/shop-data-snapshot-plan.md -->
<!-- Description: ゲーム起動なしでNPC販売データ追従を確認するためのShopDataSnapshot実装内容と後続計画 -->
<!-- Reason: Lodestone検算や将来のShopDataCache反映判断の前提を残すため -->
<!-- RELEVANT FILES: tools/ShopDataSnapshot/Program.cs, tools/ShopDataSnapshot/Core/ShopSnapshotBuilder.cs, tools/ShopDataSnapshot/Core/ShopSnapshotRecord.cs, projects/XIV-Mini-Util/Services/Shop/ShopDataCache.cs -->

# ShopDataSnapshot 実装内容と後続計画

## 目的

NPC販売場所検索の追従確認を、ゲーム内 Dalamud プラグイン起動に依存せず行えるようにする。

特にカララント販売データの更新確認を主目的とし、v1ではローカル `sqpack` を Lumina 単体で読み、NPC販売データの snapshot JSON を生成する。

## v1の方針

- `ShopDataSnapshot` 単独MVPとして実装する。
- 既存プラグイン本体の挙動は変更しない。
- `ShopDataCache` は console tool から直接参照しない。
- Dalamud の `IDataManager`、`IPluginLog`、`Configuration` には依存しない。
- Lodestone は v1では使わず、後続フェーズの検算ソースにする。
- 生成物は `artifacts/` 配下に出し、リポジトリには含めない。

## 追加した実装

### ShopDataSnapshot

- `tools/ShopDataSnapshot/ShopDataSnapshot.csproj`
- `tools/ShopDataSnapshot/Program.cs`
- `tools/ShopDataSnapshot/Core/ShopSnapshotBuilder.cs`
- `tools/ShopDataSnapshot/Core/ShopSnapshotRecord.cs`
- `tools/ShopDataSnapshot/Core/SnapshotOptions.cs`
- `tools/ShopDataSnapshot/Core/SnapshotReflection.cs`
- `tools/ShopDataSnapshot/Core/MapCoordinateConverter.cs`

`ShopDataSnapshot` は独立した `.NET 10` console app として追加した。
依存は tool 側 `csproj` に閉じており、既存プラグイン本体へ `ProjectReference` や Dalamud 依存は追加していない。

### LodestoneColorantAudit

- `tools/LodestoneColorantAudit/LodestoneColorantAudit.csproj`
- `tools/LodestoneColorantAudit/Program.cs`

Lodestone item URL を直接指定し、アイテム名、正規化アイテム名、`SHOP販売価格`、取得日時、parse status を JSON に保存する。
HTML は `artifacts/shop-snapshot/cache/lodestone/` にキャッシュする。
`SHOP販売価格` も明示的な販売なし表記も取れない場合は、HTML構造変更やエラーページを販売なしと誤判定しないよう `unknown` にする。

### CompareShopSources

- `tools/CompareShopSources/CompareShopSources.csproj`
- `tools/CompareShopSources/Program.cs`

`shop-snapshot.json` と `lodestone-colorants.json` を比較し、P0-P4 の分類で `colorant-diff.json` と `colorant-diff.md` を出力する。
P0 判定では `colorantDetection == "stainSheet"` のみを公式候補として扱い、`nameFallback` は P3 の参考候補に分離する。

### CLI

既定値:

```powershell
dotnet run --project .\tools\ShopDataSnapshot\ShopDataSnapshot.csproj -- `
  --game-path "D:\SquareEnix\FINAL FANTASY XIV - A Realm Reborn" `
  --language ja `
  --out artifacts\shop-snapshot\shop-snapshot.json
```

オプション:

- `--game-path`: FFXIV install root または `sqpack` 直下。既定は `D:\SquareEnix\FINAL FANTASY XIV - A Realm Reborn`
- `--language`: 出力言語。既定は `ja`
- `--out`: snapshot JSON 出力先。既定は `artifacts\shop-snapshot\shop-snapshot.json`
- `--help`: 使用方法を表示する

`--game-path` が存在しない場合は stderr に理由を出し、exit code `1` で終了する。

### JSON出力

出力先:

```text
artifacts/shop-snapshot/shop-snapshot.json
```

メタ情報:

- `generatedAt`
- `gamePath`
- `resolvedDataPath`
- `language`
- `luminaVersion`
- `recordCount`

レコード項目:

- `itemId`
- `itemName`
- `isColorant`
- `colorantDetection`
- `shopType`
- `shopId`
- `shopName`
- `npcName`
- `territoryId`
- `areaName`
- `mapX`
- `mapY`
- `price`
- `conditionNote`
- `subAreaName`
- `mapId`
- `isManuallyAdded`

安定ソート:

```text
itemId -> shopType -> shopId -> npcName -> territoryId -> shopName -> mapId -> mapX -> mapY
```

### カララント判定

`isColorant` は日本語名の部分一致を主判定にしない。

優先順位:

1. `Stain` typed sheet から `Item` / `ItemId` / `ItemID` / `Item1` / `Item2` を reflection で取得する。
2. typed sheet で取得できない場合のみ、raw sheet fallback を使う。
3. `Stain` から取得できない場合のみ、名前fallbackで補助判定する。

`colorantDetection` は次の値を出す。

- `stainSheet`
- `nameFallback`
- `none`

summary には、`Stain` 由来判定がどの程度機能しているか確認するため次を出す。

- `stainSheetItemIds`
- `stainRawFallbackUsed`
- `nameFallbackColorantItems`

### 座標と診断

Map座標変換は offset を反映する。

```csharp
((rawPosition + offset) * scale + 1024f) / 2048f
```

NPC位置が引けない販売データは捨てず、販売有無の追跡に使えるよう record として残す。
summary には原因調査用に次を出す。

- `missingNpcLocationRecords`
- `missingNpcLocationUniqueNpcs`
- `missingNpcLocationUniqueShops`
- `missingNpcLocationByShopType`
- `missingNpcLocationSamples`
- `mapInfoLoadedCount`
- `territoryNameLoadedCount`

## 検証結果

実行済み:

```powershell
dotnet build .\projects\XIV-Mini-Util\XivMiniUtil.csproj
dotnet build .\tools\ShopDataSnapshot\ShopDataSnapshot.csproj
dotnet build .\tools\LodestoneColorantAudit\LodestoneColorantAudit.csproj
dotnet build .\tools\CompareShopSources\CompareShopSources.csproj
dotnet run --project .\tools\ShopDataSnapshot\ShopDataSnapshot.csproj -- --game-path "D:\SquareEnix\FINAL FANTASY XIV - A Realm Reborn" --language ja --out artifacts\shop-snapshot\shop-snapshot.json
dotnet run --project .\tools\LodestoneColorantAudit\LodestoneColorantAudit.csproj -- --item-url "https://jp.finalfantasyxiv.com/lodestone/playguide/db/item/873f9a66852/?patch=latest" --item-url "https://jp.finalfantasyxiv.com/lodestone/playguide/db/item/5e02f2a47cf/?patch=latest" --item-url "https://jp.finalfantasyxiv.com/lodestone/playguide/db/item/e0dc79849ed/?patch=latest" --out artifacts\shop-snapshot\lodestone-colorants.json
dotnet run --project .\tools\CompareShopSources\CompareShopSources.csproj -- --snapshot artifacts\shop-snapshot\shop-snapshot.json --lodestone artifacts\shop-snapshot\lodestone-colorants.json --json-out artifacts\shop-snapshot\colorant-diff.json --md-out artifacts\shop-snapshot\colorant-diff.md
```

確認済み:

- 既存プラグイン本体のビルドは成功。
- `ShopDataSnapshot` のビルドは成功。
- `LodestoneColorantAudit` のビルドは成功。
- `CompareShopSources` のビルドは成功。
- ゲームを起動せず snapshot JSON を生成できる。
- Lodestone 3 URL を直接取得し、HTMLキャッシュと `lodestone-colorants.json` を生成できる。
- snapshot と Lodestone 結果から `colorant-diff.json` / `colorant-diff.md` を生成できる。
- 不正な `--game-path` は stderr に理由を出し、exit code `1` で終了する。
- `artifacts/` と各 tool の `bin|obj` は `.gitignore` 対象。

ローカル実行時の代表値:

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

Lodestone 3 URL 直接検算の代表値:

```text
totalRecords: 3
availableSales: 1
noSales: 0
unknown: 2
parseFailed: 0
cachedRecords: 0
```

比較結果の代表値:

```text
p0LodestoneAvailableSnapshotMissing: 0
p1LodestoneItemUnmapped: 0
p2SnapshotAvailableLodestoneNone: 0
p2SnapshotAvailableLodestoneNoneDistinctItems: 0
p3NameFallbackReference: 1
p4AvailableButLocationMissing: 2
p4AvailableButLocationMissingDistinctItems: 1
```

## 現時点の評価

`ShopDataSnapshot` v1 は完了扱いでよい。

現時点では、販売有無の追従確認を主目的にする。座標欠落は多いが、missing location の件数、unique NPC、unique shop、sample を出せるため、v1の blocker にはしない。

`stainRawFallbackUsed: True` は注意点として残る。ただし `nameFallbackColorantItems` が少なく、カララント判定が名前fallbackへ過度依存している状態ではない。

## カララント統合後の比較方針

直近アップデートでNPC販売カララントが3件に統合された可能性があるため、Lodestone比較の主判定では `colorantDetection == "stainSheet"` のみを公式販売候補として扱う。

`colorantDetection == "nameFallback"` は参考候補とし、`Lodestone販売あり / snapshot販売なし` のP0判定には含めない。

snapshot summary の `colorantItems` は `stainSheet + nameFallback` の合算であるため、Phase 3/4 では以下の分割件数を確認する。

- `stainSheetColorantItemsInSnapshot`
- `nameFallbackColorantItems`

## 後続計画

### Phase 3: LodestoneColorantAudit

目的:
Lodestone の最新検索結果を公式検算ソースとして取得し、snapshot との比較材料を作る。

方針:

- 最初は検索モードではなく、既知の item URL 直接指定モードを使う。
- latest検索に対応する。
- ページングに対応する。
- HTMLキャッシュを `artifacts/shop-snapshot/cache/lodestone/` に保存できるようにする。
- 取得失敗は snapshot 生成とは独立させ、warning 扱いにする。
- Lodestone結果には `sourceUrl`、`fetchedAt`、`parseStatus`、`saleEvidence`、`parseWarning` を持たせる。
- `SHOP販売価格` も明示的な販売なし表記も取れない場合は `unknown` とする。

seed URL:

```text
https://jp.finalfantasyxiv.com/lodestone/playguide/db/item/873f9a66852/?patch=latest
https://jp.finalfantasyxiv.com/lodestone/playguide/db/item/5e02f2a47cf/?patch=latest
https://jp.finalfantasyxiv.com/lodestone/playguide/db/item/e0dc79849ed/?patch=latest
```

比較分類:

1. Lodestone販売あり / snapshot販売あり
2. Lodestone販売あり / snapshot販売なし
3. Lodestone販売なし / snapshot販売あり
4. Lodestone item名を snapshot itemId へ逆引き不可

最優先で確認するのは `Lodestone販売あり / snapshot販売なし`。

座標欠落は Phase 3 の主目的から外し、`snapshot販売ありだが location missing` の補助情報に留める。

### Phase 4: CompareShopSources

目的:
`shop-snapshot.json` と Lodestone 取得結果を比較し、追従が必要な候補を上部に集約する。

出力:

- `artifacts/shop-snapshot/colorant-diff.md`
- `artifacts/shop-snapshot/colorant-diff.json`

優先表示:

- P0: Lodestone販売あり / snapshot stainSheet販売なし
- Lodestone item名を snapshot itemId へ逆引き不可
- snapshot販売ありだが Lodestone販売なし
- P3: snapshot nameFallback販売あり / Lodestone照合未確認

P0には `nameFallback` を混ぜない。`nameFallback` 由来の候補は、公式販売差分ではなく参考候補として別枠表示する。

### Phase 5: overrides

必要な場合だけ `data/shop-overrides.json` を設計する。

`ShopDataCache` へのマージやプラグイン本体への自動反映は別作業にする。
v1のローカル調査ツールとは分け、既存検索挙動を壊さない形で判断する。

## 注意点

- このツールはローカル開発用であり、公開配布を前提にしない。
- Lodestone は検算ソースであり、NPC座標やショップ紐付けの正規ソースにはしない。
- `conditionNote` の完全性は v1 合格条件にしない。
- 将来 `ShopSnapshotCore` を `src/XivMiniUtil.ShopCore` に昇格するかは、Lodestone比較後の抽出品質を見て判断する。
