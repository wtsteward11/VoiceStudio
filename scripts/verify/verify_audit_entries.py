#!/usr/bin/env python3
"""
Verify Audit Entries.

Checks that staged files have corresponding audit log entries.
Used by the pre-commit hook to enforce audit logging.

Usage:
    python scripts/verify_audit_entries.py file1.py file2.cs ...
"""

import json
import sys
from datetime import datetime, timedelta, timezone

from _env_setup import PROJECT_ROOT


def get_recent_logged_files(hours: int = 1) -> set[str]:
    """
    Get files that have been logged in the audit system recently.

    Args:
        hours: How many hours back to look

    Returns:
        Set of file paths that have audit entries
    """
    logged_files = set()
    audit_dir = PROJECT_ROOT / ".audit"

    if not audit_dir.exists():
        return logged_files

    # Get today's log
    today = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    log_path = audit_dir / f"log-{today}.jsonl"

    if not log_path.exists():
        return logged_files

    cutoff_time = datetime.now(timezone.utc) - timedelta(hours=hours)

    try:
        with open(log_path, encoding="utf-8") as f:
            for line in f:
                try:
                    entry = json.loads(line.strip())
                    # Check if entry is recent
                    timestamp_str = entry.get("timestamp", "")
                    if timestamp_str:
                        entry_time = datetime.fromisoformat(
                            timestamp_str.replace("Z", "+00:00")
                        )
                        if entry_time >= cutoff_time:
                            file_path = entry.get("file_path")
                            if file_path:
                                # Normalize path
                                logged_files.add(file_path.replace("\\", "/"))
                except (json.JSONDecodeError, ValueError):
                    continue
    # ALLOWED: bare except - Best effort file parsing, failure is acceptable
    except OSError:
        pass

    return logged_files


def normalize_path(file_path: str) -> str:
    """Normalize a file path for comparison."""
    return file_path.replace("\\", "/").strip()


def check_files(files: list[str]) -> bool:
    """
    Check if files have audit log entries.

    Args:
        files: List of file paths to check

    Returns:
        True if all files have entries (or are exempt)
    """
    # Files/patterns that don't need audit entries
    exempt_patterns = [
        ".gitignore",
        ".gitattributes",
        ".editorconfig",
        "LICENSE",
        "README.md",
        ".cursor/",
        ".github/",
        "docs/",
        ".audit/",
        "*.md",
    ]

    logged_files = get_recent_logged_files(hours=2)
    missing = []

    for file_path in files:
        normalized = normalize_path(file_path)

        # Check exemptions
        is_exempt = False
        for pattern in exempt_patterns:
            if pattern.endswith("/"):
                if normalized.startswith(pattern) or f"/{pattern}" in normalized:
                    is_exempt = True
                    break
            elif pattern.startswith("*"):
                if normalized.endswith(pattern[1:]):
                    is_exempt = True
                    break
            else:
                if normalized == pattern or normalized.endswith(f"/{pattern}"):
                    is_exempt = True
                    break

        if is_exempt:
            continue

        # Check if file is in logged files
        if normalized not in logged_files:
            # Also check with project root prefix removed
            for logged in logged_files:
                if logged.endswith(normalized) or normalized.endswith(logged):
                    break
            else:
                missing.append(file_path)

    if missing:
        print(f"\nFiles missing audit log entries ({len(missing)}):")
        for f in missing[:10]:
            print(f"  - {f}")
        if len(missing) > 10:
            print(f"  ... and {len(missing) - 10} more")
        return False

    return True


def main():
    if len(sys.argv) < 2:
        print("Usage: verify_audit_entries.py <file1> [file2] ...")
        sys.exit(1)

    files = sys.argv[1:]

    if check_files(files):
        print(f"All {len(files)} file(s) have audit entries or are exempt")
        sys.exit(0)
    else:
        sys.exit(1)


if __name__ == "__main__":
    main()
