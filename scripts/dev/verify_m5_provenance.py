#!/usr/bin/env python3
"""
Manual verification script for Milestone 5: provenance + usage postconditions.

Creates an artifact via the spine and confirms:
- <file>.provenance.json exists alongside the artifact
- Usage stats are recorded when duration is known

Run from repo root: python scripts/dev/verify_m5_provenance.py
"""

from __future__ import annotations

import sys
from pathlib import Path

# Minimal valid WAV for duration
MINIMAL_WAV = (
    b"RIFF\x24\x00\x00\x00WAVEfmt \x10\x00\x00\x00\x01\x00\x01\x00"
    b"\x44\xac\x00\x00\x88X\x01\x00\x02\x00\x10\x00data\x00\x00\x00\x00"
)


def main() -> int:
    # Ensure we can import backend (run from repo root)
    repo_root = Path(__file__).resolve().parents[2]
    if str(repo_root) not in sys.path:
        sys.path.insert(0, str(repo_root))

    from backend.services.audio_artifacts.store import get_audio_artifact_store
    from backend.services.usage_stats import get_usage_stats

    store = get_audio_artifact_store()
    before_minutes = get_usage_stats().get("synthesis_minutes", 0.0)

    aid, cached_path, meta = store.store_from_bytes(
        MINIMAL_WAV,
        model_used="verify_m5",
        write_provenance=True,
    )

    path = Path(cached_path)
    sidecar = path.with_suffix(path.suffix + ".provenance.json")

    if not sidecar.exists():
        print(f"FAIL: Provenance sidecar not found at {sidecar}")
        return 1

    content = sidecar.read_text()
    if "model_used" not in content or "verify_m5" not in content:
        print(f"FAIL: Provenance sidecar missing expected fields: {content[:200]}")
        return 1

    after_minutes = get_usage_stats().get("synthesis_minutes", 0.0)
    if after_minutes <= before_minutes:
        print(
            f"WARN: Usage minutes unchanged (before={before_minutes}, after={after_minutes}). "
            "Duration may not have been computed."
        )
    else:
        print(f"OK: Usage minutes incremented ({before_minutes} -> {after_minutes})")

    print(f"OK: Artifact {aid} at {cached_path}")
    print(f"OK: Provenance sidecar at {sidecar}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
