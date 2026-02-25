"""Engine plugin base class for adding voice synthesis engines."""

from __future__ import annotations

from abc import abstractmethod
from typing import Any

from .base import VoiceStudioPlugin


class EnginePlugin(VoiceStudioPlugin):
    """Base class for voice synthesis engine plugins.

    Implement ``synthesize()`` to add a new TTS/voice cloning engine.
    The host discovers engine plugins and makes them available in the
    engine selector dropdown.

    Example::

        class MyTTSEngine(EnginePlugin):
            plugin_id = "com.example.mytts"
            name = "MyTTS Engine"

            def synthesize(self, text, voice_profile, params):
                audio = my_tts_library.generate(text, voice=voice_profile)
                return {"audio": audio, "sample_rate": 22050}
    """

    @abstractmethod
    def synthesize(
        self,
        text: str,
        voice_profile: dict[str, Any] | None,
        params: dict[str, Any],
    ) -> dict[str, Any]:
        """Synthesize speech from text.

        Args:
            text: Text to synthesize
            voice_profile: Voice profile dict (model path, speaker embedding, etc.)
            params: Engine parameters (speed, pitch, temperature, etc.)

        Returns:
            Dict with "audio" (numpy array), "sample_rate" (int),
            and optionally "duration" (float), "quality_metrics" (dict)
        """

    def get_supported_languages(self) -> list[str]:
        """Return ISO 639-1 language codes this engine supports."""
        return ["en"]

    def get_capabilities(self) -> list[str]:
        return ["audio_write"]

    def get_engine_info(self) -> dict[str, Any]:
        """Return engine metadata for the UI."""
        return {
            "engine_id": self.plugin_id,
            "name": self.name,
            "version": self.version,
            "languages": self.get_supported_languages(),
            "supports_cloning": False,
            "supports_streaming": False,
        }
