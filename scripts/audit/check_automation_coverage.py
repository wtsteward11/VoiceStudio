#!/usr/bin/env python3
"""
check_automation_coverage.py - Check UI Automation Property Coverage in XAML

Scans XAML files to verify that interactive elements have proper automation
properties for accessibility and UI testing:
- AutomationProperties.Name
- AutomationProperties.AutomationId
- x:Uid (for localization)

Usage:
    python scripts/check_automation_coverage.py
    python scripts/check_automation_coverage.py --threshold 50
    python scripts/check_automation_coverage.py --file src/VoiceStudio.App/Views/SomeView.xaml
    python scripts/check_automation_coverage.py --json

Exit codes:
    0: Coverage meets threshold (or no threshold specified)
    1: Coverage below threshold
"""

import argparse
import json
import re
import sys
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

# ============================================================================
# Configuration
# ============================================================================

def get_project_root() -> Path:
    """Find project root by looking for VoiceStudio.sln."""
    script_dir = Path(__file__).parent
    for parent in [script_dir.parent, script_dir]:
        if (parent / "VoiceStudio.sln").exists():
            return parent
    raise RuntimeError("Could not find project root (VoiceStudio.sln)")


XAML_DIRS = [
    "src/VoiceStudio.App/Views",
    "src/VoiceStudio.App/Controls",
]

# Elements that MUST have automation properties for accessibility
REQUIRED_AUTOMATION_ELEMENTS = [
    "Button",
    "ToggleButton",
    "RadioButton",
    "CheckBox",
    "Slider",
    "ComboBox",
    "TextBox",
    "PasswordBox",
    "ListView",
    "GridView",
    "TreeView",
    "AppBarButton",
    "MenuFlyoutItem",
    "NavigationViewItem",
    "HyperlinkButton",
]

# Elements that SHOULD have automation properties (best practice)
RECOMMENDED_AUTOMATION_ELEMENTS = [
    "TextBlock",
    "Image",
    "ProgressRing",
    "ProgressBar",
    "InfoBar",
    "Expander",
    "TabViewItem",
]


# ============================================================================
# Data Classes
# ============================================================================

@dataclass
class ElementInfo:
    """Information about an interactive element."""
    file_path: Path
    line_number: int
    element_type: str
    has_name: bool = False
    has_automation_id: bool = False
    has_uid: bool = False
    has_any_automation: bool = False
    is_required: bool = True


@dataclass
class FileReport:
    """Coverage report for a single file."""
    file_path: Path
    total_elements: int = 0
    covered_elements: int = 0
    required_missing: list[ElementInfo] = field(default_factory=list)
    recommended_missing: list[ElementInfo] = field(default_factory=list)

    @property
    def coverage_percent(self) -> float:
        if self.total_elements == 0:
            return 100.0
        return (self.covered_elements / self.total_elements) * 100

    def to_dict(self) -> dict:
        return {
            "file": str(self.file_path),
            "total_elements": self.total_elements,
            "covered_elements": self.covered_elements,
            "coverage_percent": round(self.coverage_percent, 1),
            "required_missing_count": len(self.required_missing),
            "recommended_missing_count": len(self.recommended_missing)
        }


@dataclass
class CoverageReport:
    """Overall automation coverage report."""
    timestamp: str = field(default_factory=lambda: datetime.now().isoformat())
    total_files: int = 0
    total_elements: int = 0
    covered_elements: int = 0
    required_missing: int = 0
    recommended_missing: int = 0
    file_reports: list[FileReport] = field(default_factory=list)

    @property
    def coverage_percent(self) -> float:
        if self.total_elements == 0:
            return 100.0
        return (self.covered_elements / self.total_elements) * 100

    def to_dict(self) -> dict:
        return {
            "timestamp": self.timestamp,
            "total_files": self.total_files,
            "total_elements": self.total_elements,
            "covered_elements": self.covered_elements,
            "coverage_percent": round(self.coverage_percent, 1),
            "required_missing": self.required_missing,
            "recommended_missing": self.recommended_missing,
            "files": [f.to_dict() for f in self.file_reports]
        }


# ============================================================================
# Parsing Patterns
# ============================================================================

# Match element open tags
ELEMENT_PATTERN = re.compile(
    r'<(' + '|'.join(REQUIRED_AUTOMATION_ELEMENTS + RECOMMENDED_AUTOMATION_ELEMENTS) + r')(?:\s+[^>]*)?(?:>|/>)',
    re.IGNORECASE
)

# Match automation properties
AUTOMATION_NAME_PATTERN = re.compile(
    r'AutomationProperties\.Name\s*=\s*"[^"]*"'
)
AUTOMATION_ID_PATTERN = re.compile(
    r'AutomationProperties\.AutomationId\s*=\s*"[^"]*"'
)
X_UID_PATTERN = re.compile(
    r'x:Uid\s*=\s*"[^"]*"'
)
X_NAME_PATTERN = re.compile(
    r'x:Name\s*=\s*"[^"]*"'
)


# ============================================================================
# Analysis Functions
# ============================================================================

def analyze_element(content: str, match: re.Match, required_elements: set[str]) -> ElementInfo:
    """Analyze a single element for automation properties."""
    element_type = match.group(1)
    start = match.start()

    # Find the closing of this element tag (either > or />)
    end = match.end()
    # Look for extended attributes if the element spans multiple lines
    # Find the full element including any attributes
    tag_content = content[start:min(end + 500, len(content))]

    # Find where this tag ends
    tag_end_match = re.search(r'(?:/>|>)', tag_content)
    if tag_end_match:
        tag_content = tag_content[:tag_end_match.end()]

    # Check for automation properties
    has_name = bool(AUTOMATION_NAME_PATTERN.search(tag_content))
    has_automation_id = bool(AUTOMATION_ID_PATTERN.search(tag_content))
    has_uid = bool(X_UID_PATTERN.search(tag_content))
    bool(X_NAME_PATTERN.search(tag_content))

    # Consider x:Name as partial coverage (good for testing, not for accessibility)
    has_any = has_name or has_automation_id or has_uid

    # Calculate line number
    line_number = content[:start].count('\n') + 1

    is_required = element_type in required_elements

    return ElementInfo(
        file_path=Path(""),  # Will be set by caller
        line_number=line_number,
        element_type=element_type,
        has_name=has_name,
        has_automation_id=has_automation_id,
        has_uid=has_uid,
        has_any_automation=has_any,
        is_required=is_required
    )


def analyze_file(file_path: Path) -> FileReport:
    """Analyze a XAML file for automation coverage."""
    report = FileReport(file_path=file_path)
    required_set = set(REQUIRED_AUTOMATION_ELEMENTS)

    try:
        content = file_path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return report

    # Skip resource dictionaries
    if "<ResourceDictionary" in content[:500]:
        return report

    # Find all interactive elements
    for match in ELEMENT_PATTERN.finditer(content):
        element = analyze_element(content, match, required_set)
        element.file_path = file_path

        report.total_elements += 1

        if element.has_any_automation:
            report.covered_elements += 1
        else:
            if element.is_required:
                report.required_missing.append(element)
            else:
                report.recommended_missing.append(element)

    return report


def analyze_directory(dir_path: Path) -> list[FileReport]:
    """Analyze all XAML files in a directory."""
    reports = []

    if not dir_path.exists():
        return reports

    for xaml_file in dir_path.rglob("*.xaml"):
        report = analyze_file(xaml_file)
        if report.total_elements > 0:  # Only include files with elements
            reports.append(report)

    return reports


# ============================================================================
# Output Formatting
# ============================================================================

def format_table_output(report: CoverageReport, show_files: bool = False) -> str:
    """Format report as a table."""
    lines = [
        "=" * 70,
        "UI Automation Coverage Report",
        "=" * 70,
        "",
        f"Total Files Analyzed: {report.total_files}",
        f"Total Interactive Elements: {report.total_elements}",
        f"Elements with Automation: {report.covered_elements}",
        f"Coverage: {report.coverage_percent:.1f}%",
        "",
        f"Required Elements Missing Automation: {report.required_missing}",
        f"Recommended Elements Missing Automation: {report.recommended_missing}",
        "",
    ]

    if show_files and report.file_reports:
        lines.append("Per-File Coverage:")
        lines.append("-" * 70)
        lines.append(f"{'File':<50} {'Covered':<10} {'Coverage':<10}")
        lines.append("-" * 70)

        # Sort by coverage (lowest first)
        sorted_reports = sorted(report.file_reports, key=lambda r: r.coverage_percent)

        for file_report in sorted_reports[:20]:
            file_name = file_report.file_path.name[:48]
            covered = f"{file_report.covered_elements}/{file_report.total_elements}"
            coverage = f"{file_report.coverage_percent:.1f}%"
            lines.append(f"{file_name:<50} {covered:<10} {coverage:<10}")

        if len(sorted_reports) > 20:
            lines.append(f"... and {len(sorted_reports) - 20} more files")

        lines.append("")

    lines.append("=" * 70)

    # Show some specific missing elements
    if report.required_missing > 0:
        lines.append("")
        lines.append("Sample Required Elements Missing Automation:")
        lines.append("-" * 50)

        shown = 0
        for file_report in report.file_reports:
            for elem in file_report.required_missing[:3]:
                if shown >= 10:
                    break
                lines.append(f"  {elem.file_path.name}:{elem.line_number} - <{elem.element_type}>")
                shown += 1
            if shown >= 10:
                break

        if report.required_missing > shown:
            lines.append(f"  ... and {report.required_missing - shown} more")

    return "\n".join(lines)


# ============================================================================
# Main Entry Point
# ============================================================================

def main() -> int:
    parser = argparse.ArgumentParser(
        description="Check UI Automation Property Coverage in XAML"
    )
    parser.add_argument(
        "--threshold", "-t",
        type=float,
        default=0.0,
        help="Minimum coverage percentage required (default: 0, no threshold)"
    )
    parser.add_argument(
        "--file", "-f",
        type=str,
        help="Analyze a specific XAML file"
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output results as JSON"
    )
    parser.add_argument(
        "--verbose", "-v",
        action="store_true",
        help="Show per-file coverage details"
    )
    parser.add_argument(
        "--output", "-o",
        type=str,
        help="Output file path"
    )

    args = parser.parse_args()

    try:
        project_root = get_project_root()
    except RuntimeError as e:
        print(f"Error: {e}", file=sys.stderr)
        return 1

    # Collect file reports
    file_reports: list[FileReport] = []

    if args.file:
        file_path = Path(args.file)
        if not file_path.is_absolute():
            file_path = project_root / file_path

        if not file_path.exists():
            print(f"Error: File not found: {file_path}", file=sys.stderr)
            return 1

        report = analyze_file(file_path)
        if report.total_elements > 0:
            file_reports.append(report)
    else:
        for xaml_dir in XAML_DIRS:
            dir_path = project_root / xaml_dir
            file_reports.extend(analyze_directory(dir_path))

    # Build overall report
    report = CoverageReport(
        total_files=len(file_reports),
        file_reports=file_reports
    )

    for file_report in file_reports:
        report.total_elements += file_report.total_elements
        report.covered_elements += file_report.covered_elements
        report.required_missing += len(file_report.required_missing)
        report.recommended_missing += len(file_report.recommended_missing)

    # Generate output
    if args.json:
        output = json.dumps(report.to_dict(), indent=2)
    else:
        output = format_table_output(report, show_files=args.verbose)

    # Write output
    if args.output:
        output_path = Path(args.output)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(output, encoding="utf-8")
        print(f"Report written to: {args.output}", file=sys.stderr)
    else:
        print(output)

    # Save JSON to buildlogs
    buildlogs_dir = project_root / ".buildlogs"
    buildlogs_dir.mkdir(exist_ok=True)
    json_path = buildlogs_dir / "automation-coverage.json"
    json_path.write_text(json.dumps(report.to_dict(), indent=2), encoding="utf-8")

    # Print summary to stderr
    print(f"\nAutomation Coverage: {report.covered_elements}/{report.total_elements} ({report.coverage_percent:.1f}%)", file=sys.stderr)

    # Check threshold
    if args.threshold > 0:
        if report.coverage_percent < args.threshold:
            print(f"FAIL: Coverage {report.coverage_percent:.1f}% is below threshold {args.threshold}%", file=sys.stderr)
            return 1
        else:
            print(f"PASS: Coverage {report.coverage_percent:.1f}% meets threshold {args.threshold}%", file=sys.stderr)

    return 0


if __name__ == "__main__":
    sys.exit(main())
