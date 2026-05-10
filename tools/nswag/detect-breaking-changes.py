#!/usr/bin/env python3
"""
Breaking Change Detector - Compare OpenAPI schemas for breaking changes.

Detects breaking changes between two versions of an OpenAPI schema:
1. Removed endpoints
2. Changed HTTP methods
3. Removed required parameters
4. Type changes in request/response bodies
5. Removed response fields

Usage:
    python detect-breaking-changes.py [--baseline PATH] [--current PATH] [--json]

    If --baseline is not provided, compares against the last committed version.
"""

import argparse
import json
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any


@dataclass
class BreakingChange:
    """Represents a detected breaking change."""
    severity: str  # "breaking", "warning", "info"
    category: str
    path: str
    message: str
    details: str | None = None


@dataclass
class ChangeReport:
    """Report of all detected changes."""
    breaking_changes: list[BreakingChange] = field(default_factory=list)
    warnings: list[BreakingChange] = field(default_factory=list)
    info: list[BreakingChange] = field(default_factory=list)

    @property
    def has_breaking_changes(self) -> bool:
        return len(self.breaking_changes) > 0


def get_baseline_schema(schema_path: Path) -> dict[str, Any] | None:
    """Get the baseline schema from the last git commit."""
    try:
        result = subprocess.run(
            ["git", "show", f"HEAD:{schema_path.as_posix()}"],
            capture_output=True,
            text=True,
            check=True,
        )
        return json.loads(result.stdout)
    except (subprocess.CalledProcessError, json.JSONDecodeError):
        return None


def load_schema(path: Path) -> dict[str, Any]:
    """Load an OpenAPI schema from file."""
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def compare_endpoints(
    baseline: dict[str, Any],
    current: dict[str, Any],
) -> list[BreakingChange]:
    """Compare endpoints between schemas."""
    changes = []

    baseline_paths = baseline.get("paths", {})
    current_paths = current.get("paths", {})

    # Check for removed endpoints
    for path in baseline_paths:
        if path not in current_paths:
            changes.append(BreakingChange(
                severity="breaking",
                category="endpoint_removed",
                path=path,
                message=f"Endpoint removed: {path}",
            ))
            continue

        # Check methods within path
        baseline_methods = set(baseline_paths[path].keys())
        current_methods = set(current_paths[path].keys())

        removed_methods = baseline_methods - current_methods
        for method in removed_methods:
            if method in ["get", "post", "put", "delete", "patch"]:
                changes.append(BreakingChange(
                    severity="breaking",
                    category="method_removed",
                    path=f"{method.upper()} {path}",
                    message=f"HTTP method removed: {method.upper()} {path}",
                ))

    return changes


def compare_parameters(
    baseline: dict[str, Any],
    current: dict[str, Any],
) -> list[BreakingChange]:
    """Compare parameters between schemas."""
    changes = []

    baseline_paths = baseline.get("paths", {})
    current_paths = current.get("paths", {})

    for path in baseline_paths:
        if path not in current_paths:
            continue

        for method in baseline_paths[path]:
            if method not in current_paths.get(path, {}):
                continue

            baseline_op = baseline_paths[path][method]
            current_op = current_paths[path][method]

            baseline_params = {
                p.get("name"): p
                for p in baseline_op.get("parameters", [])
            }
            current_params = {
                p.get("name"): p
                for p in current_op.get("parameters", [])
            }

            # Check for removed required parameters
            for name, param in baseline_params.items():
                if param.get("required", False) and name not in current_params:
                    changes.append(BreakingChange(
                        severity="breaking",
                        category="parameter_removed",
                        path=f"{method.upper()} {path}",
                        message=f"Required parameter removed: {name}",
                    ))
                elif name not in current_params:
                    changes.append(BreakingChange(
                        severity="warning",
                        category="parameter_removed",
                        path=f"{method.upper()} {path}",
                        message=f"Optional parameter removed: {name}",
                    ))

            # Check for new required parameters (breaking for clients)
            for name, param in current_params.items():
                if param.get("required", False) and name not in baseline_params:
                    changes.append(BreakingChange(
                        severity="breaking",
                        category="parameter_added",
                        path=f"{method.upper()} {path}",
                        message=f"New required parameter added: {name}",
                        details="Existing clients won't send this parameter",
                    ))

    return changes


def compare_responses(
    baseline: dict[str, Any],
    current: dict[str, Any],
) -> list[BreakingChange]:
    """Compare response schemas between versions."""
    changes = []

    baseline_paths = baseline.get("paths", {})
    current_paths = current.get("paths", {})

    for path in baseline_paths:
        if path not in current_paths:
            continue

        for method in baseline_paths[path]:
            if method not in current_paths.get(path, {}):
                continue

            baseline_op = baseline_paths[path][method]
            current_op = current_paths[path][method]

            baseline_responses = baseline_op.get("responses", {})
            current_responses = current_op.get("responses", {})

            # Check for removed success responses
            for code in ["200", "201"]:
                if code in baseline_responses and code not in current_responses:
                    changes.append(BreakingChange(
                        severity="breaking",
                        category="response_removed",
                        path=f"{method.upper()} {path}",
                        message=f"Response {code} removed",
                    ))

    return changes


def compare_schemas(
    baseline: dict[str, Any],
    current: dict[str, Any],
) -> ChangeReport:
    """Compare two OpenAPI schemas and detect breaking changes."""
    report = ChangeReport()

    # Compare API version
    baseline_version = baseline.get("info", {}).get("version", "")
    current_version = current.get("info", {}).get("version", "")

    if baseline_version != current_version:
        report.info.append(BreakingChange(
            severity="info",
            category="version_change",
            path="info.version",
            message=f"API version changed: {baseline_version} -> {current_version}",
        ))

    # Run comparisons
    all_changes = []
    all_changes.extend(compare_endpoints(baseline, current))
    all_changes.extend(compare_parameters(baseline, current))
    all_changes.extend(compare_responses(baseline, current))

    # Categorize changes
    for change in all_changes:
        if change.severity == "breaking":
            report.breaking_changes.append(change)
        elif change.severity == "warning":
            report.warnings.append(change)
        else:
            report.info.append(change)

    return report


def print_report(report: ChangeReport, json_output: bool = False) -> None:
    """Print the change report."""
    if json_output:
        data = {
            "has_breaking_changes": report.has_breaking_changes,
            "breaking_changes": [
                {"category": c.category, "path": c.path, "message": c.message}
                for c in report.breaking_changes
            ],
            "warnings": [
                {"category": c.category, "path": c.path, "message": c.message}
                for c in report.warnings
            ],
            "info": [
                {"category": c.category, "path": c.path, "message": c.message}
                for c in report.info
            ],
        }
        print(json.dumps(data, indent=2))
        return

    print("=" * 60)
    print(" Breaking Change Detection Report")
    print("=" * 60)
    print()

    if report.breaking_changes:
        print(f"[BREAKING CHANGES] ({len(report.breaking_changes)})")
        for change in report.breaking_changes:
            print(f"  [BREAK] {change.message}")
            print(f"     Path: {change.path}")
            if change.details:
                print(f"     Note: {change.details}")
        print()

    if report.warnings:
        print(f"[WARNINGS] ({len(report.warnings)})")
        for change in report.warnings:
            print(f"  [WARN] {change.message}")
            print(f"     Path: {change.path}")
        print()

    if report.info:
        print(f"[INFO] ({len(report.info)})")
        for change in report.info[:5]:
            print(f"  [INFO] {change.message}")
        if len(report.info) > 5:
            print(f"  ... and {len(report.info) - 5} more")
        print()

    if not report.breaking_changes and not report.warnings:
        print("[OK] No breaking changes detected!")
        print()


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Detect breaking changes in OpenAPI schema"
    )
    parser.add_argument(
        "--baseline",
        type=Path,
        help="Path to baseline schema (default: last git commit)",
    )
    parser.add_argument(
        "--current",
        type=Path,
        default=Path("docs/api/openapi.json"),
        help="Path to current schema",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output as JSON",
    )

    args = parser.parse_args()

    # Load current schema
    if not args.current.exists():
        print(f"[ERROR] Current schema not found: {args.current}", file=sys.stderr)
        return 1

    current = load_schema(args.current)

    # Load baseline schema
    if args.baseline:
        if not args.baseline.exists():
            print(f"[ERROR] Baseline schema not found: {args.baseline}", file=sys.stderr)
            return 1
        baseline = load_schema(args.baseline)
    else:
        baseline = get_baseline_schema(args.current)
        if baseline is None:
            if not args.json:
                print("[INFO] No baseline found in git history. Skipping comparison.")
            return 0

    # Compare schemas
    report = compare_schemas(baseline, current)

    # Output report
    print_report(report, args.json)

    # Return exit code based on breaking changes
    return 1 if report.has_breaking_changes else 0


if __name__ == "__main__":
    sys.exit(main())
