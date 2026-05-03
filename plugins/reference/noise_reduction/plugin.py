"""
Noise Reduction Plugin for VoiceStudio.

Reference plugin demonstrating audio processing capabilities.
Uses noisereduce library for spectral gating noise reduction.
"""

from __future__ import annotations

import logging
from pathlib import Path
from typing import Any

import numpy as np

logger = logging.getLogger(__name__)

try:
    import noisereduce as nr
    NOISEREDUCE_AVAILABLE = True
except ImportError:
    NOISEREDUCE_AVAILABLE = False
    nr = None

try:
    import soundfile as sf
    SOUNDFILE_AVAILABLE = True
except ImportError:
    SOUNDFILE_AVAILABLE = False
    sf = None


class NoiseReductionPlugin:
    """Reduces background noise from audio using spectral gating."""

    PLUGIN_ID = "com.voicestudio.noise_reduction"
    PLUGIN_VERSION = "1.0.0"

    def __init__(self) -> None:
        self._config: dict[str, Any] = {
            "reduction_strength": 0.7,
            "stationary": True,
            "prop_decrease": 1.0,
        }

    async def activate(self) -> bool:
        if not NOISEREDUCE_AVAILABLE:
            logger.error("noisereduce library not installed")
            return False
        if not SOUNDFILE_AVAILABLE:
            logger.error("soundfile library not installed")
            return False
        logger.info("NoiseReductionPlugin activated")
        return True

    async def deactivate(self) -> None:
        logger.info("NoiseReductionPlugin deactivated")

    def configure(self, settings: dict[str, Any]) -> None:
        for key in ("reduction_strength", "stationary", "prop_decrease"):
            if key in settings:
                self._config[key] = settings[key]

    async def process(self, input_data: dict[str, Any]) -> dict[str, Any]:
        """
        Process audio to reduce noise.

        Args:
            input_data: Dict with 'audio_path' (str) pointing to input audio file.

        Returns:
            Dict with 'audio_path' (str) pointing to processed output file.
        """
        audio_path = Path(input_data["audio_path"])
        if not audio_path.exists():
            return {"error": f"Input file not found: {audio_path}"}

        if not NOISEREDUCE_AVAILABLE:
            return {"error": "noisereduce library not installed"}
        if not SOUNDFILE_AVAILABLE or sf is None:
            return {"error": "soundfile library not installed"}

        audio_data, sample_rate = sf.read(str(audio_path))

        reduced = nr.reduce_noise(
            y=audio_data,
            sr=sample_rate,
            stationary=self._config["stationary"],
            prop_decrease=self._config["prop_decrease"] * self._config["reduction_strength"],
        )

        output_path = audio_path.parent / f"{audio_path.stem}_denoised{audio_path.suffix}"
        sf.write(str(output_path), reduced, sample_rate)

        return {
            "audio_path": str(output_path),
            "sample_rate": sample_rate,
            "duration_seconds": len(reduced) / sample_rate,
            "reduction_strength": self._config["reduction_strength"],
        }

    def get_info(self) -> dict[str, Any]:
        return {
            "id": self.PLUGIN_ID,
            "version": self.PLUGIN_VERSION,
            "config": self._config,
            "noisereduce_available": NOISEREDUCE_AVAILABLE,
        }
