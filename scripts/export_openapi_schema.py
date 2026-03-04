#!/usr/bin/env python3
"""
Export OpenAPI Schema Script
Exports the FastAPI OpenAPI schema to docs/api/openapi.json

Usage:
    python scripts/export_openapi_schema.py
    python scripts/export_openapi_schema.py --output path/to/openapi.json
    python scripts/export_openapi_schema.py --update-hash  # Also update drift test hash
"""

import argparse
import hashlib
import json
import sys
from pathlib import Path

from _env_setup import PROJECT_ROOT

# Backend path already in sys.path via _env_setup


def main():
    parser = argparse.ArgumentParser(description="Export OpenAPI schema from FastAPI app")
    parser.add_argument(
        "--output", "-o",
        type=str,
        default=None,
        help="Output file path (default: docs/api/openapi.json)"
    )
    parser.add_argument(
        "--update-hash",
        action="store_true",
        help="Also update tests/contract/.openapi_schema_hash for drift test"
    )
    args = parser.parse_args()

    try:
        from backend.api.main import app

        # Generate OpenAPI schema
        openapi_schema = app.openapi()

        # Determine output path
        if args.output:
            output_file = Path(args.output)
        else:
            output_file = PROJECT_ROOT / "docs" / "api" / "openapi.json"

        # Ensure output directory exists
        output_file.parent.mkdir(parents=True, exist_ok=True)

        # Write schema to file
        with open(output_file, "w", encoding="utf-8") as f:
            json.dump(openapi_schema, f, indent=2, ensure_ascii=False)

        print(f"[OK] OpenAPI schema exported to {output_file}")
        print(
            f"   Schema version: {openapi_schema.get('info', {}).get('version', 'unknown')}"
        )
        print(f"   Total paths: {len(openapi_schema.get('paths', {}))}")

        if args.update_hash:
            hash_file = PROJECT_ROOT / "tests" / "contract" / ".openapi_schema_hash"
            normalized = {
                "paths": openapi_schema.get("paths", {}),
                "components": openapi_schema.get("components", {}),
            }
            h = hashlib.sha256(json.dumps(normalized, sort_keys=True).encode()).hexdigest()
            hash_file.parent.mkdir(parents=True, exist_ok=True)
            hash_file.write_text(h)
            print(f"   Hash updated: {hash_file}")

        return 0

    except Exception as e:
        print(f"[ERROR] Error exporting OpenAPI schema: {e}", file=sys.stderr)
        import traceback
        traceback.print_exc()
        return 1


if __name__ == "__main__":
    sys.exit(main())
