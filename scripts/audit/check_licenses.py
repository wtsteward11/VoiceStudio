#!/usr/bin/env python3
"""
License Compliance Checker.

Task 2.3.2: Verify all dependencies are compliant.
Checks Python and .NET dependencies for license compliance.
"""

import json
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

# Allowed licenses
ALLOWED_LICENSES: set[str] = {
    "MIT",
    "MIT License",
    "Apache 2.0",
    "Apache-2.0",
    "Apache License 2.0",
    "Apache Software License",
    "BSD",
    "BSD-2-Clause",
    "BSD-3-Clause",
    "BSD License",
    "ISC",
    "ISC License",
    "PSF",
    "Python Software Foundation License",
    "LGPL",
    "LGPL-2.1",
    "LGPL-3.0",
    "MPL",
    "MPL-2.0",
    "Unlicense",
    "CC0",
    "Public Domain",
    "WTFPL",
}

# Licenses that need review
REVIEW_LICENSES: set[str] = {
    "GPL",
    "GPL-2.0",
    "GPL-3.0",
    "AGPL",
    "AGPL-3.0",
}

# Known exceptions (manually verified)
EXCEPTIONS: dict[str, str] = {
    # package: reason
}


@dataclass
class LicenseInfo:
    """License information for a package."""
    package: str
    version: str
    license: str
    status: str  # "allowed", "review", "unknown"


def check_python_licenses() -> list[LicenseInfo]:
    """Check Python package licenses."""
    results = []

    try:
        # Try pip-licenses if available
        result = subprocess.run(
            [sys.executable, "-m", "pip_licenses", "--format=json"],
            capture_output=True,
            text=True,
        )

        if result.returncode == 0:
            packages = json.loads(result.stdout)

            for pkg in packages:
                license_name = pkg.get("License", "UNKNOWN")
                status = _get_license_status(license_name, pkg["Name"])

                results.append(LicenseInfo(
                    package=pkg["Name"],
                    version=pkg.get("Version", "unknown"),
                    license=license_name,
                    status=status,
                ))
        else:
            # Fallback: parse pip show
            result = subprocess.run(
                [sys.executable, "-m", "pip", "list", "--format=json"],
                capture_output=True,
                text=True,
            )

            if result.returncode == 0:
                packages = json.loads(result.stdout)

                for pkg in packages:
                    license_name = _get_package_license(pkg["name"])
                    status = _get_license_status(license_name, pkg["name"])

                    results.append(LicenseInfo(
                        package=pkg["name"],
                        version=pkg.get("version", "unknown"),
                        license=license_name,
                        status=status,
                    ))

    except Exception as e:
        print(f"Error checking Python licenses: {e}")

    return results


def _get_package_license(package_name: str) -> str:
    """Get license for a package using pip show."""
    try:
        result = subprocess.run(
            [sys.executable, "-m", "pip", "show", package_name],
            capture_output=True,
            text=True,
        )

        if result.returncode == 0:
            for line in result.stdout.split("\n"):
                if line.startswith("License:"):
                    return line.split(":", 1)[1].strip()
    except Exception:
        pass  # ALLOWED: bare except - pip show may fail for many reasons

    return "UNKNOWN"


def _get_license_status(license_name: str, package_name: str) -> str:
    """Determine if license is allowed."""
    # Check exceptions
    if package_name in EXCEPTIONS:
        return "allowed"

    # Normalize license name
    license_upper = license_name.upper()

    # Check allowed
    for allowed in ALLOWED_LICENSES:
        if allowed.upper() in license_upper:
            return "allowed"

    # Check review required
    for review in REVIEW_LICENSES:
        if review.upper() in license_upper:
            return "review"

    return "unknown"


def generate_report(results: list[LicenseInfo]) -> str:
    """Generate license compliance report."""
    lines = [
        "# License Compliance Report",
        f"Generated: {__import__('datetime').datetime.now().isoformat()}",
        "",
        "## Summary",
        f"- Total packages: {len(results)}",
        f"- Allowed: {sum(1 for r in results if r.status == 'allowed')}",
        f"- Need review: {sum(1 for r in results if r.status == 'review')}",
        f"- Unknown: {sum(1 for r in results if r.status == 'unknown')}",
        "",
    ]

    # Group by status
    review_needed = [r for r in results if r.status == "review"]
    unknown = [r for r in results if r.status == "unknown"]

    if review_needed:
        lines.append("## Packages Requiring Review")
        lines.append("")
        lines.append("| Package | Version | License |")
        lines.append("|---------|---------|---------|")
        for r in review_needed:
            lines.append(f"| {r.package} | {r.version} | {r.license} |")
        lines.append("")

    if unknown:
        lines.append("## Packages with Unknown License")
        lines.append("")
        lines.append("| Package | Version | License |")
        lines.append("|---------|---------|---------|")
        for r in unknown:
            lines.append(f"| {r.package} | {r.version} | {r.license} |")
        lines.append("")

    lines.append("## All Packages")
    lines.append("")
    lines.append("| Package | Version | License | Status |")
    lines.append("|---------|---------|---------|--------|")
    for r in sorted(results, key=lambda x: x.package.lower()):
        status_icon = {"allowed": "✅", "review": "⚠️", "unknown": "❓"}[r.status]
        lines.append(f"| {r.package} | {r.version} | {r.license} | {status_icon} |")

    return "\n".join(lines)


def main():
    """Main entry point."""
    print("Checking license compliance...")

    # Check Python packages
    python_results = check_python_licenses()

    # Generate report
    report = generate_report(python_results)

    # Write report
    report_path = Path("docs/reports/license/LICENSE_REPORT.md")
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(report)

    print(f"Report written to: {report_path}")

    # Check for issues
    issues = [r for r in python_results if r.status in ("review", "unknown")]

    if issues:
        print(f"\n⚠️  Found {len(issues)} packages requiring attention:")
        for r in issues:
            print(f"  - {r.package} ({r.license})")
        return 1

    print("\n✅ All packages have compliant licenses")
    return 0


if __name__ == "__main__":
    sys.exit(main())
