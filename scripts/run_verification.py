#!/usr/bin/env python3
"""
Run Verification

Automated verification script that validates gate status and ledger.
Includes import validation as a defensive pre-check.

For full product verification including startup orchestration (icon launch,
backend auto-start, overlay), use scripts/verify.ps1 stages **UI Self-Test**,
**Icon-Launch Smoke**, **Failure-Path Smoke**, **Runtime-Missing Failure Smoke**
(see verify.ps1 header stage list) or scripts/gatec-publish-launch.ps1 -UiSmoke. See
docs/design/STARTUP_ORCHESTRATION_HARDENING_PLAN.md.

Exit codes:
  0 - All checks passed
  1 - One or more checks failed

Flags:
  --enforce-runtime-proof — `runtime_proof_staleness` fails the run if
    PROOF_GOLDEN_PATH_REAL_*.json is missing or older than 72 hours (GAP-015 slice 2).
  --skip-runtime-proof-staleness — omit the staleness row entirely.
  --enforce-backend-smoke — `backend_smoke_freshness` fails the run if
    PROOF_BACKEND_SMOKE_*.json is missing, older than 72 hours, or status=FAIL (GAP-069 slice 3).
    Latest proof with status=BLOCKED never fails (prerequisites absent; not a regression).
  --skip-backend-smoke-staleness — omit the backend smoke freshness row entirely.

Always-on advisory (GAP-015 slice 3): `slo_baseline_freshness` scans for
`slo_baselines.json` under `artifacts/verify/*/` and `docs/reports/verification/`;
never fails the run.
"""


import io
import json
import os
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path


def _runtime_proof_staleness_result(project_root: Path, *, enforce: bool = False) -> dict:
    """
    GAP-015: Report freshness of optional PROOF_GOLDEN_PATH_REAL_*.json artifacts.

    When enforce=False (default): advisory only; passed=True; exit_code=0.
    When enforce=True (GAP-015 slice 2): MISSING/STALE/ERROR fails the run (passed=False, exit_code=1).
    """
    start_time = datetime.now()
    ver_dir = project_root / "docs" / "reports" / "verification"
    try:
        files = sorted(
            ver_dir.glob("PROOF_GOLDEN_PATH_REAL_*.json"),
            key=lambda p: p.stat().st_mtime,
            reverse=True,
        )
    except OSError as e:
        duration = (datetime.now() - start_time).total_seconds()
        passed = not enforce
        return {
            "name": "runtime_proof_staleness",
            "command": "scan docs/reports/verification/PROOF_GOLDEN_PATH_REAL_*.json",
            "exit_code": 0 if passed else 1,
            "passed": passed,
            "duration_seconds": round(duration, 2),
            "output_sample": f"STATUS=ERROR listing proofs: {e}",
            "enforce": enforce,
        }

    if not files:
        duration = (datetime.now() - start_time).total_seconds()
        msg = (
            "STATUS=MISSING: no PROOF_GOLDEN_PATH_REAL_*.json under docs/reports/verification "
            "(optional artifact; generate via scripts/ci/write_golden_path_real_proof.py). "
        )
        if enforce:
            msg += "Enforce mode: this is a hard failure."
        else:
            msg += "This check is informational and does not fail the run."
        passed = not enforce
        return {
            "name": "runtime_proof_staleness",
            "command": "scan docs/reports/verification/PROOF_GOLDEN_PATH_REAL_*.json",
            "exit_code": 0 if passed else 1,
            "passed": passed,
            "duration_seconds": round(duration, 2),
            "output_sample": msg,
            "enforce": enforce,
        }

    latest = files[0]
    mtime = datetime.fromtimestamp(latest.stat().st_mtime, tz=timezone.utc)
    age_hours = (datetime.now(timezone.utc) - mtime).total_seconds() / 3600.0
    status = "FRESH" if age_hours <= 72 else "STALE"
    duration = (datetime.now() - start_time).total_seconds()
    if enforce:
        passed = status == "FRESH"
        tail = "Enforce mode: STALE or MISSING fails exit code."
    else:
        passed = True
        tail = "warning-only, does not fail exit code"
    return {
        "name": "runtime_proof_staleness",
        "command": "scan docs/reports/verification/PROOF_GOLDEN_PATH_REAL_*.json",
        "exit_code": 0 if passed else 1,
        "passed": passed,
        "duration_seconds": round(duration, 2),
        "output_sample": (
            f"STATUS={status}: latest_file={latest.name} age_hours={age_hours:.2f} "
            f"(policy_window_hours=72; {tail})"
        ),
        "enforce": enforce,
    }


def _slo_baseline_freshness_result(project_root: Path) -> dict:
    """
    GAP-015 slice 3: Report freshness of optional slo_baselines.json artifacts.

    Advisory only — never fails the run (baseline_policy is not enforced via this check).
    Scans artifacts/verify/*/slo_baselines.json and docs/reports/verification/slo_baselines*.json.
    """
    start_time = datetime.now()
    candidates: list[Path] = []
    try:
        art = project_root / "artifacts" / "verify"
        if art.is_dir():
            candidates.extend(sorted(art.glob("*/slo_baselines.json")))
        ver_dir = project_root / "docs" / "reports" / "verification"
        if ver_dir.is_dir():
            candidates.extend(sorted(ver_dir.glob("slo_baselines*.json")))
    except OSError as e:
        duration = (datetime.now() - start_time).total_seconds()
        return {
            "name": "slo_baseline_freshness",
            "command": "scan slo_baselines.json (artifacts + docs/reports/verification)",
            "exit_code": 0,
            "passed": True,
            "duration_seconds": round(duration, 2),
            "output_sample": f"STATUS=ERROR listing baselines: {e}",
            "enforce": False,
        }

    if not candidates:
        duration = (datetime.now() - start_time).total_seconds()
        return {
            "name": "slo_baseline_freshness",
            "command": "scan slo_baselines.json (artifacts + docs/reports/verification)",
            "exit_code": 0,
            "passed": True,
            "duration_seconds": round(duration, 2),
            "output_sample": (
                "STATUS=MISSING: no slo_baselines.json found under artifacts/verify/*/ or "
                "docs/reports/verification/slo_baselines*.json (optional; generate via verify.ps1 -RuntimeProof). "
                "Advisory only."
            ),
            "enforce": False,
        }

    latest = max(candidates, key=lambda p: p.stat().st_mtime)
    mtime = datetime.fromtimestamp(latest.stat().st_mtime, tz=timezone.utc)
    age_hours = (datetime.now(timezone.utc) - mtime).total_seconds() / 3600.0
    status = "FRESH" if age_hours <= 72 else "STALE"
    duration = (datetime.now() - start_time).total_seconds()
    return {
        "name": "slo_baseline_freshness",
        "command": "scan slo_baselines.json (artifacts + docs/reports/verification)",
        "exit_code": 0,
        "passed": True,
        "duration_seconds": round(duration, 2),
        "output_sample": (
            f"STATUS={status}: latest_file={latest.relative_to(project_root)} "
            f"age_hours={age_hours:.2f} (policy_window_hours=72; advisory only, does not fail exit code)"
        ),
        "enforce": False,
    }


def _backend_smoke_freshness_result(project_root: Path, *, enforce: bool = False) -> dict:
    """
    GAP-069 slice 3: Report freshness of PROOF_BACKEND_SMOKE_*.json artifacts.

    When enforce=False (default): advisory only for missing/stale/FAIL; passed=True unless
    enforce path would fail (we still set passed True for advisory mode for the row).
    When enforce=True: missing, stale (>72h), status=FAIL, or parse error fails the run.
    status=BLOCKED always passes (honest prerequisite gap; not a product regression).
    """
    start_time = datetime.now()
    ver_dir = project_root / "docs" / "reports" / "verification"
    name = "backend_smoke_freshness"
    cmd = "scan docs/reports/verification/PROOF_BACKEND_SMOKE_*.json"

    try:
        files = sorted(
            ver_dir.glob("PROOF_BACKEND_SMOKE_*.json"),
            key=lambda p: p.stat().st_mtime,
            reverse=True,
        )
    except OSError as e:
        duration = (datetime.now() - start_time).total_seconds()
        passed = not enforce
        return {
            "name": name,
            "command": cmd,
            "exit_code": 0 if passed else 1,
            "passed": passed,
            "duration_seconds": round(duration, 2),
            "output_sample": f"STATUS=ERROR listing proofs: {e}",
            "enforce": enforce,
        }

    if not files:
        duration = (datetime.now() - start_time).total_seconds()
        msg = (
            "STATUS=MISSING: no PROOF_BACKEND_SMOKE_*.json under docs/reports/verification "
            "(optional; generate via python scripts/ci/run_backend_smoke.py or verify.ps1 -BackendSmoke). "
        )
        if enforce:
            msg += "Enforce mode: this is a hard failure."
        else:
            msg += "Advisory only; does not fail the run."
        passed = not enforce
        return {
            "name": name,
            "command": cmd,
            "exit_code": 0 if passed else 1,
            "passed": passed,
            "duration_seconds": round(duration, 2),
            "output_sample": msg,
            "enforce": enforce,
        }

    latest = files[0]
    duration = (datetime.now() - start_time).total_seconds()

    try:
        raw = latest.read_text(encoding="utf-8")
        data = json.loads(raw)
    except (OSError, json.JSONDecodeError) as e:
        msg = f"STATUS=ERROR: could not read/parse {latest.name}: {e}"
        passed = not enforce
        return {
            "name": name,
            "command": cmd,
            "exit_code": 0 if passed else 1,
            "passed": passed,
            "duration_seconds": round(duration, 2),
            "output_sample": msg,
            "enforce": enforce,
        }

    if not isinstance(data, dict):
        msg = f"STATUS=ERROR: root JSON is not an object in {latest.name}"
        passed = not enforce
        return {
            "name": name,
            "command": cmd,
            "exit_code": 0 if passed else 1,
            "passed": passed,
            "duration_seconds": round(duration, 2),
            "output_sample": msg,
            "enforce": enforce,
        }

    status = data.get("status")
    mtime = datetime.fromtimestamp(latest.stat().st_mtime, tz=timezone.utc)
    age_hours = (datetime.now(timezone.utc) - mtime).total_seconds() / 3600.0

    if status == "BLOCKED":
        msg = (
            f"STATUS=BLOCKED: latest_file={latest.name} age_hours={age_hours:.2f} "
            "(prerequisites absent when smoke ran; advisory only; never fails enforce mode)"
        )
        return {
            "name": name,
            "command": cmd,
            "exit_code": 0,
            "passed": True,
            "duration_seconds": round(duration, 2),
            "output_sample": msg,
            "enforce": enforce,
        }

    if status == "FAIL":
        msg = (
            f"STATUS=FAIL: latest_file={latest.name} age_hours={age_hours:.2f} "
            f"(failure_reason={data.get('failure_reason')!r})"
        )
        if enforce:
            msg += " Enforce mode: hard failure."
        else:
            msg += " Advisory only; does not fail the run."
        passed = not enforce
        return {
            "name": name,
            "command": cmd,
            "exit_code": 0 if passed else 1,
            "passed": passed,
            "duration_seconds": round(duration, 2),
            "output_sample": msg,
            "enforce": enforce,
        }

    if status == "PASS":
        fresh = age_hours <= 72.0
        st = "FRESH" if fresh else "STALE"
        if enforce:
            passed = fresh
            tail = "Enforce mode: STALE or MISSING fails exit code."
        else:
            passed = True
            tail = "advisory only, does not fail exit code"
        return {
            "name": name,
            "command": cmd,
            "exit_code": 0 if passed else 1,
            "passed": passed,
            "duration_seconds": round(duration, 2),
            "output_sample": (
                f"STATUS={st}: latest_file={latest.name} age_hours={age_hours:.2f} "
                f"(policy_window_hours=72; {tail})"
            ),
            "enforce": enforce,
        }

    msg = f"STATUS=UNKNOWN: latest_file={latest.name} status={status!r}"
    if enforce:
        msg += " Enforce mode: hard failure."
    else:
        msg += " Advisory only."
    passed = not enforce
    return {
        "name": name,
        "command": cmd,
        "exit_code": 0 if passed else 1,
        "passed": passed,
        "duration_seconds": round(duration, 2),
        "output_sample": msg,
        "enforce": enforce,
    }


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


def _check_testhost_present():
    """Return True if testhost.exe is running (Windows only)."""
    if sys.platform != "win32":
        return False
    try:
        result = subprocess.run(
            ["tasklist", "/FI", "IMAGENAME eq testhost.exe", "/NH"],
            capture_output=True,
            text=True,
            timeout=5,
        )
        return "testhost.exe" in (result.stdout or "").lower()
    except Exception as ex:
        print(f"  [SKIP] testhost probe: {ex}", file=sys.stderr)
        return False


def _kill_testhost_before_build():
    """
    Kill lingering testhost processes that can lock DLLs during build.

    Root cause: MSB3027/MSB3021 when testhost.exe holds VoiceStudio.Core.dll
    or VoiceStudio.App.dll from a previous test run. Running build immediately
    after tests can fail with "file is being used by another process".

    Returns:
        bool: True if testhost was present and cleanup was performed.
    """
    if sys.platform != "win32":
        return False
    was_present = _check_testhost_present()
    if not was_present:
        return False
    try:
        subprocess.run(
            ["taskkill", "/F", "/IM", "testhost.exe"],
            capture_output=True,
            timeout=5,
        )
        return True
    except Exception as e:
        print(f"  [SKIP] testhost cleanup: {e}", file=sys.stderr)
        return False


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

    # Constructor invariant: all MIGRATED ViewModels must have Constructor_DoesNotCallClient_BeforeActivation
    # Uses baseline for documented exemptions (MiniTimeline, AdvancedRealTimeVisualization)
    constructor_invariant_script = project_root / "scripts" / "ci" / "check_constructor_invariant_coverage.py"
    constructor_invariant_baseline = project_root / ".ci" / "constructor_invariant_baseline.txt"
    if constructor_invariant_script.exists():
        if constructor_invariant_baseline.exists():
            checks.append({
                "name": "constructor_invariant",
                "command": f"{sys.executable} {constructor_invariant_script} --baseline-file {constructor_invariant_baseline}"
            })
        else:
            checks.append({
                "name": "constructor_invariant",
                "command": f"{sys.executable} {constructor_invariant_script}"
            })

    # Retained-async rule (Assessment Remediation Plan Task 4.2, Truth Reset Task 4)
    # Fails only on NEW violations when baseline exists; FAIL when baseline missing (no skip)
    retained_async_script = project_root / "scripts" / "ci" / "check_retained_async.py"
    retained_async_baseline = project_root / ".ci" / "retained_async_baseline.txt"
    if retained_async_script.exists():
        if retained_async_baseline.exists():
            checks.append({
                "name": "retained_async",
                "command": f"{sys.executable} {retained_async_script} --baseline-file {retained_async_baseline}"
            })
        else:
            checks.append({
                "name": "retained_async",
                "command": f"{sys.executable} -c \"import sys; print('ERROR: .ci/retained_async_baseline.txt is required; create with: python scripts/ci/check_retained_async.py --baseline'); sys.exit(1)\""
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

        # Startup artifact schema v2 guard (GAP-069 regression guard lane)
        skip_startup_artifact = os.environ.get(
            "VOICESTUDIO_SKIP_STARTUP_ARTIFACT_CHECK", ""
        ).strip().lower() in ("1", "true", "yes")
        startup_checker = project_root / "scripts" / "ci" / "check_startup_artifact.py"
        if startup_checker.exists() and not skip_startup_artifact:
            # Use argv list on Windows so paths are not broken by shlex.quote/split.
            artifact_path = os.environ.get("VOICESTUDIO_STARTUP_ARTIFACT_PATH", "").strip()
            if artifact_path:
                startup_cmd: list[str] | str = [
                    sys.executable,
                    str(startup_checker),
                    "--path",
                    artifact_path,
                ]
            else:
                startup_cmd = f"{sys.executable} {startup_checker}"
            checks.append({
                "name": "startup_artifact_check",
                "command": startup_cmd,
                "timeout": 10,
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
            "command": "dotnet build VoiceStudio.sln -c Debug -p:Platform=x64 --verbosity minimal",
            "timeout": 90
        })

    # Optionally add release build check if --release flag (WS-5)
    if "--release" in sys.argv:
        checks.append({
            "name": "release_build_smoke",
            "command": "dotnet build VoiceStudio.sln -c Release -p:Platform=x64 --verbosity minimal",
            "timeout": 90
        })

    # GAP-015: optional real golden-path proof staleness (warning-only unless --enforce-runtime-proof)
    skip_runtime_stale = "--skip-runtime-proof-staleness" in sys.argv
    enforce_runtime_proof = "--enforce-runtime-proof" in sys.argv

    # GAP-069 slice 3: optional backend smoke proof freshness
    skip_backend_smoke_stale = "--skip-backend-smoke-staleness" in sys.argv
    enforce_backend_smoke = "--enforce-backend-smoke" in sys.argv

    # Run checks
    results = []
    print("=" * 60)
    print("VERIFICATION REPORT (automated)")
    print("=" * 60)
    print()
    if skip_guard:
        print("  [SKIP] completion_guard (--skip-guard flag)")
    for check in checks:
        # Pre-build cleanup: kill testhost to avoid MSB3027 file-lock failures
        stale_process_cleaned = False
        if check["name"] in ("build_smoke", "release_build_smoke"):
            stale_process_cleaned = _kill_testhost_before_build()
            if stale_process_cleaned:
                print(f"  [AUDIT] testhost.exe was present; cleanup performed before {check['name']}")
        timeout = check.get("timeout", 30)  # Default 30s, or per-check override
        result = run_check(check["name"], check["command"], timeout=timeout)
        if stale_process_cleaned:
            result["stale_process_cleaned"] = True
        results.append(result)

        status = "PASS" if result["passed"] else "FAIL"
        print(f"  [{status}] {result['name']} (exit {result['exit_code']}, {result['duration_seconds']}s)")

    if not skip_runtime_stale:
        stale_result = _runtime_proof_staleness_result(
            project_root, enforce=enforce_runtime_proof
        )
        results.append(stale_result)
        if stale_result["passed"]:
            tag = "PASS" if enforce_runtime_proof else "ADVISORY"
        else:
            tag = "FAIL"
        print(
            f"  [{tag}] {stale_result['name']} "
            f"(exit {stale_result['exit_code']}, {stale_result['duration_seconds']}s)"
        )
        print(f"       {stale_result['output_sample'][:300]}")

    slo_fresh_result = _slo_baseline_freshness_result(project_root)
    results.append(slo_fresh_result)
    print(
        f"  [ADVISORY] {slo_fresh_result['name']} "
        f"(exit {slo_fresh_result['exit_code']}, {slo_fresh_result['duration_seconds']}s)"
    )
    print(f"       {slo_fresh_result['output_sample'][:300]}")

    if not skip_backend_smoke_stale:
        smoke_result = _backend_smoke_freshness_result(
            project_root, enforce=enforce_backend_smoke
        )
        results.append(smoke_result)
        sample = smoke_result["output_sample"]
        if smoke_result["passed"]:
            if sample.startswith("STATUS=BLOCKED"):
                tag = "ADVISORY"
            else:
                tag = "PASS" if enforce_backend_smoke else "ADVISORY"
        else:
            tag = "FAIL"
        print(
            f"  [{tag}] {smoke_result['name']} "
            f"(exit {smoke_result['exit_code']}, {smoke_result['duration_seconds']}s)"
        )
        print(f"       {smoke_result['output_sample'][:300]}")

    # Summary
    all_passed = all(r["passed"] for r in results)
    any_stale_cleaned = any(r.get("stale_process_cleaned") for r in results)
    print()
    print(f"  Overall: {'PASS' if all_passed else 'FAIL'}")
    if any_stale_cleaned:
        print("  [AUDIT] stale_process_cleaned: true (testhost was killed before build)")
    print()

    # Save JSON report
    output_dir = project_root / ".buildlogs" / "verification"
    output_dir.mkdir(parents=True, exist_ok=True)

    # Include stale_process_cleaned at top level for trending
    report = {
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "timestamp_short": datetime.now().strftime("%Y%m%d-%H%M%S"),
        "all_passed": all_passed,
        "stale_process_cleaned": any_stale_cleaned,
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
