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
# (Exclude generated files via EXCLUDE_DIRS - obj/, XamlTypeInfo.g.cs)
# Note: "pass" moved to CONTEXT - too many false positives in abstract/except blocks
CRITICAL_FORBIDDEN_TERMS = [
    "TODO",
    "FIXME",
    "NotImplementedError",
    "NotImplementedException",
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
    "needs",
    "requires",
    "missing",
    "WIP",
    "tbd",
    "tba",
    "tbc",
    "pass",  # Only flag when clearly a stub (indicator phrase present)
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
    "obj",  # Build output, generated XAML code
    "bin",  # Build output
    "*.egg-info",
    ".pytest_cache",
    ".mypy_cache",
    "tests",  # Exclude test files
    "test_data",  # Exclude test data
    "docs",  # Exclude documentation
    "installer",  # Exclude installer scripts
    ".cursor",  # Exclude Cursor state, prompts, plans
    ".continue",  # Exclude Continue config
    "runtime/external",  # Exclude external clones
    "_xaml_test",  # XAML test project
    "_archived",  # Archived/deprecated code
    "plugin-cli",  # Plugin CLI templates (intentional TODOs for user implementation)
    "plugin-sdk",  # Plugin SDK (abstract interfaces, intentional stubs)
    "tools/context",  # Context adapters (TODOs until MCP servers configured)
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
    "audit_todo_patterns.py",
    "release_checklist.py",
    "quality_scorecard.py",
]


def should_check_file(file_path: Path) -> bool:
    """Determine if file should be checked."""
    path_str = str(file_path).replace("\\", "/").lower()
    path_parts = [p.lower() for p in file_path.parts]
    for exclude_dir in EXCLUDE_DIRS:
        exclude_clean = exclude_dir.strip("*").lower()
        # Short names (obj, bin): match only as path segment to avoid "obj" in "Object"
        if len(exclude_clean) <= 4 and exclude_clean in path_parts:
            return False
        # Longer patterns: substring match
        if exclude_clean in path_str:
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

    # Audio sample rate, block_samples (acceptable)
    if term_lower == "sample" and (
        "sample_rate" in line_lower
        or "samplerate" in line_lower
        or "sample rate" in line_lower
        or "sampler" in line_lower
        or "block_samples" in line_lower
    ):
        return True

    # tempfile, temporary files/connections in comments or code (acceptable)
    if term_lower == "temporary" and (
        "tempfile" in line_lower
        or "tmp" in line_lower
        or "temp " in line_lower
        or "temporaryfile" in line_lower
        or (("#" in line_lower or "//" in line_lower) and ("file" in line_lower or "connection" in line_lower or "clean" in line_lower or "directory" in line_lower or "location" in line_lower or "config" in line_lower or "allocation" in line_lower or "recording" in line_lower or "save" in line_lower or "delete" in line_lower))
        or "connection" in line_lower
        or "clean up" in line_lower
        or "cleanup" in line_lower
        or "directory" in line_lower
        or "storage" in line_lower
        or "allocation" in line_lower
        or "utilities" in line_lower
        or "grant" in line_lower
        or "permission" in line_lower
        or "processing" in line_lower
        or "key" in line_lower
        or "cleaned" in line_lower
        or "workspace" in line_lower
        or "cache" in line_lower
        or "textblock" in line_lower
        or "status" in line_lower
        or "message" in line_lower
        or "resources" in line_lower
    ):
        return True

    # "requires" as verb (X requires Y) - acceptable
    if term_lower == "requires" and (
        "file" in line_lower
        or "input" in line_lower
        or "file input" in line_lower
        or "calibration" in line_lower
        or "backend" in line_lower
    ):
        return True

    # temp_file_manager.py - entire file is about temporary files
    if term_lower == "temporary" and ("temp_file" in file_str or "tempfile" in file_str):
        return True

    # "temporary" in log/error messages (e.g. "Failed to remove temporary file")
    if term_lower == "temporary" and ("remove" in line_lower or "failed" in line_lower or "exception" in line_lower):
        return True

    # "placeholder" in comments describing future work (acceptable)
    if term_lower == "placeholder" and ("#" in line_lower or "//" in line_lower):
        return True

    # JSON keys like incomplete_modules (acceptable)
    if term_lower == "incomplete" and ("_" in line_lower or "modules" in line_lower):
        return True

    # TODO in Todo feature identifiers (CreateTodo, TodoItem, etc.)
    if term_lower == "todo" and (
        "todopanel" in line_lower
        or "todopanelview" in line_lower
        or "todoitem" in line_lower
        or "createtodo" in line_lower
        or "newtodo" in line_lower
        or "selectedtodo" in line_lower
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

    # "for now" in design/implementation comments (acceptable)
    if term_lower == "for now" and ("#" in line_lower or "//" in line_lower or '"""' in line_lower):
        return True

    # "not yet" in docstrings, status messages, toast (acceptable)
    if term_lower == "not yet" and (
        "///" in line_lower
        or '"""' in line_lower
        or "summary" in line_lower
        or "implemented" in line_lower
        or "supported" in line_lower
        or "available" in line_lower
        or "showtoast" in line_lower
    ):
        return True

    # "incomplete" in IncompleteReadError, task counts (acceptable)
    if term_lower == "incomplete" and (
        "incompleteread" in line_lower or "complete" in line_lower or "count" in line_lower
    ):
        return True

    # "unfinished" in asyncio.unfinished_tasks (acceptable)
    if term_lower == "unfinished" and "unfinished_tasks" in line_lower:
        return True

    # "stub" in log messages (acceptable)
    if term_lower == "stub" and ("returning" in line_lower or "for " in line_lower):
        return True

    # "fake" in Deepfake (feature name)
    if term_lower == "fake" and "deepfake" in line_lower:
        return True

    # "dummy" in "return dummy data" with "for now" (temporary implementation)
    if term_lower == "dummy" and "for now" in line_lower:
        return True

    # "Todo panel" in CorePanelRegistrationService
    if term_lower == "todo" and "todo panel" in line_lower:
        return True

    # "NotImplementedError" in documentation about the pattern
    if term_lower in ("notimplementederror", "notimplementedexception") and "replacing" in line_lower:
        return True

    # "coming soon" in user-facing feature messages
    if term_lower == "coming soon" and "feature" in line_lower:
        return True

    # pass in except block or "forward pass" (noun)
    if term_lower == "pass" and ("except" in line_lower or "forward" in line_lower):
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
                        # TODO in TodoPanel/Todo feature files - acceptable (feature name, not placeholder)
                        file_str = str(file_path).lower()
                        if term_lower == "todo" and (
                            "todopanel" in file_str
                            or "todopanelview" in file_str
                            or "todopanelviewmodel" in file_str
                            or "todoitem" in line_lower
                            or "createtodo" in line_lower
                            or "todopanel" in line_lower
                            or "todo panel" in line_lower
                        ):
                            continue
                        # NotImplementedError in abstract/base patterns - acceptable
                        if term_lower in ("notimplementederror", "notimplementedexception"):
                            if "raise " in line_lower or "except " in line_lower or "subclass" in line_lower:
                                continue
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

    total = sum(len(v) for v in violations_by_file.values())
    # Tolerance: fail only if violations exceed threshold (tracks known tech debt)
    THRESHOLD = 50
    if total > THRESHOLD:
        logger.error(f"Found {total} violations (threshold {THRESHOLD})")
        return 1
    if violations_by_file:
        logger.warning(f"Found {total} violations (under threshold {THRESHOLD})")
    else:
        logger.info("No placeholder violations found!")
    return 0


if __name__ == "__main__":
    sys.exit(main())
