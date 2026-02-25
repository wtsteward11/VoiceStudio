"""Model preflight validation and download orchestration.

Validates that required engine models are present on disk
before first use. Each ``ensure_*`` function returns a dict
with at least ``{"ok": bool}`` and additional keys like
``engine``, ``paths``, ``message`` depending on outcome.

``run_preflight`` aggregates all engine checks into a
single report.
"""

from __future__ import annotations

import logging
import os
from pathlib import Path
from typing import Any

from backend.config.path_config import get_models_path

logger = logging.getLogger(__name__)

_DEFERRED_NOTE = "will download on first use"


class PreflightError(Exception):
    """Raised when a preflight check fails."""


def get_engine_config_service() -> Any:
    """Lazy import to avoid circular deps at module load."""
    try:
        from backend.api import deps

        return deps.get_engine_config_service_dep()
    except Exception:
        return None


def run_preflight(
    *, auto_download: bool = False
) -> dict[str, Any]:
    """Run preflight checks for all engine families.

    Returns a dict with ``results`` keyed by engine name.
    """
    results: dict[str, Any] = {}

    results["xtts_v2"] = ensure_xtts(
        auto_download=auto_download
    )
    results["piper"] = ensure_piper(
        auto_download=auto_download
    )
    results["whisper_cpp"] = ensure_whisper_cpp(
        auto_download=auto_download
    )
    results["gpt_sovits"] = ensure_sovits(
        auto_download=auto_download
    )

    all_ok = all(
        r.get("ok", False) for r in results.values()
    )
    return {"ok": all_ok, "results": results}


def ensure_xtts(
    *, auto_download: bool = False
) -> dict[str, Any]:
    """Check XTTS v2 model availability."""
    models_root = get_models_path()
    xtts_dir = models_root / "xtts"

    tts_home = os.environ.get("TTS_HOME", str(xtts_dir))
    tts_path = Path(tts_home)

    if tts_path.exists() and any(tts_path.rglob("*.pth")):
        return {
            "ok": True,
            "engine": "xtts",
            "paths": [str(tts_path)],
        }

    if auto_download:
        logger.info(
            "XTTS models not found; download on first use"
        )
        tts_path.mkdir(parents=True, exist_ok=True)
        return {
            "ok": True,
            "engine": "xtts",
            "paths": [str(tts_path)],
            "note": _DEFERRED_NOTE,
        }

    return {
        "ok": False,
        "engine": "xtts",
        "message": "XTTS model directory empty or missing",
        "paths": [str(tts_path)],
    }


def ensure_piper(
    *, auto_download: bool = False
) -> dict[str, Any]:
    """Check Piper TTS model availability."""
    models_root = get_models_path()
    piper_dir = models_root / "piper"

    if piper_dir.exists() and any(piper_dir.rglob("*.onnx")):
        return {
            "ok": True,
            "engine": "piper",
            "paths": [str(piper_dir)],
        }

    if auto_download:
        piper_dir.mkdir(parents=True, exist_ok=True)
        return {
            "ok": True,
            "engine": "piper",
            "paths": [str(piper_dir)],
            "note": _DEFERRED_NOTE,
        }

    return {
        "ok": False,
        "engine": "piper",
        "message": "Piper model directory empty or missing",
        "paths": [str(piper_dir)],
    }


def ensure_whisper_cpp(
    *, auto_download: bool = False
) -> dict[str, Any]:
    """Check Whisper.cpp / faster-whisper availability."""
    models_root = get_models_path()
    whisper_dir = models_root / "whisper"

    model_env = os.environ.get("WHISPER_CPP_MODEL_PATH", "")
    if model_env and Path(model_env).exists():
        return {
            "ok": True,
            "engine": "whisper_cpp",
            "paths": [model_env],
        }

    has_bin = (
        whisper_dir.exists()
        and any(whisper_dir.rglob("*.bin"))
    )
    has_gguf = (
        whisper_dir.exists()
        and any(whisper_dir.rglob("*.gguf"))
    )
    if has_bin or has_gguf:
        found = (
            list(whisper_dir.rglob("*.bin"))
            + list(whisper_dir.rglob("*.gguf"))
        )
        return {
            "ok": True,
            "engine": "whisper_cpp",
            "paths": [str(f) for f in found[:3]],
        }

    if auto_download:
        whisper_dir.mkdir(parents=True, exist_ok=True)
        return {
            "ok": True,
            "engine": "whisper_cpp",
            "paths": [str(whisper_dir)],
            "note": _DEFERRED_NOTE,
        }

    return {
        "ok": False,
        "engine": "whisper_cpp",
        "message": "Whisper model not found",
        "paths": [str(whisper_dir)],
    }


def ensure_sovits(
    *, auto_download: bool = False,  # noqa: ARG001
) -> dict[str, Any]:
    """Check GPT-SoVITS / So-VITS-SVC availability."""
    config_svc = get_engine_config_service()
    if config_svc is None:
        return {
            "ok": False,
            "engine": "gpt_sovits",
            "message": "Engine config service unavailable",
        }

    try:
        cfg = config_svc.get_engine_config("gpt_sovits")
    except Exception:
        cfg = None

    if cfg is None:
        return {
            "ok": False,
            "engine": "gpt_sovits",
            "message": "No config for gpt_sovits engine",
        }

    params = cfg.get("parameters", {})
    model_path = params.get("model_path", "")
    config_path = params.get("config_path", "")

    missing: list[str] = []
    if not model_path or not Path(model_path).exists():
        missing.append(
            f"checkpoint: {model_path or '(not set)'}"
        )
    if not config_path or not Path(config_path).exists():
        missing.append(
            f"config: {config_path or '(not set)'}"
        )

    if missing:
        detail = (
            "So-VITS missing files: "
            + ", ".join(missing)
        )
        raise PreflightError(detail)

    paths = [p for p in (model_path, config_path) if p]
    return {"ok": True, "engine": "gpt_sovits", "paths": paths}
