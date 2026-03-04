#!/usr/bin/env python3
"""
Produce Phase 3 bulletproof proof artifact (Milestone-proof schema).

Runs route boundaries, route size, service boundaries, and verification checks.
Writes PROOF_PHASE_3_BULLETPROOF_YYYY-MM-DD.json with full schema fields.

Usage: python scripts/ci/write_phase3_proof.py
"""
from __future__ import annotations

import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scripts.ci.proof_fingerprint import compute_fingerprint

PROOF_DIR = ROOT / "docs" / "reports" / "verification"


def _run(cmd: list[str], cwd: Path | None = None) -> tuple[int, str, str]:
    """Run command, return (exit_code, stdout, stderr)."""
    r = subprocess.run(
        cmd,
        cwd=cwd or ROOT,
        capture_output=True,
        text=True,
        timeout=60,
    )
    return r.returncode, r.stdout or "", r.stderr or ""


def _git_info() -> tuple[str, str]:
    """Return (commit_hash, branch)."""
    commit_ec, commit_out, _ = _run(["git", "rev-parse", "HEAD"])
    branch_ec, branch_out, _ = _run(["git", "branch", "--show-current"])
    return (
        commit_out.strip() if commit_ec == 0 else "unknown",
        branch_out.strip() if branch_ec == 0 else "unknown",
    )


def main() -> int:
    PROOF_DIR.mkdir(parents=True, exist_ok=True)

    checks: dict[str, dict[str, int | str]] = {}
    all_ok = True

    # 1. check_route_boundaries
    ec, _, _ = _run([sys.executable, "scripts/ci/check_route_boundaries.py"])
    checks["check_route_boundaries"] = {"exit_code": ec}
    if ec != 0:
        all_ok = False

    # 2. check_route_size
    ec, _, _ = _run([sys.executable, "scripts/ci/check_route_size.py"])
    checks["check_route_size"] = {"exit_code": ec}
    if ec != 0:
        all_ok = False

    # 3. check_service_boundaries
    ec, _, _ = _run([sys.executable, "scripts/ci/check_service_boundaries.py"])
    checks["check_service_boundaries"] = {"exit_code": ec}
    if ec != 0:
        all_ok = False

    # 4. api.utils imports in routes (must be 0)
    import re
    routes_dir = ROOT / "backend" / "api" / "routes"
    api_utils_count = 0
    if routes_dir.exists():
        pattern = re.compile(
            r"from\s+api\.utils|import\s+api\.utils|"
            r"from\s+backend\.api\.utils|import\s+backend\.api\.utils"
        )
        for py in routes_dir.rglob("*.py"):
            if "_archived" in str(py):
                continue
            text = py.read_text(encoding="utf-8")
            api_utils_count += len(pattern.findall(text))
    checks["quality_route_api_utils_imports"] = api_utils_count
    if api_utils_count > 0:
        all_ok = False

    # 5. quality.py line count
    quality_route = ROOT / "backend" / "api" / "routes" / "quality.py"
    quality_lines = len(quality_route.read_text().splitlines()) if quality_route.exists() else 0
    checks["quality_route_lines"] = quality_lines
    checks["quality_route_limit"] = 2000
    if quality_lines > 2000:
        all_ok = False

    # 6. quality_trends_service exists and route delegates
    trends_svc = ROOT / "backend" / "services" / "quality_trends_service.py"
    quality_content = quality_route.read_text() if quality_route.exists() else ""
    trends_delegated = (
        trends_svc.exists()
        and "quality_trends_service" in quality_content
        and "compute_quality_trends" in quality_content
    )
    checks["trends_delegated_to_service"] = trends_delegated
    if not trends_delegated:
        all_ok = False

    date_str = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    timestamp_str = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    git_commit, git_branch = _git_info()

    proof: dict = {
        "phase": "3",
        "date": date_str,
        "checks": checks,
        "command": "python scripts/ci/check_route_boundaries.py && check_route_size && check_service_boundaries",
        "exit_code": 0 if all_ok else 1,
        "timestamp": timestamp_str,
        "git_commit": git_commit,
        "git_branch": git_branch,
        "services_created": [
            "quality_trends_service",
            "quality_history_service",
            "quality_text_service",
            "quality_degradation_service",
            "quality_consistency_service",
            "quality_visualization_service",
            "quality_dashboard_service",
            "quality_benchmark_service",
        ],
        "guardrails_added": [
            "check_route_boundaries: api.utils import ban, inline regression ban",
            "check_route_size.py: 2000-line limit for routes",
        ],
        "note": "Phase 3.4: trends delegated to quality_trends_service; route thin; proof schema aligned.",
    }

    proof["evidence_fingerprint"] = compute_fingerprint(proof, "PROOF_PHASE_3")

    proof_path = PROOF_DIR / f"PROOF_PHASE_3_BULLETPROOF_{date_str}.json"
    proof_path.write_text(json.dumps(proof, indent=2), encoding="utf-8")
    print(f"Proof written to {proof_path}")

    if not all_ok:
        print("Some checks failed. Proof written with exit_code=1.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
