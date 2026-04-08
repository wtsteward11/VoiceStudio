"""
GAP-043: Canonical model download jobs — stage, verify, then register.

Orchestrates httpx streaming download into temp storage, optional SHA-256 gate,
and ModelStorage registration. Job cannot complete until verification + register succeed.
"""

from __future__ import annotations

import asyncio
import hashlib
import json
import logging
import shutil
import tempfile
import zipfile
from pathlib import Path
from urllib.parse import urlparse

import httpx

from backend.services.canonical_job_lifecycle import (
    complete_job,
    fail_job,
    mark_job_running,
    update_job_progress,
)
from backend.core.security.file_validation import FileValidationError, validate_archive_file
from backend.data.repositories.job_repository import (
    JobStatus,
    JobType,
    get_job_repository,
)
from backend.services.model_facade import ModelStorage

logger = logging.getLogger(__name__)

CHUNK_SIZE = 65536
MIN_FREE_BYTES_MARGIN = 64 * 1024 * 1024

_storage_singleton: ModelStorage | None = None
_download_tasks: dict[str, asyncio.Task[None]] = {}


class DownloadCancelledError(Exception):
    """Raised when the canonical job is cancelled while downloading."""


def _get_model_storage(model_storage: ModelStorage | None) -> ModelStorage:
    global _storage_singleton
    if model_storage is not None:
        return model_storage
    if _storage_singleton is None:
        _storage_singleton = ModelStorage()
    return _storage_singleton


def validate_model_download_url(url: str) -> None:
    """Allow only http(s) URLs for model downloads (fail-closed)."""
    parsed = urlparse(url)
    if parsed.scheme not in ("http", "https"):
        raise ValueError(f"URL scheme not allowed: {parsed.scheme!r}")


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as f:
        while True:
            block = f.read(CHUNK_SIZE)
            if not block:
                break
            digest.update(block)
    return digest.hexdigest()


async def _poll_job_state(job_id: str) -> str | None:
    repo = get_job_repository()
    entity = await repo.get_by_id(job_id)
    return entity.status if entity else None


async def _wait_while_paused(job_id: str) -> None:
    while True:
        status = await _poll_job_state(job_id)
        if status == JobStatus.CANCELLED.value:
            raise DownloadCancelledError()
        if status != JobStatus.PAUSED.value:
            return
        await asyncio.sleep(0.25)


async def _register_from_zip_extract(
    extract_dir: Path,
    engine_override: str | None,
    model_storage: ModelStorage,
) -> tuple[str, str, str]:
    metadata_file = extract_dir / "model_info.json"
    if not metadata_file.exists():
        raise ValueError("Invalid model archive: missing model_info.json")
    with metadata_file.open(encoding="utf-8") as mf:
        metadata = json.load(mf)
    model_engine = engine_override or metadata.get("engine")
    if not model_engine:
        raise ValueError("Engine not specified")
    model_name = metadata.get("model_name")
    if not model_name:
        raise ValueError("Model name not found in metadata")
    model_files = [
        f for f in extract_dir.rglob("*") if f.is_file() and f.name != "model_info.json"
    ]
    if not model_files:
        raise ValueError("No model files found in archive")
    if len(model_files) == 1:
        model_path = model_files[0]
    else:
        model_path = extract_dir
    storage_base = Path(model_storage.base_dir) / model_engine / model_name
    storage_base.parent.mkdir(parents=True, exist_ok=True)
    if model_path.is_file():
        shutil.copy2(model_path, storage_base)
    else:
        if storage_base.exists():
            shutil.rmtree(storage_base)
        shutil.copytree(model_path, storage_base)
    registered = model_storage.register_model(
        engine=model_engine,
        model_name=model_name,
        model_path=str(storage_base),
        version=metadata.get("version"),
        metadata=metadata.get("metadata"),
    )
    reg_path = registered.to_dict().get("model_path", str(storage_base))
    return model_engine, model_name, reg_path


async def _register_single_file(
    staged: Path,
    engine_id: str,
    model_name: str,
    version: str,
    model_storage: ModelStorage,
) -> tuple[str, str, str]:
    storage_base = Path(model_storage.base_dir) / engine_id / model_name
    storage_base.parent.mkdir(parents=True, exist_ok=True)
    if storage_base.exists() and storage_base.is_dir():
        shutil.rmtree(storage_base)
    shutil.copy2(staged, storage_base)
    registered = model_storage.register_model(
        engine=engine_id,
        model_name=model_name,
        model_path=str(storage_base),
        version=version,
        metadata={"source": "url_download"},
    )
    reg_path = registered.to_dict().get("model_path", str(storage_base))
    return engine_id, model_name, reg_path


def _try_register_model_registry(
    engine_id: str,
    model_name: str,
    version: str,
    path: str,
    sha256_hex: str,
    size_bytes: int,
) -> None:
    try:
        from backend.services.model_registry import get_model_registry_service

        reg = get_model_registry_service()
        reg.register_artifact(
            engine_id=engine_id,
            model_name=model_name,
            path=path,
            version=version,
            size_bytes=size_bytes,
            sha256=sha256_hex,
            metadata={"source": "download"},
        )
    except (ImportError, OSError, ValueError, TypeError) as e:
        logger.debug("Model registry hook skipped: %s", e)


async def find_active_download_job(
    engine_id: str,
    model_name: str,
    version: str,
) -> str | None:
    """Return job_id if a download for this triple is already pending/running/paused."""
    repo = get_job_repository()
    active_statuses = {
        JobStatus.PENDING.value,
        JobStatus.RUNNING.value,
        JobStatus.PAUSED.value,
    }
    entities = await repo.find({"job_type": JobType.DOWNLOAD.value}, None)
    for entity in entities:
        if entity.status not in active_statuses:
            continue
        md = entity.get_metadata()
        if (
            md.get("engine_id") == engine_id
            and md.get("model_name") == model_name
            and md.get("version") == version
        ):
            return entity.id
    return None


async def run_model_download_job(
    job_id: str,
    model_storage: ModelStorage | None = None,
) -> None:
    repo = get_job_repository()
    storage = _get_model_storage(model_storage)
    temp_root: Path | None = None
    staged_path: Path | None = None

    try:
        entity = await repo.get_by_id(job_id)
        if not entity or entity.job_type != JobType.DOWNLOAD.value:
            logger.warning("run_model_download_job: missing or non-download job %s", job_id)
            return

        md = entity.get_metadata()
        url = md.get("url")
        engine_id = md.get("engine_id")
        model_name = md.get("model_name")
        version = md.get("version") or "1.0"
        expected_sha256 = md.get("expected_sha256")
        if not url or not engine_id or not model_name:
            await fail_job(job_id, "Invalid download job metadata")
            return

        if entity.status == JobStatus.PENDING.value:
            started = await mark_job_running(job_id)
            if not started:
                logger.warning("Could not mark download job running: %s", job_id)
                return
        elif entity.status not in (JobStatus.RUNNING.value, JobStatus.PAUSED.value):
            return

        temp_root = Path(tempfile.mkdtemp(prefix="vs_model_dl_"))
        staged_path = temp_root / "staged.bin"

        async with httpx.AsyncClient(
            follow_redirects=True,
            timeout=httpx.Timeout(120.0, connect=30.0),
        ) as client:
            async with client.stream("GET", url) as response:
                response.raise_for_status()
                cl_header = response.headers.get("content-length")
                if cl_header and cl_header.isdigit():
                    need = int(cl_header) + MIN_FREE_BYTES_MARGIN
                    free = shutil.disk_usage(str(temp_root)).free
                    if free < need:
                        await fail_job(
                            job_id,
                            f"Insufficient disk space (need ~{need} bytes, free {free})",
                        )
                        return

                total = int(cl_header) if cl_header and cl_header.isdigit() else 0
                downloaded = 0
                with staged_path.open("wb") as out:
                    async for chunk in response.aiter_bytes(chunk_size=CHUNK_SIZE):
                        await _wait_while_paused(job_id)
                        status = await _poll_job_state(job_id)
                        if status == JobStatus.CANCELLED.value:
                            raise DownloadCancelledError()
                        out.write(chunk)
                        downloaded += len(chunk)
                        if total > 0:
                            progress = min(downloaded / total, 0.99)
                            await update_job_progress(
                                job_id,
                                progress,
                                current_step="downloading",
                            )

        await update_job_progress(job_id, 0.99, current_step="verifying")

        sha_hex = _sha256_file(staged_path)
        if expected_sha256:
            exp = str(expected_sha256).strip().lower()
            if exp != sha_hex.lower():
                await fail_job(
                    job_id,
                    f"Checksum mismatch: expected {exp}, got {sha_hex}",
                )
                return

        if zipfile.is_zipfile(staged_path):
            raw = staged_path.read_bytes()
            try:
                validate_archive_file(raw, filename="model.zip")
            except FileValidationError as e:
                await fail_job(job_id, f"Invalid archive: {e.message}")
                return
            extract_dir = temp_root / "extracted"
            extract_dir.mkdir()
            with zipfile.ZipFile(staged_path, "r") as zf:
                zf.extractall(extract_dir)
            eng, mname, mpath = await _register_from_zip_extract(
                extract_dir,
                engine_override=engine_id,
                model_storage=storage,
            )
        else:
            eng, mname, mpath = await _register_single_file(
                staged_path,
                engine_id,
                model_name,
                version,
                storage,
            )

        size_bytes = Path(mpath).stat().st_size if Path(mpath).exists() else 0
        _try_register_model_registry(eng, mname, version, mpath, sha_hex, size_bytes)

        await complete_job(job_id, result_path=mpath, result_id=f"{eng}/{mname}")
        logger.info("Download job %s completed: %s/%s", job_id, eng, mname)
    except DownloadCancelledError:
        logger.info("Download job %s cancelled", job_id)
    except httpx.HTTPError as e:
        logger.error("Download HTTP error for %s: %s", job_id, e)
        await fail_job(job_id, f"Download failed: {e}")
    except ValueError as e:
        logger.error("Download validation error for %s: %s", job_id, e)
        await fail_job(job_id, str(e))
    except OSError as e:
        logger.error("Download IO error for %s: %s", job_id, e)
        await fail_job(job_id, f"IO error: {e}")
    finally:
        if temp_root and temp_root.exists():
            shutil.rmtree(temp_root, ignore_errors=True)
        _download_tasks.pop(job_id, None)


def schedule_model_download(job_id: str, model_storage: ModelStorage | None = None) -> None:
    """Run download orchestration in a background task (idempotent per job_id)."""
    existing = _download_tasks.get(job_id)
    if existing is not None and not existing.done():
        return
    task = asyncio.create_task(run_model_download_job(job_id, model_storage))
    _download_tasks[job_id] = task

    def _done(t: asyncio.Task[None]) -> None:
        exc = t.exception()
        if exc is not None and not isinstance(exc, asyncio.CancelledError):
            logger.error("Download task failed: %s", exc, exc_info=exc)

    task.add_done_callback(_done)
