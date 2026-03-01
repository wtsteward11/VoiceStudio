#!/usr/bin/env python3
"""
CI check: fail if repo gains payload dirs or oversized files that brick Cursor.

Payload dirs (fail if non-empty and files not allowlisted):
- backups/
- data/audio_uploads/
- data/recordings/
- installer/runtime/
- installer/runtime__DISABLED/

Size policy: fail if any file > max_file_mb (default 25MB) unless allowlisted.
Allowlisted files: fail if they grow beyond stored_size + 5MB.

Usage:
  python scripts/ci/check_repo_payloads.py           # CI mode (strict)
  python scripts/ci/check_repo_payloads.py --update-allowlist  # Add existing payloads to allowlist
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
ALLOWLIST_PATH = ROOT / ".ci" / "repo_payload_allowlist.json"

PAYLOAD_DIRS = [
    "backups",
    "data/audio_uploads",
    "data/recordings",
    "installer/runtime",
    "installer/runtime__DISABLED",
]

GROWTH_THRESHOLD_MB = 5


def load_allowlist() -> dict:
    """Load allowlist JSON. Return empty structure if missing."""
    if not ALLOWLIST_PATH.exists():
        return {"allowed_paths": [], "max_file_mb": 25, "file_sizes": {}}
    data = json.loads(ALLOWLIST_PATH.read_text(encoding="utf-8"))
    data.setdefault("allowed_paths", [])
    data.setdefault("max_file_mb", 25)
    data.setdefault("file_sizes", {})
    return data


def save_allowlist(data: dict) -> None:
    """Write allowlist JSON."""
    ALLOWLIST_PATH.parent.mkdir(parents=True, exist_ok=True)
    ALLOWLIST_PATH.write_text(json.dumps(data, indent=2), encoding="utf-8")


def normalize_path(p: Path) -> str:
    """Return path relative to ROOT with forward slashes."""
    try:
        rel = p.relative_to(ROOT)
    except ValueError:
        return str(p).replace("\\", "/")
    return str(rel).replace("\\", "/")


def collect_payload_files() -> list[tuple[Path, int]]:
    """Return [(path, size_bytes), ...] for all files in payload dirs and files > max_mb."""
    allowlist = load_allowlist()
    max_mb = allowlist.get("max_file_mb", 25)
    max_bytes = max_mb * 1024 * 1024

    seen: set[Path] = set()
    result: list[tuple[Path, int]] = []

    for dir_rel in PAYLOAD_DIRS:
        full = ROOT / dir_rel
        if not full.exists():
            continue
        for f in full.rglob("*"):
            if f.is_file() and f not in seen:
                seen.add(f)
                result.append((f, f.stat().st_size))

    skip_dirs = {".git", ".venv", "venv", "node_modules", "__pycache__", ".buildlogs", "artifacts"}
    for f in ROOT.rglob("*"):
        if any(skip in f.parts for skip in skip_dirs):
            continue
        if f.is_file() and f.stat().st_size > max_bytes and f not in seen:
            seen.add(f)
            result.append((f, f.stat().st_size))

    return result


def check_strict() -> int:
    """CI mode: fail on new payload files or growth. Return 1 if violations."""
    allowlist = load_allowlist()
    allowed = set(p.replace("\\", "/") for p in allowlist["allowed_paths"])
    file_sizes = allowlist.get("file_sizes", {})
    max_mb = allowlist.get("max_file_mb", 25)
    growth_limit_bytes = GROWTH_THRESHOLD_MB * 1024 * 1024

    errors: list[str] = []

    payload_files = collect_payload_files()

    for path, size in payload_files:
        rel = normalize_path(path)
        if rel in allowed:
            stored = file_sizes.get(rel, 0)
            if size > stored + growth_limit_bytes:
                errors.append(f"ALLOWLIST GROWTH: {rel} grew beyond +{GROWTH_THRESHOLD_MB}MB (was {stored}, now {size})")
        else:
            if size > max_mb * 1024 * 1024:
                errors.append(f"OVERSIZED: {rel} ({size // (1024*1024)}MB > {max_mb}MB)")
            else:
                errors.append(f"NEW PAYLOAD: {rel} (add to allowlist or remove)")

    for err in errors:
        print(err)
    return 1 if errors else 0


def update_allowlist() -> int:
    """Scan repo, add existing payload files to allowlist."""
    allowlist = load_allowlist()
    allowed = set(allowlist["allowed_paths"])
    file_sizes = dict(allowlist.get("file_sizes", {}))

    payload_files = collect_payload_files()
    added = 0
    for path, size in payload_files:
        rel = normalize_path(path)
        if rel not in allowed:
            allowed.add(rel)
            file_sizes[rel] = size
            added += 1
            print(f"Added: {rel} ({size} bytes)")
        else:
            file_sizes[rel] = size

    allowlist["allowed_paths"] = sorted(allowed)
    allowlist["file_sizes"] = file_sizes
    save_allowlist(allowlist)
    print(f"Updated allowlist: {added} new, {len(allowed)} total")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="CI repo payload tripwire")
    parser.add_argument("--update-allowlist", action="store_true", help="Add existing payloads to allowlist")
    args = parser.parse_args()

    if args.update_allowlist:
        return update_allowlist()
    return check_strict()


if __name__ == "__main__":
    sys.exit(main())
