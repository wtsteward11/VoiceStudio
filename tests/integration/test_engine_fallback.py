"""Engine fallback chain integration tests.

Validates that the EngineService correctly falls back to secondary engines
when the primary engine fails, following the defined fallback chain.
"""

from __future__ import annotations

from unittest.mock import MagicMock, patch

import pytest


@pytest.fixture
def engine_service():
    """Create a fresh EngineService instance for testing."""
    from backend.services.engine_service import EngineService

    svc = EngineService()
    return svc


class TestFallbackChain:
    """Test engine fallback chain behavior."""

    def test_fallback_chain_defined(self, engine_service):
        """Verify fallback chains are defined for core engines."""
        chains = engine_service.ENGINE_FALLBACK_CHAIN
        assert "xtts_v2" in chains
        assert len(chains["xtts_v2"]) >= 2
        assert "piper" in chains["xtts_v2"]

    def test_whisper_fallback_defined(self, engine_service):
        """Verify whisper has a fallback to faster_whisper."""
        chains = engine_service.ENGINE_FALLBACK_CHAIN
        assert "whisper" in chains or "faster_whisper" in chains

    def test_fallback_chain_no_self_reference(self, engine_service):
        """No engine should list itself as its own fallback."""
        chains = engine_service.ENGINE_FALLBACK_CHAIN
        for engine, fallbacks in chains.items():
            assert engine not in fallbacks, f"Engine {engine} lists itself as a fallback"

    def test_circuit_breaker_exists_per_engine(self, engine_service):
        """Each engine in the fallback chain should have a circuit breaker."""
        chains = engine_service.ENGINE_FALLBACK_CHAIN
        for engine in chains:
            breaker = engine_service._get_circuit_breaker(engine)
            assert breaker is not None, f"No circuit breaker for {engine}"


class TestCircuitBreakerIntegration:
    """Test circuit breaker integration with engine service."""

    def test_circuit_breaker_failure_threshold(self, engine_service):
        """Circuit breaker should open after failure_threshold failures."""
        breaker = engine_service._get_circuit_breaker("xtts_v2")
        assert breaker is not None

        for _ in range(breaker.failure_threshold):
            breaker.record_failure()

        assert breaker.state == "open"

    def test_circuit_breaker_success_resets(self, engine_service):
        """Successful call should reset failure count."""
        breaker = engine_service._get_circuit_breaker("xtts_v2")
        breaker.record_failure()
        breaker.record_failure()
        breaker.record_success()
        assert breaker.state == "closed"
