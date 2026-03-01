#!/usr/bin/env python3
"""
Generate JSON schemas from Pydantic models.

GAP-X06: Extracts JSON schemas from all registered Pydantic models
and writes them to shared/contracts/ for frontend/backend synchronization.

Usage:
    python scripts/generate_schemas.py [--check] [--output-dir DIR]

Options:
    --check         Verify schemas are up-to-date without writing
    --output-dir    Output directory (default: shared/contracts)
    --verbose       Print verbose output
"""

import argparse
import hashlib
import json
import re
import sys
from datetime import datetime
from pathlib import Path
from typing import Any


def to_snake_case(name: str) -> str:
    """Convert CamelCase to snake_case."""
    s1 = re.sub(r"(.)([A-Z][a-z]+)", r"\1_\2", name)
    return re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", s1).lower()


def get_schema_hash(schema: dict[str, Any]) -> str:
    """Get a hash of the schema for change detection."""
    # Remove metadata that changes (like title which may differ)
    schema_copy = schema.copy()
    schema_copy.pop("title", None)
    return hashlib.md5(
        json.dumps(schema_copy, sort_keys=True).encode()
    ).hexdigest()[:12]


def load_existing_schema(path: Path) -> dict[str, Any] | None:
    """Load an existing schema file if it exists."""
    if path.exists():
        try:
            return json.loads(path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            return None
    return None


def generate_schemas(
    output_dir: Path,
    check_only: bool = False,
    verbose: bool = False,
) -> tuple[int, int, list[str]]:
    """
    Generate JSON schemas from all registered Pydantic models.

    Returns:
        Tuple of (schemas_written, schemas_unchanged, errors)
    """
    # Import models
    try:
        from backend.api.models import (
            ApiOk,
            BlendRequest,
            EmbeddingMap,
            LexiconEntry,
            MixAnalyzeRequest,
            SpectrogramRequest,
            StyleExtractRequest,
            TrainRequest,
            TtsRequest,
        )
        from backend.api.models_routes import ROUTE_MODELS
    except ImportError as e:
        return 0, 0, [f"Failed to import models: {e}"]

    # Collect all models
    base_models = [
        ApiOk, TrainRequest, TtsRequest, SpectrogramRequest,
        LexiconEntry, EmbeddingMap, MixAnalyzeRequest,
        StyleExtractRequest, BlendRequest,
    ]
    all_models = base_models + ROUTE_MODELS

    output_dir.mkdir(parents=True, exist_ok=True)

    schemas_written = 0
    schemas_unchanged = 0
    errors: list[str] = []
    registry_entries: dict[str, dict[str, str]] = {}

    for model in all_models:
        try:
            schema = model.model_json_schema()
            filename = f"{to_snake_case(model.__name__)}.schema.json"
            filepath = output_dir / filename

            # Add metadata
            schema["$schema"] = "http://json-schema.org/draft-07/schema#"
            schema["$id"] = f"voicestudio://{filename}"
            schema["$generated"] = datetime.utcnow().isoformat() + "Z"

            # Check if schema changed
            existing = load_existing_schema(filepath)
            if existing:
                existing_hash = get_schema_hash(existing)
                new_hash = get_schema_hash(schema)
                if existing_hash == new_hash:
                    schemas_unchanged += 1
                    if verbose:
                        print(f"  [unchanged] {filename}")
                    registry_entries[to_snake_case(model.__name__)] = {
                        "path": f"contracts/{filename}",
                        "version": "1.0.0",
                    }
                    continue

            if check_only:
                errors.append(f"Schema outdated: {filename}")
            else:
                filepath.write_text(
                    json.dumps(schema, indent=2, ensure_ascii=False) + "\n",
                    encoding="utf-8",
                )
                schemas_written += 1
                if verbose:
                    print(f"  [written] {filename}")

            registry_entries[to_snake_case(model.__name__)] = {
                "path": f"contracts/{filename}",
                "version": "1.0.0",
            }

        except (AttributeError, TypeError, ValueError) as e:
            err_msg = f"Failed to generate schema for {model.__name__}: {e}"
            errors.append(err_msg)

    return schemas_written, schemas_unchanged, errors


def update_registry(
    registry_path: Path,
    entries: dict[str, dict[str, str]],
    verbose: bool = False,
) -> bool:
    """Update the schema registry with new entries."""
    if registry_path.exists():
        try:
            registry = json.loads(registry_path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            registry = {"version": "1.0.0", "schemas": {}}
    else:
        registry = {"version": "1.0.0", "schemas": {}}

    # Merge entries (preserving existing versions)
    for name, entry in entries.items():
        if name not in registry.get("schemas", {}):
            registry.setdefault("schemas", {})[name] = entry
            if verbose:
                print(f"  [added] {name}")

    # Bump version
    current = registry.get("version", "1.0.0")
    parts = current.split(".")
    parts[-1] = str(int(parts[-1]) + 1)
    registry["version"] = ".".join(parts)
    registry["updated"] = datetime.utcnow().isoformat() + "Z"

    registry_path.write_text(
        json.dumps(registry, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    return True


def main() -> int:
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description="Generate JSON schemas from Pydantic models"
    )
    parser.add_argument(
        "--check",
        action="store_true",
        help="Verify schemas are up-to-date without writing",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path("shared/contracts"),
        help="Output directory for schemas",
    )
    parser.add_argument(
        "--registry",
        type=Path,
        default=Path("shared/schemas/_registry.json"),
        help="Path to schema registry",
    )
    parser.add_argument(
        "--verbose", "-v",
        action="store_true",
        help="Print verbose output",
    )
    args = parser.parse_args()

    print("Generating JSON schemas from Pydantic models...")
    print(f"  Output: {args.output_dir}")
    print(f"  Mode: {'check' if args.check else 'write'}")
    print()

    written, unchanged, errors = generate_schemas(
        output_dir=args.output_dir,
        check_only=args.check,
        verbose=args.verbose,
    )

    print()
    print("Results:")
    print(f"  Written: {written}")
    print(f"  Unchanged: {unchanged}")
    print(f"  Errors: {len(errors)}")

    if errors:
        print()
        print("Errors:")
        for error in errors:
            print(f"  - {error}")
        return 1

    if args.check and written > 0:
        print()
        print("Schemas are outdated. Run without --check to update.")
        return 1

    print()
    print("Schema generation complete.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
