"""
FastAPI Dependency Injection Wrappers (Phase 2.3)

Provides type-safe dependency injection for services using FastAPI's Depends().
This improves testability by allowing services to be easily mocked in tests.

Usage:
    from backend.api.deps import EngineServiceDep, EngineConfigServiceDep

    @router.get("/engines")
    async def list_engines(engine_service: EngineServiceDep):
        return engine_service.list_engines()

    @router.get("/engines/config")
    async def get_config(config_service: EngineConfigServiceDep):
        return config_service.get_default_engine("tts")
"""

from __future__ import annotations

from typing import Annotated, Any

from fastapi import Depends


# EngineConfigService dependency
def get_engine_config_service_dep() -> Any:
    """Get the EngineConfigService singleton."""
    from backend.ml.models.engine_config_service import get_engine_config_service

    return get_engine_config_service()


_EngineConfigService: type = object
try:
    from backend.ml.models.engine_config_service import EngineConfigService

    _EngineConfigService = EngineConfigService
except ImportError:  # ALLOWED: bare except - optional dependency
    pass

EngineConfigServiceDep = Annotated[Any, Depends(get_engine_config_service_dep)]


# EngineService dependency
def get_engine_service_dep() -> Any:
    """Get the EngineService singleton."""
    from backend.ml.models.engine_service import get_engine_service

    return get_engine_service()


_IEngineService: type = object
try:
    from backend.ml.models.engine_service import IEngineService

    _IEngineService = IEngineService
except ImportError:  # ALLOWED: bare except - optional dependency
    pass

EngineServiceDep = Annotated[Any, Depends(get_engine_service_dep)]


# AudioArtifactRegistry dependency
def get_audio_registry_dep() -> Any:
    """Get the AudioArtifactRegistry singleton."""
    from backend.audio.processing.audio_artifact_registry import get_audio_registry

    return get_audio_registry()


_AudioArtifactRegistry: type = object
try:
    from backend.audio.processing.audio_artifact_registry import (
        AudioArtifactRegistry,
    )

    _AudioArtifactRegistry = AudioArtifactRegistry
except ImportError:  # ALLOWED: bare except - optional dependency
    pass

AudioRegistryDep = Annotated[Any, Depends(get_audio_registry_dep)]


# ContentAddressedAudioCache dependency
def get_audio_cache_dep() -> Any:
    """Get the ContentAddressedAudioCache singleton."""
    from backend.audio.processing.content_addressed_audio_cache import get_audio_cache

    return get_audio_cache()


_ContentAddressedAudioCache: type = object
try:
    from backend.audio.processing.content_addressed_audio_cache import (
        ContentAddressedAudioCache,
    )

    _ContentAddressedAudioCache = ContentAddressedAudioCache
except ImportError:  # ALLOWED: bare except - optional dependency
    pass

AudioCacheDep = Annotated[Any, Depends(get_audio_cache_dep)]


# JobStateStore dependency
def get_job_state_store_dep() -> Any:
    """Get the JobStateStore singleton."""
    from backend.infrastructure.adapters.job_state_store import get_job_state_store

    return get_job_state_store(namespace="default")


_JobStateStore: type = object
try:
    from backend.infrastructure.adapters.job_state_store import JobStateStore

    _JobStateStore = JobStateStore
except ImportError:  # ALLOWED: bare except - optional dependency
    pass

JobStateStoreDep = Annotated[Any, Depends(get_job_state_store_dep)]


# CircuitBreaker dependency (per-engine)
def get_circuit_breaker_dep(engine_id: str) -> Any:
    """Get circuit breaker for a specific engine."""
    from backend.core.circuit_breaker import get_engine_breaker

    return get_engine_breaker(engine_id)


# ProjectStoreService dependency
def get_project_store_service_dep() -> Any:
    """Get the ProjectStoreService singleton."""
    from backend.project.management.project_store_service import get_project_store_service

    return get_project_store_service()


_ProjectStoreService: type = object
try:
    from backend.project.management.project_store_service import ProjectStoreService

    _ProjectStoreService = ProjectStoreService
except ImportError:  # ALLOWED: bare except - optional dependency
    pass

ProjectStoreServiceDep = Annotated[Any, Depends(get_project_store_service_dep)]


# ProfileStore dependency
def get_profile_store_dep() -> Any:
    """Get the ProfileStore singleton."""
    from backend.project.management.profile_store import get_profile_store

    return get_profile_store()


_ProfileStore: type = object
try:
    from backend.project.management.profile_store import ProfileStore

    _ProfileStore = ProfileStore
except ImportError:  # ALLOWED: bare except - optional dependency
    pass

ProfileStoreDep = Annotated[Any, Depends(get_profile_store_dep)]


# TrackStore dependency
def get_track_store_dep() -> Any:
    """Get the TrackStore singleton."""
    from backend.project.tracks.track_store import get_track_store

    return get_track_store()


_TrackStore: type = object
try:
    from backend.project.tracks.track_store import TrackStore

    _TrackStore = TrackStore
except ImportError:  # ALLOWED: bare except - optional dependency
    pass

TrackStoreDep = Annotated[Any, Depends(get_track_store_dep)]


# ArtifactRefCounter dependency
def get_ref_counter_dep() -> Any:
    """Get the ArtifactRefCounter singleton."""
    from backend.infrastructure.adapters.artifact_ref_counter import get_ref_counter

    return get_ref_counter()


_ArtifactRefCounter: type = object
try:
    from backend.infrastructure.adapters.artifact_ref_counter import ArtifactRefCounter

    _ArtifactRefCounter = ArtifactRefCounter
except ImportError:  # ALLOWED: bare except - optional dependency
    pass

ArtifactRefCounterDep = Annotated[Any, Depends(get_ref_counter_dep)]


# EditHistory dependency
def get_edit_history_dep() -> Any:
    """Get a project-scoped EditHistory instance."""
    from backend.project.versioning.edit_history import EditHistory

    return EditHistory()


_EditHistory: type = object
try:
    from backend.project.versioning.edit_history import EditHistory as _EditHistoryClass

    _EditHistory = _EditHistoryClass
except ImportError:  # ALLOWED: bare except - optional dependency
    pass

EditHistoryDep = Annotated[Any, Depends(get_edit_history_dep)]


# UnifiedConfigService dependency
def get_unified_config_dep() -> Any:
    """Get the UnifiedConfigService singleton."""
    from backend.platform.config.unified_config import get_config

    return get_config()


_UnifiedConfigService: type = object
try:
    from backend.platform.config.unified_config import UnifiedConfigService

    _UnifiedConfigService = UnifiedConfigService
except ImportError:  # ALLOWED: bare except - optional dependency
    pass

UnifiedConfigDep = Annotated[Any, Depends(get_unified_config_dep)]


# Export all dependencies
__all__ = [
    "ArtifactRefCounterDep",
    "AudioCacheDep",
    "AudioRegistryDep",
    "EditHistoryDep",
    "EngineConfigServiceDep",
    "EngineServiceDep",
    "JobStateStoreDep",
    "ProfileStoreDep",
    "ProjectStoreServiceDep",
    "TrackStoreDep",
    "UnifiedConfigDep",
    "get_audio_cache_dep",
    "get_audio_registry_dep",
    "get_circuit_breaker_dep",
    "get_edit_history_dep",
    "get_engine_config_service_dep",
    "get_engine_service_dep",
    "get_job_state_store_dep",
    "get_profile_store_dep",
    "get_project_store_service_dep",
    "get_ref_counter_dep",
    "get_track_store_dep",
    "get_unified_config_dep",
]
