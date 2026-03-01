#!/usr/bin/env python3
"""
OpenAPI Spec Generation Script

Generates the OpenAPI specification from the FastAPI application.
Supports validation, diff detection, and CI integration.

Usage:
    python scripts/generate_openapi.py                    # Generate spec
    python scripts/generate_openapi.py --validate         # Validate existing spec
    python scripts/generate_openapi.py --check-drift      # Check for drift (CI mode)
    python scripts/generate_openapi.py --output custom.json  # Custom output path

Phase 2.1: API Contract Hardening - OpenAPI Generation
"""

import argparse
import hashlib
import json
import logging
import sys
from datetime import datetime
from pathlib import Path
from typing import Any

# Add project root to path for imports
PROJECT_ROOT = Path(__file__).parent.parent
sys.path.insert(0, str(PROJECT_ROOT))

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(levelname)s - %(message)s"
)
logger = logging.getLogger(__name__)

# Default output path
DEFAULT_OUTPUT_PATH = PROJECT_ROOT / "docs" / "api" / "openapi.json"


def get_openapi_schema() -> dict[str, Any]:
    """
    Import the FastAPI app and extract its OpenAPI schema.

    Returns:
        OpenAPI schema dictionary
    """
    try:
        from backend.api.main import app

        # Get the OpenAPI schema
        schema = app.openapi()

        # Add generation metadata
        schema["info"]["x-generated-at"] = datetime.utcnow().isoformat() + "Z"
        schema["info"]["x-generator"] = "VoiceStudio OpenAPI Generator"

        return schema
    except ImportError as e:
        logger.error(f"Failed to import FastAPI app: {e}")
        logger.error("Make sure you're running from the project root with dependencies installed.")
        raise
    except Exception as e:
        logger.error(f"Failed to generate OpenAPI schema: {e}")
        raise


def compute_schema_hash(schema: dict[str, Any]) -> str:
    """
    Compute a stable hash of the schema for drift detection.

    Excludes volatile fields like timestamps.
    """
    # Create a copy without volatile fields
    schema_copy = json.loads(json.dumps(schema))

    # Remove fields that change between generations
    if "info" in schema_copy:
        schema_copy["info"].pop("x-generated-at", None)

    # Sort keys for stable hashing
    stable_json = json.dumps(schema_copy, sort_keys=True)
    return hashlib.sha256(stable_json.encode()).hexdigest()[:16]


def validate_openapi_schema(schema: dict[str, Any]) -> bool:
    """
    Validate the OpenAPI schema structure.

    Returns:
        True if valid, False otherwise
    """
    required_fields = ["openapi", "info", "paths"]

    for field in required_fields:
        if field not in schema:
            logger.error(f"Missing required OpenAPI field: {field}")
            return False

    # Check OpenAPI version
    openapi_version = schema.get("openapi", "")
    if not openapi_version.startswith("3."):
        logger.error(f"Unsupported OpenAPI version: {openapi_version}")
        return False

    # Check info section
    info = schema.get("info", {})
    if not info.get("title"):
        logger.warning("OpenAPI spec missing 'info.title'")
    if not info.get("version"):
        logger.warning("OpenAPI spec missing 'info.version'")

    # Count endpoints
    paths = schema.get("paths", {})
    endpoint_count = sum(
        len([m for m in path_item if m in ("get", "post", "put", "delete", "patch")])
        for path_item in paths.values()
    )

    logger.info(f"OpenAPI spec has {len(paths)} paths with {endpoint_count} operations")

    return True


def write_schema(schema: dict[str, Any], output_path: Path) -> None:
    """Write the schema to a JSON file."""
    output_path.parent.mkdir(parents=True, exist_ok=True)

    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(schema, f, indent=2, ensure_ascii=False)

    logger.info(f"OpenAPI spec written to: {output_path}")


def load_existing_schema(path: Path) -> dict[str, Any] | None:
    """Load existing schema from file if it exists."""
    if not path.exists():
        return None

    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except json.JSONDecodeError as e:
        logger.error(f"Failed to parse existing schema: {e}")
        return None


def check_drift(new_schema: dict[str, Any], existing_path: Path) -> bool:
    """
    Check if the new schema differs from the existing one.

    Returns:
        True if schemas match (no drift), False if there's drift
    """
    existing_schema = load_existing_schema(existing_path)

    if existing_schema is None:
        logger.warning(f"No existing schema at {existing_path}")
        return False

    new_hash = compute_schema_hash(new_schema)
    existing_hash = compute_schema_hash(existing_schema)

    if new_hash != existing_hash:
        logger.error("API contract drift detected!")
        logger.error(f"  Existing hash: {existing_hash}")
        logger.error(f"  New hash:      {new_hash}")

        # Report specific differences
        report_schema_diff(existing_schema, new_schema)
        return False

    logger.info("No API contract drift detected")
    return True


def report_schema_diff(old: dict[str, Any], new: dict[str, Any]) -> None:
    """Report differences between two schemas."""
    old_paths = set(old.get("paths", {}).keys())
    new_paths = set(new.get("paths", {}).keys())

    added = new_paths - old_paths
    removed = old_paths - new_paths

    if added:
        logger.info(f"  Added endpoints: {sorted(added)}")
    if removed:
        logger.info(f"  Removed endpoints: {sorted(removed)}")

    # Check for modified endpoints
    common_paths = old_paths & new_paths
    modified = []
    for path in common_paths:
        old_methods = set(old["paths"][path].keys())
        new_methods = set(new["paths"][path].keys())
        if old_methods != new_methods:
            modified.append(path)

    if modified:
        logger.info(f"  Modified endpoints: {sorted(modified)}")


def main() -> int:
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description="Generate and validate OpenAPI specification"
    )
    parser.add_argument(
        "--output", "-o",
        type=Path,
        default=DEFAULT_OUTPUT_PATH,
        help=f"Output path for OpenAPI spec (default: {DEFAULT_OUTPUT_PATH})"
    )
    parser.add_argument(
        "--validate",
        action="store_true",
        help="Only validate the schema, don't write"
    )
    parser.add_argument(
        "--check-drift",
        action="store_true",
        help="Check for drift against existing spec (CI mode)"
    )
    parser.add_argument(
        "--verbose", "-v",
        action="store_true",
        help="Enable verbose output"
    )

    args = parser.parse_args()

    if args.verbose:
        logging.getLogger().setLevel(logging.DEBUG)

    try:
        logger.info("Generating OpenAPI schema...")
        schema = get_openapi_schema()

        # Validate schema structure
        if not validate_openapi_schema(schema):
            logger.error("Schema validation failed")
            return 1

        schema_hash = compute_schema_hash(schema)
        logger.info(f"Schema hash: {schema_hash}")

        # Check drift mode (for CI)
        if args.check_drift:
            if check_drift(schema, args.output):
                return 0
            else:
                logger.error("Run 'python scripts/generate_openapi.py' to update the spec")
                return 1

        # Validate only mode
        if args.validate:
            logger.info("Validation successful")
            return 0

        # Write schema
        write_schema(schema, args.output)
        logger.info("OpenAPI spec generation complete")

        return 0

    except Exception as e:
        logger.error(f"Failed to generate OpenAPI spec: {e}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
