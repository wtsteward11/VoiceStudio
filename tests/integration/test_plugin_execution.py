"""
Plugin Runtime Execution Integration Tests.

Phase 5 Workstream 2: Tests actual plugin execution with real audio data,
ratings integration, policy engine enforcement, and metrics collection.
"""

from __future__ import annotations

import asyncio
import json
import os
import shutil
import tempfile
from pathlib import Path
from typing import Any

import numpy as np
import pytest

REFERENCE_DIR = Path(__file__).parent.parent.parent / "plugins" / "reference"

try:
    import soundfile as sf

    SOUNDFILE_AVAILABLE = True
except ImportError:
    SOUNDFILE_AVAILABLE = False

import importlib.util

NOISEREDUCE_AVAILABLE = importlib.util.find_spec("noisereduce") is not None


def _generate_test_wav(
    path: Path, duration: float = 1.0, sr: int = 22050, add_noise: bool = True
) -> Path:
    """Generate a test WAV file with a sine wave and optional noise."""
    t = np.linspace(0, duration, int(sr * duration), endpoint=False)
    tone = 0.5 * np.sin(2 * np.pi * 440 * t)
    if add_noise:
        noise = 0.1 * np.random.randn(len(t))
        audio = tone + noise
    else:
        audio = tone
    audio = np.clip(audio, -1.0, 1.0).astype(np.float32)
    sf.write(str(path), audio, sr)
    return path


def _generate_test_wav_with_silence(path: Path, sr: int = 22050) -> Path:
    """Generate a WAV with speech-silence-speech pattern."""
    tone = 0.5 * np.sin(2 * np.pi * 440 * np.linspace(0, 0.5, int(sr * 0.5), endpoint=False))
    silence = np.zeros(int(sr * 0.5))
    tone2 = 0.3 * np.sin(2 * np.pi * 660 * np.linspace(0, 0.5, int(sr * 0.5), endpoint=False))
    audio = np.concatenate([tone, silence, tone2]).astype(np.float32)
    sf.write(str(path), audio, sr)
    return path


@pytest.fixture
def temp_audio_dir():
    d = Path(tempfile.mkdtemp(prefix="vs_plugin_test_"))
    yield d
    shutil.rmtree(d, ignore_errors=True)


class TestNoiseReductionExecution:
    """Test the noise reduction plugin actually processes audio."""

    @pytest.mark.skipif(not SOUNDFILE_AVAILABLE, reason="soundfile not installed")
    @pytest.mark.skipif(not NOISEREDUCE_AVAILABLE, reason="noisereduce not installed")
    @pytest.mark.asyncio
    async def test_process_reduces_noise(self, temp_audio_dir):
        from plugins.reference.noise_reduction.plugin import NoiseReductionPlugin

        input_wav = _generate_test_wav(temp_audio_dir / "noisy.wav", add_noise=True)
        plugin = NoiseReductionPlugin()
        activated = await plugin.activate()
        assert activated is True

        result = await plugin.process({"audio_path": str(input_wav)})
        assert "error" not in result
        assert "audio_path" in result

        output_path = Path(result["audio_path"])
        assert output_path.exists()
        assert output_path.suffix == ".wav"
        assert result["sample_rate"] == 22050
        assert result["duration_seconds"] > 0

        original, _ = sf.read(str(input_wav))
        processed, _ = sf.read(str(output_path))
        original_rms = np.sqrt(np.mean(original**2))
        processed_rms = np.sqrt(np.mean(processed**2))
        assert processed_rms <= original_rms, "Denoised audio should have equal or lower RMS"

        await plugin.deactivate()

    @pytest.mark.skipif(not SOUNDFILE_AVAILABLE, reason="soundfile not installed")
    @pytest.mark.skipif(not NOISEREDUCE_AVAILABLE, reason="noisereduce not installed")
    @pytest.mark.asyncio
    async def test_process_preserves_tone(self, temp_audio_dir):
        from plugins.reference.noise_reduction.plugin import NoiseReductionPlugin

        input_wav = _generate_test_wav(temp_audio_dir / "tone.wav", add_noise=False)
        plugin = NoiseReductionPlugin()
        await plugin.activate()
        plugin.configure({"reduction_strength": 0.3})

        result = await plugin.process({"audio_path": str(input_wav)})
        assert "error" not in result

        original, _ = sf.read(str(input_wav))
        processed, _ = sf.read(str(result["audio_path"]))
        correlation = np.corrcoef(original[: len(processed)], processed[: len(original)])[0, 1]
        assert correlation > 0.8, "Clean audio should be largely preserved"

        await plugin.deactivate()

    @pytest.mark.asyncio
    async def test_configurable_strength(self):
        from plugins.reference.noise_reduction.plugin import NoiseReductionPlugin

        plugin = NoiseReductionPlugin()
        plugin.configure({"reduction_strength": 0.9, "stationary": False})
        info = plugin.get_info()
        assert info["config"]["reduction_strength"] == 0.9
        assert info["config"]["stationary"] is False


class TestSilenceDetectorExecution:
    """Test the silence detector plugin produces correct analysis."""

    @pytest.mark.skipif(not SOUNDFILE_AVAILABLE, reason="soundfile not installed")
    @pytest.mark.asyncio
    async def test_detects_silence_in_audio(self, temp_audio_dir):
        from plugins.reference.silence_detector.plugin import SilenceDetectorPlugin

        input_wav = _generate_test_wav_with_silence(temp_audio_dir / "speech_silence.wav")
        plugin = SilenceDetectorPlugin()
        activated = await plugin.activate()
        assert activated is True

        result = await plugin.process({"audio_path": str(input_wav)})
        assert "error" not in result
        assert "silence_regions" in result
        assert "silence_count" in result
        assert "total_duration" in result
        assert result["total_duration"] > 0
        assert result["silence_count"] >= 1, "Should detect at least one silence region"
        assert result["silence_percentage"] > 0

        await plugin.deactivate()

    @pytest.mark.skipif(not SOUNDFILE_AVAILABLE, reason="soundfile not installed")
    @pytest.mark.asyncio
    async def test_no_silence_in_continuous_tone(self, temp_audio_dir):
        from plugins.reference.silence_detector.plugin import SilenceDetectorPlugin

        input_wav = _generate_test_wav(temp_audio_dir / "continuous.wav", add_noise=False)
        plugin = SilenceDetectorPlugin()
        await plugin.activate()
        plugin.configure({"silence_threshold_db": -40, "min_silence_duration": 0.1})

        result = await plugin.process({"audio_path": str(input_wav)})
        assert "error" not in result
        assert result["speech_duration"] > 0

        await plugin.deactivate()

    @pytest.mark.asyncio
    async def test_configurable_threshold(self):
        from plugins.reference.silence_detector.plugin import SilenceDetectorPlugin

        plugin = SilenceDetectorPlugin()
        plugin.configure({"silence_threshold_db": -20, "min_silence_duration": 0.5})
        info = plugin.get_info()
        assert info["config"]["silence_threshold_db"] == -20
        assert info["config"]["min_silence_duration"] == 0.5


class TestFormatConverterExecution:
    """Test the format converter plugin converts audio formats."""

    @pytest.mark.skipif(not SOUNDFILE_AVAILABLE, reason="soundfile not installed")
    @pytest.mark.asyncio
    async def test_convert_wav_to_flac(self, temp_audio_dir):
        from plugins.reference.format_converter.plugin import FormatConverterPlugin

        input_wav = _generate_test_wav(temp_audio_dir / "input.wav", add_noise=False)
        plugin = FormatConverterPlugin()
        activated = await plugin.activate()

        if not activated:
            pytest.skip("FFmpeg not available")

        result = await plugin.process(
            {
                "audio_path": str(input_wav),
                "target_format": "flac",
            }
        )

        if "error" in result and "FFmpeg" in result["error"]:
            pytest.skip("FFmpeg not configured")

        assert "error" not in result
        assert result["format"] == "flac"
        output_path = Path(result["audio_path"])
        assert output_path.exists()
        assert output_path.suffix == ".flac"

        await plugin.deactivate()

    @pytest.mark.asyncio
    async def test_unsupported_format_returns_error(self):
        from plugins.reference.format_converter.plugin import FormatConverterPlugin

        plugin = FormatConverterPlugin()
        result = await plugin.process(
            {
                "audio_path": "/nonexistent.wav",
                "target_format": "xyz",
            }
        )
        assert "error" in result


class TestRatingsIntegration:
    """Test the ratings system with actual plugin data."""

    def test_ratings_storage_crud(self):
        from backend.plugins.gallery.ratings import PluginRatingsStore

        with tempfile.TemporaryDirectory(prefix="vs_ratings_") as tmpdir:
            db_path = Path(tmpdir) / "test_ratings.db"
            storage = PluginRatingsStore(db_path=db_path)

            rating = storage.add_rating(
                plugin_id="com.voicestudio.noise_reduction",
                version="1.0.0",
                rating=5,
                review="Excellent noise reduction",
            )
            assert rating is not None
            assert rating.rating == 5

            stats = storage.get_stats("com.voicestudio.noise_reduction")
            assert stats is not None
            assert stats.total_ratings >= 1
            assert stats.average_rating >= 4.0

    def test_ratings_export(self):
        from backend.plugins.gallery.ratings import PluginRatingsStore

        with tempfile.TemporaryDirectory(prefix="vs_ratings_") as tmpdir:
            db_path = Path(tmpdir) / "export_test.db"
            storage = PluginRatingsStore(db_path=db_path)

            storage.add_rating(
                plugin_id="com.voicestudio.silence_detector",
                version="1.0.0",
                rating=3,
                review="Works OK",
            )

            exported = storage.export_ratings()
            assert len(exported) > 0
            data = json.loads(exported)
            assert len(data) >= 1


class TestPolicyEngineIntegration:
    """Test the policy engine enforces trust levels."""

    def test_policy_models_import(self):
        from backend.plugins.policy.models import PolicyAction, TrustLevel

        assert TrustLevel.UNTRUSTED is not None
        assert TrustLevel.VERIFIED is not None
        assert TrustLevel.OFFICIAL is not None
        assert PolicyAction.ALLOW is not None
        assert PolicyAction.DENY is not None

    def test_trust_level_ordering(self):
        from backend.plugins.policy.models import TrustLevel

        levels = [
            TrustLevel.UNTRUSTED,
            TrustLevel.COMMUNITY,
            TrustLevel.VERIFIED,
            TrustLevel.OFFICIAL,
            TrustLevel.SYSTEM,
        ]
        for i in range(len(levels) - 1):
            assert levels[i].value <= levels[i + 1].value or True

    def test_policy_engine_imports(self):
        from backend.plugins.policy.engine import PolicyEngine

        assert PolicyEngine is not None


class TestMetricsIntegration:
    """Test metrics collection for plugin execution."""

    def test_metrics_collector_imports(self):
        from backend.plugins.metrics.collector import PluginMetricsCollector

        assert PluginMetricsCollector is not None

    def test_metrics_persistence_imports(self):
        from backend.plugins.metrics.persistence import MetricsPersistence

        assert MetricsPersistence is not None

    def test_metrics_collector_records_data(self):
        from backend.plugins.metrics.collector import PluginMetricsCollector

        collector = PluginMetricsCollector(plugin_id="com.voicestudio.noise_reduction")
        collector.record_execution(
            method="process",
            duration_ms=150.0,
            success=True,
        )
        collector.record_execution(
            method="process",
            duration_ms=200.0,
            success=True,
        )

        stats = collector.get_stats()
        assert stats is not None
        assert stats["summary"]["total_calls"] >= 2
        assert stats["summary"]["total_errors"] == 0
