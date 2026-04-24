"""Contract: STT regression pack keeps pytest targets and truth steps."""

from __future__ import annotations

from pathlib import Path

from tests.unit.scripts.stt_pack_required_targets import (
    STT_PACK_PYTEST_PATHS,
    STT_PACK_SCRIPT_FRAGMENTS,
)

_REPO_ROOT = Path(__file__).resolve().parents[3]
_PACK_SCRIPT = _REPO_ROOT / "scripts" / "stt_hardening_regress.ps1"


def test_stt_hardening_regress_pack_includes_required_targets() -> None:
    text = _PACK_SCRIPT.read_text(encoding="utf-8")
    for fragment in STT_PACK_PYTEST_PATHS + STT_PACK_SCRIPT_FRAGMENTS:
        msg = f"missing from stt_hardening_regress.ps1: {fragment}"
        assert fragment in text, msg

    assert "python scripts/generate_engine_truth.py" in text
    assert "--schema v2" in text
