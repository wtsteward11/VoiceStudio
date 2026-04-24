"""Tests for preflight_registry (Slice 29)."""

from __future__ import annotations

import sys
from pathlib import Path

import pytest

project_root = Path(__file__).parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

from backend.services import preflight_registry


def test_registry_contains_core_stt_entries() -> None:
    m = preflight_registry.get_engine_preflight_callables()
    assert "whisper" in m and "whisper_cpp" in m and "vosk" in m
    assert callable(m["vosk"])


def test_run_registered_preflight_unknown_returns_none() -> None:
    assert preflight_registry.run_registered_preflight("nonexistent_engine_xyz") is None
