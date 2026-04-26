"""CI gate: GAP-008 spine last_run_summary.json shape matches script contract (Task 433)."""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
FIXTURE = ROOT / "tests" / "fixtures" / "gap008_spine" / "last_run_summary_example.json"

REQUIRED_KEYS = frozenset(
    {
        "timestampUtc",
        "filterPath",
        "effectiveFilter",
        "discoveryPath",
        "listedTestCount",
        "trxPath",
        "passed",
        "failed",
        "skippedApprox",
        "dotnetExitCode",
    }
)


def test_gap008_last_run_summary_fixture_has_required_keys():
    data = json.loads(FIXTURE.read_text(encoding="utf-8"))
    missing = sorted(REQUIRED_KEYS - set(data))
    assert not missing, f"Fixture missing keys: {missing}"


def test_gap008_last_run_summary_fixture_types():
    data = json.loads(FIXTURE.read_text(encoding="utf-8"))
    assert isinstance(data["timestampUtc"], str)
    assert isinstance(data["filterPath"], str)
    assert isinstance(data["effectiveFilter"], str)
    assert isinstance(data["discoveryPath"], str)
    assert isinstance(data["listedTestCount"], int)
    assert isinstance(data["trxPath"], str)
    assert isinstance(data["passed"], int)
    assert isinstance(data["failed"], int)
    assert isinstance(data["dotnetExitCode"], int)
    assert isinstance(data["skippedApprox"], int)


GREEN_COHERENT = (
    ROOT / "tests" / "fixtures" / "gap008_spine" / "last_run_summary_green_listing_matches_trx.json"
)


def _assert_listed_test_count_matches_passed_when_green_contract(data: dict) -> None:
    """Green contract: dotnetExitCode 0 and failed 0 ⇒ listedTestCount equals TRX passed (Task 453)."""
    if data.get("dotnetExitCode") != 0:
        return
    failed = data.get("failed")
    if failed is not None and failed != 0:
        return
    listed = data["listedTestCount"]
    passed = data.get("passed")
    if passed is None:
        return
    assert listed == passed, (
        f"Green contract: listedTestCount ({listed}) must equal passed ({passed}). "
        "See GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md."
    )


def test_gap008_green_fixture_listedTestCount_matches_passed():
    data = json.loads(GREEN_COHERENT.read_text(encoding="utf-8"))
    _assert_listed_test_count_matches_passed_when_green_contract(data)


def test_gap008_green_contract_detects_listed_passed_mismatch():
    data = json.loads(GREEN_COHERENT.read_text(encoding="utf-8"))
    data = dict(data)
    data["listedTestCount"] = data["passed"] + 1
    try:
        _assert_listed_test_count_matches_passed_when_green_contract(data)
    except AssertionError:
        return
    raise AssertionError("Expected mismatch to fail green coherence check.")
