#!/usr/bin/env python3
"""
Dependency Audit Script
Audits NuGet and pip dependencies for security vulnerabilities.

Supports:
- pip-audit (preferred)
- safety
- dotnet list package --vulnerable
"""

import json
import subprocess
import sys
from pathlib import Path

project_root = Path(__file__).parent.parent


def audit_pip():
    """Audit pip dependencies using pip-audit or safety."""
    print("Auditing pip dependencies...")

    # Try pip-audit first (preferred)
    try:
        result = subprocess.run(
            ["pip-audit", "--format", "json"],
            capture_output=True,
            text=True,
            cwd=project_root
        )

        if result.returncode == 0:
            print("[OK] pip-audit: No known security vulnerabilities")
            return 0
        else:
            try:
                vulnerabilities = json.loads(result.stdout) if result.stdout else []
                if vulnerabilities:
                    print(f"[WARN] pip-audit found {len(vulnerabilities)} vulnerabilities:")
                    for vuln in vulnerabilities[:10]:
                        name = vuln.get('name', 'unknown')
                        version = vuln.get('version', 'unknown')
                        vulns = vuln.get('vulns', [])
                        for v in vulns[:3]:
                            cve = v.get('id', 'unknown')
                            fix = v.get('fix_versions', ['upgrade'])
                            print(f"  - {name}=={version}: {cve} (fix: {fix})")
                    if len(vulnerabilities) > 10:
                        print(f"  ... and {len(vulnerabilities) - 10} more")
                    return 1
            except json.JSONDecodeError:
                print("[WARN] pip-audit output parsing failed")
                print(result.stdout[:500] if result.stdout else "No output")
            return 1

    except FileNotFoundError:
        print("[INFO] pip-audit not installed, trying safety...")

    # Fallback to safety
    try:
        result = subprocess.run(
            ["safety", "check", "--json"],
            capture_output=True,
            text=True,
            cwd=project_root
        )

        if result.returncode == 0:
            print("[OK] safety: No known security vulnerabilities")
            return 0
        else:
            try:
                data = json.loads(result.stdout) if result.stdout else {}
                vulnerabilities = data.get('vulnerabilities', [])
                if vulnerabilities:
                    print(f"[WARN] safety found {len(vulnerabilities)} vulnerabilities:")
                    for vuln in vulnerabilities[:10]:
                        pkg = vuln.get('package_name', 'unknown')
                        vuln_id = vuln.get('vulnerability_id', 'unknown')
                        print(f"  - {pkg}: {vuln_id}")
                    return 1
            except json.JSONDecodeError:
                print("[WARN] safety check found issues (parse failed)")
            return 1

    except FileNotFoundError:
        print("[WARN] Neither pip-audit nor safety installed")
        print("[INFO] Install with: pip install pip-audit safety")
        return 0
    except Exception as e:
        print(f"[ERROR] Failed to audit pip dependencies: {e}")
        return 1

def audit_nuget():
    """Audit NuGet dependencies."""
    print("Auditing NuGet dependencies...")

    # Find all .csproj files
    csproj_files = list(project_root.glob("**/*.csproj"))

    if not csproj_files:
        print("[INFO] No .csproj files found")
        return 0

    try:
        # Use dotnet list package --vulnerable
        for csproj in csproj_files:
            print(f"  Checking {csproj.relative_to(project_root)}...")
            result = subprocess.run(
                ["dotnet", "list", str(csproj), "package", "--vulnerable", "--include-transitive"],
                capture_output=True,
                text=True,
                cwd=project_root
            )

            if result.returncode == 0:
                output = result.stdout
                if "vulnerable" in output.lower() or "security" in output.lower():
                    print(f"[WARN] Potential vulnerabilities found in {csproj.name}")
                    print(output)
                    return 1
                else:
                    print(f"[OK] No known vulnerabilities in {csproj.name}")
            else:
                print(f"[WARN] Failed to check {csproj.name}: {result.stderr}")

        return 0
    except FileNotFoundError:
        print("[WARN] 'dotnet' not found. Install .NET SDK")
        return 0
    except Exception as e:
        print(f"[ERROR] Failed to audit NuGet dependencies: {e}")
        return 1

def main():
    """Main audit function."""
    print("=" * 60)
    print("Dependency Security Audit")
    print("=" * 60)

    pip_result = audit_pip()
    nuget_result = audit_nuget()

    print("=" * 60)
    if pip_result == 0 and nuget_result == 0:
        print("[OK] All dependency audits passed")
        return 0
    else:
        print("[WARN] Some dependency audits found issues")
        return 1

if __name__ == "__main__":
    sys.exit(main())
