# tools inventory

作成日: 2026-06-10

Phase 1 の `tools/` 棚卸し結果です。2026-06-11 に現役ゲートではない調査用ツールを main から削除し、`tools/` は `CharaSelectLogicTests` のみを保持しました。

## 現行ゲートとして保持

### CharaSelectLogicTests

- パス: `tools/CharaSelectLogicTests/CharaSelectLogicTests.csproj`
- 用途: TitleBackground / CharaSelect の純ロジック検証。`scripts/verify-refactor-phase.ps1` の完了ゲート。
- 判定: 保持。
- 根拠: リファクタリング計画の各フェーズゲートが `CharaSelectLogicTests` を前提にしている。

## main から削除済み

### ShopDataSnapshot

- パス: `tools/ShopDataSnapshot/ShopDataSnapshot.csproj`
- 用途: ゲーム起動なしでローカル sqpack から NPC 販売 snapshot JSON を生成する調査ツール。
- 出力: `artifacts/shop-snapshot/shop-snapshot.json`
- 判定: 削除済み。
- 根拠: 現役ビルドゲートではない。履歴資料は `docs/archive/shop/` に退避済み。

### LodestoneColorantAudit

- パス: `tools/LodestoneColorantAudit/LodestoneColorantAudit.csproj`
- 用途: Lodestone のカララント販売情報を取得し、snapshot 比較用 JSON を生成する調査ツール。
- 出力: `artifacts/shop-snapshot/lodestone-colorants.json`
- 判定: 削除済み。
- 根拠: ShopDataSnapshot と対になる一回限りの検算補助。現役ビルドゲートではない。

### CompareShopSources

- パス: `tools/CompareShopSources/CompareShopSources.csproj`
- 用途: `shop-snapshot.json` と `lodestone-colorants.json` を比較し、差分 JSON / Markdown を生成する調査ツール。
- 出力: `artifacts/shop-snapshot/colorant-diff.json`, `artifacts/shop-snapshot/colorant-diff.md`
- 判定: 削除済み。
- 根拠: ShopDataSnapshot / LodestoneColorantAudit の比較補助。現役ビルドゲートではない。

## 確認結果

- 削除前の棚卸しでは 4プロジェクトすべて Debug build 成功。
- `bin/` / `obj/` 配下にコミット済みファイルなし。
- `.gitignore` は `bin/` / `obj/` を除外済み。
- 2026-06-11 時点の現役 `tools/` は `CharaSelectLogicTests` のみ。
