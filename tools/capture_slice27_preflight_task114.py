"""Task 114: capture ``GET /api/health/preflight`` JSON using the same TestClient wiring as unit tests.

When a dedicated Uvicorn port is unavailable (startup crash, CI), this still proves **current
repo** ``preflight_check()`` includes ``checks.whisper_cpp`` (Slice 22 contract).
"""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path
from unittest.mock import MagicMock, patch

from fastapi import FastAPI
from fastapi.testclient import TestClient

_REPO = Path(__file__).resolve().parents[1]
_OUT = _REPO / "docs" / "reports" / "verification" / "slice27" / "slice27_preflight_task114.json"


def _git_head() -> str:
    try:
        return (
            subprocess.run(
                ["git", "rev-parse", "HEAD"],
                cwd=_REPO,
                capture_output=True,
                text=True,
                check=True,
                timeout=10,
            ).stdout.strip()
        )
    except (subprocess.CalledProcessError, OSError, subprocess.TimeoutExpired):
        return "unknown"


def main() -> int:
    mock_health_checker = MagicMock()
    mock_health_checker.check.return_value = MagicMock(
        status="healthy",
        checks={"database": True, "gpu": True, "engines": True},
    )
    mock_engine_service = MagicMock()
    mock_engine_service.list_engines.return_value = [
        {"id": "xtts", "name": "XTTS", "status": "available"},
        {"id": "piper", "name": "Piper", "status": "available"},
    ]
    mock_breaker_stats = {
        "xtts": {
            "state": "closed",
            "failure_count": 0,
            "success_count": 10,
            "last_failure": None,
        },
        "piper": {
            "state": "closed",
            "failure_count": 0,
            "success_count": 5,
            "last_failure": None,
        },
    }
    with (
        patch(
            "backend.api.routes.health.get_health_checker",
            return_value=mock_health_checker,
        ),
        patch(
            "backend.api.routes.health.get_engine_service",
            return_value=mock_engine_service,
        ),
        patch(
            "backend.api.routes.health.get_engine_breaker_stats",
            return_value=mock_breaker_stats,
        ),
        patch(
            "backend.api.routes.health._check_database",
            return_value=True,
        ),
        patch(
            "backend.api.routes.health._check_gpu",
            return_value={
                "status": "healthy",
                "available": True,
                "device_count": 1,
                "device_name": "NVIDIA GeForce RTX 3080",
            },
        ),
        patch(
            "backend.api.routes.health._check_engines",
            return_value={
                "status": "healthy",
                "available_count": 2,
                "engines": ["xtts", "piper"],
            },
        ),
    ):
        from backend.api.routes.health import router

        app = FastAPI()
        app.include_router(router)
        client = TestClient(app)
        response = client.get("/api/health/preflight")
    if response.status_code != 200:
        print("preflight HTTP", response.status_code, file=sys.stderr)
        return 1
    body = response.json()
    checks = body.get("checks") or {}
    w = checks.get("whisper_cpp")
    if not isinstance(w, dict):
        print("checks.whisper_cpp missing or not dict", file=sys.stderr)
        return 2
    if not isinstance(w.get("ok"), bool):
        print("checks.whisper_cpp.ok must be bool", file=sys.stderr)
        return 3

    _OUT.parent.mkdir(parents=True, exist_ok=True)
    _OUT.write_text(json.dumps(body, indent=2), encoding="utf-8")
    meta_path = _OUT.with_name("slice27_preflight_task114_capture.txt")
    meta_path.write_text(
        "Task 114 preflight capture\n"
        f"git_head: {_git_head()}\n"
        "method: FastAPI TestClient (same patches as tests/unit/backend/api/routes/test_health.py)\n"
        "reason: Dedicated Uvicorn on 127.0.0.1:8017 was not kept running in this environment;\n"
        "in-process capture still proves current health.preflight_check publishes checks.whisper_cpp.\n"
        f"written: {_OUT.as_posix()}\n",
        encoding="utf-8",
    )
    print("Wrote", _OUT)
    print("Meta ", meta_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
