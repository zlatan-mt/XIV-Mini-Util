<!-- Path: docs/notes/shop-snapshot-tools-usage.md -->
<!-- Description: NPC販売データ追従確認用ローカルツール群の使い方 -->
<!-- Reason: ゲーム起動なしで snapshot 生成、Lodestone検算、比較まで再実行できるようにするため -->
<!-- RELEVANT FILES: tools/ShopDataSnapshot/Program.cs, tools/LodestoneColorantAudit/Program.cs, tools/CompareShopSources/Program.cs, docs/notes/shop-snapshot-tools-implementation.md -->

# Shop Snapshot Tools 使い方

## 前提

- Windows / PowerShell 前提。
- FFXIV のローカルインストール済み game data を使う。
- 既定 game path は `D:\SquareEnix\FINAL FANTASY XIV - A Realm Reborn`。
- ゲーム本体と Dalamud プラグインを起動する必要はない。
- 出力先は `artifacts/shop-snapshot/`。
- `artifacts/` は `.gitignore` 対象なので、生成物はコミットしない。

## 1. ビルド確認

```powershell
dotnet build .\projects\XIV-Mini-Util\XivMiniUtil.csproj
dotnet build .\tools\ShopDataSnapshot\ShopDataSnapshot.csproj
dotnet build .\tools\LodestoneColorantAudit\LodestoneColorantAudit.csproj
dotnet build .\tools\CompareShopSources\CompareShopSources.csproj
```

## 2. sqpack から snapshot を生成する

```powershell
dotnet run --project .\tools\ShopDataSnapshot\ShopDataSnapshot.csproj -- `
  --game-path "D:\SquareEnix\FINAL FANTASY XIV - A Realm Reborn" `
  --language ja `
  --out artifacts\shop-snapshot\shop-snapshot.json
```

出力:

```text
artifacts/shop-snapshot/shop-snapshot.json
```

見るべき summary:

- `totalRecords`
- `uniqueItems`
- `colorantItems`
- `stainSheetColorantItemsInSnapshot`
- `nameFallbackColorantItems`
- `stainRawFallbackUsed`
- `missingNpcLocationRecords`
- `missingNpcLocationUniqueNpcs`
- `missingNpcLocationUniqueShops`

カララント比較では、`colorantItems` ではなく `stainSheetColorantItemsInSnapshot` を主に見る。
`colorantItems` は `stainSheet + nameFallback` の合算。

## 3. Lodestone の既知URLを検算する

```powershell
dotnet run --project .\tools\LodestoneColorantAudit\LodestoneColorantAudit.csproj -- `
  --item-url "https://jp.finalfantasyxiv.com/lodestone/playguide/db/item/873f9a66852/?patch=latest" `
  --item-url "https://jp.finalfantasyxiv.com/lodestone/playguide/db/item/5e02f2a47cf/?patch=latest" `
  --item-url "https://jp.finalfantasyxiv.com/lodestone/playguide/db/item/e0dc79849ed/?patch=latest" `
  --out artifacts\shop-snapshot\lodestone-colorants.json
```

出力:

```text
artifacts/shop-snapshot/lodestone-colorants.json
artifacts/shop-snapshot/cache/lodestone/*.html
```

見るべき summary:

- `availableSales`
- `noSales`
- `unknown`
- `parseFailed`
- `cachedRecords`

`shopSaleStatus` の読み方:

- `available`: `SHOP販売価格` が取れた。
- `none`: 明示的な販売なし表記が取れた。
- `unknown`: 価格も明示的な販売なし表記も取れないため、販売有無を断定しない。

`unknown` は失敗ではない。
Lodestone が販売なしを明示しないHTMLの場合、販売なし候補は `none` ではなく `unknown` になる。

## 4. snapshot と Lodestone 結果を比較する

```powershell
dotnet run --project .\tools\CompareShopSources\CompareShopSources.csproj -- `
  --snapshot artifacts\shop-snapshot\shop-snapshot.json `
  --lodestone artifacts\shop-snapshot\lodestone-colorants.json `
  --json-out artifacts\shop-snapshot\colorant-diff.json `
  --md-out artifacts\shop-snapshot\colorant-diff.md
```

出力:

```text
artifacts/shop-snapshot/colorant-diff.json
artifacts/shop-snapshot/colorant-diff.md
```

比較分類:

```text
P0: Lodestone販売あり / snapshot stainSheet販売なし
P1: Lodestone item名を snapshot itemId へ逆引き不可
P2: snapshot stainSheet販売あり / Lodestone販売なし
P3: snapshot nameFallback販売あり / Lodestone照合未確認
P4: Lodestone販売あり / snapshot販売あり / location missing
```

優先して見る順:

1. `P0`
2. `P1`
3. `P2`
4. `P3`
5. `P4`

重要:

- `nameFallback` は P0 に混ぜない。
- `P3` は公式販売差分ではなく参考候補。
- `P2 = 0` でも `lodestoneUnknownRecords > 0` なら、販売なし矛盾が無いのではなく未判定が残っている。
- `P4` は販売有無の差分ではなく、snapshot 側の location 補助情報。

## 5. 代表的な確認コマンド

snapshot内のカララント候補を確認する:

```powershell
$json = Get-Content artifacts\shop-snapshot\shop-snapshot.json -Raw | ConvertFrom-Json

$json.records |
  Where-Object { $_.isColorant } |
  Sort-Object colorantDetection,itemId,shopType,shopId |
  Select-Object itemId,itemName,colorantDetection,shopType,shopId,shopName,npcName,price |
  Format-Table -Auto
```

Lodestoneで `unknown` になった item を確認する:

```powershell
$lodestone = Get-Content artifacts\shop-snapshot\lodestone-colorants.json -Raw | ConvertFrom-Json

$lodestone.records |
  Where-Object { $_.shopSaleStatus -eq "unknown" } |
  Select-Object itemName,itemPageUrl,saleEvidence,parseStatus,parseWarning |
  Format-List
```

比較結果の summary だけ確認する:

```powershell
$diff = Get-Content artifacts\shop-snapshot\colorant-diff.json -Raw | ConvertFrom-Json
$diff.summary | Format-List
```

## 6. よくある読み方

### `colorantItems: 4` だが公式販売は3件に見える

`colorantItems` は `nameFallback` を含む。
公式販売候補として見るのは `stainSheetColorantItemsInSnapshot`。

`nameFallbackColorantItems: 1` の候補は、`P3` として別枠で見る。

### Lodestone で `unknown: 2` が出る

`SHOP販売価格` も明示的な販売なし表記も取れなかった状態。
販売なしとは断定していない。

### `P2 = 0` なのに snapshot 側に販売がある

Lodestone側が `unknown` の場合、P2には入れない。
P2は「Lodestone が明示的に販売なしと判定できたもの」だけ。

### `P4` が出る

販売有無は一致しているが、snapshot 側の location が欠けている。
Phase 3 の主目的では blocker にしない。

## 7. 次の改善候補

- Lodestone検索モードの追加。
- `data/lodestone-item-name-map.json` のような手動mapping file。
- fixture HTML による判定テスト。
- `LodestoneColorantAudit` の parser を HTML構造により強く寄せる。
- `ShopSnapshotCore` を将来 `src/XivMiniUtil.ShopCore` に昇格するか検討する。
