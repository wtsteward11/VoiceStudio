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


def _check_whisper_cpp() -> dict:
    """Check whisper.cpp GGUF model exists."""
    models_root = _get_models_path()
    explicit = os.getenv("WHISPER_CPP_MODEL_PATH")
    if explicit:
        path = Path(explicit)
    else:
        path = models_root / "whisper" / "whisper-medium.en.gguf"

    exists = path.exists() and path.is_file()
    return {
        "ok": exists,
        "path": str(path),
        "message": f"Whisper GGUF at {path}" if exists else f"Missing: {path}. Download via scripts/download_all_models.py",
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
        "whisper_cpp": _check_whisper_cpp(),
        "piper": _check_piper(),
        "xtts": _check_xtts(),
        "test_audio": _check_test_audio(),
    }

    if check_backend:
        report["backend"] = _check_backend_health(check_backend)
    else:
        report["backend"] = {"ok": None, "message": "Skipped (use --check-backend URL)"}

    # Overall readiness for real-mode golden path
    engines_ok = report["whisper_cpp"]["ok"] and (report["piper"]["ok"] or report["xtts"]["ok"])
    report["ready_for_real_mode"] = (
        report["python"]["ok"]
        and report["packages"]["ok"]
        and engines_ok
        and (report["backend"]["ok"] if check_backend else False)
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
