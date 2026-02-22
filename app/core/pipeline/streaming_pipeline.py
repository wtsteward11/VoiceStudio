"""
Streaming Pipeline Mode for VoiceStudio (Phase 9.2.2)

Enables partial transcription → streaming LLM → buffer-ahead TTS
for low-latency conversational voice AI.
"""

from __future__ import annotations

import logging
import time
from collections.abc import AsyncIterator, Callable
from dataclasses import dataclass
from typing import Any

logger = logging.getLogger(__name__)


@dataclass
class StreamChunk:
    """A chunk of data flowing through the streaming pipeline."""

    chunk_type: str  # "partial_transcript", "token", "audio_chunk", "complete", "error"
    content: Any = None
    timestamp_ms: float = 0.0
    metadata: dict[str, Any] | None = None


class StreamingPipeline:
    """
    Streaming pipeline that processes audio in real-time.

    Key optimization: Each stage begins processing before the previous
    stage completes, minimizing perceived latency.

    Flow: Audio → [STT partial transcripts] → [LLM streaming tokens]
          → [TTS buffer-ahead audio] → Audio Output
    """

    def __init__(
        self,
        llm_provider=None,
        stt_engine: str = "whisper",
        tts_engine: str = "xtts_v2",
        buffer_size_tokens: int = 10,
    ):
        self._llm_provider = llm_provider
        self._stt_engine = stt_engine
        self._tts_engine = tts_engine
        self._buffer_size = buffer_size_tokens
        self._token_buffer: list = []
        self._is_running = False

    async def process_audio_stream(
        self,
        audio_chunks: AsyncIterator[bytes],
        on_chunk: Callable[[StreamChunk], None] | None = None,
    ) -> AsyncIterator[StreamChunk]:
        """
        Process an audio stream through the full pipeline.

        Args:
            audio_chunks: Async iterator of audio data chunks.
            on_chunk: Optional callback for each output chunk.

        Yields:
            StreamChunk objects as data flows through the pipeline.
        """
        self._is_running = True
        start_time = time.perf_counter()

        try:
            # Stage 1: Accumulate audio and transcribe
            accumulated_audio = bytearray()
            async for chunk in audio_chunks:
                if not self._is_running:
                    break
                accumulated_audio.extend(chunk)

            # Full transcription (for simplicity; partial would use WebSocket)
            transcript = await self._transcribe(bytes(accumulated_audio))

            yield StreamChunk(
                chunk_type="partial_transcript",
                content=transcript,
                timestamp_ms=(time.perf_counter() - start_time) * 1000,
            )

            if not transcript.strip():
                yield StreamChunk(chunk_type="complete", content="")
                return

            # Stage 2: Stream LLM response
            if self._llm_provider:
                from app.core.engines.llm_interface import Message, MessageRole

                messages = [Message(role=MessageRole.USER, content=transcript)]
                accumulated_text = ""
                first_token = True

                async for token in self._llm_provider.generate_stream(messages):
                    accumulated_text += token
                    self._token_buffer.append(token)

                    if first_token:
                        yield StreamChunk(
                            chunk_type="token",
                            content=token,
                            timestamp_ms=(time.perf_counter() - start_time) * 1000,
                            metadata={"first_token": True},
                        )
                        first_token = False
                    else:
                        yield StreamChunk(
                            chunk_type="token",
                            content=token,
                            timestamp_ms=(time.perf_counter() - start_time) * 1000,
                        )

                    # Buffer-ahead: when we have enough tokens, trigger TTS
                    if len(self._token_buffer) >= self._buffer_size:
                        sentence = "".join(self._token_buffer)
                        self._token_buffer.clear()

                        # Check for sentence boundaries for better TTS
                        if any(p in sentence for p in ".!?"):
                            audio_data = await self._synthesize(sentence)
                            if audio_data:
                                yield StreamChunk(
                                    chunk_type="audio_chunk",
                                    content=audio_data,
                                    timestamp_ms=(time.perf_counter() - start_time) * 1000,
                                )

                # Flush remaining tokens
                if self._token_buffer:
                    remaining = "".join(self._token_buffer)
                    self._token_buffer.clear()
                    audio_data = await self._synthesize(remaining)
                    if audio_data:
                        yield StreamChunk(
                            chunk_type="audio_chunk",
                            content=audio_data,
                            timestamp_ms=(time.perf_counter() - start_time) * 1000,
                        )

                yield StreamChunk(
                    chunk_type="complete",
                    content=accumulated_text,
                    timestamp_ms=(time.perf_counter() - start_time) * 1000,
                )

        except Exception as exc:
            logger.error(f"Streaming pipeline error: {exc}")
            yield StreamChunk(
                chunk_type="error",
                content=str(exc),
                timestamp_ms=(time.perf_counter() - start_time) * 1000,
            )
        finally:
            self._is_running = False

    async def process_text_stream(
        self,
        text: str,
    ) -> AsyncIterator[StreamChunk]:
        """
        Stream LLM response with buffer-ahead TTS for text input.
        """
        start_time = time.perf_counter()

        if self._llm_provider is None:
            yield StreamChunk(chunk_type="error", content="No LLM provider available")
            return

        try:
            from app.core.engines.llm_interface import Message, MessageRole

            messages = [Message(role=MessageRole.USER, content=text)]
            accumulated = ""
            sentence_buffer = ""

            async for token in self._llm_provider.generate_stream(messages):
                accumulated += token
                sentence_buffer += token

                yield StreamChunk(
                    chunk_type="token",
                    content=token,
                    timestamp_ms=(time.perf_counter() - start_time) * 1000,
                )

                # Synthesize on sentence boundaries
                if any(sentence_buffer.endswith(p) for p in [".", "!", "?", "\n"]):
                    audio = await self._synthesize(sentence_buffer.strip())
                    if audio:
                        yield StreamChunk(
                            chunk_type="audio_chunk",
                            content=audio,
                            timestamp_ms=(time.perf_counter() - start_time) * 1000,
                        )
                    sentence_buffer = ""

            # Flush remaining
            if sentence_buffer.strip():
                audio = await self._synthesize(sentence_buffer.strip())
                if audio:
                    yield StreamChunk(
                        chunk_type="audio_chunk",
                        content=audio,
                        timestamp_ms=(time.perf_counter() - start_time) * 1000,
                    )

            yield StreamChunk(
                chunk_type="complete",
                content=accumulated,
                timestamp_ms=(time.perf_counter() - start_time) * 1000,
            )

        except Exception as exc:
            yield StreamChunk(chunk_type="error", content=str(exc))

    def stop(self) -> None:
        """Stop the streaming pipeline."""
        self._is_running = False

    async def _transcribe(self, audio_data: bytes) -> str:
        """Transcribe audio using the configured STT engine."""
        try:
            from backend.ml.models.engine_service import get_engine_service

            service = get_engine_service()
            result = service.transcribe(engine_id=self._stt_engine, audio_path=str(audio_data))
            return str(result.get("text", ""))
        except Exception as exc:
            logger.error(f"STT failed: {exc}")
            return ""

    async def _synthesize(self, text: str) -> bytes | None:
        """Synthesize speech from text."""
        if not text.strip():
            return None
        try:
            from backend.ml.models.engine_service import get_engine_service

            service = get_engine_service()
            result = service.synthesize(engine_id=self._tts_engine, text=text)
            audio: bytes | None = result.get("audio_data")
            return audio
        except Exception as exc:
            logger.error(f"TTS synthesis failed: {exc}")
            return None
