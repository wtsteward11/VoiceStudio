"""Opt-in live integration for voice synthesis proof harness (no default CI)."""

from __future__ import annotations

import os
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent
HARNESS = ROOT / "scripts" / "proof" / "run_voice_synthesis_real_engine_proof.py"


@pytest.mark.real_voice_synthesis_proof
def test_live_harness_smoke_when_opt_in(tmp_path: Path) -> None:
    """Runs the harness against VOICESTUDIO_REAL_ENGINE_PROOF_BASE when explicitly enabled."""
    if os.environ.get("VOICESTUDIO_RUN_REAL_ENGINE_PROOF", "").strip() != "1":
        pytest.skip("Set VOICESTUDIO_RUN_REAL_ENGINE_PROOF=1 to run live harness integration")

    base = os.environ.get("VOICESTUDIO_REAL_ENGINE_PROOF_BASE", "http://127.0.0.1:8000").strip()
    out = tmp_path / "live_harness_out"
    proc = subprocess.run(
        [
            sys.executable,
            str(HARNESS),
            "--base-url",
            base,
            "--output-dir",
            str(out),
        ],
        cwd=str(ROOT),
        capture_output=True,
        text=True,
        timeout=300,
    )
    assert proc.returncode in (0, 1), (
        f"harness unexpected exit {proc.returncode}\nstdout:\n{proc.stdout}\nstderr:\n{proc.stderr}"
    )
