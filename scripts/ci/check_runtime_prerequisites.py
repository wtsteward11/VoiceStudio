#!/usr/bin/env python3
"""
GAP-015 slice 2: prerequisite probe for verify.ps1 -RuntimeProof.

Exits:
  0 — prerequisites OK for real golden-loop + training export CI tests
  2 — BLOCKED (engine/consent plumbing missing; do not run pytest)
  1 — unexpected error during probe

Writes JSON diagnostics to stdout (single line or pretty-printed object).
"""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path

# Reduce noisy imports when probing engine router (best-effort)
os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "3")
os.environ.setdefault("GRPC_VERBOSITY", "ERROR")


def _piper_manifest_present(project_root: Path) -> bool:
    """True if a Piper engine manifest exists under engines/ (fast, no ML imports)."""
    try:
        for manifest in project_root.glob("engines/**/engine.manifest.json"):
            try:
                data = json.loads(manifest.read_text(encoding="utf-8"))
            except OSError:
                continue
            if data.get("engine_id") == "piper":
                return True
    except OSError:
        return False
    return False


def _probe() -> dict:
    project_root = Path(__file__).resolve().parent.parent.parent
    root_str = str(project_root)
    if root_str not in sys.path:
        sys.path.insert(0, root_str)
    # Match real-mode tests: VOICESTUDIO_TEST_MODE=real for engine router behavior
    os.environ.setdefault("VOICESTUDIO_TEST_MODE", "real")

    result: dict = {
        "python_version": f"{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}",
        "pytest_available": False,
        "environment_mode": os.environ.get("VOICESTUDIO_TEST_MODE", "").strip(),
        "consent_routes_importable": False,
        "engine_piper_available": False,
        "engine_probe_error": None,
        "blocked": False,
        "blocked_reason": None,
    }

    try:
        import pytest

        result["pytest_available"] = True
        result["pytest_version"] = getattr(pytest, "__version__", "unknown")
    except ImportError:
        result["blocked"] = True
        result["blocked_reason"] = "pytest is not importable (install test deps)"
        return result

    try:
        import importlib

        importlib.import_module("backend.api.routes.consent")
        result["consent_routes_importable"] = True
    except Exception as exc:
        result["blocked"] = True
        result["blocked_reason"] = f"consent routes not importable: {exc}"
        return result

    result["piper_manifest_present"] = _piper_manifest_present(project_root)
    if not result["piper_manifest_present"]:
        result["blocked"] = True
        result["blocked_reason"] = (
            "no Piper engine manifest under engines/ (expected engines/**/piper/engine.manifest.json)"
        )
        return result

    try:
        from backend.services.engine_shared import _ensure_engine_router, engine_router

        _ensure_engine_router()
        if engine_router is None:
            result["engine_probe_error"] = "engine_router is None after _ensure_engine_router"
        else:
            engines = engine_router.list_engines()
            ids = [e.lower() if isinstance(e, str) else str(e).lower() for e in engines]
            result["engine_piper_available"] = any("piper" in x for x in ids)
            if not result["engine_piper_available"]:
                result["engine_probe_error"] = (
                    f"piper not in engine list: {engines!r}"
                )
    except Exception as exc:
        result["engine_probe_error"] = str(exc)[:500]
        result["engine_piper_available"] = False

    if not result["engine_piper_available"]:
        # Do not BLOCK: let real-mode pytest report FAIL with engine error text (honest distinction vs setup).
        result["engine_probe_warning"] = (
            result.get("engine_probe_error")
            or "piper not listed by engine router (synthesis may fail until models/venv are fixed)"
        )

    return result


def main() -> int:
    try:
        data = _probe()
    except Exception as exc:
        data = {
            "blocked": True,
            "blocked_reason": f"probe_failed: {exc}",
            "python_version": f"{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}",
        }
    print(json.dumps(data, indent=2))
    if data.get("blocked"):
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
