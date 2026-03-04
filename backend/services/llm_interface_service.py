"""
M4: LLM interface service wrapper.

Routes import from backend.services.llm_interface_service instead of
app.core.engines.llm_interface or backend.engines.llm_facade.
"""

from __future__ import annotations

from backend.engines.llm_facade import LLMConfig, Message, MessageRole

__all__ = ["LLMConfig", "Message", "MessageRole"]
