#!/usr/bin/env python3
"""
Crash Artifact Watcher.

Monitors the VoiceStudio crash directory for new artifacts and
cross-references them with recent audit log entries.

Usage:
    python scripts/crash_watcher.py              # Run once
    python scripts/crash_watcher.py --watch      # Continuous monitoring
    python scripts/crash_watcher.py --link-all   # Link all unlinked artifacts
"""

import argparse
import json
import os
import sys
import time
from pathlib import Path

from app.core.audit import AuditEntry, AuditLogger, get_audit_logger


def get_crash_directory() -> Path:
    """Get the VoiceStudio crash artifacts directory."""
    if sys.platform == "win32":
        appdata = os.environ.get("LOCALAPPDATA", "")
        if appdata:
            return Path(appdata) / "VoiceStudio" / "crashes"

    # Fallback for other platforms
    return Path.home() / ".voicestudio" / "crashes"


def get_crash_files(crash_dir: Path) -> list[Path]:
    """Get list of crash artifact files."""
    if not crash_dir.exists():
        return []

    files = []
    for ext in ["*.json", "*.log", "*.dmp", "*.txt"]:
        files.extend(crash_dir.glob(ext))

    return sorted(files, key=lambda f: f.stat().st_mtime, reverse=True)


def get_recent_audit_entry(audit_logger: AuditLogger) -> AuditEntry | None:
    """Get the most recent audit entry."""
    entries = audit_logger.get_recent_entries(1)
    return entries[0] if entries else None


def link_crash_to_audit(
    crash_path: Path,
    audit_logger: AuditLogger,
    entry_id: str | None = None,
) -> bool:
    """
    Link a crash artifact to an audit entry.

    Args:
        crash_path: Path to the crash artifact
        audit_logger: AuditLogger instance
        entry_id: Specific entry ID to link to (uses most recent if None)

    Returns:
        True if linked successfully
    """
    if entry_id is None:
        entry = get_recent_audit_entry(audit_logger)
        if entry is None:
            print(f"No audit entries found to link {crash_path.name}")
            return False
        entry_id = entry.entry_id

    # Link the artifact
    audit_logger.link_crash_artifact(entry_id, str(crash_path))

    # Also log the crash artifact as its own entry
    audit_logger.log_runtime_exception(
        exception=Exception(f"Crash artifact: {crash_path.name}"),
        context={
            "crash_path": str(crash_path),
            "linked_entry": entry_id,
            "subsystem": "Runtime.Crash",
        }
    )

    return True


def process_new_crashes(
    crash_dir: Path,
    audit_logger: AuditLogger,
    processed_file: Path,
) -> int:
    """
    Process new crash artifacts.

    Args:
        crash_dir: Directory containing crash files
        audit_logger: AuditLogger instance
        processed_file: File tracking already processed crashes

    Returns:
        Number of new crashes processed
    """
    # Load processed crashes
    processed = set()
    if processed_file.exists():
        try:
            with open(processed_file) as f:
                processed = set(json.load(f))
        # Best effort - failure is acceptable here
        except (OSError, json.JSONDecodeError):
            pass

    # Get crash files
    crash_files = get_crash_files(crash_dir)
    new_count = 0

    for crash_path in crash_files:
        crash_key = f"{crash_path.name}:{crash_path.stat().st_mtime}"

        if crash_key in processed:
            continue

        print(f"New crash artifact: {crash_path.name}")
        if link_crash_to_audit(crash_path, audit_logger):
            processed.add(crash_key)
            new_count += 1

    # Save processed list
    with open(processed_file, "w") as f:
        json.dump(list(processed), f)

    return new_count


def watch_crashes(crash_dir: Path, interval: int = 5):
    """
    Continuously watch for new crash artifacts.

    Args:
        crash_dir: Directory to watch
        interval: Check interval in seconds
    """
    audit_logger = get_audit_logger()
    processed_file = crash_dir / ".processed_crashes.json"

    print(f"Watching for crash artifacts in: {crash_dir}")
    print(f"Check interval: {interval}s")
    print("Press Ctrl+C to stop")

    try:
        while True:
            new_count = process_new_crashes(crash_dir, audit_logger, processed_file)
            if new_count > 0:
                print(f"Processed {new_count} new crash artifact(s)")
            time.sleep(interval)
    except KeyboardInterrupt:
        print("\nStopped watching")


def link_all_unlinked(crash_dir: Path):
    """Link all crash artifacts to their nearest audit entries."""
    audit_logger = get_audit_logger()
    crash_files = get_crash_files(crash_dir)

    print(f"Found {len(crash_files)} crash artifact(s)")

    linked_count = 0
    for crash_path in crash_files:
        if link_crash_to_audit(crash_path, audit_logger):
            print(f"  Linked: {crash_path.name}")
            linked_count += 1

    print(f"\nLinked {linked_count} crash artifact(s)")


def main():
    parser = argparse.ArgumentParser(
        description="Crash Artifact Watcher",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument(
        "--watch",
        action="store_true",
        help="Continuously watch for new crashes",
    )
    parser.add_argument(
        "--interval",
        type=int,
        default=5,
        help="Watch interval in seconds (default: 5)",
    )
    parser.add_argument(
        "--link-all",
        action="store_true",
        help="Link all existing crash artifacts",
    )
    parser.add_argument(
        "--crash-dir",
        type=Path,
        help="Override crash directory path",
    )

    args = parser.parse_args()

    crash_dir = args.crash_dir or get_crash_directory()

    if not crash_dir.exists():
        print(f"Crash directory does not exist: {crash_dir}")
        crash_dir.mkdir(parents=True, exist_ok=True)
        print(f"Created: {crash_dir}")

    if args.link_all:
        link_all_unlinked(crash_dir)
    elif args.watch:
        watch_crashes(crash_dir, args.interval)
    else:
        # Run once
        audit_logger = get_audit_logger()
        processed_file = crash_dir / ".processed_crashes.json"
        new_count = process_new_crashes(crash_dir, audit_logger, processed_file)
        print(f"Processed {new_count} new crash artifact(s)")

    return 0


if __name__ == "__main__":
    sys.exit(main())
