# Refactor baseline

作成日: 2026-06-10

段階的リファクタリング前後の差分確認に使う baseline です。

## 自動確認

- `scripts/verify-refactor-phase.ps1` で Debug build、Release build、`CharaSelectLogicTests`、`git diff --check` をまとめて実行します。
- Release build は `-p:DevPluginOutputDir=` を指定し、devPlugins へのコピーを避けます。

## 現在の取得済み baseline

- `2026-06-10-chara-select-logic-tests.txt`
- `2026-06-10-release-output-files.txt`

## 実機確認

`/xmu diag`、`/xmutbgdiag`、`/xmutbgcheck` はゲーム内実行が必要なため、この環境では未取得です。取得手順と保存先は `manual-game-diagnostics.md` に残しています。
