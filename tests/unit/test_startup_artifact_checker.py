"""Unit tests for scripts/ci/check_startup_artifact.py (schema v3 regression guard)."""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

from scripts.ci.check_startup_artifact import ADVISORY_HEALTHY_ELAPSED_MS, check_artifact


def _minimal_valid_v2(**overrides: object) -> dict:
    """Payload shape aligned with BackendProcessManager.WriteStartupArtifact."""
    base = {
        "schema_version": 3,
        "status": "success",
        "timestamp_utc": "2026-04-11T12:00:00.0000000Z",
        "decision": "spawn",
        "health_probe_result": True,
        "port_occupied": False,
        "backend_pid": 12345,
        "spawn_attempted": True,
        "reused_existing_backend": False,
        "conflict_category": None,
        "timeout_seconds": 120,
        "elapsed_ms": 100.0,
        "spawn_elapsed_ms": 50.0,
        "health_attempts": 3,
        "healthy_elapsed_ms": 30.0,
        "last_stderr_lines": [],
        "python_path_resolved": r"C:\Python\python.exe",
        "baseline_deps_valid": None,
    }
    base.update(overrides)
    return base


def _run_checker(tmp_path: Path, data: dict | None, name: str = "startup_decision.json") -> tuple[int, dict]:
    p = tmp_path / name
    if data is not None:
        p.write_text(json.dumps(data, indent=2), encoding="utf-8")
    script = Path(__file__).resolve().parents[2] / "scripts" / "ci" / "check_startup_artifact.py"
    proc = subprocess.run(
        [sys.executable, str(script), "--path", str(p)],
        capture_output=True,
        text=True,
        timeout=30,
        check=False,
    )
    out = json.loads(proc.stdout) if proc.stdout.strip() else {}
    return proc.returncode, out


def test_valid_success_artifact(tmp_path: Path) -> None:
    data = _minimal_valid_v2()
    code, out = _run_checker(tmp_path, data)
    assert code == 0
    assert out["passed"] is True
    assert out["errors"] == []


def test_valid_reuse_decision(tmp_path: Path) -> None:
    data = _minimal_valid_v2(decision="reuse", reused_existing_backend=True)
    code, out = _run_checker(tmp_path, data)
    assert code == 0
    assert out["passed"] is True


def test_missing_file(tmp_path: Path) -> None:
    missing = tmp_path / "nope.json"
    script = Path(__file__).resolve().parents[2] / "scripts" / "ci" / "check_startup_artifact.py"
    proc = subprocess.run(
        [sys.executable, str(script), "--path", str(missing)],
        capture_output=True,
        text=True,
        timeout=30,
        check=False,
    )
    assert proc.returncode == 1
    payload = json.loads(proc.stdout)
    assert payload["passed"] is False


def test_wrong_schema_version(tmp_path: Path) -> None:
    data = _minimal_valid_v2(schema_version=1)
    code, out = _run_checker(tmp_path, data)
    assert code == 1
    assert out["passed"] is False


def test_status_failure_hard_decision(tmp_path: Path) -> None:
    data = _minimal_valid_v2(
        status="failure",
        decision="health_timeout",
        health_probe_result=False,
    )
    code, out = _run_checker(tmp_path, data)
    assert code == 1
    assert any("Operational regression" in e for e in out["errors"])


def test_health_probe_false_on_success(tmp_path: Path) -> None:
    data = _minimal_valid_v2(health_probe_result=False)
    code, out = _run_checker(tmp_path, data)
    assert code == 1
    assert any("contradiction" in e.lower() for e in out["errors"])


def test_missing_required_field(tmp_path: Path) -> None:
    data = _minimal_valid_v2()
    del data["decision"]
    code, out = _run_checker(tmp_path, data)
    assert code == 1
    assert any("Missing required key" in e for e in out["errors"])


def test_advisory_timing_warning(tmp_path: Path) -> None:
    data = _minimal_valid_v2(healthy_elapsed_ms=float(ADVISORY_HEALTHY_ELAPSED_MS + 5_000))
    code, out = _run_checker(tmp_path, data)
    assert code == 0
    assert out["passed"] is True
    assert out["warnings"]
    assert any("healthy_elapsed_ms" in w for w in out["warnings"])


def test_check_artifact_direct_api(tmp_path: Path) -> None:
    p = tmp_path / "startup_decision.json"
    p.write_text(json.dumps(_minimal_valid_v2()), encoding="utf-8")
    r = check_artifact(p)
    assert r.passed
    assert not r.errors


def test_spawn_elapsed_advisory_only(tmp_path: Path) -> None:
    """spawn_elapsed_ms over advisory budget warns; still passed."""
    data = _minimal_valid_v2(spawn_elapsed_ms=15_000.0, healthy_elapsed_ms=1.0)
    p = tmp_path / "s.json"
    p.write_text(json.dumps(data), encoding="utf-8")
    r = check_artifact(p)
    assert r.passed
    assert any("spawn_elapsed_ms" in w for w in r.warnings)
