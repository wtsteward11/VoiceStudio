"""Slice 27 live session manifest schema (Task 146)."""

from __future__ import annotations

import json
from pathlib import Path

_REPO_ROOT = Path(__file__).resolve().parents[3]
_MANIFEST = (
    _REPO_ROOT
    / "docs"
    / "reports"
    / "verification"
    / "slice27"
    / "slice27_live_session_manifest.json"
)


def test_slice27_live_session_manifest_schema_pass() -> None:
    data = json.loads(_MANIFEST.read_text(encoding="utf-8"))
    assert data.get("schema_version") == 1
    assert isinstance(data.get("base_url"), str) and data["base_url"].startswith("http://")
    assert isinstance(data.get("port"), int) and data["port"] > 0
    for key in (
        "preflight_artifact",
        "pytest_log",
        "transcript_response",
        "outcome",
        "recorded_utc",
    ):
        assert key in data, f"missing manifest key: {key}"
    assert data["outcome"] == "pass"
    assert data.get("blocked_reason_code") is None
    for rel in (
        data["preflight_artifact"],
        data["pytest_log"],
        data["transcript_response"],
    ):
        p = _REPO_ROOT / rel
        assert p.is_file(), f"manifest path must exist: {rel}"
