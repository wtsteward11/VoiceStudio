#!/usr/bin/env python3
"""
analyze_binlog_trends.py - Analyze build trends from binlog metrics

This script analyzes historical build metrics to detect:
- XAML file count growth
- Binlog size increases (complexity indicator)
- Build success rate
- XAML compiler invocation trends

Usage:
    python scripts/analyze_binlog_trends.py [--trend-file PATH] [--output FORMAT]

Exit codes:
    0: Analysis complete, no regressions detected
    1: Regressions detected (binlog size increase > threshold, etc.)
"""

import argparse
import json
import sys
from pathlib import Path

DEFAULT_TREND_FILE = ".buildlogs/binlog-metrics/build-trend.jsonl"
BINLOG_SIZE_INCREASE_THRESHOLD = 1.5  # 50% increase triggers warning
XAML_FILE_INCREASE_THRESHOLD = 10  # More than 10 new files triggers notice


def load_trend_data(trend_file: Path) -> list[dict]:
    """Load trend data from JSONL file."""
    if not trend_file.exists():
        print(f"Trend file not found: {trend_file}")
        return []

    entries = []
    with open(trend_file, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                try:
                    entries.append(json.loads(line))
                except json.JSONDecodeError as e:
                    print(f"Warning: Skipping invalid JSON line: {e}")

    return entries


def analyze_trends(entries: list[dict]) -> dict:
    """Analyze trends in build metrics."""
    if len(entries) < 2:
        return {
            "status": "insufficient_data",
            "message": f"Need at least 2 data points, have {len(entries)}",
            "regressions": []
        }

    # Sort by timestamp
    sorted_entries = sorted(entries, key=lambda x: x.get("timestamp", ""))

    latest = sorted_entries[-1]
    previous = sorted_entries[-2]
    first = sorted_entries[0]

    regressions = []
    warnings = []
    notices = []

    # Analyze binlog size trends
    latest_size = latest.get("binlog_debug_size_mb", 0)
    previous_size = previous.get("binlog_debug_size_mb", 0)
    first_size = first.get("binlog_debug_size_mb", 0)

    if previous_size > 0 and latest_size > 0:
        size_ratio = latest_size / previous_size
        if size_ratio > BINLOG_SIZE_INCREASE_THRESHOLD:
            regressions.append({
                "type": "binlog_size_increase",
                "message": f"Binlog size increased {size_ratio:.1f}x ({previous_size:.1f}MB -> {latest_size:.1f}MB)",
                "severity": "warning"
            })

    # Analyze overall growth
    if first_size > 0 and latest_size > 0:
        total_growth = latest_size / first_size
        if total_growth > 2.0:
            warnings.append({
                "type": "binlog_total_growth",
                "message": f"Binlog size has grown {total_growth:.1f}x since first measurement",
                "severity": "info"
            })

    # Analyze XAML file count
    latest_xaml = latest.get("xaml_file_count", 0)
    previous_xaml = previous.get("xaml_file_count", 0)

    if previous_xaml > 0 and latest_xaml > 0:
        xaml_diff = latest_xaml - previous_xaml
        if xaml_diff > XAML_FILE_INCREASE_THRESHOLD:
            notices.append({
                "type": "xaml_file_increase",
                "message": f"XAML file count increased by {xaml_diff} ({previous_xaml} -> {latest_xaml})",
                "severity": "info"
            })

    # Analyze build success rate (last 10 builds)
    recent = sorted_entries[-10:] if len(sorted_entries) >= 10 else sorted_entries
    debug_successes = sum(1 for e in recent if e.get("build_debug_success", False))
    release_successes = sum(1 for e in recent if e.get("build_release_success", False))

    debug_rate = debug_successes / len(recent) * 100
    release_rate = release_successes / len(recent) * 100

    if debug_rate < 80:
        regressions.append({
            "type": "debug_success_rate",
            "message": f"Debug build success rate dropped to {debug_rate:.0f}% (last {len(recent)} builds)",
            "severity": "warning"
        })

    if release_rate < 80:
        regressions.append({
            "type": "release_success_rate",
            "message": f"Release build success rate dropped to {release_rate:.0f}% (last {len(recent)} builds)",
            "severity": "warning"
        })

    return {
        "status": "analyzed",
        "data_points": len(entries),
        "time_range": {
            "first": first.get("timestamp"),
            "latest": latest.get("timestamp")
        },
        "current_metrics": {
            "binlog_debug_size_mb": latest_size,
            "xaml_file_count": latest_xaml,
            "debug_success_rate": debug_rate,
            "release_success_rate": release_rate
        },
        "regressions": regressions,
        "warnings": warnings,
        "notices": notices
    }


def print_report(analysis: dict, output_format: str = "text") -> None:
    """Print analysis report."""
    if output_format == "json":
        print(json.dumps(analysis, indent=2))
        return

    # Text format
    print("\n" + "=" * 60)
    print("Build Trend Analysis Report")
    print("=" * 60)

    if analysis["status"] == "insufficient_data":
        print(f"\n{analysis['message']}")
        print("Run more builds to gather trend data.")
        return

    print(f"\nData points analyzed: {analysis['data_points']}")
    print(f"Time range: {analysis['time_range']['first']} to {analysis['time_range']['latest']}")

    metrics = analysis["current_metrics"]
    print("\n--- Current Metrics ---")
    print(f"  Binlog size (Debug): {metrics['binlog_debug_size_mb']:.1f} MB")
    print(f"  XAML file count: {metrics['xaml_file_count']}")
    print(f"  Debug success rate: {metrics['debug_success_rate']:.0f}%")
    print(f"  Release success rate: {metrics['release_success_rate']:.0f}%")

    if analysis["regressions"]:
        print(f"\n--- REGRESSIONS ({len(analysis['regressions'])}) ---")
        for reg in analysis["regressions"]:
            print(f"  [!] {reg['message']}")

    if analysis["warnings"]:
        print(f"\n--- Warnings ({len(analysis['warnings'])}) ---")
        for warn in analysis["warnings"]:
            print(f"  [W] {warn['message']}")

    if analysis["notices"]:
        print(f"\n--- Notices ({len(analysis['notices'])}) ---")
        for notice in analysis["notices"]:
            print(f"  [i] {notice['message']}")

    if not analysis["regressions"] and not analysis["warnings"]:
        print("\n[OK] No regressions detected")

    print("=" * 60)


def main() -> int:
    parser = argparse.ArgumentParser(description="Analyze build trends from binlog metrics")
    parser.add_argument(
        "--trend-file",
        type=Path,
        default=Path(DEFAULT_TREND_FILE),
        help=f"Path to trend JSONL file (default: {DEFAULT_TREND_FILE})"
    )
    parser.add_argument(
        "--output",
        choices=["text", "json"],
        default="text",
        help="Output format (default: text)"
    )
    args = parser.parse_args()

    # Find project root
    script_dir = Path(__file__).parent
    project_root = script_dir.parent
    trend_file = project_root / args.trend_file if not args.trend_file.is_absolute() else args.trend_file

    entries = load_trend_data(trend_file)
    analysis = analyze_trends(entries)
    print_report(analysis, args.output)

    # Return non-zero if regressions detected
    if analysis.get("regressions"):
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
