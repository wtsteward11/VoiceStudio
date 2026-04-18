"""GAP-069 slice 4: unit tests for backend_smoke_freshness discovery and age semantics."""
from __future__ import annotations

import json
import os
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest

_ROOT = Path(__file__).resolve().parents[2]
if str(_ROOT) not in sys.path:
    sys.path.insert(0, str(_ROOT))

import scripts.run_verification as rv


def _ver_dir(root: Path) -> Path:
    d = root / "docs" / "reports" / "verification"
    d.mkdir(parents=True, exist_ok=True)
    return d


def _pass_proof_dict(ts: datetime) -> dict:
    return {
        "schema_version": 1,
        "status": "PASS",
        "timestamp_utc": ts.replace(tzinfo=timezone.utc).isoformat(),
        "port": 8765,
        "cold_start_ms": 100,
        "health_probe_result": True,
        "engines_ready_value": True,
        "api_call_result": True,
        "startup_decision_artifact": None,
        "blocking_reason": None,
        "failure_reason": None,
        "environment_hints": [],
    }


def test_prefers_timestamp_utc_over_mtime(tmp_path: Path) -> None:
    ver = _ver_dir(tmp_path)
    now = datetime.now(timezone.utc)
    data = _pass_proof_dict(now)
    p = ver / "PROOF_BACKEND_SMOKE_test.json"
    p.write_text(json.dumps(data), encoding="utf-8")
    # Ancient mtime; age must still follow JSON (fresh)
    old = 1_000_000_000  # 2001-ish
    os.utime(p, (old, old))

    r = rv._backend_smoke_freshness_result(tmp_path, enforce=False)
    assert r["passed"] is True
    assert "STATUS=FRESH" in r["output_sample"]
    assert "age_source=timestamp_utc" in r["output_sample"]


def test_falls_back_to_mtime_when_field_absent(tmp_path: Path) -> None:
    ver = _ver_dir(tmp_path)
    data = _pass_proof_dict(datetime.now(timezone.utc))
    del data["timestamp_utc"]
    p = ver / "PROOF_BACKEND_SMOKE_nomtime.json"
    p.write_text(json.dumps(data), encoding="utf-8")
    old = 1_000_000_000
    os.utime(p, (old, old))

    r = rv._backend_smoke_freshness_result(tmp_path, enforce=False)
    assert "STATUS=STALE" in r["output_sample"]
    assert "age_source=mtime" in r["output_sample"]


def test_stale_pass_proof_returns_stale(tmp_path: Path) -> None:
    ver = _ver_dir(tmp_path)
    old_ts = datetime.now(timezone.utc) - timedelta(hours=80)
    data = _pass_proof_dict(old_ts)
    p = ver / "PROOF_BACKEND_SMOKE_stale.json"
    p.write_text(json.dumps(data), encoding="utf-8")

    r = rv._backend_smoke_freshness_result(tmp_path, enforce=False)
    assert r["passed"] is True
    assert "STATUS=STALE" in r["output_sample"]
    assert "age_source=timestamp_utc" in r["output_sample"]


def test_blocked_always_passes_regardless_of_age(tmp_path: Path) -> None:
    ver = _ver_dir(tmp_path)
    data = {
        "schema_version": 1,
        "status": "BLOCKED",
        "timestamp_utc": "2000-01-01T00:00:00+00:00",
        "port": None,
        "cold_start_ms": None,
        "health_probe_result": False,
        "engines_ready_value": None,
        "api_call_result": None,
        "startup_decision_artifact": None,
        "blocking_reason": "test",
        "failure_reason": None,
        "environment_hints": [],
    }
    p = ver / "PROOF_BACKEND_SMOKE_blocked.json"
    p.write_text(json.dumps(data), encoding="utf-8")

    r = rv._backend_smoke_freshness_result(tmp_path, enforce=True)
    assert r["passed"] is True
    assert r["exit_code"] == 0
    assert "STATUS=BLOCKED" in r["output_sample"]


def test_missing_message_includes_operator_command(tmp_path: Path) -> None:
    _ver_dir(tmp_path)
    r = rv._backend_smoke_freshness_result(tmp_path, enforce=False)
    msg = r["output_sample"]
    assert "STATUS=MISSING" in msg
    assert "run_backend_smoke.py" in msg
    assert "SkipSmoke" in msg or "-SkipSmoke" in msg


def test_sample_subdir_not_discovered(tmp_path: Path) -> None:
    ver = _ver_dir(tmp_path)
    samples = ver / "samples"
    samples.mkdir(parents=True, exist_ok=True)
    decoy = samples / "PROOF_BACKEND_SMOKE_decoy.json"
    decoy.write_text(json.dumps(_pass_proof_dict(datetime.now(timezone.utc))), encoding="utf-8")

    r = rv._backend_smoke_freshness_result(tmp_path, enforce=False)
    assert "STATUS=MISSING" in r["output_sample"]
