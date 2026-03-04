"""
Unit tests for scripts/ci/proof_fingerprint.py (M11 tamper-evidence).
"""
from __future__ import annotations

import pytest

from scripts.ci.proof_fingerprint import compute_fingerprint


def test_compute_fingerprint_deterministic() -> None:
    """Same input produces same fingerprint."""
    data = {
        "command": "pytest -q",
        "exit_code": 0,
        "stdout": "passed",
        "stderr": "",
    }
    fp1 = compute_fingerprint(data, "PROOF_PROVENANCE")
    fp2 = compute_fingerprint(data, "PROOF_PROVENANCE")
    assert fp1 == fp2
    assert len(fp1) == 64
    assert all(c in "0123456789abcdef" for c in fp1)


def test_compute_fingerprint_different_input_different_output() -> None:
    """Slight change produces different fingerprint."""
    data1 = {"command": "pytest -q", "exit_code": 0, "stdout": "passed", "stderr": ""}
    data2 = {"command": "pytest -q", "exit_code": 0, "stdout": "failed", "stderr": ""}
    fp1 = compute_fingerprint(data1, "PROOF_PROVENANCE")
    fp2 = compute_fingerprint(data2, "PROOF_PROVENANCE")
    assert fp1 != fp2


def test_large_string_hashed_not_expanded() -> None:
    """Field >250KB uses hash in canonical form; fingerprint still deterministic."""
    big = "x" * 300000
    data = {
        "command": "pytest -q",
        "exit_code": 0,
        "stdout": big,
        "stderr": "",
    }
    fp1 = compute_fingerprint(data, "PROOF_PROVENANCE")
    fp2 = compute_fingerprint(data, "PROOF_PROVENANCE")
    assert fp1 == fp2
