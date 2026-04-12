#!/usr/bin/env python3
"""
Bounded operator smoke: prerequisites → uvicorn → /health → /api/health.

Emits docs/reports/verification/PROOF_BACKEND_SMOKE_<timestamp>.json (schema v1).

Exit codes: 0 PASS, 1 FAIL, 2 BLOCKED (check_runtime_prerequisites.py exit 2).
"""
from __future__ import annotations

import json
import os
import socket
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, cast

ROOT = Path(__file__).resolve().parent.parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

VERIFICATION_DIR = ROOT / "docs" / "reports" / "verification"
DEADLINE_S = 90

SMOKE_SCHEMA_VERSION = 1

ENVIRONMENT_HINTS_DEFAULT = [
    "Set VOICESTUDIO_MODELS_PATH if using local models",
    "Ensure Piper / engine assets per check_runtime_prerequisites.py",
    "Run: python scripts/ci/check_runtime_prerequisites.py",
]


def _find_available_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(("127.0.0.1", 0))
        _host, port = s.getsockname()
        return int(port)


def _default_startup_artifact_path() -> Path:
    la = os.environ.get("LOCALAPPDATA", "").strip()
    if la:
        return Path(la) / "VoiceStudio" / "crashes" / "startup_decision.json"
    xdg = os.environ.get("XDG_DATA_HOME", str(Path.home() / ".local" / "share"))
    return Path(xdg) / "VoiceStudio" / "crashes" / "startup_decision.json"


def _read_startup_artifact() -> dict[str, Any] | None:
    p = _default_startup_artifact_path()
    if not p.is_file():
        return None
    try:
        raw = json.loads(p.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return None
    if isinstance(raw, dict):
        return cast(dict[str, Any], raw)
    return None


def _proof_blocked_prerequisites() -> dict[str, Any]:
    return {
        "schema_version": SMOKE_SCHEMA_VERSION,
        "status": "BLOCKED",
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "port": None,
        "cold_start_ms": None,
        "health_probe_result": False,
        "engines_ready_value": None,
        "api_call_result": None,
        "startup_decision_artifact": None,
        "blocking_reason": "check_runtime_prerequisites exit 2 (prerequisites not satisfied)",
        "failure_reason": None,
        "environment_hints": list(ENVIRONMENT_HINTS_DEFAULT),
    }


def _proof_fail_prerequisites(stderr_snippet: str | None) -> dict[str, Any]:
    fr = "check_runtime_prerequisites non-zero exit"
    if stderr_snippet and stderr_snippet.strip():
        fr = f"{fr}: {(stderr_snippet.strip())[:1500]}"
    return {
        "schema_version": SMOKE_SCHEMA_VERSION,
        "status": "FAIL",
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "port": None,
        "cold_start_ms": None,
        "health_probe_result": False,
        "engines_ready_value": None,
        "api_call_result": None,
        "startup_decision_artifact": None,
        "blocking_reason": None,
        "failure_reason": fr,
        "environment_hints": list(ENVIRONMENT_HINTS_DEFAULT),
    }


def _run_prerequisites_check() -> tuple[int, str | None]:
    """Returns (exit_code, stderr_snippet_for_fail). Exit 0 = ok, 1 = fail, 2 = blocked."""
    prereq = ROOT / "scripts" / "ci" / "check_runtime_prerequisites.py"
    proc_pr = subprocess.run(
        [sys.executable, str(prereq)],
        cwd=ROOT,
        capture_output=True,
        text=True,
        timeout=180,
        check=False,
    )
    if proc_pr.returncode == 2:
        msg = {
            "status": "BLOCKED",
            "blocking_reason": "check_runtime_prerequisites exit 2",
        }
        print(json.dumps(msg, indent=2))
        return 2, None
    if proc_pr.returncode != 0:
        stderr = (proc_pr.stderr or "")[:2000]
        body = {
            "status": "FAIL",
            "failure_reason": "check_runtime_prerequisites non-zero",
            "exit_code": proc_pr.returncode,
            "stderr": stderr,
        }
        print(json.dumps(body, indent=2))
        return 1, stderr
    return 0, None


def _env_with_pythonpath() -> dict[str, str]:
    env = os.environ.copy()
    root_str = str(ROOT)
    existing = env.get("PYTHONPATH", "").strip()
    if root_str:
        if existing:
            if root_str not in existing.split(os.pathsep):
                env["PYTHONPATH"] = root_str + os.pathsep + existing
        else:
            env["PYTHONPATH"] = root_str
    return env


def _wait_health_200(url: str, start: float, budget_s: float) -> tuple[float | None, bool]:
    import urllib.request

    deadline = start + budget_s
    while time.monotonic() < deadline:
        try:
            req = urllib.request.urlopen(url, timeout=2)
            if req.status == 200:
                cold_ms = (time.monotonic() - start) * 1000
                return cold_ms, True
        except OSError:
            time.sleep(0.2)
    return None, False


def _get_api_health_json(url: str) -> tuple[dict[str, Any] | None, str | None]:
    import urllib.request

    try:
        req = urllib.request.urlopen(url, timeout=15)
        body = req.read().decode("utf-8")
        data = json.loads(body)
    except (OSError, json.JSONDecodeError) as exc:
        return None, str(exc)[:500]
    if req.status != 200:
        return None, f"HTTP {req.status}"
    if not isinstance(data, dict):
        return None, "response root is not an object"
    return cast(dict[str, Any], data), None


def _write_proof(proof: dict[str, Any]) -> Path:
    VERIFICATION_DIR.mkdir(parents=True, exist_ok=True)
    ts = datetime.now(timezone.utc).strftime("%Y%m%d_%H%M%S")
    out = VERIFICATION_DIR / f"PROOF_BACKEND_SMOKE_{ts}.json"
    out.write_text(json.dumps(proof, indent=2), encoding="utf-8")
    summary = {"proof_path": str(out), "status": proof.get("status")}
    print(json.dumps(summary, indent=2))
    return out


def _base_proof_running(port: int) -> dict[str, Any]:
    return {
        "schema_version": SMOKE_SCHEMA_VERSION,
        "status": "FAIL",
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "port": port,
        "cold_start_ms": None,
        "health_probe_result": False,
        "engines_ready_value": None,
        "api_call_result": None,
        "startup_decision_artifact": None,
        "blocking_reason": None,
        "failure_reason": None,
        "environment_hints": [],
    }


def main() -> int:
    pre, stderr_snip = _run_prerequisites_check()
    if pre == 2:
        _write_proof(_proof_blocked_prerequisites())
        return 2
    if pre == 1:
        _write_proof(_proof_fail_prerequisites(stderr_snip))
        return 1

    port = _find_available_port()
    health_url = f"http://127.0.0.1:{port}/health"
    api_url = f"http://127.0.0.1:{port}/api/health"

    env = _env_with_pythonpath()

    uvicorn_proc = subprocess.Popen(
        [
            sys.executable,
            "-m",
            "uvicorn",
            "backend.api.main:app",
            "--host",
            "127.0.0.1",
            "--port",
            str(port),
            "--log-level",
            "warning",
        ],
        cwd=ROOT,
        env=env,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )

    proof: dict[str, Any] = _base_proof_running(port)

    try:
        start = time.monotonic()
        cold_start_ms, health_ok = _wait_health_200(health_url, start, DEADLINE_S)
        if not health_ok:
            proof["failure_reason"] = "GET /health did not return 200 within 90s"
            proof["cold_start_ms"] = (time.monotonic() - start) * 1000
            _write_proof(proof)
            return 1

        proof["cold_start_ms"] = cold_start_ms
        proof["health_probe_result"] = True

        api_json, api_err = _get_api_health_json(api_url)
        if api_json is None:
            proof["api_call_result"] = {"ok": False, "error": api_err}
            proof["failure_reason"] = api_err or "GET /api/health failed"
            _write_proof(proof)
            return 1

        if "engines_ready" not in api_json:
            proof["api_call_result"] = {
                "ok": False,
                "error": "engines_ready missing from /api/health JSON",
            }
            proof["failure_reason"] = "engines_ready missing from /api/health JSON"
            _write_proof(proof)
            return 1

        proof["engines_ready_value"] = api_json.get("engines_ready")
        proof["api_call_result"] = {"ok": True}
        proof["startup_decision_artifact"] = _read_startup_artifact()
        proof["status"] = "PASS"
        _write_proof(proof)
        return 0
    finally:
        try:
            uvicorn_proc.terminate()
            uvicorn_proc.wait(timeout=15)
        except (OSError, subprocess.TimeoutExpired) as exc:
            proof.setdefault("cleanup_error", str(exc)[:300])
            try:
                uvicorn_proc.kill()
            except OSError as exc_kill:
                prev = proof.get("cleanup_error", "")
                suffix = f"; kill_failed: {exc_kill}"
                proof["cleanup_error"] = (str(prev) + suffix)[:500]


if __name__ == "__main__":
    raise SystemExit(main())
