#!/usr/bin/env python3
"""
XAML Audit Logger.

Parses XAML compiler output and logs failures to the audit system.
Called by xaml-compiler-wrapper.cmd after XAML compilation.

Usage:
    python scripts/xaml_audit_log.py --input input.json --exit-code 1
    python scripts/xaml_audit_log.py --file path/to/file.xaml --error "Binding error"
"""

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

from _env_setup import PROJECT_ROOT

from app.core.audit import ContextEnricher, get_audit_logger


def get_current_commit() -> str | None:
    """Get current git commit hash."""
    try:
        result = subprocess.run(
            ["git", "rev-parse", "--short", "HEAD"],
            capture_output=True,
            text=True,
            cwd=PROJECT_ROOT,
            timeout=5,
        )
        if result.returncode == 0:
            return result.stdout.strip()
    # Best effort - failure is acceptable here
    except (subprocess.SubprocessError, FileNotFoundError):
        pass
    return None


def parse_input_json(input_path: Path) -> list[str]:
    """
    Parse XAML compiler input.json to extract file list.

    Args:
        input_path: Path to input.json

    Returns:
        List of XAML file paths
    """
    files = []
    if not input_path.exists():
        return files

    try:
        with open(input_path, encoding="utf-8") as f:
            data = json.load(f)

        # Extract source files from input.json structure
        if isinstance(data, dict):
            # Try common keys
            for key in ["SourceFiles", "CompileFiles", "Items", "Files"]:
                if key in data:
                    items = data[key]
                    if isinstance(items, list):
                        for item in items:
                            if isinstance(item, str):
                                files.append(item)
                            elif isinstance(item, dict):
                                for path_key in ["Path", "Include", "FullPath"]:
                                    if path_key in item:
                                        files.append(item[path_key])
                                        break
    # Best effort - failure is acceptable here
    except (OSError, json.JSONDecodeError):
        pass

    return files


def log_xaml_failure(
    file_path: str,
    error_type: str,
    message: str,
    commit_hash: str | None = None,
    task_id: str | None = None,
) -> str:
    """
    Log XAML failure to audit system.

    Args:
        file_path: Path to the XAML file
        error_type: Type of error (compile, binding)
        message: Error message
        commit_hash: Git commit hash
        task_id: Task ID from Quality Ledger

    Returns:
        Entry ID
    """
    audit_logger = get_audit_logger()
    enricher = ContextEnricher()
    audit_logger.set_context_enricher(enricher)

    return audit_logger.log_xaml_failure(
        file_path=file_path,
        error_type=error_type,
        message=message,
        commit_hash=commit_hash,
    )


def log_xaml_compilation_result(
    exit_code: int,
    input_files: list[str],
    commit_hash: str | None = None,
    task_id: str | None = None,
) -> list[str]:
    """
    Log XAML compilation result.

    Args:
        exit_code: XAML compiler exit code
        input_files: List of input XAML files
        commit_hash: Git commit hash
        task_id: Task ID

    Returns:
        List of entry IDs
    """
    entry_ids = []

    if exit_code == 0:
        # Success - no need to log individual files
        return entry_ids

    # Failure - log each file as potentially problematic
    # In a real scenario, we would identify the specific failing file
    for file_path in input_files[:10]:  # Limit to first 10
        entry_id = log_xaml_failure(
            file_path=file_path,
            error_type="compile",
            message=f"XAML compilation failed (exit code {exit_code})",
            commit_hash=commit_hash,
            task_id=task_id,
        )
        entry_ids.append(entry_id)

    return entry_ids


def main():
    parser = argparse.ArgumentParser(
        description="XAML Audit Logger",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument(
        "--input",
        type=Path,
        help="Path to XAML compiler input.json",
    )
    parser.add_argument(
        "--exit-code",
        type=int,
        default=1,
        help="XAML compiler exit code",
    )
    parser.add_argument(
        "--file",
        help="Specific XAML file that failed",
    )
    parser.add_argument(
        "--error",
        default="XAML compilation failure",
        help="Error message",
    )
    parser.add_argument(
        "--error-type",
        default="compile",
        choices=["compile", "binding"],
        help="Type of XAML error",
    )
    parser.add_argument(
        "--task",
        default=os.environ.get("TASK_ID", ""),
        help="Task ID from Quality Ledger",
    )

    args = parser.parse_args()

    commit_hash = get_current_commit()
    entry_ids = []

    if args.file:
        # Log specific file failure
        entry_id = log_xaml_failure(
            file_path=args.file,
            error_type=args.error_type,
            message=args.error,
            commit_hash=commit_hash,
            task_id=args.task,
        )
        entry_ids.append(entry_id)
        print(f"Logged XAML failure: {args.file} (ID: {entry_id})")

    elif args.input:
        # Parse input.json and log failures
        input_files = parse_input_json(args.input)
        if not input_files:
            print(f"No files found in {args.input}")
        else:
            entry_ids = log_xaml_compilation_result(
                exit_code=args.exit_code,
                input_files=input_files,
                commit_hash=commit_hash,
                task_id=args.task,
            )
            print(f"Logged {len(entry_ids)} XAML failure entries")

    else:
        print("Error: Provide --input or --file", file=sys.stderr)
        sys.exit(1)

    if entry_ids:
        print(f"Entry IDs: {', '.join(entry_ids)}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
