"""
M4: TTS utilities service wrapper.

Routes import from backend.services.tts_utilities_service instead of
app.core.tts.tts_utilities or backend.tts.tts_utils.
"""

from __future__ import annotations

from backend.tts.tts_utils import synthesize_with_utility

__all__ = ["synthesize_with_utility"]
