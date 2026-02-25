"""
After Shell Execution Hook -- runs after every shell command in Cursor.

Detects build/test/lint failures from the shell output and automatically
injects remediation context (relevant skills, tools, and files) so the
agent knows how to fix the problem.

Called by .cursor/hooks.json -> afterShellExecution.

Cursor passes the shell output via environment variables:
  CURSOR_SHELL_OUTPUT  -- last N chars of terminal output
  CURSOR_EXIT_CODE     -- exit code of the last command
"""

from __future__ import annotations

import os
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent.parent

sys.path.insert(0, str(REPO_ROOT))

from tools.context.context_bridge import run_shell_context  # noqa: E402


def main() -> None:
    exit_code = os.environ.get("CURSOR_EXIT_CODE", "")
    shell_output = os.environ.get("CURSOR_SHELL_OUTPUT", "")

    if not shell_output and not sys.stdin.isatty():
        shell_output = sys.stdin.read()

    if exit_code == "0" and not shell_output:
        return

    combined = shell_output
    if exit_code and exit_code != "0":
        combined = f"exit code {exit_code}\n{shell_output}"

    try:
        output = run_shell_context(combined)
        if output:
            print(output)
    except Exception as exc:
        print(f"[Context Manager] Shell hook error: {exc}", file=sys.stderr)


if __name__ == "__main__":
    main()
