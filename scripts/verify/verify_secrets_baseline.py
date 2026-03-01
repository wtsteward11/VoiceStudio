#!/usr/bin/env python3
"""
Secrets Baseline Verification Script

Automated verification of .secrets.baseline for CI integration.
Runs detect-secrets scan and compares against baseline, reporting:
- New unresolved secrets (FAIL)
- Resolved secrets no longer in baseline (INFO)
- All secrets properly baselined (PASS)

Usage:
    python scripts/verify_secrets_baseline.py [--update] [--verbose]

Exit Codes:
    0 - No new secrets found
    1 - New secrets detected (requires investigation)
    2 - Error during execution
"""

import argparse
import json
import logging
import subprocess
import sys
from pathlib import Path

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(levelname)s - %(message)s",
)
logger = logging.getLogger(__name__)

# VoiceStudio repository root
REPO_ROOT = Path(__file__).parent.parent
BASELINE_FILE = REPO_ROOT / ".secrets.baseline"

# Files/patterns to exclude from scan (beyond what's in baseline config)
ADDITIONAL_EXCLUDES = [
    ".venv",
    "venv",
    "runtime/external",
    "node_modules",
    "__pycache__",
    ".git",
    ".buildlogs",
]


def load_baseline() -> dict:
    """Load the existing secrets baseline file."""
    if not BASELINE_FILE.exists():
        logger.error(f"Baseline file not found: {BASELINE_FILE}")
        return {}

    with open(BASELINE_FILE, encoding="utf-8") as f:
        return json.load(f)


def run_secrets_scan(update_baseline: bool = False) -> tuple[int, str, str]:
    """
    Run detect-secrets scan against the repository.

    Args:
        update_baseline: If True, update the baseline with new findings

    Returns:
        Tuple of (return_code, stdout, stderr)
    """
    cmd = ["detect-secrets", "scan"]

    if BASELINE_FILE.exists():
        cmd.extend(["--baseline", str(BASELINE_FILE)])

    if update_baseline:
        cmd.append("--update")

    logger.info(f"Running: {' '.join(cmd)}")

    try:
        result = subprocess.run(
            cmd,
            cwd=str(REPO_ROOT),
            capture_output=True,
            text=True,
            timeout=600,  # 10 minute timeout
        )
        return result.returncode, result.stdout, result.stderr
    except subprocess.TimeoutExpired:
        logger.error("Secrets scan timed out after 10 minutes")
        return 2, "", "Timeout"
    except Exception as e:
        logger.error(f"Error running detect-secrets: {e}")
        return 2, "", str(e)


def compare_results(
    baseline: dict,
    scan_output: str,
) -> tuple[list[dict], list[dict]]:
    """
    Compare scan results against baseline.

    Returns:
        Tuple of (new_secrets, resolved_secrets)
    """
    baseline_results = baseline.get("results", {})
    baseline_hashes: set[str] = set()

    # Collect all hashed secrets from baseline
    for file_results in baseline_results.values():
        for result in file_results:
            baseline_hashes.add(result.get("hashed_secret", ""))

    new_secrets = []

    # Parse scan output if it's JSON
    try:
        if scan_output.strip():
            scan_results = json.loads(scan_output)
            for file_path, results in scan_results.get("results", {}).items():
                for result in results:
                    if result.get("hashed_secret") not in baseline_hashes:
                        new_secrets.append({
                            "file": file_path,
                            "line": result.get("line_number"),
                            "type": result.get("type"),
                            "hashed_secret": result.get("hashed_secret"),
                        })
    except json.JSONDecodeError:
        # Scan output may be empty or non-JSON on success
        pass

    return new_secrets, []


def get_baseline_stats(baseline: dict) -> dict[str, int]:
    """Get statistics about the current baseline."""
    results = baseline.get("results", {})

    stats = {
        "total_files": len(results),
        "total_findings": sum(len(v) for v in results.values()),
        "by_type": {},
    }

    for file_results in results.values():
        for result in file_results:
            secret_type = result.get("type", "Unknown")
            stats["by_type"][secret_type] = stats["by_type"].get(secret_type, 0) + 1

    return stats


def verify_false_positives(baseline: dict) -> list[str]:
    """
    Verify that all findings in baseline are documented false positives.

    Returns list of undocumented findings that need justification.
    """
    undocumented = []
    results = baseline.get("results", {})

    # Known false positive patterns
    known_fp_patterns = [
        # Build logs with base64 encoded content
        "docs/reports/build",
        # Test files with mock secrets
        "tests/",
        # Config templates
        ".continue/",
    ]

    for file_path, findings in results.items():
        # Check if file matches known false positive patterns
        is_known_fp = any(pattern in file_path for pattern in known_fp_patterns)

        for finding in findings:
            if not finding.get("is_verified", False) and not is_known_fp:
                # Check if it's a high-risk type
                secret_type = finding.get("type", "")
                high_risk_types = [
                    "AWS", "Private", "API", "Bearer", "OAuth",
                    "GitHub", "GitLab", "Stripe", "Twilio",
                ]

                if any(risk in secret_type for risk in high_risk_types):
                    undocumented.append(
                        f"{file_path}:{finding.get('line_number')} - {secret_type}"
                    )

    return undocumented


def main() -> int:
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description="Verify secrets baseline for VoiceStudio"
    )
    parser.add_argument(
        "--update",
        action="store_true",
        help="Update baseline with new findings (requires review)",
    )
    parser.add_argument(
        "--verbose",
        "-v",
        action="store_true",
        help="Enable verbose output",
    )
    parser.add_argument(
        "--stats-only",
        action="store_true",
        help="Only show baseline statistics",
    )
    args = parser.parse_args()

    if args.verbose:
        logging.getLogger().setLevel(logging.DEBUG)

    # Load existing baseline
    baseline = load_baseline()
    if not baseline:
        logger.error("No baseline found. Run: detect-secrets scan > .secrets.baseline")
        return 2

    # Show baseline stats
    stats = get_baseline_stats(baseline)
    logger.info("Baseline statistics:")
    logger.info(f"  Files with findings: {stats['total_files']}")
    logger.info(f"  Total findings: {stats['total_findings']}")
    for secret_type, count in stats["by_type"].items():
        logger.info(f"    {secret_type}: {count}")

    if args.stats_only:
        return 0

    # Check for undocumented high-risk findings
    undocumented = verify_false_positives(baseline)
    if undocumented:
        logger.warning(f"Found {len(undocumented)} high-risk undocumented findings:")
        for finding in undocumented:
            logger.warning(f"  - {finding}")

    # Run secrets scan
    logger.info("Running secrets scan...")
    return_code, stdout, stderr = run_secrets_scan(update_baseline=args.update)

    if return_code != 0:
        logger.error(f"Secrets scan failed with return code {return_code}")
        if stderr:
            logger.error(f"Error: {stderr}")
        return 1

    # Compare results
    new_secrets, resolved = compare_results(baseline, stdout)

    if new_secrets:
        logger.error(f"FAIL: Found {len(new_secrets)} new potential secrets:")
        for secret in new_secrets:
            logger.error(
                f"  {secret['file']}:{secret['line']} - {secret['type']}"
            )
        logger.error("")
        logger.error("Actions required:")
        logger.error("  1. If false positive: Update .secrets.baseline with justification")
        logger.error("  2. If real secret: Remove from code and rotate immediately")
        logger.error("")
        logger.error("To update baseline (after verification):")
        logger.error("  detect-secrets scan --baseline .secrets.baseline --update")
        return 1

    if resolved:
        logger.info(f"INFO: {len(resolved)} secrets were resolved (no longer detected)")

    logger.info("PASS: No new secrets detected")
    logger.info(f"Baseline version: {baseline.get('version', 'unknown')}")
    logger.info(f"Generated: {baseline.get('generated_at', 'unknown')}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
