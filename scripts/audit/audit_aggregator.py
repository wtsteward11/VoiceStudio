#!/usr/bin/env python3
"""
Audit Log Aggregator.

Post-processes daily audit logs into per-file and per-task views.
Generates human-readable Markdown summaries.

Usage:
    python scripts/audit_aggregator.py                    # Process today's logs
    python scripts/audit_aggregator.py --date 2026-02-03  # Specific date
    python scripts/audit_aggregator.py --all              # All logs
"""

import argparse
import json
import sys
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path

from _env_setup import PROJECT_ROOT

from app.core.audit.schema import AuditEntry


def get_audit_dir() -> Path:
    """Get the audit log directory."""
    return PROJECT_ROOT / ".audit"


def load_daily_log(date_str: str) -> list[AuditEntry]:
    """
    Load audit entries from a daily log file.

    Args:
        date_str: Date string in YYYY-MM-DD format

    Returns:
        List of AuditEntry objects
    """
    log_path = get_audit_dir() / f"log-{date_str}.jsonl"
    entries = []

    if not log_path.exists():
        return entries

    with open(log_path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                entries.append(AuditEntry.from_json(line))
            except json.JSONDecodeError:
                continue

    return entries


def aggregate_by_file(entries: list[AuditEntry], output_dir: Path):
    """
    Group entries by file and write per-file logs.

    Args:
        entries: List of audit entries
        output_dir: Directory to write per-file logs
    """
    files_dir = output_dir / "files"
    files_dir.mkdir(exist_ok=True)

    by_file: dict[str, list[AuditEntry]] = defaultdict(list)

    for entry in entries:
        if entry.file_path:
            by_file[entry.file_path].append(entry)

    for file_path, file_entries in by_file.items():
        # Sanitize filename
        safe_name = file_path.replace("/", "_").replace("\\", "_").replace(":", "_")
        if len(safe_name) > 100:
            safe_name = safe_name[-100:]

        log_path = files_dir / f"{safe_name}.log"

        with open(log_path, "a", encoding="utf-8") as f:
            for entry in file_entries:
                f.write(entry.to_json() + "\n")

    print(f"  Written per-file logs for {len(by_file)} files")


def aggregate_by_task(entries: list[AuditEntry], output_dir: Path):
    """
    Group entries by task and write per-task logs.

    Args:
        entries: List of audit entries
        output_dir: Directory to write per-task logs
    """
    tasks_dir = output_dir / "tasks"
    tasks_dir.mkdir(exist_ok=True)

    by_task: dict[str, list[AuditEntry]] = defaultdict(list)

    for entry in entries:
        if entry.task_id:
            by_task[entry.task_id].append(entry)

    for task_id, task_entries in by_task.items():
        task_path = tasks_dir / f"{task_id}.json"

        # Load existing entries
        existing = []
        if task_path.exists():
            try:
                with open(task_path, encoding="utf-8") as f:
                    existing = json.load(f)
            # Best effort - failure is acceptable here
            except json.JSONDecodeError:
                pass

        # Add new entries (avoid duplicates by entry_id)
        existing_ids = {e.get("entry_id") for e in existing}
        for entry in task_entries:
            if entry.entry_id not in existing_ids:
                existing.append(entry.to_dict())

        with open(task_path, "w", encoding="utf-8") as f:
            json.dump(existing, f, indent=2, default=str)

    print(f"  Updated per-task logs for {len(by_task)} tasks")


def generate_markdown_summary(date_str: str, entries: list[AuditEntry], output_dir: Path):
    """
    Generate human-readable Markdown summary.

    Args:
        date_str: Date string
        entries: List of audit entries
        output_dir: Directory to write summary
    """
    md_path = output_dir / f"log-{date_str}.md"

    # Calculate statistics
    stats = {
        "total": len(entries),
        "file_changes": 0,
        "build_events": 0,
        "exceptions": 0,
        "xaml_failures": 0,
        "by_actor": defaultdict(int),
        "by_role": defaultdict(int),
        "by_subsystem": defaultdict(int),
    }

    for entry in entries:
        if "file_" in entry.event_type:
            stats["file_changes"] += 1
        elif "build_" in entry.event_type:
            stats["build_events"] += 1
        elif "exception" in entry.event_type:
            stats["exceptions"] += 1
        elif "xaml" in entry.event_type:
            stats["xaml_failures"] += 1

        stats["by_actor"][entry.actor] += 1
        if entry.role:
            stats["by_role"][entry.role] += 1
        if entry.subsystem:
            stats["by_subsystem"][entry.subsystem] += 1

    # Generate Markdown
    md_content = f"""# Audit Log Summary - {date_str}

Generated: {datetime.now(timezone.utc).isoformat()}

## Statistics

| Metric | Count |
|--------|-------|
| Total Entries | {stats['total']} |
| File Changes | {stats['file_changes']} |
| Build Events | {stats['build_events']} |
| Exceptions | {stats['exceptions']} |
| XAML Failures | {stats['xaml_failures']} |

## By Actor

| Actor | Count |
|-------|-------|
"""
    for actor, count in sorted(stats["by_actor"].items(), key=lambda x: -x[1]):
        md_content += f"| {actor} | {count} |\n"

    md_content += """
## By Role

| Role | Count |
|------|-------|
"""
    for role, count in sorted(stats["by_role"].items(), key=lambda x: -x[1]):
        md_content += f"| {role} | {count} |\n"

    md_content += """
## By Subsystem

| Subsystem | Count |
|-----------|-------|
"""
    for subsystem, count in sorted(stats["by_subsystem"].items(), key=lambda x: -x[1])[:15]:
        md_content += f"| {subsystem} | {count} |\n"

    md_content += """
## Recent Entries

| Timestamp | Event | Role | Task | Summary |
|-----------|-------|------|------|---------|
"""
    for entry in entries[-50:]:  # Last 50 entries
        timestamp = entry.timestamp[:19].replace("T", " ") if entry.timestamp else ""
        role = entry.role or "System"
        task = entry.task_id or "-"
        summary = entry.summary[:60] if entry.summary else "-"
        md_content += f"| {timestamp} | {entry.event_type} | {role} | {task} | {summary} |\n"

    with open(md_path, "w", encoding="utf-8") as f:
        f.write(md_content)

    print(f"  Generated Markdown summary: {md_path.name}")


def update_index(entries: list[AuditEntry], output_dir: Path):
    """
    Update the quick lookup index.

    Args:
        entries: List of audit entries
        output_dir: Audit directory
    """
    index_path = output_dir / "index.json"

    # Load existing index
    index = {
        "last_updated": "",
        "files": {},
        "tasks": {},
        "recent_entries": [],
    }
    if index_path.exists():
        try:
            with open(index_path, encoding="utf-8") as f:
                index = json.load(f)
        # Best effort - failure is acceptable here
        except json.JSONDecodeError:
            pass

    index["last_updated"] = datetime.now(timezone.utc).isoformat()

    for entry in entries:
        # Update file index
        if entry.file_path:
            if entry.file_path not in index["files"]:
                index["files"][entry.file_path] = {"entry_count": 0}
            index["files"][entry.file_path]["last_modified"] = entry.timestamp
            index["files"][entry.file_path]["last_task"] = entry.task_id
            index["files"][entry.file_path]["entry_count"] += 1

        # Update task index
        if entry.task_id:
            if entry.task_id not in index["tasks"]:
                index["tasks"][entry.task_id] = {
                    "files_changed": 0,
                    "errors_fixed": 0,
                }
            index["tasks"][entry.task_id]["last_activity"] = entry.timestamp
            if entry.file_path:
                index["tasks"][entry.task_id]["files_changed"] += 1

    # Keep last 100 recent entries
    recent = index.get("recent_entries", [])
    for entry in entries[-100:]:
        recent.insert(0, {
            "id": entry.entry_id,
            "type": entry.event_type,
            "time": entry.timestamp,
        })
    index["recent_entries"] = recent[:100]

    with open(index_path, "w", encoding="utf-8") as f:
        json.dump(index, f, indent=2, default=str)

    print(f"  Updated index: {len(index['files'])} files, {len(index['tasks'])} tasks")


def process_date(date_str: str, output_dir: Path):
    """
    Process all aggregation for a specific date.

    Args:
        date_str: Date string in YYYY-MM-DD format
        output_dir: Output directory
    """
    print(f"\nProcessing {date_str}...")

    entries = load_daily_log(date_str)
    if not entries:
        print(f"  No entries found for {date_str}")
        return

    print(f"  Found {len(entries)} entries")

    aggregate_by_file(entries, output_dir)
    aggregate_by_task(entries, output_dir)
    generate_markdown_summary(date_str, entries, output_dir)
    update_index(entries, output_dir)


def get_all_log_dates(audit_dir: Path) -> list[str]:
    """Get all dates with log files."""
    dates = []
    for log_file in audit_dir.glob("log-*.jsonl"):
        date_str = log_file.stem.replace("log-", "")
        if len(date_str) == 10:  # YYYY-MM-DD
            dates.append(date_str)
    return sorted(dates)


def main():
    parser = argparse.ArgumentParser(
        description="Audit Log Aggregator",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument(
        "--date",
        help="Specific date to process (YYYY-MM-DD)",
    )
    parser.add_argument(
        "--all",
        action="store_true",
        help="Process all available dates",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        help="Override output directory",
    )

    args = parser.parse_args()

    audit_dir = args.output_dir or get_audit_dir()

    if not audit_dir.exists():
        print(f"Audit directory does not exist: {audit_dir}")
        audit_dir.mkdir(parents=True, exist_ok=True)
        print(f"Created: {audit_dir}")

    # Ensure subdirectories exist
    (audit_dir / "files").mkdir(exist_ok=True)
    (audit_dir / "tasks").mkdir(exist_ok=True)
    (audit_dir / "diffs").mkdir(exist_ok=True)

    if args.all:
        dates = get_all_log_dates(audit_dir)
        if not dates:
            print("No log files found")
            return 0

        print(f"Processing {len(dates)} date(s)...")
        for date_str in dates:
            process_date(date_str, audit_dir)
    else:
        date_str = args.date or datetime.now(timezone.utc).strftime("%Y-%m-%d")
        process_date(date_str, audit_dir)

    print("\nAggregation complete")
    return 0


if __name__ == "__main__":
    sys.exit(main())
