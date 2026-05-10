#!/usr/bin/env python3
"""Global static scan for forbidden silent-fallback / fake-success language in production code.

This is intentionally conservative: it flags suspicious *phrasing* that often indicates silent
substitution, masking failures, or "best effort" success semantics.

It is NOT a semantic analyzer. Pair findings with code review.

Usage:
  python scripts/ci/check_runtime_no_fallback_global.py [--json] [--path PATH ...]
  python scripts/ci/check_runtime_no_fallback_global.py --self-test-examples [--json]

Allowlist:
  scripts/ci/runtime_no_fallback_allowlist.json
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import tempfile
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Iterable

ROOT = Path(__file__).resolve().parent.parent.parent
ALLOWLIST_PATH = ROOT / "scripts" / "ci" / "runtime_no_fallback_allowlist.json"

DEFAULT_SCAN_ROOTS = [
    ROOT / "backend",
    ROOT / "app" / "core" / "runtime",
    ROOT / "src" / "VoiceStudio.App",
    ROOT / "src" / "VoiceStudio.Core",
]

IGNORE_DIR_PARTS_STR = {
    "tests",
    "test",
    "__pycache__",
    ".venv",
    "node_modules",
    "artifacts",
    "docs",
    ".git",
    "obj",
    "bin",
}


@dataclass(frozen=True)
class Violation:
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


def _load_allowlist() -> dict[str, Any]:
    if not ALLOWLIST_PATH.exists():
        return {"paths": [], "line_regex": []}
    try:
        data = json.loads(ALLOWLIST_PATH.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {"paths": [], "line_regex": []}
    if not isinstance(data, dict):
        return {"paths": [], "line_regex": []}
    return data


def _path_allowed(rel: str, allow: dict[str, Any]) -> bool:
    paths = allow.get("paths") if isinstance(allow.get("paths"), list) else []
    norm = rel.replace("\\", "/").lower()
    for p in paths:
        if not isinstance(p, str):
            continue
        needle = p.replace("\\", "/").strip().lower()
        if needle and (norm.startswith(needle) or norm.endswith(needle) or f"/{needle}" in f"/{norm}"):
            return True
    return False


def _line_allowed(text: str, allow: dict[str, Any]) -> bool:
    raw_patterns = allow.get("line_regex") if isinstance(allow.get("line_regex"), list) else []
    for pat in raw_patterns:
        if not isinstance(pat, str):
            continue
        try:
            if re.search(pat, text):
                return True
        except re.error:
            continue
    return False


_RULES: list[tuple[str, re.Pattern[str]]] = [
    (
        "PRODUCTION_SILENT_FALLBACK",
        re.compile(
            r"\b(?:fallback_engine|fallback\s+mode|fallback\s+method|fallback\s+synthesis|fallback\s+tts)\b"
            r"|\b(?:using|trying|attempting)\b.*\bfallback\b"
            r'|"\s*fallback\s*"\s*:\s*true',
            re.IGNORECASE,
        ),
    ),
    (
        "PRODUCTION_FAKE_SUCCESS",
        re.compile(
            r"\b(fake|empty|placeholder)\b.*\bsuccess\b|\bsuccess\b.*\b(fake|empty|placeholder)\b"
            r"|\bSuccess\s*=\s*true\b",
            re.IGNORECASE,
        ),
    ),
    ("PRODUCTION_PLACEHOLDER_METRIC", re.compile(r"(?i)\bquality_metrics\s*=\s*\{\s*\}\s*$")),
    (
        "PRODUCTION_SIMULATION_MASQUERADES_REAL",
        re.compile(
            r"real_training_performed\s*[:=]\s*false|is_simulated\s*[:=]\s*false"
            r"|simulation_mode\s*[:=]\s*true.*\bcompleted\b|\bcompleted\b.*simulation_mode\s*[:=]\s*true",
            re.IGNORECASE,
        ),
    ),
    ("PRODUCTION_BEST_EFFORT_SUCCESS", re.compile(r"best[-_ ]effort.*success|success.*best[-_ ]effort", re.IGNORECASE)),
    ("UNCLASSIFIED_FALLBACK_TERM", re.compile(r"\b(degrade gracefully|graceful\s+degradation)\b", re.IGNORECASE)),
]


def _should_scan_file(path: Path) -> bool:
    lower_parts = {part.lower() for part in path.parts}
    if lower_parts & IGNORE_DIR_PARTS_STR:
        return False
    norm = str(path).replace("\\", "/").lower()
    if "/docs/" in norm:
        return False
    if "/_archived/" in norm:
        return False
    return path.suffix.lower() in {".py", ".cs"}


def scan_file(path: Path, allow: dict[str, Any]) -> list[Violation]:
    rel = _rel(path)
    if _path_allowed(rel, allow):
        return []
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except UnicodeDecodeError:
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError as exc:
        return [Violation(rel, 0, "FILE_READ", str(exc), "")]

    violations: list[Violation] = []
    in_triple_string = False
    triple_delim = ""
    for line_no, line in enumerate(lines, start=1):
        stripped = line.strip()
        if not stripped:
            continue

        # Skip full-line comments.
        if stripped.startswith("#") or stripped.startswith("//"):
            continue

        # Skip docstring lines (many historical docs mention "graceful degradation"/fallback
        # as architecture language, not runtime branching).
        if in_triple_string:
            if stripped.endswith(triple_delim) and stripped.count(triple_delim) >= 2:
                in_triple_string = False
                triple_delim = ""
            elif stripped.endswith(triple_delim):
                in_triple_string = False
                triple_delim = ""
            continue

        if stripped.startswith('"""') or stripped.startswith("'''"):
            delim = stripped[:3]
            if stripped.count(delim) >= 2 and len(stripped) > 3:
                # Single-line triple-quoted string/docstring
                continue
            in_triple_string = True
            triple_delim = delim
            continue

        if _line_allowed(stripped, allow):
            continue
        for rule, pattern in _RULES:
            if pattern.search(stripped):
                violations.append(
                    Violation(
                        rel,
                        line_no,
                        rule,
                        "Suspicious fallback / fake-success / simulation masquerade language",
                        stripped,
                    )
                )
                break
    return violations


def iter_files_under(roots: Iterable[Path]) -> list[Path]:
    files: list[Path] = []
    for root in roots:
        if not root.exists():
            continue
        for p in root.rglob("*"):
            if not p.is_file():
                continue
            if _should_scan_file(p):
                files.append(p)
    # Deterministic order
    return sorted({str(x.resolve()): x for x in files}.values(), key=lambda p: str(p))


def scan(roots: list[Path], extra_paths: list[Path] | None) -> list[Violation]:
    allow = _load_allowlist()
    violations: list[Violation] = []
    paths = iter_files_under(roots)
    if extra_paths:
        for p in extra_paths:
            if p.is_file() and _should_scan_file(p):
                paths.append(p)
            elif p.is_dir():
                paths.extend(iter_files_under([p]))
    for p in sorted({str(x.resolve()): x for x in paths}.values(), key=lambda p: str(p)):
        violations.extend(scan_file(p, allow))
    return violations


def _payload(status: str, violations: list[Violation]) -> dict[str, Any]:
    return {"status": status, "violations": [asdict(v) for v in violations]}


def run_self_test() -> int:
    allow = {"paths": [], "line_regex": []}
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        bad_py = root / "backend" / "svc.py"
        bad_py.parent.mkdir(parents=True)
        bad_py.write_text(
            "\n".join(
                [
                    "def synthesize():",
                    "    try:",
                    "        return primary()",
                    "    except Exception:",
                    "        return fallback_engine()",
                    "def metrics():",
                    "    return {'success': True, 'message': 'empty success without audio'}",
                    "def train():",
                    "    real_training_performed = false",
                    "def cfg():",
                    '    return {"fallback": True}',
                ]
            ),
            encoding="utf-8",
        )

        v = scan_file(bad_py, allow)
        rules = {x.rule for x in v}
        ok = {
            "PRODUCTION_SILENT_FALLBACK",
            "PRODUCTION_FAKE_SUCCESS",
            "PRODUCTION_SIMULATION_MASQUERADES_REAL",
        } <= rules
        print(f"[runtime_no_fallback_global] self-test rules={sorted(rules)}")
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

    roots = DEFAULT_SCAN_ROOTS
    violations = scan(roots, args.paths)
    status = "fail" if violations else "pass"
    payload = _payload(status, violations)
    if args.json_output:
        print(json.dumps(payload, indent=2))
    elif violations:
        print("RUNTIME NO-FALLBACK GLOBAL VIOLATIONS:", file=sys.stderr)
        for v in violations:
            print(f"FAIL {v.file}:{v.line}: {v.rule}: {v.text}", file=sys.stderr)
    else:
        print(f"[runtime_no_fallback_global] PASS scanned_roots={len(roots)}")
    return 1 if violations else 0


if __name__ == "__main__":
    raise SystemExit(main())
