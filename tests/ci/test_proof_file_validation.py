"""
Proof File Validation CI Gate.

Validates ALL docs/reports/verification/PROOF_*.json files against schema and semantics.
Catches corruption without relying on STATE.md references.

FCM-009 item 6: CI check for proof integrity.
"""

from __future__ import annotations

import json
from datetime import datetime
from pathlib import Path

import pytest

pytestmark = [pytest.mark.ci]

ROOT = Path(__file__).resolve().parent.parent.parent
PROOF_DIR = ROOT / "docs" / "reports" / "verification"
SCHEMA_PATH = ROOT / ".ci" / "proof_schema.json"


def _get_proof_files() -> list[Path]:
    """Discover all PROOF_*.json files."""
    return sorted(PROOF_DIR.glob("PROOF_*.json"))


def _load_schema() -> dict:
    """Load proof schema."""
    with open(SCHEMA_PATH, encoding="utf-8") as f:
        return json.load(f)


def _get_common_required(schema: dict) -> list[str]:
    """Return common_required keys from schema."""
    return sorted(schema.get("common_required", []))


@pytest.fixture(scope="module")
def schema() -> dict:
    """Load schema once for all tests."""
    return _load_schema()


@pytest.fixture(scope="module")
def proof_files() -> list[Path]:
    """Discover proof files once."""
    return _get_proof_files()


@pytest.mark.parametrize("proof_path", _get_proof_files(), ids=lambda p: p.name)
def test_proof_file_valid(proof_path: Path, schema: dict) -> None:
    """Each PROOF_*.json must have required keys, exit_code 0, fingerprint, valid timestamp."""
    assert proof_path.exists(), f"Proof file not found: {proof_path}"
    with open(proof_path, encoding="utf-8") as f:
        data = json.load(f)

    common_required = _get_common_required(schema)
    for key in common_required:
        assert key in data, f"{proof_path.name}: missing required key '{key}'"

    assert data.get("exit_code") == 0, (
        f"{proof_path.name}: exit_code must be 0, got {data.get('exit_code')}"
    )

    fp = data.get("evidence_fingerprint")
    assert fp is not None and isinstance(fp, str) and len(fp) > 0, (
        f"{proof_path.name}: evidence_fingerprint must be non-empty string"
    )

    ts = data.get("timestamp")
    assert ts is not None and isinstance(ts, str), (
        f"{proof_path.name}: timestamp required"
    )
    try:
        datetime.fromisoformat(ts.replace("Z", "+00:00"))
    except ValueError as e:
        pytest.fail(f"{proof_path.name}: timestamp must parse as ISO8601: {e}")
