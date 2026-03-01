#!/usr/bin/env python3
"""
VoiceStudio TODO/Stub Pattern Auditor

Scans Views/Panels for TODO/stub patterns and generates a structured report.
This helps track placeholder code, incomplete implementations, and technical debt.

Usage:
    python scripts/audit_todo_patterns.py
    python scripts/audit_todo_patterns.py --output .buildlogs/todo_audit.md
    python scripts/audit_todo_patterns.py --json

Exit codes:
    0: Success (report generated)
    1: Critical patterns found (Phase 0 or NotImplementedException)
"""

import argparse
import json
import re
import sys
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path


@dataclass
class PatternMatch:
    """Represents a found pattern in a file."""
    file: str
    line: int
    category: str
    severity: str
    content: str

    def to_dict(self) -> dict:
        return {
            "file": self.file,
            "line": self.line,
            "category": self.category,
            "severity": self.severity,
            "content": self.content
        }


@dataclass
class AuditReport:
    """Audit report summary."""
    timestamp: str = field(default_factory=lambda: datetime.now().isoformat())
    total_issues: int = 0
    critical_count: int = 0
    high_count: int = 0
    medium_count: int = 0
    low_count: int = 0
    files_scanned: int = 0
    issues: list[PatternMatch] = field(default_factory=list)

    def to_dict(self) -> dict:
        return {
            "timestamp": self.timestamp,
            "total_issues": self.total_issues,
            "critical_count": self.critical_count,
            "high_count": self.high_count,
            "medium_count": self.medium_count,
            "low_count": self.low_count,
            "files_scanned": self.files_scanned,
            "issues": [i.to_dict() for i in self.issues]
        }


# Pattern definitions: (regex, category, severity)
PATTERNS = [
    # Critical - These should block release
    (r'NotImplementedException|NotImplementedError', 'Not Implemented', 'critical'),
    (r'Phase\s*0', 'Phase 0 Placeholder', 'critical'),

    # High - Should be addressed before release
    (r'Temporarily\s+disabled|Temporarily\s+simplified', 'Temporarily Disabled', 'high'),
    (r'throw\s+new\s+Exception\s*\(\s*["\']TODO', 'TODO Exception', 'high'),

    # Medium - Technical debt to track
    (r'will be implemented|not yet implemented|to be implemented', 'Pending Implementation', 'medium'),
    (r'TODO\s*[:\-]?\s*\w', 'TODO Comment', 'medium'),
    (r'FIXME\s*[:\-]?\s*\w', 'FIXME Comment', 'medium'),
    (r'HACK\s*[:\-]?\s*\w', 'HACK Comment', 'medium'),

    # Low - Informational
    (r'Coming\s+Soon', 'Coming Soon Placeholder', 'low'),
    (r'Under\s+Development', 'Under Development', 'low'),
]


def get_project_root() -> Path:
    """Get the project root directory."""
    return Path(__file__).parent.parent.resolve()


def scan_file(file_path: Path, project_root: Path) -> list[PatternMatch]:
    """Scan a file for patterns and return matches."""
    issues = []

    try:
        content = file_path.read_text(encoding='utf-8', errors='replace')
    except Exception as e:
        print(f"Warning: Could not read {file_path}: {e}", file=sys.stderr)
        return issues

    relative_path = str(file_path.relative_to(project_root))

    for line_num, line in enumerate(content.splitlines(), 1):
        for pattern, category, severity in PATTERNS:
            if re.search(pattern, line, re.IGNORECASE):
                # Clean up the content for display
                content_preview = line.strip()[:120]
                if len(line.strip()) > 120:
                    content_preview += "..."

                issues.append(PatternMatch(
                    file=relative_path,
                    line=line_num,
                    category=category,
                    severity=severity,
                    content=content_preview
                ))
                break  # Only match one pattern per line

    return issues


def scan_directory(directory: Path, project_root: Path, extensions: tuple[str, ...] = ('.cs', '.xaml')) -> AuditReport:
    """Scan a directory for patterns."""
    report = AuditReport()

    for ext in extensions:
        for file_path in directory.rglob(f'*{ext}'):
            report.files_scanned += 1
            issues = scan_file(file_path, project_root)
            report.issues.extend(issues)

    # Count by severity
    for issue in report.issues:
        if issue.severity == 'critical':
            report.critical_count += 1
        elif issue.severity == 'high':
            report.high_count += 1
        elif issue.severity == 'medium':
            report.medium_count += 1
        else:
            report.low_count += 1

    report.total_issues = len(report.issues)
    return report


def generate_markdown_report(report: AuditReport) -> str:
    """Generate a markdown report from the audit results."""
    lines = [
        "# TODO/Stub Pattern Audit Report",
        "",
        f"**Generated:** {report.timestamp}",
        f"**Files Scanned:** {report.files_scanned}",
        f"**Total Issues:** {report.total_issues}",
        "",
        "## Summary",
        "",
        "| Severity | Count |",
        "|----------|-------|",
        f"| Critical | {report.critical_count} |",
        f"| High | {report.high_count} |",
        f"| Medium | {report.medium_count} |",
        f"| Low | {report.low_count} |",
        "",
    ]

    # Group issues by category
    by_category: dict[str, list[PatternMatch]] = {}
    for issue in report.issues:
        if issue.category not in by_category:
            by_category[issue.category] = []
        by_category[issue.category].append(issue)

    # Sort categories by severity (critical first)
    severity_order = {'critical': 0, 'high': 1, 'medium': 2, 'low': 3}
    sorted_categories = sorted(
        by_category.items(),
        key=lambda x: (severity_order.get(x[1][0].severity, 99), x[0])
    )

    for category, issues in sorted_categories:
        severity = issues[0].severity.upper()
        lines.append(f"## {category} ({len(issues)}) [{severity}]")
        lines.append("")

        for issue in sorted(issues, key=lambda x: (x.file, x.line)):
            lines.append(f"- `{issue.file}:{issue.line}` - {issue.content}")

        lines.append("")

    # Recommendations
    lines.extend([
        "## Recommendations",
        "",
    ])

    if report.critical_count > 0:
        lines.append("- **CRITICAL:** Resolve all Phase 0 placeholders and NotImplementedException before release")
    if report.high_count > 0:
        lines.append("- **HIGH:** Address temporarily disabled features or document permanent removal in an ADR")
    if report.medium_count > 0:
        lines.append("- **MEDIUM:** Review TODO/FIXME comments and create tracking issues for significant items")
    if report.low_count > 0:
        lines.append("- **LOW:** Informational placeholders can be addressed as time permits")

    lines.append("")
    lines.append("---")
    lines.append("*Generated by audit_todo_patterns.py*")

    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Audit VoiceStudio codebase for TODO/stub patterns"
    )
    parser.add_argument(
        "--output", "-o",
        type=str,
        default=None,
        help="Output file path (default: print to stdout)"
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output as JSON instead of markdown"
    )
    parser.add_argument(
        "--directory", "-d",
        type=str,
        default=None,
        help="Directory to scan (default: src/VoiceStudio.App/Views/Panels)"
    )
    parser.add_argument(
        "--fail-on-critical",
        action="store_true",
        help="Exit with code 1 if critical issues found"
    )

    args = parser.parse_args()

    project_root = get_project_root()

    if args.directory:
        scan_dir = Path(args.directory)
        if not scan_dir.is_absolute():
            scan_dir = project_root / scan_dir
    else:
        scan_dir = project_root / "src" / "VoiceStudio.App" / "Views" / "Panels"

    if not scan_dir.exists():
        print(f"Error: Directory not found: {scan_dir}", file=sys.stderr)
        return 1

    print(f"Scanning: {scan_dir}", file=sys.stderr)
    report = scan_directory(scan_dir, project_root)

    if args.json:
        output = json.dumps(report.to_dict(), indent=2)
    else:
        output = generate_markdown_report(report)

    if args.output:
        output_path = Path(args.output)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(output, encoding='utf-8')
        print(f"Report written to: {args.output}", file=sys.stderr)
    else:
        print(output)

    # Summary to stderr
    print(f"\nAudit complete: {report.total_issues} issues found "
          f"(Critical: {report.critical_count}, High: {report.high_count}, "
          f"Medium: {report.medium_count}, Low: {report.low_count})", file=sys.stderr)

    if args.fail_on_critical and report.critical_count > 0:
        print(f"\nFAIL: {report.critical_count} critical issues found", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
