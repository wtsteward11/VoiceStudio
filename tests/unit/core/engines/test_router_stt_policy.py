"""
Slice 24: STT router policy — single default, no silent substitution, id aliases.

See docs/design/VOICESTUDIO_BOUNDED_SLICE24_STT_ROUTER_FAIL_CLOSED.md.
"""

from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import MagicMock

import pytest

project_root = Path(__file__).parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

try:
    from app.core.engines.router import (
        EngineRouter,
        normalize_engine_request_id,
    )

    HAS_ROUTER = True
except ImportError:
    HAS_ROUTER = False

pytestmark = pytest.mark.skipif(not HAS_ROUTER, reason="Engine router not importable")


@pytest.fixture
def engine_router() -> EngineRouter:
    return EngineRouter(
        idle_timeout_seconds=60.0,
        memory_threshold_mb=8192.0,
        auto_cleanup_enabled=False,
    )


def test_normalize_faster_whisper_alias_to_whisper() -> None:
    assert normalize_engine_request_id("faster_whisper") == "whisper"
    assert normalize_engine_request_id("whisper") == "whisper"
    assert normalize_engine_request_id("whisper_cpp") == "whisper_cpp"


def test_stt_fallback_chain_is_single_default_only(engine_router: EngineRouter) -> None:
    engine_router._get_default_engine_id = MagicMock(return_value="whisper_cpp")  # type: ignore[method-assign]
    chain = engine_router._get_fallback_chain("stt")
    assert chain == ["whisper_cpp"]


def test_stt_load_balance_never_swaps(engine_router: EngineRouter) -> None:
    engine_router._get_engine_load_stats = MagicMock(return_value=MagicMock())  # type: ignore[method-assign]
    assert engine_router._get_lower_load_alternative("whisper_cpp", "stt") is None


def test_explicit_stt_only_attempts_resolved_id(engine_router: EngineRouter) -> None:
    calls: list[str] = []

    def fake_get(name: str):
        calls.append(name)
        return None

    engine_router.get_engine = fake_get  # type: ignore[method-assign]
    eng, attempted = engine_router.select_engine_with_fallback(
        "stt",
        explicit_engine_id="whisper_cpp",
    )
    assert eng is None
    assert attempted == ["whisper_cpp"]
    assert calls == ["whisper_cpp"]


def test_explicit_faster_whisper_resolves_to_whisper_id(engine_router: EngineRouter) -> None:
    calls: list[str] = []

    def fake_get(name: str):
        calls.append(name)
        return None

    engine_router.get_engine = fake_get  # type: ignore[method-assign]
    engine_router.select_engine_with_fallback("stt", explicit_engine_id="faster_whisper")
    assert calls == ["whisper"]
