#!/usr/bin/env python3
"""Audit generated-audio product paths for fallback/mock/stub drift."""
from __future__ import annotations

import argparse
import json
import re
import sys
import tempfile
from pathlib import Path
from typing import NamedTuple

ROOT = Path(__file__).resolve().parent.parent.parent

DEFAULT_PRODUCT_PATHS = [
    ROOT / "backend" / "services" / "synthesis_service.py",
    ROOT / "backend" / "api" / "routes" / "library.py",
    ROOT / "backend" / "api" / "routes" / "timeline.py",
]

SUSPICIOUS_PATTERNS = [
    ("SILENT_FALLBACK", re.compile(r"\bfallback\w*", re.IGNORECASE)),
    ("FAKE_SUCCESS", re.compile(r"\b(fake|empty|without)\b.*\bsuccess\b|\bsuccess\b.*\b(fake|empty|without)\b", re.IGNORECASE)),
    ("STUB_PRODUCTION_CODE", re.compile(r"\b(stub|mock)\w*", re.IGNORECASE)),
]

PATH_ALLOWLIST_PARTS = {"tests", "test", "docs", "artifacts", ".buildlogs"}
LINE_ALLOWLIST_RE = re.compile(
    r"blocker|non-claim|raise\s+HTTPException|explicit|reject|\bfail(?:ed|s|ure)?\b|"
    r"fallback_project_audio_id|mocked|test_mode|stub_gate",
    re.IGNORECASE,
)


class Violation(NamedTuple):
    file: str
    line: int
    rule: str
    detail: str
    text: str


def _rel(path: Path) -> str:
    try:
        return str(path.relative_to(ROOT)).replace("\\", "/")
    except ValueError:
        return str(path)


def _path_allowed(path: Path) -> bool:
    parts = {part.lower() for part in path.parts}
    return bool(parts & PATH_ALLOWLIST_PARTS)


def _line_allowed(text: str) -> bool:
    return LINE_ALLOWLIST_RE.search(text) is not None


def scan_file(path: Path) -> list[Violation]:
    if _path_allowed(path):
        return []
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except UnicodeDecodeError:
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError as exc:
        return [Violation(_rel(path), 0, "FILE_READ", str(exc), "")]

    violations: list[Violation] = []
    for line_no, line in enumerate(lines, start=1):
        stripped = line.strip()
        if not stripped or stripped.startswith("#") or _line_allowed(stripped):
            continue
        for rule, pattern in SUSPICIOUS_PATTERNS:
            if pattern.search(stripped):
                violations.append(
                    Violation(
                        _rel(path),
                        line_no,
                        rule,
                        "Suspicious fallback/mock/stub terminology in generated-audio product path",
                        stripped,
                    )
                )
                break
    return violations


def scan_paths(paths: list[Path]) -> list[Violation]:
    violations: list[Violation] = []
    for path in paths:
        if path.is_dir():
            for file_path in sorted(path.rglob("*.py")):
                violations.extend(scan_file(file_path))
        elif path.exists():
            violations.extend(scan_file(path))
        else:
            violations.append(Violation(_rel(path), 0, "FILE_READ", "Path does not exist", ""))
    return violations


def _payload(status: str, mode: str, violations: list[Violation]) -> dict:
    return {
        "status": status,
        "mode": mode,
        "violations": [v._asdict() for v in violations],
    }


def run_self_test() -> int:
    with tempfile.TemporaryDirectory() as raw:
        root = Path(raw)
        bad = root / "backend" / "api" / "routes" / "product.py"
        bad.parent.mkdir(parents=True)
        bad.write_text(
            "\n".join(
                [
                    "def route():",
                    "    if primary_failed: return fallback_engine()",
                    "    return {'success': True, 'message': 'empty success without audio'}",
                    "    return mock_audio_id",
                ]
            ),
            encoding="utf-8",
        )
        allowed = root / "tests" / "test_product.py"
        allowed.parent.mkdir(parents=True)
        allowed.write_text("def test_uses_mock(): return 'mock'\n", encoding="utf-8")
        violations = scan_paths([bad, allowed])
        rules = {v.rule for v in violations}
        ok = {"SILENT_FALLBACK", "FAKE_SUCCESS", "STUB_PRODUCTION_CODE"} <= rules
        print(f"[runtime_no_fallback_product_path] self-test rules={sorted(rules)}")
        return 0 if ok else 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--path", action="append", type=Path, dest="paths")
    parser.add_argument("--json", action="store_true", dest="json_output")
    parser.add_argument("--self-test-examples", action="store_true")
    args = parser.parse_args(argv)

    if args.self_test_examples:
        rc = run_self_test()
        if args.json_output:
            print(json.dumps({"status": "pass" if rc == 0 else "fail", "mode": "self-test"}, indent=2))
        return rc

    paths = args.paths or DEFAULT_PRODUCT_PATHS
    violations = scan_paths(paths)
    status = "fail" if violations else "pass"
    payload = _payload(status, "paths", violations)
    if args.json_output:
        print(json.dumps(payload, indent=2))
    elif violations:
        print("RUNTIME NO-FALLBACK PRODUCT PATH VIOLATIONS:", file=sys.stderr)
        for violation in violations:
            print(
                f"FAIL {violation.file}:{violation.line}: {violation.rule} {violation.text}",
                file=sys.stderr,
            )
    else:
        print(f"[runtime_no_fallback_product_path] PASS {len(paths)} path(s)")
    return 0 if not violations else 1


if __name__ == "__main__":
    raise SystemExit(main())
