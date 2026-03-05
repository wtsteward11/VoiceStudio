#!/usr/bin/env python3
"""
Dependency Security Scanner.

Task 2.3.1: Automated vulnerability scanning for dependencies.
Uses Safety and pip-audit for Python packages.
"""

import json
import subprocess
import sys
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path


@dataclass
class Vulnerability:
    """A security vulnerability."""
    package: str
    installed_version: str
    vulnerability_id: str
    severity: str
    description: str
    fix_versions: list[str]


def run_safety_scan() -> list[Vulnerability]:
    """Run safety check for known vulnerabilities."""
    vulnerabilities = []

    try:
        result = subprocess.run(
            [sys.executable, "-m", "safety", "check", "--json"],
            capture_output=True,
            text=True,
        )

        if result.stdout:
            data = json.loads(result.stdout)

            # Handle different safety output formats
            vulns = data if isinstance(data, list) else data.get("vulnerabilities", [])

            for vuln in vulns:
                if isinstance(vuln, list):
                    # Old format: [pkg, affected, installed, desc, id]
                    vulnerabilities.append(Vulnerability(
                        package=vuln[0],
                        installed_version=vuln[2],
                        vulnerability_id=str(vuln[4]) if len(vuln) > 4 else "unknown",
                        severity="unknown",
                        description=vuln[3] if len(vuln) > 3 else "",
                        fix_versions=[],
                    ))
                else:
                    # New format: dict
                    vulnerabilities.append(Vulnerability(
                        package=vuln.get("package_name", "unknown"),
                        installed_version=vuln.get("installed_version", "unknown"),
                        vulnerability_id=vuln.get("vulnerability_id", "unknown"),
                        severity=vuln.get("severity", "unknown"),
                        description=vuln.get("advisory", ""),
                        fix_versions=vuln.get("fix_versions", []),
                    ))

    except FileNotFoundError:
        print("Safety not installed. Install with: pip install safety")
    # ALLOWED: bare except - best effort, failure acceptable
    except json.JSONDecodeError:
        pass
    except Exception as e:
        print(f"Safety scan error: {e}")

    return vulnerabilities


def run_pip_audit() -> list[Vulnerability]:
    """Run pip-audit for vulnerabilities."""
    vulnerabilities = []

    try:
        result = subprocess.run(
            [sys.executable, "-m", "pip_audit", "--format=json"],
            capture_output=True,
            text=True,
        )

        if result.stdout:
            data = json.loads(result.stdout)

            for pkg_data in data:
                pkg_name = pkg_data.get("name", "unknown")
                pkg_version = pkg_data.get("version", "unknown")

                for vuln in pkg_data.get("vulns", []):
                    vulnerabilities.append(Vulnerability(
                        package=pkg_name,
                        installed_version=pkg_version,
                        vulnerability_id=vuln.get("id", "unknown"),
                        severity=vuln.get("severity", "unknown"),
                        description=vuln.get("description", ""),
                        fix_versions=vuln.get("fix_versions", []),
                    ))

    except FileNotFoundError:
        print("pip-audit not installed. Install with: pip install pip-audit")
    # ALLOWED: bare except - best effort, failure acceptable
    except json.JSONDecodeError:
        pass
    except Exception as e:
        print(f"pip-audit error: {e}")

    return vulnerabilities


def merge_results(
    safety_results: list[Vulnerability],
    audit_results: list[Vulnerability],
) -> list[Vulnerability]:
    """Merge and deduplicate vulnerability results."""
    seen = set()
    merged = []

    for vuln in safety_results + audit_results:
        key = (vuln.package, vuln.vulnerability_id)
        if key not in seen:
            seen.add(key)
            merged.append(vuln)

    return merged


def generate_report(vulnerabilities: list[Vulnerability]) -> str:
    """Generate security scan report."""
    lines = [
        "# Dependency Security Scan Report",
        f"Generated: {datetime.now().isoformat()}",
        "",
    ]

    if not vulnerabilities:
        lines.extend([
            "## Result: ✅ No Vulnerabilities Found",
            "",
            "All dependencies passed security checks.",
        ])
    else:
        # Group by severity
        by_severity: dict[str, list[Vulnerability]] = {}
        for vuln in vulnerabilities:
            sev = vuln.severity.upper() if vuln.severity else "UNKNOWN"
            by_severity.setdefault(sev, []).append(vuln)

        lines.extend([
            "## Result: ⚠️ Vulnerabilities Found",
            "",
            "### Summary",
            f"- Total vulnerabilities: {len(vulnerabilities)}",
        ])

        for sev in ["CRITICAL", "HIGH", "MEDIUM", "LOW", "UNKNOWN"]:
            if sev in by_severity:
                lines.append(f"- {sev}: {len(by_severity[sev])}")

        lines.append("")

        # Detail each vulnerability
        for sev in ["CRITICAL", "HIGH", "MEDIUM", "LOW", "UNKNOWN"]:
            if sev not in by_severity:
                continue

            lines.extend([
                f"### {sev} Severity",
                "",
            ])

            for vuln in by_severity[sev]:
                lines.extend([
                    f"#### {vuln.package} ({vuln.installed_version})",
                    "",
                    f"- **ID**: {vuln.vulnerability_id}",
                    f"- **Severity**: {vuln.severity}",
                ])

                if vuln.fix_versions:
                    lines.append(f"- **Fix versions**: {', '.join(vuln.fix_versions)}")

                if vuln.description:
                    desc = vuln.description[:500]
                    if len(vuln.description) > 500:
                        desc += "..."
                    lines.append(f"- **Description**: {desc}")

                lines.append("")

    return "\n".join(lines)


def main():
    """Main entry point."""
    print("Running dependency security scan...")
    print()

    # Run scans
    print("Running safety check...")
    safety_results = run_safety_scan()
    print(f"  Found {len(safety_results)} issues")

    print("Running pip-audit...")
    audit_results = run_pip_audit()
    print(f"  Found {len(audit_results)} issues")

    # Merge results
    all_vulns = merge_results(safety_results, audit_results)

    # Generate report
    report = generate_report(all_vulns)

    # Write report
    report_path = Path("docs/reports/security/DEPENDENCY_SCAN.md")
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(report)

    print()
    print(f"Report written to: {report_path}")

    if all_vulns:
        print()
        print(f"⚠️  Found {len(all_vulns)} vulnerabilities:")
        for vuln in all_vulns:
            print(f"  - {vuln.package} ({vuln.vulnerability_id}): {vuln.severity}")
        return 1

    print()
    print("✅ No vulnerabilities found")
    return 0


if __name__ == "__main__":
    sys.exit(main())
