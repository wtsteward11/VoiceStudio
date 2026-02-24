"""
Engine Port Interfaces.

Defines abstract interfaces for engine operations. These ports serve as
the boundary between the application layer (routes, services) and the
infrastructure layer (concrete engine implementations).

Following Clean Architecture:
- Routes depend on these interfaces (not concrete engines)
- Engine adapters implement these interfaces
- Dependency injection provides the concrete implementations

Usage:
    from backend.interfaces import IEnginePort, ISynthesisEngine
    from backend.adapters.engine_adapter import get_engine_service

    engine_service: IEnginePort = get_engine_service()
    synthesis_engine: ISynthesisEngine = engine_service.get_synthesis_engine()
"""

from __future__ import annotations

from collections.abc import AsyncIterator
from dataclasses import dataclass, field
from enum import Enum
from typing import Any, Protocol, runtime_checkable

import numpy as np


class EngineCapability(Enum):
    """Engine capability types."""

    TTS = "tts"  # Text-to-speech
    STT = "stt"  # Speech-to-text
    VOICE_CONVERSION = "vc"  # Voice conversion (RVC, etc.)
    EMOTION = "emotion"  # Emotion detection/synthesis
    TRANSLATION = "translation"  # Voice/text translation
    LIP_SYNC = "lip_sync"  # Lip sync generation
    CLONING = "cloning"  # Voice cloning
    ENHANCEMENT = "enhancement"  # Audio enhancement


class EngineStatus(Enum):
    """Engine status."""

    UNLOADED = "unloaded"
    LOADING = "loading"
    READY = "ready"
    BUSY = "busy"
    ERROR = "error"
    UNLOADING = "unloading"


@dataclass
class EngineInfo:
    """Engine information."""

    engine_id: str
    name: str
    version: str
    capabilities: list[EngineCapability]
    status: EngineStatus
    device: str = "cpu"
    vram_usage_mb: float = 0
    metadata: dict[str, Any] = field(default_factory=dict)


@dataclass
class SynthesisRequest:
    """Request for speech synthesis."""

    text: str
    voice_id: str | None = None
    speaker_embedding: np.ndarray | None = None
    language: str = "en"
    sample_rate: int = 22050
    speed: float = 1.0
    pitch: float = 0.0
    emotion: str | None = None
    emotion_intensity: float = 1.0


@dataclass
class SynthesisResult:
    """Result from speech synthesis."""

    audio_data: np.ndarray
    sample_rate: int
    duration_seconds: float
    engine_used: str
    latency_ms: float
    metadata: dict[str, Any] = field(default_factory=dict)


@dataclass
class TranscriptionRequest:
    """Request for speech-to-text."""

    audio_data: np.ndarray
    sample_rate: int
    language: str | None = None  # None = auto-detect
    task: str = "transcribe"  # transcribe, translate
    word_timestamps: bool = False


@dataclass
class TranscriptionResult:
    """Result from speech-to-text."""

    text: str
    language: str
    confidence: float
    word_timestamps: list[dict[str, Any]] | None = None
    engine_used: str = ""
    latency_ms: float = 0


@dataclass
class VoiceConversionRequest:
    """Request for voice conversion."""

    audio_data: np.ndarray
    sample_rate: int
    target_voice_id: str | None = None
    pitch_shift: int = 0
    index_rate: float = 0.75


@dataclass
class VoiceConversionResult:
    """Result from voice conversion."""

    audio_data: np.ndarray
    sample_rate: int
    engine_used: str
    latency_ms: float
    quality_score: float = 0.0


@runtime_checkable
class IEnginePort(Protocol):
    """
    Main engine port interface.

    This is the primary interface that routes should use to access
    engine functionality. It provides factory methods for specific
    engine types and engine management operations.
    """

    def get_available_engines(self) -> list[EngineInfo]:
        """Get list of available engines with their status."""
        ...

    def get_engine_status(self, engine_id: str) -> EngineStatus:
        """Get status of a specific engine."""
        ...

    async def load_engine(self, engine_id: str) -> bool:
        """Load an engine into memory."""
        ...

    async def unload_engine(self, engine_id: str) -> bool:
        """Unload an engine from memory."""
        ...

    def get_synthesis_engine(self, engine_id: str | None = None) -> ISynthesisEngine:
        """Get a synthesis engine instance."""
        ...

    def get_transcription_engine(self, engine_id: str | None = None) -> ITranscriptionEngine:
        """Get a transcription engine instance."""
        ...

    def get_voice_conversion_engine(self, engine_id: str | None = None) -> IVoiceConversionEngine:
        """Get a voice conversion engine instance."""
        ...

    def get_emotion_engine(self) -> IEmotionEngine:
        """Get the emotion engine instance."""
        ...

    def get_translation_engine(self) -> ITranslationEngine:
        """Get the translation engine instance."""
        ...


@runtime_checkable
class ISynthesisEngine(Protocol):
    """Interface for TTS engines."""

    @property
    def engine_id(self) -> str:
        """Engine identifier."""
        ...

    @property
    def is_loaded(self) -> bool:
        """Check if engine is loaded and ready."""
        ...

    async def synthesize(self, request: SynthesisRequest) -> SynthesisResult:
        """Synthesize speech from text."""
        ...

    async def synthesize_streaming(
        self,
        request: SynthesisRequest,
    ) -> AsyncIterator[np.ndarray]:
        """Synthesize speech with streaming output."""
        ...

    def get_voices(self) -> list[dict[str, Any]]:
        """Get available voices for this engine."""
        ...

    def get_languages(self) -> list[str]:
        """Get supported languages."""
        ...


@runtime_checkable
class ITranscriptionEngine(Protocol):
    """Interface for STT engines."""

    @property
    def engine_id(self) -> str:
        """Engine identifier."""
        ...

    @property
    def is_loaded(self) -> bool:
        """Check if engine is loaded and ready."""
        ...

    async def transcribe(self, request: TranscriptionRequest) -> TranscriptionResult:
        """Transcribe audio to text."""
        ...

    async def transcribe_streaming(
        self,
        audio_stream: AsyncIterator[np.ndarray],
        sample_rate: int,
    ) -> AsyncIterator[TranscriptionResult]:
        """Transcribe streaming audio."""
        ...

    def get_languages(self) -> list[str]:
        """Get supported languages."""
        ...


@runtime_checkable
class IVoiceConversionEngine(Protocol):
    """Interface for voice conversion engines (RVC, etc.)."""

    @property
    def engine_id(self) -> str:
        """Engine identifier."""
        ...

    @property
    def is_loaded(self) -> bool:
        """Check if engine is loaded and ready."""
        ...

    async def convert(self, request: VoiceConversionRequest) -> VoiceConversionResult:
        """Convert voice in audio."""
        ...

    async def load_model(self, model_path: str, index_path: str | None = None) -> bool:
        """Load a voice model."""
        ...

    def get_models(self) -> list[dict[str, Any]]:
        """Get available voice models."""
        ...


@runtime_checkable
class IEmotionEngine(Protocol):
    """Interface for emotion detection and synthesis."""

    @property
    def is_loaded(self) -> bool:
        """Check if engine is loaded and ready."""
        ...

    async def detect(
        self,
        audio_data: np.ndarray,
        sample_rate: int,
    ) -> dict[str, Any]:
        """Detect emotion in audio."""
        ...

    async def apply_emotion(
        self,
        audio_data: np.ndarray,
        sample_rate: int,
        emotion: str,
        intensity: float = 1.0,
    ) -> np.ndarray:
        """Apply emotion to audio."""
        ...

    def get_emotions(self) -> list[str]:
        """Get supported emotions."""
        ...


@runtime_checkable
class ITranslationEngine(Protocol):
    """Interface for voice/text translation."""

    @property
    def is_loaded(self) -> bool:
        """Check if engine is loaded and ready."""
        ...

    async def translate(
        self,
        audio_data: np.ndarray,
        sample_rate: int,
        target_language: str,
        source_language: str | None = None,
    ) -> dict[str, Any]:
        """Translate voice to target language."""
        ...

    async def translate_text(
        self,
        text: str,
        target_language: str,
        source_language: str | None = None,
    ) -> str:
        """Translate text to target language."""
        ...

    def get_languages(self) -> list[str]:
        """Get supported languages."""
        ...
