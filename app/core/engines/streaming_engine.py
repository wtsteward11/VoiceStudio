"""
Streaming Engine for VoiceStudio
Real-time audio streaming synthesis and playback

Compatible with:
- Python 3.10+
- All TTS engines with streaming support
- asyncio for async streaming
"""

from __future__ import annotations

import asyncio
import hashlib
import logging
import queue
import threading
from collections import OrderedDict
from collections.abc import AsyncIterator, Callable, Iterator
from pathlib import Path
from typing import Any

import numpy as np
import numpy.typing as npt

# Import base protocol
try:
    from .protocols import EngineProtocol
except ImportError:
    from .base import EngineProtocol

logger = logging.getLogger(__name__)

# Optional dependencies for audio processing
# These are not directly used but may be needed by underlying engines
try:
    import importlib.util

    HAS_SOUNDFILE = importlib.util.find_spec("soundfile") is not None
except ImportError:
    HAS_SOUNDFILE = False

try:
    import importlib.util

    HAS_LIBROSA = importlib.util.find_spec("librosa") is not None
except ImportError:
    HAS_LIBROSA = False


class StreamingEngine(EngineProtocol):
    """
    Streaming Engine for real-time audio streaming synthesis.

    Supports:
    - Real-time streaming from any TTS engine
    - Chunked synthesis and buffering
    - Async and sync streaming interfaces
    - Multiple engine support
    - Audio buffering and queue management
    - Real-time playback coordination
    """

    # Default chunk size (characters)
    DEFAULT_CHUNK_SIZE = 100

    # Default buffer size (audio samples)
    DEFAULT_BUFFER_SIZE = 48000  # 2 seconds at 24kHz

    # Default sample rate
    DEFAULT_SAMPLE_RATE = 24000

    def __init__(
        self,
        engine: EngineProtocol | None = None,
        engine_name: str | None = None,
        device: str | None = None,
        gpu: bool = True,
        chunk_size: int = DEFAULT_CHUNK_SIZE,
        buffer_size: int = DEFAULT_BUFFER_SIZE,
        overlap_size: int = 0,
    ):
        """
        Initialize Streaming Engine.

        Args:
            engine: TTS engine instance to use for synthesis
            engine_name: Name of engine to load (if engine not provided)
            device: Device to use
            gpu: Whether to use GPU if available
            chunk_size: Number of characters per chunk
            buffer_size: Audio buffer size in samples
            overlap_size: Overlap size between chunks (samples)
        """
        super().__init__(device=device, gpu=gpu)

        self.engine = engine
        self.engine_name = engine_name
        self.chunk_size = chunk_size
        self.buffer_size = buffer_size
        self.overlap_size = overlap_size

        # Audio queue for streaming
        self._audio_queue: queue.Queue[npt.NDArray[np.float32]] = queue.Queue(maxsize=10)
        self._is_streaming = False
        self._stream_thread: threading.Thread | None = None

        # Buffer for overlap-add
        self._overlap_buffer: npt.NDArray[np.float32] | None = None

        # LRU caching for performance (optimized)
        self._chunk_cache: OrderedDict[str, list[str]] = OrderedDict()  # LRU cache for text chunks
        self._stream_cache: OrderedDict[str, list[npt.NDArray[np.float32]]] = (
            OrderedDict()
        )  # LRU cache for stream results
        self._cache_max_size = 200  # Increased cache size for better hit rate
        self.enable_cache = True
        self._cache_stats = {
            "chunk_hits": 0,
            "chunk_misses": 0,
            "stream_hits": 0,
            "stream_misses": 0,
        }

        # Buffer pool for reuse (optimized)
        self._buffer_pool: list[npt.NDArray[np.float32]] = []  # Pool of reusable buffers
        self._max_buffer_pool_size = 20  # Increased pool size for better reuse
        self._buffer_pool_stats = {"hits": 0, "misses": 0, "created": 0}

        # Connection pool for streaming endpoints (if used in web context)
        self._connection_pool: dict[str, Any] = {}  # connection_id -> connection_info
        self._max_connections = 100  # Maximum concurrent streaming connections

    def initialize(self) -> bool:
        """
        Initialize the streaming engine and underlying TTS engine.

        Returns:
            True if initialization successful, False otherwise
        """
        try:
            if self._initialized:
                return True

            logger.info("Initializing Streaming Engine")

            # Initialize underlying engine if needed
            if self.engine is None and self.engine_name:
                try:
                    from .router import router

                    self.engine = router.get_engine(self.engine_name)
                    if self.engine is None:
                        logger.error(f"Failed to load engine: {self.engine_name}")
                        return False
                except Exception as e:
                    logger.error(f"Failed to load engine: {e}")
                    return False

            if self.engine is None:
                logger.error("No engine provided and engine_name not set")
                return False

            # Initialize underlying engine
            if not self.engine.is_initialized() and not self.engine.initialize():
                logger.error("Failed to initialize underlying engine")
                return False

            self._initialized = True
            logger.info("Streaming Engine initialized successfully")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize Streaming Engine: {e}")
            self._initialized = False
            return False

    def synthesize_stream(
        self,
        text: str,
        speaker_wav: str | Path | None = None,
        language: str = "en",
        **kwargs: Any,
    ) -> Iterator[npt.NDArray[np.float32]]:
        """
        Stream synthesis in real-time chunks (synchronous).

        Args:
            text: Text to synthesize
            speaker_wav: Path to reference speaker audio
            language: Language code
            **kwargs: Additional synthesis parameters

        Yields:
            Audio chunks (numpy arrays)
        """
        if not self._initialized and not self.initialize():
            return

        try:
            if self.engine is None:
                return
            engine_ref: Any = self.engine
            if hasattr(engine_ref, "synthesize_stream"):
                yield from engine_ref.synthesize_stream(
                    text=text,
                    speaker_wav=speaker_wav,
                    language=language,
                    chunk_size=self.chunk_size,
                    **kwargs,
                )
            else:
                yield from self._synthesize_chunked(text, speaker_wav, language, **kwargs)

        except Exception as e:
            logger.error(f"Stream synthesis failed: {e}")

    async def synthesize_stream_async(
        self,
        text: str,
        speaker_wav: str | Path | None = None,
        language: str = "en",
        **kwargs: Any,
    ) -> AsyncIterator[npt.NDArray[np.float32]]:
        """
        Stream synthesis in real-time chunks (asynchronous).

        Args:
            text: Text to synthesize
            speaker_wav: Path to reference speaker audio
            language: Language code
            **kwargs: Additional synthesis parameters

        Yields:
            Audio chunks (numpy arrays)
        """
        if not self._initialized and not self.initialize():
            return

        try:
            # Run synchronous streaming in executor
            loop = asyncio.get_event_loop()

            def sync_stream() -> Iterator[npt.NDArray[np.float32]]:
                return self.synthesize_stream(text, speaker_wav, language, **kwargs)

            # Create async generator
            stream = await loop.run_in_executor(None, sync_stream)

            for chunk in stream:
                yield chunk
                await asyncio.sleep(0)  # Yield control

        except Exception as e:
            logger.error(f"Async stream synthesis failed: {e}")

    def _synthesize_chunked(
        self,
        text: str,
        speaker_wav: str | Path | None,
        language: str,
        **kwargs: Any,
    ) -> Iterator[npt.NDArray[np.float32]]:
        """Synthesize text in chunks (fallback method)."""
        # Check stream cache (LRU) - optimized
        if self.enable_cache:
            cache_key = hashlib.md5(
                f"{text}_{speaker_wav}_{language}_{self.chunk_size}".encode()
            ).hexdigest()
            if cache_key in self._stream_cache:
                logger.debug("Using cached stream results")
                self._stream_cache.move_to_end(cache_key)  # LRU update
                self._cache_stats["stream_hits"] += 1
                # Use buffer pool for copying chunks
                for chunk in self._stream_cache[cache_key]:
                    # Reuse buffers from pool when possible
                    if len(chunk) <= self.buffer_size:
                        output_buf = self._get_buffer_from_pool(len(chunk))
                        np.copyto(output_buf, chunk)
                        yield output_buf
                    else:
                        yield chunk.copy()
                return
            else:
                self._cache_stats["stream_misses"] += 1

        # Split text into chunks
        chunks = self._split_text_into_chunks(text, self.chunk_size)

        # Overlap buffer for smooth transitions
        overlap_buffer = None
        stream_result = []  # Store chunks for caching

        for chunk_text in chunks:
            try:
                if self.engine is None:
                    continue
                engine_ref: Any = self.engine
                audio = engine_ref.synthesize(
                    text=chunk_text,
                    speaker_wav=speaker_wav,
                    language=language,
                    **kwargs,
                )

                if audio is None or len(audio) == 0:
                    continue

                # Convert to numpy if needed
                if not isinstance(audio, np.ndarray):
                    audio = np.array(audio)

                # Apply overlap-add if needed
                if self.overlap_size > 0 and overlap_buffer is not None:
                    audio = self._apply_overlap_add(overlap_buffer, audio)

                # Update overlap buffer (optimized - reuse buffers)
                if self.overlap_size > 0 and len(audio) > self.overlap_size:
                    # Get buffer from pool for overlap
                    overlap_buf = self._get_buffer_from_pool(self.overlap_size)
                    np.copyto(overlap_buf, audio[-self.overlap_size :])
                    overlap_buffer = overlap_buf

                    # Get buffer for output chunk
                    output_size = len(audio) - self.overlap_size
                    output_chunk = self._get_buffer_from_pool(output_size)
                    np.copyto(output_chunk, audio[: -self.overlap_size])
                else:
                    # Get buffer from pool for output
                    output_chunk = self._get_buffer_from_pool(len(audio))
                    np.copyto(output_chunk, audio)

                # Store for caching (copy for cache, original can be reused)
                if self.enable_cache:
                    stream_result.append(output_chunk.copy())

                yield output_chunk

            except Exception as e:
                logger.error(f"Chunk synthesis failed: {e}")
                continue

        # Cache stream result (LRU)
        if self.enable_cache and stream_result:
            cache_key = hashlib.md5(
                f"{text}_{speaker_wav}_{language}_{self.chunk_size}".encode()
            ).hexdigest()
            # Manage cache size
            if len(self._stream_cache) >= self._cache_max_size:
                oldest_key = next(iter(self._stream_cache))
                del self._stream_cache[oldest_key]
            self._stream_cache[cache_key] = stream_result
            self._stream_cache.move_to_end(cache_key)  # LRU update

    def _split_text_into_chunks(self, text: str, chunk_size: int) -> list[str]:
        """Split text into chunks for streaming."""
        # Check LRU cache - optimized
        if self.enable_cache:
            cache_key = hashlib.md5(f"{text}_{chunk_size}".encode()).hexdigest()
            if cache_key in self._chunk_cache:
                logger.debug("Using cached text chunks")
                self._chunk_cache.move_to_end(cache_key)  # LRU update
                self._cache_stats["chunk_hits"] += 1
                return self._chunk_cache[cache_key].copy()
            else:
                self._cache_stats["chunk_misses"] += 1

        chunks = []
        words = text.split()

        current_chunk: list[str] = []
        current_length = 0

        for word in words:
            word_length = len(word) + 1  # +1 for space

            if current_length + word_length > chunk_size and current_chunk:
                chunks.append(" ".join(current_chunk))
                current_chunk = [word]
                current_length = word_length
            else:
                current_chunk.append(word)
                current_length += word_length

        if current_chunk:
            chunks.append(" ".join(current_chunk))

        # Cache result (LRU)
        if self.enable_cache:
            cache_key = hashlib.md5(f"{text}_{chunk_size}".encode()).hexdigest()
            # Manage cache size
            if len(self._chunk_cache) >= self._cache_max_size:
                oldest_key = next(iter(self._chunk_cache))
                del self._chunk_cache[oldest_key]
            self._chunk_cache[cache_key] = chunks.copy()
            self._chunk_cache.move_to_end(cache_key)  # LRU update

        return chunks

    def _get_buffer_from_pool(self, size: int) -> npt.NDArray[np.float32]:
        """Get buffer from pool or create new one (optimized)."""
        # Try to find suitable buffer in pool (exact size match preferred)
        best_match_idx = None
        best_match_size = float("inf")

        for i, buf in enumerate(self._buffer_pool):
            buf_size = len(buf)
            if buf_size >= size:
                # Prefer exact match or smallest suitable buffer
                if buf_size == size:
                    # Exact match - use immediately
                    self._buffer_pool.pop(i)
                    self._buffer_pool_stats["hits"] += 1
                    return buf.copy()
                elif buf_size < best_match_size:
                    best_match_idx = i
                    best_match_size = buf_size

        # Use best match if found
        if best_match_idx is not None:
            buf = self._buffer_pool.pop(best_match_idx)
            self._buffer_pool_stats["hits"] += 1
            return buf[:size].copy()  # Resize and copy

        # Create new buffer if pool empty or no suitable buffer
        self._buffer_pool_stats["misses"] += 1
        self._buffer_pool_stats["created"] += 1
        return np.zeros(size, dtype=np.float32)

    def _return_buffer_to_pool(self, buffer: npt.NDArray[np.float32]) -> None:
        """Return buffer to pool for reuse (optimized)."""
        if buffer is None or len(buffer) == 0:
            return

        # Only add if pool not full and buffer is reasonably sized
        if len(self._buffer_pool) < self._max_buffer_pool_size:
            # Clear buffer to avoid stale data
            buffer.fill(0)
            self._buffer_pool.append(buffer)

    def _cleanup_buffer_pool(self) -> None:
        """Clean up buffer pool if it's too large."""
        if len(self._buffer_pool) > self._max_buffer_pool_size:
            # Remove oldest buffers (FIFO)
            excess = len(self._buffer_pool) - self._max_buffer_pool_size
            for _ in range(excess):
                if self._buffer_pool:
                    self._buffer_pool.pop(0)

    def _apply_overlap_add(
        self, overlap: npt.NDArray[np.float32], audio: npt.NDArray[np.float32]
    ) -> npt.NDArray[np.float32]:
        """Apply overlap-add for smooth transitions with optimized buffer management."""
        if len(overlap) == 0 or len(audio) == 0:
            return audio

        overlap_len = min(len(overlap), len(audio), self.overlap_size)

        if overlap_len == 0:
            return audio

        # Cache fade windows (avoid recreating)
        if not hasattr(self, "_fade_cache"):
            self._fade_cache: dict[int, tuple[npt.NDArray[np.float32], npt.NDArray[np.float32]]] = (
                {}
            )

        cache_key = overlap_len
        if cache_key not in self._fade_cache:
            fade_out = np.linspace(1.0, 0.0, overlap_len, dtype=np.float32)
            fade_in = np.linspace(0.0, 1.0, overlap_len, dtype=np.float32)
            self._fade_cache[cache_key] = (fade_out, fade_in)

        fade_out, fade_in = self._fade_cache[cache_key]

        # Blend overlap region (in-place operations where possible)
        overlap_region = overlap[-overlap_len:]
        audio_region = audio[:overlap_len]

        # Get buffer from pool for blended region
        blended = self._get_buffer_from_pool(overlap_len)
        np.multiply(overlap_region, fade_out, out=blended)
        np.add(blended, audio_region * fade_in, out=blended)

        # Get buffer from pool for result
        result_size = len(blended) + len(audio[overlap_len:])
        result = self._get_buffer_from_pool(result_size)

        # Copy blended region
        result[:overlap_len] = blended
        # Copy remaining audio
        result[overlap_len:] = audio[overlap_len:]

        # Return blended buffer to pool (it's been copied)
        self._return_buffer_to_pool(blended)

        return result

    def start_streaming(
        self,
        text: str,
        speaker_wav: str | Path | None = None,
        language: str = "en",
        callback: Callable[[npt.NDArray[np.float32]], None] | None = None,
        **kwargs: Any,
    ) -> None:
        """
        Start streaming synthesis in background thread.

        Args:
            text: Text to synthesize
            speaker_wav: Path to reference speaker audio
            language: Language code
            callback: Callback function for each audio chunk
            **kwargs: Additional synthesis parameters
        """
        if self._is_streaming:
            logger.warning("Streaming already in progress")
            return

        self._is_streaming = True
        self._audio_queue = queue.Queue(maxsize=10)

        def stream_worker() -> None:
            try:
                for chunk in self.synthesize_stream(text, speaker_wav, language, **kwargs):
                    if not self._is_streaming:
                        break

                    # Add to queue
                    try:
                        self._audio_queue.put(chunk, timeout=1.0)
                    except queue.Full:
                        logger.warning("Audio queue full, dropping chunk")

                    # Call callback if provided
                    if callback:
                        try:
                            callback(chunk)
                        except Exception as e:
                            logger.error(f"Callback failed: {e}")

            except Exception as e:
                logger.error(f"Stream worker failed: {e}")
            finally:
                self._is_streaming = False

        self._stream_thread = threading.Thread(target=stream_worker, daemon=True)
        self._stream_thread.start()

    def stop_streaming(self) -> None:
        """Stop streaming synthesis."""
        self._is_streaming = False

        # Wait for thread to finish
        if self._stream_thread and self._stream_thread.is_alive():
            self._stream_thread.join(timeout=5.0)

        # Clear queue
        while not self._audio_queue.empty():
            try:
                self._audio_queue.get_nowait()
            except queue.Empty:
                break

    def get_next_chunk(self, timeout: float = 1.0) -> npt.NDArray[np.float32] | None:
        """
        Get next audio chunk from stream.

        Args:
            timeout: Timeout in seconds

        Returns:
            Audio chunk or None if timeout/end of stream
        """
        try:
            return self._audio_queue.get(timeout=timeout)
        except queue.Empty:
            return None

    def is_streaming(self) -> bool:
        """Check if streaming is in progress."""
        return self._is_streaming

    def synthesize(
        self,
        text: str,
        speaker_wav: str | Path | None = None,
        language: str = "en",
        output_path: str | Path | None = None,
        **kwargs: Any,
    ) -> npt.NDArray[np.float32] | None:
        """
        Synthesize full text (non-streaming).

        Args:
            text: Text to synthesize
            speaker_wav: Path to reference speaker audio
            language: Language code
            output_path: Optional path to save output
            **kwargs: Additional synthesis parameters

        Returns:
            Audio array or None if synthesis failed
        """
        if not self._initialized and not self.initialize():
            return None

        if self.engine is None:
            return None
        engine_ref: Any = self.engine
        result: npt.NDArray[np.float32] | None = engine_ref.synthesize(
            text=text,
            speaker_wav=speaker_wav,
            language=language,
            output_path=output_path,
            **kwargs,
        )
        return result

    def cleanup(self) -> None:
        """Clean up resources."""
        try:
            # Stop streaming if active
            if self._is_streaming:
                self.stop_streaming()

            # Clean up underlying engine
            if self.engine is not None:
                self.engine.cleanup()

            self._audio_queue = queue.Queue()
            self._overlap_buffer = None

            # Clear caches
            self._chunk_cache.clear()
            self._stream_cache.clear()

            # Clear buffer pool
            self._buffer_pool.clear()

            self._initialized = False
            logger.info("Streaming Engine cleaned up")
        except Exception as e:
            logger.warning(f"Error during Streaming Engine cleanup: {e}")

    def clear_cache(self) -> None:
        """Clear all caches."""
        self._chunk_cache.clear()
        self._stream_cache.clear()
        logger.info("Stream caches cleared")

    def get_cache_stats(self) -> dict[str, Any]:
        """Get cache statistics (enhanced)."""
        chunk_total = self._cache_stats["chunk_hits"] + self._cache_stats["chunk_misses"]
        stream_total = self._cache_stats["stream_hits"] + self._cache_stats["stream_misses"]

        chunk_hit_rate = (
            (self._cache_stats["chunk_hits"] / chunk_total * 100) if chunk_total > 0 else 0.0
        )
        stream_hit_rate = (
            (self._cache_stats["stream_hits"] / stream_total * 100) if stream_total > 0 else 0.0
        )

        buffer_pool_total = self._buffer_pool_stats["hits"] + self._buffer_pool_stats["misses"]
        buffer_hit_rate = (
            (self._buffer_pool_stats["hits"] / buffer_pool_total * 100)
            if buffer_pool_total > 0
            else 0.0
        )

        return {
            "chunk_cache_size": len(self._chunk_cache),
            "stream_cache_size": len(self._stream_cache),
            "max_cache_size": self._cache_max_size,
            "chunk_cache_hits": self._cache_stats["chunk_hits"],
            "chunk_cache_misses": self._cache_stats["chunk_misses"],
            "chunk_cache_hit_rate": f"{chunk_hit_rate:.2f}%",
            "stream_cache_hits": self._cache_stats["stream_hits"],
            "stream_cache_misses": self._cache_stats["stream_misses"],
            "stream_cache_hit_rate": f"{stream_hit_rate:.2f}%",
            "buffer_pool_size": len(self._buffer_pool),
            "buffer_pool_hits": self._buffer_pool_stats["hits"],
            "buffer_pool_misses": self._buffer_pool_stats["misses"],
            "buffer_pool_created": self._buffer_pool_stats["created"],
            "buffer_pool_hit_rate": f"{buffer_hit_rate:.2f}%",
            "cache_enabled": self.enable_cache,
            "active_connections": len(self._connection_pool),
            "max_connections": self._max_connections,
        }

    def get_info(self) -> dict[str, Any]:
        """Get engine information."""
        info = super().get_info()
        info.update(
            {
                "engine_name": (
                    self.engine_name or (self.engine.__class__.__name__ if self.engine else None)
                ),
                "chunk_size": self.chunk_size,
                "buffer_size": self.buffer_size,
                "overlap_size": self.overlap_size,
                "is_streaming": self._is_streaming,
            }
        )
        return info


def create_streaming_engine(
    engine: EngineProtocol | None = None,
    engine_name: str | None = None,
    device: str | None = None,
    gpu: bool = True,
    chunk_size: int = StreamingEngine.DEFAULT_CHUNK_SIZE,
    buffer_size: int = (StreamingEngine.DEFAULT_BUFFER_SIZE),
) -> StreamingEngine:
    """
    Factory function to create a Streaming Engine instance.

    Args:
        engine: TTS engine instance to use
        engine_name: Name of engine to load (if engine not provided)
        device: Device to use
        gpu: Whether to use GPU if available
        chunk_size: Number of characters per chunk
        buffer_size: Audio buffer size in samples

    Returns:
        Initialized StreamingEngine instance
    """
    engine_instance = StreamingEngine(
        engine=engine,
        engine_name=engine_name,
        device=device,
        gpu=gpu,
        chunk_size=chunk_size,
        buffer_size=buffer_size,
    )
    engine_instance.initialize()
    return engine_instance
