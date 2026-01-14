#!/usr/bin/env bash
set -euo pipefail

# Codex Review Script
# Usage: ./review.sh [max_iterations] [base_ref]
#   max_iterations: 最大繰り返し回数 (デフォルト: 5)
#   base_ref: 比較対象のgit ref (デフォルト: HEAD~1)

MAX_ITERATIONS="${1:-5}"
BASE_REF="${2:-HEAD~1}"
ITERATION=0

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]})/../../.." && pwd)"
REVIEW_PROMPT_TEMPLATE='
以下の変更をレビューしてください。

## 変更ファイル
%s

## 差分
%s

## 出力形式
必ず以下のJSON形式のみで回答してください。それ以外のテキストは含めないでください:
```json
{
  "ok": boolean,
  "issues": [
    {
      "severity": "blocking" または "advisory",
      "category": "correctness|security|performance|style|maintainability",
      "file": "ファイルパス",
      "line": 行番号,
      "description": "問題の説明",
      "suggestion": "修正案"
    }
  ]
}
```

## レビュー観点
- correctness: ロジックの正確性、バグ
- security: セキュリティリスク、脆弱性
- performance: パフォーマンス問題
- style: コードスタイル違反
- maintainability: 保守性、可読性

blockingな問題がなければ "ok": true としてください。
'

echo "=== Codex Review ==="
echo "Max iterations: $MAX_ITERATIONS"
echo "Base ref: $BASE_REF"
echo ""

# 変更ファイル一覧を取得
CHANGED_FILES=$(git diff --name-only "$BASE_REF" 2>/dev/null || echo "")
if [[ -z "$CHANGED_FILES" ]]; then
    echo "No changes detected."
    exit 0
fi

FILE_COUNT=$(echo "$CHANGED_FILES" | wc -l)
LINE_COUNT=$(git diff --stat "$BASE_REF" | tail -1 | grep -oE '[0-9]+ insertion|[0-9]+ deletion' | grep -oE '[0-9]+' | paste -sd+ | bc 2>/dev/null || echo "0")

# 規模判定
if [[ $FILE_COUNT -le 3 && $LINE_COUNT -le 100 ]]; then
    SCALE="small"
elif [[ $FILE_COUNT -le 10 && $LINE_COUNT -le 500 ]]; then
    SCALE="medium"
else
    SCALE="large"
fi

echo "Scale: $SCALE ($FILE_COUNT files, ~$LINE_COUNT lines)"
echo "Changed files:"
echo "$CHANGED_FILES" | sed 's/^/  /'
echo ""

# レビューループ
while [[ $ITERATION -lt $MAX_ITERATIONS ]]; do
    ITERATION=$((ITERATION + 1))
    echo "=== Iteration $ITERATION/$MAX_ITERATIONS ==="

    # 差分を取得
    DIFF_CONTENT=$(git diff "$BASE_REF" 2>/dev/null || echo "No diff")

    # プロンプトを構築
    PROMPT=$(printf "$REVIEW_PROMPT_TEMPLATE" "$CHANGED_FILES" "$DIFF_CONTENT")

    # Codex でレビュー実行
    echo "Running Codex review..."
    REVIEW_RESULT=$(codex exec --sandbox read-only "$PROMPT" 2>&1) || {
        echo "Codex execution failed: $REVIEW_RESULT"
        exit 1
    }

    # JSONを抽出 (```json ... ``` ブロックがあれば抽出)
    JSON_RESULT=$(echo "$REVIEW_RESULT" | sed -n '/^```json/,/^```$/p' | sed '1d;$d' || echo "$REVIEW_RESULT")

    # JSONが空の場合、全体を使用
    if [[ -z "$JSON_RESULT" ]]; then
        JSON_RESULT="$REVIEW_RESULT"
    fi

    echo "Review result:"
    echo "$JSON_RESULT" | head -50

    # ok フィールドを確認
    OK_STATUS=$(echo "$JSON_RESULT" | jq -r '.ok // false' 2>/dev/null || echo "false")

    if [[ "$OK_STATUS" == "true" ]]; then
        echo ""
        echo "=== Review Passed! ==="
        echo "No blocking issues found after $ITERATION iteration(s)."
        exit 0
    fi

    # blocking issues を抽出
    BLOCKING_ISSUES=$(echo "$JSON_RESULT" | jq -r '.issues[] | select(.severity == "blocking") | "[\(.category)] \(.file):\(.line // "?"): \(.description)"' 2>/dev/null || echo "")

    if [[ -z "$BLOCKING_ISSUES" ]]; then
        echo ""
        echo "=== Review Passed! ==="
        echo "No blocking issues found."
        exit 0
    fi

    echo ""
    echo "Blocking issues found:"
    echo "$BLOCKING_ISSUES"
    echo ""

    # 修正を促す
    if [[ $ITERATION -lt $MAX_ITERATIONS ]]; then
        echo "Please fix the blocking issues and press Enter to continue, or Ctrl+C to abort."
        read -r
    fi
done

echo ""
echo "=== Max iterations reached ==="
echo "Review did not pass after $MAX_ITERATIONS iterations."
exit 1
