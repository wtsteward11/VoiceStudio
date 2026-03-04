"""
Backend facade for app.core.nlp.text_processing.

Routes must import from backend.nlp.text_processing, not app.core.nlp.text_processing.
"""

from __future__ import annotations

from app.core.nlp.text_processing import get_text_preprocessor

__all__ = ["get_text_preprocessor"]
