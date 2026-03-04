"""
Backend facade for app.core.tts.tts_utilities.

Routes must import from backend.tts.tts_utils, not app.core.tts.tts_utilities.
"""

from __future__ import annotations

from app.core.tts.tts_utilities import synthesize_with_utility

__all__ = ["synthesize_with_utility"]
