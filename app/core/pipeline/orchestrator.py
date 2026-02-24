"""
Pipeline Orchestrator for VoiceStudio (Phase 9.2.1)

Chains STT → LLM → TTS components into a unified voice AI pipeline.
Supports streaming mode (low latency) and batch mode (high quality).
Uses a state machine to manage pipeline lifecycle and error recovery.
"""

from __future__ import annotations

import logging
import time
import uuid
from collections.abc import AsyncIterator, Callable
from dataclasses import dataclass
from enum import Enum
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)

# Project root for conversation history persistence
_PROJECT_ROOT = Path(__file__).resolve().parents[3]


class PipelineMode(str, Enum):
    """Pipeline execution mode."""

    STREAMING = "streaming"
    BATCH = "batch"
    HALF_CASCADE = "half_cascade"


class PipelineState(str, Enum):
    """Pipeline state machine states."""

    IDLE = "idle"
    LISTENING = "listening"
    TRANSCRIBING = "transcribing"
    REASONING = "reasoning"
    SYNTHESIZING = "synthesizing"
    PLAYING = "playing"
    ERROR = "error"


@dataclass
class PipelineMetrics:
    """Metrics for a single pipeline execution."""

    pipeline_id: str = ""
    mode: str = ""
    stt_latency_ms: float = 0.0
    llm_latency_ms: float = 0.0
    tts_latency_ms: float = 0.0
    total_latency_ms: float = 0.0
    time_to_first_token_ms: float = 0.0
    time_to_first_audio_ms: float = 0.0
    stt_text: str = ""
    llm_response_length: int = 0
    tts_audio_bytes: int = 0
    error: str | None = None
    timestamp: str = ""


@dataclass
class PipelineConfig:
    """Configuration for the pipeline."""

    mode: PipelineMode = PipelineMode.STREAMING
    stt_engine: str = "whisper"
    llm_provider: str = "ollama"
    tts_engine: str = "xtts_v2"
    language: str = "en"
    # STT config
    stt_model: str | None = None
    # LLM config
    llm_model: str | None = None
    llm_temperature: float = 0.7
    llm_max_tokens: int = 512
    llm_system_prompt: str | None = None
    # TTS config
    tts_voice: str | None = None
    tts_speaker_wav: str | None = None
    # Pipeline behavior
    enable_function_calling: bool = True
    buffer_ahead: bool = True
    max_conversation_turns: int = 50
    persist_conversation_to_disk: bool = True  # Enable sliding window short-term memory


class PipelineOrchestrator:
    """
    Orchestrates the STT → LLM → TTS pipeline.

    Manages the full lifecycle of a voice AI conversation, including
    state transitions, error recovery, and metrics collection.
    """

    def __init__(self, config: PipelineConfig | None = None):
        self._config = config or PipelineConfig()
        self._state = PipelineState.IDLE
        self._pipeline_id = f"pipe-{uuid.uuid4().hex[:8]}"
        self._conversation_history: list[dict[str, str]] = []
        self._metrics_history: list[PipelineMetrics] = []
        self._on_state_change: Callable | None = None
        self._llm_provider = None
        # Note: STT/TTS use get_engine_service() directly
        logger.info(f"Pipeline orchestrator created: {self._pipeline_id}")

    @property
    def state(self) -> PipelineState:
        return self._state

    @property
    def pipeline_id(self) -> str:
        return self._pipeline_id

    @property
    def metrics(self) -> list[PipelineMetrics]:
        return self._metrics_history

    def on_state_change(self, callback: Callable) -> None:
        """Register a callback for state changes."""
        self._on_state_change = callback

    def _persist_turn(self, role: str, content: str) -> None:
        """Persist a conversation turn to disk for short-term memory sliding window.

        This enables the ConversationSourceAdapter to read recent conversation
        context and inject it into future AI interactions.
        """
        if not self._config.persist_conversation_to_disk:
            return
        try:
            from tools.context.sources.conversation_adapter import append_turn

            append_turn(_PROJECT_ROOT, role, content)
        except Exception as e:
            # Non-critical: log but don't fail pipeline
            logger.debug(f"Conversation persistence failed: {e}")

    def _set_state(self, new_state: PipelineState) -> None:
        """Transition to a new state."""
        old_state = self._state
        self._state = new_state
        logger.debug(f"Pipeline state: {old_state.value} → {new_state.value}")
        if self._on_state_change:
            try:
                self._on_state_change(old_state, new_state)
            except Exception as exc:
                logger.warning(f"State change callback failed: {exc}")

    async def initialize(self) -> bool:
        """
        Initialize pipeline components.

        Lazily loads STT, LLM, and TTS providers based on config.
        """
        try:
            # Check STT engine availability (actual transcription uses engine service)
            if not self._check_engine_available(self._config.stt_engine, "stt"):
                logger.warning("No STT provider available, using engine service")

            # Initialize LLM provider (required for generation)
            self._llm_provider = self._create_llm_provider()
            if self._llm_provider is None:
                logger.warning("No LLM provider available")

            # Check TTS engine availability (actual synthesis uses engine service)
            if not self._check_engine_available(self._config.tts_engine, "tts"):
                logger.warning("No TTS provider available, using engine service")

            logger.info(f"Pipeline initialized: mode={self._config.mode.value}")
            return True
        except Exception as exc:
            logger.error(f"Pipeline initialization failed: {exc}")
            self._set_state(PipelineState.ERROR)
            return False

    def _check_engine_available(self, engine_name: str, engine_type: str) -> bool:
        """Check if an engine is available without creating an instance."""
        if not engine_name:
            return False
        try:
            from app.core.engines import get_engine_class

            engine_cls = get_engine_class(engine_name.lower())
            return engine_cls is not None
        except Exception as exc:
            logger.debug(f"Could not check {engine_type} engine '{engine_name}': {exc}")
            return False

    def _create_llm_provider(self):
        """Create the appropriate LLM provider."""
        provider_name = self._config.llm_provider.lower()

        if provider_name == "ollama":
            from app.core.engines.llm_interface import LLMConfig
            from app.core.engines.llm_local_adapter import OllamaLLMProvider

            config = LLMConfig(
                model=self._config.llm_model or "llama3.2",
                temperature=self._config.llm_temperature,
                max_tokens=self._config.llm_max_tokens,
                system_prompt=self._config.llm_system_prompt,
            )
            return OllamaLLMProvider(config)

        elif provider_name == "localai":
            from app.core.engines.llm_interface import LLMConfig
            from app.core.engines.llm_local_adapter import LocalAILLMProvider

            config = LLMConfig(
                model=self._config.llm_model or "gpt-3.5-turbo",
                temperature=self._config.llm_temperature,
                max_tokens=self._config.llm_max_tokens,
                system_prompt=self._config.llm_system_prompt,
            )
            return LocalAILLMProvider(config)

        elif provider_name == "openai":
            from app.core.engines.llm_interface import LLMConfig
            from app.core.engines.llm_openai_adapter import OpenAILLMProvider

            config = LLMConfig(
                model=self._config.llm_model or "gpt-4o-mini",
                temperature=self._config.llm_temperature,
                max_tokens=self._config.llm_max_tokens,
                system_prompt=self._config.llm_system_prompt,
            )
            return OpenAILLMProvider(config)

        logger.warning(f"Unknown LLM provider: {provider_name}")
        return None

    async def process_audio(
        self,
        audio_data: bytes,
        sample_rate: int = 16000,
    ) -> dict[str, Any]:
        """
        Process audio through the full STT → LLM → TTS pipeline (batch mode).

        Args:
            audio_data: Raw audio bytes (PCM or WAV).
            sample_rate: Audio sample rate.

        Returns:
            Dict with transcription, response text, and audio output.
        """
        metrics = PipelineMetrics(
            pipeline_id=self._pipeline_id,
            mode=self._config.mode.value,
            timestamp=time.strftime("%Y-%m-%dT%H:%M:%SZ"),
        )
        total_start = time.perf_counter()

        try:
            # Stage 1: STT
            self._set_state(PipelineState.TRANSCRIBING)
            stt_start = time.perf_counter()
            transcription = await self._transcribe(audio_data, sample_rate)
            metrics.stt_latency_ms = (time.perf_counter() - stt_start) * 1000
            metrics.stt_text = transcription

            if not transcription.strip():
                return {"transcription": "", "response": "", "audio": None}

            # Stage 2: LLM
            self._set_state(PipelineState.REASONING)
            llm_start = time.perf_counter()
            response_text = await self._generate_response(transcription)
            metrics.llm_latency_ms = (time.perf_counter() - llm_start) * 1000
            metrics.llm_response_length = len(response_text)

            # Stage 3: TTS
            self._set_state(PipelineState.SYNTHESIZING)
            tts_start = time.perf_counter()
            audio_output = await self._synthesize(response_text)
            metrics.tts_latency_ms = (time.perf_counter() - tts_start) * 1000
            metrics.tts_audio_bytes = len(audio_output) if audio_output else 0

            metrics.total_latency_ms = (time.perf_counter() - total_start) * 1000
            self._metrics_history.append(metrics)
            self._set_state(PipelineState.IDLE)

            return {
                "transcription": transcription,
                "response": response_text,
                "audio": audio_output,
                "metrics": {
                    "stt_ms": metrics.stt_latency_ms,
                    "llm_ms": metrics.llm_latency_ms,
                    "tts_ms": metrics.tts_latency_ms,
                    "total_ms": metrics.total_latency_ms,
                },
            }

        except Exception as exc:
            metrics.error = str(exc)
            metrics.total_latency_ms = (time.perf_counter() - total_start) * 1000
            self._metrics_history.append(metrics)
            self._set_state(PipelineState.ERROR)
            logger.error(f"Pipeline processing failed: {exc}")
            raise

    async def process_text(self, text: str) -> dict[str, Any]:
        """
        Process text through LLM → TTS pipeline (skip STT).

        Args:
            text: User text input.

        Returns:
            Dict with response text and audio output.
        """
        metrics = PipelineMetrics(
            pipeline_id=self._pipeline_id,
            mode="text_to_speech",
            stt_text=text,
            timestamp=time.strftime("%Y-%m-%dT%H:%M:%SZ"),
        )
        total_start = time.perf_counter()

        try:
            # LLM
            self._set_state(PipelineState.REASONING)
            llm_start = time.perf_counter()
            response_text = await self._generate_response(text)
            metrics.llm_latency_ms = (time.perf_counter() - llm_start) * 1000

            # TTS
            self._set_state(PipelineState.SYNTHESIZING)
            tts_start = time.perf_counter()
            audio_output = await self._synthesize(response_text)
            metrics.tts_latency_ms = (time.perf_counter() - tts_start) * 1000

            metrics.total_latency_ms = (time.perf_counter() - total_start) * 1000
            self._metrics_history.append(metrics)
            self._set_state(PipelineState.IDLE)

            return {
                "response": response_text,
                "audio": audio_output,
                "metrics": {
                    "llm_ms": metrics.llm_latency_ms,
                    "tts_ms": metrics.tts_latency_ms,
                    "total_ms": metrics.total_latency_ms,
                },
            }

        except Exception as exc:
            metrics.error = str(exc)
            self._set_state(PipelineState.ERROR)
            raise

    async def stream_text(self, text: str) -> AsyncIterator[dict[str, Any]]:
        """
        Stream LLM response tokens with buffer-ahead TTS (streaming mode).

        Yields partial results as they become available.
        """
        self._set_state(PipelineState.REASONING)
        total_start = time.perf_counter()
        first_token_received = False

        try:
            if self._llm_provider is None:
                raise RuntimeError("LLM provider not initialized")

            # Build messages
            messages = self._build_conversation_messages(text)

            # Stream tokens
            accumulated_text = ""
            async for token in self._llm_provider.generate_stream(messages):
                if not first_token_received:
                    ttft = (time.perf_counter() - total_start) * 1000
                    first_token_received = True
                    yield {
                        "type": "ttft",
                        "time_to_first_token_ms": ttft,
                    }

                accumulated_text += token
                yield {
                    "type": "token",
                    "content": token,
                    "accumulated": accumulated_text,
                }

            # Update conversation history (in-memory and disk persistence)
            self._conversation_history.append({"role": "user", "content": text})
            self._conversation_history.append({"role": "assistant", "content": accumulated_text})
            self._persist_turn("user", text)
            self._persist_turn("assistant", accumulated_text)

            # Final response
            yield {
                "type": "complete",
                "content": accumulated_text,
                "total_ms": (time.perf_counter() - total_start) * 1000,
            }

            self._set_state(PipelineState.IDLE)

        except Exception as exc:
            self._set_state(PipelineState.ERROR)
            yield {"type": "error", "error": str(exc)}

    def _build_conversation_messages(self, text: str) -> list:
        """Build LLM message list from conversation history."""
        from app.core.engines.llm_interface import Message, MessageRole

        messages = []
        # Include recent conversation history (limit to prevent context overflow)
        max_history = self._config.max_conversation_turns * 2
        for msg in self._conversation_history[-max_history:]:
            role = MessageRole.USER if msg["role"] == "user" else MessageRole.ASSISTANT
            messages.append(Message(role=role, content=msg["content"]))

        messages.append(Message(role=MessageRole.USER, content=text))
        return messages

    async def _transcribe(self, audio_data: bytes, sample_rate: int) -> str:
        """Transcribe audio using the configured STT engine."""
        try:
            import asyncio
            import tempfile

            from backend.ml.models.engine_service import get_engine_service

            service = get_engine_service()

            with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
                tmp.write(audio_data)
                tmp_path = tmp.name

            try:
                result = await asyncio.to_thread(
                    service.transcribe,
                    engine_id=self._config.stt_engine,
                    audio_path=tmp_path,
                    language=self._config.language,
                )
            finally:
                Path(tmp_path).unlink(missing_ok=True)

            return str(result.get("text", ""))
        except Exception as exc:
            logger.error(f"STT failed: {exc}")
            raise RuntimeError(f"Transcription failed: {exc}") from exc

    async def _generate_response(self, text: str) -> str:
        """Generate LLM response for the given text."""
        if self._llm_provider is None:
            raise RuntimeError("No LLM provider available")

        messages = self._build_conversation_messages(text)

        # Get function specs if enabled
        functions = None
        if self._config.enable_function_calling:
            try:
                from backend.ml.models.llm_function_calling import get_function_registry

                registry = get_function_registry()
                functions = registry.get_specs()
            except Exception as e:
                logger.debug("Function calling registry not available: %s", e)

        response = await self._llm_provider.generate(
            messages=messages,
            functions=functions,
        )

        # Handle tool calls - send results back to LLM for follow-up
        if response.tool_calls and self._config.enable_function_calling:
            try:
                from backend.ml.models.llm_function_calling import get_function_registry

                from app.core.engines.llm_interface import Message, MessageRole

                registry = get_function_registry()
                tool_results = await registry.process_tool_calls(response.tool_calls)

                # Add assistant message with tool calls to messages
                messages.append(
                    Message(
                        role=MessageRole.ASSISTANT,
                        content=response.content or "",
                        tool_calls=response.tool_calls,
                    )
                )

                # Add tool results as messages
                for result in tool_results:
                    messages.append(
                        Message(
                            role=MessageRole.TOOL,
                            content=result.get("content", str(result.get("result", ""))),
                            tool_call_id=result.get("tool_call_id", ""),
                        )
                    )

                # Generate follow-up response with tool results
                follow_up = await self._llm_provider.generate(messages=messages)
                response = follow_up
            except Exception as exc:
                logger.warning(f"Function call processing failed: {exc}")

        # Update history (in-memory and disk persistence)
        self._conversation_history.append({"role": "user", "content": text})
        self._conversation_history.append({"role": "assistant", "content": response.content})
        self._persist_turn("user", text)
        self._persist_turn("assistant", response.content)

        return response.content

    async def _synthesize(self, text: str) -> bytes | None:
        """Synthesize speech from text using the configured TTS engine."""
        if not text.strip():
            return None

        try:
            import asyncio

            from backend.ml.models.engine_service import get_engine_service

            service = get_engine_service()
            result = await asyncio.to_thread(
                service.synthesize,
                engine_id=self._config.tts_engine,
                text=text,
                voice_id=self._config.tts_voice,
                speaker_wav=self._config.tts_speaker_wav,
                language=self._config.language,
            )
            audio_data_val = result.get("audio_data")
            if isinstance(audio_data_val, bytes):
                return audio_data_val
            return None
        except Exception as exc:
            logger.error(f"TTS failed: {exc}")
            raise RuntimeError(f"Synthesis failed: {exc}") from exc

    def reset(self) -> None:
        """Reset conversation history and state."""
        self._conversation_history.clear()
        self._set_state(PipelineState.IDLE)
        logger.info(f"Pipeline reset: {self._pipeline_id}")

    async def cleanup(self) -> None:
        """Clean up pipeline resources."""
        if self._llm_provider and hasattr(self._llm_provider, "close"):
            await self._llm_provider.close()
        self._set_state(PipelineState.IDLE)
