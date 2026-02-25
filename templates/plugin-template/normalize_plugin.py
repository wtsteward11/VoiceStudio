"""Example VoiceStudio Plugin: Audio Normalizer.

Demonstrates the AudioEffectPlugin API by implementing a simple
peak normalization effect.

Usage:
    Copy this directory to %LOCALAPPDATA%\\VoiceStudio\\Plugins\\normalize\\
    and restart VoiceStudio. The effect will appear in the Effects Mixer.
"""

from __future__ import annotations

import logging
from typing import Any

import numpy as np

logger = logging.getLogger(__name__)

try:
    from sdk.plugin import AudioEffectPlugin
except ImportError:
    from voicestudio_sdk.plugin import AudioEffectPlugin


class NormalizePlugin(AudioEffectPlugin):
    """Peak normalization audio effect."""

    plugin_id = "com.example.normalize"
    name = "Audio Normalizer"
    version = "1.0.0"
    description = "Normalizes audio volume to a target peak level."

    def initialize(self, context: dict[str, Any]) -> None:
        logger.info(f"[{self.name}] Initialized")

    def cleanup(self) -> None:
        logger.info(f"[{self.name}] Cleaned up")

    def process(
        self,
        audio: np.ndarray,
        sample_rate: int,
        params: dict[str, Any],
    ) -> np.ndarray:
        target_db = params.get("target_db", -1.0)
        target_linear = 10 ** (target_db / 20.0)

        peak = np.max(np.abs(audio))
        if peak < 1e-10:
            return audio

        gain = target_linear / peak
        return (audio * gain).astype(np.float32)

    def get_parameter_schema(self) -> dict[str, Any]:
        return {
            "target_db": {
                "type": "number",
                "min": -30.0,
                "max": 0.0,
                "default": -1.0,
                "label": "Target Peak (dB)",
                "description": "Target peak level in decibels (0 dB = full scale)",
            }
        }

    def supports_realtime(self) -> bool:
        return True
