#!/usr/bin/env python3
"""
VoiceStudio Validation Report Generator.

Generates a comprehensive validation report with evidence from test runs.
Consolidates results from:
- Pytest test results
- Workflow traces
- API coverage logs
- Panel/Engine matrix reports
- Screenshots

Usage:
    python scripts/generate_validation_report.py
    python scripts/generate_validation_report.py --output-dir .buildlogs/validation
    python scripts/generate_validation_report.py --format html
"""

import argparse
import json
import sys
from datetime import datetime
from pathlib import Path
from typing import Any


class ValidationReportGenerator:
    """Generates comprehensive validation reports."""

    def __init__(self, output_dir: Path):
        self.output_dir = output_dir
        self.timestamp = datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
        self.results: dict[str, Any] = {
            "timestamp": self.timestamp,
            "categories": {},
            "api_coverage": {},
            "matrix_reports": {},
            "screenshots": [],
            "traces": [],
            "summary": {},
        }

    def collect_test_results(self) -> None:
        """Collect pytest test results."""
        print("Collecting test results...")

        reports_dir = self.output_dir / "reports"
        if not reports_dir.exists():
            print("  No reports directory found")
            return

        html_reports = list(reports_dir.glob("*.html"))
        json_reports = list(reports_dir.glob("*.json"))

        print(f"  Found {len(html_reports)} HTML reports")
        print(f"  Found {len(json_reports)} JSON reports")

        for json_file in json_reports:
            try:
                with open(json_file, encoding="utf-8") as f:
                    data = json.load(f)
                    category = json_file.stem.replace(f"_{self.timestamp}", "")
                    self.results["categories"][category] = data
            except Exception as e:
                print(f"  Error reading {json_file}: {e}")

    def collect_api_coverage(self) -> None:
        """Collect API coverage data."""
        print("Collecting API coverage...")

        api_dir = self.output_dir / "api_coverage"
        if not api_dir.exists():
            print("  No API coverage directory found")
            return

        api_files = list(api_dir.glob("*.json"))
        print(f"  Found {len(api_files)} API coverage files")

        all_calls = []
        endpoints_hit = set()

        for api_file in api_files:
            try:
                with open(api_file, encoding="utf-8") as f:
                    data = json.load(f)
                    if isinstance(data, list):
                        for call in data:
                            all_calls.append(call)
                            if isinstance(call, dict) and "endpoint" in call:
                                endpoints_hit.add(call["endpoint"])
                    elif isinstance(data, dict) and "calls" in data:
                        for call in data["calls"]:
                            all_calls.append(call)
                            if isinstance(call, dict) and "endpoint" in call:
                                endpoints_hit.add(call["endpoint"])
            except Exception as e:
                print(f"  Error reading {api_file}: {e}")

        self.results["api_coverage"] = {
            "total_calls": len(all_calls),
            "unique_endpoints": len(endpoints_hit),
            "endpoints": list(endpoints_hit),
            "calls": all_calls[:100],  # Limit to first 100 for report
        }

        print(f"  Total API calls: {len(all_calls)}")
        print(f"  Unique endpoints: {len(endpoints_hit)}")

    def collect_matrix_reports(self) -> None:
        """Collect panel and engine matrix reports."""
        print("Collecting matrix reports...")

        matrix_files = [
            "panel_matrix_report.txt",
            "engine_matrix_report.txt",
            "advanced_features_report.txt",
        ]

        for matrix_file in matrix_files:
            matrix_path = self.output_dir / matrix_file
            if matrix_path.exists():
                try:
                    with open(matrix_path, encoding="utf-8") as f:
                        content = f.read()
                        self.results["matrix_reports"][matrix_file] = content
                        print(f"  Found: {matrix_file}")
                except Exception as e:
                    print(f"  Error reading {matrix_file}: {e}")

    def collect_screenshots(self) -> None:
        """Collect screenshot evidence."""
        print("Collecting screenshots...")

        screenshot_dir = self.output_dir / "screenshots"
        if not screenshot_dir.exists():
            print("  No screenshots directory found")
            return

        screenshots = list(screenshot_dir.glob("*.png")) + list(screenshot_dir.glob("*.jpg"))
        print(f"  Found {len(screenshots)} screenshots")

        for screenshot in screenshots:
            self.results["screenshots"].append({
                "filename": screenshot.name,
                "path": str(screenshot),
                "size": screenshot.stat().st_size,
            })

    def collect_traces(self) -> None:
        """Collect workflow traces."""
        print("Collecting workflow traces...")

        trace_files = list(self.output_dir.glob("*_trace.json"))
        print(f"  Found {len(trace_files)} trace files")

        for trace_file in trace_files:
            try:
                with open(trace_file, encoding="utf-8") as f:
                    data = json.load(f)
                    self.results["traces"].append({
                        "name": trace_file.stem,
                        "data": data,
                    })
            except Exception as e:
                print(f"  Error reading {trace_file}: {e}")

    def calculate_summary(self) -> None:
        """Calculate summary statistics."""
        print("Calculating summary...")

        total_tests = 0
        passed_tests = 0
        failed_tests = 0
        skipped_tests = 0

        # Try to parse test results from traces
        for trace in self.results["traces"]:
            data = trace.get("data", {})
            if "steps" in data:
                total_tests += len(data["steps"])
                for step in data["steps"]:
                    status = step.get("status", "")
                    if status == "success":
                        passed_tests += 1
                    elif status == "failure":
                        failed_tests += 1
                    else:
                        skipped_tests += 1

        self.results["summary"] = {
            "total_tests": total_tests,
            "passed": passed_tests,
            "failed": failed_tests,
            "skipped": skipped_tests,
            "pass_rate": (passed_tests / total_tests * 100) if total_tests > 0 else 0,
            "api_endpoints_tested": len(self.results["api_coverage"].get("endpoints", [])),
            "screenshots_captured": len(self.results["screenshots"]),
            "trace_files": len(self.results["traces"]),
            "matrix_reports": len(self.results["matrix_reports"]),
        }

        print(f"  Tests: {total_tests} total, {passed_tests} passed, {failed_tests} failed")

    def generate_text_report(self) -> Path:
        """Generate plain text report."""
        report_path = self.output_dir / f"VALIDATION_REPORT_{self.timestamp}.txt"

        lines = [
            "=" * 80,
            "                    VOICESTUDIO VALIDATION REPORT",
            "=" * 80,
            "",
            f"Generated: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}",
            f"Output Directory: {self.output_dir}",
            "",
            "-" * 80,
            "SUMMARY",
            "-" * 80,
        ]

        summary = self.results["summary"]
        lines.extend([
            f"Total Tests: {summary.get('total_tests', 'N/A')}",
            f"Passed: {summary.get('passed', 'N/A')}",
            f"Failed: {summary.get('failed', 'N/A')}",
            f"Skipped: {summary.get('skipped', 'N/A')}",
            f"Pass Rate: {summary.get('pass_rate', 0):.1f}%",
            f"API Endpoints Tested: {summary.get('api_endpoints_tested', 0)}",
            f"Screenshots Captured: {summary.get('screenshots_captured', 0)}",
            "",
        ])

        # API Coverage
        lines.extend([
            "-" * 80,
            "API COVERAGE",
            "-" * 80,
        ])

        api_coverage = self.results["api_coverage"]
        lines.append(f"Total API Calls: {api_coverage.get('total_calls', 0)}")
        lines.append(f"Unique Endpoints: {api_coverage.get('unique_endpoints', 0)}")
        lines.append("")
        lines.append("Endpoints Tested:")
        for endpoint in sorted(api_coverage.get("endpoints", []))[:50]:
            lines.append(f"  - {endpoint}")
        if len(api_coverage.get("endpoints", [])) > 50:
            lines.append(f"  ... and {len(api_coverage['endpoints']) - 50} more")
        lines.append("")

        # Matrix Reports
        for matrix_name, matrix_content in self.results["matrix_reports"].items():
            lines.extend([
                "-" * 80,
                f"MATRIX REPORT: {matrix_name}",
                "-" * 80,
                matrix_content,
                "",
            ])

        # Screenshots
        if self.results["screenshots"]:
            lines.extend([
                "-" * 80,
                "SCREENSHOTS",
                "-" * 80,
            ])
            for screenshot in self.results["screenshots"][:20]:
                lines.append(f"  - {screenshot['filename']}")
            if len(self.results["screenshots"]) > 20:
                lines.append(f"  ... and {len(self.results['screenshots']) - 20} more")
            lines.append("")

        # Footer
        lines.extend([
            "=" * 80,
            "                          END OF REPORT",
            "=" * 80,
        ])

        with open(report_path, "w", encoding="utf-8") as f:
            f.write("\n".join(lines))

        return report_path

    def generate_html_report(self) -> Path:
        """Generate HTML report."""
        report_path = self.output_dir / f"VALIDATION_REPORT_{self.timestamp}.html"

        summary = self.results["summary"]
        pass_rate = summary.get("pass_rate", 0)
        status_color = "#28a745" if pass_rate >= 80 else "#ffc107" if pass_rate >= 50 else "#dc3545"

        html = f"""<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>VoiceStudio Validation Report</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            max-width: 1200px;
            margin: 0 auto;
            padding: 20px;
            background: #f5f5f5;
        }}
        h1, h2, h3 {{ color: #333; }}
        .header {{
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 30px;
            border-radius: 10px;
            margin-bottom: 20px;
        }}
        .summary-cards {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 20px;
            margin-bottom: 30px;
        }}
        .card {{
            background: white;
            padding: 20px;
            border-radius: 10px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }}
        .card h3 {{ margin-top: 0; color: #666; font-size: 14px; }}
        .card .value {{ font-size: 32px; font-weight: bold; color: #333; }}
        .pass-rate {{ color: {status_color}; }}
        .section {{
            background: white;
            padding: 20px;
            border-radius: 10px;
            margin-bottom: 20px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }}
        .endpoint-list {{
            max-height: 300px;
            overflow-y: auto;
            font-family: monospace;
            font-size: 12px;
            background: #f8f9fa;
            padding: 10px;
            border-radius: 5px;
        }}
        .endpoint {{ padding: 2px 0; }}
        pre {{
            background: #f8f9fa;
            padding: 15px;
            border-radius: 5px;
            overflow-x: auto;
            font-size: 12px;
        }}
        .screenshot-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
            gap: 10px;
        }}
        .screenshot-item {{
            background: #f8f9fa;
            padding: 10px;
            border-radius: 5px;
            font-size: 11px;
            word-break: break-all;
        }}
        .timestamp {{ color: #666; font-size: 14px; }}
    </style>
</head>
<body>
    <div class="header">
        <h1>VoiceStudio Validation Report</h1>
        <p class="timestamp">Generated: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}</p>
    </div>

    <div class="summary-cards">
        <div class="card">
            <h3>Total Tests</h3>
            <div class="value">{summary.get('total_tests', 0)}</div>
        </div>
        <div class="card">
            <h3>Passed</h3>
            <div class="value" style="color: #28a745;">{summary.get('passed', 0)}</div>
        </div>
        <div class="card">
            <h3>Failed</h3>
            <div class="value" style="color: #dc3545;">{summary.get('failed', 0)}</div>
        </div>
        <div class="card">
            <h3>Pass Rate</h3>
            <div class="value pass-rate">{pass_rate:.1f}%</div>
        </div>
        <div class="card">
            <h3>API Endpoints</h3>
            <div class="value">{summary.get('api_endpoints_tested', 0)}</div>
        </div>
        <div class="card">
            <h3>Screenshots</h3>
            <div class="value">{summary.get('screenshots_captured', 0)}</div>
        </div>
    </div>

    <div class="section">
        <h2>API Coverage</h2>
        <p>Total calls: {self.results['api_coverage'].get('total_calls', 0)} |
           Unique endpoints: {self.results['api_coverage'].get('unique_endpoints', 0)}</p>
        <div class="endpoint-list">
"""

        for endpoint in sorted(self.results["api_coverage"].get("endpoints", [])):
            html += f'            <div class="endpoint">{endpoint}</div>\n'

        html += """        </div>
    </div>
"""

        # Matrix reports
        for matrix_name, matrix_content in self.results["matrix_reports"].items():
            html += f"""
    <div class="section">
        <h2>{matrix_name}</h2>
        <pre>{matrix_content}</pre>
    </div>
"""

        # Screenshots
        if self.results["screenshots"]:
            html += """
    <div class="section">
        <h2>Screenshots</h2>
        <div class="screenshot-grid">
"""
            for screenshot in self.results["screenshots"]:
                html += f'            <div class="screenshot-item">{screenshot["filename"]}</div>\n'

            html += """        </div>
    </div>
"""

        html += """
</body>
</html>"""

        with open(report_path, "w", encoding="utf-8") as f:
            f.write(html)

        return report_path

    def generate_json_report(self) -> Path:
        """Generate JSON report."""
        report_path = self.output_dir / f"VALIDATION_REPORT_{self.timestamp}.json"

        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(self.results, f, indent=2, default=str)

        return report_path

    def generate(self, format: str = "all") -> dict[str, Path]:
        """Generate validation reports."""
        print("\n" + "=" * 60)
        print("VoiceStudio Validation Report Generator")
        print("=" * 60 + "\n")

        # Collect all data
        self.collect_test_results()
        self.collect_api_coverage()
        self.collect_matrix_reports()
        self.collect_screenshots()
        self.collect_traces()
        self.calculate_summary()

        # Generate reports
        reports = {}

        print("\nGenerating reports...")

        if format in ["all", "text"]:
            reports["text"] = self.generate_text_report()
            print(f"  Text report: {reports['text']}")

        if format in ["all", "html"]:
            reports["html"] = self.generate_html_report()
            print(f"  HTML report: {reports['html']}")

        if format in ["all", "json"]:
            reports["json"] = self.generate_json_report()
            print(f"  JSON report: {reports['json']}")

        print("\n" + "=" * 60)
        print("Report generation complete!")
        print("=" * 60 + "\n")

        return reports


def main():
    parser = argparse.ArgumentParser(description="Generate VoiceStudio validation report")
    parser.add_argument(
        "--output-dir",
        type=str,
        default=".buildlogs/validation",
        help="Output directory containing test results"
    )
    parser.add_argument(
        "--format",
        type=str,
        choices=["all", "text", "html", "json"],
        default="all",
        help="Report format to generate"
    )

    args = parser.parse_args()

    # Determine project root
    script_dir = Path(__file__).parent
    project_root = script_dir.parent

    output_dir = Path(args.output_dir)
    if not output_dir.is_absolute():
        output_dir = project_root / output_dir

    if not output_dir.exists():
        print(f"Error: Output directory does not exist: {output_dir}")
        print("Run validation tests first using: .\\scripts\\run_validation.ps1")
        sys.exit(1)

    generator = ValidationReportGenerator(output_dir)
    generator.generate(args.format)

    # Print summary
    summary = generator.results["summary"]
    print("Summary:")
    print(f"  Total Tests: {summary.get('total_tests', 'N/A')}")
    print(f"  Pass Rate: {summary.get('pass_rate', 0):.1f}%")
    print(f"  API Endpoints: {summary.get('api_endpoints_tested', 0)}")

    return 0 if summary.get("failed", 0) == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
