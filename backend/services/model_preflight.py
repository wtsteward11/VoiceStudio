"""
Model pre-flight checks and optional auto-downloads for core engines.

Ensures required checkpoints exist (or are pulled) under the configured model
root (`VOICESTUDIO_MODELS_PATH`, default: E:\\VoiceStudio\\models).

Engines covered:
- XTTS (Coqui TTS XTTS-v2 model)
- Piper (rhasspy/piper-voices, voice-specific .onnx + .json)
- Whisper.cpp (GGUF model)
- So-VITS-SVC (manual checkpoints/config)

All functions return a dict with:
    {
        "ok": bool,
        "paths": [list of touched/validated paths],
        "downloaded": bool,
        "message": str
    }

Raise HTTPException with actionable guidance when a required asset is missing
and auto-download is disabled or fails.
"""

from __future__ import annotations

import logging
import os
from importlib import metadata
from pathlib import Path

from backend.config.path_config import get_models_path

try:
    from huggingface_hub import hf_hub_download, snapshot_download

    HAS_HF = True
except ImportError:
    HAS_HF = False
    hf_hub_download = snapshot_download = None

from backend.services.EngineConfigService import get_engine_config_service

logger = logging.getLogger(__name__)


class PreflightError(Exception):
    """
    Service-layer exception for preflight check failures.

    Routes should catch this and convert to HTTPException.
    This keeps the service layer independent of FastAPI.
    """

    def __init__(self, detail: object, status_code: int = 503):
        self.detail = detail
        self.status_code = status_code
        super().__init__(str(detail))


def _ensure_dir(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)


def _fail(detail: object, status_code: int = 503) -> PreflightError:
    """Create a PreflightError (service-layer exception)."""
    return PreflightError(detail=detail, status_code=status_code)


def _get_pkg_version(package_name: str) -> str | None:
    try:
        return metadata.version(package_name)
    except metadata.PackageNotFoundError:
        return None
    except Exception:
        return None


def _xtts_dependency_status() -> dict[str, object]:
    versions = {
        "coqui-tts": _get_pkg_version("coqui-tts"),
        "torch": _get_pkg_version("torch"),
        "torchaudio": _get_pkg_version("torchaudio"),
    }
    ok = all(versions.values())
    unavailable = [name for name, version in versions.items() if not version]
    message = (
        "XTTS dependencies ready"
        if ok
        else f"XTTS dependencies not available: {', '.join(unavailable)}"
    )
    return {"ok": ok, "versions": versions, "message": message}


def ensure_xtts(auto_download: bool = True) -> dict[str, object]:
    """
    Ensure XTTS model assets exist.

    Notes:
    - Coqui TTS expects model identifiers like: `tts_models/<language>/<dataset>/<model_name>`
      (e.g. `tts_models/multilingual/multi-dataset/xtts_v2`).
    - Some older configs used the HuggingFace-style repo id `coqui/XTTS-v2`. That value is
      accepted by VoiceStudio as an alias, but it is not a Coqui-TTS model id.
    """
    cfg = get_engine_config_service()
    engine_cfg = cfg.get_engine_config("xtts_v2")
    model_name_raw = engine_cfg.get("parameters", {}).get("model_name")
    model_name = (model_name_raw or "tts_models/multilingual/multi-dataset/xtts_v2").strip()
    base_dir = Path(
        engine_cfg.get("model_paths", {}).get("base")
        or os.path.join(str(get_models_path()), "xtts")
    )
    cache_dir = Path(engine_cfg.get("model_paths", {}).get("cache") or base_dir / "cache")
    _ensure_dir(base_dir)
    _ensure_dir(cache_dir)

    downloaded = False
    paths: list[str] = []

    deps_status = _xtts_dependency_status()
    if not deps_status["ok"]:
        raise _fail(
            {"message": deps_status["message"], "dependencies": deps_status},
            status_code=503,
        )

    # Heuristic: XTTS expects model files inside base_dir; if empty, download.
    has_files = any(path.is_file() for path in base_dir.rglob("*"))
    if not has_files:
        is_coqui_model_id = model_name.startswith("tts_models/")
        is_hf_repo_id = ("/" in model_name) and (not is_coqui_model_id)

        if is_hf_repo_id:
            if not HAS_HF:
                raise _fail(
                    "XTTS model missing and huggingface_hub not installed. Install: pip install huggingface_hub",
                    status_code=503,
                )
            if not auto_download:
                raise _fail(
                    f"XTTS model missing at {base_dir}. Enable auto-download or place the model manually.",
                    status_code=424,
                )
            logger.info(f"XTTS preflight: downloading {model_name} into {base_dir}")
            snapshot_download(
                repo_id=model_name,
                local_dir=str(base_dir),
                local_dir_use_symlinks=False,
            )
            downloaded = True
        else:
            # Coqui model IDs are downloaded/managed by the Coqui TTS library on first use.
            # We still create the directories so downstream components have a stable place
            # for local assets/caches, but we do not attempt a HuggingFace snapshot download.
            logger.info(
                f"XTTS preflight: using Coqui model id '{model_name}' (download managed by Coqui TTS on first use)"
            )

    for f in base_dir.glob("**/*"):
        if f.is_file():
            paths.append(str(f))

    assets_present = has_files
    message = (
        f"XTTS assets ready at {base_dir}"
        if assets_present
        else (
            "XTTS assets are not on disk; Coqui download occurs on first use. "
            f"Base dir: {base_dir}"
        )
    )

    return {
        "ok": True,
        "paths": paths,
        "downloaded": downloaded,
        "message": message,
        "assets_present": assets_present,
        "deferred_download": not assets_present and not downloaded,
        "base_dir": str(base_dir),
        "cache_dir": str(cache_dir),
        "dependencies": deps_status,
    }


def ensure_piper(auto_download: bool = True) -> dict[str, object]:
    """
    Ensure Piper voice model (.onnx + .json) exists.
    """
    cfg = get_engine_config_service()
    engine_cfg = cfg.get_engine_config("piper")
    base_dir = Path(
        engine_cfg.get("model_paths", {}).get("base")
        or os.path.join(str(get_models_path()), "piper")
    )
    voice = engine_cfg.get("parameters", {}).get("voice", "en_US-amy-medium")
    model_path = Path(
        engine_cfg.get("parameters", {}).get("model_path") or base_dir / f"{voice}.onnx"
    )
    config_path = model_path.with_suffix(model_path.suffix + ".json")
    _ensure_dir(base_dir)

    def _dl(file_rel: str) -> str:
        if not HAS_HF:
            raise _fail(
                "huggingface_hub required for Piper auto-download. Install: pip install huggingface_hub",
                status_code=503,
            )
        return hf_hub_download(
            repo_id="rhasspy/piper-voices",
            filename=file_rel,
            local_dir=str(base_dir),
            local_dir_use_symlinks=False,
        )

    downloaded = False
    if not model_path.exists():
        if not auto_download:
            raise _fail(
                f"Piper model missing at {model_path}. Place the voice or enable auto-download.",
                status_code=424,
            )
        logger.info(f"Piper preflight: downloading voice {voice} into {base_dir}")
        rel = f"en/en_US/amy/medium/{voice}.onnx"
        _dl(rel)
        downloaded = True

    if not config_path.exists():
        if not auto_download:
            raise _fail(
                f"Piper config missing at {config_path}. Place the .json or enable auto-download.",
                status_code=424,
            )
        rel_json = f"en/en_US/amy/medium/{voice}.onnx.json"
        _dl(rel_json)
        downloaded = True

    return {
        "ok": True,
        "paths": [str(model_path), str(config_path)],
        "downloaded": downloaded,
        "message": f"Piper voice ready: {voice}",
    }


def ensure_whisper_cpp(auto_download: bool = True) -> dict[str, object]:
    """
    Ensure whisper.cpp GGUF model exists.
    """
    cfg = get_engine_config_service()
    engine_cfg = cfg.get_engine_config("whisper_cpp")
    model_path = Path(
        engine_cfg.get("parameters", {}).get("model_path")
        or os.path.join(
            str(get_models_path()),
            "whisper",
            "whisper-medium.en.gguf",
        )
    )
    _ensure_dir(model_path.parent)

    downloaded = False
    if not model_path.exists():
        if not auto_download:
            raise _fail(
                f"Whisper.cpp model missing at {model_path}. Place the GGUF or enable auto-download.",
                status_code=424,
            )
        if not HAS_HF:
            raise _fail(
                "huggingface_hub required for Whisper.cpp auto-download. Install: pip install huggingface_hub",
                status_code=503,
            )
        logger.info(f"Whisper.cpp preflight: downloading GGUF to {model_path}")
        hf_hub_download(
            repo_id="TheBloke/whisper-medium.en-GGUF",
            filename="whisper-medium.en.gguf",
            local_dir=str(model_path.parent),
            local_dir_use_symlinks=False,
        )
        downloaded = True

    return {
        "ok": True,
        "paths": [str(model_path)],
        "downloaded": downloaded,
        "message": "Whisper.cpp model ready",
    }


def ensure_sovits(auto_download: bool = False) -> dict[str, object]:
    """
    Validate So-VITS-SVC checkpoint + config (no auto-download; manual).
    """
    cfg = get_engine_config_service()
    engine_cfg = cfg.get_engine_config("sovits_svc") or cfg.get_engine_config("gpt_sovits")
    params = engine_cfg.get("parameters", {})
    model_path = Path(
        params.get("checkpoint_path")
        or params.get("model_path")
        or os.path.join(
            str(get_models_path()),
            "checkpoints",
            "MyVoiceProj",
            "model.pth",
        )
    )
    config_path = Path(params.get("config_path") or model_path.parent / "config.json")
    infer_command = params.get("infer_command") or os.getenv("SOVITS_SVC_INFER_COMMAND")
    infer_workdir = params.get("infer_workdir") or os.getenv("SOVITS_SVC_WORKDIR")
    allow_passthrough = bool(params.get("allow_passthrough", False))

    missing: list[str] = []
    if not model_path.exists():
        missing.append(str(model_path))
    if not config_path.exists():
        missing.append(str(config_path))

    if missing:
        raise _fail(
            "So-VITS checkpoints/config missing. Place files here: " + "; ".join(missing),
            status_code=424,
        )

    return {
        "ok": True,
        "paths": [str(model_path), str(config_path)],
        "downloaded": False,
        "message": "So-VITS checkpoints present",
        "inference_command_configured": bool(infer_command),
        "inference_workdir": infer_workdir,
        "allow_passthrough": allow_passthrough,
    }


def ensure_faster_whisper(auto_download: bool = True) -> dict[str, object]:
    """
    Validate faster-whisper (CTranslate2) availability.

    Unlike whisper.cpp, faster-whisper auto-downloads models from HuggingFace
    when given a size name (e.g. "base", "medium"). This check verifies the
    library is importable and the download cache directory is writable.
    """
    try:
        from faster_whisper import WhisperModel
    except ImportError:
        raise _fail(
            "faster-whisper not installed. Install: pip install faster-whisper",
            status_code=503,
        )

    models_root = os.environ.get("VOICESTUDIO_MODELS_PATH", "")
    if not models_root:
        models_root = os.path.join(
            os.environ.get("PROGRAMDATA", "C:\\ProgramData"),
            "VoiceStudio",
            "models",
        )
    whisper_cache = os.path.join(models_root, "whisper")
    os.makedirs(whisper_cache, exist_ok=True)

    has_cached = any(Path(whisper_cache).rglob("*.bin"))

    return {
        "ok": True,
        "paths": [whisper_cache],
        "downloaded": False,
        "message": (
            f"faster-whisper importable; cache at {whisper_cache}"
            + (" (models cached)" if has_cached else " (models download on first use)")
        ),
        "cache_dir": whisper_cache,
        "models_cached": has_cached,
    }


def run_preflight(auto_download: bool = True) -> dict[str, object]:
    """
    Run all pre-flight checks. Returns a summary dict.
    """
    results = {}
    checks = {
        "xtts_v2": ensure_xtts,
        "piper": ensure_piper,
        "whisper_cpp": ensure_whisper_cpp,
        "faster_whisper": ensure_faster_whisper,
        "gpt_sovits": ensure_sovits,
    }

    for name, fn in checks.items():
        try:
            results[name] = fn(auto_download=auto_download)
        except PreflightError as exc:  # Handle service-layer preflight errors
            detail = exc.detail
            message = detail.get("message") if isinstance(detail, dict) else None
            results[name] = {
                "ok": False,
                "downloaded": False,
                "message": message or str(detail),
                "status_code": exc.status_code,
            }
            if isinstance(detail, dict):
                for key, value in detail.items():
                    if key != "message":
                        results[name][key] = value
        except Exception as e:
            results[name] = {
                "ok": False,
                "downloaded": False,
                "message": f"{type(e).__name__}: {e}",
                "status_code": 500,
            }

    return {"results": results}
