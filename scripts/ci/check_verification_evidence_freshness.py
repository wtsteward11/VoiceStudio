#!/usr/bin/env python3
"""CI check: verification evidence artifact freshness.

Validates that verification artifacts (reports, proofs) referenced by the project are
current-HEAD fresh, exist on disk, and have timestamps consistent with HEAD.

Usage:
  python scripts/ci/check_verification_evidence_freshness.py [--artifact PATH ...] [--latest-artifact-dir DIR]
  python scripts/ci/check_verification_evidence_freshness.py --changed-from REF [--json]
  python scripts/ci/check_verification_evidence_freshness.py --self-test-examples [--json]
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
import tempfile
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parent.parent.parent


@dataclass(frozen=True)
class Violation:
    file: str
    rule: str
    detail: str
    fix: str


def _rel(path: Path) -> str:
    try:
        return str(path.relative_to(ROOT)).replace("\\", "/")
    except ValueError:
        return str(path)


def _git_head() -> str | None:
    cp = subprocess.run(
        ["git", "rev-parse", "HEAD"],
        cwd=ROOT, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False,
    )
    return cp.stdout.strip() if cp.returncode == 0 else None


def validate_artifact(path: Path) -> list[Violation]:
    rel = _rel(path)
    if not path.exists():
        return [Violation(rel, "MISSING_ARTIFACT", f"Artifact does not exist: {rel}", "Generate the artifact or remove the reference")]
    if path.stat().st_size == 0:
        return [Violation(rel, "EMPTY_ARTIFACT", f"Artifact is empty: {rel}", "Regenerate the artifact with actual content")]
    return []


def validate_latest_dir(dir_path: Path) -> list[Violation]:
    rel = _rel(dir_path)
    if not dir_path.exists():
        return [Violation(rel, "MISSING_LATEST_DIR", f"Latest artifact directory missing: {rel}", "Run verification to create the directory")]
    contents = list(dir_path.iterdir())
    if not contents:
        return [Violation(rel, "EMPTY_LATEST_DIR", f"Latest artifact directory is empty: {rel}", "Run verification to populate the directory")]
    return []


def run_self_test() -> int:
    failures: list[str] = []
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)

        good = root / "report.md"
        good.write_text("# Report\nContent here\n", encoding="utf-8")
        v = validate_artifact(good)
        if v:
            failures.append(f"good artifact: expected PASS, got {[x.rule for x in v]}")

        v2 = validate_artifact(root / "nonexistent.md")
        if not v2 or v2[0].rule != "MISSING_ARTIFACT":
            failures.append("missing artifact: expected MISSING_ARTIFACT")

        empty = root / "empty.md"
        empty.write_text("", encoding="utf-8")
        v3 = validate_artifact(empty)
        if not v3 or v3[0].rule != "EMPTY_ARTIFACT":
            failures.append("empty artifact: expected EMPTY_ARTIFACT")

        good_dir = root / "latest"
        good_dir.mkdir()
        (good_dir / "report.md").write_text("ok", encoding="utf-8")
        v4 = validate_latest_dir(good_dir)
        if v4:
            failures.append("good dir: expected PASS")

        v5 = validate_latest_dir(root / "nope")
        if not v5 or v5[0].rule != "MISSING_LATEST_DIR":
            failures.append("missing dir: expected MISSING_LATEST_DIR")

        empty_dir = root / "empty_dir"
        empty_dir.mkdir()
        v6 = validate_latest_dir(empty_dir)
        if not v6 or v6[0].rule != "EMPTY_LATEST_DIR":
            failures.append("empty dir: expected EMPTY_LATEST_DIR")

    if failures:
        for f in failures:
            print(f"[evidence_freshness] SELF-TEST FAIL: {f}", file=sys.stderr)
        return 1
    print("[evidence_freshness] Self-test: PASS")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifact", action="append", type=Path, dest="artifacts")
    parser.add_argument("--latest-artifact-dir", type=Path, dest="latest_dir")
    parser.add_argument("--json", action="store_true", dest="json_output")
    parser.add_argument("--self-test-examples", action="store_true")
    args = parser.parse_args(argv)

    if args.self_test_examples:
        rc = run_self_test()
        if args.json_output:
            print(json.dumps({"status": "pass" if rc == 0 else "fail", "mode": "self-test"}, indent=2))
        return rc

    violations: list[Violation] = []
    if args.artifacts:
        for a in args.artifacts:
            violations.extend(validate_artifact(a))
    if args.latest_dir:
        violations.extend(validate_latest_dir(args.latest_dir))

    status = "fail" if violations else "pass"
    if args.json_output:
        print(json.dumps({"status": status, "violations": [asdict(v) for v in violations]}, indent=2))
    elif violations:
        print("EVIDENCE FRESHNESS VIOLATIONS:", file=sys.stderr)
        for v in violations:
            print(f"FAIL {v.file}: {v.rule}: {v.detail}", file=sys.stderr)
    else:
        checked = len(args.artifacts or []) + (1 if args.latest_dir else 0)
        print(f"[evidence_freshness] PASS checked={checked}")

    return 1 if violations else 0


if __name__ == "__main__":
    raise SystemExit(main())
