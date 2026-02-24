"""
Unit tests for BatchPipeline (Phase 9.2.3)
"""

from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from app.core.pipeline.batch_pipeline import BatchPipeline, BatchResult


class TestBatchPipeline:
    """Tests for BatchPipeline class."""

    def setup_method(self):
        """Set up test fixtures."""
        self.mock_llm = MagicMock()
        self.mock_llm.generate = AsyncMock(return_value=MagicMock(content="Test response"))
        self.pipeline = BatchPipeline(
            llm_provider=self.mock_llm,
            stt_engine="whisper",
            tts_engine="xtts_v2",
        )

    @pytest.mark.asyncio
    async def test_process_text_returns_batch_result(self):
        """Test process_text returns BatchResult with LLM response."""
        with patch("backend.services.engine_service.get_engine_service") as mock_service:
            mock_service.return_value.synthesize = AsyncMock(return_value={"audio_data": b"audio"})

            result = await self.pipeline.process_text("Hello", synthesize=False)

            assert isinstance(result, BatchResult)
            assert result.llm_response == "Test response"
            assert result.error is None

    @pytest.mark.asyncio
    async def test_process_text_without_llm_returns_error(self):
        """Test process_text without LLM provider returns error."""
        pipeline = BatchPipeline(llm_provider=None)

        result = await pipeline.process_text("Hello")

        assert result.error is not None
        assert "LLM" in result.error

    @pytest.mark.asyncio
    async def test_process_text_with_synthesis(self):
        """Test process_text with TTS synthesis."""
        with patch("backend.services.engine_service.get_engine_service") as mock_service:
            mock_service.return_value.synthesize = AsyncMock(
                return_value={"audio_data": b"test_audio"}
            )

            result = await self.pipeline.process_text("Hello", synthesize=True)

            assert result.llm_response == "Test response"
            assert result.audio_data == b"test_audio"

    @pytest.mark.asyncio
    async def test_process_text_captures_metrics(self):
        """Test that processing captures timing metrics."""
        with patch("backend.services.engine_service.get_engine_service") as mock_service:
            mock_service.return_value.synthesize = AsyncMock(return_value={"audio_data": b"audio"})

            result = await self.pipeline.process_text("Hello", synthesize=True)

            assert result.metrics is not None
            assert "llm_ms" in result.metrics
            assert "total_ms" in result.metrics

    @pytest.mark.asyncio
    async def test_process_audio_with_transcription(self):
        """Test process_audio transcribes and generates response."""
        with patch("backend.services.engine_service.get_engine_service") as mock_service:
            mock_service.return_value.transcribe = AsyncMock(
                return_value={"text": "Transcribed text"}
            )
            mock_service.return_value.synthesize = AsyncMock(return_value={"audio_data": b"audio"})

            result = await self.pipeline.process_audio(b"audio_data")

            assert result.transcription == "Transcribed text"
            assert result.llm_response == "Test response"

    @pytest.mark.asyncio
    async def test_process_audio_empty_transcription(self):
        """Test process_audio handles empty transcription."""
        with patch("backend.services.engine_service.get_engine_service") as mock_service:
            mock_service.return_value.transcribe = AsyncMock(return_value={"text": ""})

            result = await self.pipeline.process_audio(b"audio_data")

            assert result.transcription == ""
            assert result.error is None

    @pytest.mark.asyncio
    async def test_process_batch(self):
        """Test process_batch handles multiple items."""
        with patch("backend.services.engine_service.get_engine_service") as mock_service:
            mock_service.return_value.synthesize = AsyncMock(return_value={"audio_data": b"audio"})

            results = await self.pipeline.process_batch(
                ["Item 1", "Item 2", "Item 3"],
                synthesize=False,
            )

            assert len(results) == 3
            assert all(isinstance(r, BatchResult) for r in results)

    @pytest.mark.asyncio
    async def test_process_text_handles_llm_error(self):
        """Test process_text handles LLM errors gracefully."""
        self.mock_llm.generate = AsyncMock(side_effect=Exception("LLM error"))

        result = await self.pipeline.process_text("Hello")

        assert result.error is not None
        assert "LLM failed" in result.error

    @pytest.mark.asyncio
    async def test_process_text_handles_tts_error(self):
        """Test process_text continues if TTS fails (non-fatal)."""
        with patch("backend.services.engine_service.get_engine_service") as mock_service:
            mock_service.return_value.synthesize = AsyncMock(side_effect=Exception("TTS error"))

            result = await self.pipeline.process_text("Hello", synthesize=True)

            # TTS failure is non-fatal in process_text
            assert result.llm_response == "Test response"
            assert result.audio_data is None
