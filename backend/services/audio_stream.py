"""
Audio Streaming Service.

Task 4.1.2: WebSocket-based audio streaming for real-time processing.
Provides bidirectional audio streaming with low-latency buffering.
"""

from __future__ import annotations

import asyncio
import contextlib
import logging
import time
from collections import deque
from collections.abc import AsyncIterator, Callable
from dataclasses import dataclass, field
from enum import Enum
from typing import Any

import numpy as np

logger = logging.getLogger(__name__)


class StreamFormat(Enum):
    """Audio stream formats."""

    PCM_S16LE = "pcm_s16le"  # 16-bit signed little-endian
    PCM_F32LE = "pcm_f32le"  # 32-bit float little-endian
    OPUS = "opus"  # Opus codec (requires opuslib)


@dataclass
class StreamConfig:
    """Configuration for audio stream."""

    sample_rate: int = 16000
    channels: int = 1
    chunk_size: int = 1024  # Samples per chunk
    buffer_size: int = 4  # Number of chunks to buffer
    format: StreamFormat = StreamFormat.PCM_S16LE
    latency_target_ms: float = 50.0


@dataclass
class StreamStats:
    """Statistics for audio stream."""

    chunks_received: int = 0
    chunks_sent: int = 0
    bytes_received: int = 0
    bytes_sent: int = 0
    total_latency_ms: float = 0.0
    min_latency_ms: float = float("inf")
    max_latency_ms: float = 0.0
    dropped_chunks: int = 0
    start_time: float = field(default_factory=time.time)

    @property
    def avg_latency_ms(self) -> float:
        if self.chunks_sent == 0:
            return 0.0
        return self.total_latency_ms / self.chunks_sent

    @property
    def duration_seconds(self) -> float:
        return time.time() - self.start_time


class AudioBuffer:
    """
    Lock-free audio buffer for low-latency streaming.

    Uses a ring buffer to minimize allocation overhead.
    """

    def __init__(self, max_chunks: int = 16, chunk_size: int = 1024):
        self._max_chunks = max_chunks
        self._chunk_size = chunk_size
        self._buffer: deque = deque(maxlen=max_chunks)
        self._lock = asyncio.Lock()

    async def push(self, audio_chunk: np.ndarray) -> bool:
        """Add audio chunk to buffer."""
        async with self._lock:
            if len(self._buffer) >= self._max_chunks:
                return False  # Buffer full
            self._buffer.append(audio_chunk.copy())
            return True

    async def pop(self) -> np.ndarray | None:
        """Remove and return oldest audio chunk."""
        async with self._lock:
            if not self._buffer:
                return None
            return self._buffer.popleft()

    async def peek(self) -> np.ndarray | None:
        """Return oldest chunk without removing."""
        async with self._lock:
            if not self._buffer:
                return None
            return self._buffer[0]

    @property
    def size(self) -> int:
        """Current buffer size."""
        return len(self._buffer)

    @property
    def is_empty(self) -> bool:
        return len(self._buffer) == 0

    @property
    def is_full(self) -> bool:
        return len(self._buffer) >= self._max_chunks

    async def clear(self) -> None:
        """Clear all buffered audio."""
        async with self._lock:
            self._buffer.clear()


class AudioStreamProcessor:
    """
    Real-time audio stream processor.

    Features:
    - Bidirectional streaming
    - Low-latency buffering
    - Format conversion
    - Processing callback pipeline
    """

    def __init__(self, config: StreamConfig | None = None):
        self.config = config or StreamConfig()
        self._input_buffer = AudioBuffer(
            max_chunks=self.config.buffer_size * 2,
            chunk_size=self.config.chunk_size,
        )
        self._output_buffer = AudioBuffer(
            max_chunks=self.config.buffer_size * 2,
            chunk_size=self.config.chunk_size,
        )
        self._stats = StreamStats()
        self._processors: list[Callable] = []
        self._running = False
        self._processing_task: asyncio.Task | None = None

    def add_processor(
        self,
        processor: Callable[[np.ndarray, int], np.ndarray],
    ) -> None:
        """
        Add a processing callback to the pipeline.

        Args:
            processor: Function (audio_chunk, sample_rate) -> processed_chunk
        """
        self._processors.append(processor)

    def clear_processors(self) -> None:
        """Remove all processors."""
        self._processors.clear()

    async def start(self) -> None:
        """Start the stream processor."""
        if self._running:
            return

        self._running = True
        self._stats = StreamStats()
        self._processing_task = asyncio.create_task(self._process_loop())
        logger.info("Audio stream processor started")

    async def stop(self) -> None:
        """Stop the stream processor."""
        self._running = False
        if self._processing_task:
            self._processing_task.cancel()
            with contextlib.suppress(asyncio.CancelledError):
                await self._processing_task
        await self._input_buffer.clear()
        await self._output_buffer.clear()
        logger.info("Audio stream processor stopped")

    async def _process_loop(self) -> None:
        """Main processing loop."""
        while self._running:
            try:
                chunk = await self._input_buffer.pop()
                if chunk is None:
                    await asyncio.sleep(0.001)  # 1ms sleep when no data
                    continue

                start_time = time.time()

                # Run through processor pipeline
                processed = chunk
                for processor in self._processors:
                    try:
                        processed = processor(processed, self.config.sample_rate)
                    except Exception as e:
                        logger.error(f"Processor error: {e}")

                # Push to output buffer
                if not await self._output_buffer.push(processed):
                    self._stats.dropped_chunks += 1

                # Update latency stats
                latency = (time.time() - start_time) * 1000
                self._stats.total_latency_ms += latency
                self._stats.min_latency_ms = min(self._stats.min_latency_ms, latency)
                self._stats.max_latency_ms = max(self._stats.max_latency_ms, latency)
                self._stats.chunks_sent += 1

            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Processing loop error: {e}")

    async def feed_audio(self, audio_bytes: bytes) -> bool:
        """
        Feed raw audio bytes into the stream.

        Args:
            audio_bytes: Raw audio data in configured format

        Returns:
            True if accepted, False if buffer full
        """
        self._stats.chunks_received += 1
        self._stats.bytes_received += len(audio_bytes)

        # Convert bytes to numpy array
        audio = self._bytes_to_array(audio_bytes)
        return await self._input_buffer.push(audio)

    async def get_processed(self) -> bytes | None:
        """
        Get processed audio bytes.

        Returns:
            Processed audio bytes or None if not available
        """
        chunk = await self._output_buffer.pop()
        if chunk is None:
            return None

        audio_bytes = self._array_to_bytes(chunk)
        self._stats.bytes_sent += len(audio_bytes)
        return audio_bytes

    async def stream_processed(self) -> AsyncIterator[bytes]:
        """Async generator yielding processed audio chunks."""
        while self._running:
            chunk = await self.get_processed()
            if chunk:
                yield chunk
            else:
                await asyncio.sleep(0.001)

    def _bytes_to_array(self, audio_bytes: bytes) -> np.ndarray:
        """Convert raw bytes to numpy array."""
        if self.config.format == StreamFormat.PCM_S16LE:
            return np.frombuffer(audio_bytes, dtype=np.int16).astype(np.float32) / 32768.0
        elif self.config.format == StreamFormat.PCM_F32LE:
            return np.frombuffer(audio_bytes, dtype=np.float32)
        else:
            raise ValueError(f"Unsupported format: {self.config.format}")

    def _array_to_bytes(self, audio: np.ndarray) -> bytes:
        """Convert numpy array to raw bytes."""
        if self.config.format == StreamFormat.PCM_S16LE:
            return (audio * 32768.0).astype(np.int16).tobytes()
        elif self.config.format == StreamFormat.PCM_F32LE:
            return audio.astype(np.float32).tobytes()
        else:
            raise ValueError(f"Unsupported format: {self.config.format}")

    @property
    def stats(self) -> StreamStats:
        """Get current stream statistics."""
        return self._stats

    @property
    def is_running(self) -> bool:
        return self._running


class WebSocketAudioStream:
    """
    WebSocket-based audio streaming for real-time processing.

    Designed for FastAPI WebSocket endpoints.
    """

    def __init__(
        self,
        websocket: Any,  # FastAPI WebSocket
        config: StreamConfig | None = None,
    ):
        self._websocket = websocket
        self._processor = AudioStreamProcessor(config)
        self._receive_task: asyncio.Task | None = None
        self._send_task: asyncio.Task | None = None

    async def start(self) -> None:
        """Start bidirectional streaming."""
        await self._processor.start()
        self._receive_task = asyncio.create_task(self._receive_loop())
        self._send_task = asyncio.create_task(self._send_loop())
        logger.info("WebSocket audio stream started")

    async def stop(self) -> None:
        """Stop streaming."""
        await self._processor.stop()
        if self._receive_task:
            self._receive_task.cancel()
        if self._send_task:
            self._send_task.cancel()
        logger.info("WebSocket audio stream stopped")

    async def _receive_loop(self) -> None:
        """Receive audio from WebSocket."""
        try:
            while self._processor.is_running:
                data = await self._websocket.receive_bytes()
                await self._processor.feed_audio(data)
        except Exception as e:
            logger.debug(f"Receive loop ended: {e}")

    async def _send_loop(self) -> None:
        """Send processed audio via WebSocket."""
        try:
            async for chunk in self._processor.stream_processed():
                await self._websocket.send_bytes(chunk)
        except Exception as e:
            logger.debug(f"Send loop ended: {e}")

    def add_processor(self, processor: Callable) -> None:
        """Add audio processor to pipeline."""
        self._processor.add_processor(processor)

    @property
    def stats(self) -> StreamStats:
        return self._processor.stats
