#!/usr/bin/env python3
"""
Generate machine-readable engine inventory (Slice 30) and joined v2 truth (Task 33).

  python scripts/generate_engine_truth.py           # v1 only -> generated/engine_truth.json
  python scripts/generate_engine_truth.py --schema v2
  python scripts/generate_engine_truth.py --schema all   # v1 + v2
"""

from __future__ import annotations

import argparse
import json
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


def _repo_root() -> Path:
    return Path(__file__).resolve().parents[1]


def _load_overrides(root: Path) -> dict[str, Any]:
    path = root / "tools" / "overseer" / "data" / "engine_truth_overrides.json"
    if not path.is_file():
        return {"defaults": {}, "engines": {}}
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {"defaults": {}, "engines": {}}
    if not isinstance(data, dict):
        return {"defaults": {}, "engines": {}}
    defaults = data.get("defaults") or {}
    engines = data.get("engines") or {}
    if not isinstance(engines, dict):
        engines = {}
    return {"defaults": defaults if isinstance(defaults, dict) else {}, "engines": engines}


# Manifest ``subtype`` (lowercase) -> allowed curated ``engine_kind`` when set.
# Unknown subtypes: unconstrained (``True``) until explicitly mapped.
_SUBTYPE_ALLOWED_ENGINE_KINDS: dict[str, frozenset[str]] = {
    "stt": frozenset({"stt"}),
    "tts": frozenset({"tts"}),
    "voice_conversion": frozenset({"sts"}),
    # So-VITS / voice-clone manifests use subtype ``vc``; curated ``engine_kind`` is ``vc``.
    "vc": frozenset({"vc"}),
    "chat": frozenset({"chat", "llm"}),
    "s2s": frozenset({"s2s"}),
    "embedding": frozenset({"embedding"}),
    "alignment": frozenset({"alignment"}),
    "generation": frozenset({"generation", "image", "video"}),
    "avatar": frozenset({"avatar", "video"}),
    "face_swap": frozenset({"face_swap", "video"}),
    "utility": frozenset({"utility", "video"}),
    "upscaling": frozenset({"upscaling", "image"}),
    "editing": frozenset({"editing", "video"}),
}


def _manifest_consistency_ok(
    subtype: object,
    engine_kind: str | None,
) -> bool:
    if engine_kind is None:
        return True
    sub = str(subtype or "").strip().lower()
    allowed = _SUBTYPE_ALLOWED_ENGINE_KINDS.get(sub)
    if allowed is None:
        return True
    return engine_kind in allowed


def _build_v1_rows(root: Path) -> list[dict[str, object]]:
    engines_dir = root / "engines"
    rows: list[dict[str, object]] = []
    for path in sorted(engines_dir.rglob("engine.manifest.json")):
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except OSError as e:
            rows.append(
                {
                    "manifest_path": str(path.relative_to(root)),
                    "error": f"{type(e).__name__}: {e}",
                }
            )
            continue
        rows.append(
            {
                "engine_id": data.get("engine_id"),
                "name": data.get("name"),
                "type": data.get("type"),
                "subtype": data.get("subtype"),
                "support_tier": data.get("support_tier"),
                "implementation_status": data.get("implementation_status"),
                "manifest_path": str(path.relative_to(root)),
                "entry_point": data.get("entry_point"),
            }
        )
    return rows


def _write_v1(root: Path, rows: list[dict[str, object]]) -> Path:
    out = {
        "schema": "voicestudio.engine_truth.v1",
        "generated_utc": datetime.now(timezone.utc).isoformat(),
        "engines": rows,
    }
    dest = root / "docs" / "reports" / "verification" / "generated" / "engine_truth.json"
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(json.dumps(out, indent=2), encoding="utf-8")
    return dest


def _write_v2(root: Path, v1_rows: list[dict[str, object]]) -> Path:
    override_bundle = _load_overrides(root)
    defaults = override_bundle.get("defaults") or {}
    overrides = override_bundle.get("engines") or {}
    latest_verify = defaults.get("latest_verify_artifact")
    if not isinstance(latest_verify, str):
        latest_verify = None

    merged: list[dict[str, object]] = []
    for row in v1_rows:
        if "error" in row:
            merged.append(dict(row))
            continue
        eid = row.get("engine_id")
        eid_s = str(eid) if eid is not None else ""
        ov = overrides.get(eid_s) if isinstance(overrides, dict) else None
        ov = ov if isinstance(ov, dict) else {}
        subtype = row.get("subtype")
        engine_kind = ov.get("engine_kind")
        engine_kind_s = str(engine_kind) if engine_kind is not None else None

        mco = _manifest_consistency_ok(subtype, engine_kind_s)

        v2_row: dict[str, object] = {
            **row,
            "readiness_status": ov.get("readiness_status", "unknown"),
            "runtime_proof_status": ov.get("runtime_proof_status", "unknown"),
            "first_blocker": ov.get("first_blocker"),
            "latest_proof_doc": ov.get("latest_proof_doc"),
            "latest_verify_artifact": latest_verify,
            "authority_module": ov.get("authority_module"),
            "manifest_consistency_ok": mco,
            "matrix_status": ov.get("matrix_status"),
            "notes": ov.get("notes"),
            "engine_kind": engine_kind_s,
        }
        merged.append(v2_row)

    out = {
        "schema": "voicestudio.engine_truth.v2",
        "generated_utc": datetime.now(timezone.utc).isoformat(),
        "derivation": "manifest_scan_plus_tools/overseer/data/engine_truth_overrides.json",
        "engines": merged,
    }
    dest = root / "docs" / "reports" / "verification" / "generated" / "engine_truth_v2.json"
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(json.dumps(out, indent=2), encoding="utf-8")
    return dest


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate engine_truth JSON from manifests.")
    parser.add_argument(
        "--schema",
        choices=("v1", "v2", "all"),
        default="v1",
        help="v1=inventory only; v2=joined operational fields; all=both",
    )
    args = parser.parse_args()
    root = _repo_root()
    rows = _build_v1_rows(root)

    if args.schema in ("v1", "all"):
        dest = _write_v1(root, rows)
        print(f"Wrote {dest} ({len(rows)} rows) schema=v1")

    if args.schema in ("v2", "all"):
        dest2 = _write_v2(root, rows)
        print(f"Wrote {dest2} ({len(rows)} rows) schema=v2")

    return 0


if __name__ == "__main__":
    sys.exit(main())
