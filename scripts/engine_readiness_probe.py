#!/usr/bin/env python3
"""
Engine readiness probe (Slice 10).

**Default (fast):** scans ``engines/**/engine.manifest.json`` for declared ``engine_id`` and paths.
Does **not** call ``load_all_engines`` (that import pulls heavy optional stacks and can take minutes).

**Full router probe:** set ``VOICESTUDIO_ENGINE_PROBE_FULL=1`` — then calls ``engine_router.load_all_engines("engines")``
and records ``list_engines()`` + ``get_engine`` attempts (slow; operator-only).

**Chatterbox preflight refresh (fast):** set ``VOICESTUDIO_ENGINE_PROBE_CHATTERBOX_REFRESH=1`` — loads the existing
``slice17/engine_readiness_probe.json`` (if present), re-runs only ``ensure_chatterbox(auto_download=False)`` into
``router.engines.chatterbox``, rescans manifests, updates ``timestamp_utc``. Does **not** call ``load_all_engines`` (minutes).

Writes JSON to ``docs/reports/verification/slice12/engine_readiness_probe.json``
(mirrors to ``slice10/``, ``slice13/``, ``slice14/``, ``slice15/``, ``slice17/``, and ``slice18/`` for legacy refs and slice artifacts).

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
            elif eid == "espeak_ng":
                from backend.ml.models.model_preflight import ensure_espeak_ng

                entry["preflight_assets"] = ensure_espeak_ng(auto_download=False)
            elif eid == "rhvoice":
                from backend.ml.models.model_preflight import PreflightError as _PfeRh
                from backend.ml.models.model_preflight import ensure_rhvoice

                try:
                    entry["preflight_assets"] = ensure_rhvoice(auto_download=False)
                except _PfeRh as ex:
                    detail = ex.detail
                    if isinstance(detail, dict):
                        entry["preflight_assets"] = {
                            "ok": detail.get("ok", False),
                            "message": detail.get("message", str(detail)),
                            "status_code": ex.status_code,
                        }
                    else:
                        entry["preflight_assets"] = {
                            "ok": False,
                            "message": str(detail),
                            "status_code": ex.status_code,
                        }
            elif eid == "silero":
                from backend.ml.models.model_preflight import PreflightError as _PfeSi
                from backend.ml.models.model_preflight import ensure_silero

                try:
                    entry["preflight_assets"] = ensure_silero(auto_download=False)
                except _PfeSi as ex:
                    detail = ex.detail
                    if isinstance(detail, dict):
                        entry["preflight_assets"] = {
                            "ok": detail.get("ok", False),
                            "message": detail.get("message", str(detail)),
                            "status_code": ex.status_code,
                        }
                    else:
                        entry["preflight_assets"] = {
                            "ok": False,
                            "message": str(detail),
                            "status_code": ex.status_code,
                        }
            elif eid == "chatterbox":
                from backend.ml.models.model_preflight import PreflightError as _PfeCb
                from backend.ml.models.model_preflight import ensure_chatterbox

                try:
                    entry["preflight_assets"] = ensure_chatterbox(auto_download=False)
                except _PfeCb as ex:
                    detail = ex.detail
                    if isinstance(detail, dict):
                        entry["preflight_assets"] = {
                            "ok": detail.get("ok", False),
                            "message": detail.get("message", str(detail)),
                            "status_code": ex.status_code,
                        }
                    else:
                        entry["preflight_assets"] = {
                            "ok": False,
                            "message": str(detail),
                            "status_code": ex.status_code,
                        }
            elif eid == "tortoise":
                from backend.ml.models.model_preflight import PreflightError as _PfeTo
                from backend.ml.models.model_preflight import ensure_tortoise

                try:
                    entry["preflight_assets"] = ensure_tortoise(auto_download=False)
                except _PfeTo as ex:
                    detail = ex.detail
                    if isinstance(detail, dict):
                        entry["preflight_assets"] = {
                            "ok": detail.get("ok", False),
                            "message": detail.get("message", str(detail)),
                            "status_code": ex.status_code,
                        }
                    else:
                        entry["preflight_assets"] = {
                            "ok": False,
                            "message": str(detail),
                            "status_code": ex.status_code,
                        }
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


def _merge_chatterbox_preflight_only(
    root: Path,
    previous: dict[str, object],
) -> dict[str, object]:
    """Refresh only ``router.engines.chatterbox.preflight_assets`` without ``load_all_engines``."""
    os.chdir(root)
    if str(root) not in sys.path:
        sys.path.insert(0, str(root))

    from backend.ml.models.model_preflight import PreflightError as _PfeCb
    from backend.ml.models.model_preflight import ensure_chatterbox

    out = dict(previous)
    router = dict(out.get("router") or {})
    engines = dict(router.get("engines") or {})

    cb_entry = dict(engines.get("chatterbox") or {"registered": True, "instantiable": False})
    try:
        cb_entry["preflight_assets"] = ensure_chatterbox(auto_download=False)
    except _PfeCb as ex:
        detail = ex.detail
        if isinstance(detail, dict):
            cb_entry["preflight_assets"] = {
                "ok": detail.get("ok", False),
                "message": detail.get("message", str(detail)),
                "status_code": ex.status_code,
            }
        else:
            cb_entry["preflight_assets"] = {
                "ok": False,
                "message": str(detail),
                "status_code": ex.status_code,
            }
    engines["chatterbox"] = cb_entry
    router["engines"] = engines
    out["router"] = router
    out["timestamp_utc"] = datetime.now(timezone.utc).isoformat()
    out["repo_root"] = str(root)
    out["mode"] = "manifest_scan_plus_router_chatterbox_preflight_only"
    out["manifests"] = _scan_manifests(root / "engines")
    out["note"] = (
        "Chatterbox preflight refreshed via ensure_chatterbox without load_all_engines "
        "(full router: VOICESTUDIO_ENGINE_PROBE_FULL=1; slow)."
    )
    return out


def main() -> int:
    root = _repo_root()
    engines_root = root / "engines"
    manifests = _scan_manifests(engines_root)

    slice17 = root / "docs" / "reports" / "verification" / "slice17" / "engine_readiness_probe.json"
    if os.environ.get("VOICESTUDIO_ENGINE_PROBE_CHATTERBOX_REFRESH", "").strip() == "1":
        if not slice17.is_file():
            print(
                json.dumps(
                    {"error": "missing_previous", "path": str(slice17)},
                    indent=2,
                ),
                file=sys.stderr,
            )
            return 2
        try:
            previous = json.loads(slice17.read_text(encoding="utf-8"))
        except OSError as e:
            print(json.dumps({"error": f"read_failed: {e}"}, indent=2), file=sys.stderr)
            return 2
        out = _merge_chatterbox_preflight_only(root, previous)
        primary = root / "docs" / "reports" / "verification" / "slice12" / "engine_readiness_probe.json"
        legacy = root / "docs" / "reports" / "verification" / "slice10" / "engine_readiness_probe.json"
        slice13 = root / "docs" / "reports" / "verification" / "slice13" / "engine_readiness_probe.json"
        slice14 = root / "docs" / "reports" / "verification" / "slice14" / "engine_readiness_probe.json"
        slice15 = root / "docs" / "reports" / "verification" / "slice15" / "engine_readiness_probe.json"
        slice18 = root / "docs" / "reports" / "verification" / "slice18" / "engine_readiness_probe.json"
        payload = json.dumps(out, indent=2)
        primary.parent.mkdir(parents=True, exist_ok=True)
        legacy.parent.mkdir(parents=True, exist_ok=True)
        slice13.parent.mkdir(parents=True, exist_ok=True)
        slice14.parent.mkdir(parents=True, exist_ok=True)
        slice15.parent.mkdir(parents=True, exist_ok=True)
        slice17.parent.mkdir(parents=True, exist_ok=True)
        slice18.parent.mkdir(parents=True, exist_ok=True)
        primary.write_text(payload, encoding="utf-8")
        legacy.write_text(payload, encoding="utf-8")
        slice13.write_text(payload, encoding="utf-8")
        slice14.write_text(payload, encoding="utf-8")
        slice15.write_text(payload, encoding="utf-8")
        slice17.write_text(payload, encoding="utf-8")
        slice18.write_text(payload, encoding="utf-8")
        print(
            json.dumps(
                {
                    "wrote": str(primary),
                    "mode": out.get("mode"),
                    "mirrored": [
                        str(legacy),
                        str(slice13),
                        str(slice14),
                        str(slice15),
                        str(slice17),
                        str(slice18),
                    ],
                },
                indent=2,
            )
        )
        return 0

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

    primary = root / "docs" / "reports" / "verification" / "slice12" / "engine_readiness_probe.json"
    legacy = root / "docs" / "reports" / "verification" / "slice10" / "engine_readiness_probe.json"
    slice13 = root / "docs" / "reports" / "verification" / "slice13" / "engine_readiness_probe.json"
    slice14 = root / "docs" / "reports" / "verification" / "slice14" / "engine_readiness_probe.json"
    slice15 = root / "docs" / "reports" / "verification" / "slice15" / "engine_readiness_probe.json"
    slice17 = root / "docs" / "reports" / "verification" / "slice17" / "engine_readiness_probe.json"
    slice18 = root / "docs" / "reports" / "verification" / "slice18" / "engine_readiness_probe.json"
    payload = json.dumps(out, indent=2)
    primary.parent.mkdir(parents=True, exist_ok=True)
    legacy.parent.mkdir(parents=True, exist_ok=True)
    slice13.parent.mkdir(parents=True, exist_ok=True)
    slice14.parent.mkdir(parents=True, exist_ok=True)
    slice15.parent.mkdir(parents=True, exist_ok=True)
    slice17.parent.mkdir(parents=True, exist_ok=True)
    slice18.parent.mkdir(parents=True, exist_ok=True)
    primary.write_text(payload, encoding="utf-8")
    legacy.write_text(payload, encoding="utf-8")
    slice13.write_text(payload, encoding="utf-8")
    slice14.write_text(payload, encoding="utf-8")
    slice15.write_text(payload, encoding="utf-8")
    slice17.write_text(payload, encoding="utf-8")
    slice18.write_text(payload, encoding="utf-8")
    print(
        json.dumps(
            {
                "wrote": str(primary),
                "mirrored": [
                    str(legacy),
                    str(slice13),
                    str(slice14),
                    str(slice15),
                    str(slice17),
                    str(slice18),
                ],
                "manifest_count": len(manifests),
            },
            indent=2,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
