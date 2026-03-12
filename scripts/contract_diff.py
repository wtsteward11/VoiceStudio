#!/usr/bin/env python3
"""
Contract Diff — Detect OpenAPI schema drift between schema file and stored contract.

Compares the hash of docs/api/openapi.json to the stored baseline. Fails if the
schema file has changed without an intentional update. Use export_openapi_schema.py
to regenerate the schema from the backend, then run with --update to refresh the hash.

Usage:
    python scripts/contract_diff.py           # Check for drift, exit 1 if found
    python scripts/contract_diff.py --update # Update stored hash (after intentional change)

Exit codes:
    0 - No drift (or --update succeeded)
    1 - Drift detected or error
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

# Project root
PROJECT_ROOT = Path(__file__).resolve().parent.parent
SCHEMA_FILE = PROJECT_ROOT / "docs" / "api" / "openapi.json"
HASH_FILE = PROJECT_ROOT / "tests" / "contract" / ".openapi_schema_hash"


def get_schema_hash(schema: dict) -> str:
    """Compute stable hash of paths and components (matches test_openapi_schema_drift)."""
    normalized = {
        "paths": schema.get("paths", {}),
        "components": schema.get("components", {}),
    }
    schema_str = json.dumps(normalized, sort_keys=True)
    return hashlib.sha256(schema_str.encode()).hexdigest()


def load_stored_hash() -> str:
    """Load stored schema hash."""
    if HASH_FILE.exists():
        return HASH_FILE.read_text().strip()
    return ""


def save_hash(hash_value: str) -> None:
    """Save schema hash to file."""
    HASH_FILE.parent.mkdir(parents=True, exist_ok=True)
    HASH_FILE.write_text(hash_value)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Detect OpenAPI schema drift between schema file and stored contract"
    )
    parser.add_argument(
        "--update",
        action="store_true",
        help="Update stored hash from current schema file (after intentional change)",
    )
    args = parser.parse_args()

    if not SCHEMA_FILE.exists():
        print(f"[ERROR] Schema file not found: {SCHEMA_FILE}", file=sys.stderr)
        print("        Run: python scripts/export_openapi_schema.py", file=sys.stderr)
        return 1

    with open(SCHEMA_FILE, encoding="utf-8") as f:
        schema = json.load(f)

    current_hash = get_schema_hash(schema)
    stored_hash = load_stored_hash()

    if args.update:
        save_hash(current_hash)
        print(f"[OK] Stored hash updated: {HASH_FILE}")
        print(f"     Hash: {current_hash[:16]}...")
        return 0

    if not stored_hash:
        print("[WARN] No stored hash found. First run - saving hash.")
        save_hash(current_hash)
        print("       Run again to verify. Or use --update to confirm.")
        return 0

    if current_hash == stored_hash:
        print("[PASS] Contract diff: No schema drift detected")
        return 0

    print("[FAIL] Contract diff: OpenAPI schema has changed (drift detected)", file=sys.stderr)
    print(f"       Stored:  {stored_hash[:16]}...", file=sys.stderr)
    print(f"       Current: {current_hash[:16]}...", file=sys.stderr)
    print("", file=sys.stderr)
    print("       If intentional:", file=sys.stderr)
    print("       1. python scripts/export_openapi_schema.py --update-hash", file=sys.stderr)
    print("       2. Or: python scripts/contract_diff.py --update", file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
