# ファイル整理計画

## 目的

この計画は、`XIV-Mini-Util` リポジトリ内のファイル配置を見直し、次の実装や保守で迷わない構成にするためのものである。  
目的は大規模な再編ではなく、役割ごとの置き場を明確にして、必要なファイルへ短時間で到達できる状態を作ることにある。

## 現状の課題

- ルート直下に本体運用ファイルと補助ファイルが混在している
- `docs` 配下に設計、配布、実装メモが混在している
- `projects` 配下に本体プロジェクトと外部参照用プロジェクトが同居している
- 将来の仕様検討用ドキュメントと、一時的な調査メモの区別が弱い

## 整理方針

### 1. ルート直下は入口ファイルだけに絞る

ルートには、リポジトリの入口として意味があるファイルのみを残す。

残す対象:
- `README.md`
- `AGENTS.md`
- `CHANGELOG.md`
- `LICENSE`
- `pluginmaster.json`
- `.gitignore`

原則としてルートに置かない対象:
- 機能設計メモ
- 実装メモ
- 配布手順書
- AI ツール向け補助文書

### 2. `docs` は役割で分ける

`docs` 配下は次の分類に整理する。

- `docs/design/`
  - 現行設計、機能ベースライン、技術要件、ファイル整理計画など
- `docs/release/`
  - 配布手順、release 手順、pluginmaster 運用など
- `docs/notes/`
  - 実装メモ、調査メモ、一時的な検証結果
### 3. 仕様メモと現行ドキュメントを分ける

- `docs/design`
  - 現状整理、補助設計、実装ベースの設計資料
- `docs/notes`
  - 一時的な調査メモ、実装メモ、検証結果

完了済みまたは廃止済みの旧ワークフロー資料はリポジトリに常駐させず、必要な場合のみ外部バックアップから参照する。

### 4. `projects` は本体と外部依存を意識して扱う

現時点では移動を即実施しないが、次の区別を前提に整理する。

- `projects/XIV-Mini-Util`
  - 本体コード
- `projects/*` の外部参照用コード
  - ローカル参照用コピーとして扱い、リポジトリには常駐させない

外部参照プロジェクトが必要な場合は、再取得手順を明確にしてローカルに展開する。

## 推奨配置

### ルート直下

- `README.md`
- `AGENTS.md`
- `CHANGELOG.md`
- `LICENSE`
- `pluginmaster.json`
- `projects/`
- `docs/`
- `scripts/`
- `images/`

### `docs` 配下

- `docs/design/current-feature-baseline.md`
- `docs/design/file-organization-plan.md`
- `docs/design/materia-desynth-technical-requirements.md`
- `docs/design/xiv-mini-util.md`
- `docs/release/custom-plugin-distribution.md`
- `docs/notes/implementation_summary.md`

## 対象ファイルごとの移動案

- `docs/current-feature-baseline.md`
  - `docs/design/current-feature-baseline.md`
- `docs/custom-plugin-distribution.md`
  - `docs/release/custom-plugin-distribution.md`
- `docs/implementation_summary.md`
  - `docs/notes/implementation_summary.md`
- `docs/materia-desynth-technical-requirements.md`
  - `docs/design/materia-desynth-technical-requirements.md`
- `docs/xiv-mini-util.md`
  - `docs/design/xiv-mini-util.md`
- `docs/file-organization-plan.md`
  - `docs/design/file-organization-plan.md`

## 実施フェーズ

### フェーズ 1. 文書整理

目的:
低リスクで効果が高い `docs` 配下の整理を先に行う。

作業:
- `docs/design` `docs/release` `docs/notes` を作成する
- 現在の `docs` 直下ファイルを役割ごとに移動する
- `README.md` 内のリンク切れが出ないように参照先を更新する

完了条件:
- `docs` 直下には分類ディレクトリのみがある
- 主要ドキュメントへのリンクがすべて解決している

### フェーズ 2. ルート整頓

目的:
ルート直下を入口ファイル中心に整理する。

作業:
- ルート直下にある補助文書の必要性を棚卸しする
- 不要または別置きが妥当なものは `docs` または `archive` へ移す
- AI ツール固有の補助ファイルは「運用上必要か」で残置判断する

完了条件:
- ルート直下の役割が一目で説明できる
- 利用者視点で「何を見ればよいか」が明確になっている

### フェーズ 3. コード構成の見直し検討

目的:
本体コードと外部参照コードの境界を明確にする。

作業:
- `projects/XIV-Mini-Util` から見た依存関係を確認する
- 外部同梱プロジェクトの実利用状況を調査する
- 移動によるビルド影響がないかを確認する

完了条件:
- 外部プロジェクトを現状維持するか、別ディレクトリに分離するか判断できる

## 実施順

1. `docs` の役割分類を導入する
2. 文書を新しい分類先へ移動する
3. `README.md` などの参照リンクを更新する
4. ルート直下の補助ファイルを棚卸しする
5. `projects` 配下の外部依存の扱いを別途検討する

## 注意点

- まずは文書整理を優先し、コード配置の変更は後回しにする
- `README.md` や配布手順のリンク切れを必ず確認する
- 現行ドキュメントと一時的な作業メモを混在させない
- 外部参照プロジェクトは必要になった時点で再取得する
- 旧版文書は必要なら外部バックアップへ退避する

## この計画の扱い

この文書は実施前の整理計画である。  
実際に移動を行うときは、フェーズ 1 から順に小さく進め、各フェーズごとにリンク確認とビルド影響確認を行う。
