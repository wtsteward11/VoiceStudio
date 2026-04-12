"""Unit tests for scripts/ci/run_backend_smoke.py (schema v1 smoke proof)."""
from __future__ import annotations

import json
import sys
from pathlib import Path
from unittest.mock import MagicMock, patch

import pytest

# Import module under test
_ROOT = Path(__file__).resolve().parents[2]
if str(_ROOT) not in sys.path:
    sys.path.insert(0, str(_ROOT))

import scripts.ci.run_backend_smoke as rbs

REQUIRED_KEYS = frozenset(
    {
        "schema_version",
        "status",
        "timestamp_utc",
        "port",
        "cold_start_ms",
        "health_probe_result",
        "engines_ready_value",
        "api_call_result",
        "startup_decision_artifact",
        "blocking_reason",
        "failure_reason",
        "environment_hints",
    },
)


def test_write_proof_creates_file(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(rbs, "VERIFICATION_DIR", tmp_path)
    proof = rbs._proof_blocked_prerequisites()
    out = rbs._write_proof(proof)
    assert out.is_file()
    data = json.loads(out.read_text(encoding="utf-8"))
    assert data["status"] == "BLOCKED"


def test_write_proof_schema_version(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(rbs, "VERIFICATION_DIR", tmp_path)
    proof = rbs._proof_blocked_prerequisites()
    rbs._write_proof(proof)
    written = next(tmp_path.glob("PROOF_BACKEND_SMOKE_*.json"))
    data = json.loads(written.read_text(encoding="utf-8"))
    assert data["schema_version"] == rbs.SMOKE_SCHEMA_VERSION


def test_run_prerequisites_blocked_path() -> None:
    mock_run = MagicMock()
    mock_run.return_value = MagicMock(returncode=2, stderr="", stdout="")
    with patch.object(rbs.subprocess, "run", mock_run):
        code, err = rbs._run_prerequisites_check()
    assert code == 2
    assert err is None


def test_run_prerequisites_fail_path() -> None:
    mock_run = MagicMock()
    mock_run.return_value = MagicMock(returncode=1, stderr="bad", stdout="")
    with patch.object(rbs.subprocess, "run", mock_run):
        code, err = rbs._run_prerequisites_check()
    assert code == 1
    assert err == "bad"


def test_run_prerequisites_pass_path() -> None:
    mock_run = MagicMock()
    mock_run.return_value = MagicMock(returncode=0, stderr="", stdout="")
    with patch.object(rbs.subprocess, "run", mock_run):
        code, err = rbs._run_prerequisites_check()
    assert code == 0
    assert err is None


def test_wait_health_timeout() -> None:
    with patch("urllib.request.urlopen", side_effect=OSError("nope")):
        cold, ok = rbs._wait_health_200("http://127.0.0.1:9/health", rbs.time.monotonic(), 0.15)
    assert cold is None
    assert ok is False


def test_wait_health_pass() -> None:
    mock_resp = MagicMock()
    mock_resp.status = 200

    def _open(*_a: object, **_k: object) -> MagicMock:
        return mock_resp

    with patch("urllib.request.urlopen", _open):
        cold, ok = rbs._wait_health_200("http://127.0.0.1:1/health", rbs.time.monotonic(), 2.0)
    assert ok is True
    assert cold is not None


def test_get_api_health_missing_engines_ready() -> None:
    mock_resp = MagicMock()
    mock_resp.status = 200

    def _open(*_a: object, **_k: object) -> MagicMock:
        return mock_resp

    with patch("urllib.request.urlopen", _open):
        mock_resp.read = MagicMock(return_value=b"{}")
        data, err = rbs._get_api_health_json("http://127.0.0.1:1/api/health")
    assert data == {}
    assert err is None


def test_blocked_proof_file_written_on_exit2(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(rbs, "VERIFICATION_DIR", tmp_path)

    def _pre() -> tuple[int, str | None]:
        return 2, None

    monkeypatch.setattr(rbs, "_run_prerequisites_check", _pre)
    code = rbs.main()
    assert code == 2
    files = list(tmp_path.glob("PROOF_BACKEND_SMOKE_*.json"))
    assert len(files) == 1
    body = json.loads(files[0].read_text(encoding="utf-8"))
    assert body["status"] == "BLOCKED"
    assert body["blocking_reason"]


def test_proof_shape_all_fields_present(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(rbs, "VERIFICATION_DIR", tmp_path)
    proof: dict = {
        "schema_version": rbs.SMOKE_SCHEMA_VERSION,
        "status": "PASS",
        "timestamp_utc": "2026-04-11T00:00:00+00:00",
        "port": 1,
        "cold_start_ms": 1.0,
        "health_probe_result": True,
        "engines_ready_value": True,
        "api_call_result": {"ok": True},
        "startup_decision_artifact": None,
        "blocking_reason": None,
        "failure_reason": None,
        "environment_hints": [],
    }
    rbs._write_proof(proof)
    written = next(tmp_path.glob("PROOF_BACKEND_SMOKE_*.json"))
    data = json.loads(written.read_text(encoding="utf-8"))
    missing = REQUIRED_KEYS - data.keys()
    assert not missing
