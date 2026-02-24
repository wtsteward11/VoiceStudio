from pathlib import Path

from backend.services.JobStateStore import get_job_state_store


def test_job_state_store_persists_per_job(tmp_path: Path, monkeypatch):
    cache_dir = tmp_path / "cache"
    cache_dir.mkdir(parents=True, exist_ok=True)
    monkeypatch.setenv("VOICESTUDIO_CACHE_DIR", str(cache_dir))

    store = get_job_state_store("voice_cloning_wizard_test")

    job_id = "wizard-abc123"
    payload = {"job_id": job_id, "status": "pending", "progress": 0.0}
    store.upsert(job_id, payload)

    loaded = store.get(job_id)
    assert loaded is not None
    assert loaded["job_id"] == job_id

    all_jobs = store.load_all()
    assert job_id in all_jobs
