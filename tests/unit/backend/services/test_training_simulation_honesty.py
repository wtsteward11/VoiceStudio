"""Runtime honesty: simulated training must not report status identical to real completion."""

from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

project_root = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(project_root))

from backend.services import training_service as ts


@pytest.mark.asyncio
async def test_simulation_path_sets_simulation_complete_status():
    tid = "sim-honesty-unit"
    key = f"training_{tid}"
    ts._training_jobs_store[key] = {"status": "pending"}
    ts._training_logs[tid] = []

    with (
        patch.object(ts, "get_broadcaster", return_value=MagicMock(
            broadcast_training_progress=AsyncMock(),
        )),
        patch("asyncio.sleep", new_callable=AsyncMock),
    ):
        await ts._simulate_training(tid, epochs=2, batch_size=2, learning_rate=0.001)

    assert ts._training_jobs_store[key]["status"] == ts.SIMULATION_STATUS
    assert ts._training_jobs_store[key]["status"] != "completed"
    assert ts._training_jobs_store[key].get("simulation_mode") is True


@pytest.mark.asyncio
async def test_real_path_sets_completed_status(tmp_path, monkeypatch):
    """Real training path ends in status 'completed' (mocked trainer, no Coqui)."""
    tid = "real-honesty-unit"
    key = f"training_{tid}"
    ds_id = "ds_real_honesty"
    ds_key = f"dataset_{ds_id}"
    wav = tmp_path / "clip.wav"
    wav.write_bytes(b"fake-wav")

    ts._datasets_store[ds_key] = {"audio_files": [str(wav)]}
    ts._training_jobs_store[key] = {
        "status": "pending",
        "dataset_id": ds_id,
        "profile_id": "p1",
    }
    ts._training_logs[tid] = []

    class FakeTrainer:
        def __init__(self, *a, **k):
            pass

        def prepare_dataset(self, **kwargs):
            return str(tmp_path / "meta.json")

        def initialize_model(self):
            return True

        def train(self, **_kwargs):
            return {"final_loss": 0.01}

        def export_model(self, output_path=None):
            out = tmp_path / "exported"
            out.mkdir(parents=True, exist_ok=True)
            return str(out)

    import backend.training.facade as facade_mod

    monkeypatch.setattr(facade_mod, "XTTSTrainer", FakeTrainer)
    monkeypatch.setattr(
        ts,
        "get_broadcaster",
        lambda: MagicMock(broadcast_training_progress=AsyncMock()),
    )
    monkeypatch.setattr("asyncio.create_task", lambda _coro: MagicMock())

    await ts._execute_real_training(
        tid,
        ds_id,
        "p1",
        "xtts",
        epochs=1,
        batch_size=1,
        learning_rate=0.001,
        gpu=False,
    )

    assert ts._training_jobs_store[key]["status"] == "completed"
    assert ts._training_jobs_store[key].get("simulation_mode") is not True


def test_export_trained_model_rejects_simulation_status():
    tid = "sim-export-blocked"
    key = f"training_{tid}"
    ts._training_jobs_store[key] = {
        "status": ts.SIMULATION_STATUS,
        "output_path": "/tmp/fake",
        "dataset_id": "d1",
        "profile_id": "p1",
    }
    assert ts.export_trained_model(tid, profile_id="p1") is None
