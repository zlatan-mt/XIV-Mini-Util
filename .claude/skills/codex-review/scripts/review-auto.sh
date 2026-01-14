#!/usr/bin/env bash
set -euo pipefail

# Codex Auto Review Script (Non-interactive)
# Claude Code から呼び出される自動レビュースクリプト
# Usage: ./review-auto.sh [base_ref]

BASE_REF="${1:-HEAD~1}"
OUTPUT_FILE="${2:-/tmp/codex-review-result.json}"

# 変更ファイル一覧を取得
CHANGED_FILES=$(git diff --name-only "$BASE_REF" 2>/dev/null || echo "")
if [[ -z "$CHANGED_FILES" ]]; then
    echo '{"ok": true, "issues": [], "message": "No changes detected"}' > "$OUTPUT_FILE"
    cat "$OUTPUT_FILE"
    exit 0
fi

# 差分を取得
DIFF_CONTENT=$(git diff "$BASE_REF" 2>/dev/null || echo "")

# プロンプト
PROMPT="以下の変更をレビューしてください。

## 変更ファイル
$CHANGED_FILES

## 差分
$DIFF_CONTENT

## 出力形式
必ず以下のJSON形式のみで回答:
{\"ok\": boolean, \"issues\": [{\"severity\": \"blocking|advisory\", \"category\": \"correctness|security|performance|style|maintainability\", \"file\": \"path\", \"line\": number, \"description\": \"説明\", \"suggestion\": \"修正案\"}]}

blockingな問題がなければ ok: true"

# Codex でレビュー実行
RESULT=$(codex exec --sandbox read-only "$PROMPT" 2>&1) || {
    echo "{\"ok\": false, \"error\": \"Codex execution failed\", \"message\": \"$RESULT\"}" > "$OUTPUT_FILE"
    cat "$OUTPUT_FILE"
    exit 1
}

# JSONを抽出
JSON_RESULT=$(echo "$RESULT" | sed -n '/^{/,/^}/p' | head -1 || echo "$RESULT")
if [[ -z "$JSON_RESULT" ]] || ! echo "$JSON_RESULT" | jq . >/dev/null 2>&1; then
    # JSONブロックを探す
    JSON_RESULT=$(echo "$RESULT" | grep -oP '\{.*\}' | head -1 || echo '{"ok": false, "error": "Invalid JSON", "raw": "'"${RESULT:0:500}"'"}')
fi

echo "$JSON_RESULT" > "$OUTPUT_FILE"
cat "$OUTPUT_FILE"
