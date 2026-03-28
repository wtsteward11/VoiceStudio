"""Runtime honesty: batch synthesis may write only to disk (None in-memory)."""

from __future__ import annotations

import sys
from datetime import datetime
from pathlib import Path
from unittest.mock import MagicMock

import pytest

project_root = Path(__file__).resolve().parents[5]
sys.path.insert(0, str(project_root))

from backend.api.routes import batch as batch_mod


@pytest.mark.asyncio
async def test_batch_succeeds_when_audio_is_none_but_file_exists(tmp_path, monkeypatch):
    job_id = "batch-honesty-ok"
    out = tmp_path / "out.wav"
    ref = tmp_path / "ref.wav"
    ref.write_bytes(b"ref")

    class Eng:
        def is_initialized(self):
            return True

        def initialize(self):
            pass

        def synthesize(self, **kwargs):
            op = kwargs.get("output_path")
            Path(op).write_bytes(b"RIFFfake")
            return None

    svc = MagicMock()
    svc.get_engine.return_value = Eng()

    monkeypatch.setattr(batch_mod, "ENGINE_AVAILABLE", True)
    monkeypatch.setattr(batch_mod, "HAS_WEBSOCKET", False)
    monkeypatch.setattr(batch_mod, "get_engine_service", lambda: svc)
    monkeypatch.setattr(
        "backend.services.profile_service.resolve_reference_audio_path",
        lambda _pid: ref,
    )
    monkeypatch.setattr(
        "backend.services.audio_artifacts.create_audio_artifact_from_file",
        lambda *a, **k: ("registered-artifact-id", None, {}),
    )

    job_data = {
        "id": job_id,
        "name": "n",
        "project_id": "p",
        "voice_profile_id": "vp",
        "engine_id": "e1",
        "text": "hello",
        "language": "en",
        "status": batch_mod.JobStatus.RUNNING.value,
        "progress": 0.0,
        "created": datetime.utcnow(),
        "output_path": str(out),
    }
    batch_mod._batch_jobs[job_id] = job_data

    await batch_mod._process_batch_job(job_id)

    assert batch_mod._batch_jobs[job_id]["status"] == batch_mod.JobStatus.COMPLETED.value
    assert out.is_file()


@pytest.mark.asyncio
async def test_batch_fails_when_audio_is_none_and_no_file(tmp_path, monkeypatch):
    job_id = "batch-honesty-fail"
    out = tmp_path / "missing.wav"
    ref = tmp_path / "ref.wav"
    ref.write_bytes(b"ref")

    class Eng:
        def is_initialized(self):
            return True

        def initialize(self):
            pass

        def synthesize(self, **kwargs):
            return None

    svc = MagicMock()
    svc.get_engine.return_value = Eng()

    monkeypatch.setattr(batch_mod, "ENGINE_AVAILABLE", True)
    monkeypatch.setattr(batch_mod, "HAS_WEBSOCKET", False)
    monkeypatch.setattr(batch_mod, "get_engine_service", lambda: svc)
    monkeypatch.setattr(
        "backend.services.profile_service.resolve_reference_audio_path",
        lambda _pid: ref,
    )

    job_data = {
        "id": job_id,
        "name": "n",
        "project_id": "p",
        "voice_profile_id": "vp",
        "engine_id": "e1",
        "text": "hello",
        "language": "en",
        "status": batch_mod.JobStatus.RUNNING.value,
        "progress": 0.0,
        "created": datetime.utcnow(),
        "output_path": str(out),
    }
    batch_mod._batch_jobs[job_id] = job_data

    await batch_mod._process_batch_job(job_id)

    assert batch_mod._batch_jobs[job_id]["status"] == batch_mod.JobStatus.FAILED.value
    assert "None" in (batch_mod._batch_jobs[job_id].get("error_message") or "")
