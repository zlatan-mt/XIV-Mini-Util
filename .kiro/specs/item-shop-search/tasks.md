# Implementation Plan

## Tasks

- [x] 1. ドメインモデルと設定の拡張
- [x] 1.1 (P) ショップ検索用のドメインモデルを定義する
  - 販売場所情報を表す値オブジェクト（ショップID、NPC名、エリア名、座標等）
  - 検索結果を表す値オブジェクト（アイテムID、名前、販売場所リスト、成功フラグ）
  - 既存のDomainModels.csに追加
  - _Requirements: 2.3_

- [x] 1.2 (P) 優先度設定をConfigurationに追加する
  - エリア優先度リスト（TerritoryTypeIdのリスト）を設定プロパティとして追加
  - デフォルト値として三大都市（リムサ、ウルダハ、グリダニア）のIDを設定
  - 既存のシリアライズ・デシリアライズが正しく動作することを確認
  - _Requirements: 5.4, 5.5_

- [x] 2. ショップデータキャッシュの実装
- [x] 2.1 Luminaシートからショップデータを読み込む機能を実装する
  - GilShop、GilShopItem、ENpcBase、Level、TerritoryType、Mapシートへのアクセス
  - アイテムIDからショップ情報への逆引きインデックスを構築
  - ENpcBaseのData配列からショップ参照を解析してNPC位置情報を取得
  - Level座標からマップ座標への変換計算を実装
  - _Requirements: 6.1, 6.2_

- [x] 2.2 キャッシュの非同期初期化と管理を実装する
  - プラグイン起動時に非同期でインデックスを構築（UIをブロックしない）
  - 初期化完了状態の管理（IsInitializedプロパティ）
  - アイテムIDによるO(1)ルックアップを提供
  - Levelデータが欠落しているエントリはスキップしログ出力
  - _Requirements: 6.3, 6.4, 7.1, 7.2, 7.3_

- [x] 3. 出力サービスの実装
- [x] 3.1 (P) マップピン設定サービスを実装する
  - ShopLocationInfoからMapLinkPayloadを生成
  - IGameGui.OpenMapWithMapLinkでマップを開いてフラグを設定
  - TerritoryTypeIdとMapIdの整合性検証
  - _Requirements: 4.1, 4.3_

- [x] 3.2 Echo投稿サービスを実装する
  - SeStringBuilderでクリック可能なマップリンク付きメッセージを生成
  - 上位3件（または全件）の販売場所をフォーマット出力
  - 検索失敗時のエラーメッセージ投稿
  - MapServiceからMapLinkPayloadを取得して埋め込み
  - _Requirements: 3.1, 3.2, 3.3, 2.4_

- [x] 4. ショップ検索サービスの実装
- [x] 4.1 販売場所検索のコアロジックを実装する
  - ShopDataCacheからアイテムIDで販売場所を取得
  - マーケット取引不可アイテムの除外判定
  - 検索結果の生成（成功・失敗の両ケース）
  - _Requirements: 2.1, 2.5_

- [x] 4.2 優先度に基づくソート機能を実装する
  - Configuration.ShopSearchAreaPriorityに基づいて販売場所をソート
  - 優先度リストに含まれるエリアを先頭に配置
  - 優先度リストに含まれないエリアはエリア名順で末尾に配置
  - _Requirements: 2.2_

- [x] 4.3 検索結果の出力処理を実装する
  - 検索成功時にMapServiceでマップピンを設定
  - 検索成功時にChatServiceでEcho投稿
  - 検索失敗時にChatServiceでエラー通知
  - _Requirements: 4.1, 3.1, 3.2, 3.3, 2.4_

- [x] 5. コンテキストメニュー統合の実装
- [x] 5.1 IContextMenuイベントの購読とメニュー項目追加を実装する
  - OnMenuOpenedイベントを購読
  - MenuTargetInventoryでインベントリアイテムを識別
  - ShopDataCacheで販売データの有無を確認
  - 販売データがある場合のみ「販売場所を検索」メニュー項目を追加
  - _Requirements: 1.1, 1.2, 1.3, 1.5_

- [x] 5.2 チャットアイテムリンクのコンテキストメニュー対応を実装する
  - MenuTargetDefaultでチャットアイテムリンクを識別
  - アイテムID抽出とショップ検索の実行
  - _Requirements: 1.4_

- [x] 5.3 メニュー項目クリック時の検索実行を実装する
  - OnMenuItemClickedでアイテムIDを取得
  - ShopSearchServiceの検索メソッドを呼び出し
  - Dispose時にイベント購読を解除
  - _Requirements: 2.1_

- [x] 6. 検索結果UIの実装
- [x] 6.1 検索結果ウィンドウを実装する
  - ImGuiベースのウィンドウで全販売場所をリスト表示
  - 各行にエリア名、NPC名、座標を表示
  - 検索結果が4件以上ある場合に自動表示
  - _Requirements: 3.4_

- [x] 6.2 リスト選択によるマップピン更新を実装する
  - リスト項目をクリックしたらMapServiceでピンを更新
  - 詳細情報（販売価格、必要条件等）の表示
  - _Requirements: 4.2, 3.5_

- [x] 7. 優先度設定UIの実装
- [x] 7.1 ConfigWindowに優先度設定セクションを追加する
  - エリア優先度リストの表示
  - 上下ボタンまたはドラッグ＆ドロップで順序変更
  - 設定保存時に即座に反映
  - _Requirements: 5.1, 5.2, 5.3_

- [x] 8. プラグイン統合と動作確認
- [x] 8.1 Plugin.csにサービスを統合する
  - ContextMenuService、ShopSearchService、ShopDataCache、MapService、ChatServiceの初期化
  - ShopSearchResultWindowのウィンドウシステム登録
  - Dispose時の適切なリソース解放
  - _Requirements: 1.1, 1.2, 1.3, 1.4_

- [x] 8.2 エンドツーエンドの動作確認を行う
  - 所持品アイテムの右クリックでメニュー表示を確認
  - 検索実行でEcho投稿とマップピンを確認
  - 優先度設定の変更がソート順に反映されることを確認
  - 販売データなしアイテムでメニューが表示されないことを確認
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 2.1, 2.2, 2.3, 2.4, 3.1, 3.2, 3.3, 4.1, 5.3, 7.1_
