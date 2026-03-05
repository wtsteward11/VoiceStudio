#!/usr/bin/env python3
"""
Backend Cold-Start Performance Proof Writer.

Windows-only. Cold-starts uvicorn and measures:
- ColdStartMs: process start to first /health 200
- FirstApiMs: first /api/health call after cold start
- WarmApiMs: average of 5 calls after 3 warmup calls

Budgets (realistic for ML-stack): ColdStartMs=45s, FirstApiMs=10s, WarmApiMs=2s.

Usage:
    python scripts/ci/write_backend_cold_start_proof.py
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

ROOT = Path(__file__).resolve().parent.parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scripts.ci.proof_fingerprint import compute_fingerprint

VERIFICATION_DIR = ROOT / "docs" / "reports" / "verification"

# Realistic ML-stack budgets (backend-specific, not UX)
BUDGET_COLD_START_MS = 45000
BUDGET_FIRST_API_MS = 10000
BUDGET_WARM_API_MS = 2000
DEADLINE_S = 90


def _get_git_commit() -> str:
    try:
        out = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            capture_output=True, text=True, cwd=ROOT, timeout=5,
        )
        if out.returncode == 0 and out.stdout.strip():
            return out.stdout.strip()[:40]
    except (subprocess.TimeoutExpired, FileNotFoundError):
        pass
    return "0" * 40


def _get_git_branch() -> str:
    try:
        out = subprocess.run(
            ["git", "branch", "--show-current"],
            capture_output=True, text=True, cwd=ROOT, timeout=5,
        )
        if out.returncode == 0 and out.stdout.strip():
            return out.stdout.strip()
    except (subprocess.TimeoutExpired, FileNotFoundError):
        pass
    return "unknown"


def _find_available_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def main() -> int:
    if sys.platform != "win32":
        print(
            "Windows-only backend cold-start proof.",
            file=sys.stderr,
        )
        return 1

    port = _find_available_port()
    health_url = f"http://127.0.0.1:{port}/health"
    api_url = f"http://127.0.0.1:{port}/api/health"

    proc = subprocess.Popen(
        [
            sys.executable, "-m", "uvicorn",
            "backend.api.main:app",
            "--host", "127.0.0.1", "--port", str(port),
            "--log-level", "warning",
        ],
        cwd=ROOT,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )

    try:
        import urllib.request

        start = time.monotonic()
        deadline = start + DEADLINE_S

        # ColdStartMs: wait for first /health 200
        while time.monotonic() < deadline:
            try:
                req = urllib.request.urlopen(health_url, timeout=2)
                if req.status == 200:
                    cold_start_ms = (time.monotonic() - start) * 1000
                    break
            except OSError:
                time.sleep(0.2)
        else:
            print(
                "Backend failed to start within 90s",
                file=sys.stderr,
            )
            return 1

        # FirstApiMs: first /api/health call
        t0 = time.monotonic()
        try:
            urllib.request.urlopen(api_url, timeout=10)
        except OSError as e:
            print(f"First API call failed: {e}", file=sys.stderr)
            return 1
        first_api_ms = (time.monotonic() - t0) * 1000

        # WarmApiMs: 3 warmup + 5 measured
        for _ in range(3):
            try:
                urllib.request.urlopen(api_url, timeout=5)
            except OSError as e:
                print(f"Warmup call failed: {e}", file=sys.stderr)
                return 1

        times: list[float] = []
        for _ in range(5):
            t0 = time.monotonic()
            try:
                urllib.request.urlopen(api_url, timeout=5)
                times.append((time.monotonic() - t0) * 1000)
            except OSError as e:
                print(f"Measured call failed: {e}", file=sys.stderr)
                return 1
        warm_api_ms = sum(times) / len(times) if times else -1.0

    finally:
        proc.terminate()
        try:
            proc.wait(timeout=5)
        except subprocess.TimeoutExpired:
            proc.kill()

    measured = {
        "cold_start_ms": round(cold_start_ms, 1),
        "first_api_ms": round(first_api_ms, 1),
        "warm_api_ms": round(warm_api_ms, 1),
    }
    budgets = {
        "ColdStartMs": BUDGET_COLD_START_MS,
        "FirstApiMs": BUDGET_FIRST_API_MS,
        "WarmApiMs": BUDGET_WARM_API_MS,
    }
    within = (
        cold_start_ms <= BUDGET_COLD_START_MS
        and first_api_ms <= BUDGET_FIRST_API_MS
        and warm_api_ms <= BUDGET_WARM_API_MS
    )

    if not within:
        over = []
        if cold_start_ms > BUDGET_COLD_START_MS:
            over.append(f"cold_start_ms {cold_start_ms:.0f}ms > {BUDGET_COLD_START_MS}ms")
        if first_api_ms > BUDGET_FIRST_API_MS:
            over.append(f"first_api_ms {first_api_ms:.0f}ms > {BUDGET_FIRST_API_MS}ms")
        if warm_api_ms > BUDGET_WARM_API_MS:
            over.append(f"warm_api_ms {warm_api_ms:.0f}ms > {BUDGET_WARM_API_MS}ms")
        print(f"Budget exceeded: {over}", file=sys.stderr)
        return 1

    proof = {
        "command": "python scripts/ci/write_backend_cold_start_proof.py",
        "exit_code": 0,
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "git_commit": _get_git_commit(),
        "git_branch": _get_git_branch(),
        "cold_start_ms": measured["cold_start_ms"],
        "first_api_ms": measured["first_api_ms"],
        "warm_api_ms": measured["warm_api_ms"],
        "budgets": budgets,
        "within_budget": within,
        "environment": {
            "os": "Windows",
            "runner": os.environ.get("GITHUB_ACTIONS", "local"),
            "mode": "runtime-gates",
        },
    }
    proof["evidence_fingerprint"] = compute_fingerprint(
        proof, "PROOF_BACKEND_COLD_START"
    )

    VERIFICATION_DIR.mkdir(parents=True, exist_ok=True)
    date_str = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    out_path = VERIFICATION_DIR / f"PROOF_BACKEND_COLD_START_{date_str}.json"

    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(proof, f, indent=2)

    print(f"Proof written to {out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
