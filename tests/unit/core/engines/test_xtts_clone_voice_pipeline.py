"""
Unit tests for XTTS clone_voice pipeline semantics.

Focus: ensure enhancement/metrics are applied exactly once when prosody control
is used.
"""

import sys
from collections.abc import Callable
from pathlib import Path
from typing import cast
from unittest.mock import MagicMock

import numpy as np
import pytest
from numpy.typing import NDArray

project_root = Path(__file__).parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

try:
    from app.core.engines.xtts_engine import TTS as _CoquiTTS
    from app.core.engines.xtts_engine import XTTSEngine
except ImportError:
    pytest.skip("Could not import XTTSEngine", allow_module_level=True)

# Optional Coqui dep: module imports can succeed while TTS is None.
if _CoquiTTS is None:
    pytest.skip(
        "Coqui TTS not installed on this runner; XTTS engine tests skipped",
        allow_module_level=True,
    )


AudioArray = NDArray[np.float32]
Metrics = dict[str, float]


class TestXTTSEngineCloneVoicePipeline:
    def test_clone_voice_without_prosody_delegates_to_synthesize(self):
        engine = XTTSEngine(device="cpu")
        clone_voice = cast(Callable[..., object], engine.clone_voice)

        synth_audio = np.zeros(320, dtype=np.float32)
        synth_metrics: Metrics = {"mos_score": 3.5}
        engine.synthesize = MagicMock(return_value=(synth_audio, synth_metrics))
        apply_prosody_mock = MagicMock()
        process_quality_mock = MagicMock()
        engine._apply_prosody_control = apply_prosody_mock
        engine._process_audio_quality = process_quality_mock

        result: object = clone_voice(
            reference_audio="ref.wav",
            text="hello",
            language="en",
            output_path=None,
            calculate_quality=True,
            enhance_quality=True,
            prosody_params=None,
            use_multi_reference=False,
        )

        engine.synthesize.assert_called_once_with(
            text="hello",
            speaker_wav="ref.wav",
            language="en",
            output_path=None,
            enhance_quality=True,
            calculate_quality=True,
        )
        apply_prosody_mock.assert_not_called()
        process_quality_mock.assert_not_called()

        assert isinstance(result, tuple)
        audio_out, metrics_out = cast(
            tuple[AudioArray, Metrics],
            result,
        )
        assert isinstance(audio_out, np.ndarray)
        assert np.allclose(audio_out, synth_audio)
        assert metrics_out == synth_metrics

    def test_clone_voice_with_prosody_enhances_once_after_prosody(self):
        engine = XTTSEngine(device="cpu")
        engine.tts = MagicMock(output_sample_rate=24000)
        clone_voice = cast(Callable[..., object], engine.clone_voice)

        synth_audio = np.ones(320, dtype=np.float32)
        audio_after_prosody = np.ones(320, dtype=np.float32) * 0.5
        final_audio = np.ones(320, dtype=np.float32) * 0.25
        final_metrics: Metrics = {"mos_score": 4.0}
        prosody_params = {
            "pitch": 1.05,
            "tempo": 1.0,
            "formant_shift": 0.0,
            "energy": 1.0,
        }

        engine.synthesize = MagicMock(return_value=synth_audio)
        apply_prosody_mock = MagicMock(return_value=audio_after_prosody)
        process_quality_mock = MagicMock(return_value=(final_audio, final_metrics))
        engine._apply_prosody_control = apply_prosody_mock
        engine._process_audio_quality = process_quality_mock

        result: object = clone_voice(
            reference_audio="ref.wav",
            text="hello",
            language="en",
            output_path=None,
            calculate_quality=True,
            enhance_quality=True,
            prosody_params=prosody_params,
            use_multi_reference=False,
        )

        # Critical: synthesize runs without enhancement.
        # Enhancement runs once after prosody.
        engine.synthesize.assert_called_once_with(
            text="hello",
            speaker_wav="ref.wav",
            language="en",
            output_path=None,
            enhance_quality=False,
            calculate_quality=False,
        )
        apply_prosody_mock.assert_called_once_with(synth_audio, 24000, prosody_params)
        process_quality_mock.assert_called_once_with(
            audio_after_prosody,
            24000,
            "ref.wav",
            enhance=True,
            calculate_metrics=True,
        )

        assert isinstance(result, tuple)
        audio_out, metrics_out = cast(
            tuple[AudioArray, Metrics],
            result,
        )
        assert isinstance(audio_out, np.ndarray)
        assert np.allclose(audio_out, final_audio)
        assert metrics_out == final_metrics

    def test_clone_voice_with_prosody_skips_quality_pass_when_disabled(self):
        engine = XTTSEngine(device="cpu")
        engine.tts = MagicMock(output_sample_rate=22050)
        clone_voice = cast(Callable[..., object], engine.clone_voice)

        synth_audio = np.ones(160, dtype=np.float32)
        audio_after_prosody = np.ones(160, dtype=np.float32) * 0.75
        prosody_params = {"pitch": 1.02}

        engine.synthesize = MagicMock(return_value=synth_audio)
        apply_prosody_mock = MagicMock(return_value=audio_after_prosody)
        process_quality_mock = MagicMock()
        engine._apply_prosody_control = apply_prosody_mock
        engine._process_audio_quality = process_quality_mock

        result: object = clone_voice(
            reference_audio="ref.wav",
            text="hello",
            language="en",
            output_path=None,
            calculate_quality=False,
            enhance_quality=False,
            prosody_params=prosody_params,
            use_multi_reference=False,
        )

        process_quality_mock.assert_not_called()
        assert isinstance(result, np.ndarray)
        result_arr = cast(AudioArray, result)
        assert np.allclose(result_arr, audio_after_prosody)
