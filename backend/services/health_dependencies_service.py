"""
M4: Health dependencies service wrapper.

Provides optional package availability for UI health display.
Routes import from backend.services.health_dependencies_service.
"""

from __future__ import annotations

from typing import Any


def get_optional_dependencies() -> dict[str, Any]:
    """Check availability of optional Python packages required by features."""
    packages = {
        "librosa": {"required_for": "Audio analysis, spectrogram generation"},
        "scipy": {"required_for": "Audio processing, spectral analysis"},
        "soundfile": {"required_for": "Audio file I/O"},
        "pydub": {"required_for": "Audio format conversion"},
        "faster_whisper": {"required_for": "Fast speech-to-text transcription"},
        "whisper": {"required_for": "OpenAI Whisper transcription"},
        "torch": {"required_for": "ML inference (TTS, voice cloning, RVC)"},
        "torchaudio": {"required_for": "Audio loading for ML models"},
        "TTS": {"required_for": "Coqui TTS / XTTS voice synthesis and training"},
        "numpy": {"required_for": "Core numerical operations"},
        "PIL": {"required_for": "Image processing (deepfake, image gen)"},
    }

    results: dict[str, Any] = {}
    available_count = 0

    for pkg, info in packages.items():
        try:
            __import__(pkg)
            results[pkg] = {"available": True, "required_for": info["required_for"]}
            available_count += 1
        except ImportError:
            results[pkg] = {"available": False, "required_for": info["required_for"]}

    return {
        "packages": results,
        "available": available_count,
        "total": len(packages),
        "all_available": available_count == len(packages),
    }
