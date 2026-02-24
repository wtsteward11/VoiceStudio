"""Shared fixtures for Phase 3 plugin tests."""

from __future__ import annotations

from pathlib import Path
from typing import Any

import numpy as np
import pytest

from backend.plugins.plugin_service_testing import PluginServiceStub

# ---------------------------------------------------------------------------
# Deterministic audio fixtures
# ---------------------------------------------------------------------------


@pytest.fixture()
def deterministic_audio_24k() -> np.ndarray:
    """1 second of deterministic float32 audio at 24 kHz."""
    rng = np.random.default_rng(1234)
    return rng.uniform(-0.4, 0.4, 24000).astype(np.float32)


@pytest.fixture()
def deterministic_audio_44k() -> np.ndarray:
    """1 second of deterministic float32 audio at 44.1 kHz."""
    rng = np.random.default_rng(5678)
    return rng.uniform(-0.2, 0.2, 44100).astype(np.float32)


@pytest.fixture()
def sine_wave_24k() -> np.ndarray:
    """1 second 220 Hz sine wave at 24 kHz (float32)."""
    t = np.linspace(0, 1, 24000, endpoint=False, dtype=np.float32)
    return (0.3 * np.sin(2 * np.pi * 220 * t)).astype(np.float32)


@pytest.fixture()
def sine_wave_44k() -> np.ndarray:
    """1 second 440 Hz sine wave at 44.1 kHz (float32)."""
    t = np.linspace(0, 1, 44100, endpoint=False, dtype=np.float32)
    return (0.25 * np.sin(2 * np.pi * 440 * t)).astype(np.float32)


# ---------------------------------------------------------------------------
# Fake engine for engine-adapter tests
# ---------------------------------------------------------------------------


class FakeEngine:
    """Minimal engine stub that satisfies adapter contracts."""

    def __init__(self, *args: Any, **kwargs: Any):
        self.cleaned = False

    def initialize(self) -> bool:
        return True

    def cleanup(self) -> None:
        self.cleaned = True

    def synthesize(self, *args: Any, **kwargs: Any) -> np.ndarray:
        return np.array([0.0, 0.1, -0.1], dtype=np.float32)

    def health_check(self) -> dict[str, str]:
        return {"status": "ok"}


@pytest.fixture()
def fake_engine() -> type:
    """Return the FakeEngine class (pass as ``engine_cls`` to adapters)."""
    return FakeEngine


# ---------------------------------------------------------------------------
# Fake converter for exporter tests
# ---------------------------------------------------------------------------


class FakeConversionResult:
    """Mimics AudioConversionService result."""

    def __init__(self, success: bool = True):
        self.success = success


class FakeConverter:
    """Captures ``convert_to_format`` kwargs for assertions."""

    def __init__(self) -> None:
        self.last_call_kwargs: dict[str, Any] = {}

    async def convert_to_format(self, **kwargs: Any) -> FakeConversionResult:
        self.last_call_kwargs = kwargs
        return FakeConversionResult(True)


@pytest.fixture()
def fake_converter() -> FakeConverter:
    """Return a fresh FakeConverter instance."""
    return FakeConverter()


# ---------------------------------------------------------------------------
# Plugin service stub
# ---------------------------------------------------------------------------


@pytest.fixture()
def plugin_service_stub() -> PluginServiceStub:
    """Return a fresh PluginServiceStub."""
    return PluginServiceStub()
