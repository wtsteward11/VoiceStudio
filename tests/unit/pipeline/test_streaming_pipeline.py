"""
Unit tests for StreamingPipeline (Phase 9.2.2)
"""

from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from app.core.pipeline.streaming_pipeline import StreamChunk, StreamingPipeline


class TestStreamingPipeline:
    """Tests for StreamingPipeline class."""

    def setup_method(self):
        """Set up test fixtures."""
        self.mock_llm = MagicMock()
        self.pipeline = StreamingPipeline(
            llm_provider=self.mock_llm,
            stt_engine="whisper",
            tts_engine="xtts_v2",
        )

    @pytest.mark.asyncio
    async def test_process_text_stream_yields_tokens(self):
        """Test process_text_stream yields token chunks."""

        async def mock_stream(messages):
            for token in ["Hello", " ", "world"]:
                yield token

        self.mock_llm.generate_stream = mock_stream

        with patch("backend.services.engine_service.get_engine_service") as mock_service:
            mock_service.return_value.synthesize = AsyncMock(return_value={"audio_data": b"audio"})

            chunks = []
            async for chunk in self.pipeline.process_text_stream("test"):
                chunks.append(chunk)

            token_chunks = [c for c in chunks if c.chunk_type == "token"]
            assert len(token_chunks) > 0

    @pytest.mark.asyncio
    async def test_process_text_stream_without_llm_returns_error(self):
        """Test process_text_stream without LLM provider returns error."""
        pipeline = StreamingPipeline(llm_provider=None)

        chunks = []
        async for chunk in pipeline.process_text_stream("test"):
            chunks.append(chunk)

        assert len(chunks) == 1
        assert chunks[0].chunk_type == "error"

    @pytest.mark.asyncio
    async def test_process_text_stream_yields_complete(self):
        """Test process_text_stream yields a complete chunk at end."""

        async def mock_stream(messages):
            yield "Response"

        self.mock_llm.generate_stream = mock_stream

        with patch("backend.services.engine_service.get_engine_service") as mock_service:
            mock_service.return_value.synthesize = AsyncMock(return_value={"audio_data": None})

            chunks = []
            async for chunk in self.pipeline.process_text_stream("test"):
                chunks.append(chunk)

            complete_chunks = [c for c in chunks if c.chunk_type == "complete"]
            assert len(complete_chunks) == 1

    @pytest.mark.asyncio
    async def test_process_text_stream_handles_exception(self):
        """Test process_text_stream handles exceptions gracefully."""

        async def mock_stream(messages):
            yield "Start"
            raise Exception("Stream error")

        self.mock_llm.generate_stream = mock_stream

        chunks = []
        async for chunk in self.pipeline.process_text_stream("test"):
            chunks.append(chunk)

        error_chunks = [c for c in chunks if c.chunk_type == "error"]
        assert len(error_chunks) == 1

    def test_stop_sets_is_running_false(self):
        """Test stop method sets _is_running to False."""
        self.pipeline._is_running = True
        self.pipeline.stop()
        assert self.pipeline._is_running is False

    @pytest.mark.asyncio
    async def test_process_audio_stream_with_empty_transcript(self):
        """Test process_audio_stream with empty transcript."""

        async def mock_audio_chunks():
            yield b"audio_chunk"

        with patch("backend.services.engine_service.get_engine_service") as mock_service:
            mock_service.return_value.transcribe = AsyncMock(return_value={"text": ""})

            chunks = []
            async for chunk in self.pipeline.process_audio_stream(mock_audio_chunks()):
                chunks.append(chunk)

            complete_chunks = [c for c in chunks if c.chunk_type == "complete"]
            assert len(complete_chunks) == 1
            assert complete_chunks[0].content == ""

    @pytest.mark.asyncio
    async def test_stream_chunk_dataclass(self):
        """Test StreamChunk dataclass properties."""
        chunk = StreamChunk(
            chunk_type="token",
            content="test",
            timestamp_ms=100.0,
            metadata={"key": "value"},
        )

        assert chunk.chunk_type == "token"
        assert chunk.content == "test"
        assert chunk.timestamp_ms == 100.0
        assert chunk.metadata == {"key": "value"}
