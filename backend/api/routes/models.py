"""
Model Management Routes

CRUD operations for model storage and management.
Provides model fetching, updating, and checksum verification.
"""

from __future__ import annotations

import importlib as _il
import json
import logging
import os
import shutil
import tempfile
import uuid
import zipfile
from pathlib import Path
from typing import Any

from fastapi import APIRouter, Depends, File, HTTPException, Request, UploadFile
from fastapi.responses import FileResponse
from pydantic import BaseModel

from backend.core.security.file_validation import (
    FileValidationError,
    validate_archive_file,
)
from backend.services.model_facade import ModelStorage

from ..auth import require_auth_if_enabled
from ..models import ApiOk
from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/models", tags=["models"])

_model_storage = ModelStorage()

HAS_MODEL_CACHE = False
_model_cache: Any = None
try:
    from backend.services.model_facade import get_model_cache

    _model_cache = get_model_cache(max_models=10, max_memory_mb=4096.0)
    HAS_MODEL_CACHE = True
# ALLOWED: bare except - optional dependency, import failure acceptable
except (ImportError, AttributeError):
    pass


class ModelInfoResponse(BaseModel):
    """Model information response."""

    engine: str
    model_name: str
    model_path: str
    checksum: str
    size: int
    version: str | None = None
    downloaded_at: str | None = None
    updated_at: str | None = None
    metadata: dict | None = None


class ModelRegisterRequest(BaseModel):
    """Request to register a model."""

    engine: str
    model_name: str
    model_path: str
    version: str | None = None
    metadata: dict | None = None


class ModelVerifyResponse(BaseModel):
    """Model verification response."""

    is_valid: bool
    error_message: str | None = None
    expected_checksum: str | None = None
    current_checksum: str | None = None


class StorageStatsResponse(BaseModel):
    """Storage statistics response."""

    total_models: int
    total_size: int
    total_size_mb: float
    total_size_gb: float
    engines: dict
    base_dir: str


class CacheStatsResponse(BaseModel):
    """Model cache statistics response."""

    cache_size: int
    max_models: int
    current_memory_mb: float
    max_memory_mb: float | None
    hits: int
    misses: int
    hit_rate: float
    evictions: int
    total_loaded: int
    cached_models: list[dict]


@router.get("", response_model=list[ModelInfoResponse])
@cache_response(ttl=60)  # Cache for 60 seconds (model list doesn't change frequently)
async def list_models(engine: str | None = None):
    """List all registered models, optionally filtered by engine."""
    try:
        models = _model_storage.list_models(engine=engine)
        return [ModelInfoResponse(**m.to_dict()) for m in models]
    except Exception as e:
        logger.error(f"Failed to list models: {e}")
        raise HTTPException(status_code=500, detail=str(e))


# -------------------------------------------------------------------------
# Model Registry (Phase 8 WS1) - lifecycle, activate, rollback
# -------------------------------------------------------------------------

HAS_MODEL_REGISTRY = False
_model_registry: Any = None
try:
    _mrm = _il.import_module("backend.ml.models.model_registry")
    _model_registry = _mrm.get_model_registry_service()
    HAS_MODEL_REGISTRY = True
# ALLOWED: bare except - optional dependency, import failure acceptable
except (ImportError, AttributeError):
    pass


@router.get("/registry")
@cache_response(ttl=30)
async def list_registry_models(engine_id: str | None = None):
    """List all model artifacts from the model registry (lifecycle catalog)."""
    if not HAS_MODEL_REGISTRY or _model_registry is None:
        raise HTTPException(status_code=503, detail="Model registry not available")
    try:
        return _model_registry.list_models(engine_id=engine_id)
    except Exception as e:
        logger.error(f"Failed to list registry models: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/registry/{engine_id}")
@cache_response(ttl=30)
async def get_registry_engine_models(engine_id: str):
    """Get model state for a specific engine from the registry."""
    if not HAS_MODEL_REGISTRY or _model_registry is None:
        raise HTTPException(status_code=503, detail="Model registry not available")
    try:
        return _model_registry.get_engine_models(engine_id)
    except Exception as e:
        logger.error(f"Failed to get engine models: {e}")
        raise HTTPException(status_code=500, detail=str(e))


class ActivateModelRequest(BaseModel):
    """Request to activate a model version."""

    model_name: str
    version: str | None = None


@router.post("/registry/{engine_id}/activate")
async def activate_registry_model(
    engine_id: str,
    request: ActivateModelRequest,
    _: None = Depends(require_auth_if_enabled),
):
    """Activate a model version for an engine."""
    if not HAS_MODEL_REGISTRY or _model_registry is None:
        raise HTTPException(status_code=503, detail="Model registry not available")
    try:
        return _model_registry.activate_model(
            engine_id=engine_id,
            model_name=request.model_name,
            version=request.version,
        )
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))
    except Exception as e:
        logger.error(f"Failed to activate model: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/registry/{engine_id}/rollback")
async def rollback_registry_model(
    engine_id: str,
    _: None = Depends(require_auth_if_enabled),
):
    """Rollback engine to the previous known-good model version."""
    if not HAS_MODEL_REGISTRY or _model_registry is None:
        raise HTTPException(status_code=503, detail="Model registry not available")
    try:
        return _model_registry.rollback(engine_id)
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except Exception as e:
        logger.error(f"Failed to rollback model: {e}")
        raise HTTPException(status_code=500, detail=str(e))


# -------------------------------------------------------------------------
# Engine A/B Experiment (Phase 8 WS1)
# -------------------------------------------------------------------------


class CreateModelExperimentRequest(BaseModel):
    """Request to create an engine A/B experiment for model version testing."""

    experiment_id: str
    name: str
    engine_id: str
    description: str = ""
    variants: list[dict]  # [{id, name, model_version, weight, config}]
    target_sample_size: int = 0


@router.post("/experiment")
async def create_model_experiment(
    request: CreateModelExperimentRequest,
    _: None = Depends(require_auth_if_enabled),
):
    """
    Create an engine A/B experiment for model version testing.

    Routes a percentage of synthesis to different model versions (e.g. XTTS v2.1 vs v2.0).
    Variants should have config with engine_id and model_version.
    """
    try:
        from backend.ml.models.ab_testing import (
            ABTestingService,
            Experiment,
            ExperimentStatus,
            Variant,
        )

        ab = ABTestingService()
        if ab.get_experiment(request.experiment_id):
            raise HTTPException(
                status_code=409,
                detail=f"Experiment '{request.experiment_id}' already exists",
            )
        variants = [
            Variant(
                id=v.get("id", f"v{i}"),
                name=v.get("name", f"Variant {i}"),
                description=v.get("description", ""),
                weight=v.get("weight", 50),
                config={
                    "engine_id": request.engine_id,
                    "model_version": v.get("model_version", "1.0"),
                    **(v.get("config") or {}),
                },
            )
            for i, v in enumerate(request.variants)
        ]
        exp = Experiment(
            id=request.experiment_id,
            name=request.name,
            description=request.description,
            status=ExperimentStatus.DRAFT,
            variants=variants,
            tags=[f"engine:{request.engine_id}"],
            target_sample_size=request.target_sample_size,
        )
        ab.register_experiment(exp)
        return {
            "experiment_id": exp.id,
            "status": exp.status.value,
            "variants": [
                {"id": v.id, "name": v.name, "weight": v.weight, "config": v.config}
                for v in exp.variants
            ],
        }
    except ImportError as e:
        raise HTTPException(status_code=503, detail=f"A/B testing not available: {e}")
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to create model experiment: {e}")
        raise HTTPException(status_code=500, detail=str(e))


# -------------------------------------------------------------------------
# Model Storage (engine/model_name)
# -------------------------------------------------------------------------


@router.get("/{engine}/{model_name}", response_model=ModelInfoResponse)
@cache_response(ttl=300)  # Cache for 5 minutes (model info is relatively static)
async def get_model(engine: str, model_name: str):
    """Get information about a specific model."""
    try:
        model = _model_storage.get_model(engine, model_name)
        if not model:
            raise HTTPException(status_code=404, detail="Model not found")
        return ModelInfoResponse(**model.to_dict())
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get model: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("", response_model=ModelInfoResponse)
async def register_model(
    request: ModelRegisterRequest,
    _: None = Depends(require_auth_if_enabled),  # GAP-CRIT-004: Auth required
):
    """Register a model in the storage system."""
    try:
        model = _model_storage.register_model(
            engine=request.engine,
            model_name=request.model_name,
            model_path=request.model_path,
            version=request.version,
            metadata=request.metadata,
        )
        return ModelInfoResponse(**model.to_dict())
    except FileNotFoundError as e:
        raise HTTPException(status_code=404, detail=str(e))
    except Exception as e:
        logger.error(f"Failed to register model: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/{engine}/{model_name}/verify", response_model=ModelVerifyResponse)
async def verify_model(engine: str, model_name: str):
    """Verify a model's checksum."""
    try:
        model = _model_storage.get_model(engine, model_name)
        if not model:
            raise HTTPException(status_code=404, detail="Model not found")

        is_valid, error_message = _model_storage.verify_model(engine, model_name)

        return ModelVerifyResponse(
            is_valid=is_valid,
            error_message=error_message,
            expected_checksum=model.checksum if model else None,
            current_checksum=None,  # Could calculate if needed
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to verify model: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.put("/{engine}/{model_name}/update-checksum", response_model=ModelInfoResponse)
async def update_model_checksum(
    engine: str,
    model_name: str,
    _: None = Depends(require_auth_if_enabled),  # GAP-CRIT-004: Auth required
):
    """Update a model's checksum (e.g., after model update)."""
    try:
        model = _model_storage.update_model_checksum(engine, model_name)
        if not model:
            raise HTTPException(status_code=404, detail="Model not found")
        return ModelInfoResponse(**model.to_dict())
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to update model checksum: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.delete("/{engine}/{model_name}", response_model=ApiOk)
async def delete_model(
    engine: str,
    model_name: str,
    _: None = Depends(require_auth_if_enabled),  # GAP-CRIT-004: Auth required
):
    """Delete a model from the registry (does not delete files)."""
    try:
        deleted = _model_storage.delete_model(engine, model_name)
        if not deleted:
            raise HTTPException(status_code=404, detail="Model not found")
        return ApiOk(message="Model deleted from registry")
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to delete model: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/stats/storage", response_model=StorageStatsResponse)
@cache_response(ttl=60)  # Cache for 60 seconds (storage stats don't change frequently)
async def get_storage_stats():
    """Get storage statistics."""
    try:
        stats = _model_storage.get_storage_stats()
        return StorageStatsResponse(**stats)
    except Exception as e:
        logger.error(f"Failed to get storage stats: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/stats/cache", response_model=CacheStatsResponse)
@cache_response(ttl=10)  # Cache for 10 seconds (cache stats change more frequently)
async def get_cache_stats():
    """Get model cache statistics."""
    try:
        if not HAS_MODEL_CACHE or _model_cache is None:
            raise HTTPException(status_code=503, detail="Model cache not available")

        stats = _model_cache.get_stats()
        cached_models = _model_cache.list_cached_models()

        return CacheStatsResponse(**stats, cached_models=cached_models)
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get cache stats: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/{engine}/{model_name}/export")
async def export_model(engine: str, model_name: str, request: Request):
    """Export a model as a ZIP archive."""
    # Get request ID from middleware
    request_id = getattr(request.state, "request_id", None)

    # Instrument export flow
    from ..utils.instrumentation import EventType, instrument_flow

    with instrument_flow(
        EventType.EXPORT_START,
        EventType.EXPORT_COMPLETE,
        EventType.EXPORT_ERROR,
        request_id=request_id,
        engine=engine,
        model_name=model_name,
    ):
        try:
            model = _model_storage.get_model(engine, model_name)
            if not model:
                raise HTTPException(status_code=404, detail="Model not found")

            model_path = Path(model.model_path)
            if not model_path.exists():
                raise HTTPException(status_code=404, detail="Model files not found")

            # Create temporary ZIP file
            temp_dir = tempfile.mkdtemp()
            zip_path = Path(temp_dir) / f"{engine}_{model_name}.zip"

            try:
                with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zipf:
                    # Add model files
                    if model_path.is_file():
                        zipf.write(model_path, model_path.name)
                    elif model_path.is_dir():
                        for root, _dirs, files in os.walk(model_path):
                            for file in files:
                                file_path = Path(root) / file
                                arcname = file_path.relative_to(model_path.parent)
                                zipf.write(file_path, arcname)

                    # Add metadata
                    metadata = {
                        "engine": engine,
                        "model_name": model_name,
                        "version": model.version,
                        "checksum": model.checksum,
                        "size": model.size,
                        "metadata": model.metadata,
                    }
                    zipf.writestr("model_info.json", json.dumps(metadata, indent=2))

                # Return file response
                return FileResponse(
                    path=str(zip_path),
                    filename=f"{engine}_{model_name}.zip",
                    media_type="application/zip",
                )
            except Exception as e:
                # Cleanup on error
                if zip_path.exists():
                    zip_path.unlink()
                raise e
        except HTTPException:
            raise
        except Exception as e:
            logger.error(f"Failed to export model: {e}")
            raise HTTPException(status_code=500, detail=str(e))


@router.post("/import")
async def import_model(
    request: Request,
    file: UploadFile = File(...),
    engine: str | None = None,
    _: None = Depends(require_auth_if_enabled),  # GAP-CRIT-004: Auth required
):
    """Import a model from a ZIP archive."""
    # Get request ID from middleware
    request_id = getattr(request.state, "request_id", None) if request is not None else None

    # Instrument import flow
    from ..utils.instrumentation import EventType, instrument_flow

    with instrument_flow(
        EventType.IMPORT_START,
        EventType.IMPORT_COMPLETE,
        EventType.IMPORT_ERROR,
        request_id=request_id,
        engine=engine,
        filename=file.filename if file else None,
    ):
        try:
            # Read and validate archive file
            content = await file.read()
            try:
                validate_archive_file(content, filename=file.filename)
            except FileValidationError as e:
                raise HTTPException(
                    status_code=400,
                    detail=f"Invalid archive file: {e.message}",
                ) from e

            # Save uploaded file temporarily
            temp_dir = tempfile.mkdtemp()
            temp_file = Path(temp_dir) / (file.filename or "upload.zip")

            try:
                # Write validated file
                Path(temp_file).write_bytes(content)

                # Extract ZIP
                extract_dir = Path(temp_dir) / "extracted"
                extract_dir.mkdir()

                with zipfile.ZipFile(temp_file, "r") as zipf:
                    zipf.extractall(extract_dir)

                # Read metadata
                metadata_file = extract_dir / "model_info.json"
                if not metadata_file.exists():
                    raise HTTPException(
                        status_code=400,
                        detail="Invalid model archive: missing model_info.json",
                    )

                with open(metadata_file) as mf:
                    metadata = json.load(mf)

                # Use provided engine or metadata engine
                model_engine = engine or metadata.get("engine")
                if not model_engine:
                    raise HTTPException(status_code=400, detail="Engine not specified")

                model_name = metadata.get("model_name")
                if not model_name:
                    raise HTTPException(status_code=400, detail="Model name not found in metadata")

                # Find model files (exclude metadata file)
                model_files = [
                    f for f in extract_dir.rglob("*") if f.is_file() and f.name != "model_info.json"
                ]
                if not model_files:
                    raise HTTPException(status_code=400, detail="No model files found in archive")

                # Determine model path (use first file's parent or the file itself)
                if len(model_files) == 1:
                    model_path = model_files[0]
                else:
                    # Multiple files - use common parent
                    model_path = extract_dir

                # Move to model storage location
                storage_base = Path(_model_storage.base_dir) / model_engine / model_name
                storage_base.parent.mkdir(parents=True, exist_ok=True)

                if model_path.is_file():
                    shutil.copy2(model_path, storage_base)
                else:
                    if storage_base.exists():
                        shutil.rmtree(storage_base)
                    shutil.copytree(model_path, storage_base)

                # Register model
                registered_model = _model_storage.register_model(
                    engine=model_engine,
                    model_name=model_name,
                    model_path=str(storage_base),
                    version=metadata.get("version"),
                    metadata=metadata.get("metadata"),
                )

                return ModelInfoResponse(**registered_model.to_dict())
            finally:
                # Cleanup
                shutil.rmtree(temp_dir, ignore_errors=True)
        except HTTPException:
            raise
        except Exception as e:
            logger.error(f"Failed to import model: {e}")
            raise HTTPException(status_code=500, detail=str(e))


# -------------------------------------------------------------------------
# GAP-043: In-app model download (canonical job + verify + register)
# -------------------------------------------------------------------------


class ModelDownloadStartRequest(BaseModel):
    """Start a URL-based model download tracked as job_type=download."""

    url: str
    engine: str
    model_name: str
    version: str = "1.0"
    expected_sha256: str | None = None


class ModelDownloadStartResponse(BaseModel):
    """Response with canonical job id for progress via /api/jobs."""

    job_id: str


@router.post("/download", response_model=ModelDownloadStartResponse)
async def start_model_download(
    request: ModelDownloadStartRequest,
    _: None = Depends(require_auth_if_enabled),
):
    """Create a canonical download job and begin staging the remote artifact."""
    from backend.data.repositories.job_repository import JobType as RepoJobType
    from backend.services.canonical_job_lifecycle import create_job
    from backend.services.model_download_service import (
        find_active_download_job,
        schedule_model_download,
        validate_model_download_url,
    )

    try:
        validate_model_download_url(request.url)
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e)) from e

    existing = await find_active_download_job(
        request.engine,
        request.model_name,
        request.version,
    )
    if existing:
        raise HTTPException(
            status_code=409,
            detail={
                "code": "DOWNLOAD_ACTIVE",
                "message": "A download is already in progress for this engine/model/version",
                "job_id": existing,
            },
        )

    job_id = str(uuid.uuid4())
    metadata = {
        "url": request.url,
        "engine_id": request.engine,
        "model_name": request.model_name,
        "version": request.version,
        "expected_sha256": request.expected_sha256,
    }
    await create_job(
        job_id,
        RepoJobType.DOWNLOAD.value,
        name=f"Download {request.engine}/{request.model_name}",
        metadata=metadata,
    )
    schedule_model_download(job_id, model_storage=_model_storage)
    return ModelDownloadStartResponse(job_id=job_id)
