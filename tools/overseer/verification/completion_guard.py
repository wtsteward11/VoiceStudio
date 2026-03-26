"""
Completion Evidence Guard

Fail when completion markers appear in uncommitted changes.
This prevents marking plan/task items complete without committing proof.
"""
from __future__ import annotations

import argparse
import io
import json
import re
import subprocess
import sys
from collections.abc import Iterable
from dataclasses import asdict, dataclass
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[3]
MAX_UNTRACKED_BYTES = 500_000
TEXT_EXTENSIONS = {".md", ".markdown", ".yml", ".yaml", ".txt", ".json"}
GUARDED_PREFIXES = (
    ".cursor/STATE.md",
    ".cursor/plans/",
    "docs/tasks/",
    "docs/reports/verification/",
    "docs/reports/packaging/",
    "docs/governance/",
    "docs/design/",
)

# Require "status:" (label) for status lines — avoids false positives in governance tables.
# Omit broad "state ... complete" / "phase ... complete" — they fire on STATE.md / archive prose.
_COMPLETION_LINE_PATTERNS = [
    re.compile(r"\[[xX]\]"),
    re.compile(r"(?i)status\s*:\s*.*\bcomplete(d)?\b"),
    re.compile(r"(?i)status\s*:\s*.*\bdone\b"),
]

_MARKDOWN_CHECKLIST_DONE = re.compile(r"^[-*]\s+\[[xX]\]\s+")

# Ensure UTF-8 output on Windows console
if sys.platform == "win32" and hasattr(sys.stdout, "buffer"):
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8", errors="replace")


@dataclass(frozen=True)
class MarkerHit:
    path: str
    line: str
    source: str


def _run_git(args: list[str], root: Path) -> str | None:
    try:
        result = subprocess.run(
            ["git", *args],
            cwd=root,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
    except Exception:
        return None
    if result.returncode != 0:
        return None
    return result.stdout.strip() if result.stdout else ""


def _is_docs_design_path(rel_path: str | None) -> bool:
    if not rel_path:
        return False
    normalized = rel_path.replace("\\", "/")
    return normalized.startswith("docs/design/")


def _matches_completion(line: str, rel_path: str | None = None) -> bool:
    """True if line looks like an uncommitted task-closure marker in a guarded path."""
    s = line.strip()
    design = _is_docs_design_path(rel_path)
    if design and _MARKDOWN_CHECKLIST_DONE.match(s):
        return False
    if design and re.match(r"^\*\*Status:\*\*", s, re.IGNORECASE):
        return False
    return any(pattern.search(line) for pattern in _COMPLETION_LINE_PATTERNS)


def _is_guarded_path(path: str) -> bool:
    normalized = path.replace("\\", "/")
    if normalized == ".cursor/STATE.md":
        return True
    return any(normalized.startswith(prefix) for prefix in GUARDED_PREFIXES)


def _parse_diff(diff_text: str, source: str) -> list[MarkerHit]:
    hits: list[MarkerHit] = []
    current_file: str | None = None
    for raw in diff_text.splitlines():
        if raw.startswith("diff --git"):
            current_file = None
            continue
        if raw.startswith("+++ "):
            current_file = raw[6:].strip() if raw.startswith("+++ b/") else None
            continue
        if raw.startswith("--- "):
            continue
        if not raw.startswith("+") or raw.startswith("+++"):
            continue
        if not current_file or not _is_guarded_path(current_file):
            continue
        line = raw[1:]
        if _matches_completion(line, current_file):
            hits.append(MarkerHit(path=current_file, line=line.strip(), source=source))
    return hits


def _extract_untracked(status_lines: Iterable[str]) -> list[str]:
    untracked: list[str] = []
    for line in status_lines:
        if line.startswith("?? "):
            untracked.append(line[3:].strip())
    return untracked


def _is_in_code_fence(lines: list[str], line_no: int) -> bool:
    """Check if line is inside a markdown code fence (odd number of fences before it)."""
    fence_count = 0
    for line in lines[: line_no - 1]:
        if line.strip().startswith("```"):
            fence_count += 1
    return fence_count % 2 == 1


def _scan_untracked(paths: Iterable[str]) -> list[MarkerHit]:
    hits: list[MarkerHit] = []
    for rel_path in paths:
        rel_path = rel_path.replace("\\", "/")
        if not _is_guarded_path(rel_path):
            continue
        path = PROJECT_ROOT / rel_path
        if not path.exists():
            continue
        if path.suffix.lower() not in TEXT_EXTENSIONS:
            continue
        try:
            if path.stat().st_size > MAX_UNTRACKED_BYTES:
                continue
            text = path.read_text(encoding="utf-8", errors="ignore")
        except Exception:
            continue
        all_lines = text.splitlines()
        for line_no, line in enumerate(all_lines, start=1):
            if _is_in_code_fence(all_lines, line_no):
                continue
            if _matches_completion(line, rel_path):
                hits.append(
                    MarkerHit(
                        path=rel_path,
                        line=f"L{line_no}: {line.strip()}",
                        source="untracked",
                    )
                )
                if len(hits) >= 20:
                    return hits
    return hits


def run_guard() -> tuple[bool, dict]:
    status = _run_git(["status", "--porcelain"], PROJECT_ROOT)
    if status is None:
        return False, {
            "passed": False,
            "reason": "Unable to run git status. Completion guard requires git.",
        }
    status_lines = [line for line in status.splitlines() if line.strip()]
    if not status_lines:
        return True, {"passed": True, "dirty": False, "message": "Working tree clean."}

    diff_unstaged = _run_git(["diff", "--unified=0"], PROJECT_ROOT) or ""
    diff_staged = _run_git(["diff", "--cached", "--unified=0"], PROJECT_ROOT) or ""
    hits = _parse_diff(diff_unstaged, "unstaged")
    hits.extend(_parse_diff(diff_staged, "staged"))
    hits.extend(_scan_untracked(_extract_untracked(status_lines)))

    if hits:
        return False, {
            "passed": False,
            "dirty": True,
            "message": "Completion markers found in uncommitted changes.",
            "hits": [asdict(hit) for hit in hits[:20]],
        }

    return True, {
        "passed": True,
        "dirty": True,
        "message": "Working tree dirty but no completion markers detected.",
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Fail when completion markers appear in uncommitted changes."
    )
    parser.add_argument("--json", action="store_true", help="Emit JSON output")
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Report what would fail but exit 0",
    )
    parser.add_argument("--list-paths", action="store_true", help="Print GUARDED_PREFIXES and exit")
    parser.add_argument(
        "--list-patterns", action="store_true", help="Print completion line patterns and exit"
    )
    parser.add_argument("--verbose", action="store_true", help="Show all scanned files, not just hits")
    args = parser.parse_args()

    if args.list_paths:
        for p in GUARDED_PREFIXES:
            print(p)
        return 0
    if args.list_patterns:
        for p in _COMPLETION_LINE_PATTERNS:
            print(p.pattern)
        return 0

    passed, report = run_guard()
    if args.dry_run:
        if not passed:
            print("Completion guard would FAIL: uncommitted completion markers detected.")
            for hit in report.get("hits", [])[:10]:
                print(f"- {hit['source']}: {hit['path']}: {hit['line']}")
        else:
            print(f"Completion guard would PASS: {report.get('message')}")
        return 0
    if args.json:
        print(json.dumps(report, indent=2))
    else:
        if passed:
            print(f"Completion guard PASS: {report.get('message')}")
        else:
            print("Completion guard FAIL: uncommitted completion markers detected.")
            for hit in report.get("hits", [])[:10]:
                print(f"- {hit['source']}: {hit['path']}: {hit['line']}")
            if args.verbose and report.get("hits"):
                for hit in report.get("hits", [])[10:]:
                    print(f"- {hit['source']}: {hit['path']}: {hit['line']}")
            print("Commit completion/proof updates before marking complete.")
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
