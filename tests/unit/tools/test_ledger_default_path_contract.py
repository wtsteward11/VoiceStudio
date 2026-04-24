"""Canonical Quality Ledger path must stay aligned across parser, context adapter, and monitor."""

from __future__ import annotations

from pathlib import Path

_REPO_ROOT = Path(__file__).resolve().parents[3]
_EXPECTED_REL = Path("docs/archive/Recovery_Plan/QUALITY_LEDGER.md")


def test_ledger_default_path_matches_archive_layout() -> None:
    from tools.overseer.ledger_parser import LEDGER_DEFAULT_PATH

    assert LEDGER_DEFAULT_PATH == _EXPECTED_REL
    resolved = _REPO_ROOT / LEDGER_DEFAULT_PATH
    assert resolved.name == "QUALITY_LEDGER.md"
    assert resolved.parent.name == "Recovery_Plan"
    assert resolved.parent.parent.name == "archive"


def test_ledger_source_adapter_default_uses_ledger_default_path() -> None:
    from tools.context.sources.ledger_adapter import LedgerSourceAdapter
    from tools.overseer.ledger_parser import LEDGER_DEFAULT_PATH

    adapter = LedgerSourceAdapter()
    assert adapter._ledger_path == LEDGER_DEFAULT_PATH


def test_overseer_monitor_ledger_path_matches_ledger_default_path() -> None:
    from tools.overseer.ledger_parser import LEDGER_DEFAULT_PATH
    from tools.overseer_monitor import OverseerMonitor

    mon = OverseerMonitor(_REPO_ROOT, quiet=True)
    assert mon.ledger_path == _REPO_ROOT / LEDGER_DEFAULT_PATH


def test_monitor_source_contains_no_legacy_recovery_plan_string() -> None:
    """Core tooling must not reintroduce the old relative path string."""
    monitor_py = _REPO_ROOT / "tools" / "overseer_monitor.py"
    text = monitor_py.read_text(encoding="utf-8")
    assert "Recovery Plan/QUALITY_LEDGER" not in text
