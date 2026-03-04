"""
Audio bounded context.

Handles audio processing, streaming, analysis, and effects. Boundaries:
audio I/O and transformation; voice synthesis consumes audio from this context.

Routes use backend.audio.audio_utils, backend.audio.post_fx, backend.audio.audit
instead of app.core.audio.* to preserve route boundary (ADR-007).
"""

from . import audio_utils

__all__ = ["audio_utils"]
