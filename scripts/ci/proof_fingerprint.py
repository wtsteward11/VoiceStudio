#!/usr/bin/env python3
"""
Compute evidence fingerprint for proof JSON files (M11 tamper-evidence).

Fingerprint = sha256 of canonical evidence fields. Large strings (>250KB) are
hashed before inclusion to keep computation cheap.

Usage:
  from scripts.ci.proof_fingerprint import compute_fingerprint
  fp = compute_fingerprint(proof_dict, "PROOF_GATE_C")
"""
from __future__ import annotations

import hashlib
import json
from typing import Any

# Evidence fields per proof type (must match .ci/proof_schema.json evidence_fields)
EVIDENCE_FIELDS: dict[str, list[str]] = {
    "PROOF_PROVENANCE": ["stdout", "stderr", "command", "exit_code"],
    "PROOF_GATE_C": ["command", "exit_code", "gatec_log", "ui_smoke"],
    "PROOF_INSTALLER": ["command", "exit_code", "all_passed", "results"],
    "PROOF_PAYLOAD_DETOX": [
        "command",
        "exit_code",
        "moved_payloads",
        "check_repo_payloads",
        "policy_file_summary",
    ],
    "PROOF_PHASE_3": ["phase", "date", "checks", "command", "exit_code"],
    "PROOF_PHASE_2_1": ["phase", "date", "checks", "command", "exit_code"],
    "PROOF_PHASE": ["phase", "date", "checks"],
    "PROOF_GOLDEN_PATH": [
        "command",
        "exit_code",
        "engine_mode",
        "model_hashes",
        "output_file_hash",
        "output_duration_seconds",
        "output_energy_rms",
        "all_steps_passed",
        "git_commit",
    ],
    "PROOF_UI_COMMAND_SURFACE": [
        "command",
        "exit_code",
        "commands_checked",
        "panels_checked",
        "all_commands_registered",
        "all_panels_registered",
    ],
}

LARGE_STRING_THRESHOLD = 256000  # 250KB


def _canonical_value(val: Any, path: str = "") -> Any:
    """
    Produce canonical representation for fingerprinting.
    Large strings are replaced with sha256 hex to avoid memory blowup.
    """
    if val is None:
        return None
    if isinstance(val, str):
        if len(val) > LARGE_STRING_THRESHOLD:
            return f"<sha256:{hashlib.sha256(val.encode('utf-8')).hexdigest()}>"
        return val
    if isinstance(val, dict):
        return {k: _canonical_value(v, f"{path}.{k}") for k, v in sorted(val.items())}
    if isinstance(val, list):
        return [_canonical_value(v, f"{path}[{i}]") for i, v in enumerate(val)]
    return val


def compute_fingerprint(proof_dict: dict[str, Any], proof_type: str) -> str:
    """
    Compute sha256 hex fingerprint of evidence fields for the given proof type.

    Args:
        proof_dict: The proof JSON as a dict (evidence_fingerprint excluded from input)
        proof_type: One of PROOF_PROVENANCE, PROOF_GATE_C, PROOF_INSTALLER, PROOF_PAYLOAD_DETOX

    Returns:
        64-char hex string (sha256)
    """
    fields = EVIDENCE_FIELDS.get(proof_type, [])
    if not fields:
        return hashlib.sha256(b"unknown_proof_type").hexdigest()

    defaults: dict[str, Any] = {
        "moved_payloads": [],
        "stderr": "",
        "stdout": "",
    }
    evidence: dict[str, Any] = {}
    for key in sorted(fields):
        val = proof_dict.get(key, defaults.get(key))
        evidence[key] = _canonical_value(val, key)

    canonical = json.dumps(evidence, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()
