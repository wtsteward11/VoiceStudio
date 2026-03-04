#!/usr/bin/env python3
"""Add support_tier to engine manifests that don't have it.
Tier 1 (tier1_supported) is already set in: xtts_v2, piper, whisper, whisper_cpp, silero.
Sets: tier2_best_effort for full/basic, experimental for placeholder/external.
"""
from pathlib import Path
import json

TIER1_IDS = {"xtts_v2", "piper", "whisper", "whisper_cpp", "silero"}
ENGINES_ROOT = Path(__file__).resolve().parent.parent / "engines"


def main():
    count = 0
    for manifest_path in ENGINES_ROOT.rglob("engine.manifest.json"):
        with open(manifest_path, encoding="utf-8") as f:
            data = json.load(f)
        if "support_tier" in data:
            continue
        engine_id = data.get("engine_id", "")
        status = data.get("implementation_status", "placeholder")
        if engine_id in TIER1_IDS:
            tier = "tier1_supported"
        elif status in ("full", "basic"):
            tier = "tier2_best_effort"
        else:
            tier = "experimental"
        data["support_tier"] = tier
        with open(manifest_path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2, ensure_ascii=False)
        count += 1
        print(f"  {manifest_path.relative_to(ENGINES_ROOT.parent)} -> {tier}")
    print(f"Added support_tier to {count} manifests.")


if __name__ == "__main__":
    main()
