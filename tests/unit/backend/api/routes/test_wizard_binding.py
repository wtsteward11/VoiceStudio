"""
Wizard binding tests (GOV-VOICESTUDIO-VOICE-CLONING-INTEGRITY-01).

process_voice_cloning must bind AudioRegistry path into profile reference_audio.wav.
finalize_wizard must not invent profile_id when missing.
"""

from __future__ import annotations

import asyncio
import sys
from datetime import datetime
from pathlib import Path
from unittest.mock import AsyncMock, MagicMock

import pytest

project_root = Path(__file__).resolve().parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

pytest.importorskip("fastapi")

from backend.project.management import profile_store as profile_store_mod
from backend.services.path_service import PathService


@pytest.fixture
def isolated_profile_root(tmp_path, monkeypatch):
    root = tmp_path / "profiles"
    root.mkdir(parents=True, exist_ok=True)
    store = profile_store_mod.ProfileStore(base_dir=str(root))
    monkeypatch.setattr(profile_store_mod, "get_profile_store", lambda: store)
    monkeypatch.setattr(PathService, "get_profiles_dir", staticmethod(lambda: root))
    return root


@pytest.mark.asyncio
async def test_process_wizard_binds_reference_audio(isolated_profile_root, tmp_path, monkeypatch):
    from backend.api.routes import voice_cloning_wizard as vcw

    wav = tmp_path / "upload.wav"
    wav.write_bytes(b"wizard-upload-reference-bytes")

    monkeypatch.setattr(
        "backend.services.audio_artifacts.AudioRegistry.get_path",
        lambda _rid: str(wav),
    )

    async def fake_synth(*_a, **_kw):
        class R:
            audio_id = "synthetic_proof_id"
            audio_url = "http://127.0.0.1/test.wav"

        return R()

    monkeypatch.setattr(
        "backend.voice.services.synthesis_service.SynthesisService.synthesize",
        fake_synth,
    )
    monkeypatch.setattr(
        "backend.services.audio_analysis_service.analyze_audio_metrics",
        AsyncMock(return_value={"mos_score": 4.0}),
    )

    job_id = "wizard-bind-pytest"
    now = datetime.utcnow().isoformat()
    vcw._wizard_jobs.clear()
    vcw._wizard_jobs[job_id] = vcw.WizardJob(
        job_id=job_id,
        step=2,
        reference_audio_id="artifact-1",
        reference_audio_url="/api/voice/audio/artifact-1",
        engine="piper",
        quality_mode="fast",
        profile_name="Wizard Bound",
        profile_description="pytest",
        processing_status="pending",
        progress=0.0,
        created_at=now,
        updated_at=now,
    )

    pending: list[asyncio.Task] = []
    orig_create_task = asyncio.create_task

    def capture_task(coro):
        task = orig_create_task(coro)
        pending.append(task)
        return task

    monkeypatch.setattr(asyncio, "create_task", capture_task)

    http_request = MagicMock()
    await vcw.process_wizard(job_id, http_request, None)

    assert len(pending) == 1
    await pending[0]

    job = vcw._wizard_jobs[job_id]
    assert job.profile_id, "profile_id should be set after successful process"
    assert job.processing_status == "completed"

    ref = isolated_profile_root / job.profile_id / "reference_audio.wav"
    assert ref.is_file()
    assert ref.read_bytes() == wav.read_bytes()


@pytest.mark.asyncio
async def test_finalize_wizard_without_profile_id_raises_400():
    from fastapi import HTTPException

    from backend.api.routes import voice_cloning_wizard as vcw

    job_id = "wizard-finalize-missing-profile"
    now = datetime.utcnow().isoformat()
    vcw._wizard_jobs.clear()
    vcw._wizard_jobs[job_id] = vcw.WizardJob(
        job_id=job_id,
        step=4,
        reference_audio_id="x",
        reference_audio_url="/api/x",
        processing_status="completed",
        progress=1.0,
        profile_id=None,
        created_at=now,
        updated_at=now,
    )

    with pytest.raises(HTTPException) as exc_info:
        await vcw.finalize_wizard(
            job_id,
            vcw.WizardFinalizeRequest(job_id=job_id),
        )
    assert exc_info.value.status_code == 400
    assert "process step" in exc_info.value.detail.lower()
