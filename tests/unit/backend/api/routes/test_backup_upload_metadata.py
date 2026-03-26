"""Pass 06 slice 5: upload_backup metadata validation (D6)."""

from __future__ import annotations

import io
import json
import zipfile
from pathlib import Path

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from backend.api.routes import backup


def _zip_bytes(metadata: dict) -> bytes:
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.writestr("metadata.json", json.dumps(metadata))
    return buf.getvalue()


@pytest.fixture
def upload_backup_app(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> TestClient:
    """Isolate backup storage and suppress side effects for upload tests."""
    backups_dir = tmp_path / "backups"
    backups_dir.mkdir(parents=True, exist_ok=True)
    monkeypatch.setattr(backup, "BACKUP_DIR", backups_dir)

    def fake_get_path(name: str) -> Path:
        p = tmp_path / name
        p.mkdir(parents=True, exist_ok=True)
        return p

    monkeypatch.setattr(backup, "get_path", fake_get_path)
    monkeypatch.setattr(backup, "_cleanup_old_backups", lambda: None)
    monkeypatch.setattr(backup, "_check_disk_space", lambda _n: True)
    monkeypatch.setattr(backup, "_backups", {})

    app = FastAPI()
    app.include_router(backup.router)
    return TestClient(app)


def test_upload_accepts_legacy_metadata_when_structure_valid(upload_backup_app: TestClient) -> None:
    meta = {
        "name": "Legacy",
        "includes_profiles": True,
        "includes_projects": False,
        "includes_settings": False,
        "includes_models": False,
    }
    response = upload_backup_app.post(
        "/api/backup/upload",
        files={"file": ("backup.zip", _zip_bytes(meta), "application/zip")},
    )
    assert response.status_code == 200
    data = response.json()
    assert data["includes_profiles"] is True


def test_upload_accepts_schema_version_1(upload_backup_app: TestClient) -> None:
    meta = {
        "schema_version": 1,
        "name": "V1",
        "includes_profiles": False,
        "includes_projects": True,
        "includes_settings": False,
        "includes_models": False,
    }
    response = upload_backup_app.post(
        "/api/backup/upload",
        files={"file": ("backup.zip", _zip_bytes(meta), "application/zip")},
    )
    assert response.status_code == 200


def test_upload_rejects_missing_includes_key(upload_backup_app: TestClient) -> None:
    meta = {"includes_profiles": True}  # missing other keys
    response = upload_backup_app.post(
        "/api/backup/upload",
        files={"file": ("backup.zip", _zip_bytes(meta), "application/zip")},
    )
    assert response.status_code == 400
    assert "includes_projects" in response.json()["detail"] or "missing" in response.json()["detail"].lower()


def test_upload_rejects_non_bool_includes(upload_backup_app: TestClient) -> None:
    meta = {
        "includes_profiles": "true",
        "includes_projects": False,
        "includes_settings": False,
        "includes_models": False,
    }
    response = upload_backup_app.post(
        "/api/backup/upload",
        files={"file": ("backup.zip", _zip_bytes(meta), "application/zip")},
    )
    assert response.status_code == 400
    assert "boolean" in response.json()["detail"].lower()


def test_upload_rejects_all_includes_false(upload_backup_app: TestClient) -> None:
    meta = {
        "includes_profiles": False,
        "includes_projects": False,
        "includes_settings": False,
        "includes_models": False,
    }
    response = upload_backup_app.post(
        "/api/backup/upload",
        files={"file": ("backup.zip", _zip_bytes(meta), "application/zip")},
    )
    assert response.status_code == 400
    assert "does not declare" in response.json()["detail"].lower()


def test_upload_rejects_newer_schema_version(upload_backup_app: TestClient) -> None:
    meta = {
        "schema_version": 99,
        "includes_profiles": True,
        "includes_projects": False,
        "includes_settings": False,
        "includes_models": False,
    }
    response = upload_backup_app.post(
        "/api/backup/upload",
        files={"file": ("backup.zip", _zip_bytes(meta), "application/zip")},
    )
    assert response.status_code == 400
    assert "newer" in response.json()["detail"].lower()


def test_upload_rejects_negative_schema_version(upload_backup_app: TestClient) -> None:
    meta = {
        "schema_version": -1,
        "includes_profiles": True,
        "includes_projects": False,
        "includes_settings": False,
        "includes_models": False,
    }
    response = upload_backup_app.post(
        "/api/backup/upload",
        files={"file": ("backup.zip", _zip_bytes(meta), "application/zip")},
    )
    assert response.status_code == 400
    assert "unsupported" in response.json()["detail"].lower()


def test_upload_rejects_non_object_metadata(upload_backup_app: TestClient) -> None:
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.writestr("metadata.json", json.dumps(["not", "an", "object"]))
    response = upload_backup_app.post(
        "/api/backup/upload",
        files={"file": ("backup.zip", buf.getvalue(), "application/zip")},
    )
    assert response.status_code == 400
