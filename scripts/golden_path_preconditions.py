#!/usr/bin/env python3
"""
Golden Path Preconditions Check

Verifies environment and model availability before running the golden path E2E test.
Outputs a structured JSON report for CI and local runs.

Usage:
    python scripts/golden_path_preconditions.py
    python scripts/golden_path_preconditions.py --json
    python scripts/golden_path_preconditions.py --check-backend http://localhost:8000
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from datetime import datetime
from pathlib import Path

from _env_setup import PROJECT_ROOT


def _check_python_version() -> dict:
    """Check Python version meets minimum (3.9+)."""
    major, minor = sys.version_info[:2]
    ok = (major, minor) >= (3, 9)
    return {
        "ok": ok,
        "version": f"{major}.{minor}.{sys.version_info[2]}",
        "message": "Python 3.9+ required" if not ok else "Python version OK",
    }


def _has_pkg(name: str) -> bool:
    try:
        __import__(name.replace("-", "_"))
        return True
    except ImportError:
        return False


def _check_packages() -> dict:
    """Check required packages for golden path test."""
    required = ["requests", "numpy", "pytest"]
    optional = ["scipy"]
    missing = [p for p in required if not _has_pkg(p)]
    optional_missing = [p for p in optional if not _has_pkg(p)]

    ok = len(missing) == 0
    return {
        "ok": ok,
        "required": required,
        "missing_required": missing,
        "optional": optional,
        "optional_missing": optional_missing,
        "message": f"Missing required: {missing}" if missing else "All required packages OK",
    }


def _get_models_path() -> Path:
    """Resolve models root from env or default."""
    env_path = os.getenv("VOICESTUDIO_MODELS_PATH")
    if env_path:
        return Path(env_path)
    if os.name == "nt":
        program_data = os.getenv("PROGRAMDATA", "C:\\ProgramData")
        return Path(program_data) / "VoiceStudio" / "models"
    return Path(os.path.expanduser("~/.voicestudio/models"))


def _check_whisper() -> dict:
    """Check whisper model exists (whisper.cpp GGUF or faster_whisper CTranslate2)."""
    models_root = _get_models_path()
    explicit = os.getenv("WHISPER_CPP_MODEL_PATH")
    if explicit:
        path = Path(explicit)
        exists = path.exists() and path.is_file()
        return {
            "ok": exists,
            "path": str(path),
            "format": "whisper_cpp",
            "message": f"Whisper GGUF at {path}" if exists else f"Missing: {path}",
        }

    gguf_path = models_root / "whisper" / "whisper-medium.en.gguf"
    if gguf_path.exists() and gguf_path.is_file():
        return {
            "ok": True,
            "path": str(gguf_path),
            "format": "whisper_cpp",
            "message": f"Whisper GGUF at {gguf_path}",
        }

    # Fallback: project-root models when default path lacks whisper (e.g. CI/venv)
    project_gguf = PROJECT_ROOT / "models" / "whisper" / "whisper-medium.en.gguf"
    if project_gguf.exists() and project_gguf.is_file():
        return {
            "ok": True,
            "path": str(project_gguf),
            "format": "whisper_cpp",
            "message": f"Whisper GGUF at {project_gguf}",
        }

    fw_dir = models_root / "whisper"
    if fw_dir.exists():
        for sub in fw_dir.iterdir():
            if sub.is_dir() and (sub / "model.bin").exists():
                return {
                    "ok": True,
                    "path": str(sub),
                    "format": "faster_whisper",
                    "message": f"Faster Whisper at {sub}",
                }

    try:
        from faster_whisper.utils import get_assets_path
        cached = Path(get_assets_path())
        if cached.exists():
            return {
                "ok": True,
                "path": str(cached),
                "format": "faster_whisper_cached",
                "message": f"Faster Whisper cached at {cached}",
            }
    except (ImportError, Exception):
        pass

    try:
        from faster_whisper import WhisperModel
        WhisperModel("base", device="cpu", compute_type="int8")
        return {
            "ok": True,
            "path": "huggingface_cache",
            "format": "faster_whisper",
            "message": "Faster Whisper base loadable (cached by HuggingFace)",
        }
    except (ImportError, Exception):
        pass

    return {
        "ok": False,
        "path": str(gguf_path),
        "message": f"No whisper model found. Download via: python scripts/download_all_models.py --engine whisper",
    }


def _check_piper() -> dict:
    """Check Piper voice model (.onnx + .json) exists."""
    models_root = _get_models_path()
    voice = "en_US-amy-medium"
    base = models_root / "piper"
    onnx = base / f"{voice}.onnx"
    config = base / f"{voice}.onnx.json"
    # Also check nested path (rhasspy layout)
    onnx_alt = base / "en" / "en_US" / "amy" / "medium" / f"{voice}.onnx"
    config_alt = base / "en" / "en_US" / "amy" / "medium" / f"{voice}.onnx.json"

    exists = (onnx.exists() and config.exists()) or (onnx_alt.exists() and config_alt.exists())
    return {
        "ok": exists,
        "path": str(onnx) if onnx.exists() else str(onnx_alt) if onnx_alt.exists() else str(onnx),
        "message": "Piper voice ready" if exists else f"Missing Piper model. Download via scripts/download_all_models.py",
    }


def _check_xtts() -> dict:
    """Check XTTS model directory has content."""
    models_root = _get_models_path()
    xtts_dir = models_root / "xtts"
    has_files = any(p.is_file() for p in xtts_dir.rglob("*")) if xtts_dir.exists() else False
    return {
        "ok": has_files,
        "path": str(xtts_dir),
        "message": "XTTS assets present" if has_files else f"XTTS dir empty or missing. Coqui TTS downloads on first use.",
    }


def _check_backend_health(base_url: str) -> dict:
    """Check backend health endpoint via HTTP."""
    try:
        import requests
        url = f"{base_url.rstrip('/')}/api/health"
        r = requests.get(url, timeout=5)
        ok = r.status_code == 200
        return {
            "ok": ok,
            "url": url,
            "status_code": r.status_code,
            "message": "Backend healthy" if ok else f"Backend returned {r.status_code}",
        }
    except ImportError:
        return {"ok": False, "message": "requests not installed"}
    except Exception as e:
        return {"ok": False, "message": f"Backend unreachable: {e}"}


def _check_engine_availability(base_url: str) -> dict:
    """Check that backend has at least one TTS engine available for synthesis."""
    try:
        import requests
        url = f"{base_url.rstrip('/')}/api/engines"
        r = requests.get(url, timeout=10)
        if r.status_code != 200:
            return {
                "ok": False,
                "url": url,
                "status_code": r.status_code,
                "message": f"Engines endpoint returned {r.status_code}",
            }
        data = r.json()
        count = data.get("count", 0)
        available = data.get("available", False)
        engines = data.get("engines", [])
        tts_engines = [e for e in engines if isinstance(e, dict) and e.get("type") == "tts"]
        has_tts = len(tts_engines) > 0
        ok = count > 0 and available and has_tts
        return {
            "ok": ok,
            "url": url,
            "count": count,
            "available": available,
            "tts_count": len(tts_engines),
            "message": (
                f"{count} engines, {len(tts_engines)} TTS available"
                if ok
                else f"No TTS engines available (count={count}, available={available})"
            ),
        }
    except ImportError:
        return {"ok": False, "message": "requests not installed"}
    except Exception as e:
        return {"ok": False, "message": f"Engine availability check failed: {e}"}


def _generate_sine_wav(duration_sec: float = 1.0) -> bytes | None:
    """Generate a minimal WAV (sine tone) for smoke tests. Returns None if scipy unavailable."""
    try:
        import numpy as np
        import scipy.io.wavfile as wav
        import io

        sample_rate = 22050
        t = np.linspace(0, duration_sec, int(sample_rate * duration_sec), False)
        audio = (np.sin(2 * np.pi * 440 * t) * 32767).astype(np.int16)
        buf = io.BytesIO()
        wav.write(buf, sample_rate, audio)
        return buf.getvalue()
    except ImportError:
        return None


def _check_stt_smoke(base_url: str) -> dict:
    """Run STT smoke: upload tiny WAV, transcribe. Verifies engines can actually transcribe."""
    try:
        import requests

        wav_bytes = _generate_sine_wav(1.0)
        if not wav_bytes:
            return {
                "ok": False,
                "engine": None,
                "message": "scipy required for STT smoke (pip install scipy)",
            }

        url = base_url.rstrip("/")
        # Upload
        files = {"file": ("smoke.wav", wav_bytes, "audio/wav")}
        up = requests.post(
            f"{url}/api/library/assets/upload",
            files=files,
            data={"folder_id": None},
            timeout=10,
        )
        if up.status_code not in (200, 201):
            return {
                "ok": False,
                "engine": None,
                "message": f"Upload failed: {up.status_code} - {up.text[:200]}",
            }

        audio_id = up.json().get("id")
        if not audio_id:
            return {"ok": False, "engine": None, "message": "No audio_id in upload response"}

        # Transcribe (prefer whisper_cpp, fallback to whisper)
        for engine_name in ("whisper_cpp", "whisper"):
            trans = requests.post(
                f"{url}/api/transcribe/",
                json={
                    "audio_id": audio_id,
                    "engine": engine_name,
                    "language": "en",
                    "word_timestamps": False,
                },
                timeout=60,
            )
            if trans.status_code == 200:
                eng = trans.json().get("engine", engine_name)
                return {
                    "ok": True,
                    "engine": eng,
                    "message": f"STT smoke passed (engine: {eng})",
                }

        return {
            "ok": False,
            "engine": None,
            "message": f"Transcribe failed: {trans.status_code} - {trans.text[:200]}",
        }
    except ImportError:
        return {"ok": False, "engine": None, "message": "requests not installed"}
    except Exception as e:
        return {"ok": False, "engine": None, "message": f"STT smoke error: {e}"}


def _check_tts_smoke(base_url: str) -> dict:
    """Run TTS smoke: verify at least one TTS engine is loaded and reachable."""
    try:
        import requests

        url = base_url.rstrip("/")
        r = requests.get(f"{url}/api/engines", timeout=10)
        if r.status_code != 200:
            return {
                "ok": False,
                "message": f"Engines endpoint returned {r.status_code}",
            }
        data = r.json()
        engines = data.get("engines", [])
        tts = [
            e for e in engines
            if isinstance(e, dict) and e.get("type") == "tts"
        ]
        if not tts:
            return {"ok": False, "message": "No TTS engines available"}
        names = [e.get("id", "?") for e in tts[:5]]
        return {
            "ok": True,
            "message": f"TTS engines available: {', '.join(names)}",
        }
    except ImportError:
        return {"ok": False, "message": "requests not installed"}
    except Exception as e:
        return {"ok": False, "message": f"TTS smoke error: {e}"}


def _check_test_audio() -> dict:
    """Check test audio fixture exists."""
    paths = [
        PROJECT_ROOT / "tests" / "fixtures" / "audio" / "sample.wav",
        PROJECT_ROOT / "tests" / "fixtures" / "sample.wav",
        PROJECT_ROOT / "test_data" / "sample.wav",
    ]
    found = next((p for p in paths if p.exists()), None)
    ok = found is not None
    return {
        "ok": ok,
        "path": str(found) if found else "none",
        "message": f"Test audio at {found}" if found else "No fixture found; test will generate sine tone (scipy required)",
    }


def run_checks(check_backend: str | None = None) -> dict:
    """Run all preconditions checks and return report."""
    report = {
        "timestamp": datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ"),
        "python": _check_python_version(),
        "packages": _check_packages(),
        "models_path": str(_get_models_path()),
        "whisper_cpp": _check_whisper(),
        "piper": _check_piper(),
        "xtts": _check_xtts(),
        "test_audio": _check_test_audio(),
    }

    if check_backend:
        report["backend"] = _check_backend_health(check_backend)
        report["engines_available"] = _check_engine_availability(check_backend)
        report["stt_smoke"] = _check_stt_smoke(check_backend)
        report["tts_smoke"] = _check_tts_smoke(check_backend)
    else:
        report["backend"] = {"ok": None, "message": "Skipped (use --check-backend URL)"}
        report["engines_available"] = {"ok": None, "message": "Skipped (use --check-backend URL)"}
        report["stt_smoke"] = {"ok": None, "message": "Skipped (use --check-backend URL)"}
        report["tts_smoke"] = {"ok": None, "message": "Skipped (use --check-backend URL)"}

    # Overall readiness for real-mode golden path
    engines_ok = report["whisper_cpp"]["ok"] and (report["piper"]["ok"] or report["xtts"]["ok"])
    backend_ok = (
        report["backend"]["ok"] and report["engines_available"]["ok"]
        if check_backend
        else False
    )
    smoke_ok = (
        report["stt_smoke"]["ok"] and report["tts_smoke"]["ok"]
        if check_backend
        else False
    )
    report["ready_for_real_mode"] = (
        report["python"]["ok"]
        and report["packages"]["ok"]
        and engines_ok
        and backend_ok
        and smoke_ok
    )
    report["ready_for_stub_mode"] = report["python"]["ok"] and report["packages"]["ok"]

    return report


def main():
    parser = argparse.ArgumentParser(description="Golden path preconditions check")
    parser.add_argument("--json", action="store_true", help="Output JSON only")
    parser.add_argument(
        "--check-backend",
        type=str,
        default=None,
        metavar="URL",
        help="Check backend health at URL (e.g. http://localhost:8000)",
    )
    args = parser.parse_args()

    report = run_checks(check_backend=args.check_backend)

    if args.json:
        print(json.dumps(report, indent=2))
        return 0

    # Human-readable output
    print("Golden Path Preconditions Report")
    print("=" * 50)
    for key, val in report.items():
        if key in ("timestamp", "models_path", "ready_for_real_mode", "ready_for_stub_mode"):
            continue
        if isinstance(val, dict) and "ok" in val:
            status = "OK" if val["ok"] else ("SKIP" if val["ok"] is None else "FAIL")
            print(f"  {key}: [{status}] {val.get('message', val)}")
    print()
    print(f"  Models path: {report['models_path']}")
    print(f"  Ready for real mode: {report['ready_for_real_mode']}")
    print(f"  Ready for stub mode: {report['ready_for_stub_mode']}")

    return 0 if report["ready_for_stub_mode"] else 1


if __name__ == "__main__":
    sys.exit(main())
