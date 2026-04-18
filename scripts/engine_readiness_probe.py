#!/usr/bin/env python3
"""
Engine readiness probe (Slice 10).

**Default (fast):** scans ``engines/**/engine.manifest.json`` for declared ``engine_id`` and paths.
Does **not** call ``load_all_engines`` (that import pulls heavy optional stacks and can take minutes).

**Full router probe:** set ``VOICESTUDIO_ENGINE_PROBE_FULL=1`` — then calls ``engine_router.load_all_engines("engines")``
and records ``list_engines()`` + ``get_engine`` attempts (slow; operator-only).

Writes JSON to ``docs/reports/verification/slice10/engine_readiness_probe.json``.

Usage (from repo root, .venv activated):
  python scripts/engine_readiness_probe.py
  $env:VOICESTUDIO_ENGINE_PROBE_FULL='1'; python scripts/engine_readiness_probe.py
"""

from __future__ import annotations

import json
import os
import sys
import traceback
from datetime import datetime, timezone
from pathlib import Path


def _repo_root() -> Path:
    return Path(__file__).resolve().parents[1]


def _scan_manifests(engines_root: Path) -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    for path in sorted(engines_root.rglob("engine.manifest.json")):
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except OSError as e:
            rows.append(
                {
                    "manifest_path": str(path),
                    "error": f"{type(e).__name__}: {e}",
                }
            )
            continue
        rel = str(path.relative_to(_repo_root()))
        rows.append(
            {
                "engine_id": data.get("engine_id"),
                "type": data.get("type"),
                "subtype": data.get("subtype"),
                "manifest_path": rel,
                "entry_point": data.get("entry_point"),
            }
        )
    return rows


def _router_probe_full(root: Path) -> dict[str, object]:
    os.chdir(root)
    if str(root) not in sys.path:
        sys.path.insert(0, str(root))

    from app.core.engines.router import router as engine_router

    load_err: str | None = None
    try:
        engine_router.load_all_engines("engines")
    except Exception as e:
        load_err = f"{type(e).__name__}: {e}"

    registered = engine_router.list_engines()
    per_engine: dict[str, object] = {}

    for eid in sorted(registered):
        entry: dict[str, object] = {"registered": True}
        try:
            inst = engine_router.get_engine(eid)
            entry["instantiable"] = inst is not None
            entry["instance_type"] = type(inst).__name__ if inst is not None else None
        except Exception as ex:
            entry["instantiable"] = False
            entry["get_engine_error"] = f"{type(ex).__name__}: {ex}"
            entry["get_engine_traceback"] = traceback.format_exc()

        try:
            if eid in ("xtts", "xtts_v2"):
                from backend.ml.models.model_preflight import ensure_xtts

                entry["preflight_assets"] = ensure_xtts(auto_download=False)
            elif eid == "piper":
                from backend.ml.models.model_preflight import ensure_piper

                entry["preflight_assets"] = ensure_piper(auto_download=False)
            else:
                entry["preflight_assets"] = {
                    "ok": None,
                    "reason": "no ensure_* in probe (runtime-only)",
                }
        except Exception as ex:
            entry["preflight_assets"] = f"{type(ex).__name__}: {ex}"

        per_engine[eid] = entry

    return {
        "load_all_engines_error": load_err,
        "engine_router_list_engines": registered,
        "engines": per_engine,
    }


def main() -> int:
    root = _repo_root()
    engines_root = root / "engines"
    manifests = _scan_manifests(engines_root)

    out: dict[str, object] = {
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "repo_root": str(root),
        "mode": "manifest_scan",
        "manifests": manifests,
    }

    if os.environ.get("VOICESTUDIO_ENGINE_PROBE_FULL", "").strip() == "1":
        out["mode"] = "manifest_scan_plus_full_router"
        out["router"] = _router_probe_full(root)
    else:
        out["note"] = (
            "Router not loaded (set VOICESTUDIO_ENGINE_PROBE_FULL=1 for load_all_engines + list_engines; slow)."
        )

    out_path = root / "docs" / "reports" / "verification" / "slice10" / "engine_readiness_probe.json"
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(out, indent=2), encoding="utf-8")
    print(json.dumps({"wrote": str(out_path), "manifest_count": len(manifests)}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
