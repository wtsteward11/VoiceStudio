"""
CI gate: fail if OpenAPI contract artifacts are inconsistent.

Verifies that docs/api/openapi.json hashes to the stored baseline in
tests/contract/.openapi_schema_hash. Fails if the committed schema and hash
are out of sync (e.g. openapi.json was edited without updating the hash).

When the backend API changes intentionally:
  1. Run: python scripts/export_openapi_schema.py --update-hash
  2. Commit docs/api/openapi.json and tests/contract/.openapi_schema_hash

Run: python -m pytest tests/ci/test_contract_drift_gate.py -v
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent
SCHEMA_FILE = ROOT / "docs" / "api" / "openapi.json"
HASH_FILE = ROOT / "tests" / "contract" / ".openapi_schema_hash"


def _get_schema_hash(schema: dict) -> str:
    """Compute fingerprint of paths + components (matches export_openapi_schema.py)."""
    normalized = {
        "paths": schema.get("paths", {}),
        "components": schema.get("components", {}),
    }
    return hashlib.sha256(
        json.dumps(normalized, sort_keys=True).encode()
    ).hexdigest()


def _load_stored_hash() -> str:
    """Load committed schema hash."""
    if HASH_FILE.exists():
        return HASH_FILE.read_text().strip()
    return ""


def test_contract_drift_gate() -> None:
    """Fail if exported schema fingerprint differs from committed contract."""
    if not SCHEMA_FILE.exists():
        pytest.fail(
            f"Schema file not found: {SCHEMA_FILE}. "
            "Run: python scripts/export_openapi_schema.py"
        )

    with open(SCHEMA_FILE, encoding="utf-8") as f:
        schema = json.load(f)

    current_hash = _get_schema_hash(schema)
    stored_hash = _load_stored_hash()

    if not stored_hash:
        pytest.fail(
            "No stored contract hash. Run: python scripts/export_openapi_schema.py --update-hash"
        )

    assert current_hash == stored_hash, (
        "OpenAPI contract drift detected. Backend schema has changed.\n"
        f"  Expected hash: {stored_hash}\n"
        f"  Current hash:  {current_hash}\n"
        "If this change is intentional:\n"
        "  1. Run: python scripts/export_openapi_schema.py --update-hash\n"
        "  2. Commit docs/api/openapi.json and tests/contract/.openapi_schema_hash"
    )
