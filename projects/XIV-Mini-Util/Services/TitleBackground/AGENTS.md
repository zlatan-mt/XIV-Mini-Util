# AGENTS.md — Title Background / Character Select 恒久契約

この契約は次を触るときだけ読む（root `AGENTS.md` には置かない component 固有ルール）:

- `projects/XIV-Mini-Util/Services/TitleBackground/`
- `projects/XIV-Mini-Util/Services/CharaSelect/`
- `projects/XIV-Mini-Util/Windows/Components/SettingsTab.TitleBackground.cs` / `SettingsTab.CharaSelect.cs`
- `projects/XIV-Mini-Util/Configuration.TitleBackground.cs` / `Configuration.CharaSelect.cs`
- `tools/CharaSelectLogicTests/`

関連: `/title-background` skill（カメラ・配置・環境・保存view・facing・診断コードの境界と蓄積知見。実装・レビュー・実機レポート判読の前に必ず参照）。この `AGENTS.md` は skill を呼ばずに上記ファイルを編集する agent 向けの backstop。skill とこの契約が矛盾する場合は skill を優先する。

この契約は root `AGENTS.md` の「実機確認は必要最小限の手順」より優先する。

## 通常画面（ユーザーが普段見る画面）

- 常時表示量は、過去の Developer 表示時の 10 分の 1 以下にする。
- 操作部品は最大 4 個、定常時の説明・状態行は最大 6 行。
- `CollapsingHeader` / `TreeNode` / 表示モード切替 / 閉じたタブで隠すだけ、の「見かけ上の削減」は禁止。実際に量を削る。
- 通常利用に不要な診断・生設定・signature・resolver・legacy 比較・manual candidate・probe 操作・anchor 座標・nudge・layer 探索・FixOn toggle・camera profile compare・delivery mode・preset raw・Phase 番号・英語の内部診断文言は、通常画面から削除する。
- 通常画面の描画から Developer 系描画メソッドを呼ばない。

## 残す開発機能

- 残す必要がある開発機能は、責務ごとに別ページ／別ファイルへ物理的に分割する。通常画面からは呼び出さない。
- 単に折りたたみや別タブへ移すだけにせず、ファイルと責務を実際に分ける。
- バックエンドロジックは現役参照を確認せずに削除しない。UI は重複・自動レポートで代替済みのものを削除する。

## 実機確認フロー（1クリック契約）

- ユーザーに許される操作は「1クリック → ログアウト → ログイン → 自動コピーされたレポートを貼る」だけ。これ以外を要求しない。
- candidate 設定、probe 取得、有効化、古い run の整理、hook 再初期化、QuickCheck 開始、sample 追加、レポート統合、clipboard コピーは、すべてコード側が自動処理する。
- candidate 設定は必ず probe 取得より先に行い、candidate-mismatch を構造的に防ぐ。
- hook が未準備なら、コード側で安全な再初期化を 1 回行って再評価する。それでも不可なら、logout を要求せず run を失敗完了し、原因・hook 状態・candidate・territory・再初期化結果を含む失敗レポートを自動コピーする。手動の OFF→候補選択・probe クリア・ゲーム再起動を先に案内しない。
- 成功・失敗どちらも、追加操作を要求せず最終レポートを自動コピーする。
- 複数地点の測定が必要でも、各地点で同じ 1 クリックフローだけを使う。sample はセッション中に自動蓄積し、各 run の最終レポートに集約結果を含める。
- 新機能が追加操作を必要とする場合、ユーザーへ手順を増やす前に自動化経路を実装する。
- レポートは原因判定に必要な値・適用可否・失敗理由・取得時コンテキストを含め、詳細なフレームログは別ファイルへ分離して本文を過大にしない。
- pre-login など特定コンテキストでしか安全に読めない値はその場でスナップショット化し、後から安全に一括出力する。安全でないコンテキストで再読取しない。

## 禁止する案内（ユーザーへ要求してはいけない操作）

開発者向け設定の表示 / 候補の選び直し / probe の保存・ON・クリア / 診断を別ボタンでコピー / 診断キー探し / 設定リセット / コマンド入力 / 同じ run のための複数ボタン押下 / 多数のスクリーンショット・目視項目 / hookNotReady 時の手動再起動・再設定を先に要求すること。

## 安全境界（自動化しても緩めない）

- 安全ゲート、run-scoped 判定、pre-login snapshot、非永続 probe、`PersistentApplyEnabled` は緩めない。
- world 座標を FixOn／カメラ焦点へ流さない。実機確認前に ground-verified へ昇格させない。

## テストで固定する

通常画面の操作部品数・許可ラベル・Developer 描画の不呼び出し・折りたたみ/表示 toggle の不在・1クリック契約（主ボタンが単一サービスメソッドのみ呼ぶ・推奨設定が probe 取得より先・mismatch 時に開始しない・hookNotReady 自動再初期化・失敗時自動コピー・統合レポート・旧 run 非混入・安全境界）を自動テストで固定する。検証は `dotnet run --project tools/CharaSelectLogicTests`。
