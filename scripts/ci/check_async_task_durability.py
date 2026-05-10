#!/usr/bin/env python3
"""CI check: detect fire-and-forget async/background work lacking durable job tracking.

Scans production Python and C# code for patterns that launch background work without
creating a durable job record first. This catches asyncio.create_task, BackgroundTasks,
threading.Thread, subprocess.Popen, and C# Task.Run where no corresponding
canonical_job_lifecycle call or job_id creation is evident nearby.

Usage:
  python scripts/ci/check_async_task_durability.py [--json] [--path PATH ...]
  python scripts/ci/check_async_task_durability.py --self-test-examples [--json]

Allowlist:
  scripts/ci/async_task_durability_allowlist.json
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
ALLOWLIST_PATH = ROOT / "scripts" / "ci" / "async_task_durability_allowlist.json"

DEFAULT_SCAN_ROOTS = [
    ROOT / "backend",
    ROOT / "app" / "core",
    ROOT / "src" / "VoiceStudio.App",
    ROOT / "src" / "VoiceStudio.Core",
]

IGNORE_DIR_PARTS = {
    "tests", "test", "__pycache__", ".venv", "node_modules",
    "artifacts", "docs", ".git", "obj", "bin", "_archived",
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
    return data if isinstance(data, dict) else {"paths": [], "line_regex": []}


def _path_allowed(rel: str, allow: dict[str, Any]) -> bool:
    norm = rel.replace("\\", "/").lower()
    for p in (allow.get("paths") or []):
        if isinstance(p, str):
            needle = p.replace("\\", "/").strip().lower()
            if needle and (norm.startswith(needle) or norm.endswith(needle) or f"/{needle}" in f"/{norm}"):
                return True
    return False


def _line_allowed(text: str, allow: dict[str, Any]) -> bool:
    for pat in (allow.get("line_regex") or []):
        if isinstance(pat, str):
            try:
                if re.search(pat, text):
                    return True
            except re.error:
                continue
    return False


_RULES: list[tuple[str, re.Pattern[str]]] = [
    ("FIRE_AND_FORGET_ASYNC_TASK", re.compile(r"\basyncio\.create_task\b")),
    ("FASTAPI_BACKGROUND_TASK_UNDURABLE", re.compile(r"\bbackground_tasks\.add_task\b", re.IGNORECASE)),
    ("UNTRACKED_THREAD", re.compile(r"\bthreading\.Thread\s*\(")),
    ("UNTRACKED_PROCESS", re.compile(r"\bsubprocess\.Popen\s*\(")),
    ("UNTRACKED_CSHARP_TASK", re.compile(r"\b_\s*=\s*Task\.Run\b")),
    ("FIRE_AND_FORGET_DISCARD", re.compile(r"\b_\s*=\s*\w+Async\s*\(")),
]


def _should_scan_file(path: Path) -> bool:
    lower_parts = {part.lower() for part in path.parts}
    if lower_parts & IGNORE_DIR_PARTS:
        return False
    return path.suffix.lower() in {".py", ".cs"}


def scan_file(path: Path, allow: dict[str, Any]) -> list[Violation]:
    rel = _rel(path)
    if _path_allowed(rel, allow):
        return []
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except (UnicodeDecodeError, OSError):
        return []

    violations: list[Violation] = []
    in_triple = False
    for line_no, line in enumerate(lines, start=1):
        stripped = line.strip()
        if not stripped:
            continue
        if stripped.startswith("#") or stripped.startswith("//"):
            continue

        if in_triple:
            if stripped.endswith('"""') or stripped.endswith("'''"):
                in_triple = False
            continue
        if stripped.startswith('"""') or stripped.startswith("'''"):
            delim = stripped[:3]
            if stripped.count(delim) < 2 or len(stripped) <= 3:
                in_triple = True
            continue

        if _line_allowed(stripped, allow):
            continue
        for rule, pattern in _RULES:
            if pattern.search(stripped):
                violations.append(Violation(rel, line_no, rule, "Untracked background work", stripped))
                break
    return violations


def iter_files_under(roots: Iterable[Path]) -> list[Path]:
    files: list[Path] = []
    for root in roots:
        if not root.exists():
            continue
        for p in root.rglob("*"):
            if p.is_file() and _should_scan_file(p):
                files.append(p)
    return sorted({str(x.resolve()): x for x in files}.values(), key=lambda p: str(p))


def scan(roots: list[Path], extra_paths: list[Path] | None) -> list[Violation]:
    allow = _load_allowlist()
    paths = iter_files_under(roots)
    if extra_paths:
        for p in extra_paths:
            if p.is_file() and _should_scan_file(p):
                paths.append(p)
            elif p.is_dir():
                paths.extend(iter_files_under([p]))
    violations: list[Violation] = []
    for p in sorted({str(x.resolve()): x for x in paths}.values(), key=lambda p: str(p)):
        violations.extend(scan_file(p, allow))
    return violations


def run_self_test() -> int:
    allow: dict[str, Any] = {"paths": [], "line_regex": []}
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        bad_py = root / "backend" / "svc.py"
        bad_py.parent.mkdir(parents=True)
        bad_py.write_text(
            "\n".join([
                "import asyncio",
                "async def start():",
                "    asyncio.create_task(do_work())",
                "import threading",
                "def spawn():",
                "    threading.Thread(target=work).start()",
            ]),
            encoding="utf-8",
        )
        v = scan_file(bad_py, allow)
        rules = {x.rule for x in v}
        ok = {"FIRE_AND_FORGET_ASYNC_TASK", "UNTRACKED_THREAD"} <= rules
        print(f"[async_task_durability] self-test rules={sorted(rules)}")
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

    violations = scan(DEFAULT_SCAN_ROOTS, args.paths)
    status = "fail" if violations else "pass"
    if args.json_output:
        print(json.dumps({"status": status, "violations": [asdict(v) for v in violations]}, indent=2))
    elif violations:
        print("ASYNC TASK DURABILITY VIOLATIONS:", file=sys.stderr)
        for v in violations:
            print(f"FAIL {v.file}:{v.line}: {v.rule}: {v.text}", file=sys.stderr)
    else:
        print(f"[async_task_durability] PASS scanned_roots={len(DEFAULT_SCAN_ROOTS)}")
    return 1 if violations else 0


if __name__ == "__main__":
    raise SystemExit(main())
