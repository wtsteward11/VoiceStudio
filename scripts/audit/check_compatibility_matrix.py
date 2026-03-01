#!/usr/bin/env python3
"""
VoiceStudio Compatibility Matrix Validator

Validates that project files match the version pins defined in
config/compatibility_matrix.yml.

Usage:
    python scripts/check_compatibility_matrix.py [--local] [--json] [--verbose]

Options:
    --local     Run in local/pre-commit mode (quick checks only)
    --json      Output results as JSON
    --verbose   Show detailed validation output
    --dry-run   Show what would be checked without running
    --list-pins List all version pins from the matrix

Exit codes:
    0 - All validations passed
    1 - Validation failures detected
    2 - Configuration error (matrix file missing, parse error)

Part of the Claude Recommendations Integration (Phase 1).
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any, NamedTuple

# Attempt to import yaml; provide helpful error if missing
try:
    import yaml
except ImportError:
    print("ERROR: PyYAML is required. Install with: pip install pyyaml", file=sys.stderr)
    sys.exit(2)


class ValidationResult(NamedTuple):
    """Result of a single validation check."""
    passed: bool
    source: str
    expected: str
    actual: str
    message: str


class MatrixValidator:
    """Validates project files against the compatibility matrix."""

    def __init__(self, project_root: Path, verbose: bool = False):
        self.project_root = project_root
        self.verbose = verbose
        self.matrix_path = project_root / "config" / "compatibility_matrix.yml"
        self.matrix: dict[str, Any] = {}
        self.results: list[ValidationResult] = []

    def load_matrix(self) -> bool:
        """Load the compatibility matrix YAML file."""
        if not self.matrix_path.exists():
            print(f"ERROR: Matrix file not found: {self.matrix_path}", file=sys.stderr)
            return False

        try:
            with open(self.matrix_path, encoding="utf-8") as f:
                self.matrix = yaml.safe_load(f)
            if self.verbose:
                print(f"Loaded matrix version {self.matrix.get('version', 'unknown')}")
            return True
        except yaml.YAMLError as e:
            print(f"ERROR: Failed to parse matrix: {e}", file=sys.stderr)
            return False

    def validate_global_json(self) -> None:
        """Validate global.json against matrix platform.dotnet_sdk."""
        global_json_path = self.project_root / "global.json"
        if not global_json_path.exists():
            self.results.append(ValidationResult(
                passed=False,
                source="global.json",
                expected="exists",
                actual="missing",
                message="global.json not found"
            ))
            return

        try:
            with open(global_json_path, encoding="utf-8") as f:
                data = json.load(f)

            actual_sdk = data.get("sdk", {}).get("version", "")
            expected_sdk = self.matrix.get("platform", {}).get("dotnet_sdk", "")

            if actual_sdk == expected_sdk:
                self.results.append(ValidationResult(
                    passed=True,
                    source="global.json",
                    expected=expected_sdk,
                    actual=actual_sdk,
                    message="SDK version matches"
                ))
            else:
                self.results.append(ValidationResult(
                    passed=False,
                    source="global.json",
                    expected=expected_sdk,
                    actual=actual_sdk,
                    message=f"SDK version mismatch: expected {expected_sdk}, found {actual_sdk}"
                ))
        except (json.JSONDecodeError, OSError) as e:
            self.results.append(ValidationResult(
                passed=False,
                source="global.json",
                expected="valid JSON",
                actual=str(e),
                message=f"Failed to parse global.json: {e}"
            ))

    def validate_version_lock(self) -> None:
        """Validate version_lock.json against matrix Python dependencies."""
        lock_path = self.project_root / "version_lock.json"
        if not lock_path.exists():
            self.results.append(ValidationResult(
                passed=False,
                source="version_lock.json",
                expected="exists",
                actual="missing",
                message="version_lock.json not found"
            ))
            return

        try:
            with open(lock_path, encoding="utf-8") as f:
                lock_data = json.load(f)
        except (json.JSONDecodeError, OSError) as e:
            self.results.append(ValidationResult(
                passed=False,
                source="version_lock.json",
                expected="valid JSON",
                actual=str(e),
                message=f"Failed to parse version_lock.json: {e}"
            ))
            return

        python_deps = self.matrix.get("dependencies", {}).get("python", {})
        critical_deps = ["torch", "numpy", "librosa", "transformers"]

        for dep in critical_deps:
            if dep not in python_deps:
                continue

            expected = python_deps[dep].get("version", "")
            # Handle package name variations (e.g., faster_whisper vs faster-whisper)
            lock_key = dep.replace("_", "-")
            actual = lock_data.get(lock_key, lock_data.get(dep, ""))

            # Skip comparison for version ranges (>=, <, etc.)
            if expected.startswith((">=", "<=", ">", "<", "~=")):
                continue

            if actual == expected:
                self.results.append(ValidationResult(
                    passed=True,
                    source=f"version_lock.json:{dep}",
                    expected=expected,
                    actual=actual,
                    message=f"{dep} version matches"
                ))
            else:
                self.results.append(ValidationResult(
                    passed=False,
                    source=f"version_lock.json:{dep}",
                    expected=expected,
                    actual=actual or "(not found)",
                    message=f"{dep} version mismatch"
                ))

    def validate_directory_build_props(self) -> None:
        """Validate Directory.Build.props against matrix .NET dependencies."""
        props_path = self.project_root / "Directory.Build.props"
        if not props_path.exists():
            self.results.append(ValidationResult(
                passed=False,
                source="Directory.Build.props",
                expected="exists",
                actual="missing",
                message="Directory.Build.props not found"
            ))
            return

        try:
            content = props_path.read_text(encoding="utf-8")
        except OSError as e:
            self.results.append(ValidationResult(
                passed=False,
                source="Directory.Build.props",
                expected="readable",
                actual=str(e),
                message=f"Failed to read Directory.Build.props: {e}"
            ))
            return

        dotnet_deps = self.matrix.get("dependencies", {}).get("dotnet", {})

        # Check WindowsAppSDK version
        win_app_sdk = dotnet_deps.get("windows_app_sdk", {}).get("version", "")
        if win_app_sdk:
            # Look for the default version in Directory.Build.props
            # Format: <MicrosoftWindowsAppSDKVersion Condition="...''">VERSION</...>
            # The default value is in the Condition="...== ''" case
            patterns = [
                # Match: Condition="...== ''">VERSION<
                r"MicrosoftWindowsAppSDKVersion[^>]*==\s*''[^>]*>\s*([0-9][^<\s]+)",
                # Match direct property without condition
                r"<MicrosoftWindowsAppSDKVersion>\s*([0-9][^<\s]+)",
            ]

            actual = None
            for pattern in patterns:
                match = re.search(pattern, content, re.IGNORECASE)
                if match:
                    actual = match.group(1).strip()
                    break

            if actual:
                if actual == win_app_sdk:
                    self.results.append(ValidationResult(
                        passed=True,
                        source="Directory.Build.props:WindowsAppSDK",
                        expected=win_app_sdk,
                        actual=actual,
                        message="WindowsAppSDK version matches"
                    ))
                else:
                    self.results.append(ValidationResult(
                        passed=False,
                        source="Directory.Build.props:WindowsAppSDK",
                        expected=win_app_sdk,
                        actual=actual,
                        message="WindowsAppSDK version mismatch"
                    ))
            else:
                self.results.append(ValidationResult(
                    passed=False,
                    source="Directory.Build.props:WindowsAppSDK",
                    expected=win_app_sdk,
                    actual="(not found)",
                    message="Could not find WindowsAppSDK version in Directory.Build.props"
                ))

    def validate_protected_surfaces(self, changed_files: list[str] | None = None) -> None:
        """Check if any protected surfaces are in the changed files list."""
        if not changed_files:
            return

        protected = self.matrix.get("protected_surfaces", [])

        for surface in protected:
            pattern = surface.get("path", "")
            owner = surface.get("owner", "")
            reason = surface.get("reason", "")

            # Convert glob patterns to regex
            regex_pattern = pattern.replace("**", ".*").replace("*", "[^/]*")
            regex = re.compile(regex_pattern)

            for changed_file in changed_files:
                # Normalize path separators
                normalized = changed_file.replace("\\", "/")
                if regex.match(normalized) or regex.search(normalized):
                    self.results.append(ValidationResult(
                        passed=True,  # Info, not failure
                        source=f"protected:{pattern}",
                        expected="Overseer review required",
                        actual=changed_file,
                        message=f"Protected surface modified: {pattern} (Owner: {owner}). Reason: {reason}"
                    ))

    def run_local_checks(self) -> bool:
        """Run quick local checks suitable for pre-commit."""
        if not self.load_matrix():
            return False

        self.validate_global_json()
        self.validate_version_lock()
        self.validate_directory_build_props()

        return all(r.passed for r in self.results)

    def run_full_checks(self) -> bool:
        """Run all validation checks."""
        if not self.load_matrix():
            return False

        self.validate_global_json()
        self.validate_version_lock()
        self.validate_directory_build_props()
        # Additional checks can be added here for Phase 3

        return all(r.passed for r in self.results)

    def get_report(self) -> dict[str, Any]:
        """Generate a validation report."""
        passed = sum(1 for r in self.results if r.passed)
        failed = sum(1 for r in self.results if not r.passed)

        return {
            "matrix_version": self.matrix.get("version", "unknown"),
            "matrix_updated": self.matrix.get("last_updated", "unknown"),
            "passed": failed == 0,
            "summary": {
                "total": len(self.results),
                "passed": passed,
                "failed": failed
            },
            "results": [
                {
                    "passed": r.passed,
                    "source": r.source,
                    "expected": r.expected,
                    "actual": r.actual,
                    "message": r.message
                }
                for r in self.results
            ]
        }

    def print_report(self) -> None:
        """Print human-readable validation report."""
        report = self.get_report()

        print("\n" + "=" * 60)
        print("COMPATIBILITY MATRIX VALIDATION")
        print("=" * 60)
        print(f"Matrix Version: {report['matrix_version']}")
        print(f"Last Updated: {report['matrix_updated']}")
        print("-" * 60)

        for result in self.results:
            status = "[PASS]" if result.passed else "[FAIL]"
            print(f"\n{status}: {result.source}")
            if self.verbose or not result.passed:
                print(f"  Expected: {result.expected}")
                print(f"  Actual:   {result.actual}")
                print(f"  Message:  {result.message}")

        print("\n" + "-" * 60)
        print(f"Summary: {report['summary']['passed']}/{report['summary']['total']} passed")

        if report['passed']:
            print("\n[OK] All compatibility checks PASSED")
        else:
            print(f"\n[ERROR] {report['summary']['failed']} check(s) FAILED")
        print("=" * 60 + "\n")

    def list_pins(self) -> None:
        """List all version pins from the matrix."""
        if not self.load_matrix():
            return

        print("\n" + "=" * 60)
        print("COMPATIBILITY MATRIX VERSION PINS")
        print("=" * 60)

        # Platform
        print("\nPLATFORM:")
        platform = self.matrix.get("platform", {})
        for key, value in platform.items():
            print(f"  {key}: {value}")

        # Python dependencies
        print("\nPYTHON DEPENDENCIES:")
        python_deps = self.matrix.get("dependencies", {}).get("python", {})
        for name, info in python_deps.items():
            version = info.get("version", "unknown")
            locked = " [LOCKED]" if info.get("locked") else ""
            tech_debt = f" [TD: {info['tech_debt']}]" if info.get("tech_debt") else ""
            print(f"  {name}: {version}{locked}{tech_debt}")

        # .NET dependencies
        print("\n.NET DEPENDENCIES:")
        dotnet_deps = self.matrix.get("dependencies", {}).get("dotnet", {})
        for name, info in dotnet_deps.items():
            version = info.get("version", "unknown")
            print(f"  {name}: {version}")

        print("\n" + "=" * 60)


def find_project_root() -> Path:
    """Find the VoiceStudio project root directory."""
    # Start from script location
    current = Path(__file__).resolve().parent

    # Walk up looking for markers
    markers = ["VoiceStudio.sln", "global.json", ".cursor"]

    for _ in range(10):  # Max 10 levels up
        for marker in markers:
            if (current / marker).exists():
                return current
        current = current.parent

    # Fallback to current working directory
    return Path.cwd()


def main() -> int:
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description="Validate project files against the compatibility matrix"
    )
    parser.add_argument(
        "--local", action="store_true",
        help="Run local/pre-commit mode (quick checks only)"
    )
    parser.add_argument(
        "--json", action="store_true",
        help="Output results as JSON"
    )
    parser.add_argument(
        "--verbose", "-v", action="store_true",
        help="Show detailed validation output"
    )
    parser.add_argument(
        "--dry-run", action="store_true",
        help="Show what would be checked without running"
    )
    parser.add_argument(
        "--list-pins", action="store_true",
        help="List all version pins from the matrix"
    )

    args = parser.parse_args()

    project_root = find_project_root()
    validator = MatrixValidator(project_root, verbose=args.verbose)

    if args.list_pins:
        validator.list_pins()
        return 0

    if args.dry_run:
        print("Dry run mode - would check:")
        print("  - global.json (SDK version)")
        print("  - version_lock.json (Python dependencies)")
        print("  - Directory.Build.props (.NET dependencies)")
        return 0

    # Run validations
    passed = validator.run_local_checks() if args.local else validator.run_full_checks()

    # Output results
    if args.json:
        print(json.dumps(validator.get_report(), indent=2))
    else:
        validator.print_report()

    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
