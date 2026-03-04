"""
Single source of truth for provenance and usage recording behavior.

This module is the only place that declares whether provenance/usage failures
are fatal (strict) or best-effort (log and continue). All callers of
record_artifact_provenance_and_usage respect this policy.

Configure via env: VOICESTUDIO_PROVENANCE_POLICY=strict|best_effort
"""

from __future__ import annotations

import os
from enum import Enum


class ProvenancePolicy(str, Enum):
    """Policy for provenance and usage recording failures."""

    STRICT = "strict"
    """Provenance/usage failure = raise, do not return artifact."""

    BEST_EFFORT = "best_effort"
    """Log warning on failure, continue (current default)."""


def _resolve_policy() -> ProvenancePolicy:
    # VOICESTUDIO_PROVENANCE_STRICT=1 overrides to strict
    if os.getenv("VOICESTUDIO_PROVENANCE_STRICT", "").strip() in ("1", "true", "yes"):
        return ProvenancePolicy.STRICT
    raw = os.getenv("VOICESTUDIO_PROVENANCE_POLICY", "best_effort").lower().strip()
    try:
        return ProvenancePolicy(raw)
    except ValueError:
        return ProvenancePolicy.BEST_EFFORT


POLICY: ProvenancePolicy = _resolve_policy()
