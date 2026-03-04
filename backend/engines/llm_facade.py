"""
Backend facade for app.core.engines.llm_interface.

Routes must import from backend.engines.llm_facade, not app.core.engines.llm_interface.
"""

from __future__ import annotations

from app.core.engines.llm_interface import LLMConfig, Message, MessageRole

__all__ = ["LLMConfig", "Message", "MessageRole"]
