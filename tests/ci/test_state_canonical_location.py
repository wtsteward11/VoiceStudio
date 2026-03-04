"""CI gate: STATE.md canonical location enforcement.

.cursor/STATE.md is the single source of truth for session state.
Repo-root STATE.md must be a small pointer (<=2KB) with no proof entries.
"""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent

MAX_POINTER_BYTES = 2048


def test_cursor_state_exists():
    canonical = ROOT / ".cursor" / "STATE.md"
    assert canonical.exists(), (
        ".cursor/STATE.md must exist (canonical state file)"
    )


def test_repo_root_state_is_pointer():
    root_state = ROOT / "STATE.md"
    if not root_state.exists():
        return
    size = root_state.stat().st_size
    assert size <= MAX_POINTER_BYTES, (
        f"Repo-root STATE.md is {size} bytes. "
        f"Must be <= {MAX_POINTER_BYTES} bytes (pointer only). "
        f"Move content to .cursor/STATE.md."
    )


def test_repo_root_state_has_no_proof_entries():
    root_state = ROOT / "STATE.md"
    if not root_state.exists():
        return
    content = root_state.read_text(encoding="utf-8")
    proof_lines = [
        line for line in content.splitlines()
        if re.search(r"Proof:\s*[`\"']?[a-zA-Z]", line)
    ]
    assert len(proof_lines) == 0, (
        f"Repo-root STATE.md contains {len(proof_lines)} "
        f"Proof: lines. Proof entries belong in "
        f".cursor/STATE.md only."
    )


def test_repo_root_state_references_canonical():
    root_state = ROOT / "STATE.md"
    if not root_state.exists():
        return
    content = root_state.read_text(encoding="utf-8").lower()
    assert ".cursor/state.md" in content, (
        "Repo-root STATE.md must reference .cursor/STATE.md "
        "as the canonical state file"
    )
