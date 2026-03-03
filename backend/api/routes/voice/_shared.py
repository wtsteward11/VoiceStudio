"""
Voice Cloning and Synthesis Routes - Shared State and Imports

Shared imports, module-level state, and router definition for voice route modules.

WebSocket Protocol Migration (GAP-INT-002):
    This file contains WebSocket endpoints that should use the standardized
    protocol from backend.api.ws.protocol. New WebSocket messages should use:

        from backend.api.ws import create_message, create_error, MessageType

        await ws.send_json(create_message(MessageType.AUDIO_CHUNK, {...}))
        await ws.send_json(create_error("Failed", code=ErrorCode.ENGINE_ERROR))

    See backend/api/ws/protocol.py for the full protocol specification.
"""

from __future__ import annotations

import asyncio
import base64
import hashlib
import json
import logging
import os
import tempfile
import time
import uuid
from pathlib import Path
from typing import Any
from urllib.parse import urlparse

import numpy as np

# Try to import HTTP client for URL downloads
HAS_HTTPX = False
try:
    import httpx

    HAS_HTTPX = True
except ImportError:  # ALLOWED: bare except - optional httpx
    pass
from fastapi import (
    APIRouter,
    Depends,
    File,
    Form,
    HTTPException,
    Request,
    UploadFile,
    WebSocket,
    WebSocketDisconnect,
)
from fastapi.responses import FileResponse

from backend.core.circuit_breaker import get_engine_breaker
from backend.core.security.file_validation import (
    FileCategory,
    FileValidationError,
    validate_audio_file,
    validate_media_for_audio_extraction,
)
from backend.ml.models.engine_service import IEngineService, get_engine_service
from backend.ml.models.model_preflight import (
    PreflightError,
    ensure_piper,
    ensure_sovits,
    ensure_xtts,
)
from backend.platform.config.unified_config import get_config

from ...deps import EngineConfigServiceDep, EngineServiceDep
from ...exceptions import (
    EngineProcessingException,
    EngineUnavailableException,
    InvalidEngineException,
    ProfileNotFoundException,
)
from ...middleware.auth_middleware import require_auth_if_enabled
from ...security.voice_policy import enforce_voice_policy_http as _enforce_voice_policy

# WebSocket protocol for standardized messaging (GAP-CRIT-002)
from ...ws.protocol import ErrorCode, MessageType, create_complete, create_error, create_message

HAS_PITCH_TRACKER = False
try:
    from ...audio_processing import PitchTracker

    HAS_PITCH_TRACKER = True
except Exception as _pitch_import_err:
    logging.getLogger(__name__).warning(
        "Pitch tracking unavailable (audio_processing import failed): %s",
        _pitch_import_err,
    )
# Import correlation ID support for enhanced logging (Phase 3A, GAP-I08)
import contextlib

from ...dependencies import RequestContext, get_request_context
from ...middleware.correlation_id import get_correlation_id, get_span_id, get_trace_id
from ...models_additional import (
    ABTestRequest,
    ABTestResponse,
    ABTestResult,
    ArtifactRemovalRequest,
    ArtifactRemovalResponse,
    EnhancementStageResult,
    MultiPassSynthesisRequest,
    MultiPassSynthesisResponse,
    PostProcessingPipelineRequest,
    PostProcessingPipelineResponse,
    ProsodyControlRequest,
    ProsodyControlResponse,
    QualityMetrics,
    VoiceAnalyzeResponse,
    VoiceCharacteristicAnalysisRequest,
    VoiceCharacteristicAnalysisResponse,
    VoiceCharacteristicData,
    VoiceCloneResponse,
    VoiceSynthesizeRequest,
    VoiceSynthesizeResponse,
)
from ...optimization import cache_response
from ...utils.instrumentation import EventType, instrument_flow
from ...utils.quality_batch import calculate_batch_quality_score

logger = logging.getLogger(__name__)

# Quality optimization via EngineService (ADR-008 compliant)
_voice_engine_service: IEngineService | None = None
HAS_QUALITY_OPTIMIZATION = False
try:
    _voice_engine_service = get_engine_service()
    presets = _voice_engine_service.get_quality_presets()
    HAS_QUALITY_OPTIMIZATION = len(presets) >= 0  # Always true if service works
except Exception as e:
    HAS_QUALITY_OPTIMIZATION = False
    logger.warning("Quality optimization not available: %s", e)

router = APIRouter(
    prefix="/api/voice",
    tags=["voice"],
    dependencies=[
        Depends(require_auth_if_enabled),
        Depends(_enforce_voice_policy),
    ],
)

# Backward-compatible engine aliases used by the UI and some clients.
_ENGINE_ID_ALIASES: dict[str, str] = {
    "xtts": "xtts_v2",
}

_state_lock = asyncio.Lock()

# Cleanup configuration (mapping only; registry rows removed, files not deleted)
AUDIO_STORAGE_MAX_AGE_SECONDS = 7 * 24 * 3600  # 7 days
AUDIO_STORAGE_MAX_SIZE = 2000  # Maximum number of registered audio IDs to keep

# Engine router for voice synthesis (initialized lazily)
ENGINE_AVAILABLE = False
engine_router = None
quality_metrics = None
_voice_engine_service = None

# Engines that support streaming synthesis
STREAMING_ENGINES = {
    "openvoice",
    "xtts",
    "xtts_v2",
    "tacotron2",
    "piper",
    "bark",
    "tortoise",
}
