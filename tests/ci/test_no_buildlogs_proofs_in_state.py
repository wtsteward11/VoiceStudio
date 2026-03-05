"""CI gate: STATE.md must not reference .buildlogs/proof_runs (SSOT enforcement).

Proof artifacts live in docs/reports/verification/. References to transient
.buildlogs/proof_runs paths violate SSOT and must be eliminated.
"""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
STATE_PATH = ROOT / ".cursor" / "STATE.md"
SSOT_PREFIX = "docs/reports/verification/"


def test_no_buildlogs_proof_runs_in_state():
    """STATE must never reference .buildlogs/proof_runs."""
    text = STATE_PATH.read_text(encoding="utf-8")
    violations = []
    for i, line in enumerate(text.splitlines(), 1):
        if ".buildlogs/proof_runs" in line:
            violations.append(f"  line {i}: {line.strip()[:120]}")
    assert not violations, (
        f"STATE.md references .buildlogs/proof_runs "
        f"({len(violations)} violations):\n" + "\n".join(violations)
    )


def test_no_proof_runs_slash_in_state():
    """STATE must not contain proof_runs/ references."""
    text = STATE_PATH.read_text(encoding="utf-8")
    violations = []
    for i, line in enumerate(text.splitlines(), 1):
        if "proof_runs/" in line:
            violations.append(f"  line {i}: {line.strip()[:120]}")
    assert not violations, (
        f"STATE.md references proof_runs/ "
        f"({len(violations)} violations):\n" + "\n".join(violations)
    )


def test_all_proof_json_refs_are_ssot():
    """Every PROOF_*.json reference must be under docs/reports/verification/."""
    text = STATE_PATH.read_text(encoding="utf-8")
    pattern = re.compile(r"PROOF_\w+\.json")
    violations = []
    for i, line in enumerate(text.splitlines(), 1):
        for m in pattern.finditer(line):
            # Line must contain SSOT prefix when referencing PROOF_*.json
            if SSOT_PREFIX not in line:
                violations.append(f"  line {i}: {line.strip()[:120]}")
                break
    assert not violations, (
        f"STATE.md has PROOF_*.json refs not under {SSOT_PREFIX} "
        f"({len(violations)} violations):\n" + "\n".join(violations)
    )
