"""
Session Start Hook -- runs on every new Cursor chat session.

Reads STATE.md, classifies the current phase/task, and outputs
context text that Cursor injects via hooks_context so the agent
automatically knows which role, skills, and tools are relevant.

Called by .cursor/hooks.json -> sessionStart.
"""

from __future__ import annotations

import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent.parent

sys.path.insert(0, str(REPO_ROOT))

from tools.context.context_bridge import run_session_context  # noqa: E402


def main() -> None:
    try:
        output = run_session_context()
        if output:
            print(output)
    except Exception as exc:
        print(f"[Context Manager] Hook error: {exc}", file=sys.stderr)


if __name__ == "__main__":
    main()
