#!/usr/bin/env python3
"""
Backfill evidence_fingerprint into existing PROOF_*.json files (M11).

Reads each proof, computes fingerprint from evidence fields, writes it back.
No heavy pipeline runs. Pure JSON read/write.

Usage:
  python scripts/ci/backfill_proof_fingerprints.py
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from scripts.ci.proof_fingerprint import compute_fingerprint


def get_proof_type(basename: str) -> str | None:
    """Return schema key for proof type or None if unknown."""
    if not basename.startswith("PROOF_") or not basename.endswith(".json"):
        return None
    stem = basename[: -len(".json")]
    for prefix in ("PROOF_PAYLOAD_DETOX", "PROOF_PROVENANCE", "PROOF_GATE_C", "PROOF_INSTALLER"):
        if stem == prefix or stem.startswith(prefix + "_"):
            return prefix
    return None


def backfill_proof(path: Path) -> bool:
    """Add evidence_fingerprint to proof. Return True if updated."""
    if not path.exists() or path.suffix.lower() != ".json":
        return False
    proof_type = get_proof_type(path.name)
    if not proof_type:
        return False
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except json.JSONDecodeError:
        return False

    # PROOF_PAYLOAD_DETOX may lack moved_payloads; add empty list for fingerprint
    if proof_type == "PROOF_PAYLOAD_DETOX" and "moved_payloads" not in data:
        data["moved_payloads"] = []

    fp = compute_fingerprint(data, proof_type)
    if data.get("evidence_fingerprint") == fp:
        return False  # Already correct
    data["evidence_fingerprint"] = fp
    path.write_text(json.dumps(data, indent=2), encoding="utf-8")
    return True


def main() -> int:
    proof_dir = ROOT / "docs" / "reports" / "verification"
    paths = list(proof_dir.glob("PROOF_*.json"))
    if not paths:
        print("No PROOF_*.json files found.", file=sys.stderr)
        return 1
    updated = 0
    for p in paths:
        if backfill_proof(p):
            print(f"Backfilled: {p.relative_to(ROOT)}")
            updated += 1
    if updated == 0:
        print("All proofs already have correct evidence_fingerprint.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
