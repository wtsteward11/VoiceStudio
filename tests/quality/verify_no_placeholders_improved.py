"""
Placeholder Verification Script (Improved)
Comprehensive scan of all code files for forbidden placeholder terms with smart filtering.
"""

import logging
import sys
from datetime import datetime
from pathlib import Path

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

project_root = Path(__file__).parent.parent.parent

# Critical forbidden terms that should ALWAYS be flagged
CRITICAL_FORBIDDEN_TERMS = [
    "TODO",
    "FIXME",
    "NotImplementedError",
    "NotImplementedException",
    "pass",  # Only in non-abstract methods
]

# Context-aware forbidden terms (check context before flagging)
CONTEXT_FORBIDDEN_TERMS = [
    "placeholder",
    "stub",
    "dummy",
    "mock",
    "fake",
    "sample",
    "temporary",
    "incomplete",
    "unfinished",
    "partial",
    "coming soon",
    "not yet",
    "eventually",
    "later",
    "for now",
    "temporary",
    "needs",
    "requires",
    "missing",
    "WIP",
    "tbd",
    "tba",
    "tbc",
]

# File patterns to check
INCLUDE_PATTERNS = [
    "**/*.py",
    "**/*.cs",
    "**/*.xaml",
]

# Directories to exclude
EXCLUDE_DIRS = [
    "__pycache__",
    ".git",
    "node_modules",
    ".venv",
    "venv",
    "env",
    "build",
    "dist",
    "*.egg-info",
    ".pytest_cache",
    ".mypy_cache",
    "tests",  # Exclude test files
    "test_data",  # Exclude test data
    "docs",  # Exclude documentation
    "installer",  # Exclude installer scripts
]

# Files to exclude
EXCLUDE_FILES = [
    "test_*.py",
    "*_test.py",
    "conftest.py",
    "verify_*.py",
    "calculate_*.py",
    "run_*.py",
    "README*.md",
    "*.iss",
    "*.wxs",
]


def should_check_file(file_path: Path) -> bool:
    """Determine if file should be checked."""
    # Check if in exclude directory
    for exclude_dir in EXCLUDE_DIRS:
        if exclude_dir in str(file_path):
            return False

    # Check if matches exclude pattern
    for exclude_pattern in EXCLUDE_FILES:
        if file_path.match(exclude_pattern):
            return False

    # Check if matches include pattern
    return any(file_path.match(include_pattern) for include_pattern in INCLUDE_PATTERNS)


def is_acceptable_context(line: str, term: str, file_path: Path) -> bool:
    """Check if term appears in acceptable context."""
    line_lower = line.lower()
    term_lower = term.lower()
    file_str = str(file_path).lower()

    # C# partial classes (acceptable)
    if term_lower == "partial" and "partial class" in line_lower:
        return True

    # UI PlaceholderText attributes (acceptable)
    if term_lower == "placeholder" and "placeholdertext" in line_lower:
        return True

    # Audio sample rate (acceptable)
    if term_lower == "sample" and (
        "sample_rate" in line_lower
        or "samplerate" in line_lower
        or "sample rate" in line_lower
        or "sampler" in line_lower
    ):
        return True

    # Checksum (acceptable)
    if term_lower == "check" and (
        "checksum" in line_lower or "check_health" in line_lower or "checkhealth" in line_lower
    ):
        return True

    # Error handling severity levels (acceptable)
    if term_lower == "warning" and (
        "severity" in line_lower
        or "level" in line_lower
        or "enum" in line_lower
        or "alertseverity" in line_lower
    ):
        return True

    # Test files (acceptable to have test-related terms)
    if "test" in file_str and term_lower in ["test", "sample", "mock"]:
        return True

    # Documentation files (acceptable to have certain terms)
    return bool(file_str.endswith(".md") and term_lower in ["note", "check", "verify", "test"])


def check_file_for_violations(file_path: Path) -> list[tuple[int, str, str]]:
    """Check file for forbidden terms and return violations."""
    violations = []

    try:
        with open(file_path, encoding="utf-8", errors="ignore") as f:
            lines = f.readlines()

            for line_num, line in enumerate(lines, 1):
                line_lower = line.lower()
                line_stripped = line.strip()

                # Skip empty lines
                if not line_stripped:
                    continue

                # Check critical forbidden terms (always flag)
                for term in CRITICAL_FORBIDDEN_TERMS:
                    term_lower = term.lower()

                    if term_lower in line_lower:
                        # Special handling for "pass" - only flag if not in abstract method or exception handler
                        if term_lower == "pass":
                            # Check if it's just "pass" on its own (likely a stub)
                            if line_stripped.strip() == "pass" or line_stripped.strip().endswith(
                                ": pass"
                            ):
                                violations.append((line_num, term, line_stripped[:100]))
                        else:
                            violations.append((line_num, term, line_stripped[:100]))

                # Check context-aware forbidden terms
                for term in CONTEXT_FORBIDDEN_TERMS:
                    term_lower = term.lower()

                    if term_lower in line_lower:
                        # Check if it's in acceptable context
                        if not is_acceptable_context(line, term, file_path):
                            # Only flag if it's clearly a placeholder/stub
                            if any(
                                indicator in line_lower
                                for indicator in [
                                    "placeholder for",
                                    "stub for",
                                    "dummy for",
                                    "mock for",
                                    "fake for",
                                    "temporary",
                                    "for now",
                                    "not yet",
                                    "coming soon",
                                    "incomplete",
                                    "unfinished",
                                ]
                            ):
                                violations.append((line_num, term, line_stripped[:100]))
    except Exception as e:
        logger.warning(f"Could not read {file_path}: {e}")

    return violations


def scan_directory(directory: Path) -> dict[str, list[tuple[int, str, str]]]:
    """Scan directory for placeholder violations."""
    violations_by_file = {}

    for file_path in directory.rglob("*"):
        if file_path.is_file() and should_check_file(file_path):
            violations = check_file_for_violations(file_path)
            if violations:
                violations_by_file[str(file_path.relative_to(project_root))] = violations

    return violations_by_file


def generate_report(violations_by_file: dict[str, list[tuple[int, str, str]]]) -> str:
    """Generate violation report."""
    report_lines = []
    report_lines.append("=" * 80)
    report_lines.append("PLACEHOLDER VERIFICATION REPORT (IMPROVED)")
    report_lines.append("=" * 80)
    report_lines.append(f"Generated: {datetime.now().isoformat()}")
    report_lines.append(f"Total files with violations: {len(violations_by_file)}")
    report_lines.append("")

    total_violations = sum(len(v) for v in violations_by_file.values())
    report_lines.append(f"Total violations found: {total_violations}")
    report_lines.append("")

    if violations_by_file:
        report_lines.append("CRITICAL VIOLATIONS (Must Fix):")
        report_lines.append("")

        for file_path, violations in sorted(violations_by_file.items()):
            report_lines.append(f"\nFile: {file_path}")
            report_lines.append(f"  Violations: {len(violations)}")
            report_lines.append("")

            for line_num, term, line_content in violations[:10]:
                report_lines.append(f"  Line {line_num}: Found '{term}'")
                report_lines.append(f"    {line_content}")

            if len(violations) > 10:
                report_lines.append(f"  ... and {len(violations) - 10} more violations")
    else:
        report_lines.append("✅ No placeholder violations found!")

    report_lines.append("")
    report_lines.append("=" * 80)

    return "\n".join(report_lines)


def main():
    """Main function."""
    logger.info("Starting improved placeholder verification scan...")
    logger.info(f"Project root: {project_root}")

    violations_by_file = scan_directory(project_root)

    report = generate_report(violations_by_file)
    print(report)

    report_file = project_root / "placeholder_verification_report_improved.txt"
    with open(report_file, "w", encoding="utf-8") as f:
        f.write(report)

    logger.info(f"Report saved to: {report_file}")

    if violations_by_file:
        logger.error(f"Found {len(violations_by_file)} files with violations")
        return 1
    else:
        logger.info("No placeholder violations found!")
        return 0


if __name__ == "__main__":
    sys.exit(main())
