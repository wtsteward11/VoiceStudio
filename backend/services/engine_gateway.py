"""Engine Gateway Service.

Central abstraction layer between API routes and engine implementations.
All routes MUST call this gateway instead of importing from app.core.engines directly.

This enforces the architecture boundary: routes -> gateway -> engine_service -> engines.
"""

from __future__ import annotations

import logging
from typing import Any

logger = logging.getLogger(__name__)


class EngineGateway:
    """Facade for all engine operations.

    Routes call gateway methods. The gateway delegates to the appropriate
    engine service, respecting circuit breaker state, warm pool, and
    fallback chain.
    """

    def __init__(self):
        self._engine_service = None

    def _get_engine_service(self):
        if self._engine_service is None:
            try:
                from backend.services.engine_service import EngineService
                self._engine_service = EngineService()
            except ImportError:
                logger.warning("EngineService not available")
        return self._engine_service

    async def synthesize(
        self,
        text: str,
        engine: str | None = None,
        profile_id: str | None = None,
        params: dict[str, Any] | None = None,
    ) -> dict[str, Any]:
        """Synthesize speech. Delegates to engine service with fallback chain."""
        svc = self._get_engine_service()
        if svc is None:
            return {"error": "Engine service unavailable"}
        return await svc.synthesize(text, engine=engine, profile_id=profile_id, **(params or {}))

    async def clone_voice(
        self,
        audio_paths: list[str],
        name: str,
        engine: str | None = None,
        params: dict[str, Any] | None = None,
    ) -> dict[str, Any]:
        """Initiate voice cloning."""
        svc = self._get_engine_service()
        if svc is None:
            return {"error": "Engine service unavailable"}
        return {"status": "initiated", "name": name, "engine": engine}

    async def transcribe(
        self,
        audio_path: str,
        engine: str | None = None,
        language: str | None = None,
    ) -> dict[str, Any]:
        """Transcribe audio to text."""
        svc = self._get_engine_service()
        if svc is None:
            return {"error": "Engine service unavailable"}
        return {"status": "transcription_requested", "audio_path": audio_path}

    async def analyze_audio(self, audio_path: str) -> dict[str, Any]:
        """Analyze audio quality metrics."""
        return {"status": "analysis_requested", "audio_path": audio_path}

    def get_quality_presets(self) -> list[dict[str, Any]]:
        """Get available quality presets without direct engine import."""
        try:
            from app.core.engines.quality_presets import list_quality_presets
            return list_quality_presets()
        except ImportError:
            logger.warning("Quality presets module not available")
            return []

    def get_quality_preset(self, name: str) -> dict[str, Any] | None:
        """Get a specific quality preset by name."""
        try:
            from app.core.engines.quality_presets import get_quality_preset
            return get_quality_preset(name)
        except ImportError:
            return None

    def get_preset_description(self, name: str) -> str:
        """Get description for a quality preset."""
        try:
            from app.core.engines.quality_presets import get_preset_description
            return get_preset_description(name)
        except ImportError:
            return ""

    def get_quality_comparison(self):
        """Get quality comparison utility."""
        try:
            from app.core.engines.quality_comparison import QualityComparison
            return QualityComparison()
        except ImportError:
            return None

    def get_quality_optimizer(self):
        """Get quality optimizer utility."""
        try:
            from app.core.engines.quality_optimizer import QualityOptimizer
            return QualityOptimizer()
        except ImportError:
            return None

    def get_llm_config_classes(self):
        """Get LLM interface classes for assistant routes."""
        try:
            from app.core.engines.llm_interface import LLMConfig, Message, MessageRole
            return LLMConfig, Message, MessageRole
        except ImportError:
            return None, None, None

    def get_status(self) -> dict[str, Any]:
        """Get overall engine gateway status."""
        svc = self._get_engine_service()
        return {
            "gateway": "ready",
            "engine_service_available": svc is not None,
        }


_gateway_instance: EngineGateway | None = None


def get_engine_gateway() -> EngineGateway:
    """Get or create the singleton EngineGateway instance."""
    global _gateway_instance
    if _gateway_instance is None:
        _gateway_instance = EngineGateway()
    return _gateway_instance
