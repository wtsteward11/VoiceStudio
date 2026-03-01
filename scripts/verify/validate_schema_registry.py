#!/usr/bin/env python3
"""
Validate Schema Registry (CI)

Recomputes content hashes for all schemas and exits 1 if any differ from
the committed _registry.json. Use in CI to ensure registry is up to date.

Usage:
  python scripts/validate_schema_registry.py
"""

import hashlib
import json
import sys
from pathlib import Path


def calculate_schema_hash(schema_path: Path) -> str:
    """Stable hash of a schema file (normalized JSON)."""
    with open(schema_path, encoding="utf-8") as f:
        content = json.load(f)
    normalized = json.dumps(content, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(normalized.encode("utf-8")).hexdigest()


def main() -> int:
    project_root = Path(__file__).resolve().parent.parent
    registry_path = project_root / "shared" / "schemas" / "_registry.json"
    if not registry_path.exists():
        print("Error: _registry.json not found", file=sys.stderr)
        return 1
    with open(registry_path, encoding="utf-8") as f:
        registry = json.load(f)
    mismatches = []
    for schema_path, metadata in registry.get("schemas", {}).items():
        full_path = project_root / schema_path
        if not full_path.exists():
            mismatches.append((schema_path, "file not found"))
            continue
        try:
            computed = calculate_schema_hash(full_path)
            recorded = metadata.get("hash", "")
            if recorded and computed != recorded:
                mismatches.append((schema_path, f"hash mismatch (computed {computed[:16]}..., recorded {recorded[:16]}...)"))
        except Exception as e:
            mismatches.append((schema_path, str(e)))
    if mismatches:
        print("Schema registry validation FAILED:", file=sys.stderr)
        for path, msg in mismatches:
            print(f"  {path}: {msg}", file=sys.stderr)
        print("Run: python scripts/update_schema_registry.py", file=sys.stderr)
        return 1
    print("Schema registry validation passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
