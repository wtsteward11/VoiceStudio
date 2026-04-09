"""Tests for GAP-015 slice 2: runtime_proof_staleness enforce vs advisory."""
from __future__ import annotations

import importlib.util
from pathlib import Path

import pytest

_REPO_ROOT = Path(__file__).resolve().parents[2]
_RV_PATH = _REPO_ROOT / "scripts" / "run_verification.py"


def _load_run_verification():
    spec = importlib.util.spec_from_file_location("run_verification_mod", _RV_PATH)
    assert spec and spec.loader
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


@pytest.fixture(scope="module")
def rv_mod():
    return _load_run_verification()


def test_staleness_advisory_passes_when_missing_proof(tmp_path: Path, rv_mod) -> None:
    """Default mode: missing proof is reported but does not fail."""
    res = rv_mod._runtime_proof_staleness_result(tmp_path, enforce=False)
    assert res["passed"] is True
    assert res["exit_code"] == 0
    assert "MISSING" in res["output_sample"]


def test_staleness_enforce_fails_when_missing_proof(tmp_path: Path, rv_mod) -> None:
    """Enforce mode: missing proof fails the staleness row."""
    res = rv_mod._runtime_proof_staleness_result(tmp_path, enforce=True)
    assert res["passed"] is False
    assert res["exit_code"] == 1
    assert "MISSING" in res["output_sample"]


def test_slo_baseline_freshness_advisory_when_missing(tmp_path: Path, rv_mod) -> None:
    """GAP-015 slice 3: no slo_baselines.json is advisory-only (never fails)."""
    res = rv_mod._slo_baseline_freshness_result(tmp_path)
    assert res["passed"] is True
    assert res["exit_code"] == 0
    assert res["name"] == "slo_baseline_freshness"
    assert "MISSING" in res["output_sample"]


def test_slo_baseline_freshness_fresh_under_artifacts_verify(tmp_path: Path, rv_mod) -> None:
    art = tmp_path / "artifacts" / "verify" / "fake_run"
    art.mkdir(parents=True)
    (art / "slo_baselines.json").write_text('{"schema_version": 1}\n', encoding="utf-8")
    res = rv_mod._slo_baseline_freshness_result(tmp_path)
    assert res["passed"] is True
    assert "FRESH" in res["output_sample"]
