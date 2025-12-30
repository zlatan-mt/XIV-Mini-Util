# Research & Design Decisions

## Summary
- **Feature**: `item-shop-search`
- **Discovery Scope**: Extension（既存プラグインへの機能追加）
- **Key Findings**:
  - Dalamud `IContextMenu` APIでアイテム右クリックメニューへのカスタム項目追加が可能
  - Lumina経由でGilShop、GilShopItem、ENpcBase等のExcelシートからショップデータ取得可能
  - `IGameGui.OpenMapWithMapLink` と `MapLinkPayload` でマップピン設定とEcho投稿が実現可能

## Research Log

### コンテキストメニュー統合
- **Context**: アイテム右クリック時にカスタムメニュー項目を追加する方法の調査
- **Sources Consulted**:
  - [Dalamud Context Menu API](https://dalamud.dev/api/Dalamud.Game.Gui.ContextMenu/)
  - [IContextMenu.cs (GitHub)](https://github.com/goatcorp/Dalamud/blob/master/Dalamud/Plugin/Services/IContextMenu.cs)
- **Findings**:
  - `IContextMenu` サービスで `OnMenuOpened` イベントを購読
  - `IMenuOpenedArgs` から `MenuTarget` を取得し、`MenuTargetInventory` の場合にアイテム情報を取得
  - `MenuItem` クラスでカスタムメニュー項目を作成し、`args.AddMenuItem()` で追加
  - `ContextMenuType.Inventory` でインベントリコンテキストメニューを識別
- **Implications**:
  - 所持品、アーマリーチェスト、チョコボかばんは同じInventoryコンテキストで処理可能
  - チャットアイテムリンクは別途 `ContextMenuType.Default` で処理が必要な可能性

### Luminaショップデータシート
- **Context**: NPCショップ販売情報の取得方法
- **Sources Consulted**:
  - [Lumina GitHub](https://github.com/NotAdam/Lumina)
  - [Lumina.Excel NuGet](https://www.nuget.org/packages/Lumina.Excel)
  - [XIV Docs Excel Data](https://docs.xiv.zone/format/exd/)
- **Findings**:
  - 関連Excelシート:
    - `GilShop`: ギルショップ定義（ショップID、名前）
    - `GilShopItem`: ショップ販売アイテム（ショップID、アイテムID、行番号）
    - `ENpcBase`: NPC基本データ（ショップへの参照を含む）
    - `ENpcResident`: NPC表示名
    - `Level`: NPC配置座標（TerritoryType、X、Y、Z）
    - `TerritoryType`: エリア情報
    - `Map`: マップ情報（座標変換用）
  - データ関係:
    ```
    Item → GilShopItem (ItemId) → GilShop (ShopId) → ENpcBase (Data参照) → Level (位置情報)
    ```
- **Implications**:
  - 逆引きインデックス（ItemId → ShopLocations）を起動時に構築してキャッシュ
  - ENpcBase.Data配列内のショップ参照を解析する必要あり
  - 座標変換: Level.X/Y → Map座標 への変換計算が必要

### マップピン・Echo投稿
- **Context**: 検索結果をマップとチャットに表示する方法
- **Sources Consulted**:
  - [GameGui.cs (GitHub)](https://github.com/goatcorp/Dalamud/blob/master/Dalamud/Game/Gui/GameGui.cs)
  - [MapLinker Plugin](https://github.com/Bluefissure/MapLinker)
- **Findings**:
  - `IGameGui.OpenMapWithMapLink(MapLinkPayload)`: マップを開いてフラグを設定
  - `MapLinkPayload`: マップリンク情報をエンコード（TerritoryType、MapId、X、Y座標）
  - `IChatGui.Print()`: Echoにメッセージを投稿
  - `SeString` と `MapLinkPayload` を組み合わせてクリック可能なマップリンクを生成
- **Implications**:
  - 座標はマップ座標系（1-42程度の範囲）に変換が必要
  - 複数場所をEcho投稿する場合は複数行または連続投稿

### マーケット取引不可属性
- **Context**: 店売りアイテムの判定基準
- **Sources Consulted**: Lumina Item シート
- **Findings**:
  - `Item.ItemSearchCategory`: 0の場合はマーケット検索不可
  - `Item.IsUntradable`: true の場合は取引不可
  - ただし店売りアイテムでもマーケット取引不可のものがある（クエストアイテム等）
- **Implications**:
  - マーケット取引不可 ≠ 店売りなし ではない
  - 実際のGilShopItemデータで販売有無を判定するのが正確

## Architecture Pattern Evaluation

| Option | Description | Strengths | Risks / Limitations | Notes |
|--------|-------------|-----------|---------------------|-------|
| Service-Based | 既存パターン踏襲、ShopSearchServiceを追加 | 一貫性、テスト容易 | なし | 採用 |
| Event-Driven | メニュー選択をイベントで通知 | 疎結合 | 複雑化 | 不採用 |

## Design Decisions

### Decision: データキャッシュ戦略
- **Context**: 大量のショップデータを効率的に検索する必要がある
- **Alternatives Considered**:
  1. 毎回Luminaシートをスキャン — シンプルだが遅い
  2. 起動時に逆引きインデックスを構築 — 初期化コストはあるが検索高速
  3. 遅延構築（初回検索時） — 初回のみ遅延
- **Selected Approach**: 起動時に逆引きインデックスを構築
- **Rationale**: 検索は頻繁に行われる想定、500ms以内の応答要件を満たすため
- **Trade-offs**: プラグイン起動時に数秒のロード時間が発生する可能性
- **Follow-up**: インデックス構築を非同期化して起動ブロックを回避

### Decision: 優先度ソートの実装方式
- **Context**: ユーザー設定の優先度に基づいてエリアをソート
- **Alternatives Considered**:
  1. TerritoryTypeIdのリストで優先度管理
  2. エリア名文字列のリストで優先度管理
  3. 優先度スコア（数値）をエリアごとに設定
- **Selected Approach**: TerritoryTypeIdのリストで優先度管理
- **Rationale**: 言語非依存、パッチ間で安定、既存のTerritoryTypeデータと整合
- **Trade-offs**: UIでの表示時にエリア名への変換が必要
- **Follow-up**: 優先度未設定エリアはリスト末尾に配置

### Decision: Echo投稿フォーマット
- **Context**: 上位3件の販売場所を見やすく表示
- **Selected Approach**: 各販売場所を1行ずつ、クリック可能なマップリンク付きで投稿
- **Rationale**: ユーザーが即座にマップを開ける、情報が整理されている
- **Example Output**:
  ```
  [販売場所検索] ハイポーション
  1. リムサ・ロミンサ：下甲板層 <マップリンク>
  2. ウルダハ：ナル回廊 <マップリンク>
  3. グリダニア：旧市街 <マップリンク>
  ```

## Risks & Mitigations
- **リスク1**: GilShopItem→ENpcBase→Levelの結合が複雑でパフォーマンス懸念
  - **緩和**: 起動時に完全なインデックスを構築し、検索時は O(1) ルックアップ
- **リスク2**: ゲームパッチでExcelシート構造が変更される可能性
  - **緩和**: Lumina.Excel型定義を使用し、型変更時はビルドエラーで検出
- **リスク3**: 一部NPCのLevel情報が欠落している可能性
  - **緩和**: Level情報がないショップは結果から除外、ログで警告

## References
- [Dalamud Context Menu API](https://dalamud.dev/api/Dalamud.Game.Gui.ContextMenu/) — コンテキストメニュー統合
- [Lumina GitHub](https://github.com/NotAdam/Lumina) — Excelシートアクセス
- [MapLinker Plugin](https://github.com/Bluefissure/MapLinker) — マップリンク実装参考
- [IContextMenu.cs](https://github.com/goatcorp/Dalamud/blob/master/Dalamud/Plugin/Services/IContextMenu.cs) — コンテキストメニューAPI
