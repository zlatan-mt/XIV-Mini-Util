---
name: codex-review
description: |
  Codex CLIを使用した自動コードレビュー。実装完了後にCodexでレビューし、問題がなくなるまで修正を繰り返す。
  使用タイミング: (1) 実装完了後のレビュー (2) マイルストーン完了時 (3) PR作成前の品質チェック
---

# Codex Review

Codex CLI (read-only) でコードレビューを実行し、`ok: true` になるまで修正を繰り返す。

## 前提条件

- Codex CLI インストール済み (`codex --version`)
- OpenAI API キーまたは ChatGPT Plus/Pro サブスクリプション

## 規模判定

変更内容に応じて戦略を選択:

| 規模 | ファイル数 | 行数 | 戦略 |
|------|-----------|------|------|
| small | ≤3 | ≤100 | 直接diff |
| medium | 4-10 | 100-500 | アーキテクチャ確認→diff |
| large | >10 | >500 | 並列レビュー→統合 |

## 出力スキーマ

Codexには以下のJSON形式のみを出力させる:

```json
{
  "ok": true,
  "issues": [
    {
      "severity": "blocking|advisory",
      "category": "correctness|security|performance|style|maintainability",
      "file": "path/to/file.cs",
      "line": 42,
      "description": "問題の説明",
      "suggestion": "修正案"
    }
  ]
}
```

## 実行手順

### 1. 変更差分を取得

```bash
git diff --name-only HEAD~1
git diff --stat HEAD~1
```

### 2. 規模を判定

ファイル数と行数から small/medium/large を判定。

### 3. Codex でレビュー実行

```bash
codex exec --sandbox read-only "
以下の変更をレビューしてください。

## 変更ファイル
<diffの内容>

## 出力形式
必ず以下のJSON形式のみで回答:
{
  \"ok\": boolean,
  \"issues\": [{\"severity\": \"blocking|advisory\", \"category\": \"...\", \"file\": \"...\", \"line\": number, \"description\": \"...\", \"suggestion\": \"...\"}]
}

## レビュー観点
- correctness: ロジックの正確性
- security: セキュリティリスク
- performance: パフォーマンス問題
- style: コードスタイル
- maintainability: 保守性

blockingな問題がなければ ok: true
"
```

### 4. 結果を解析

- `ok: true` → 完了
- `ok: false` → blocking issues を修正

### 5. 修正ループ

最大5回まで繰り返し:
1. blocking issues を修正
2. 再度 Codex でレビュー
3. `ok: true` または指摘なしで終了

## 統合方法

CLAUDE.md または実装計画に以下を追記:

```markdown
## レビュー指針
各マイルストーン完了時、codex-review スキルを実行してレビュー→修正→再レビューまで繰り返す。
```

## スクリプト

自動化スクリプト: `scripts/review.sh`

```bash
bash .claude/skills/codex-review/scripts/review.sh [max_iterations]
```
