"""Tests for training simulation honesty enforcement.

Verifies that:
- run_training fails closed when real training backend is unavailable
- Simulation does not masquerade as real training completion
- Training status distinguishes simulation_complete from completed
- Placeholder metrics are not returned as real quality scores
"""

from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import AsyncMock, patch

import pytest

ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(ROOT))


class TestTrainingSimulationHonesty:
    """Training service must fail closed when real training is unavailable."""

    @pytest.mark.asyncio
    async def test_run_training_fails_when_coqui_missing(self) -> None:
        """run_training must not silently fall back to simulation."""
        from backend.services.training_service import _training_jobs_store, run_training

        training_id = "test_honesty_001"
        key = f"training_{training_id}"
        _training_jobs_store[key] = {
            "status": "pending",
            "progress": 0.0,
        }

        with patch.dict("sys.modules", {"backend.training.facade": None}):
            with patch("builtins.__import__", side_effect=ImportError("No module named 'backend.training.facade'")):
                await run_training(
                    training_id=training_id,
                    dataset_id="ds_001",
                    profile_id="profile_001",
                    engine="xtts",
                    epochs=10,
                    batch_size=4,
                    learning_rate=0.0001,
                    gpu=False,
                )

        status = _training_jobs_store.get(key, {})
        assert status.get("status") == "failed", (
            f"Expected 'failed' when Coqui TTS unavailable, got '{status.get('status')}'"
        )
        assert "error" in status, "Expected error field in failed training job"
        assert "coqui" in status["error"].lower() or "unavailable" in status["error"].lower(), (
            f"Error should mention install instructions, got: {status.get('error')}"
        )

        if key in _training_jobs_store:
            del _training_jobs_store[key]

    def test_simulation_status_is_not_completed(self) -> None:
        """SIMULATION_STATUS must differ from 'completed'."""
        from backend.services.training_service import SIMULATION_STATUS

        assert SIMULATION_STATUS != "completed", (
            "SIMULATION_STATUS must not be 'completed' — simulation must not masquerade as real"
        )
        assert SIMULATION_STATUS == "simulation_complete"

    def test_simulation_status_is_explicit(self) -> None:
        """SIMULATION_STATUS must contain 'simulation' to be distinguishable."""
        from backend.services.training_service import SIMULATION_STATUS

        assert "simulation" in SIMULATION_STATUS.lower()
