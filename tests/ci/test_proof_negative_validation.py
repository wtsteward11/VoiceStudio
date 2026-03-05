"""
Anti-theater hardening: proofs MUST fail validation when tampered.

These negative tests prove the proof system cannot be gamed.
Each test creates a corrupted proof in a temp directory and runs
the validator, confirming it rejects the forgery.
"""
from __future__ import annotations

import copy
import json
import tempfile
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent

import sys

if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scripts.ci.check_state_proofs import load_schema, validate_proof
from scripts.ci.proof_fingerprint import compute_fingerprint

SCHEMA = load_schema()

VALID_STUB_PROOF = {
    "command": "pytest tests/e2e/test_golden_path.py -v --engine-mode=stub",
    "exit_code": 0,
    "timestamp": "2026-03-03T12:00:00",
    "git_commit": "a" * 40,
    "git_branch": "main",
    "engine_mode": "stub",
    "checks": {"synthesis": "pass", "transcription": "pass"},
    "output_metrics": {
        "duration_seconds": 2.5,
        "rms_energy": 0.05,
        "output_sha256": "b" * 64,
    },
    "models": {"stub_tts": "c" * 64},
    "passed": True,
    "test_ran": True,
    "pytest_stdout_sha256": "d" * 64,
    "pytest_stderr_sha256": "e" * 64,
    "artifact_path": ".buildlogs/proof_runs/golden_path_stub/proof.json",
    "artifact_sha256": "f" * 64,
    "historical_proof": True,
}
VALID_STUB_PROOF["evidence_fingerprint"] = compute_fingerprint(
    VALID_STUB_PROOF, "PROOF_GOLDEN_PATH_STUB"
)

VALID_GATEC_PROOF = {
    "command": "dotnet test ... --filter UI",
    "exit_code": 0,
    "timestamp": "2026-03-03T12:00:00",
    "git_commit": "a" * 40,
    "git_branch": "main",
    "gatec_log": "all panels navigated",
    "ui_smoke": {
        "exit_code": 0,
        "nav_steps_completed": 12,
        "binding_failure_count": 0,
        "summary_path": ".buildlogs/ui_tests/summary.txt",
        "summary_sha256": "1" * 64,
        "log_path": ".buildlogs/ui_tests/log.txt",
        "log_sha256": "2" * 64,
    },
    "historical_proof": True,
}
VALID_GATEC_PROOF["evidence_fingerprint"] = compute_fingerprint(
    VALID_GATEC_PROOF, "PROOF_GATE_C"
)


def _write_proof(tmpdir: Path, name: str, data: dict) -> Path:
    path = tmpdir / name
    path.write_text(json.dumps(data, indent=2), encoding="utf-8")
    return path


class TestGoldenPathStubNegative:
    """Prove stub proof validation rejects forgeries."""

    def test_test_ran_false_rejected(self, tmp_path: Path) -> None:
        proof = copy.deepcopy(VALID_STUB_PROOF)
        proof["test_ran"] = False
        proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_GOLDEN_PATH_STUB")
        path = _write_proof(tmp_path, "PROOF_GOLDEN_PATH_STUB_2026-03-03.json", proof)
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("test_ran must be true" in e for e in errors), f"Expected test_ran rejection, got: {errors}"

    def test_passed_false_rejected(self, tmp_path: Path) -> None:
        proof = copy.deepcopy(VALID_STUB_PROOF)
        proof["passed"] = False
        proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_GOLDEN_PATH_STUB")
        path = _write_proof(tmp_path, "PROOF_GOLDEN_PATH_STUB_2026-03-03.json", proof)
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("passed must be true" in e for e in errors), f"Expected passed rejection, got: {errors}"

    def test_corrupt_artifact_sha256_rejected(self, tmp_path: Path) -> None:
        proof = copy.deepcopy(VALID_STUB_PROOF)
        proof["artifact_sha256"] = "0000"
        proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_GOLDEN_PATH_STUB")
        path = _write_proof(tmp_path, "PROOF_GOLDEN_PATH_STUB_2026-03-03.json", proof)
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("artifact_sha256 must be 64-hex" in e for e in errors), f"Expected sha256 rejection, got: {errors}"

    def test_wrong_engine_mode_rejected(self, tmp_path: Path) -> None:
        proof = copy.deepcopy(VALID_STUB_PROOF)
        proof["engine_mode"] = "real"
        proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_GOLDEN_PATH_STUB")
        path = _write_proof(tmp_path, "PROOF_GOLDEN_PATH_STUB_2026-03-03.json", proof)
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("engine_mode must be 'stub'" in e for e in errors), f"Expected mode rejection, got: {errors}"

    def test_missing_output_metrics_rejected(self, tmp_path: Path) -> None:
        proof = copy.deepcopy(VALID_STUB_PROOF)
        proof["output_metrics"] = "not_a_dict"
        proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_GOLDEN_PATH_STUB")
        path = _write_proof(tmp_path, "PROOF_GOLDEN_PATH_STUB_2026-03-03.json", proof)
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("output_metrics must be an object" in e for e in errors), f"Expected metrics rejection, got: {errors}"

    def test_zero_duration_rejected(self, tmp_path: Path) -> None:
        proof = copy.deepcopy(VALID_STUB_PROOF)
        proof["output_metrics"]["duration_seconds"] = 0
        proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_GOLDEN_PATH_STUB")
        path = _write_proof(tmp_path, "PROOF_GOLDEN_PATH_STUB_2026-03-03.json", proof)
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("duration_seconds must be > 0" in e for e in errors), f"Expected duration rejection, got: {errors}"

    def test_fingerprint_mismatch_rejected(self, tmp_path: Path) -> None:
        proof = copy.deepcopy(VALID_STUB_PROOF)
        proof["evidence_fingerprint"] = "0" * 64
        path = _write_proof(tmp_path, "PROOF_GOLDEN_PATH_STUB_2026-03-03.json", proof)
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("evidence_fingerprint mismatch" in e for e in errors), f"Expected fingerprint rejection, got: {errors}"

    def test_nonexistent_buildlogs_artifact_rejected(self, tmp_path: Path) -> None:
        """Prove .buildlogs/ artifact paths are NOT exempt from existence validation."""
        proof = copy.deepcopy(VALID_STUB_PROOF)
        proof["artifact_path"] = ".buildlogs/fake/nonexistent.wav"
        proof["artifact_sha256"] = "0" * 64
        proof["historical_proof"] = False
        proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_GOLDEN_PATH_STUB")
        path = _write_proof(tmp_path, "PROOF_GOLDEN_PATH_STUB_2026-03-03.json", proof)
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("artifact_path does not exist" in e for e in errors), (
            f"Expected nonexistent .buildlogs/ artifact rejection, got: {errors}"
        )

    def test_sha256_unverifiable_when_artifact_missing_rejected(self, tmp_path: Path) -> None:
        """Prove SHA256 cannot be verified when artifact file does not exist."""
        proof = copy.deepcopy(VALID_STUB_PROOF)
        proof["artifact_path"] = ".buildlogs/fake/nonexistent.wav"
        proof["artifact_sha256"] = "a" * 64  # valid format, but file doesn't exist
        proof["historical_proof"] = False
        proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_GOLDEN_PATH_STUB")
        path = _write_proof(tmp_path, "PROOF_GOLDEN_PATH_STUB_2026-03-03.json", proof)
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("cannot verify artifact_sha256" in e or "artifact_path does not exist" in e for e in errors), (
            f"Expected SHA256 unverifiable (missing artifact) rejection, got: {errors}"
        )


class TestGateCNegative:
    """Prove Gate C proof validation rejects forgeries."""

    def test_nav_steps_zero_rejected(self, tmp_path: Path) -> None:
        proof = copy.deepcopy(VALID_GATEC_PROOF)
        proof["ui_smoke"]["nav_steps_completed"] = 0
        proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_GATE_C")
        path = _write_proof(tmp_path, "PROOF_GATE_C_2026-03-03.json", proof)
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("nav_steps_completed must be >= 8" in e for e in errors), f"Expected nav_steps rejection, got: {errors}"

    def test_binding_failures_rejected(self, tmp_path: Path) -> None:
        proof = copy.deepcopy(VALID_GATEC_PROOF)
        proof["ui_smoke"]["binding_failure_count"] = 5
        proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_GATE_C")
        path = _write_proof(tmp_path, "PROOF_GATE_C_2026-03-03.json", proof)
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("binding_failure_count must be 0" in e for e in errors), f"Expected binding failure rejection, got: {errors}"

    def test_exit_code_nonzero_rejected(self, tmp_path: Path) -> None:
        proof = copy.deepcopy(VALID_GATEC_PROOF)
        proof["exit_code"] = 1
        proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_GATE_C")
        path = _write_proof(tmp_path, "PROOF_GATE_C_2026-03-03.json", proof)
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("exit_code must be 0" in e for e in errors), f"Expected exit_code rejection, got: {errors}"

    def test_ui_smoke_exit_nonzero_rejected(self, tmp_path: Path) -> None:
        proof = copy.deepcopy(VALID_GATEC_PROOF)
        proof["ui_smoke"]["exit_code"] = 1
        proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_GATE_C")
        path = _write_proof(tmp_path, "PROOF_GATE_C_2026-03-03.json", proof)
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("ui_smoke.exit_code must be 0" in e for e in errors), f"Expected ui_smoke exit rejection, got: {errors}"


class TestGoldenPathRealNegative:
    """Prove real golden path proof validation rejects forgeries."""

    def test_real_mode_missing_models_rejected(self, tmp_path: Path) -> None:
        proof = copy.deepcopy(VALID_STUB_PROOF)
        proof["engine_mode"] = "real"
        proof["models"] = {}
        proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_GOLDEN_PATH_REAL")
        path = _write_proof(tmp_path, "PROOF_GOLDEN_PATH_REAL_2026-03-03.json", proof)
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("models must be a non-empty dict" in e for e in errors), f"Expected models rejection, got: {errors}"

    def test_real_mode_bad_model_hash_rejected(self, tmp_path: Path) -> None:
        proof = copy.deepcopy(VALID_STUB_PROOF)
        proof["engine_mode"] = "real"
        proof["models"] = {"xtts": "not_a_hash"}
        proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_GOLDEN_PATH_REAL")
        path = _write_proof(tmp_path, "PROOF_GOLDEN_PATH_REAL_2026-03-03.json", proof)
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("models.xtts must be 64-hex" in e for e in errors), f"Expected model hash rejection, got: {errors}"


class TestGeneralNegative:
    """Prove general proof validation rejects corrupted proofs."""

    def test_missing_required_key_rejected(self, tmp_path: Path) -> None:
        proof = copy.deepcopy(VALID_STUB_PROOF)
        del proof["command"]
        proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_GOLDEN_PATH_STUB")
        path = _write_proof(tmp_path, "PROOF_GOLDEN_PATH_STUB_2026-03-03.json", proof)
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("missing required keys" in e for e in errors), f"Expected missing key rejection, got: {errors}"

    def test_invalid_json_rejected(self, tmp_path: Path) -> None:
        path = tmp_path / "PROOF_GOLDEN_PATH_STUB_2026-03-03.json"
        path.write_text("{invalid json", encoding="utf-8")
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("JSON parse error" in e for e in errors), f"Expected JSON parse rejection, got: {errors}"

    def test_file_missing_rejected(self, tmp_path: Path) -> None:
        path = tmp_path / "PROOF_GOLDEN_PATH_STUB_NONEXISTENT.json"
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("file missing" in e for e in errors), f"Expected file missing rejection, got: {errors}"

    def test_invalid_git_commit_rejected(self, tmp_path: Path) -> None:
        proof = copy.deepcopy(VALID_STUB_PROOF)
        proof["git_commit"] = "not_a_valid_commit"
        proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_GOLDEN_PATH_STUB")
        path = _write_proof(tmp_path, "PROOF_GOLDEN_PATH_STUB_2026-03-03.json", proof)
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("git_commit must be 40 hex" in e for e in errors), f"Expected git_commit rejection, got: {errors}"

    def test_nonzero_exit_code_rejected(self, tmp_path: Path) -> None:
        proof = copy.deepcopy(VALID_STUB_PROOF)
        proof["exit_code"] = 1
        proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_GOLDEN_PATH_STUB")
        path = _write_proof(tmp_path, "PROOF_GOLDEN_PATH_STUB_2026-03-03.json", proof)
        errors = validate_proof(path, SCHEMA, no_git_match=True)
        assert any("exit_code must be 0" in e for e in errors), f"Expected exit_code rejection, got: {errors}"
