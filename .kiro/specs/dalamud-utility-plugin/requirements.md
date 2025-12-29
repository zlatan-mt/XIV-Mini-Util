# Requirements Document

## Introduction
本ドキュメントは、Final Fantasy XIV Launcher (Dalamud) 向けミニユーティリティプラグイン「XIV Mini Util」の要件を定義します。このプラグインはDalamud API v14および.NET 10を使用し、マテリア精製およびアイテム分解の自動化機能を提供します。

## Requirements

### Requirement 1: プラグイン基盤
**Objective:** As a プラグイン開発者, I want Dalamudフレームワークに準拠したプラグイン構造を持つ, so that 安定した動作と将来のAPI更新への対応が容易になる

#### Acceptance Criteria
1. The Plugin shall Dalamud API v14インターフェースを実装する
2. The Plugin shall .NET 10ランタイムで動作する
3. When プラグインがロードされた時, the Plugin shall 必要なサービスをDependency Injectionで取得する
4. When プラグインがアンロードされた時, the Plugin shall すべてのリソースを適切に解放する
5. The Plugin shall プラグインマニフェストを含む

### Requirement 2: 設定管理
**Objective:** As a プレイヤー, I want プラグインの設定を保存・復元できる, so that ゲーム再起動後も設定が維持される

#### Acceptance Criteria
1. The Plugin shall JSON形式で設定ファイルを保存する
2. When プラグインが起動した時, the Plugin shall 保存された設定を自動的に読み込む
3. When ユーザーが設定を変更した時, the Plugin shall 変更を設定ファイルに保存する
4. If 設定ファイルが存在しないか破損している場合, then the Plugin shall デフォルト設定で初期化する

### Requirement 3: ユーザーインターフェース
**Objective:** As a プレイヤー, I want ゲーム内でプラグインのUIを操作できる, so that 機能に簡単にアクセスできる

#### Acceptance Criteria
1. The Plugin shall ImGuiベースのメインウィンドウを提供する
2. The Plugin shall 設定ウィンドウを提供する
3. When ユーザーがスラッシュコマンドを入力した時, the Plugin shall 対応するウィンドウを表示/非表示する
4. The Plugin shall マテリア精製機能のオン/オフボタンを表示する
5. The Plugin shall アイテム分解機能の開始ボタンを表示する
6. The Plugin shall アイテム分解の設定項目（対象レベル範囲、ジョブ条件）を表示する

### Requirement 4: コマンドシステム
**Objective:** As a プレイヤー, I want スラッシュコマンドでプラグインを操作できる, so that キーボードから素早く機能にアクセスできる

#### Acceptance Criteria
1. The Plugin shall メインコマンド（/xivminiutil または同等のコマンド）を登録する
2. When コマンドが実行された時, the Plugin shall メインウィンドウを表示/非表示する
3. If 無効なサブコマンドが入力された場合, then the Plugin shall 使用可能なコマンド一覧を表示する

### Requirement 5: マテリア精製機能
**Objective:** As a プレイヤー, I want 所持品・アーマリーチェスト内のマテリア精製可能アイテムを自動で精製したい, so that 手動操作の手間を省ける

#### Acceptance Criteria
1. The Plugin shall 所持品およびアーマリーチェスト内のアイテムをスキャンする
2. The Plugin shall スピリットボンド100%のアイテムを検出する
3. When マテリア精製機能がオンの状態で精製可能アイテムが検出された時, the Plugin shall 自動的にマテリア精製を実行する
4. The Plugin shall マテリア精製機能のオン/オフをボタンで切り替え可能とする
5. While マテリア精製機能がオフの状態の時, the Plugin shall 自動精製を実行しない
6. When マテリア精製が完了した時, the Plugin shall 次の精製可能アイテムを検索する

### Requirement 6: アイテム分解機能
**Objective:** As a プレイヤー, I want 条件を指定してアイテム分解を自動化したい, so that 効率的にアイテムを整理できる

#### Acceptance Criteria
1. The Plugin shall 所持品およびアーマリーチェスト内の分解可能アイテムをスキャンする
2. When 分解開始ボタンが押された時, the Plugin shall 設定条件に合致するアイテムの分解を開始する
3. The Plugin shall 分解対象のアイテムレベル範囲（最小レベル、最大レベル）を設定可能とする
4. The Plugin shall 設定されたレベル範囲内かつ分解可能なアイテムのみを分解対象とする
5. While 分解処理中, the Plugin shall 分解対象アイテムを順次処理する

### Requirement 7: アイテム分解の安全機能
**Objective:** As a プレイヤー, I want 高レベルアイテムの誤分解を防ぎたい, so that 重要なアイテムを失わない

#### Acceptance Criteria
1. The Plugin shall 所持アイテム（所持品、アーマリーチェスト、装備中アイテム）の最高アイテムレベルを取得する
2. When 分解対象アイテムのレベルが（最高レベル - 100）以上の場合, the Plugin shall 警告ダイアログを表示する
3. If 警告ダイアログで「はい」が選択された場合, then the Plugin shall 分解を実行する
4. If 警告ダイアログで「いいえ」が選択された場合, then the Plugin shall 分解をキャンセルする
5. The Plugin shall 警告対象となるアイテムの情報（名前、レベル）を警告メッセージに含める

### Requirement 8: アイテム分解のジョブ条件
**Objective:** As a プレイヤー, I want 現在のジョブに応じて分解実行を制御したい, so that 意図しない状況での分解を防げる

#### Acceptance Criteria
1. The Plugin shall 現在のプレイヤージョブを取得する
2. The Plugin shall 分解実行のジョブ条件を3段階から選択可能とする
3. Where ジョブ条件が「クラフターのみ」に設定されている場合, the Plugin shall クラフタージョブ時のみ分解を許可する
4. Where ジョブ条件が「戦闘職のみ」に設定されている場合, the Plugin shall 戦闘職ジョブ時のみ分解を許可する
5. Where ジョブ条件が「すべて」に設定されている場合, the Plugin shall ジョブに関係なく分解を許可する
6. If 現在のジョブがジョブ条件を満たさない場合, then the Plugin shall 分解開始ボタンを無効化またはエラーメッセージを表示する

### Requirement 9: ゲームデータアクセス
**Objective:** As a プラグイン, I want ゲーム内データにアクセスできる, so that 機能を実現するための情報を取得できる

#### Acceptance Criteria
1. The Plugin shall Dalamud APIを通じてインベントリ（所持品）情報にアクセスする
2. The Plugin shall Dalamud APIを通じてアーマリーチェスト情報にアクセスする
3. The Plugin shall アイテムのスピリットボンド値を取得する
4. The Plugin shall アイテムの分解可否を判定する
5. The Plugin shall アイテムレベルを取得する
6. The Plugin shall 現在のプレイヤージョブ情報を取得する
7. If プレイヤーがログアウト状態の場合, then the Plugin shall ゲームデータ依存の機能を無効化する

### Requirement 10: ログ・通知機能
**Objective:** As a プレイヤー/開発者, I want 処理状況を確認できる, so that 正常動作を確認し問題発生時に原因を特定できる

#### Acceptance Criteria
1. The Plugin shall Dalamudログシステムを使用してログを出力する
2. When マテリア精製が実行された時, the Plugin shall 精製結果をログに記録する
3. When アイテム分解が実行された時, the Plugin shall 分解結果をログに記録する
4. If エラーが発生した場合, then the Plugin shall エラー内容をログに記録する

## Technical Constraints
- **API Version**: Dalamud API 14
- **Runtime**: .NET 10
- **UI Framework**: ImGui (Dalamud.Interface)
- **参考資料**: https://dalamud.dev/, https://github.com/goatcorp/Dalamud
