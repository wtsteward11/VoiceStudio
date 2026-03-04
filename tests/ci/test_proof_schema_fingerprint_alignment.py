"""CI gate: proof schema evidence_fields must match proof_fingerprint.py EVIDENCE_FIELDS.

Fails with a diff if they diverge for any checked proof type.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent.parent


CHECKED_PROOF_TYPES = [
    "PROOF_GATE_C",
    "PROOF_GOLDEN_PATH_STUB",
    "PROOF_GOLDEN_PATH_REAL",
]


@pytest.fixture(scope="module")
def schema_evidence_fields() -> dict[str, list[str]]:
    schema_path = ROOT / ".ci" / "proof_schema.json"
    assert schema_path.exists(), f"Schema not found: {schema_path}"
    schema = json.loads(schema_path.read_text(encoding="utf-8"))
    return schema.get("evidence_fields", {})


@pytest.fixture(scope="module")
def fingerprint_evidence_fields() -> dict[str, list[str]]:
    if str(ROOT) not in sys.path:
        sys.path.insert(0, str(ROOT))
    from scripts.ci.proof_fingerprint import EVIDENCE_FIELDS
    return EVIDENCE_FIELDS


@pytest.mark.parametrize("proof_type", CHECKED_PROOF_TYPES)
def test_schema_fingerprint_alignment(
    proof_type: str,
    schema_evidence_fields: dict[str, list[str]],
    fingerprint_evidence_fields: dict[str, list[str]],
):
    schema_keys = set(schema_evidence_fields.get(proof_type, []))
    fp_keys = set(fingerprint_evidence_fields.get(proof_type, []))

    assert schema_keys, (
        f"{proof_type}: not found in .ci/proof_schema.json evidence_fields"
    )
    assert fp_keys, (
        f"{proof_type}: not found in proof_fingerprint.py EVIDENCE_FIELDS"
    )

    missing_in_fp = schema_keys - fp_keys
    extra_in_fp = fp_keys - schema_keys
    assert schema_keys == fp_keys, (
        f"{proof_type} schema/fingerprint drift: "
        f"missing_in_fingerprint={missing_in_fp}, "
        f"extra_in_fingerprint={extra_in_fp}"
    )
