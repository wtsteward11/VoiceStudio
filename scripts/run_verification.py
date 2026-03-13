#!/usr/bin/env python3
"""
Run Verification

Automated verification script that validates gate status and ledger.
Includes import validation as a defensive pre-check.

Exit codes:
  0 - All checks passed
  1 - One or more checks failed
"""


import io
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

# Ensure UTF-8 output on Windows console
if sys.platform == "win32" and hasattr(sys.stdout, "buffer"):
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding="utf-8", errors="replace")


def _validate_imports_first():
    """
    Validate critical imports before running verification commands.

    Defensive check to catch ModuleNotFoundError before running CLI commands.
    """
    critical_modules = [
        "tools.overseer",
        "tools.overseer.models",
        "tools.overseer.cli.main",
    ]

    for module in critical_modules:
        try:
            __import__(module)
        except (ModuleNotFoundError, ImportError) as e:
            return False, f"Import validation failed: {module} - {e}"

    return True, "Imports validated"


def run_check(name, command, timeout=30):
    """Run a single verification check."""
    start_time = datetime.now()

    try:
        import shlex
        if isinstance(command, str):
            cmd_list = shlex.split(command, posix=(sys.platform != "win32"))
        else:
            cmd_list = command
        result = subprocess.run(
            cmd_list,
            capture_output=True,
            text=True,
            timeout=timeout,
        )

        duration = (datetime.now() - start_time).total_seconds()

        return {
            "name": name,
            "command": command if isinstance(command, str) else " ".join(command),
            "exit_code": result.returncode,
            "passed": result.returncode == 0,
            "duration_seconds": round(duration, 2),
            "output_sample": (result.stdout + result.stderr)[:500] if result.stdout or result.stderr else ""
        }
    except subprocess.TimeoutExpired:
        duration = (datetime.now() - start_time).total_seconds()
        return {
            "name": name,
            "command": command if isinstance(command, str) else " ".join(command),
            "exit_code": -1,
            "passed": False,
            "duration_seconds": round(duration, 2),
            "output_sample": f"Command timed out after {timeout}s"
        }
    except Exception as e:
        duration = (datetime.now() - start_time).total_seconds()
        return {
            "name": name,
            "command": command if isinstance(command, str) else " ".join(command),
            "exit_code": -1,
            "passed": False,
            "duration_seconds": round(duration, 2),
            "output_sample": f"Exception: {e}"
        }


def main():
    """Run all verification checks."""
    project_root = Path(__file__).parent.parent

    # Add project root to path for imports
    if str(project_root) not in sys.path:
        sys.path.insert(0, str(project_root))

    # Pre-check: Validate imports
    print("Pre-check: Validating imports...")
    valid, message = _validate_imports_first()
    if not valid:
        print(f"❌ {message}")
        print("   Fix: Ensure all required modules have __init__.py and are importable")
        return 1
    print(f"✓ {message}\n")

    # Define checks
    skip_guard = "--skip-guard" in sys.argv
    skip_quality = "--skip-quality" in sys.argv
    skip_contract_diff = "--skip-contract-diff" in sys.argv
    checks = [
        {
            "name": "gate_status",
            "command": f"{sys.executable} -m tools.overseer.cli.main gate status"
        },
        {
            "name": "ledger_validate",
            "command": f"{sys.executable} -m tools.overseer.cli.main ledger validate"
        },
    ]
    if not skip_contract_diff:
        contract_diff_script = project_root / "scripts" / "contract_diff.py"
        if contract_diff_script.exists():
            checks.append({
                "name": "contract_diff",
                "command": f"{sys.executable} {contract_diff_script}"
            })
    if not skip_guard:
        checks.append({
            "name": "completion_guard",
            "command": f"{sys.executable} -m tools.overseer.verification.completion_guard"
        })

    # IBackendClient creep + SynthesizeVoiceAsync ownership (Phase 7B)
    creep_script = project_root / "scripts" / "ci" / "check_ibackendclient_creep.py"
    if creep_script.exists():
        checks.append({
            "name": "ibackendclient_creep",
            "command": f"{sys.executable} {creep_script}"
        })

    # Retained-async rule (Assessment Remediation Plan Task 4.2)
    # Fails only on NEW violations; baseline in .ci/retained_async_baseline.txt
    retained_async_script = project_root / "scripts" / "ci" / "check_retained_async.py"
    retained_async_baseline = project_root / ".ci" / "retained_async_baseline.txt"
    if retained_async_script.exists() and retained_async_baseline.exists():
        checks.append({
            "name": "retained_async",
            "command": f"{sys.executable} {retained_async_script} --baseline-file {retained_async_baseline}"
        })

    # Quality checks (WS-1, WS-4) - can be skipped with --skip-quality
    if not skip_quality:
        # Empty catch block check (WS-1) - needs longer timeout due to large codebase scan
        empty_catch_script = project_root / "scripts" / "check_empty_catches.py"
        if empty_catch_script.exists():
            checks.append({
                "name": "empty_catch_check",
                "command": f"{sys.executable} {empty_catch_script}",
                "timeout": 60  # Extended timeout for large codebase scan
            })

        # XAML safety check (WS-4)
        xaml_lint_script = project_root / "scripts" / "lint_xaml.py"
        if xaml_lint_script.exists():
            checks.append({
                "name": "xaml_safety_check",
                "command": f"{sys.executable} {xaml_lint_script}"
            })

        # UI gap audit (ContentDialog XamlRoot, hidden panels, placeholders)
        ui_audit_script = project_root / "scripts" / "audit_ui_gaps.py"
        if ui_audit_script.exists():
            checks.append({
                "name": "ui_gap_audit",
                "command": f"{sys.executable} {ui_audit_script}"
            })

    # Optionally add build check if --build flag
    if "--build" in sys.argv:
        checks.append({
            "name": "build_smoke",
            "command": "dotnet build VoiceStudio.sln -c Debug -p:Platform=x64 --verbosity minimal"
        })

    # Optionally add release build check if --release flag (WS-5)
    if "--release" in sys.argv:
        checks.append({
            "name": "release_build_smoke",
            "command": "dotnet build VoiceStudio.sln -c Release -p:Platform=x64 --verbosity minimal"
        })

    # Run checks
    results = []
    print("=" * 60)
    print("VERIFICATION REPORT (automated)")
    print("=" * 60)
    print()
    if skip_guard:
        print("  [SKIP] completion_guard (--skip-guard flag)")
    for check in checks:
        timeout = check.get("timeout", 30)  # Default 30s, or per-check override
        result = run_check(check["name"], check["command"], timeout=timeout)
        results.append(result)

        status = "PASS" if result["passed"] else "FAIL"
        print(f"  [{status}] {result['name']} (exit {result['exit_code']}, {result['duration_seconds']}s)")

    # Summary
    all_passed = all(r["passed"] for r in results)
    print()
    print(f"  Overall: {'PASS' if all_passed else 'FAIL'}")
    print()

    # Save JSON report
    output_dir = project_root / ".buildlogs" / "verification"
    output_dir.mkdir(parents=True, exist_ok=True)

    report = {
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "timestamp_short": datetime.now().strftime("%Y%m%d-%H%M%S"),
        "all_passed": all_passed,
        "checks": results
    }

    output_file = output_dir / "last_run.json"
    with open(output_file, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)

    print(f"  JSON: {output_file}")
    print()

    return 0 if all_passed else 1


if __name__ == "__main__":
    sys.exit(main())
