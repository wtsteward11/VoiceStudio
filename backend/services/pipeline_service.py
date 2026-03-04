"""
M4: Pipeline service wrapper.

Provides pipeline orchestration facade.
Routes import from backend.services.pipeline_service.
"""

from __future__ import annotations

from typing import Any


def run_pipeline(
    pipeline_id: str,
    inputs: dict[str, Any] | None = None,
    **kwargs: Any,
) -> dict[str, Any]:
    """Run a pipeline. Facade for pipeline orchestration."""
    try:
        from backend.pipeline.facade import PipelineConfig, PipelineMode, PipelineOrchestrator

        config = PipelineConfig(
            mode=PipelineMode.BATCH,
            stt_engine=kwargs.get("stt_engine", "whisper"),
            llm_provider=kwargs.get("llm_provider", "ollama"),
            tts_engine=kwargs.get("tts_engine", "xtts_v2"),
            language=kwargs.get("language", "en"),
        )
        orchestrator = PipelineOrchestrator(config)
        if hasattr(orchestrator, "process"):
            result = orchestrator.process(inputs or {})
        else:
            result = {}
        return {"status": "ok", "result": result}
    except ImportError:
        pass
    return {"status": "error", "message": "Pipeline module not available"}
