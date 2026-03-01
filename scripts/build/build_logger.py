#!/usr/bin/env python3
"""
Build Logger for Audit System.

Parses MSBuild/dotnet build output and logs warnings/errors to the audit system.

Usage:
    python scripts/build_logger.py --output build.log
    python scripts/build_logger.py --stdin < build_output.txt
    dotnet build 2>&1 | python scripts/build_logger.py --stdin

Integrates with:
- dotnet build (via --verbosity structured)
- gatec-publish-launch.ps1
- CI pipeline
"""

import argparse
import os
import re
import subprocess
import sys
from pathlib import Path

from _env_setup import PROJECT_ROOT

from app.core.audit import ContextEnricher, get_audit_logger

# Regex patterns for parsing MSBuild output
WARNING_PATTERN = re.compile(
    r"(?P<file>[^(]+)\((?P<line>\d+),(?P<col>\d+)\):\s*warning\s+(?P<code>\w+):\s*(?P<message>.+)",
    re.IGNORECASE,
)
ERROR_PATTERN = re.compile(
    r"(?P<file>[^(]+)\((?P<line>\d+),(?P<col>\d+)\):\s*error\s+(?P<code>\w+):\s*(?P<message>.+)",
    re.IGNORECASE,
)
SIMPLE_WARNING_PATTERN = re.compile(
    r"warning\s+(?P<code>\w+):\s*(?P<message>.+)",
    re.IGNORECASE,
)
SIMPLE_ERROR_PATTERN = re.compile(
    r"error\s+(?P<code>\w+):\s*(?P<message>.+)",
    re.IGNORECASE,
)


def parse_msbuild_output(output: str) -> tuple[list[dict], list[dict]]:
    """
    Extract warnings and errors from MSBuild output.

    Args:
        output: MSBuild output text

    Returns:
        Tuple of (warnings, errors) as lists of dicts
    """
    warnings = []
    errors = []

    for line in output.split("\n"):
        line = line.strip()
        if not line:
            continue

        # Try full pattern with file location
        match = WARNING_PATTERN.search(line)
        if match:
            warnings.append({
                "code": match.group("code"),
                "message": match.group("message").strip(),
                "file": match.group("file").strip(),
                "line": int(match.group("line")),
                "column": int(match.group("col")),
            })
            continue

        match = ERROR_PATTERN.search(line)
        if match:
            errors.append({
                "code": match.group("code"),
                "message": match.group("message").strip(),
                "file": match.group("file").strip(),
                "line": int(match.group("line")),
                "column": int(match.group("col")),
            })
            continue

        # Try simple pattern without file location
        match = SIMPLE_WARNING_PATTERN.search(line)
        if match and "warning" in line.lower():
            warnings.append({
                "code": match.group("code"),
                "message": match.group("message").strip(),
                "file": None,
                "line": None,
                "column": None,
            })
            continue

        match = SIMPLE_ERROR_PATTERN.search(line)
        if match and "error" in line.lower() and "0 Error" not in line:
            errors.append({
                "code": match.group("code"),
                "message": match.group("message").strip(),
                "file": None,
                "line": None,
                "column": None,
            })

    return warnings, errors


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


def log_build_results(
    warnings: list[dict],
    errors: list[dict],
    task_id: str | None = None,
    commit_hash: str | None = None,
) -> tuple[list[str], bool]:
    """
    Log build results to audit system.

    Args:
        warnings: List of warning dicts
        errors: List of error dicts
        task_id: Task ID from Quality Ledger
        commit_hash: Git commit hash

    Returns:
        Tuple of (entry_ids, success)
    """
    audit_logger = get_audit_logger()
    enricher = ContextEnricher()
    audit_logger.set_context_enricher(enricher)

    entry_ids = []

    # Log each warning
    for warning in warnings:
        entry_id = audit_logger.log_file_change(
            file_path=warning.get("file") or "build",
            operation="modify",
            role="System",
            task_id=task_id or "",
            summary=f"Build warning {warning['code']}: {warning['message'][:100]}",
            actor="system",
        )
        entry_ids.append(entry_id)

    # Log each error
    for error in errors:
        entry_id = audit_logger.log_file_change(
            file_path=error.get("file") or "build",
            operation="modify",
            role="System",
            task_id=task_id or "",
            summary=f"Build error {error['code']}: {error['message'][:100]}",
            actor="system",
        )
        entry_ids.append(entry_id)

    # Log overall build result
    warning_codes = [w["code"] for w in warnings]
    error_codes = [e["code"] for e in errors]

    entry_ids.extend(
        audit_logger.log_build_event(
            warnings=warning_codes,
            errors=error_codes,
            commit_hash=commit_hash,
            task_id=task_id,
        )
    )

    success = len(errors) == 0
    return entry_ids, success


def run_build(
    solution: str = "VoiceStudio.sln",
    configuration: str = "Debug",
    platform: str = "x64",
) -> tuple[str, int]:
    """
    Run dotnet build and capture output.

    Args:
        solution: Solution file path
        configuration: Build configuration
        platform: Build platform

    Returns:
        Tuple of (output, exit_code)
    """
    cmd = [
        "dotnet", "build",
        solution,
        "-c", configuration,
        f"-p:Platform={platform}",
        "--verbosity", "normal",
    ]

    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            cwd=PROJECT_ROOT,
        )
        return result.stdout + result.stderr, result.returncode
    except (subprocess.SubprocessError, FileNotFoundError) as e:
        return str(e), 1


def main():
    parser = argparse.ArgumentParser(
        description="Build Logger for Audit System",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument(
        "--output",
        type=Path,
        help="Path to build output file to parse",
    )
    parser.add_argument(
        "--stdin",
        action="store_true",
        help="Read build output from stdin",
    )
    parser.add_argument(
        "--run-build",
        action="store_true",
        help="Run dotnet build and parse output",
    )
    parser.add_argument(
        "--solution",
        default="VoiceStudio.sln",
        help="Solution file (default: VoiceStudio.sln)",
    )
    parser.add_argument(
        "--configuration",
        default="Debug",
        help="Build configuration (default: Debug)",
    )
    parser.add_argument(
        "--platform",
        default="x64",
        help="Build platform (default: x64)",
    )
    parser.add_argument(
        "--task",
        default=os.environ.get("TASK_ID", ""),
        help="Task ID from Quality Ledger",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output results as JSON",
    )

    args = parser.parse_args()

    build_output = ""
    exit_code = 0

    if args.run_build:
        print(f"Running build: {args.solution} ({args.configuration}|{args.platform})")
        build_output, exit_code = run_build(
            solution=args.solution,
            configuration=args.configuration,
            platform=args.platform,
        )
    elif args.output:
        if not args.output.exists():
            print(f"Error: File not found: {args.output}", file=sys.stderr)
            sys.exit(1)
        build_output = args.output.read_text()
    elif args.stdin or not sys.stdin.isatty():
        build_output = sys.stdin.read()
    else:
        print("Error: Provide --output, --stdin, or --run-build", file=sys.stderr)
        sys.exit(1)

    # Parse build output
    warnings, errors = parse_msbuild_output(build_output)

    print(f"Found {len(warnings)} warning(s), {len(errors)} error(s)")

    # Get commit hash
    commit_hash = get_current_commit()

    # Log results
    entry_ids, success = log_build_results(
        warnings=warnings,
        errors=errors,
        task_id=args.task,
        commit_hash=commit_hash,
    )

    if args.json:
        import json
        result = {
            "success": success,
            "warnings_count": len(warnings),
            "errors_count": len(errors),
            "warnings": warnings,
            "errors": errors,
            "entry_ids": entry_ids,
            "commit_hash": commit_hash,
        }
        print(json.dumps(result, indent=2))
    else:
        print(f"\nLogged {len(entry_ids)} audit entries")
        if warnings:
            print(f"\nWarnings ({len(warnings)}):")
            for w in warnings[:10]:  # Show first 10
                print(f"  {w['code']}: {w['message'][:80]}")
            if len(warnings) > 10:
                print(f"  ... and {len(warnings) - 10} more")
        if errors:
            print(f"\nErrors ({len(errors)}):")
            for e in errors:
                print(f"  {e['code']}: {e['message'][:80]}")

        print(f"\nBuild {'succeeded' if success else 'FAILED'}")

    # Return exit code from build or 0 if just parsing
    return exit_code if args.run_build else (0 if success else 1)


if __name__ == "__main__":
    sys.exit(main())
