# Requirements Document

## Introduction
本機能は、ゲーム内のアイテムを右クリックした際に「販売場所を検索」オプションを追加し、そのアイテムを販売しているNPCショップの位置情報を検索・表示するものである。ユーザーが設定した優先度に基づいて最適な販売場所を提案し、マップへのピン表示およびEchoへの投稿を行う。

## Requirements

### Requirement 1: コンテキストメニュー統合
**Objective:** As a プレイヤー, I want アイテムの右クリックメニューから販売場所を検索したい, so that 素早く購入可能な場所を特定できる

#### Acceptance Criteria
1.1 When プレイヤーが所持品内のアイテムを右クリックする, the Plugin shall 「販売場所を検索」メニュー項目を表示する
1.2 When プレイヤーがアーマリーチェスト内のアイテムを右クリックする, the Plugin shall 「販売場所を検索」メニュー項目を表示する
1.3 When プレイヤーがチョコボかばん内のアイテムを右クリックする, the Plugin shall 「販売場所を検索」メニュー項目を表示する
1.4 When プレイヤーがチャットで共有されたアイテムリンクを右クリックする, the Plugin shall 「販売場所を検索」メニュー項目を表示する
1.5 If 対象アイテムがNPCショップで販売されていない場合, the Plugin shall 「販売場所を検索」メニュー項目をグレーアウトまたは非表示にする

### Requirement 2: 販売場所検索
**Objective:** As a プレイヤー, I want 選択したアイテムの販売場所を検索したい, so that どこで購入できるか把握できる

#### Acceptance Criteria
2.1 When プレイヤーが「販売場所を検索」を選択する, the Plugin shall 該当アイテムを販売している全NPCショップを検索する
2.2 When 検索が完了する, the Plugin shall ユーザー設定の優先度に基づいて販売場所をソートする
2.3 The Plugin shall 検索結果に各販売場所のエリア名、NPC名、マップ座標（X, Y）を含める
2.4 If 販売場所が見つからない場合, the Plugin shall 「このアイテムはNPCショップでは販売されていません」と通知する
2.5 The Plugin shall 「マーケット取引不可」属性を持つアイテムはNPCショップで販売されていないものとして扱う

### Requirement 3: 検索結果表示
**Objective:** As a プレイヤー, I want 検索結果を分かりやすく確認したい, so that 最適な購入場所を選択できる

#### Acceptance Criteria
3.1 When 検索結果が1件以上存在する, the Plugin shall 優先度上位3件までの販売場所をチャットEchoに投稿する
3.2 If 検索結果が3件未満の場合, the Plugin shall 存在する全ての販売場所をEchoに投稿する
3.3 The Plugin shall Echo投稿に販売場所名、NPC名、座標情報を含める
3.4 When 複数の販売場所が存在する, the Plugin shall 優先度順にリスト表示するUIを提供する
3.5 The Plugin shall 各販売場所の選択により詳細情報（価格、必要条件等）を表示する

### Requirement 4: マップ連携
**Objective:** As a プレイヤー, I want 販売場所をマップ上で確認したい, so that 実際に移動しやすくなる

#### Acceptance Criteria
4.1 When 検索結果が表示される, the Plugin shall 最優先の販売場所にマップピン（フラグ）を設定する
4.2 When プレイヤーが検索結果リストから別の場所を選択する, the Plugin shall 選択した場所にマップピンを移動する
4.3 The Plugin shall マップピンにNPC名とアイテム名を含むツールチップを設定する

### Requirement 5: 優先度設定
**Objective:** As a プレイヤー, I want 販売場所の優先度をカスタマイズしたい, so that 自分がよく行くエリアを優先表示できる

#### Acceptance Criteria
5.1 The Plugin shall 設定画面でエリア優先度リストを編集可能にする
5.2 The Plugin shall ドラッグ＆ドロップまたは上下ボタンで優先度を変更可能にする
5.3 When 設定が保存される, the Plugin shall 即座に検索結果のソート順に反映する
5.4 The Plugin shall デフォルトの優先度プリセット（例：三大都市優先）を提供する
5.5 The Plugin shall 優先度設定をConfiguration.jsonに永続化する

### Requirement 6: データソース管理
**Objective:** As a プレイヤー, I want 常に正確な販売場所データを利用したい, so that 無駄な移動を避けられる

#### Acceptance Criteria
6.1 The Plugin shall ゲーム内データ（Luminaライブラリ経由）を主データソースとして使用する
6.2 The Plugin shall GilShop、GilShopItem、ENpcBase、ENpcResidentシートからショップ情報を取得する
6.3 The Plugin shall 起動時にデータをキャッシュし、ゲームセッション中は再読み込みを避ける
6.4 If ゲームデータから販売場所が取得できない場合, the Plugin shall エラーログを出力し、ユーザーに通知する

### Requirement 7: パフォーマンス要件
**Objective:** As a プレイヤー, I want 検索が素早く完了してほしい, so that ゲームプレイを中断しない

#### Acceptance Criteria
7.1 The Plugin shall 検索処理を500ms以内に完了する
7.2 The Plugin shall 検索処理中にゲームのフレームレートを低下させない（非同期処理）
7.3 The Plugin shall キャッシュ済みデータを使用して繰り返し検索を高速化する

## Data Source Discussion

### 背景
ユーザー要件では「Eorzea Database」をデータソースとして指定しつつ、直接アクセスは禁止されている。

### 推奨アプローチ: ゲーム内データ活用
FFXIVのゲームデータにはLuminaライブラリ経由でアクセス可能な以下のシートが含まれる：
- **GilShop**: ギルショップの基本情報
- **GilShopItem**: ショップで販売されるアイテム一覧
- **ENpcBase / ENpcResident**: NPC情報と配置場所
- **Map / TerritoryType**: エリア・マップ情報

このアプローチの利点：
1. **常に最新**: ゲームバージョンと同期
2. **外部依存なし**: ネットワーク不要
3. **既存パターン踏襲**: 現在のInventoryService等と同様の実装

### 検討事項
- 特殊ショップ（期間限定、条件付き）の扱い
- ギルショップ以外（軍票交換、スクリップ交換等）の対応範囲
- データキャッシュの有効期間と更新タイミング

**ユーザー確認**: 上記アプローチで問題ないか、追加要件があればご指示ください。
