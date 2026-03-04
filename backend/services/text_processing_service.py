"""
M4: Text processing service wrapper.

Routes import from backend.services.text_processing_service instead of
app.core.nlp.text_processing or backend.nlp.text_processing.
"""

from __future__ import annotations

from backend.nlp.text_processing import get_text_preprocessor

__all__ = ["get_text_preprocessor"]
