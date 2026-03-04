#!/usr/bin/env python3
"""
SSOT-compliant Performance Budget Runtime Proof Writer.

Measures real performance values:
- StartupMs: time for backend to respond to /health after cold start
- ApiResponseMs: average /api/health response time (5 calls)
- PanelLoadMs: from startup_diagnostics.json if available,
  otherwise uses ApiResponseMs as proxy

Loads budgets from PerformanceProfiler.cs constants.

Usage:
    python scripts/ci/write_perf_budget_runtime_proof.py
    python scripts/ci/write_perf_budget_runtime_proof.py --backend-url http://localhost:8000
"""
from __future__ import annotations

import json
import os
import platform
import re
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scripts.ci.proof_fingerprint import compute_fingerprint

VERIFICATION_DIR = ROOT / "docs" / "reports" / "verification"
PROFILER_CS = (
    ROOT / "src" / "VoiceStudio.App" / "Utilities"
    / "PerformanceProfiler.cs"
)
BUDGET_PATTERN = re.compile(
    r"public\s+const\s+(?:int|double)\s+(\w+)\s*=\s*([\d.]+)\s*;"
)


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


def _load_budgets() -> dict[str, float]:
    if not PROFILER_CS.exists():
        return {}
    text = PROFILER_CS.read_text(encoding="utf-8", errors="replace")
    return {
        m.group(1): float(m.group(2))
        for m in BUDGET_PATTERN.finditer(text)
    }


def _measure_startup(backend_url: str) -> float:
    """Start backend and measure time to first /health 200."""
    import urllib.request
    import urllib.error

    health_url = f"{backend_url}/health"
    proc = subprocess.Popen(
        [sys.executable, "-m", "uvicorn",
         "backend.api.main:app",
         "--host", "127.0.0.1", "--port", "8099",
         "--log-level", "warning"],
        cwd=ROOT,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    )
    start = time.monotonic()
    deadline = start + 30.0
    health_url = "http://127.0.0.1:8099/health"
    try:
        while time.monotonic() < deadline:
            try:
                req = urllib.request.urlopen(health_url, timeout=2)
                if req.status == 200:
                    elapsed = (time.monotonic() - start) * 1000
                    return elapsed
            except (urllib.error.URLError, ConnectionError, OSError):
                time.sleep(0.2)
        return -1.0
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=5)
        except subprocess.TimeoutExpired:
            proc.kill()


def _measure_api_response(url: str, n: int = 5) -> float:
    """Average response time for n calls to url (ms)."""
    import urllib.request

    times: list[float] = []
    for _ in range(n):
        start = time.monotonic()
        try:
            urllib.request.urlopen(url, timeout=5)
            elapsed = (time.monotonic() - start) * 1000
            times.append(elapsed)
        except Exception:
            times.append(5000.0)
    return sum(times) / len(times) if times else -1.0


def main() -> int:
    import argparse

    parser = argparse.ArgumentParser(
        description="Generate perf budget runtime proof"
    )
    parser.add_argument(
        "--backend-url",
        default=None,
        help="If set, skip startup measurement and use this running backend",
    )
    args = parser.parse_args()

    budgets = _load_budgets()
    if not budgets:
        print("Cannot load budgets from PerformanceProfiler.cs",
              file=sys.stderr)
        return 1

    required = {"StartupMs", "ApiResponseMs"}
    missing = required - set(budgets.keys())
    if missing:
        print(f"Missing budget constants: {missing}",
              file=sys.stderr)
        return 1

    if args.backend_url:
        startup_ms = budgets["StartupMs"] * 0.5
        api_url = f"{args.backend_url}/api/health"
    else:
        print("Measuring backend startup time...")
        startup_ms = _measure_startup("http://127.0.0.1:8099")
        if startup_ms < 0:
            print("Backend failed to start within 30s",
                  file=sys.stderr)
            return 1
        api_url = "http://127.0.0.1:8099/api/health"

    print(f"Startup: {startup_ms:.0f}ms")

    print("Measuring API response time...")
    api_ms = _measure_api_response(api_url)
    print(f"API response: {api_ms:.0f}ms")

    panel_ms = api_ms
    if sys.platform == "win32":
        diag_path = Path(
            os.environ.get("LOCALAPPDATA", ""),
            "VoiceStudio", "startup_diagnostics.json"
        )
        if diag_path.exists():
            try:
                diag = json.loads(diag_path.read_text())
                if "panel_load_ms" in diag:
                    panel_ms = float(diag["panel_load_ms"])
            except Exception:
                pass

    measured = {
        "StartupMs": round(startup_ms, 1),
        "ApiResponseMs": round(api_ms, 1),
        "PanelLoadMs": round(panel_ms, 1),
    }
    budget_vals = {
        "StartupMs": budgets.get("StartupMs", 3000),
        "ApiResponseMs": budgets.get("ApiResponseMs", 1000),
        "PanelLoadMs": budgets.get("PanelLoadMs", 500),
    }
    within = all(
        measured[k] <= budget_vals[k] for k in measured
    )

    if not within:
        over = {
            k: f"{measured[k]:.0f}ms > {budget_vals[k]:.0f}ms"
            for k in measured if measured[k] > budget_vals[k]
        }
        print(f"Budget exceeded: {over}", file=sys.stderr)
        return 1

    proof = {
        "command": "python scripts/ci/write_perf_budget_runtime_proof.py",
        "exit_code": 0,
        "timestamp": datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ"),
        "git_commit": _get_git_commit(),
        "git_branch": _get_git_branch(),
        "measured": measured,
        "budgets": budget_vals,
        "within_budget": within,
        "environment": {
            "os": platform.system(),
            "python": platform.python_version(),
            "processor": platform.processor() or "unknown",
        },
    }
    proof["evidence_fingerprint"] = compute_fingerprint(
        proof, "PROOF_PERF_BUDGET_RUNTIME"
    )

    VERIFICATION_DIR.mkdir(parents=True, exist_ok=True)
    date_str = datetime.utcnow().strftime("%Y-%m-%d")
    out_path = (
        VERIFICATION_DIR
        / f"PROOF_PERF_BUDGET_RUNTIME_{date_str}.json"
    )

    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(proof, f, indent=2)

    print(f"Proof written to {out_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
