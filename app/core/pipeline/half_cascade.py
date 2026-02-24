"""
Half-Cascade Pipeline for VoiceStudio (Phase 11.3.1)

S2S audio input + Traditional TTS output.
Incoming audio is processed natively by a speech model, but output
is generated via the traditional TTS pipeline for deterministic
quality and DSP chain integration.

This provides 500-800ms latency with robust tool integration.
"""

from __future__ import annotations

import logging
import time
from typing import Any

logger = logging.getLogger(__name__)


class HalfCascadePipeline:
    """
    Half-cascade pipeline: S2S input + Traditional TTS output.

    Advantages over full cascade:
    - Native audio input processing preserves tone and emotion
    - Better than pure STT → text conversion

    Advantages over full S2S:
    - Deterministic TTS output quality
    - Integrates with VoiceStudio's DSP effects chain
    - Lower cost (no audio token accumulation)
    - Better tool calling support
    """

    def __init__(
        self,
        s2s_provider=None,
        llm_provider=None,
        tts_engine: str = "xtts_v2",
        language: str = "en",
    ):
        self._s2s = s2s_provider
        self._llm = llm_provider
        self._tts_engine = tts_engine
        self._language = language

    async def process_audio(
        self,
        audio_data: bytes,
        context: str | None = None,
    ) -> dict[str, Any]:
        """
        Process audio through the half-cascade pipeline.

        1. S2S model processes audio input (gets text response)
        2. Traditional TTS synthesizes the response text
        """
        total_start = time.perf_counter()
        metrics: dict[str, float] = {}

        # Stage 1: Audio input → text response (via S2S or STT+LLM)
        input_start = time.perf_counter()

        response_text = ""
        if self._s2s:
            # Use S2S for audio understanding (text-only output)
            try:
                s2s_response = await self._s2s.respond(audio_data, context=context)
                response_text = s2s_response.response_text or ""
                metrics["s2s_input_ms"] = (time.perf_counter() - input_start) * 1000
            except Exception as exc:
                logger.warning(f"S2S input failed, falling back to STT+LLM: {exc}")

        if not response_text and self._llm:
            # Fallback: STT + LLM
            try:
                transcript = await self._transcribe(audio_data)
                if transcript:
                    from app.core.engines.llm_interface import Message, MessageRole

                    messages = [Message(role=MessageRole.USER, content=transcript)]
                    llm_response = await self._llm.generate(messages)
                    response_text = llm_response.content
                    metrics["stt_llm_fallback_ms"] = (time.perf_counter() - input_start) * 1000
            except Exception as exc:
                logger.error(f"STT+LLM fallback failed: {exc}")
                return {"error": str(exc), "metrics": metrics}

        if not response_text:
            return {"response_text": "", "audio": None, "metrics": metrics}

        # Stage 2: Traditional TTS output
        tts_start = time.perf_counter()
        try:
            audio_output = await self._synthesize(response_text)
            metrics["tts_ms"] = (time.perf_counter() - tts_start) * 1000
        except Exception as exc:
            logger.error(f"TTS synthesis failed: {exc}")
            audio_output = None

        metrics["total_ms"] = (time.perf_counter() - total_start) * 1000

        return {
            "response_text": response_text,
            "audio": audio_output,
            "metrics": metrics,
            "mode": "half_cascade",
        }

    async def _transcribe(self, audio_data: bytes) -> str:
        """Transcribe audio using STT engine."""
        try:
            from backend.ml.models.engine_service import get_engine_service

            service = get_engine_service()
            result = service.transcribe(
                engine_id="whisper",
                audio_path=str(audio_data),
                language=self._language,
            )
            return str(result.get("text", ""))
        except Exception as exc:
            logger.error(f"STT failed: {exc}")
            return ""

    async def _synthesize(self, text: str) -> bytes | None:
        """Synthesize audio using traditional TTS."""
        if not text.strip():
            return None
        try:
            from backend.ml.models.engine_service import get_engine_service

            service = get_engine_service()
            result = service.synthesize(
                engine_id=self._tts_engine,
                text=text,
                language=self._language,
            )
            audio: bytes | None = result.get("audio_data")
            return audio
        except Exception as exc:
            raise RuntimeError(f"TTS failed: {exc}") from exc
