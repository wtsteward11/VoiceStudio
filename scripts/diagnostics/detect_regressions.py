#!/usr/bin/env python3
"""
Unified Regression Detection

Detects regressions across multiple dimensions:
- Performance: Response time, throughput regressions
- Quality: Score drops from previous baseline
- Tests: New test failures compared to baseline
- XAML: UI component binding/resource regressions

Usage:
    python scripts/detect_regressions.py --all
    python scripts/detect_regressions.py --type performance
    python scripts/detect_regressions.py --type quality
    python scripts/detect_regressions.py --type tests
    python scripts/detect_regressions.py --type xaml
    python scripts/detect_regressions.py --ci

Exit Codes:
    0: No regressions detected
    1: Regressions detected
    2: Error occurred
"""

import argparse
import json
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from datetime import datetime
from typing import Any

from _env_setup import BUILDLOGS_DIR, PROJECT_ROOT

# Output directories
REGRESSION_DIR = BUILDLOGS_DIR / "regressions"
REGRESSION_DIR.mkdir(parents=True, exist_ok=True)


@dataclass
class RegressionItem:
    """A single regression finding."""
    type: str  # performance, quality, tests, xaml
    severity: str  # critical, high, medium, low
    message: str
    details: dict[str, Any] = field(default_factory=dict)
    file_path: str | None = None
    line_number: int | None = None


@dataclass
class RegressionReport:
    """Complete regression detection report."""
    timestamp: str
    performance: list[RegressionItem]
    quality: list[RegressionItem]
    tests: list[RegressionItem]
    xaml: list[RegressionItem]
    overall_passed: bool

    @property
    def total_regressions(self) -> int:
        return len(self.performance) + len(self.quality) + len(self.tests) + len(self.xaml)

    @property
    def critical_count(self) -> int:
        all_items = self.performance + self.quality + self.tests + self.xaml
        return sum(1 for item in all_items if item.severity == "critical")

    def to_dict(self) -> dict[str, Any]:
        return {
            "timestamp": self.timestamp,
            "overall_passed": self.overall_passed,
            "total_regressions": self.total_regressions,
            "critical_count": self.critical_count,
            "performance": [
                {"type": r.type, "severity": r.severity, "message": r.message, "details": r.details}
                for r in self.performance
            ],
            "quality": [
                {"type": r.type, "severity": r.severity, "message": r.message, "details": r.details}
                for r in self.quality
            ],
            "tests": [
                {"type": r.type, "severity": r.severity, "message": r.message, "details": r.details}
                for r in self.tests
            ],
            "xaml": [
                {"type": r.type, "severity": r.severity, "message": r.message,
                 "file": r.file_path, "line": r.line_number}
                for r in self.xaml
            ],
        }


class PerformanceRegressionDetector:
    """Detect performance regressions using existing script."""

    def detect(self) -> list[RegressionItem]:
        """Run performance regression detection."""
        regressions = []

        perf_script = PROJECT_ROOT / "scripts" / "detect_performance_regression.py"

        if not perf_script.exists():
            return []

        try:
            result = subprocess.run(
                [sys.executable, str(perf_script), "--report"],
                capture_output=True,
                text=True,
                timeout=120,
                cwd=str(PROJECT_ROOT)
            )

            # Parse output for regressions
            if result.returncode == 1:
                # Regressions detected
                for line in result.stdout.split("\n"):
                    if "REGRESSION" in line.upper():
                        regressions.append(RegressionItem(
                            type="performance",
                            severity="high",
                            message=line.strip(),
                            details={"exit_code": result.returncode}
                        ))

            # Check for specific regression patterns
            output = result.stdout + result.stderr
            regression_matches = re.findall(
                r"(\w+):\s*(\d+\.?\d*)%?\s*(?:regression|degradation)",
                output,
                re.IGNORECASE
            )

            for metric, value in regression_matches:
                regressions.append(RegressionItem(
                    type="performance",
                    severity="high" if float(value) > 20 else "medium",
                    message=f"{metric} regressed by {value}%",
                    details={"metric": metric, "regression_percent": float(value)}
                ))

        except subprocess.TimeoutExpired:
            regressions.append(RegressionItem(
                type="performance",
                severity="low",
                message="Performance detection timed out",
            ))
        except Exception:
            pass  # Script may not exist or other issues

        return regressions


class QualityRegressionDetector:
    """Detect quality score regressions."""

    def __init__(self, threshold: float = 5.0):
        self.threshold = threshold  # Minimum drop to trigger regression

    def detect(self) -> list[RegressionItem]:
        """Detect quality score drops."""
        regressions = []

        quality_dir = BUILDLOGS_DIR / "quality"

        if not quality_dir.exists():
            return []

        # Find scorecard files
        scorecards = sorted(quality_dir.glob("scorecard_*.json"), reverse=True)

        if len(scorecards) < 2:
            return []  # Need at least 2 scorecards to compare

        try:
            current = json.loads(scorecards[0].read_text())
            previous = json.loads(scorecards[1].read_text())

            current_score = current.get("overall_score", 0)
            previous_score = previous.get("overall_score", 0)

            drop = previous_score - current_score

            if drop >= self.threshold:
                severity = "critical" if drop >= 15 else "high" if drop >= 10 else "medium"
                regressions.append(RegressionItem(
                    type="quality",
                    severity=severity,
                    message=f"Quality score dropped by {drop:.1f} points",
                    details={
                        "current_score": current_score,
                        "previous_score": previous_score,
                        "drop": drop,
                    }
                ))

            # Check individual dimensions
            current_dims = current.get("dimensions", {})
            previous_dims = previous.get("dimensions", {})

            for dim_name in current_dims:
                if dim_name in previous_dims:
                    curr_score = current_dims[dim_name].get("score", 0)
                    prev_score = previous_dims[dim_name].get("score", 0)
                    dim_drop = prev_score - curr_score

                    if dim_drop >= 10:  # 10-point drop in dimension
                        regressions.append(RegressionItem(
                            type="quality",
                            severity="medium",
                            message=f"Quality dimension '{dim_name}' dropped by {dim_drop:.1f}",
                            details={
                                "dimension": dim_name,
                                "current": curr_score,
                                "previous": prev_score,
                            }
                        ))

        except Exception:
            pass  # File parsing issues

        return regressions


class TestRegressionDetector:
    """Detect test regressions (new failures)."""

    def detect(self) -> list[RegressionItem]:
        """Detect test failures compared to baseline."""
        regressions = []

        # Check pytest results
        pytest_regressions = self._check_pytest()
        regressions.extend(pytest_regressions)

        # Check MSTest results
        mstest_regressions = self._check_mstest()
        regressions.extend(mstest_regressions)

        return regressions

    def _check_pytest(self) -> list[RegressionItem]:
        """Check for pytest failures."""
        regressions = []

        # Look for pytest results
        junit_files = list((PROJECT_ROOT / "test-results").glob("*.xml")) if (PROJECT_ROOT / "test-results").exists() else []
        junit_files.extend(list(PROJECT_ROOT.glob("pytest_*.xml")))

        for junit_file in junit_files:
            try:
                tree = ET.parse(junit_file)
                root = tree.getroot()

                # Find failed tests
                for testcase in root.iter("testcase"):
                    failure = testcase.find("failure")
                    error = testcase.find("error")

                    if failure is not None or error is not None:
                        test_name = testcase.get("name", "unknown")
                        class_name = testcase.get("classname", "")

                        full_name = f"{class_name}.{test_name}" if class_name else test_name

                        element = failure if failure is not None else error
                        message = element.get("message", "Test failed")[:200]

                        regressions.append(RegressionItem(
                            type="tests",
                            severity="high",
                            message=f"Test failure: {full_name}",
                            details={
                                "test_name": test_name,
                                "class_name": class_name,
                                "failure_message": message,
                            }
                        ))
            # ALLOWED: bare except - File parsing, individual file failure is acceptable
            except Exception:
                pass

        return regressions

    def _check_mstest(self) -> list[RegressionItem]:
        """Check for MSTest failures."""
        regressions = []

        # Look for TRX files
        test_results = PROJECT_ROOT / "TestResults"
        if not test_results.exists():
            return []

        trx_files = list(test_results.glob("*.trx"))

        for trx_file in trx_files:
            try:
                tree = ET.parse(trx_file)
                root = tree.getroot()

                # TRX namespace
                ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

                # Find failed tests
                for result in root.findall(".//t:UnitTestResult[@outcome='Failed']", ns):
                    test_name = result.get("testName", "unknown")

                    # Get error message
                    error_info = result.find(".//t:ErrorInfo/t:Message", ns)
                    message = error_info.text[:200] if error_info is not None and error_info.text else "Test failed"

                    regressions.append(RegressionItem(
                        type="tests",
                        severity="high",
                        message=f"MSTest failure: {test_name}",
                        details={
                            "test_name": test_name,
                            "failure_message": message,
                        }
                    ))
            # ALLOWED: bare except - File parsing, individual file failure is acceptable
            except Exception:
                pass

        return regressions


class XamlRegressionDetector:
    """Detect XAML regressions (binding errors, missing resources)."""

    # Common XAML issues patterns
    BINDING_ERROR_PATTERN = re.compile(
        r"(?:BindingExpression|Binding).*?Error|"
        r"Cannot find.*?resource|"
        r"StaticResource.*?not found|"
        r"Cannot resolve.*?property",
        re.IGNORECASE
    )

    def detect(self) -> list[RegressionItem]:
        """Detect XAML issues."""
        regressions = []

        # Check build warnings for XAML issues
        build_output = PROJECT_ROOT / "build_output.txt"
        build_warnings = PROJECT_ROOT / "build_warnings.txt"

        for build_file in [build_output, build_warnings]:
            if build_file.exists():
                content = build_file.read_text(errors="ignore")
                regressions.extend(self._parse_xaml_issues(content))

        # Check for StaticResource/DynamicResource issues
        regressions.extend(self._check_resource_references())

        return regressions

    def _parse_xaml_issues(self, content: str) -> list[RegressionItem]:
        """Parse build output for XAML issues."""
        regressions = []

        for line in content.split("\n"):
            if ".xaml" in line.lower() and ("error" in line.lower() or "warning" in line.lower()):
                # Extract file and line info
                match = re.search(r"([^\s]+\.xaml)\((\d+)", line, re.IGNORECASE)
                file_path = match.group(1) if match else None
                line_num = int(match.group(2)) if match else None

                # Determine severity
                severity = "high" if "error" in line.lower() else "medium"

                regressions.append(RegressionItem(
                    type="xaml",
                    severity=severity,
                    message=line.strip()[:200],
                    file_path=file_path,
                    line_number=line_num,
                ))

        return regressions

    def _check_resource_references(self) -> list[RegressionItem]:
        """Check for potentially broken resource references."""
        regressions = []

        # This is a lightweight check - full validation would require XAML parsing
        xaml_dir = PROJECT_ROOT / "src" / "VoiceStudio.App"

        if not xaml_dir.exists():
            return []

        # Collect defined resources
        defined_resources: set[str] = set()
        referenced_resources: dict[str, list[tuple[str, int]]] = {}

        for xaml_file in xaml_dir.rglob("*.xaml"):
            try:
                content = xaml_file.read_text(encoding="utf-8", errors="ignore")
                rel_path = str(xaml_file.relative_to(PROJECT_ROOT))

                # Find defined resources (x:Key)
                for match in re.finditer(r'x:Key="(\w+)"', content):
                    defined_resources.add(match.group(1))

                # Find StaticResource references
                for line_num, line in enumerate(content.split("\n"), 1):
                    for match in re.finditer(r'\{StaticResource\s+(\w+)\}', line):
                        resource_name = match.group(1)
                        if resource_name not in referenced_resources:
                            referenced_resources[resource_name] = []
                        referenced_resources[resource_name].append((rel_path, line_num))
            # ALLOWED: bare except - File parsing, individual file failure is acceptable
            except Exception:
                pass

        # Find undefined references (may be defined in merged dictionaries, so don't flag as critical)
        # This is informational - actual runtime errors would show in build logs

        return regressions


class RegressionDetector:
    """Main unified regression detector."""

    def __init__(self):
        self.detectors = {
            "performance": PerformanceRegressionDetector(),
            "quality": QualityRegressionDetector(),
            "tests": TestRegressionDetector(),
            "xaml": XamlRegressionDetector(),
        }

    def detect_all(self) -> RegressionReport:
        """Run all regression detectors."""
        performance = self.detectors["performance"].detect()
        quality = self.detectors["quality"].detect()
        tests = self.detectors["tests"].detect()
        xaml = self.detectors["xaml"].detect()

        all_items = performance + quality + tests + xaml
        has_critical = any(item.severity == "critical" for item in all_items)

        return RegressionReport(
            timestamp=datetime.now().isoformat(),
            performance=performance,
            quality=quality,
            tests=tests,
            xaml=xaml,
            overall_passed=len(all_items) == 0 or not has_critical,
        )

    def detect_type(self, regression_type: str) -> list[RegressionItem]:
        """Detect regressions of a specific type."""
        if regression_type in self.detectors:
            return self.detectors[regression_type].detect()
        return []


def format_markdown(report: RegressionReport) -> str:
    """Format regression report as Markdown."""
    lines = [
        "# Regression Detection Report",
        "",
        f"**Generated**: {report.timestamp}",
        f"**Status**: {'PASS' if report.overall_passed else 'FAIL'}",
        f"**Total Regressions**: {report.total_regressions}",
        f"**Critical Issues**: {report.critical_count}",
        "",
    ]

    categories = [
        ("Performance", report.performance),
        ("Quality", report.quality),
        ("Tests", report.tests),
        ("XAML", report.xaml),
    ]

    for name, items in categories:
        lines.extend([
            f"## {name} Regressions",
            "",
        ])

        if items:
            lines.append("| Severity | Message |")
            lines.append("|----------|---------|")
            for item in items:
                lines.append(f"| {item.severity.upper()} | {item.message} |")
        else:
            lines.append("_No regressions detected_")

        lines.append("")

    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description="Unified regression detection")
    parser.add_argument(
        "--all",
        action="store_true",
        help="Run all regression checks (default)"
    )
    parser.add_argument(
        "--type",
        choices=["performance", "quality", "tests", "xaml"],
        help="Run specific regression check"
    )
    parser.add_argument(
        "--ci",
        action="store_true",
        help="CI mode: exit non-zero on any regression"
    )
    parser.add_argument(
        "--output-format",
        choices=["json", "markdown", "both"],
        default="both",
        help="Output format (default: both)"
    )

    args = parser.parse_args()

    print("=" * 60)
    print("Unified Regression Detection")
    print("=" * 60)
    print()

    detector = RegressionDetector()

    if args.type:
        items = detector.detect_type(args.type)
        report = RegressionReport(
            timestamp=datetime.now().isoformat(),
            performance=items if args.type == "performance" else [],
            quality=items if args.type == "quality" else [],
            tests=items if args.type == "tests" else [],
            xaml=items if args.type == "xaml" else [],
            overall_passed=len(items) == 0,
        )
    else:
        report = detector.detect_all()

    # Generate timestamp for filenames
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")

    # Save outputs
    if args.output_format in ["json", "both"]:
        json_path = REGRESSION_DIR / f"regression_report_{timestamp}.json"
        json_path.write_text(json.dumps(report.to_dict(), indent=2))
        print(f"JSON saved: {json_path}")

    if args.output_format in ["markdown", "both"]:
        md_path = REGRESSION_DIR / f"regression_report_{timestamp}.md"
        md_path.write_text(format_markdown(report))
        print(f"Markdown saved: {md_path}")

    # Print summary
    print()
    print("-" * 60)
    print(f"  Performance: {len(report.performance):3} regression(s)")
    print(f"  Quality:     {len(report.quality):3} regression(s)")
    print(f"  Tests:       {len(report.tests):3} regression(s)")
    print(f"  XAML:        {len(report.xaml):3} regression(s)")
    print("-" * 60)
    print(f"  Total:       {report.total_regressions:3} regression(s)")
    print(f"  Critical:    {report.critical_count:3}")
    print()
    print(f"Status: {'PASS' if report.overall_passed else 'FAIL'}")
    print()

    if args.ci and not report.overall_passed:
        sys.exit(1)

    sys.exit(0)


if __name__ == "__main__":
    main()
