#!/usr/bin/env python3
"""
Add evidence_fingerprint to a proof JSON file (M11).

Used by proof generators (e.g. copy_gatec_proof.ps1, copy_installer_proof.ps1)
that cannot easily import proof_fingerprint. Loads JSON, computes fingerprint,
sets evidence_fingerprint, writes back.

Usage:
  python scripts/ci/add_proof_fingerprint.py docs/reports/verification/PROOF_GATE_C_2026-03-02.json
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


def add_fingerprint(path: Path) -> bool:
    """Add evidence_fingerprint to proof. Return True on success."""
    if not path.exists() or path.suffix.lower() != ".json":
        return False
    proof_type = get_proof_type(path.name)
    if not proof_type:
        print(f"Unknown proof type for {path.name}", file=sys.stderr)
        return False
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except json.JSONDecodeError as e:
        print(f"Invalid JSON: {e}", file=sys.stderr)
        return False

    # PROOF_PAYLOAD_DETOX may lack moved_payloads; add empty list for fingerprint
    if proof_type == "PROOF_PAYLOAD_DETOX" and "moved_payloads" not in data:
        data["moved_payloads"] = []

    fp = compute_fingerprint(data, proof_type)
    data["evidence_fingerprint"] = fp
    path.write_text(json.dumps(data, indent=2), encoding="utf-8")
    return True


def main() -> int:
    if len(sys.argv) < 2:
        print("Usage: python add_proof_fingerprint.py <proof_path>", file=sys.stderr)
        return 1
    proof_path = Path(sys.argv[1])
    if not proof_path.is_absolute():
        proof_path = (ROOT / proof_path).resolve()
    else:
        proof_path = proof_path.resolve()
    if not add_fingerprint(proof_path):
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
