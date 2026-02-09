#!/usr/bin/env bash
set -euo pipefail

ROOT="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$ROOT"

mkdir -p "acceptance/reports"

PROMPT_FILE="$(mktemp)"
cat >"$PROMPT_FILE" <<'PROMPT'
你是一个只读的验收机器人，请不要修改仓库文件。

任务：
1) 读取 acceptance/manifest.json 与 plan.md（只需要 M0 相关的内容）。
2) 验证仓库是否满足 M0 验收（以文件存在/目录结构/工作流存在为主）。
3) 输出一份简洁的 Markdown 报告，包含：
   - 总结：PASS/FAIL
   - 每个 check 的结论与原因
   - 如失败，给出最短修复建议
4) 不要输出与验收无关的内容。
PROMPT

codex exec \
  --skip-git-repo-check \
  --sandbox read-only \
  -m "gpt-5.2" \
  -c "model_reasoning_effort=\"low\"" \
  --output-last-message "acceptance/reports/ai.md" \
  -C "$ROOT" \
  "$(cat "$PROMPT_FILE")"

rm -f "$PROMPT_FILE"
echo "[acceptance] AI report written: acceptance/reports/ai.md"
