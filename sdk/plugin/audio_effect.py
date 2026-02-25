"""Audio effect plugin base class."""

from __future__ import annotations

from abc import abstractmethod
from typing import Any

import numpy as np

from .base import VoiceStudioPlugin


class AudioEffectPlugin(VoiceStudioPlugin):
    """Base class for audio processing effect plugins.

    Implement ``process()`` to apply your audio effect. The host calls
    this with a numpy array of audio samples and effect parameters.

    Example::

        class GainPlugin(AudioEffectPlugin):
            plugin_id = "com.example.gain"
            name = "Gain"

            def process(self, audio, sample_rate, params):
                gain = params.get("gain_db", 0.0)
                return audio * (10 ** (gain / 20.0))

            def get_parameter_schema(self):
                return {
                    "gain_db": {"type": "number", "min": -60, "max": 24, "default": 0}
                }
    """

    @abstractmethod
    def process(
        self,
        audio: np.ndarray,
        sample_rate: int,
        params: dict[str, Any],
    ) -> np.ndarray:
        """Process audio samples with the effect.

        Args:
            audio: Input audio as float32 numpy array (samples x channels)
            sample_rate: Audio sample rate in Hz
            params: Effect parameters matching get_parameter_schema()

        Returns:
            Processed audio as float32 numpy array (same shape as input)
        """

    def get_parameter_schema(self) -> dict[str, Any]:
        """Return parameter schema for the effect UI.

        Each key is a parameter name, value is a dict with:
        - type: "number", "boolean", "string", "select"
        - min/max: for number type
        - default: default value
        - label: human-readable label
        """
        return {}

    def get_capabilities(self) -> list[str]:
        return ["audio_read", "audio_write"]

    def supports_realtime(self) -> bool:
        """Return True if this effect can process in real-time (low latency)."""
        return False
