#!/usr/bin/env python3
"""
CI check: fail if repo gains payload dirs or oversized files that brick Cursor.

Uses .ci/repo_payload_policy.json (NOT the legacy allowlist).

Payload dirs: backups/, data/audio_uploads/, data/recordings/, installer/runtime/,
installer/runtime__DISABLED/. Each has a baseline (count, bytes) and optionally a manifest.
Mode "forbidden" = fail if any file exists. Mode "baseline" = fail if growth beyond baseline.

Large files: fail if any file > max_file_mb unless in large_file_exceptions (with justification).
Exceptions: fail if file grows beyond stored_size + growth_threshold.

Usage:
  python scripts/ci/check_repo_payloads.py              # CI mode (strict)
  python scripts/ci/check_repo_payloads.py --update-baselines      # Update dir baselines
  python scripts/ci/check_repo_payloads.py --refresh-large-file-sizes  # Update sizes for existing exceptions
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
POLICY_PATH = ROOT / ".ci" / "repo_payload_policy.json"

PAYLOAD_DIRS = [
    "backups",
    "data/audio_uploads",
    "data/recordings",
    "installer/runtime",
    "installer/runtime__DISABLED",
]

SKIP_DIRS = {".git", ".venv", "venv", "node_modules", "__pycache__", ".buildlogs", "artifacts", "bin", "obj"}


def normalize_path(p: Path) -> str:
    """Return path relative to ROOT with forward slashes."""
    try:
        rel = p.relative_to(ROOT)
    except ValueError:
        return str(p).replace("\\", "/")
    return str(rel).replace("\\", "/")


def load_policy() -> dict:
    """Load policy JSON. Raise if missing."""
    if not POLICY_PATH.exists():
        print(f"ERROR: Policy file not found: {POLICY_PATH}")
        sys.exit(1)
    data = json.loads(POLICY_PATH.read_text(encoding="utf-8"))
    data.setdefault("settings", {})
    data.setdefault("payload_dir_baselines", [])
    data.setdefault("large_file_exceptions", [])
    return data


def save_policy(data: dict) -> None:
    """Write policy JSON."""
    POLICY_PATH.parent.mkdir(parents=True, exist_ok=True)
    POLICY_PATH.write_text(json.dumps(data, indent=2), encoding="utf-8")


def get_payload_dir_baseline(policy: dict, dir_path: str) -> dict | None:
    """Return baseline entry for dir_path or None."""
    for entry in policy.get("payload_dir_baselines", []):
        if entry.get("path", "").replace("\\", "/") == dir_path.replace("\\", "/"):
            return entry
    return None


def scan_payload_dir(dir_rel: str) -> tuple[list[tuple[Path, int]], int, int]:
    """Return ([(path, size), ...], total_count, total_bytes) for dir."""
    full = ROOT / dir_rel
    if not full.exists():
        return [], 0, 0
    files: list[tuple[Path, int]] = []
    total_bytes = 0
    for f in full.rglob("*"):
        if f.is_file():
            sz = f.stat().st_size
            files.append((f, sz))
            total_bytes += sz
    return files, len(files), total_bytes


def collect_large_files(max_bytes: int) -> list[tuple[Path, int]]:
    """Return [(path, size), ...] for git-tracked files > max_bytes (M8: source-only repo)."""
    result: list[tuple[Path, int]] = []
    try:
        out = subprocess.run(
            ["git", "ls-files"],
            cwd=ROOT,
            capture_output=True,
            text=True,
            check=False,
            timeout=30,
        )
        if out.returncode != 0:
            return []
        for line in out.stdout.strip().splitlines():
            if not line.strip():
                continue
            path = ROOT / line.replace("/", os.sep)
            if not path.exists() or not path.is_file():
                continue
            if any(skip in path.parts for skip in SKIP_DIRS):
                continue
            sz = path.stat().st_size
            if sz > max_bytes:
                result.append((path, sz))
    except (subprocess.TimeoutExpired, FileNotFoundError, OSError):
        pass
    return result


def check_strict() -> int:
    """CI mode: fail on payload dir growth or new/large files. Return 1 if violations."""
    policy = load_policy()
    settings = policy.get("settings", {})
    max_mb = settings.get("max_file_mb", 25)
    growth_mb = settings.get("growth_threshold_mb", 5)
    max_bytes = max_mb * 1024 * 1024
    growth_bytes = growth_mb * 1024 * 1024

    exceptions = policy.get("large_file_exceptions", [])
    # M8: large_file_exceptions must be empty (payloads migrated to VOICESTUDIO_PAYLOADS_ROOT)
    if len(exceptions) > 0:
        print("M8: large_file_exceptions must be empty; migrate payloads first (scripts/dev/payload_migrate.ps1 -Execute)")
        return 1

    exceptions_by_path: dict[str, dict] = {}

    errors: list[str] = []

    # 1) Payload dir checks
    for dir_rel in PAYLOAD_DIRS:
        baseline = get_payload_dir_baseline(policy, dir_rel)
        files, count, total = scan_payload_dir(dir_rel)

        if baseline is None:
            if count > 0:
                errors.append(f"PAYLOAD DIR: {dir_rel} has {count} files but no baseline (add baseline or remove files)")
            continue

        mode = baseline.get("mode", "baseline")
        base_count = baseline.get("baseline_file_count", 0)
        base_bytes = baseline.get("baseline_total_bytes", 0)
        max_growth = baseline.get("max_growth_bytes", 0)

        if mode == "forbidden":
            if count > 0:
                errors.append(f"FORBIDDEN DIR: {dir_rel} must be empty (found {count} files)")
            continue

        if mode == "baseline":
            if count > base_count:
                errors.append(f"PAYLOAD GROWTH: {dir_rel} file count {count} > baseline {base_count}")
            if total > base_bytes + max_growth:
                errors.append(f"PAYLOAD GROWTH: {dir_rel} total bytes {total} > baseline {base_bytes} + max_growth {max_growth}")
            manifest = baseline.get("manifest")
            if manifest:
                manifest_set = set(p.replace("\\", "/") for p in manifest)
                for f, _ in files:
                    rel = normalize_path(f)
                    if rel not in manifest_set:
                        errors.append(f"NEW FILE IN PAYLOAD DIR: {rel} (not in manifest)")

    # 2) Large file check
    large_files = collect_large_files(max_bytes)
    for path, size in large_files:
        rel = normalize_path(path)
        exc = exceptions_by_path.get(rel)
        if exc is None:
            errors.append(f"OVERSIZED: {rel} ({size // (1024*1024)}MB > {max_mb}MB, add to large_file_exceptions with justification)")
        else:
            stored = exc.get("stored_size_bytes", 0)
            if size > stored + growth_bytes:
                errors.append(f"EXCEPTION GROWTH: {rel} grew beyond +{growth_mb}MB (was {stored}, now {size})")

    for err in errors:
        print(err)
    return 1 if errors else 0


def update_baselines() -> int:
    """Update payload_dir_baselines from current repo. Do NOT add new large-file exceptions."""
    policy = load_policy()
    baselines: list[dict] = []

    MANIFEST_DIRS = {"backups", "data/audio_uploads", "data/recordings"}

    for dir_rel in PAYLOAD_DIRS:
        files, count, total = scan_payload_dir(dir_rel)
        existing = get_payload_dir_baseline(policy, dir_rel) or {}

        entry: dict = {
            "path": dir_rel,
            "mode": existing.get("mode", "forbidden" if count == 0 else "baseline"),
            "baseline_file_count": count,
            "baseline_total_bytes": total,
            "max_growth_bytes": existing.get("max_growth_bytes", 0),
        }
        if dir_rel in MANIFEST_DIRS and count > 0:
            entry["manifest"] = sorted(normalize_path(f) for f, _ in files)
        baselines.append(entry)
        print(f"Updated baseline: {dir_rel} ({count} files, {total} bytes)")

    policy["payload_dir_baselines"] = baselines
    save_policy(policy)
    print("Baselines updated. large_file_exceptions unchanged.")
    return 0


def refresh_large_file_sizes() -> int:
    """Update stored_size_bytes for paths already in large_file_exceptions. Do NOT add new paths."""
    policy = load_policy()
    exceptions = policy.get("large_file_exceptions", [])
    max_mb = policy.get("settings", {}).get("max_file_mb", 25)
    max_bytes = max_mb * 1024 * 1024

    updated = 0
    for exc in exceptions:
        path_str = exc.get("path", "")
        if not path_str:
            continue
        full = ROOT / path_str
        if full.exists() and full.is_file():
            sz = full.stat().st_size
            if sz > max_bytes and exc.get("stored_size_bytes") != sz:
                exc["stored_size_bytes"] = sz
                updated += 1
                print(f"Refreshed: {path_str} -> {sz} bytes")

    policy["large_file_exceptions"] = exceptions
    save_policy(policy)
    print(f"Refreshed {updated} exception sizes. No new paths added.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="CI repo payload tripwire (policy-based)")
    parser.add_argument("--update-baselines", action="store_true", help="Update payload_dir_baselines from repo")
    parser.add_argument("--refresh-large-file-sizes", action="store_true", help="Update stored sizes for existing exceptions only")
    args = parser.parse_args()

    if args.update_baselines:
        return update_baselines()
    if args.refresh_large_file_sizes:
        return refresh_large_file_sizes()
    return check_strict()


if __name__ == "__main__":
    sys.exit(main())
