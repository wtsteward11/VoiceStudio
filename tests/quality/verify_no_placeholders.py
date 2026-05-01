"""
Placeholder Verification Script

Scans first-party source for high-signal placeholder markers using word-boundary
regexes (avoids false positives such as ``NamedTemporaryFile`` or ``TodoPanel``).
"""

from __future__ import annotations

import logging
import os
import re
import subprocess
import sys
from datetime import datetime
from pathlib import Path

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

project_root = Path(__file__).parent.parent.parent

# Case-sensitive bookmark tokens (avoids matching ``Todo`` feature names / ``todo-panel`` routes)
PLACEHOLDER_SCAN_PATTERNS: list[tuple[re.Pattern[str], str]] = [
    (re.compile(r"\bTODO\b"), "TODO"),
    (re.compile(r"\bFIXME\b"), "FIXME"),
    (re.compile(r"\bHACK\b"), "HACK"),
    (re.compile(r"\bXXX\b"), "XXX"),
    (re.compile(r"\bTBD\b"), "TBD"),
    (re.compile(r"\bTBA\b"), "TBA"),
    (re.compile(r"\bTBC\b"), "TBC"),
    (re.compile(r"\bWIP\b"), "WIP"),
]

# File patterns to check
INCLUDE_PATTERNS = [
    "**/*.py",
    "**/*.cs",
    "**/*.xaml",
]

# Directories to exclude
EXCLUDE_DIRS = [
    "__pycache__",
    ".git",
    "node_modules",
    ".venv",
    "venv",
    "env",
    "build",
    "dist",
    "*.egg-info",
    ".pytest_cache",
    ".mypy_cache",
    "tests",  # Exclude test files
    "test_data",  # Exclude test data
    "docs",  # Canonical docs use words like NOTE/VERIFY; not code placeholders
    "installer",  # Installer scripts and bundled strings
    "runtime/external",  # Vendored trees — not VoiceStudio-authored placeholders
    ".cursor",  # Editor/agent artifacts
    "artifacts",
    ".buildlogs",
    ".vscode",
    "_archived",
]

# Files to exclude
EXCLUDE_FILES = [
    "test_*.py",
    "*_test.py",
    "conftest.py",
    "verify_*.py",  # Exclude verification scripts themselves
    "calculate_*.py",  # Exclude calculation scripts
    "run_*.py",  # Exclude test runners
    "README*.md",  # Exclude README files
    "*.iss",  # Inno Setup scripts
    "*.wxs",  # WiX scripts
]


def should_check_file(file_path: Path) -> bool:
    """Determine if file should be checked."""
    for exclude_dir in EXCLUDE_DIRS:
        if exclude_dir in str(file_path):
            return False

    for exclude_pattern in EXCLUDE_FILES:
        if file_path.match(exclude_pattern):
            return False

    return any(file_path.match(include_pattern) for include_pattern in INCLUDE_PATTERNS)


def check_file_for_violations(file_path: Path) -> list[tuple[int, str, str]]:
    """Check file for forbidden terms and return violations."""
    violations: list[tuple[int, str, str]] = []

    try:
        with open(file_path, encoding="utf-8", errors="ignore") as f:
            lines = f.readlines()
    except OSError as exc:
        logger.warning("Could not read %s: %s", file_path, exc)
        return violations

    for line_num, line in enumerate(lines, 1):
        line_stripped = line.strip()
        if not line_stripped:
            continue
        for pattern, label in PLACEHOLDER_SCAN_PATTERNS:
            if pattern.search(line):
                violations.append((line_num, label, line_stripped[:120]))

    return violations


def _pull_request_changed_files() -> list[Path] | None:
    """
    Return paths changed on this PR relative to the merge base with the base branch.

    Requires a non-shallow checkout (fetch-depth: 0) so ``origin/$GITHUB_BASE_REF`` exists.
    """
    if os.environ.get("GITHUB_ACTIONS", "").lower() != "true":
        return None
    if os.environ.get("GITHUB_EVENT_NAME", "") != "pull_request":
        return None
    base = os.environ.get("GITHUB_BASE_REF", "").strip()
    if not base:
        return None

    upstream = f"origin/{base}"
    diff = subprocess.run(
        ["git", "diff", "--name-only", f"{upstream}...HEAD"],
        cwd=str(project_root),
        capture_output=True,
        text=True,
        check=False,
    )
    if diff.returncode != 0:
        logger.error(
            "git diff for placeholder scan failed (ensure checkout fetch-depth: 0). stderr: %s",
            diff.stderr.strip(),
        )
        sys.exit(2)

    paths: list[Path] = []
    for rel in diff.stdout.splitlines():
        rel = rel.strip().replace("\\", "/")
        if not rel:
            continue
        candidate = (project_root / rel).resolve()
        try:
            candidate.relative_to(project_root.resolve())
        except ValueError:
            continue
        if candidate.is_file() and should_check_file(candidate):
            paths.append(candidate)
    logger.info("PR-scoped placeholder scan: %s candidate files", len(paths))
    return paths


def scan_directory(directory: Path) -> dict[str, list[tuple[int, str, str]]]:
    """Scan directory for placeholder violations."""
    violations_by_file: dict[str, list[tuple[int, str, str]]] = {}

    pr_files = _pull_request_changed_files()
    if pr_files is not None:
        for file_path in pr_files:
            violations = check_file_for_violations(file_path)
            if violations:
                violations_by_file[str(file_path.relative_to(project_root))] = violations
        return violations_by_file

    for file_path in directory.rglob("*"):
        if file_path.is_file() and should_check_file(file_path):
            violations = check_file_for_violations(file_path)
            if violations:
                violations_by_file[str(file_path.relative_to(project_root))] = violations

    return violations_by_file


def generate_report(violations_by_file: dict[str, list[tuple[int, str, str]]]) -> str:
    """Generate violation report."""
    report_lines = [
        "=" * 80,
        "PLACEHOLDER VERIFICATION REPORT",
        "=" * 80,
        f"Generated: {datetime.now().isoformat()}",
        f"Total files with violations: {len(violations_by_file)}",
        "",
    ]

    total_violations = sum(len(v) for v in violations_by_file.values())
    report_lines.append(f"Total violations found: {total_violations}")
    report_lines.append("")

    for file_path, violations in sorted(violations_by_file.items()):
        report_lines.append(f"\nFile: {file_path}")
        report_lines.append(f"  Violations: {len(violations)}")
        report_lines.append("")

        for line_num, term, line_content in violations[:10]:
            report_lines.append(f"  Line {line_num}: Found '{term}'")
            report_lines.append(f"    {line_content}")

        if len(violations) > 10:
            report_lines.append(f"  ... and {len(violations) - 10} more violations")

    report_lines.append("")
    report_lines.append("=" * 80)

    return "\n".join(report_lines)


def main() -> int:
    """Main function."""
    logger.info("Starting placeholder verification scan...")
    logger.info("Project root: %s", project_root)

    violations_by_file = scan_directory(project_root)

    if violations_by_file:
        report = generate_report(violations_by_file)

        report_file = project_root / "placeholder_verification_report.txt"
        with open(report_file, "w", encoding="utf-8") as f:
            f.write(report)

        logger.error(
            "Found %s files with violations - full report written to %s",
            len(violations_by_file),
            report_file,
        )
        return 1

    logger.info("No placeholder violations found!")
    return 0


if __name__ == "__main__":
    sys.exit(main())
