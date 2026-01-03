<!-- Path: docs/implementation_summary.md -->
<!-- Description: 現状までの実装内容と調査結果をまとめたメモ -->
<!-- Reason: 検証や次の修正判断の前提を共有するため -->
<!-- RELEVANT FILES: projects/XIV-Mini-Util/Services/ContextMenuService.cs, projects/XIV-Mini-Util/Services/ShopDataCache.cs, plans/feature_implementation_plan.md -->
# 実装サマリー（現状）

## 対象
- 機能1: 染色画面（ColorantColoring）で右クリック時に販売場所検索を出す
- 機能2: 設定画面のアイテム名検索

## 追加・変更された主要実装

### 1) 色・アイテム取得経路の拡張
- `ContextMenuService.TryGetItemId` に `ColorantColoring` 判定を追加し、専用の取得ロジックに分岐。
- `GetColorantItemIdFromAddon` で Addon（AtkValues）からの解析を実装。
- `GetItemIdFromColorantAgent`（AgentColorant）も併用して調査。

### 2) Addon（AtkValues）解析の方針
- テキスト候補を抽出し、`カララント:` 形式のラベルを優先的に解析。
- 右クリック時の差分（数値の変化）を記録し、選択インデックス候補を推定。
- 色ラベル一覧と数値インデックスの対応を試み、色の選択Indexを特定。
- アイテム候補（テレビン油など）は、差分インデックスが大きい場合に優先するルールを追加。

### 3) 追加されたデバッグ出力
- `ColorantDebug` の詳細ログ（色ラベル、数値差分、候補一覧、両方候補など）。
- `ContextMenuDebug` で Addon/Target/HoveredItem を出力。

### 4) 判定の競合対策
- 同一右クリック中に2回評価されるため、直近の決定を短時間キャッシュ。
- ただし、候補競合時は色が優先されるため、アイテムが上書きされるケースが残っている。

## 現状の挙動（ログベース）

### 正常に確認できたこと
- 色ラベル候補は正しく取得できている。
- 色の右クリック時に色のIDが取得できることがある。
- テレビン油の文字列がAdd-onから取得できる。

### まだ不安定なこと
- テレビン油とカララントが同時に候補になる場合、色が優先されることがある。
- 選択Index補正の試行でズレが発生したため、補正は削除した。
- キャッシュによる二重評価の上書きは一部抑止できるが、判定ロジックが安定していない。

## 次の修正方針（現行）
- 「両方候補が出た場合」の優先順位を、差分インデックスで切り替える。
- DiffIndex が大きい（例: 347）場合はアイテム選択とみなして優先する。
- 小さい差分は色選択とみなし、色を優先する。

## 影響ファイル
- `projects/XIV-Mini-Util/Services/ContextMenuService.cs` が主な変更対象。
- `ShopDataCache` と `ShopSearchService` は参照先として使用。

