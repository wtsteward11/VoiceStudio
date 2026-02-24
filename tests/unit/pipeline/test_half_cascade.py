"""
Unit tests for HalfCascadePipeline (Phase 11.3.1)
"""

from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from app.core.pipeline.half_cascade import HalfCascadePipeline


class TestHalfCascadePipeline:
    """Tests for HalfCascadePipeline class."""

    def setup_method(self):
        """Set up test fixtures."""
        self.mock_s2s = MagicMock()
        self.mock_llm = MagicMock()
        self.pipeline = HalfCascadePipeline(
            s2s_provider=self.mock_s2s,
            llm_provider=self.mock_llm,
            tts_engine="xtts_v2",
        )

    @pytest.mark.asyncio
    async def test_process_audio_uses_s2s_provider(self):
        """Test process_audio uses S2S provider for input."""
        self.mock_s2s.respond = AsyncMock(return_value=MagicMock(response_text="S2S response"))

        with patch("backend.services.engine_service.get_engine_service") as mock_service:
            mock_service.return_value.synthesize = AsyncMock(return_value={"audio_data": b"audio"})

            result = await self.pipeline.process_audio(b"audio_data")

            assert result["response_text"] == "S2S response"
            assert result["mode"] == "half_cascade"

    @pytest.mark.asyncio
    async def test_process_audio_fallback_to_stt_llm(self):
        """Test process_audio falls back to STT+LLM when S2S fails."""
        self.mock_s2s.respond = AsyncMock(side_effect=Exception("S2S failed"))
        self.mock_llm.generate = AsyncMock(return_value=MagicMock(content="LLM response"))

        with patch("backend.services.engine_service.get_engine_service") as mock_service:
            mock_service.return_value.transcribe = AsyncMock(return_value={"text": "Transcribed"})
            mock_service.return_value.synthesize = AsyncMock(return_value={"audio_data": b"audio"})

            result = await self.pipeline.process_audio(b"audio_data")

            assert result["response_text"] == "LLM response"
            assert "stt_llm_fallback_ms" in result["metrics"]

    @pytest.mark.asyncio
    async def test_process_audio_empty_response(self):
        """Test process_audio handles empty response."""
        self.mock_s2s.respond = AsyncMock(return_value=MagicMock(response_text=""))

        result = await self.pipeline.process_audio(b"audio_data")

        assert result["response_text"] == ""
        assert result["audio"] is None

    @pytest.mark.asyncio
    async def test_process_audio_tts_synthesis(self):
        """Test process_audio synthesizes audio output."""
        self.mock_s2s.respond = AsyncMock(return_value=MagicMock(response_text="Test"))

        with patch("backend.services.engine_service.get_engine_service") as mock_service:
            mock_service.return_value.synthesize = AsyncMock(
                return_value={"audio_data": b"synthesized_audio"}
            )

            result = await self.pipeline.process_audio(b"audio_data")

            assert result["audio"] == b"synthesized_audio"
            assert "tts_ms" in result["metrics"]

    @pytest.mark.asyncio
    async def test_process_audio_tts_error_non_fatal(self):
        """Test process_audio handles TTS error gracefully."""
        self.mock_s2s.respond = AsyncMock(return_value=MagicMock(response_text="Test"))

        with patch("backend.services.engine_service.get_engine_service") as mock_service:
            mock_service.return_value.synthesize = AsyncMock(side_effect=Exception("TTS failed"))

            result = await self.pipeline.process_audio(b"audio_data")

            assert result["response_text"] == "Test"
            assert result["audio"] is None

    @pytest.mark.asyncio
    async def test_process_audio_captures_metrics(self):
        """Test process_audio captures timing metrics."""
        self.mock_s2s.respond = AsyncMock(return_value=MagicMock(response_text="Test"))

        with patch("backend.services.engine_service.get_engine_service") as mock_service:
            mock_service.return_value.synthesize = AsyncMock(return_value={"audio_data": b"audio"})

            result = await self.pipeline.process_audio(b"audio_data")

            assert "total_ms" in result["metrics"]

    @pytest.mark.asyncio
    async def test_process_audio_returns_error_on_total_failure(self):
        """Test process_audio returns error when both S2S and fallback fail."""
        self.mock_s2s.respond = AsyncMock(side_effect=Exception("S2S failed"))
        self.mock_llm.generate = AsyncMock(side_effect=Exception("LLM failed"))

        with patch("backend.services.engine_service.get_engine_service") as mock_service:
            mock_service.return_value.transcribe = AsyncMock(return_value={"text": "Transcribed"})

            result = await self.pipeline.process_audio(b"audio_data")

            assert "error" in result

    @pytest.mark.asyncio
    async def test_process_audio_without_providers(self):
        """Test process_audio without any providers returns empty."""
        pipeline = HalfCascadePipeline(s2s_provider=None, llm_provider=None)

        result = await pipeline.process_audio(b"audio_data")

        assert result["response_text"] == ""
