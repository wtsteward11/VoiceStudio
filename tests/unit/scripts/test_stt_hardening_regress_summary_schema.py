"""Task 73 / 79 — schema for ``stt_hardening_regress_summary.json`` (STT pack)."""

from __future__ import annotations

import json
import re
from pathlib import Path

import pytest

from tests.unit.scripts.stt_pack_required_targets import STT_PACK_PYTEST_PATHS

_REPO_ROOT = Path(__file__).resolve().parents[3]
_SUMMARY = (
    _REPO_ROOT
    / "docs"
    / "reports"
    / "verification"
    / "generated"
    / "stt_hardening_regress_summary.json"
)


def test_stt_hardening_regress_summary_schema() -> None:
    if not _SUMMARY.is_file():
        pytest.skip(
            "Run .\\scripts\\stt_hardening_regress.ps1 (writes summary at end) "
            "or copy docs/reports/verification/generated/"
            "stt_hardening_regress_summary.json"
        )
    data = json.loads(_SUMMARY.read_text(encoding="utf-8"))
    assert data.get("schema_version") == 1
    assert isinstance(data.get("timestamp_utc"), str) and data["timestamp_utc"]
    args = data.get("pytest_args")
    assert isinstance(args, list) and args
    # Pack includes ``file.py::Class::method`` node ids (e.g. ``test_preflight_check``), not only ``*.py`` paths.
    paths_in_summary = [
        a for a in args if isinstance(a, str) and not a.startswith("-")
    ]
    extra = set(paths_in_summary) - set(STT_PACK_PYTEST_PATHS)
    missing = set(STT_PACK_PYTEST_PATHS) - set(paths_in_summary)
    assert set(paths_in_summary) == set(STT_PACK_PYTEST_PATHS), (
        "summary pytest_args must match stt_hardening_regress.ps1 "
        f"(see stt_pack_required_targets.py): extra={extra} missing={missing}"
    )
    assert data.get("pytest_exit_code") == 0
    assert isinstance(data.get("passed_count"), int)
    assert data["passed_count"] >= 1
    assert data.get("generate_engine_truth_v1_exit_code") == 0
    assert data.get("generate_engine_truth_v2_exit_code") == 0
    failed = data.get("failed_count")
    assert failed in (0, None), (
        f"expected failed_count 0 or absent on green pack, got {failed!r}"
    )
    tail = data.get("pytest_stdout_tail")
    assert isinstance(tail, str) and tail.strip()
    tail_passed = re.findall(r"(\d+)\s+passed", tail)
    assert tail_passed, "pytest_stdout_tail must contain a line like 'N passed'"
    tail_last = tail_passed[-1]
    pc = data["passed_count"]
    assert int(tail_last) == pc, (
        "passed_count must match last pytest 'N passed' in pytest_stdout_tail "
        f"(drift guard): json={pc!r} tail_last={tail_last!r}"
    )
