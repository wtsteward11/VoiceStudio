"""
Unit tests for scripts/ci/check_state_proofs.py (M9 Proof Schema Enforcement).
"""
from __future__ import annotations

import json
from pathlib import Path
from unittest.mock import patch

import pytest

import scripts.ci.check_state_proofs as check_module


def test_extract_proof_paths() -> None:
    """extract_proof_paths yields expected paths from STATE.md snippet."""
    content = """
## Next 3 Steps
1. DONE - Proof: `docs/reports/verification/PROOF_PROVENANCE_2026-03-02.json`
2. DONE - Proof: docs/reports/verification/PROOF_GATE_C_2026-03-02.json
3. DONE - Proof: docs/reports/verification/PROOF_INSTALLER_2026-03-02.json
## Active Task
- Proof: docs/reports/verification/PROOF_PAYLOAD_DETOX_2026-03-02.json
"""
    paths = check_module.extract_proof_paths(content)
    assert "docs/reports/verification/PROOF_PROVENANCE_2026-03-02.json" in paths
    assert "docs/reports/verification/PROOF_GATE_C_2026-03-02.json" in paths
    assert "docs/reports/verification/PROOF_INSTALLER_2026-03-02.json" in paths
    assert "docs/reports/verification/PROOF_PAYLOAD_DETOX_2026-03-02.json" in paths
    assert len(paths) == 4


def test_filter_canonical_proof_paths() -> None:
    """filter_canonical_proof_paths keeps only docs/reports/verification/PROOF_*.json."""
    paths = [
        "docs/reports/verification/PROOF_PROVENANCE_2026-03-02.json",
        ".buildlogs/verification/last_run.json",
        "docs/reports/verification/PROOF_GATE_C_2026-03-02.json",
        "artifacts/verify/phase10.json",
    ]
    canonical = check_module.filter_canonical_proof_paths(paths)
    assert len(canonical) == 2
    assert "docs/reports/verification/PROOF_PROVENANCE_2026-03-02.json" in canonical
    assert "docs/reports/verification/PROOF_GATE_C_2026-03-02.json" in canonical


def test_get_proof_type() -> None:
    """get_proof_type returns schema key for known prefixes."""
    assert check_module.get_proof_type("PROOF_PROVENANCE_2026-03-02.json") == "PROOF_PROVENANCE"
    assert check_module.get_proof_type("PROOF_GATE_C_2026-03-02.json") == "PROOF_GATE_C"
    assert check_module.get_proof_type("PROOF_INSTALLER_2026-03-02.json") == "PROOF_INSTALLER"
    assert check_module.get_proof_type("PROOF_PAYLOAD_DETOX_2026-03-02.json") == "PROOF_PAYLOAD_DETOX"
    assert check_module.get_proof_type("last_run.json") is None
    assert check_module.get_proof_type("PROOF_UNKNOWN_2026.json") is None


def test_get_required_keys() -> None:
    """get_required_keys returns common + type-specific keys."""
    schema = {
        "common_required": ["command", "exit_code", "timestamp", "git_commit", "git_branch"],
        "type_specific": {
            "PROOF_PROVENANCE": {"required": ["stdout", "stderr"]},
            "PROOF_GATE_C": {"required": ["ui_smoke", "gatec_log"]},
        },
    }
    keys = check_module.get_required_keys(schema, "PROOF_PROVENANCE")
    assert "command" in keys
    assert "stdout" in keys
    assert "stderr" in keys
    assert len(keys) == 7


def test_validate_proof_valid(tmp_path: Path) -> None:
    """Valid proof JSON passes validation."""
    proof_dir = tmp_path / "docs" / "reports" / "verification"
    proof_dir.mkdir(parents=True)
    proof_file = proof_dir / "PROOF_PROVENANCE_2026-03-02.json"
    proof_file.write_text(
        json.dumps({
            "command": "pytest tests/unit/test_foo.py -q",
            "exit_code": 0,
            "timestamp": "2026-03-02T12:00:00Z",
            "git_commit": "a" * 40,
            "git_branch": "main",
            "stdout": "passed",
            "stderr": "",
        }),
        encoding="utf-8",
    )
    schema = {
        "common_required": ["command", "exit_code", "timestamp", "git_commit", "git_branch"],
        "type_specific": {"PROOF_PROVENANCE": {"required": ["stdout", "stderr"]}},
        "historical_proof_allowlist": [],
    }
    with patch.object(check_module, "ROOT", tmp_path):
        errs = check_module.validate_proof(proof_file, schema, no_git_match=True)
    assert len(errs) == 0


def test_validate_proof_missing_required_key(tmp_path: Path) -> None:
    """Missing required key fails with clear message."""
    proof_dir = tmp_path / "docs" / "reports" / "verification"
    proof_dir.mkdir(parents=True)
    proof_file = proof_dir / "PROOF_PROVENANCE_2026-03-02.json"
    proof_file.write_text(
        json.dumps({
            "exit_code": 0,
            "timestamp": "2026-03-02T12:00:00Z",
            "git_commit": "a" * 40,
            "git_branch": "main",
            "stdout": "",
            "stderr": "",
        }),
        encoding="utf-8",
    )
    schema = {
        "common_required": ["command", "exit_code", "timestamp", "git_commit", "git_branch"],
        "type_specific": {"PROOF_PROVENANCE": {"required": ["stdout", "stderr"]}},
        "historical_proof_allowlist": [],
    }
    with patch.object(check_module, "ROOT", tmp_path):
        errs = check_module.validate_proof(proof_file, schema, no_git_match=True)
    assert any("missing required keys" in e for e in errs)
    assert any("command" in e for e in errs)


def test_validate_proof_exit_code_nonzero(tmp_path: Path) -> None:
    """exit_code != 0 fails."""
    proof_dir = tmp_path / "docs" / "reports" / "verification"
    proof_dir.mkdir(parents=True)
    proof_file = proof_dir / "PROOF_PROVENANCE_2026-03-02.json"
    proof_file.write_text(
        json.dumps({
            "command": "pytest tests/unit/test_foo.py -q",
            "exit_code": 1,
            "timestamp": "2026-03-02T12:00:00Z",
            "git_commit": "a" * 40,
            "git_branch": "main",
            "stdout": "",
            "stderr": "",
        }),
        encoding="utf-8",
    )
    schema = {
        "common_required": ["command", "exit_code", "timestamp", "git_commit", "git_branch"],
        "type_specific": {"PROOF_PROVENANCE": {"required": ["stdout", "stderr"]}},
        "historical_proof_allowlist": [],
    }
    with patch.object(check_module, "ROOT", tmp_path):
        errs = check_module.validate_proof(proof_file, schema, no_git_match=True)
    assert any("exit_code must be 0" in e for e in errs)


def test_validate_proof_bad_timestamp(tmp_path: Path) -> None:
    """Unparsable timestamp fails."""
    proof_dir = tmp_path / "docs" / "reports" / "verification"
    proof_dir.mkdir(parents=True)
    proof_file = proof_dir / "PROOF_PROVENANCE_2026-03-02.json"
    proof_file.write_text(
        json.dumps({
            "command": "pytest tests/unit/test_foo.py -q",
            "exit_code": 0,
            "timestamp": "not-a-date",
            "git_commit": "a" * 40,
            "git_branch": "main",
            "stdout": "",
            "stderr": "",
        }),
        encoding="utf-8",
    )
    schema = {
        "common_required": ["command", "exit_code", "timestamp", "git_commit", "git_branch"],
        "type_specific": {"PROOF_PROVENANCE": {"required": ["stdout", "stderr"]}},
        "historical_proof_allowlist": [],
    }
    with patch.object(check_module, "ROOT", tmp_path):
        errs = check_module.validate_proof(proof_file, schema, no_git_match=True)
    assert any("timestamp" in e and "ISO8601" in e for e in errs)


def test_validate_proof_git_commit_mismatch(tmp_path: Path) -> None:
    """git_commit mismatch fails (mock git; no allowlist bypass)."""
    proof_dir = tmp_path / "docs" / "reports" / "verification"
    proof_dir.mkdir(parents=True)
    proof_file = proof_dir / "PROOF_PROVENANCE_2026-03-02.json"
    proof_file.write_text(
        json.dumps({
            "command": "pytest tests/unit/test_foo.py -q",
            "exit_code": 0,
            "timestamp": "2026-03-02T12:00:00Z",
            "git_commit": "a" * 40,
            "git_branch": "main",
            "stdout": "",
            "stderr": "",
        }),
        encoding="utf-8",
    )
    schema = {
        "common_required": ["command", "exit_code", "timestamp", "git_commit", "git_branch"],
        "type_specific": {"PROOF_PROVENANCE": {"required": ["stdout", "stderr"]}},
        "historical_proof_allowlist": [],
    }
    with patch.object(check_module, "ROOT", tmp_path):
        with patch("subprocess.run") as mock_run:
            mock_run.return_value = type("R", (), {"returncode": 0, "stdout": "b" * 40})()
            errs = check_module.validate_proof(proof_file, schema, no_git_match=False)
    assert any("does not match HEAD" in e for e in errs)


def test_validate_proof_historical_skips_git_match(tmp_path: Path) -> None:
    """historical_proof: true skips git_commit match."""
    proof_dir = tmp_path / "docs" / "reports" / "verification"
    proof_dir.mkdir(parents=True)
    proof_file = proof_dir / "PROOF_PROVENANCE_2026-03-02.json"
    proof_file.write_text(
        json.dumps({
            "command": "pytest tests/unit/test_foo.py -q",
            "exit_code": 0,
            "timestamp": "2026-03-02T12:00:00Z",
            "git_commit": "a" * 40,
            "git_branch": "main",
            "stdout": "",
            "stderr": "",
            "historical_proof": True,
        }),
        encoding="utf-8",
    )
    schema = {
        "common_required": ["command", "exit_code", "timestamp", "git_commit", "git_branch"],
        "type_specific": {"PROOF_PROVENANCE": {"required": ["stdout", "stderr"]}},
        "historical_proof_allowlist": [],
    }
    with patch.object(check_module, "ROOT", tmp_path):
        with patch("subprocess.run") as mock_run:
            mock_run.return_value = type("R", (), {"returncode": 0, "stdout": "b" * 40})()
            errs = check_module.validate_proof(proof_file, schema, no_git_match=False)
    assert not any("does not match HEAD" in e for e in errs)
    assert len(errs) == 0


def test_validate_proof_gate_c_ui_smoke_exit_code_nonzero_fails(tmp_path: Path) -> None:
    """PROOF_GATE_C with ui_smoke.exit_code != 0 fails nested validation."""
    proof_dir = tmp_path / "docs" / "reports" / "verification"
    proof_dir.mkdir(parents=True)
    proof_file = proof_dir / "PROOF_GATE_C_2026-03-02.json"
    proof_file.write_text(
        json.dumps({
            "command": ".\\scripts\\gatec-publish-launch.ps1 -UiSmoke",
            "exit_code": 0,
            "timestamp": "2026-03-02T12:00:00Z",
            "git_commit": "a" * 40,
            "git_branch": "main",
            "ui_smoke": {"exit_code": 1, "nav_steps_completed": 0, "binding_failure_count": 0},
            "gatec_log": "log content",
        }),
        encoding="utf-8",
    )
    schema = {
        "common_required": ["command", "exit_code", "timestamp", "git_commit", "git_branch"],
        "type_specific": {"PROOF_GATE_C": {"required": ["ui_smoke", "gatec_log"]}},
        "nested_semantics": {"PROOF_GATE_C": {"ui_smoke.exit_code": 0}},
    }
    with patch.object(check_module, "ROOT", tmp_path):
        errs = check_module.validate_proof(proof_file, schema, no_git_match=True)
    assert any("ui_smoke.exit_code" in e for e in errs)
    assert any("must be 0" in e for e in errs)


def test_validate_proof_gate_c_ui_smoke_exit_code_zero_passes(tmp_path: Path) -> None:
    """PROOF_GATE_C with ui_smoke.exit_code == 0 passes nested check."""
    proof_dir = tmp_path / "docs" / "reports" / "verification"
    proof_dir.mkdir(parents=True)
    proof_file = proof_dir / "PROOF_GATE_C_2026-03-02.json"
    proof_file.write_text(
        json.dumps({
            "command": ".\\scripts\\gatec-publish-launch.ps1 -UiSmoke",
            "exit_code": 0,
            "timestamp": "2026-03-02T12:00:00Z",
            "git_commit": "a" * 40,
            "git_branch": "main",
            "ui_smoke": {"exit_code": 0, "nav_steps_completed": 0, "binding_failure_count": 0},
            "gatec_log": "log content",
        }),
        encoding="utf-8",
    )
    schema = {
        "common_required": ["command", "exit_code", "timestamp", "git_commit", "git_branch"],
        "type_specific": {"PROOF_GATE_C": {"required": ["ui_smoke", "gatec_log"]}},
        "nested_semantics": {"PROOF_GATE_C": {"ui_smoke.exit_code": 0}},
    }
    with patch.object(check_module, "ROOT", tmp_path):
        errs = check_module.validate_proof(proof_file, schema, no_git_match=True)
    assert len(errs) == 0


def test_validate_proof_installer_missing_step_fails(tmp_path: Path) -> None:
    """PROOF_INSTALLER missing a required step fails nested validation."""
    proof_dir = tmp_path / "docs" / "reports" / "verification"
    proof_dir.mkdir(parents=True)
    proof_file = proof_dir / "PROOF_INSTALLER_2026-03-02.json"
    proof_file.write_text(
        json.dumps({
            "command": ".\\installer\\test-installer-lifecycle.ps1",
            "exit_code": 0,
            "timestamp": "2026-03-02T12:00:00Z",
            "git_commit": "a" * 40,
            "git_branch": "main",
            "results": {
                "InstallV1": "PASS",
                "LaunchV1": "PASS",
            },
            "all_passed": True,
        }),
        encoding="utf-8",
    )
    schema = {
        "common_required": ["command", "exit_code", "timestamp", "git_commit", "git_branch"],
        "type_specific": {"PROOF_INSTALLER": {"required": ["results", "all_passed"]}},
        "nested_semantics": {
            "PROOF_INSTALLER": {
                "results_required_keys": [
                    "InstallV1", "LaunchV1", "UpgradeV1ToV2", "LaunchV2",
                    "RollbackV2ToV1", "LaunchV1AfterRollback", "UninstallV1",
                ],
                "results_all_pass_when_all_passed_true": True,
            },
        },
    }
    with patch.object(check_module, "ROOT", tmp_path):
        errs = check_module.validate_proof(proof_file, schema, no_git_match=True)
    assert any("results missing required keys" in e for e in errs)


def test_validate_proof_installer_step_fail_when_all_passed_fails(tmp_path: Path) -> None:
    """PROOF_INSTALLER with all_passed true but one step FAIL fails nested validation."""
    proof_dir = tmp_path / "docs" / "reports" / "verification"
    proof_dir.mkdir(parents=True)
    proof_file = proof_dir / "PROOF_INSTALLER_2026-03-02.json"
    proof_file.write_text(
        json.dumps({
            "command": ".\\installer\\test-installer-lifecycle.ps1",
            "exit_code": 0,
            "timestamp": "2026-03-02T12:00:00Z",
            "git_commit": "a" * 40,
            "git_branch": "main",
            "results": {
                "InstallV1": "PASS",
                "LaunchV1": "PASS",
                "UpgradeV1ToV2": "PASS",
                "LaunchV2": "PASS",
                "RollbackV2ToV1": "PASS",
                "LaunchV1AfterRollback": "PASS",
                "UninstallV1": "FAIL",
            },
            "all_passed": True,
        }),
        encoding="utf-8",
    )
    schema = {
        "common_required": ["command", "exit_code", "timestamp", "git_commit", "git_branch"],
        "type_specific": {"PROOF_INSTALLER": {"required": ["results", "all_passed"]}},
        "nested_semantics": {
            "PROOF_INSTALLER": {
                "results_required_keys": [
                    "InstallV1", "LaunchV1", "UpgradeV1ToV2", "LaunchV2",
                    "RollbackV2ToV1", "LaunchV1AfterRollback", "UninstallV1",
                ],
                "results_all_pass_when_all_passed_true": True,
            },
        },
    }
    with patch.object(check_module, "ROOT", tmp_path):
        errs = check_module.validate_proof(proof_file, schema, no_git_match=True)
    assert any("all_passed=true but results not all PASS" in e for e in errs)
