"""
Model pre-flight checks and optional auto-downloads for core engines.

Ensures required checkpoints exist (or are pulled) under the configured model
root (`VOICESTUDIO_MODELS_PATH`, default: E:\\VoiceStudio\\models).

Engines covered:
- XTTS (Coqui TTS XTTS-v2 model)
- Piper (rhasspy/piper-voices, voice-specific .onnx + .json)
- Whisper.cpp (GGUF model)
- Vosk STT (package + on-disk model tree)
- Parakeet TTS (Paddle stack + checkpoints layout)
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

import json
import logging
import os
import shutil
import subprocess
import tempfile
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


def _voice_studio_repo_root() -> Path:
    """Repository root (parent of ``backend``)."""
    return Path(__file__).resolve().parents[2]


def _resolve_whisper_cpp_executable_path(raw: str | None) -> Path:
    """Resolve ``executable_path`` from engine config (absolute or repo-relative)."""
    default = _voice_studio_repo_root() / "tools" / "whispercpp" / "whisper-cli.exe"
    if not raw:
        return default
    p = Path(raw)
    if p.is_absolute():
        return p
    return _voice_studio_repo_root() / p


def _whisper_cpp_python_binding_available() -> bool:
    try:
        import whisper_cpp
    except ImportError:
        return False
    return True


def _probe_whisper_cpp_cli(exe: Path) -> tuple[bool, str]:
    """Non-shell probe: whisper.cpp builds typically print usage for ``-h`` / ``--help``."""
    if not exe.is_file():
        return False, f"binary not found at {exe}"
    for args in ([str(exe), "-h"], [str(exe), "--help"]):
        try:
            proc = subprocess.run(
                args,
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )
            out = ((proc.stdout or "") + (proc.stderr or "")).strip()
            if proc.returncode == 0 or len(out) >= 12:
                return True, ""
        except subprocess.TimeoutExpired:
            return False, f"whisper.cpp CLI timed out ({args!r})"
        except OSError as exc:
            return False, f"whisper.cpp CLI failed to start: {exc}"
    return False, "whisper.cpp CLI did not respond to -h or --help"


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
                resume_download=True,
            )
            downloaded = True
            try:
                from backend.services.usage_stats import record_model_downloaded
                record_model_downloaded()
            except Exception as e:
                logger.debug("Usage stats record_model_downloaded skip: %s", e)
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
        out = hf_hub_download(
            repo_id="rhasspy/piper-voices",
            filename=file_rel,
            local_dir=str(base_dir),
            local_dir_use_symlinks=False,
            resume_download=True,
        )
        try:
            from backend.services.usage_stats import record_model_downloaded
            record_model_downloaded()
        except Exception as e:
            logger.debug("Usage stats record_model_downloaded skip: %s", e)
        return out

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



def ensure_espeak_ng(auto_download: bool = True) -> dict[str, object]:
    """
    Ensure eSpeak NG CLI is available (manifest default: ``espeak-ng`` on PATH).
    """
    del auto_download
    cfg = get_engine_config_service()
    engine_cfg = cfg.get_engine_config("espeak_ng") or {}
    params = engine_cfg.get("parameters", {}) if isinstance(engine_cfg, dict) else {}
    configured = params.get("executable_path") if isinstance(params, dict) else None
    candidates: list[str | None] = [
        configured,
        shutil.which("espeak-ng"),
        shutil.which("espeak"),
    ]
    exe = next((c for c in candidates if c and Path(c).exists()), None)
    if not exe:
        raise _fail(
            {
                "message": (
                    "eSpeak NG executable not found. Install eSpeak NG and ensure "
                    "`espeak-ng` is on PATH, or set engine parameters.executable_path."
                ),
                "ok": False,
            },
            status_code=503,
        )
    return {
        "ok": True,
        "paths": [exe],
        "downloaded": False,
        "message": f"eSpeak NG ready: {exe}",
    }


def ensure_rhvoice(auto_download: bool = True) -> dict[str, object]:
    """
    Ensure RHVoice CLI is available.

    Manifest default name is ``rhvoice-client``; the engine implementation prefers
    ``rhvoice-say`` / ``rhvoice-cli`` (see ``RHVoiceEngine._find_executable``).
    """
    del auto_download
    cfg = get_engine_config_service()
    engine_cfg = cfg.get_engine_config("rhvoice") or {}
    params = engine_cfg.get("parameters", {}) if isinstance(engine_cfg, dict) else {}
    configured = params.get("executable_path") if isinstance(params, dict) else None
    candidates: list[str | None] = [
        configured,
        shutil.which("rhvoice-client"),
        shutil.which("rhvoice-say"),
        shutil.which("rhvoice-cli"),
        shutil.which("RHVoice-test"),
    ]
    exe = next((c for c in candidates if c and Path(c).exists()), None)
    if not exe:
        raise _fail(
            {
                "message": (
                    "RHVoice CLI not found. Stock Windows does not ship RHVoice; "
                    "install a supported RHVoice binary externally (see RHVoice project), "
                    "then set engine_configs.rhvoice.parameters.executable_path in "
                    "backend/config/engine_config.json to the full path of rhvoice-say, "
                    "rhvoice-cli, or rhvoice-client, or place one of those names on PATH."
                ),
                "ok": False,
            },
            status_code=503,
        )
    return {
        "ok": True,
        "paths": [exe],
        "downloaded": False,
        "message": f"RHVoice ready: {exe}",
    }


def ensure_silero(auto_download: bool = True) -> dict[str, object]:
    """
    Ensure Silero TTS can load via ``torch.hub`` (``snakers4/silero-models``).

    Hub ``silero_tts`` expects **speaker** IDs from the upstream model (e.g. ``v3_en``,
    ``v4_ru``, ``aidar_v2``) — not the legacy ``silero_tts_{model_id}`` pattern. Defaults:
    ``language=en``, ``speaker=v3_en`` unless overridden in engine config parameters.

    When ``auto_download=False`` (preflight / probe), a **cached** hub checkout must
    already exist under ``torch.hub.get_dir()`` — no silent network fetch.
    """
    try:
        import torch
    except ImportError:
        raise _fail(
            {
                "message": "PyTorch (torch) not installed. Silero requires torch>=1.9.",
                "ok": False,
            },
            status_code=503,
        )

    cfg = get_engine_config_service()
    engine_cfg = cfg.get_engine_config("silero") or {}
    params = engine_cfg.get("parameters", {}) if isinstance(engine_cfg, dict) else {}
    model_id = str(params.get("model_id") or "v4")
    language = str(params.get("language") or "en")
    # snakers4/silero-models master: speaker must be a hub-supported ID (see silero_tts())
    speaker = str(params.get("speaker") or "v3_en")

    hub_root = Path(torch.hub.get_dir())
    hub_cached = any(hub_root.glob("snakers4_silero-models*"))

    downloaded = False
    if not hub_cached and not auto_download:
        raise _fail(
            {
                "message": (
                    "Silero: torch.hub repo snakers4/silero-models is not cached under "
                    f"{hub_root}. Preflight uses auto_download=False (no automatic hub fetch). "
                    "Warm the cache once with network access (run ensure_silero(auto_download=True) "
                    "or a successful Silero synthesis), then re-run preflight."
                ),
                "ok": False,
                "hub_dir": str(hub_root),
            },
            status_code=503,
        )
    if not hub_cached and auto_download:
        downloaded = True

    try:
        model, _example_text = torch.hub.load(
            repo_or_dir="snakers4/silero-models",
            model="silero_tts",
            language=language,
            speaker=speaker,
            trust_repo=True,
        )
        del model
        if torch.cuda.is_available():
            try:
                torch.cuda.empty_cache()
            except Exception as ex:
                logger.debug(
                    "torch.cuda.empty_cache after Silero hub load: %s",
                    ex,
                )
    except Exception as e:
        raise _fail(
            {
                "message": f"Silero torch.hub.load failed: {type(e).__name__}: {e}",
                "ok": False,
            },
            status_code=503,
        )

    return {
        "ok": True,
        "paths": [str(hub_root)],
        "downloaded": downloaded,
        "message": f"Silero TTS ready (language={language}, speaker={speaker})",
        "model_id": model_id,
        "language": language,
    }


CHATTERBOX_REPO_ID = "ResembleAI/chatterbox"
CHATTERBOX_PROBE_FILE = "ve.safetensors"


def _require_venv_advanced_tts_python_exe(*, consumer: str = "Chatterbox") -> Path:
    """Resolve ``venv_advanced_tts`` ``python.exe`` (Chatterbox; OpenVoice uses ``venv_openvoice``)."""
    try:
        from app.core.runtime.venv_family_manager import VenvFamily, get_venv_manager
    except ImportError as e:
        raise _fail(
            {
                "message": (
                    f"{consumer} preflight: venv family manager unavailable "
                    f"({type(e).__name__}: {e})."
                ),
                "ok": False,
            },
            status_code=503,
        ) from e

    mgr = get_venv_manager()
    fam = VenvFamily.ADVANCED_TTS
    if not mgr.is_venv_created(fam):
        raise _fail(
            {
                "message": (
                    f"{consumer} requires the venv_advanced_tts virtual environment "
                    "(engines/audio/chatterbox/engine.manifest.json). Create it with "
                    "scripts/engines/create_engine_venv.py for the Advanced TTS family, "
                    "then install the engine stack into that venv."
                ),
                "ok": False,
                "reason": "venv_advanced_tts_not_created",
            },
            status_code=503,
        )
    return Path(mgr.get_python_executable(fam))


def _subprocess_chatterbox_import_ok(python_exe: Path, timeout: float = 90.0) -> str | None:
    """Return ``None`` if ``chatterbox.tts`` imports in ``python_exe``; else an error string."""
    cmd = [
        str(python_exe),
        "-c",
        "from chatterbox.tts import ChatterboxTTS; print('chatterbox_import_ok')",
    ]
    try:
        proc = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=timeout,
            check=False,
        )
    except subprocess.TimeoutExpired:
        return "chatterbox import probe: timeout"
    except OSError as e:
        return f"chatterbox import probe: {e}"
    if proc.returncode == 0:
        return None
    err = (proc.stderr or proc.stdout or "").strip() or f"exit_code={proc.returncode}"
    return err


_CHATTERBOX_HF_SUBPROCESS_TAIL = """
def main():
    try:
        from huggingface_hub import hf_hub_download
    except ImportError as e:
        print(json.dumps({"ok": False, "error": "import", "detail": repr(e)}))
        sys.exit(1)
    downloaded = False
    if not AUTO:
        try:
            local_path = hf_hub_download(repo_id=REPO, filename=FN, local_files_only=True)
        except Exception as e:
            print(json.dumps({"ok": False, "error": "hf", "detail": type(e).__name__ + ": " + str(e)}))
            sys.exit(1)
    else:
        had_local = False
        try:
            hf_hub_download(repo_id=REPO, filename=FN, local_files_only=True)
            had_local = True
        except Exception:
            had_local = False
        try:
            local_path = hf_hub_download(repo_id=REPO, filename=FN)
            downloaded = not had_local
        except Exception as e:
            print(json.dumps({"ok": False, "error": "hf", "detail": type(e).__name__ + ": " + str(e)}))
            sys.exit(1)
    print(json.dumps({"ok": True, "path": str(local_path), "downloaded": downloaded}))


main()
"""


def _chatterbox_hf_subprocess_script(auto_download: bool) -> str:
    """Run inside ``venv_advanced_tts`` so HF cache paths match that interpreter."""
    ad = "True" if auto_download else "False"
    return (
        "import json\nimport sys\nAUTO = "
        + ad
        + "\nREPO = "
        + json.dumps(CHATTERBOX_REPO_ID)
        + "\nFN = "
        + json.dumps(CHATTERBOX_PROBE_FILE)
        + "\n"
        + _CHATTERBOX_HF_SUBPROCESS_TAIL
    )


def _subprocess_chatterbox_hf_hub(
    python_exe: Path,
    auto_download: bool,
    *,
    timeout: float = 180.0,
) -> tuple[str, bool, str | None]:
    """Returns ``(local_path_str, downloaded, error_or_none)``."""
    script_body = _chatterbox_hf_subprocess_script(auto_download)
    fd, tpath = tempfile.mkstemp(suffix="_chatterbox_hf.py", text=True)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            f.write(script_body)
        run_env = os.environ.copy()
        run_env.setdefault("HF_ENDPOINT", "https://huggingface.co")
        proc = subprocess.run(
            [str(python_exe), tpath],
            capture_output=True,
            text=True,
            timeout=timeout,
            check=False,
            env=run_env,
        )
    finally:
        try:
            os.unlink(tpath)
        except OSError as ex:
            logger.debug("chatterbox HF temp script cleanup: %s", ex)

    if proc.returncode != 0:
        tail = (proc.stderr or proc.stdout or "").strip()
        return "", False, tail or f"exit_code={proc.returncode}"

    out = (proc.stdout or "").strip().splitlines()
    if not out:
        return "", False, "chatterbox HF probe: empty stdout"
    try:
        payload = json.loads(out[-1])
    except json.JSONDecodeError as e:
        return "", False, f"chatterbox HF probe: invalid json ({e}): {out[-1]!r}"

    if not payload.get("ok"):
        detail = payload.get("detail") or payload.get("error") or payload
        return "", False, str(detail)

    return str(payload["path"]), bool(payload.get("downloaded")), None


def _require_venv_tortoise_python_exe() -> Path:
    """Resolve ``venv_tortoise`` ``python.exe`` (authoritative Tortoise runtime; Slice 18B)."""
    try:
        from app.core.runtime.venv_family_manager import VenvFamily, get_venv_manager
    except ImportError as e:
        raise _fail(
            {
                "message": (
                    "Tortoise preflight: venv family manager unavailable "
                    f"({type(e).__name__}: {e})."
                ),
                "ok": False,
            },
            status_code=503,
        ) from e

    mgr = get_venv_manager()
    fam = VenvFamily.TORTOISE
    if not mgr.is_venv_created(fam):
        raise _fail(
            {
                "message": (
                    "Tortoise requires the venv_tortoise virtual environment "
                    "(engines/audio/tortoise/engine.manifest.json). Create it with "
                    "scripts/engines/create_engine_venv.py --family tortoise "
                    "then verify the interpreter can import tortoise.api.TextToSpeech."
                ),
                "ok": False,
                "reason": "venv_tortoise_not_created",
            },
            status_code=503,
        )
    return Path(mgr.get_python_executable(fam))


def _require_venv_openvoice_python_exe() -> Path:
    """Resolve ``venv_openvoice`` ``python.exe`` (authoritative OpenVoice runtime; Slice 19F / ADR-054)."""
    try:
        from app.core.runtime.venv_family_manager import VenvFamily, get_venv_manager
    except ImportError as e:
        raise _fail(
            {
                "message": (
                    "OpenVoice preflight: venv family manager unavailable "
                    f"({type(e).__name__}: {e})."
                ),
                "ok": False,
            },
            status_code=503,
        ) from e

    mgr = get_venv_manager()
    fam = VenvFamily.OPENVOICE
    if not mgr.is_venv_created(fam):
        raise _fail(
            {
                "message": (
                    "OpenVoice requires the venv_openvoice virtual environment "
                    "(engines/audio/openvoice/engine.manifest.json). Create it with "
                    "scripts/engines/create_engine_venv.py --family openvoice, install "
                    "config/venv_families/requirements-openvoice.txt into that venv, then "
                    "see docs/design/VOICESTUDIO_BOUNDED_SLICE19F_OPENVOICE_ISOLATED_VENV.md."
                ),
                "ok": False,
                "reason": "venv_openvoice_not_created",
            },
            status_code=503,
        )
    return Path(mgr.get_python_executable(fam))


def _subprocess_tortoise_import_ok(python_exe: Path, timeout: float = 120.0) -> str | None:
    """Return ``None`` if ``tortoise.api.TextToSpeech`` imports in ``python_exe``; else an error string."""
    cmd = [
        str(python_exe),
        "-c",
        "from tortoise.api import TextToSpeech; print('tortoise_import_ok')",
    ]
    try:
        proc = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=timeout,
            check=False,
        )
    except subprocess.TimeoutExpired:
        return "tortoise import probe: timeout"
    except OSError as e:
        return f"tortoise import probe: {e}"
    if proc.returncode == 0:
        return None
    err = (proc.stderr or proc.stdout or "").strip() or f"exit_code={proc.returncode}"
    return err


_TORTOISE_WARM_SUBPROCESS_BODY = """
import json
import sys
from pathlib import Path

MODELS = Path(sys.argv[1])
AUTO = sys.argv[2] == "1"


def _has_weights() -> bool:
    return MODELS.is_dir() and any(p.is_file() for p in MODELS.rglob("*"))


def main() -> None:
    if not _has_weights() and not AUTO:
        print(json.dumps({"ok": False, "error": "no_cache", "models_dir": str(MODELS)}))
        sys.exit(1)
    if not _has_weights() and AUTO:
        import torch
        from tortoise.api import TextToSpeech

        device = "cuda" if torch.cuda.is_available() else "cpu"
        tts = TextToSpeech(device=device, models_dir=str(MODELS), kv_cache=True)
        del tts
        if torch.cuda.is_available():
            try:
                torch.cuda.empty_cache()
            except Exception as e:
                print(
                    json.dumps({"warn": "cuda_empty_cache", "detail": repr(e)}),
                    file=sys.stderr,
                )
        print(json.dumps({"ok": True, "downloaded": True, "models_dir": str(MODELS)}))
        return
    print(json.dumps({"ok": True, "downloaded": False, "models_dir": str(MODELS)}))


main()
"""


def _subprocess_tortoise_models_ready(
    python_exe: Path,
    models_dir: Path,
    auto_download: bool,
    *,
    timeout: float = 600.0,
) -> tuple[bool, str | None]:
    """Run Tortoise weight/cache validation in the family venv. Returns (downloaded, error_or_none)."""
    fd, tpath = tempfile.mkstemp(suffix="_tortoise_warm.py", text=True)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as handle:
            handle.write(_TORTOISE_WARM_SUBPROCESS_BODY)
        proc = subprocess.run(
            [str(python_exe), tpath, str(models_dir), "1" if auto_download else "0"],
            capture_output=True,
            text=True,
            timeout=timeout,
            check=False,
        )
    finally:
        try:
            os.unlink(tpath)
        except OSError as ex:
            logger.debug("tortoise warm temp script cleanup: %s", ex)

    if proc.returncode != 0:
        tail = (proc.stderr or proc.stdout or "").strip()
        return False, tail or f"exit_code={proc.returncode}"

    out = (proc.stdout or "").strip().splitlines()
    if not out:
        return False, "tortoise models probe: empty stdout"
    try:
        payload = json.loads(out[-1])
    except json.JSONDecodeError as e:
        return False, f"tortoise models probe: invalid json ({e}): {out[-1]!r}"

    if not payload.get("ok"):
        detail = payload.get("detail") or payload.get("error") or payload
        return False, str(detail)

    return bool(payload.get("downloaded")), None


def ensure_chatterbox(auto_download: bool = True) -> dict[str, object]:
    """
    Ensure Chatterbox TTS can resolve weights from Hugging Face repo ``ResembleAI/chatterbox``.

    Preflight runs **in the ``venv_advanced_tts`` interpreter** (import + ``hf_hub_download``),
    not the FastAPI worker venv, so ``checks.chatterbox`` matches the manifest runtime.

    When ``auto_download=False``, a cached copy of the probe file must exist — no silent fetch.
    """
    python_exe = _require_venv_advanced_tts_python_exe()

    import_err = _subprocess_chatterbox_import_ok(python_exe)
    if import_err is not None:
        raise _fail(
            {
                "message": (
                    f"chatterbox-tts import failed in venv_advanced_tts ({python_exe}): {import_err}. "
                    "Install chatterbox-tts and dependencies into that venv."
                ),
                "ok": False,
                "python_exe": str(python_exe),
            },
            status_code=503,
        )

    local_path, downloaded, hf_err = _subprocess_chatterbox_hf_hub(
        python_exe,
        auto_download,
    )
    repo_id = CHATTERBOX_REPO_ID
    probe_file = CHATTERBOX_PROBE_FILE
    if hf_err is not None:
        if not auto_download:
            raise _fail(
                {
                    "message": (
                        f"Chatterbox: no usable Hugging Face cache for {repo_id} ({probe_file}) "
                        f"in venv_advanced_tts. Preflight uses auto_download=False (no automatic download). "
                        f"Warm the cache once with network access. Detail: {hf_err}"
                    ),
                    "ok": False,
                    "repo_id": repo_id,
                    "python_exe": str(python_exe),
                },
                status_code=503,
            )
        raise _fail(
            {
                "message": (
                    f"Chatterbox: hf_hub_download failed for {repo_id}/{probe_file} "
                    f"in venv_advanced_tts: {hf_err}"
                ),
                "ok": False,
                "repo_id": repo_id,
                "python_exe": str(python_exe),
            },
            status_code=503,
        )

    cache_dir = str(Path(local_path).parent)

    return {
        "ok": True,
        "paths": [cache_dir],
        "downloaded": downloaded,
        "message": f"Chatterbox TTS ready (repo={repo_id}, python={python_exe})",
        "repo_id": repo_id,
        "python_exe": str(python_exe),
    }


def _tortoise_models_dir() -> Path:
    """Match Tortoise cache layout (``tortoise_models`` under the tortoise model root)."""
    model_cache_dir = os.getenv("VOICESTUDIO_MODELS_PATH")
    if not model_cache_dir:
        model_cache_dir = os.path.join(
            os.getenv("PROGRAMDATA", "C:\\ProgramData"),
            "VoiceStudio",
            "models",
            "tortoise",
        )
    base = Path(model_cache_dir)
    _ensure_dir(base)
    out = base / "tortoise_models"
    _ensure_dir(out)
    return out


def _tortoise_has_cached_weights(models_dir: Path) -> bool:
    if not models_dir.is_dir():
        return False
    return any(p.is_file() for p in models_dir.rglob("*"))


def ensure_tortoise(auto_download: bool = True) -> dict[str, object]:
    """
    Ensure Tortoise TTS is usable from the **dedicated ``venv_tortoise`` interpreter** (Slice 18B).

    Preflight runs import and optional ``TextToSpeech`` warm/download **in that subprocess**,
    not in the FastAPI worker — ``tortoise-tts`` must never be installed into the backend ``.venv``.

    When ``auto_download=False`` (health preflight / probe), at least one cached weight file
    must exist under ``<tortoise cache>/tortoise_models`` — no silent network fetch.
    When ``auto_download=True``, an empty cache triggers ``TextToSpeech`` construction in the
    Tortoise venv so weights may download (operator warm-up path).
    """
    python_exe = _require_venv_tortoise_python_exe()

    import_err = _subprocess_tortoise_import_ok(python_exe)
    if import_err is not None:
        raise _fail(
            {
                "message": (
                    f"tortoise-tts import failed in venv_tortoise ({python_exe}): {import_err}. "
                    "Install tortoise-tts and its stack into runtime/venvs/tortoise (see ADR-052)."
                ),
                "ok": False,
                "python_exe": str(python_exe),
            },
            status_code=503,
        )

    tortoise_models_dir = _tortoise_models_dir()
    has_weights = _tortoise_has_cached_weights(tortoise_models_dir)

    if not has_weights and not auto_download:
        raise _fail(
            {
                "message": (
                    f"Tortoise: no cached weights under {tortoise_models_dir}. "
                    "Preflight uses auto_download=False (no automatic download). "
                    "Warm once with network access: ensure_tortoise(auto_download=True) "
                    "or a successful Tortoise synthesis in the isolated venv, then re-run preflight."
                ),
                "ok": False,
                "models_dir": str(tortoise_models_dir),
                "python_exe": str(python_exe),
            },
            status_code=424,
        )

    downloaded = False
    if not has_weights and auto_download:
        downloaded, warm_err = _subprocess_tortoise_models_ready(
            python_exe,
            tortoise_models_dir,
            True,
        )
        if warm_err is not None:
            raise _fail(
                {
                    "message": (
                        f"Tortoise TextToSpeech init/download failed in venv_tortoise: {warm_err}"
                    ),
                    "ok": False,
                    "models_dir": str(tortoise_models_dir),
                    "python_exe": str(python_exe),
                },
                status_code=503,
            )

    return {
        "ok": True,
        "paths": [str(tortoise_models_dir)],
        "downloaded": downloaded,
        "message": f"Tortoise TTS ready (models_dir={tortoise_models_dir}, python={python_exe})",
        "python_exe": str(python_exe),
    }


def _subprocess_openvoice_import_ok(python_exe: Path, timeout: float = 90.0) -> str | None:
    """Return ``None`` if OpenVoice API imports in ``python_exe``; else an error string."""
    cmd = [
        str(python_exe),
        "-c",
        "from openvoice.api import BaseSpeakerTTS, ToneColorConverter; print('openvoice_import_ok')",
    ]
    try:
        proc = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=timeout,
            check=False,
        )
    except subprocess.TimeoutExpired:
        return "openvoice import probe: timeout"
    except OSError as e:
        return f"openvoice import probe: {e}"
    if proc.returncode == 0:
        return None
    err = (proc.stderr or proc.stdout or "").strip() or f"exit_code={proc.returncode}"
    return err


def _openvoice_models_root() -> Path:
    """Resolve OpenVoice checkpoint root (``VOICESTUDIO_MODELS_PATH`` or ProgramData layout)."""
    model_cache_dir = os.getenv("VOICESTUDIO_MODELS_PATH")
    if not model_cache_dir:
        model_cache_dir = os.path.join(
            os.getenv("PROGRAMDATA", "C:\\ProgramData"),
            "VoiceStudio",
            "models",
        )
    return Path(model_cache_dir)


def _openvoice_has_checkpoints(asset_root: Path) -> bool:
    """True when a ``config.json`` has a sibling checkpoint file OpenVoice loaders expect."""
    if not asset_root.is_dir():
        return False
    for cfg in asset_root.rglob("config.json"):
        if not cfg.is_file():
            continue
        parent = cfg.parent
        for name in ("checkpoint.pth", "checkpoint.ckpt", "model.pth"):
            if (parent / name).is_file():
                return True
    return False


def ensure_openvoice(auto_download: bool = True) -> dict[str, object]:
    """
    Ensure OpenVoice is importable from ``venv_openvoice`` and local checkpoints exist.

    ``engines/audio/openvoice/engine.manifest.json`` declares ``venv_family: venv_openvoice``.
    Preflight probes imports **in that interpreter**, not the FastAPI worker.

    ``auto_download`` is accepted for API symmetry with other ``ensure_*`` functions but is
    **not used**: OpenVoice weights are operator-supplied under ``<models>/openvoice/`` — no
    silent download (bounded slice 19; ``no-fallbacks`` alignment).
    """
    _ = auto_download
    python_exe = _require_venv_openvoice_python_exe()

    import_err = _subprocess_openvoice_import_ok(python_exe)
    if import_err is not None:
        raise _fail(
            {
                "message": (
                    f"OpenVoice import failed in venv_openvoice ({python_exe}): {import_err}. "
                    "Install the OpenVoice stack into runtime/venvs/openvoice (see "
                    "docs/design/VOICESTUDIO_BOUNDED_SLICE19F_OPENVOICE_ISOLATED_VENV.md)."
                ),
                "ok": False,
                "python_exe": str(python_exe),
            },
            status_code=503,
        )

    root = _openvoice_models_root()
    base_speakers = root / "openvoice" / "base_speakers"
    converter = root / "openvoice" / "converter"

    missing_parts: list[str] = []
    if not _openvoice_has_checkpoints(base_speakers):
        missing_parts.append(
            f"base_speaker tree incomplete under {base_speakers} "
            "(need config.json + checkpoint.pth next to it, or nested layout discoverable by rglob)."
        )
    if not _openvoice_has_checkpoints(converter):
        missing_parts.append(
            f"tone-color converter tree incomplete under {converter} (same checkpoint rule)."
        )
    if missing_parts:
        raise _fail(
            {
                "message": " ".join(missing_parts),
                "ok": False,
                "paths": [str(base_speakers), str(converter)],
            },
            status_code=424,
        )

    return {
        "ok": True,
        "paths": [str(base_speakers), str(converter)],
        "downloaded": False,
        "message": f"OpenVoice ready (python={python_exe}, models under {root / 'openvoice'})",
        "python_exe": str(python_exe),
    }


def ensure_whisper_cpp(auto_download: bool = True) -> dict[str, object]:
    """
    Ensure whisper.cpp model weights exist and at least one execution surface is available.

    Readiness (Slice 22): on-disk weights (default ``ggml-medium.en.bin`` under the whisper
    models directory) **and** (Python ``whisper_cpp`` import **or** whisper.cpp CLI at resolved
    ``executable_path`` / manifest default under ``tools/whispercpp/``). Some third-party files
    named ``*.gguf`` are not loadable by upstream ``whisper-cli``; the default auto-download uses
    the MIT-licensed ``ggerganov/whisper.cpp`` ``ggml-medium.en.bin`` artifact.

    Health preflight uses ``auto_download=False`` (no silent HF pull each request).
    """
    cfg = get_engine_config_service()
    engine_cfg = cfg.get_engine_config("whisper_cpp") or {}
    params = engine_cfg.get("parameters", {}) if isinstance(engine_cfg, dict) else {}
    model_path = Path(
        params.get("model_path")
        or os.path.join(
            str(get_models_path()),
            "whisper",
            "ggml-medium.en.bin",
        )
    )
    _ensure_dir(model_path.parent)

    downloaded = False
    if not model_path.exists():
        if not auto_download:
            raise _fail(
                f"Whisper.cpp model missing at {model_path}. "
                "Place compatible whisper.cpp weights (see engine manifest) or enable auto-download.",
                status_code=424,
            )
        if not HAS_HF:
            raise _fail(
                "huggingface_hub required for Whisper.cpp auto-download. Install: pip install huggingface_hub",
                status_code=503,
            )
        logger.info("Whisper.cpp preflight: downloading ggml-medium.en.bin to %s", model_path)
        downloaded_path = hf_hub_download(
            repo_id="ggerganov/whisper.cpp",
            filename="ggml-medium.en.bin",
            local_dir=str(model_path.parent),
            local_dir_use_symlinks=False,
        )
        src = Path(downloaded_path)
        if src != model_path and src.exists():
            shutil.move(str(src), str(model_path))
        downloaded = True
        try:
            from backend.services.usage_stats import record_model_downloaded

            record_model_downloaded()
        except Exception as e:
            logger.debug("Usage stats record_model_downloaded skip: %s", e)

    exe_raw = params.get("executable_path") if isinstance(params, dict) else None
    exe_raw_str = exe_raw if isinstance(exe_raw, str) and exe_raw.strip() else None
    exe_path = _resolve_whisper_cpp_executable_path(exe_raw_str)

    binding_ok = _whisper_cpp_python_binding_available()
    binary_ok = False
    binary_detail = ""
    if exe_path.is_file():
        binary_ok, binary_detail = _probe_whisper_cpp_cli(exe_path)

    if not binding_ok and not binary_ok:
        msg_parts = [
            "Whisper.cpp has no execution surface: install whisper-cpp-python in this venv,",
            "or place a working whisper.cpp CLI (see engines/audio/whisper_cpp/engine.manifest.json",
            f"default tools/whispercpp/whisper-cli.exe). Model weights present at {model_path}.",
        ]
        if exe_path.is_file() or exe_raw_str:
            msg_parts.append(f"CLI probe: {binary_detail or 'failed'} (path: {exe_path}).")
        else:
            msg_parts.append(f"Default CLI path not found: {exe_path}.")
        if not binding_ok:
            msg_parts.append("`import whisper_cpp` failed.")
        raise _fail(
            {
                "message": " ".join(msg_parts),
                "ok": False,
                "model_path": str(model_path),
                "python_binding": binding_ok,
                "executable": str(exe_path),
            },
            status_code=503,
        )

    surfaces: list[str] = []
    if binding_ok:
        surfaces.append("whisper_cpp_python")
    if binary_ok:
        surfaces.append(f"cli:{exe_path}")

    paths: list[str] = [str(model_path)]
    if binary_ok:
        paths.append(str(exe_path))

    return {
        "ok": True,
        "paths": paths,
        "downloaded": downloaded,
        "message": f"Whisper.cpp ready ({', '.join(surfaces)})",
        "execution_surfaces": surfaces,
        "python_binding": binding_ok,
        "executable": str(exe_path) if binary_ok else None,
    }


def ensure_vosk(auto_download: bool = True) -> dict[str, object]:
    """
    Readiness for engine_id ``vosk`` (Vosk STT): ``vosk`` import + on-disk model directory.

    Model resolution: ``parameters.model_path`` / ``parameters.model_name`` from engine config,
    else ``VOICESTUDIO_VOSK_MODEL_PATH``, else ``<models>/vosk/<model_name>`` with default
    ``vosk-model-en-us-0.22``. No silent download: operator must lay down models
    (see https://alphacephei.com/vosk/models).
    """
    try:
        from vosk import Model
    except ImportError:
        raise _fail(
            {
                "message": "vosk package not installed. Install: pip install vosk>=0.3.45",
                "ok": False,
                "first_blocker": "import_vosk",
            },
            status_code=503,
        )

    cfg = get_engine_config_service()
    engine_cfg = cfg.get_engine_config("vosk") or {}
    params = (engine_cfg.get("parameters", {}) if isinstance(engine_cfg, dict) else {}) or {}
    default_name = str(
        params.get("model_name")
        or os.environ.get("VOICESTUDIO_VOSK_MODEL_NAME", "vosk-model-en-us-0.22")
    )
    raw_path = params.get("model_path") or os.environ.get("VOICESTUDIO_VOSK_MODEL_PATH")
    if isinstance(raw_path, str) and raw_path.strip():
        model_dir = Path(raw_path.strip()).expanduser()
    else:
        model_dir = Path(get_models_path()) / "vosk" / default_name
    _ensure_dir(model_dir.parent)

    if not model_dir.is_dir():
        raise _fail(
            {
                "message": (
                    f"Vosk model directory missing: {model_dir}. "
                    "Download a model from alphacephei.com/vosk/models into that path."
                ),
                "ok": False,
                "first_blocker": "model_dir_missing",
                "model_dir": str(model_dir),
            },
            status_code=424,
        )
    if not any(model_dir.iterdir()):
        raise _fail(
            {
                "message": f"Vosk model directory is empty: {model_dir}",
                "ok": False,
                "first_blocker": "model_dir_empty",
                "model_dir": str(model_dir),
            },
            status_code=424,
        )
    try:
        Model(str(model_dir))
    except Exception as e:
        raise _fail(
            {
                "message": f"{type(e).__name__}: {e}",
                "ok": False,
                "first_blocker": "model_load_failed",
                "model_dir": str(model_dir),
            },
            status_code=500,
        )

    return {
        "ok": True,
        "paths": [str(model_dir)],
        "downloaded": False,
        "message": f"Vosk ready (model_dir={model_dir})",
    }


def ensure_parakeet(auto_download: bool = True) -> dict[str, object]:
    """
    Readiness for engine_id ``parakeet`` (PaddleSpeech Parakeet TTS).

    Verifies ``paddlepaddle`` and ``paddlespeech`` imports and a non-empty checkpoints
    directory under ``<models>/parakeet/checkpoints``. No silent weight download.
    """
    try:
        import paddle
    except ImportError:
        raise _fail(
            {
                "message": "paddlepaddle not installed (required for Parakeet TTS).",
                "ok": False,
                "first_blocker": "paddle_missing",
            },
            status_code=503,
        )
    try:
        import paddlespeech
    except ImportError:
        raise _fail(
            {
                "message": "paddlespeech not installed (required for Parakeet TTS).",
                "ok": False,
                "first_blocker": "paddlespeech_missing",
            },
            status_code=503,
        )

    root = Path(get_models_path()) / "parakeet" / "checkpoints"
    _ensure_dir(root.parent)
    if not root.is_dir() or not any(root.iterdir()):
        raise _fail(
            {
                "message": f"Parakeet checkpoints directory missing or empty: {root}",
                "ok": False,
                "first_blocker": "checkpoints_missing",
            },
            status_code=424,
        )
    return {
        "ok": True,
        "paths": [str(root)],
        "downloaded": False,
        "message": f"Parakeet ready (checkpoints at {root})",
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


def ensure_whisper(auto_download: bool = True) -> dict[str, object]:
    """
    Preflight for engine_id ``whisper`` (``engines/audio/whisper/engine.manifest.json``).

    Runtime uses **faster-whisper**; this is the public ``checks.whisper`` entry and
    delegates to :func:`ensure_faster_whisper` (no alternate engine fallback).
    """
    return ensure_faster_whisper(auto_download=auto_download)


def run_preflight(auto_download: bool = True) -> dict[str, object]:
    """
    Run all pre-flight checks. Returns a summary dict.
    """
    results = {}
    checks = {
        "xtts_v2": ensure_xtts,
        "piper": ensure_piper,
        "espeak_ng": ensure_espeak_ng,
        "rhvoice": ensure_rhvoice,
        "silero": ensure_silero,
        "chatterbox": ensure_chatterbox,
        "tortoise": ensure_tortoise,
        "openvoice": ensure_openvoice,
        "whisper": ensure_whisper,
        "whisper_cpp": ensure_whisper_cpp,
        "faster_whisper": ensure_faster_whisper,
        "vosk": ensure_vosk,
        "parakeet": ensure_parakeet,
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


def _sha256_file(path: Path) -> str:
    """Compute SHA256 hex digest of a file."""
    import hashlib
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def model_integrity_check(models_root: str | Path) -> dict[str, object]:
    """
    Verify downloaded models match expected hashes (Item 33).

    Looks for .sha256 sidecar files next to assets (e.g. model.onnx.sha256
    containing one line: hexdigest). If present, verifies the asset file.
    Returns dict with keys: ok, checked, passed, failed, message.
    """
    root = Path(models_root)
    if not root.is_dir():
        return {
            "ok": True,
            "checked": 0,
            "passed": 0,
            "failed": 0,
            "message": "Models root not found; nothing to verify",
        }
    checked = 0
    passed = 0
    failed: list[dict] = []
    for sha_path in root.rglob("*.sha256"):
        asset_path = sha_path.with_suffix("")
        if not asset_path.is_file():
            continue
        checked += 1
        expected = sha_path.read_text().strip().split()[0]
        actual = _sha256_file(asset_path)
        if actual.lower() == expected.lower():
            passed += 1
        else:
            failed.append(
                {"path": str(asset_path), "expected": expected, "actual": actual}
            )
    ok = len(failed) == 0
    return {
        "ok": ok,
        "checked": checked,
        "passed": passed,
        "failed": failed,
        "message": f"Checked {checked} files, {passed} passed, {len(failed)} failed"
        if checked else "No .sha256 sidecars found",
    }
