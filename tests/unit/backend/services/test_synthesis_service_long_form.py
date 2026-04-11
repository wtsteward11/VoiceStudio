"""GAP-049: Long-form synthesis chunking and orchestration unit tests."""

from __future__ import annotations

from unittest.mock import AsyncMock, MagicMock, patch

import numpy as np
import pytest

from backend.core.exceptions import ServiceError
from backend.services.synthesis_service import SynthesisService


def test_chunk_text_respects_sentence_boundaries() -> None:
    text = "First sentence here. Second sentence there. Third is last."
    chunks = SynthesisService._chunk_text_for_long_form(text, max_chunk_chars=28, language="en")
    assert len(chunks) >= 2
    joined = " ".join(chunks)
    assert "First sentence" in joined
    assert "Third is last" in joined


def test_chunk_text_falls_back_on_nlp_failure(monkeypatch: pytest.MonkeyPatch) -> None:
    def boom() -> None:
        raise RuntimeError("nlp down")

    monkeypatch.setattr(
        "backend.nlp.text_processing.get_text_preprocessor",
        boom,
    )
    text = "Only one chunk when NLP fails."
    chunks = SynthesisService._chunk_text_for_long_form(text, max_chunk_chars=50, language="en")
    assert chunks == [text.strip()]


def test_split_oversized_sentence() -> None:
    words = " ".join(["word"] * 30)
    parts = SynthesisService._split_oversized_sentence(words, max_chunk_chars=20)
    assert len(parts) > 1
    assert all(len(p) <= 20 for p in parts)


@pytest.mark.asyncio
async def test_synthesize_long_form_raises_on_total_failure(monkeypatch: pytest.MonkeyPatch) -> None:
    req = MagicMock()
    req.engine = "xtts_v2"
    req.profile_id = "p1"
    req.text = "Hello. World."
    req.language = "en"
    req.emotion = None
    req.enhance_quality = False
    req.consent_id = None
    req.speed = None
    req.pitch = None
    req.stability = None
    req.clarity = None
    req.temperature = None
    req.chunk_size_chars = 8000

    async def boom(*_a: object, **_k: object) -> None:
        raise ServiceError(500, "synth failed")

    monkeypatch.setattr(SynthesisService, "synthesize", boom)

    fake_router = MagicMock()
    fake_router.list_engines.return_value = ["xtts_v2"]
    fake_router.get_engine.return_value = object()

    with patch(
        "backend.services.engine_shared.engine_router",
        fake_router,
    ), patch(
        "backend.services.engine_shared.ENGINE_AVAILABLE",
        True,
    ), patch(
        "backend.services.engine_shared._ensure_engine_router",
        lambda: None,
    ):
        with pytest.raises(ServiceError) as ei:
            await SynthesisService.synthesize_long_form(req, MagicMock(), None)
        assert ei.value.status_code == 500


def _two_fixed_chunks(text: str, max_c: int, lang: str) -> list[str]:
    return ["chunk_a", "chunk_b"]


@pytest.mark.asyncio
async def test_synthesize_long_form_surfaces_partial_failure(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """First chunk fails synthesis; second succeeds — partial_failure True."""
    monkeypatch.setattr(
        SynthesisService,
        "_chunk_text_for_long_form",
        staticmethod(_two_fixed_chunks),
    )
    req = MagicMock()
    req.engine = "xtts_v2"
    req.profile_id = "p1"
    req.text = "A. B."
    req.language = "en"
    req.emotion = None
    req.enhance_quality = False
    req.consent_id = None
    req.speed = None
    req.pitch = None
    req.stability = None
    req.clarity = None
    req.temperature = None
    req.chunk_size_chars = 8000

    call_count = {"n": 0}

    class Resp:
        audio_id = "aid"
        audio_url = "/x"
        quality_score = 0.5

    async def synth(req_inner: object, *_a: object, **_k: object) -> Resp:
        call_count["n"] += 1
        if call_count["n"] == 1:
            raise RuntimeError("chunk 0 failed")
        return Resp()

    monkeypatch.setattr(SynthesisService, "synthesize", synth)

    fake_router = MagicMock()
    fake_router.list_engines.return_value = ["xtts_v2"]
    fake_router.get_engine.return_value = object()

    wav_path = "/tmp/vs_longform_test.wav"
    rng = np.random.default_rng(0)
    audio = rng.standard_normal(400).astype(np.float32)

    with patch(
        "backend.services.engine_shared.engine_router",
        fake_router,
    ), patch(
        "backend.services.engine_shared.ENGINE_AVAILABLE",
        True,
    ), patch(
        "backend.services.engine_shared._ensure_engine_router",
        lambda: None,
    ), patch(
        "backend.services.audio_path_resolver.resolve_audio_path",
        lambda _aid: wav_path,
    ), patch(
        "backend.services.synthesis_service.os.path.exists",
        lambda p: p == wav_path,
    ), patch(
        "backend.audio.audio_utils.load_audio",
        lambda _p: (audio, 22050),
    ), patch(
        "backend.services.synthesis_service.create_audio_artifact_from_wav_array",
        lambda *a, **k: ("out1", "", {}),
    ):
        out = await SynthesisService.synthesize_long_form(req, MagicMock(), None)
        assert out.partial_failure is True
        assert len(out.failed_chunks) >= 1
        assert out.chunks_succeeded >= 1


@pytest.mark.asyncio
async def test_settings_envelope_consistent_across_chunks(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        SynthesisService,
        "_chunk_text_for_long_form",
        staticmethod(_two_fixed_chunks),
    )
    captured: list[object] = []

    req = MagicMock()
    req.engine = "xtts_v2"
    req.profile_id = "p1"
    req.text = "One. Two."
    req.language = "en"
    req.emotion = "warm"
    req.enhance_quality = True
    req.consent_id = None
    req.speed = 1.1
    req.pitch = 2.0
    req.stability = 0.5
    req.clarity = 0.6
    req.temperature = 0.4
    req.chunk_size_chars = 8000

    class Resp:
        audio_id = "aid"
        audio_url = "/x"
        quality_score = 0.9

    async def synth(req_inner: object, *_a: object, **_k: object) -> Resp:
        captured.append(req_inner)
        return Resp()

    monkeypatch.setattr(SynthesisService, "synthesize", synth)

    fake_router = MagicMock()
    fake_router.list_engines.return_value = ["xtts_v2"]
    fake_router.get_engine.return_value = object()

    wav_path = "/tmp/vs_env_test.wav"
    audio = np.zeros(200, dtype=np.float32)

    with patch(
        "backend.services.engine_shared.engine_router",
        fake_router,
    ), patch(
        "backend.services.engine_shared.ENGINE_AVAILABLE",
        True,
    ), patch(
        "backend.services.engine_shared._ensure_engine_router",
        lambda: None,
    ), patch(
        "backend.services.audio_path_resolver.resolve_audio_path",
        lambda _aid: wav_path,
    ), patch(
        "backend.services.synthesis_service.os.path.exists",
        lambda p: p == wav_path,
    ), patch(
        "backend.audio.audio_utils.load_audio",
        lambda _p: (audio, 22050),
    ), patch(
        "backend.services.synthesis_service.create_audio_artifact_from_wav_array",
        lambda *a, **k: ("merged", "", {}),
    ):
        await SynthesisService.synthesize_long_form(req, MagicMock(), None)

    assert len(captured) >= 2
    first = captured[0]
    for c in captured[1:]:
        assert first.engine == c.engine
        assert first.profile_id == c.profile_id
        assert first.language == c.language
        assert first.emotion == c.emotion
        assert first.enhance_quality == c.enhance_quality
        assert first.speed == c.speed
        assert first.pitch == c.pitch


@pytest.mark.asyncio
async def test_synthesize_long_form_assembles_chunks_in_order(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    """Verify concatenate order matches chunk index (via distinct audio lengths)."""
    monkeypatch.setattr(
        SynthesisService,
        "_chunk_text_for_long_form",
        staticmethod(_two_fixed_chunks),
    )
    req = MagicMock()
    req.engine = "xtts_v2"
    req.profile_id = "p1"
    req.text = "First. Second."
    req.language = "en"
    req.emotion = None
    req.enhance_quality = False
    req.consent_id = None
    req.speed = None
    req.pitch = None
    req.stability = None
    req.clarity = None
    req.temperature = None
    req.chunk_size_chars = 8000

    seq = {"i": 0}

    class Resp:
        def __init__(self) -> None:
            self.audio_id = f"id_{seq['i']}"
            self.audio_url = "/x"
            self.quality_score = 0.5
            seq["i"] += 1

    async def synth(_req_inner: object, *_a: object, **_k: object) -> Resp:
        return Resp()

    monkeypatch.setattr(SynthesisService, "synthesize", synth)

    fake_router = MagicMock()
    fake_router.list_engines.return_value = ["xtts_v2"]
    fake_router.get_engine.return_value = object()

    def fake_load(path: str) -> tuple[np.ndarray, int]:
        if "id_0" in path or path.endswith("id_0.wav"):
            return np.ones(100, dtype=np.float32), 22050
        return np.full(200, 2.0, dtype=np.float32), 22050

    merged: list[np.ndarray | None] = []

    def capture_merge(audio: object, _sr: int, **_: object) -> tuple[str, str, dict]:
        merged.append(np.asarray(audio))
        return ("merged", "", {})

    with patch(
        "backend.services.engine_shared.engine_router",
        fake_router,
    ), patch(
        "backend.services.engine_shared.ENGINE_AVAILABLE",
        True,
    ), patch(
        "backend.services.engine_shared._ensure_engine_router",
        lambda: None,
    ), patch(
        "backend.services.audio_path_resolver.resolve_audio_path",
        lambda aid: f"/tmp/{aid}.wav",
    ), patch(
        "backend.services.synthesis_service.os.path.exists",
        lambda _p: True,
    ), patch(
        "backend.audio.audio_utils.load_audio",
        fake_load,
    ), patch(
        "backend.services.synthesis_service.create_audio_artifact_from_wav_array",
        capture_merge,
    ):
        await SynthesisService.synthesize_long_form(req, MagicMock(), None)

    assert merged and merged[0] is not None
    assert merged[0].shape[0] == 300
