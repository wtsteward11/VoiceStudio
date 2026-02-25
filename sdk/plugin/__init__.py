"""VoiceStudio Plugin SDK.

Base classes for developing VoiceStudio plugins. Plugins extend the application
with custom audio effects, UI panels, voice synthesis engines, and automation tools.

Usage:
    from voicestudio_sdk.plugin import AudioEffectPlugin

    class MyEffect(AudioEffectPlugin):
        def process(self, audio, params):
            return audio * params.get("gain", 1.0)
"""

from .base import VoiceStudioPlugin
from .audio_effect import AudioEffectPlugin
from .engine_plugin import EnginePlugin

__all__ = ["VoiceStudioPlugin", "AudioEffectPlugin", "EnginePlugin"]
