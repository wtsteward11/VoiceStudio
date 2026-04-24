"""STATE, overrides, and generated v2 must agree on latest_verify_artifact (Tasks 44–45)."""

from __future__ import annotations

import json
import re
from pathlib import Path

_REPO_ROOT = Path(__file__).resolve().parents[3]
_OVERRIDES = _REPO_ROOT / "tools" / "overseer" / "data" / "engine_truth_overrides.json"
_V2 = (
    _REPO_ROOT
    / "docs"
    / "reports"
    / "verification"
    / "generated"
    / "engine_truth_v2.json"
)
_STATE = _REPO_ROOT / ".cursor" / "STATE.md"


def _active_window(text: str) -> str:
    start = text.index("## ACTIVE WINDOW")
    end = text.index("## HISTORY LEDGER", start)
    return text[start:end]


def _state_latest_verify_artifact(state_text: str) -> str:
    """Path like artifacts/verify/YYYYMMDD_HHMMSS/verification_report.md."""
    window = _active_window(state_text)
    m = re.search(
        r"\*\*Latest verify artifact:\*\* \[`(artifacts/verify/[^`]+\.md)`",
        window,
    )
    assert m is not None, "STATE ACTIVE WINDOW missing Latest verify artifact backtick path"
    return m.group(1)


def test_engine_truth_verify_artifact_three_way_match() -> None:
    overrides = json.loads(_OVERRIDES.read_text(encoding="utf-8"))
    default_path = (overrides.get("defaults") or {}).get("latest_verify_artifact")
    assert isinstance(default_path, str) and default_path.startswith(
        "artifacts/verify/"
    ), default_path

    state_path = _state_latest_verify_artifact(_STATE.read_text(encoding="utf-8"))
    assert state_path == default_path, (state_path, default_path)

    v2 = json.loads(_V2.read_text(encoding="utf-8"))
    engines = v2.get("engines")
    assert isinstance(engines, list)
    for row in engines:
        if not isinstance(row, dict) or "error" in row:
            continue
        got = row.get("latest_verify_artifact")
        assert got == default_path, (row.get("engine_id"), got, default_path)
