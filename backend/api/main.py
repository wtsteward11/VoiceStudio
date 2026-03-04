# IMPORTANT: Import Hugging Face fix FIRST to set environment variables
# before any huggingface_hub imports

from __future__ import annotations

import os
import sys
from pathlib import Path

# Ensure app package is importable when running from repo root (dev)
# Centralizes path fix once; routes use direct "from app.core.X" imports
_MAIN_ROOT = Path(__file__).resolve().parents[2]  # backend/api/main.py -> repo root
if str(_MAIN_ROOT) not in sys.path:
    sys.path.insert(0, str(_MAIN_ROOT))


def _configure_hf_endpoints() -> None:
    """Configure Hugging Face endpoints with env overrides."""
    endpoint_default = os.getenv("VOICESTUDIO_HF_ENDPOINT", "https://router.huggingface.co")
    inference_default = os.getenv("VOICESTUDIO_HF_INFERENCE_API_BASE", endpoint_default)

    endpoint = os.getenv("HF_ENDPOINT", endpoint_default)
    inference = os.getenv("HF_INFERENCE_API_BASE", inference_default)

    # Override legacy endpoints with configured defaults
    if endpoint == "https://api-inference.huggingface.co":
        endpoint = endpoint_default
    if inference == "https://api-inference.huggingface.co":
        inference = inference_default

    os.environ["HF_ENDPOINT"] = endpoint
    os.environ["HF_INFERENCE_API_BASE"] = inference


try:
    from .routes import huggingface_fix
except ImportError:
    _configure_hf_endpoints()

from backend.config.path_config import get_models_path

_configure_hf_endpoints()

# Accept Coqui CPML license for non-interactive XTTS model download.
# Without this, XTTS init raises RuntimeError when stdin is not a TTY
# (e.g. uvicorn, pytest, CI). Users who disagree can set COQUI_TOS_AGREED=0.
os.environ.setdefault("COQUI_TOS_AGREED", "1")

# Set default model/cache locations (override with env if needed)
_default_models_root = os.environ.get("VOICESTUDIO_MODELS_PATH")
if not _default_models_root:
    _default_models_root = str(get_models_path())
os.environ.setdefault("VOICESTUDIO_MODELS_PATH", _default_models_root)
os.environ.setdefault("HF_HOME", os.path.join(_default_models_root, "hf_cache"))
os.environ.setdefault("TTS_HOME", os.path.join(_default_models_root, "xtts"))
os.environ.setdefault("HUGGINGFACE_HUB_CACHE", os.path.join(os.environ["HF_HOME"], "hub"))
os.environ.setdefault("TRANSFORMERS_CACHE", os.path.join(os.environ["HF_HOME"], "transformers"))
os.environ.setdefault("HF_DATASETS_CACHE", os.path.join(os.environ["HF_HOME"], "datasets"))
os.environ.setdefault("TORCH_HOME", os.path.join(_default_models_root, "torch"))
os.environ.setdefault(
    "WHISPER_CPP_MODEL_PATH",
    os.path.join(_default_models_root, "whisper", "whisper-medium.en.gguf"),
)
try:
    os.makedirs(_default_models_root, exist_ok=True)
    os.makedirs(os.environ["HF_HOME"], exist_ok=True)
    os.makedirs(os.environ["HUGGINGFACE_HUB_CACHE"], exist_ok=True)
    os.makedirs(os.environ["TRANSFORMERS_CACHE"], exist_ok=True)
    os.makedirs(os.environ["HF_DATASETS_CACHE"], exist_ok=True)
    os.makedirs(os.environ["TORCH_HOME"], exist_ok=True)
    os.makedirs(os.path.join(_default_models_root, "whisper"), exist_ok=True)
    os.makedirs(os.path.join(_default_models_root, "piper"), exist_ok=True)
    os.makedirs(os.path.join(_default_models_root, "xtts"), exist_ok=True)
except Exception as e:
    import logging

    logging.getLogger(__name__).warning("Failed to precreate model directories: %s", e)

# ---------------------------------------------------------------------------
# Core imports
# ---------------------------------------------------------------------------
import importlib as _il
import logging
from typing import Any, cast

from fastapi import FastAPI
from fastapi.exceptions import RequestValidationError
from starlette.exceptions import HTTPException as StarletteHTTPException

app_config: Any = None
try:
    _settings_mod = _il.import_module("backend.settings")
    app_config = _settings_mod.config
except (ImportError, AttributeError):
    pass

# Try importing Prometheus for metrics
try:
    from prometheus_fastapi_instrumentator import Instrumentator

    HAS_PROMETHEUS = True
except ImportError:
    HAS_PROMETHEUS = False
    Instrumentator = None
    logging.getLogger(__name__).debug(
        "prometheus-fastapi-instrumentator not installed. "
        "Metrics will be limited."
    )

# Initialize structured JSON logging if enabled
if os.environ.get("VOICESTUDIO_JSON_LOGGING", "").lower() in ("1", "true", "yes"):
    try:
        from backend.platform.telemetry.telemetry import setup_json_logging

        setup_json_logging()
        logging.getLogger(__name__).info("JSON logging enabled via VOICESTUDIO_JSON_LOGGING")
    # ALLOWED: bare except - Optional dependency, import failure is acceptable
    except ImportError:
        pass

from .error_handling import (
    general_exception_handler,
    http_exception_handler,
    validation_exception_handler,
)
from .lifecycle import on_shutdown, on_startup
from .middleware_setup import (
    get_performance_middleware as _get_performance_middleware,
)
from .middleware_setup import (
    lazy_import_response_cache as _lazy_import_response_cache,
)
from .middleware_setup import (
    setup_middleware,
)
from .observability import register_observability_routes
from .route_registry import register_all_routes


# Backward-compatibility wrappers (used by tests/contract/conftest.py, test_observability.py)
def _register_all_routes():
    """Backward-compat wrapper: delegates to register_all_routes(app)."""
    register_all_routes(app)


# ---------------------------------------------------------------------------
# FastAPI application
# ---------------------------------------------------------------------------
app = FastAPI(
    title="VoiceStudio Quantum+ Backend API",
    description="""
    VoiceStudio Quantum+ provides a comprehensive REST API for voice cloning, audio processing, and project management.

    ## Features

    - **Voice Cloning:** Multiple engines (XTTS v2, Chatterbox TTS, Tortoise TTS, OpenVoice, RVC, and more)
    - **Audio Processing:** 17+ audio effects and processing tools
    - **Project Management:** Projects, tracks, clips, and timeline management
    - **Quality Metrics:** MOS score, similarity, naturalness, SNR, artifact detection
    - **Training:** Custom voice model training with data optimization
    - **Batch Processing:** Queue-based batch synthesis
    - **Transcription:** Whisper-based speech-to-text
    - **Real-time Updates:** WebSocket support for real-time updates

    ## Error Handling

    All errors follow a standardized format with error codes, recovery suggestions, and context.
    See the error handling documentation for details.

    ## Rate Limiting

    API endpoints are rate-limited to ensure fair usage and system stability.
    Rate limit information is provided in response headers.
    See the rate limiting documentation for details.
    """,
    version="1.1.0",
    contact={
        "name": "VoiceStudio Support",
        "url": "https://github.com/voicestudio",
    },
    license_info={
        "name": "MIT",
    },
    servers=[
        {
            "url": (
                f"{app_config.server.base_url}/api/v1"
                if app_config
                else "http://localhost:8000/api/v1"
            ),
            "description": "Development server (v1)",
        },
        {
            "url": app_config.server.base_url if app_config else "http://localhost:8000",
            "description": "Development server (legacy)",
        },
        {"url": "https://api.voicestudio.com/api/v1", "description": "Production server (v1)"},
        {"url": "https://api.voicestudio.com", "description": "Production server (legacy)"},
    ],
    swagger_ui_parameters={
        "tryItOutEnabled": True,
        "persistAuthorization": True,
        "displayRequestDuration": True,
        # Disable service worker to prevent registration errors
        "deepLinking": False,
    },
    openapi_tags=[
        {"name": "profiles", "description": "Voice profile management operations."},
        {"name": "projects", "description": "Project management operations."},
        {"name": "voice", "description": "Voice synthesis and cloning operations."},
        {"name": "effects", "description": "Audio effects and processing operations."},
        {"name": "macros", "description": "Macros and automation operations."},
        {"name": "training", "description": "Voice model training operations."},
        {
            "name": "transcribe",
            "description": "Speech-to-text transcription operations.",
        },
        {"name": "models", "description": "Model management operations."},
        {"name": "quality", "description": "Quality metrics and analysis operations."},
        {"name": "batch", "description": "Batch processing operations."},
        {
            "name": "documentation",
            "description": "API documentation and validation operations.",
        },
        {
            "name": "versioning",
            "description": "API versioning and compatibility information.",
        },
    ],
)

logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Lifecycle hooks
# ---------------------------------------------------------------------------


async def _startup_wrapper():
    await on_startup(app)


async def _shutdown_wrapper():
    await on_shutdown(app)


app.add_event_handler("startup", _startup_wrapper)
app.add_event_handler("shutdown", _shutdown_wrapper)

# ---------------------------------------------------------------------------
# Prometheus instrumentation
# ---------------------------------------------------------------------------
if HAS_PROMETHEUS and Instrumentator is not None:
    Instrumentator().instrument(app).expose(app, endpoint="/api/v1/metrics/prometheus")
    logging.getLogger(__name__).info(
        "Prometheus instrumentation activated at /api/v1/metrics/prometheus"
    )

# ---------------------------------------------------------------------------
# Custom OpenAPI schema generation (lazy)
# ---------------------------------------------------------------------------
_openapi_schema_generated = False


def custom_openapi():
    """Generate custom OpenAPI schema with enhancements (lazy)."""
    global _openapi_schema_generated

    if app.openapi_schema:
        return app.openapi_schema

    # Only generate schema on first request (not during startup)
    if not _openapi_schema_generated:
        try:
            from .documentation import enhance_openapi_schema

            openapi_schema = enhance_openapi_schema(app)
            app.openapi_schema = openapi_schema
            _openapi_schema_generated = True
            return app.openapi_schema
        except ImportError:
            # Fallback to default OpenAPI generation
            from fastapi.openapi.utils import get_openapi

            openapi_schema = get_openapi(
                title=app.title,
                version=app.version,
                description=app.description,
                routes=app.routes,
            )
            app.openapi_schema = openapi_schema
            _openapi_schema_generated = True
            return app.openapi_schema

    return app.openapi_schema


app.openapi = custom_openapi

# ---------------------------------------------------------------------------
# Assembly: middleware, routes, observability, exception handlers
# ---------------------------------------------------------------------------
setup_middleware(app)
register_all_routes(app)
register_observability_routes(app)

# Register exception handlers
# Note: VoiceStudioException is a subclass of HTTPException, so it's handled by http_exception_handler
app.add_exception_handler(RequestValidationError, cast(Any, validation_exception_handler))
app.add_exception_handler(StarletteHTTPException, cast(Any, http_exception_handler))
app.add_exception_handler(Exception, general_exception_handler)
