#!/usr/bin/env python3
"""
validate_xaml_quality.py - Unified XAML Quality Validation

Consolidates XAML quality checks into a single script:
1. StaticResource reference validation (VSQ.* resources)
2. XAML page count thresholds per project
3. Naming convention compliance
4. x:Uid presence for accessibility

Usage:
    python scripts/validate_xaml_quality.py
    python scripts/validate_xaml_quality.py --check resources
    python scripts/validate_xaml_quality.py --check pages
    python scripts/validate_xaml_quality.py --check naming
    python scripts/validate_xaml_quality.py --check accessibility
    python scripts/validate_xaml_quality.py --json

Exit codes:
    0: All checks passed
    1: One or more checks failed
"""

import argparse
import json
import re
import sys
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path


# Project root detection
def get_project_root() -> Path:
    """Find project root by looking for VoiceStudio.sln."""
    script_dir = Path(__file__).parent
    for parent in [script_dir.parent, script_dir]:
        if (parent / "VoiceStudio.sln").exists():
            return parent
    raise RuntimeError("Could not find project root (VoiceStudio.sln)")


# ============================================================================
# Data Classes
# ============================================================================

@dataclass
class CheckResult:
    """Result of a single check."""
    name: str
    passed: bool
    message: str
    details: list[str] = field(default_factory=list)

    def to_dict(self) -> dict:
        return {
            "name": self.name,
            "passed": self.passed,
            "message": self.message,
            "details": self.details
        }


@dataclass
class QualityReport:
    """Overall XAML quality report."""
    timestamp: str = field(default_factory=lambda: datetime.now().isoformat())
    passed: bool = True
    checks: list[CheckResult] = field(default_factory=list)

    def add_check(self, result: CheckResult):
        self.checks.append(result)
        if not result.passed:
            self.passed = False

    def to_dict(self) -> dict:
        return {
            "timestamp": self.timestamp,
            "passed": self.passed,
            "checks": [c.to_dict() for c in self.checks]
        }


# ============================================================================
# Check 1: StaticResource Validation
# ============================================================================

RESOURCE_DIRS = [
    "src/VoiceStudio.App/Resources",
    "src/VoiceStudio.App/Resources/Styles",
]
XAML_SCAN_DIRS = [
    "src/VoiceStudio.App/Views",
    "src/VoiceStudio.App/Controls",
]

RESOURCE_DEF_PATTERN = re.compile(r'x:Key="(VSQ\.[A-Za-z0-9.]+)"')
RESOURCE_REF_PATTERN = re.compile(r'\{(?:Static|Theme)Resource\s+(VSQ\.[A-Za-z0-9.]+)\}')


def check_resources(project_root: Path, verbose: bool = False) -> CheckResult:
    """Validate that all VSQ.* StaticResource references are defined."""
    # Collect defined resources
    defined: dict[str, Path] = {}
    for res_dir in RESOURCE_DIRS:
        res_path = project_root / res_dir
        if not res_path.exists():
            continue
        for xaml_file in res_path.glob("*.xaml"):
            try:
                content = xaml_file.read_text(encoding="utf-8")
                for match in RESOURCE_DEF_PATTERN.finditer(content):
                    defined[match.group(1)] = xaml_file
            except (OSError, UnicodeDecodeError):
                pass

    # Collect referenced resources
    referenced: dict[str, list[tuple[Path, int]]] = {}
    for xaml_dir in XAML_SCAN_DIRS:
        dir_path = project_root / xaml_dir
        if not dir_path.exists():
            continue
        for xaml_file in dir_path.rglob("*.xaml"):
            try:
                content = xaml_file.read_text(encoding="utf-8")
                for i, line in enumerate(content.splitlines(), 1):
                    for match in RESOURCE_REF_PATTERN.finditer(line):
                        key = match.group(1)
                        if key not in referenced:
                            referenced[key] = []
                        referenced[key].append((xaml_file, i))
            except (OSError, UnicodeDecodeError):
                pass

    # Find missing resources
    missing = set(referenced.keys()) - set(defined.keys())

    details = []
    if verbose:
        for key in sorted(missing)[:10]:
            locs = referenced[key][:3]
            loc_strs = [f"{p.name}:{ln}" for p, ln in locs]
            details.append(f"  Missing: {key} (used in: {', '.join(loc_strs)})")
        if len(missing) > 10:
            details.append(f"  ... and {len(missing) - 10} more")

    if missing:
        return CheckResult(
            name="resources",
            passed=False,
            message=f"Found {len(missing)} undefined VSQ.* resources",
            details=details
        )

    return CheckResult(
        name="resources",
        passed=True,
        message=f"All {len(referenced)} VSQ.* resource references are defined"
    )


# ============================================================================
# Check 2: XAML Page Count Thresholds
# ============================================================================

PAGE_THRESHOLDS = {
    "VoiceStudio.App": 25,
    "VoiceStudio.Module.Voice": 50,
    "VoiceStudio.Module.Media": 50,
    "VoiceStudio.Module.Analysis": 50,
    "VoiceStudio.Module.Workflow": 50,
    "VoiceStudio.Common.UI": 10,
}


def count_xaml_pages(project_dir: Path) -> int:
    """Count XAML files, excluding ResourceDictionaries."""
    count = 0
    for xaml_file in project_dir.glob("**/*.xaml"):
        try:
            content = xaml_file.read_text(encoding="utf-8")[:500]
            if "<ResourceDictionary" not in content:
                count += 1
        except (OSError, UnicodeDecodeError):
            count += 1  # Conservative: count unreadable files
    return count


def check_page_count(project_root: Path, verbose: bool = False) -> CheckResult:
    """Check XAML page count against thresholds."""
    src_dir = project_root / "src"
    if not src_dir.exists():
        return CheckResult(
            name="pages",
            passed=True,
            message="No src directory found"
        )

    violations = []
    details = []

    for project, threshold in PAGE_THRESHOLDS.items():
        path = src_dir / project
        if not path.exists():
            continue

        count = count_xaml_pages(path)
        status = "PASS" if count <= threshold else "FAIL"

        if verbose or count > threshold:
            details.append(f"  {project}: {count}/{threshold} [{status}]")

        if count > threshold:
            violations.append((project, count, threshold))

    if violations:
        return CheckResult(
            name="pages",
            passed=False,
            message=f"{len(violations)} projects exceed XAML page thresholds",
            details=details
        )

    return CheckResult(
        name="pages",
        passed=True,
        message="All projects within XAML page thresholds",
        details=details if verbose else []
    )


# ============================================================================
# Check 3: Naming Convention Compliance
# ============================================================================

NAMING_PATTERNS = {
    "View": r".*View\.xaml$",
    "UserControl": r".*(?:Control|Panel|Card|Dialog)\.xaml$",
    "Page": r".*Page\.xaml$",
    "Window": r".*Window\.xaml$",
}

# Files that should match one of the above patterns
NAMING_CONVENTION_DIRS = [
    "src/VoiceStudio.App/Views",
    "src/VoiceStudio.App/Controls",
]


def check_naming(project_root: Path, verbose: bool = False) -> CheckResult:
    """Check XAML file naming conventions."""
    violations = []
    checked = 0

    for xaml_dir in NAMING_CONVENTION_DIRS:
        dir_path = project_root / xaml_dir
        if not dir_path.exists():
            continue

        for xaml_file in dir_path.rglob("*.xaml"):
            # Skip resource dictionaries and App.xaml
            if xaml_file.name in ("App.xaml",):
                continue

            try:
                content = xaml_file.read_text(encoding="utf-8")[:500]
                if "<ResourceDictionary" in content:
                    continue
            except (OSError, UnicodeDecodeError):
                continue

            checked += 1

            # Check if file matches any expected pattern
            matches_pattern = False
            for _pattern_name, pattern in NAMING_PATTERNS.items():
                if re.match(pattern, xaml_file.name, re.IGNORECASE):
                    matches_pattern = True
                    break

            if not matches_pattern:
                violations.append(xaml_file)

    details = []
    if violations:
        for f in violations[:10]:
            details.append(f"  Non-standard name: {f.name}")
        if len(violations) > 10:
            details.append(f"  ... and {len(violations) - 10} more")

    if violations:
        return CheckResult(
            name="naming",
            passed=False,
            message=f"{len(violations)} files don't follow naming conventions",
            details=details
        )

    return CheckResult(
        name="naming",
        passed=True,
        message=f"All {checked} XAML files follow naming conventions"
    )


# ============================================================================
# Check 4: Accessibility (x:Uid presence)
# ============================================================================

# Elements that should have x:Uid for localization/accessibility
ACCESSIBLE_ELEMENTS = [
    "Button",
    "ToggleButton",
    "CheckBox",
    "RadioButton",
    "TextBlock",  # Only for important labels
    "MenuFlyoutItem",
    "AppBarButton",
    "NavigationViewItem",
]

X_UID_PATTERN = re.compile(r'x:Uid="[^"]*"')
ELEMENT_PATTERN = re.compile(r'<(' + '|'.join(ACCESSIBLE_ELEMENTS) + r')(?:\s|>|/)')


def check_accessibility(project_root: Path, verbose: bool = False) -> CheckResult:
    """Check for x:Uid presence on interactive elements."""
    issues = []
    checked_files = 0
    total_elements = 0
    elements_with_uid = 0

    for xaml_dir in XAML_SCAN_DIRS:
        dir_path = project_root / xaml_dir
        if not dir_path.exists():
            continue

        for xaml_file in dir_path.rglob("*.xaml"):
            try:
                content = xaml_file.read_text(encoding="utf-8")
            except (OSError, UnicodeDecodeError):
                continue

            if "<ResourceDictionary" in content[:500]:
                continue

            checked_files += 1

            # Find accessible elements
            for match in ELEMENT_PATTERN.finditer(content):
                total_elements += 1
                # Get surrounding context to check for x:Uid
                start = max(0, match.start() - 10)
                end = min(len(content), match.end() + 100)
                context = content[start:end]

                if X_UID_PATTERN.search(context):
                    elements_with_uid += 1
                else:
                    # Only report a sample of issues
                    if len(issues) < 20:
                        line_num = content[:match.start()].count('\n') + 1
                        issues.append((xaml_file.name, line_num, match.group(1)))

    coverage = (elements_with_uid / total_elements * 100) if total_elements > 0 else 100

    details = []
    if verbose and issues:
        for file_name, line_num, element in issues[:10]:
            details.append(f"  {file_name}:{line_num} - <{element}> missing x:Uid")
        if len(issues) > 10:
            details.append(f"  ... and {len(issues) - 10} more")

    # We don't fail for accessibility - it's informational
    return CheckResult(
        name="accessibility",
        passed=True,  # Informational only
        message=f"x:Uid coverage: {elements_with_uid}/{total_elements} elements ({coverage:.1f}%)",
        details=details
    )


# ============================================================================
# Main Entry Point
# ============================================================================

ALL_CHECKS = {
    "resources": check_resources,
    "pages": check_page_count,
    "naming": check_naming,
    "accessibility": check_accessibility,
}


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Unified XAML Quality Validation"
    )
    parser.add_argument(
        "--check", "-c",
        choices=list(ALL_CHECKS.keys()),
        action="append",
        help="Specific check to run (can be repeated). Default: all checks"
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output results as JSON"
    )
    parser.add_argument(
        "--verbose", "-v",
        action="store_true",
        help="Show detailed output"
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

    # Determine which checks to run
    checks_to_run = args.check if args.check else list(ALL_CHECKS.keys())

    # Run checks
    report = QualityReport()

    for check_name in checks_to_run:
        check_fn = ALL_CHECKS[check_name]
        result = check_fn(project_root, verbose=args.verbose)
        report.add_check(result)

    # Generate output
    if args.json:
        output = json.dumps(report.to_dict(), indent=2)
    else:
        lines = [
            "=" * 60,
            "XAML Quality Validation Report",
            "=" * 60,
            "",
        ]

        for check in report.checks:
            status = "[PASS]" if check.passed else "[FAIL]"
            lines.append(f"{status} {check.name}: {check.message}")
            for detail in check.details:
                lines.append(detail)
            lines.append("")

        lines.append("=" * 60)
        if report.passed:
            lines.append("Overall: PASS - All checks passed")
        else:
            failed = [c.name for c in report.checks if not c.passed]
            lines.append(f"Overall: FAIL - Failed checks: {', '.join(failed)}")
        lines.append("=" * 60)

        output = "\n".join(lines)

    # Output results
    if args.output:
        output_path = Path(args.output)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(output, encoding="utf-8")
        print(f"Report written to: {args.output}", file=sys.stderr)
    else:
        print(output)

    # Also save JSON to buildlogs
    buildlogs_dir = project_root / ".buildlogs"
    buildlogs_dir.mkdir(exist_ok=True)
    json_path = buildlogs_dir / "xaml-quality.json"
    json_path.write_text(json.dumps(report.to_dict(), indent=2), encoding="utf-8")

    return 0 if report.passed else 1


if __name__ == "__main__":
    sys.exit(main())
