#!/usr/bin/env python3
"""
SSOT-compliant Performance Budget Runtime Proof Writer.

Windows-only. Measures real performance values:
- StartupMs: time for backend to respond to /health after cold start
- ApiResponseMs: average /api/health response time (5 calls)
- PanelLoadMs: from startup_diagnostics.json (required; no proxy)

Loads budgets from PerformanceProfiler.cs constants.

Usage:
    python scripts/ci/write_perf_budget_runtime_proof.py
"""
from __future__ import annotations

import json
import os
import re
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
PROFILER_CS = (
    ROOT / "src" / "VoiceStudio.App" / "Utilities"
    / "PerformanceProfiler.cs"
)
BUDGET_PATTERN = re.compile(
    r"public\s+const\s+(?:int|double)\s+(\w+)\s*=\s*([\d.]+)\s*;"
)
DIAG_PATH = Path(
    os.environ.get("LOCALAPPDATA", ""),
    "VoiceStudio", "Logs", "startup_diagnostics.json"
)


def _get_git_commit() -> str:
    try:
        out = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            capture_output=True, text=True, cwd=ROOT, timeout=5,
        )
        if out.returncode == 0 and out.stdout.strip():
            return out.stdout.strip()[:40]
    # ALLOWED: bare except - best effort, failure acceptable
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
    # ALLOWED: bare except - best effort, failure acceptable
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


def _measure_startup() -> float:
    """Start backend and measure time to first /health 200."""
    import urllib.request
    import urllib.error

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
            except OSError:
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
        except OSError:
            times.append(5000.0)
    return sum(times) / len(times) if times else -1.0


def _read_panel_load_ms() -> float:
    """Read PanelLoadMs from startup_diagnostics.json. Exit 1 if missing."""
    if not DIAG_PATH.exists():
        print(
            f"startup_diagnostics.json not found at {DIAG_PATH}",
            file=sys.stderr,
        )
        sys.exit(1)
    try:
        diag = json.loads(DIAG_PATH.read_text())
    except (json.JSONDecodeError, OSError) as e:
        print(
            f"Cannot read startup_diagnostics.json: {e}",
            file=sys.stderr,
        )
        sys.exit(1)
    if "panel_load_ms" not in diag:
        print(
            "panel_load_ms missing from startup_diagnostics.json ("
            "StartupDiagnostics must emit it)",
            file=sys.stderr,
        )
        sys.exit(1)
    val = diag["panel_load_ms"]
    if not isinstance(val, (int, float)) or val <= 0:
        print(
            f"panel_load_ms must be > 0, got {val!r}",
            file=sys.stderr,
        )
        sys.exit(1)
    return float(val)


def main() -> int:
    if sys.platform != "win32":
        print(
            "Windows-only runtime perf proof.",
            file=sys.stderr,
        )
        return 1

    budgets = _load_budgets()
    if not budgets:
        print("Cannot load budgets from PerformanceProfiler.cs",
              file=sys.stderr)
        return 1

    required = {"StartupMs", "ApiResponseMs", "PanelLoadMs"}
    missing = required - set(budgets.keys())
    if missing:
        print(f"Missing budget constants: {missing}",
              file=sys.stderr)
        return 1

    print("Measuring backend startup time...")
    startup_ms = _measure_startup()
    if startup_ms < 0:
        print("Backend failed to start within 30s",
              file=sys.stderr)
        return 1
    api_url = "http://127.0.0.1:8099/api/health"

    print(f"Startup: {startup_ms:.0f}ms")

    print("Measuring API response time...")
    api_ms = _measure_api_response(api_url)
    print(f"API response: {api_ms:.0f}ms")

    print("Reading PanelLoadMs from startup_diagnostics.json...")
    panel_ms = _read_panel_load_ms()
    print(f"Panel load: {panel_ms:.0f}ms")

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
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "git_commit": _get_git_commit(),
        "git_branch": _get_git_branch(),
        "measured": measured,
        "budgets": budget_vals,
        "within_budget": within,
        "environment": {
            "os": "Windows",
            "runner": os.environ.get("GITHUB_ACTIONS", "local"),
            "mode": "runtime-gates",
        },
        "panel_measurement_source": "startup_diagnostics.json",
    }
    proof["evidence_fingerprint"] = compute_fingerprint(
        proof, "PROOF_PERF_BUDGET_RUNTIME"
    )

    VERIFICATION_DIR.mkdir(parents=True, exist_ok=True)
    date_str = datetime.now(timezone.utc).strftime("%Y-%m-%d")
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
