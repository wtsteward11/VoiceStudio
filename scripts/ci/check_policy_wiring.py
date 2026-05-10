#!/usr/bin/env python3
"""
CI check: verify trust & safety hooks are wired in the voice route stack.

Voice routes are modular under ``backend/api/routes/voice/``; policy enforcement
lives on the shared router. Provenance and usage recording are centralized in
``artifact_provenance``.

Exits 1 if any required symbol is missing from the specified file.
Run: python scripts/ci/check_policy_wiring.py
"""
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent

CHECKS = [
    (
        "backend/api/routes/voice/_shared.py",
        "enforce_voice_policy",
        "Policy enforcer must be in voice router deps (shared router)",
    ),
    (
        "backend/services/artifact_provenance.py",
        "write_provenance_sidecar",
        "Provenance must be invoked from artifact provenance helper after synthesis",
    ),
    (
        "backend/services/artifact_provenance.py",
        "record_synthesis_minutes",
        "Usage stats must be invoked from artifact provenance helper after synthesis",
    ),
    (
        "backend/api/security/voice_policy.py",
        "check_synthesis_rate_limit",
        "Synthesis rate limiting must be in the choke point",
    ),
    (
        "backend/api/security/voice_policy.py",
        "check_clone_rate_limit",
        "Clone rate limiting must be in the choke point",
    ),
    (
        "backend/api/security/voice_policy.py",
        "_SYNTHESIS_PREFIX",
        "Prefix-based rate limiting must cover all synth routes",
    ),
]

failed = False
for rel, symbol, desc in CHECKS:
    path = ROOT / rel
    if not path.exists():
        print(f"FAIL [file missing]: {rel}")
        print(f"     -> {desc}")
        failed = True
        continue
    if symbol not in path.read_text(encoding="utf-8"):
        print(f"FAIL [symbol absent]: {symbol!r} not in {rel}")
        print(f"     -> {desc}")
        failed = True
    else:
        print(f"OK:   {symbol!r} in {rel}")

sys.exit(1 if failed else 0)
