#!/usr/bin/env python3
"""
VoiceStudio Release Evidence Collection Script

Collects and organizes evidence artifacts for release verification.
Generates an evidence pack based on EVIDENCE_PACK_TEMPLATE.md.

Usage:
    python scripts/collect_evidence.py --version 1.0.0
    python scripts/collect_evidence.py --version 1.0.0 --dry-run
"""

import argparse
import json
import os
import shutil
import subprocess
import sys
from datetime import datetime
from pathlib import Path


def get_project_root() -> Path:
    """Get the project root directory."""
    return Path(__file__).parent.parent.resolve()


def run_command(cmd: list[str], capture: bool = True) -> tuple[int, str, str]:
    """Run a command and return exit code, stdout, stderr."""
    try:
        result = subprocess.run(
            cmd,
            capture_output=capture,
            text=True,
            cwd=get_project_root(),
            timeout=300,
        )
        return result.returncode, result.stdout, result.stderr
    except subprocess.TimeoutExpired:
        return -1, "", "Command timed out"
    except Exception as e:
        return -1, "", str(e)


def collect_build_evidence(evidence_dir: Path, version: str) -> dict:
    """Collect build-related evidence."""
    print("Collecting build evidence...")

    evidence = {
        "build_success": False,
        "test_success": False,
        "artifacts": [],
    }

    # Copy build logs if they exist
    buildlogs = get_project_root() / ".buildlogs"
    if buildlogs.exists():
        for log_file in buildlogs.glob("*.log"):
            dest = evidence_dir / "build_logs" / log_file.name
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(log_file, dest)
            evidence["artifacts"].append(str(dest.relative_to(evidence_dir)))

        # Check for test results
        for xml_file in buildlogs.glob("*.xml"):
            dest = evidence_dir / "test_results" / xml_file.name
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(xml_file, dest)
            evidence["artifacts"].append(str(dest.relative_to(evidence_dir)))

    # Run verification if no logs exist
    if not evidence["artifacts"]:
        print("  Running build verification...")
        code, stdout, stderr = run_command([
            "dotnet", "build", "VoiceStudio.sln",
            "-c", "Release", "-p:Platform=x64"
        ])
        evidence["build_success"] = code == 0
        evidence["build_output"] = stdout if stdout else stderr

        # Save build output
        build_log = evidence_dir / "build_logs" / f"build_{datetime.now():%Y%m%d_%H%M%S}.log"
        build_log.parent.mkdir(parents=True, exist_ok=True)
        build_log.write_text(evidence.get("build_output", ""))
        evidence["artifacts"].append(str(build_log.relative_to(evidence_dir)))

    return evidence


def collect_test_evidence(evidence_dir: Path) -> dict:
    """Collect test-related evidence."""
    print("Collecting test evidence...")

    evidence = {
        "csharp_tests": {"passed": 0, "failed": 0, "total": 0},
        "python_tests": {"passed": 0, "failed": 0, "total": 0},
        "artifacts": [],
    }

    # Check for existing test results
    test_results_dir = evidence_dir / "test_results"
    test_results_dir.mkdir(parents=True, exist_ok=True)

    # Run C# tests if no results exist
    print("  Running C# tests...")
    code, stdout, stderr = run_command([
        "dotnet", "test",
        "src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj",
        "-c", "Debug", "-p:Platform=x64",
        "--logger", "trx",
        "--no-build"
    ])

    evidence["csharp_tests"]["success"] = code == 0

    # Run Python tests
    print("  Running Python tests...")
    code, _stdout, _stderr = run_command([
        sys.executable, "-m", "pytest",
        "tests/", "-v", "--tb=short",
        f"--junitxml={test_results_dir / 'pytest_results.xml'}"
    ])

    evidence["python_tests"]["success"] = code == 0

    return evidence


def collect_quality_ledger(evidence_dir: Path) -> dict:
    """Extract Quality Ledger status."""
    print("Collecting Quality Ledger status...")

    ledger_path = (
        get_project_root() / "docs" / "archive" / "Recovery_Plan" / "QUALITY_LEDGER.md"
    )
    evidence = {
        "open_p0": 0,
        "open_p1": 0,
        "total_issues": 0,
    }

    if ledger_path.exists():
        content = ledger_path.read_text(encoding="utf-8")

        # Count open P0/P1 issues
        lines = content.split("\n")
        for line in lines:
            if "| OPEN" in line or "| TRIAGE" in line or "| IN_PROGRESS" in line:
                evidence["total_issues"] += 1
                if "S0 Blocker" in line:
                    evidence["open_p0"] += 1
                elif "S1 Critical" in line:
                    evidence["open_p1"] += 1

        # Copy ledger to evidence
        dest = evidence_dir / "quality" / "QUALITY_LEDGER_SNAPSHOT.md"
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ledger_path, dest)

    return evidence


def generate_evidence_pack(
    version: str,
    evidence_dir: Path,
    build_evidence: dict,
    test_evidence: dict,
    ledger_evidence: dict,
) -> Path:
    """Generate the final evidence pack markdown."""
    print("Generating evidence pack...")

    template_path = get_project_root() / "docs" / "release" / "EVIDENCE_PACK_TEMPLATE.md"

    if template_path.exists():
        template = template_path.read_text(encoding="utf-8")
    else:
        template = "# Release Evidence Pack - v{VERSION}\n\nNo template found."

    # Replace placeholders
    now = datetime.now()
    content = template.replace("{VERSION}", version)
    content = content.replace("{DATE}", now.strftime("%Y-%m-%d"))
    content = content.replace("{AGENT_NAME}", os.getenv("COMPUTERNAME", "Unknown"))

    # Add summary section
    summary = f"""

## Evidence Collection Summary

**Collection Date:** {now.isoformat()}
**Version:** {version}

### Build Status
- Build artifacts collected: {len(build_evidence.get('artifacts', []))}

### Test Status
- C# tests: {'PASS' if test_evidence.get('csharp_tests', {}).get('success') else 'FAIL'}
- Python tests: {'PASS' if test_evidence.get('python_tests', {}).get('success') else 'FAIL'}

### Quality Ledger
- Open P0 issues: {ledger_evidence.get('open_p0', 0)}
- Open P1 issues: {ledger_evidence.get('open_p1', 0)}
- Total open issues: {ledger_evidence.get('total_issues', 0)}

"""

    # Insert summary after the header
    lines = content.split("\n")
    output_lines = []
    inserted = False
    for line in lines:
        output_lines.append(line)
        if line.startswith("**Build Agent:**") and not inserted:
            output_lines.append(summary)
            inserted = True

    output_content = "\n".join(output_lines)

    # Save evidence pack
    pack_path = evidence_dir / f"EVIDENCE_PACK_v{version}.md"
    pack_path.write_text(output_content, encoding="utf-8")

    # Save metadata
    metadata = {
        "version": version,
        "collected_at": now.isoformat(),
        "build": build_evidence,
        "tests": test_evidence,
        "quality_ledger": ledger_evidence,
    }

    metadata_path = evidence_dir / "evidence_metadata.json"
    metadata_path.write_text(json.dumps(metadata, indent=2), encoding="utf-8")

    return pack_path


def main():
    parser = argparse.ArgumentParser(
        description="Collect release evidence for VoiceStudio"
    )
    parser.add_argument(
        "--version", "-v",
        required=True,
        help="Release version (e.g., 1.0.0)"
    )
    parser.add_argument(
        "--output-dir", "-o",
        help="Output directory for evidence (default: docs/release/evidence/v{version})"
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Show what would be collected without actually running"
    )
    parser.add_argument(
        "--skip-tests",
        action="store_true",
        help="Skip running tests (use existing results only)"
    )

    args = parser.parse_args()

    # Setup evidence directory
    if args.output_dir:
        evidence_dir = Path(args.output_dir)
    else:
        evidence_dir = get_project_root() / "docs" / "release" / "evidence" / f"v{args.version}"

    if args.dry_run:
        print(f"[DRY RUN] Would collect evidence to: {evidence_dir}")
        print(f"[DRY RUN] Version: {args.version}")
        return

    print(f"Collecting evidence for version {args.version}")
    print(f"Output directory: {evidence_dir}")

    evidence_dir.mkdir(parents=True, exist_ok=True)

    # Collect evidence
    build_evidence = collect_build_evidence(evidence_dir, args.version)

    if args.skip_tests:
        test_evidence = {"csharp_tests": {}, "python_tests": {}}
    else:
        test_evidence = collect_test_evidence(evidence_dir)

    ledger_evidence = collect_quality_ledger(evidence_dir)

    # Generate evidence pack
    pack_path = generate_evidence_pack(
        args.version,
        evidence_dir,
        build_evidence,
        test_evidence,
        ledger_evidence,
    )

    print(f"\n{'='*60}")
    print("Evidence collection complete!")
    print(f"Evidence pack: {pack_path}")
    print(f"Evidence directory: {evidence_dir}")
    print(f"{'='*60}")


if __name__ == "__main__":
    main()
