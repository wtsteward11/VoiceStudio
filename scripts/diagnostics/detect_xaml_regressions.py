#!/usr/bin/env python3
"""
detect_xaml_regressions.py - Detect XAML quality regressions

This script scans XAML files for common quality regressions:
- Missing x:DataType on UserControl/DataTemplate
- Missing d:DataContext on Views
- Missing d:Visibility on loading/error overlays
- Missing FallbackValue on critical bindings
- Use of {Binding} where {x:Bind} should be used
- Missing namespace declarations

Usage:
    python scripts/detect_xaml_regressions.py [--path PATH] [--fix] [--json]

Exit codes:
    0: No regressions detected
    1: Regressions detected
    2: Error during execution
"""

import argparse
import json
import re
import sys
from pathlib import Path

# Regression detection patterns
REGRESSIONS = {
    "missing_x_datatype": {
        "pattern": r"<UserControl[^>]*(?!x:DataType=)",
        "message": "UserControl missing x:DataType attribute",
        "severity": "error",
        "applies_to": ["Views/Panels/*.xaml"]
    },
    "datatemplate_no_datatype": {
        "pattern": r"<DataTemplate[^>]*>(?![^<]*x:DataType)",
        "message": "DataTemplate missing x:DataType attribute",
        "severity": "warning",
        "applies_to": ["*.xaml"]
    },
    "legacy_binding_in_panel": {
        "pattern": r"\{Binding\s+[^}]+\}",
        "message": "Legacy {Binding} found (consider {x:Bind})",
        "severity": "warning",
        "applies_to": ["Views/Panels/*.xaml"],
        "exclude_patterns": ["ElementName=", "TemplatedParent"]
    },
    "missing_fallback_error": {
        "pattern": r'ErrorMessage[^}]*Mode=OneWay[^}]*\}(?![^}]*FallbackValue)',
        "message": "ErrorMessage binding missing FallbackValue",
        "severity": "warning",
        "applies_to": ["Views/Panels/*.xaml"]
    },
    "missing_fallback_status": {
        "pattern": r'StatusMessage[^}]*Mode=OneWay[^}]*\}(?![^}]*FallbackValue)',
        "message": "StatusMessage binding missing FallbackValue",
        "severity": "warning",
        "applies_to": ["Views/Panels/*.xaml"]
    },
    "loading_overlay_no_d_visibility": {
        "pattern": r'IsLoading[^>]*>(?![^<]*d:Visibility)',
        "message": "Loading overlay may need d:Visibility='Collapsed'",
        "severity": "info",
        "applies_to": ["Views/Panels/*.xaml"]
    }
}

# Required namespace declarations for Views
REQUIRED_NAMESPACES = [
    ("d", "http://schemas.microsoft.com/expression/blend/2008"),
    ("mc", "http://schemas.openxmlformats.org/markup-compatibility/2006"),
]


def find_project_root() -> Path:
    """Find the VoiceStudio project root."""
    script_dir = Path(__file__).parent
    for parent in [script_dir, script_dir.parent]:
        if (parent / "VoiceStudio.sln").exists():
            return parent
    raise RuntimeError("Could not find project root")


def matches_file_pattern(file_path: Path, patterns: list[str], base_path: Path) -> bool:
    """Check if file matches any of the glob patterns."""
    relative = file_path.relative_to(base_path)
    for pattern in patterns:
        # Convert pattern to regex
        regex_pattern = pattern.replace("**/", "(.*/)?").replace("*", "[^/]*")
        if re.match(regex_pattern, str(relative)):
            return True
    return False


def check_file_for_regressions(file_path: Path, base_path: Path) -> list[dict]:
    """Check a single XAML file for regressions."""
    issues = []

    try:
        content = file_path.read_text(encoding="utf-8")
    except Exception as e:
        return [{"file": str(file_path), "error": str(e)}]

    lines = content.splitlines()

    for reg_name, reg_config in REGRESSIONS.items():
        # Check if this regression applies to this file
        if not matches_file_pattern(file_path, reg_config.get("applies_to", ["*.xaml"]), base_path):
            continue

        # Check for exclusion patterns
        exclude_patterns = reg_config.get("exclude_patterns", [])

        # Simple pattern matching (not perfect, but catches most cases)
        pattern = reg_config["pattern"]

        for i, line in enumerate(lines, 1):
            # Check for exclusions first
            skip = False
            for exclude in exclude_patterns:
                if exclude in line:
                    skip = True
                    break

            if skip:
                continue

            # Check for pattern
            if re.search(pattern, line, re.IGNORECASE):
                issues.append({
                    "file": str(file_path.relative_to(base_path)),
                    "line": i,
                    "type": reg_name,
                    "message": reg_config["message"],
                    "severity": reg_config["severity"],
                    "content": line.strip()[:100]
                })

    # Check for missing namespace declarations in Views
    if "Views" in str(file_path) and "<UserControl" in content:
        for ns_prefix, _ns_uri in REQUIRED_NAMESPACES:
            if f'xmlns:{ns_prefix}=' not in content:
                issues.append({
                    "file": str(file_path.relative_to(base_path)),
                    "line": 1,
                    "type": f"missing_namespace_{ns_prefix}",
                    "message": f"Missing namespace declaration: xmlns:{ns_prefix}",
                    "severity": "warning"
                })

    return issues


def scan_directory(base_path: Path, target_dirs: list[str]) -> list[dict]:
    """Scan directories for XAML regressions."""
    all_issues = []

    for target_dir in target_dirs:
        dir_path = base_path / target_dir
        if not dir_path.exists():
            continue

        for xaml_file in dir_path.rglob("*.xaml"):
            issues = check_file_for_regressions(xaml_file, base_path)
            all_issues.extend(issues)

    return all_issues


def summarize_issues(issues: list[dict]) -> dict:
    """Generate summary statistics for issues."""
    by_severity = {"error": 0, "warning": 0, "info": 0}
    by_type = {}
    by_file = {}

    for issue in issues:
        severity = issue.get("severity", "warning")
        by_severity[severity] = by_severity.get(severity, 0) + 1

        issue_type = issue.get("type", "unknown")
        by_type[issue_type] = by_type.get(issue_type, 0) + 1

        file_name = issue.get("file", "unknown")
        by_file[file_name] = by_file.get(file_name, 0) + 1

    return {
        "total": len(issues),
        "by_severity": by_severity,
        "by_type": by_type,
        "files_affected": len(by_file)
    }


def print_report(issues: list[dict], output_format: str = "text") -> None:
    """Print regression report."""
    summary = summarize_issues(issues)

    if output_format == "json":
        print(json.dumps({
            "summary": summary,
            "issues": issues
        }, indent=2))
        return

    # Text format
    print("\n" + "=" * 60)
    print("XAML Regression Detection Report")
    print("=" * 60)

    if not issues:
        print("\n[OK] No regressions detected!")
        print("=" * 60)
        return

    print(f"\nTotal issues: {summary['total']}")
    print(f"  Errors: {summary['by_severity']['error']}")
    print(f"  Warnings: {summary['by_severity']['warning']}")
    print(f"  Info: {summary['by_severity']['info']}")
    print(f"  Files affected: {summary['files_affected']}")

    # Group by file
    issues_by_file: dict[str, list[dict]] = {}
    for issue in issues:
        file_name = issue.get("file", "unknown")
        if file_name not in issues_by_file:
            issues_by_file[file_name] = []
        issues_by_file[file_name].append(issue)

    print("\n--- Issues by File ---")
    for file_name, file_issues in sorted(issues_by_file.items()):
        print(f"\n{file_name}:")
        for issue in file_issues:
            severity = issue.get("severity", "warning")
            line = issue.get("line", "?")
            message = issue.get("message", "Unknown issue")

            severity_marker = {"error": "[E]", "warning": "[W]", "info": "[i]"}.get(severity, "[?]")
            print(f"  {severity_marker} Line {line}: {message}")

    print("\n" + "=" * 60)

    if summary["by_severity"]["error"] > 0:
        print("\n[FAIL] Errors detected - must fix before committing")
    elif summary["by_severity"]["warning"] > 0:
        print("\n[WARN] Warnings detected - consider fixing")
    else:
        print("\n[INFO] Only informational issues detected")


def main() -> int:
    parser = argparse.ArgumentParser(description="Detect XAML quality regressions")
    parser.add_argument(
        "--path",
        type=Path,
        help="Path to scan (default: src/VoiceStudio.App)"
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output as JSON"
    )
    parser.add_argument(
        "--strict",
        action="store_true",
        help="Treat warnings as errors"
    )
    args = parser.parse_args()

    try:
        project_root = find_project_root()
    except RuntimeError as e:
        print(f"Error: {e}", file=sys.stderr)
        return 2

    # Default scan directories
    target_dirs = ["src/VoiceStudio.App/Views", "src/VoiceStudio.App/Controls"]

    if args.path:
        target_dirs = [str(args.path)]

    print(f"Scanning for XAML regressions in: {project_root}")
    issues = scan_directory(project_root, target_dirs)

    output_format = "json" if args.json else "text"
    print_report(issues, output_format)

    # Determine exit code
    summary = summarize_issues(issues)

    if summary["by_severity"]["error"] > 0:
        return 1

    if args.strict and summary["by_severity"]["warning"] > 0:
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
