#!/usr/bin/env python3
"""
Verify proof artifacts for Skeptical Validator.

Checks .buildlogs/verification/last_run.json has expected keys and all_passed;
optionally validates audio proof_data.json (MOS >= threshold) and build exit codes.
Output: pass/fail per check. Use --json for CI. Exit 0 only if all checks pass.

Usage:
  python scripts/verify-proof-artifacts.py [--audio] [--build] [--json]
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def _project_root() -> Path:
    root = Path(__file__).resolve().parent.parent
    return root


def check_last_run(root: Path) -> tuple[bool, str]:
    """Check .buildlogs/verification/last_run.json has expected keys and all_passed."""
    path = root / ".buildlogs" / "verification" / "last_run.json"
    if not path.exists():
        return False, "last_run.json not found"
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except Exception as e:
        return False, f"last_run.json invalid: {e}"
    if "all_passed" not in data:
        return False, "last_run.json missing all_passed"
    if "checks" not in data:
        return False, "last_run.json missing checks"
    if not data.get("all_passed", False):
        failed = [c["name"] for c in data.get("checks", []) if not c.get("passed", True)]
        return False, f"verification failed: {failed}"
    return True, "ok"


def check_audio_proofs(root: Path, mos_min: float = 3.5) -> tuple[bool, str]:
    """Check latest proof_data.json in .buildlogs/proof_runs has MOS >= threshold."""
    proof_dir = root / ".buildlogs" / "proof_runs"
    if not proof_dir.exists():
        return True, "no proof_runs dir (skip)"
    runs = sorted(proof_dir.iterdir(), key=lambda p: p.stat().st_mtime, reverse=True)
    for run_dir in runs[:5]:
        if not run_dir.is_dir():
            continue
        path = run_dir / "proof_data.json"
        if not path.exists():
            continue
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except Exception:
            continue
        mos = data.get("mos_score") or data.get("mos") or 0.0
        if isinstance(mos, (int, float)) and mos >= mos_min:
            return True, f"ok (MOS {mos})"
        return False, f"MOS {mos} below {mos_min}"
    return True, "no proof_data.json found (skip)"


def check_build_exit(root: Path) -> tuple[bool, str]:
    """Check last_run.json includes build check and it passed."""
    path = root / ".buildlogs" / "verification" / "last_run.json"
    if not path.exists():
        return True, "last_run.json missing (skip)"
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except Exception:
        return True, "skip"
    for c in data.get("checks", []):
        if c.get("name") == "build_smoke":
            return bool(c.get("passed", False)), "build_smoke in last_run"
    return True, "no build_smoke in last_run (skip)"


def main() -> int:
    ap = argparse.ArgumentParser(description="Verify proof artifacts for Validator")
    ap.add_argument("--audio", action="store_true", help="Check audio proof MOS >= threshold")
    ap.add_argument("--build", action="store_true", help="Require build_smoke in last_run and passed")
    ap.add_argument("--json", action="store_true", help="Emit JSON for CI")
    ap.add_argument("--mos-min", type=float, default=3.5, help="Min MOS for audio proof (default 3.5)")
    args = ap.parse_args()

    root = _project_root()
    results = []

    passed, msg = check_last_run(root)
    results.append(("last_run", passed, msg))

    if args.audio:
        passed, msg = check_audio_proofs(root, mos_min=args.mos_min)
        results.append(("audio_proof", passed, msg))

    if args.build:
        passed, msg = check_build_exit(root)
        results.append(("build", passed, msg))

    all_passed = all(r[1] for r in results)

    if args.json:
        out = {
            "all_passed": all_passed,
            "checks": [{"name": n, "passed": p, "message": m} for n, p, m in results],
        }
        print(json.dumps(out, indent=2))
    else:
        for name, passed, msg in results:
            status = "PASS" if passed else "FAIL"
            print(f"  [{status}] {name}: {msg}")
        print()
        print(f"  Overall: {'PASS' if all_passed else 'FAIL'}")

    return 0 if all_passed else 1


if __name__ == "__main__":
    sys.exit(main())
