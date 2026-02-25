# IMPORTANT: Import Hugging Face fix FIRST to set environment variables
# before any huggingface_hub imports

from __future__ import annotations

import os
from datetime import datetime


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

import importlib
import importlib as _il
import logging
import os
import time
from typing import Any, cast

from fastapi import FastAPI, Request, WebSocket
from fastapi.exceptions import RequestValidationError
from fastapi.middleware.cors import CORSMiddleware
from starlette.exceptions import HTTPException as StarletteHTTPException
from starlette.middleware.base import BaseHTTPMiddleware

app_config: Any = None
try:
    _settings_mod = _il.import_module("backend.settings")
    app_config = _settings_mod.config
except (ImportError, AttributeError):
    pass

# Try importing Prometheus for metrics
try:
    from prometheus_client import Counter, Gauge, Histogram, generate_latest
    from prometheus_fastapi_instrumentator import Instrumentator

    HAS_PROMETHEUS = True
except ImportError:
    HAS_PROMETHEUS = False
    Counter = Histogram = Gauge = generate_latest = None
    Instrumentator = None
    logging.getLogger(__name__).debug(
        "prometheus-client or prometheus-fastapi-instrumentator not installed. "
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

# Lazy imports - defer heavy imports until needed
# Exception handlers are lightweight, import immediately
from .error_handling import (
    add_request_id_middleware,
    general_exception_handler,
    get_error_metrics,
    http_exception_handler,
    validation_exception_handler,
)
from .version_info import get_version_info, get_version_string
from .versioning import (
    CURRENT_VERSION,
    MIN_SUPPORTED_VERSION,
    APIVersion,
    get_version_from_request,
    get_version_headers,
)

# API versioning
API_VERSION_PREFIX = "/api/v1"
LEGACY_API_PREFIX = "/api"
API_SUNSET_DATE = "2026-06-30"

# Defer heavy imports until startup
_PerformanceMonitoringMiddleware = None
_CompressionMiddleware = None
_load_all_plugins = None
_get_response_cache = None
_response_cache_middleware = None


def _lazy_import_performance_middleware():
    """Lazy import of performance monitoring middleware."""
    global _PerformanceMonitoringMiddleware
    if _PerformanceMonitoringMiddleware is None:
        from .middleware.performance_monitoring import PerformanceMonitoringMiddleware

        _PerformanceMonitoringMiddleware = PerformanceMonitoringMiddleware
    return _PerformanceMonitoringMiddleware


def _lazy_import_compression_middleware():
    """Lazy import of compression middleware."""
    global _CompressionMiddleware
    if _CompressionMiddleware is None:
        from .optimization import CompressionMiddleware

        _CompressionMiddleware = CompressionMiddleware
    return _CompressionMiddleware


def _lazy_import_plugins():
    """Lazy import of plugins module."""
    global _load_all_plugins
    if _load_all_plugins is None:
        from .plugins import load_all_plugins

        _load_all_plugins = load_all_plugins
    return _load_all_plugins


def _lazy_import_response_cache():
    """Lazy import of response cache."""
    global _get_response_cache, _response_cache_middleware
    if _get_response_cache is None:
        from .response_cache import get_response_cache, response_cache_middleware

        _get_response_cache = get_response_cache
        _response_cache_middleware = response_cache_middleware
    return _get_response_cache, _response_cache_middleware


def _perform_startup_sanity_checks():
    """
    Perform startup sanity checks for critical dependencies and assets.

    Checks:
    - coqui-tts==0.27.2 package version
    - XTTS model assets availability

    Fail fast (raise) or warn (log) based on severity.
    """

    # Check coqui-tts version
    try:
        try:
            # Try importlib.metadata (Python 3.8+)
            from importlib.metadata import PackageNotFoundError, version

            try:
                coqui_version = version("coqui-tts")
                expected_version = "0.27.2"
                if coqui_version != expected_version:
                    logger.warning(
                        f"coqui-tts version mismatch: found {coqui_version}, "
                        f"expected {expected_version}. XTTS may not work correctly. "
                        f"Install with: pip install coqui-tts=={expected_version}"
                    )
                else:
                    logger.info(f"coqui-tts version check: OK ({coqui_version})")
            except PackageNotFoundError:
                logger.warning(
                    "coqui-tts not installed. XTTS engine will not work. "
                    "Install with: pip install coqui-tts==0.27.2"
                )
        except ImportError:
            # Fallback to pkg_resources (older Python or if importlib.metadata unavailable)
            try:
                import pkg_resources

                coqui_version = pkg_resources.get_distribution("coqui-tts").version
                expected_version = "0.27.2"
                if coqui_version != expected_version:
                    logger.warning(
                        f"coqui-tts version mismatch: found {coqui_version}, "
                        f"expected {expected_version}. XTTS may not work correctly. "
                        f"Install with: pip install coqui-tts=={expected_version}"
                    )
                else:
                    logger.info(f"coqui-tts version check: OK ({coqui_version})")
            except pkg_resources.DistributionNotFound:
                logger.warning(
                    "coqui-tts not installed. XTTS engine will not work. "
                    "Install with: pip install coqui-tts==0.27.2"
                )
    except Exception as e:
        logger.warning(f"Failed to check coqui-tts version: {e}")

    # Check XTTS model assets
    try:
        from backend.ml.models.model_preflight import ensure_xtts

        # Run preflight check (non-blocking, auto-download disabled during startup)
        result = ensure_xtts(auto_download=False)
        if not result.get("ok", False):
            logger.warning(
                f"XTTS model assets missing at {result.get('paths', [])}. "
                f"XTTS engine may not work until models are downloaded. "
                f"Run: python -m backend.scripts.ensure_engines_ready"
            )
        else:
            paths = result.get("paths", [])
            if paths:
                logger.info(f"XTTS model assets check: OK ({len(paths)} files found)")
            else:
                logger.warning(
                    "XTTS model directory exists but appears empty. "
                    "Models will be downloaded on first use."
                )
    except Exception as e:
        logger.warning(f"Failed to check XTTS model assets: {e}")


# Lazy route imports - routes will be imported during startup
_ROUTES_LOADED = False


def _perform_contract_validation():
    """
    Validate OpenAPI contract at startup.

    Checks:
    - Schema is well-formed
    - Schema has required fields
    - Compare with exported schema for drift detection
    """
    from pathlib import Path

    try:
        from .contract_validation import (
            compare_with_exported_schema,
            validate_schema_at_startup,
        )

        # Path to exported schema (used for drift detection)
        project_root = Path(__file__).parent.parent.parent
        schema_path = project_root / "docs" / "api" / "openapi.json"

        # Validate the current schema
        validate_schema_at_startup(
            app,
            export_path=None,  # Don't auto-export at startup
            fail_on_error=False,  # Log errors but don't fail startup
        )

        # Check for drift against exported schema
        if schema_path.exists():
            compare_with_exported_schema(app, schema_path)
        else:
            logger.info(
                f"No exported schema at {schema_path}. "
                "Run 'python scripts/export_openapi_schema.py' to create baseline."
            )

    except ImportError as e:
        logger.debug(f"Contract validation not available: {e}")
    except Exception as e:
        logger.warning(f"Contract validation failed: {e}")


# Load plugins and routes on application startup (lazy initialization)
# Registered below after the FastAPI `app` is created.
async def startup_event():
    """Load plugins and routes on application startup."""
    startup_start = time.time()

    # Initialize temp file manager and perform startup cleanup
    try:
        from app.core.utils.temp_file_manager import get_temp_file_manager

        temp_manager = get_temp_file_manager()
        temp_manager.cleanup_on_startup()
        logger.info("Temp file manager initialized and startup cleanup performed")
    except Exception as e:
        logger.warning(f"Failed to initialize temp file manager: {e}")

    # Initialize database and run migrations (Phase 1 - Backend-Frontend Integration)
    try:
        # Create database connection for migrations
        import aiosqlite

        from backend.data.migrations import (
            MigrationRunner,
            get_all_migrations,
        )
        from backend.data.repository_base import ConnectionConfig

        config = ConnectionConfig()
        db_path = config.sqlite_path

        # Ensure data directory exists
        from pathlib import Path

        Path(db_path).parent.mkdir(parents=True, exist_ok=True)

        async with aiosqlite.connect(db_path) as connection:
            connection.row_factory = aiosqlite.Row

            # Initialize and run migrations
            runner = MigrationRunner(connection)
            await runner.initialize()

            # Register all migrations
            for migration_class in get_all_migrations():
                runner.register_class(migration_class)

            # Run pending migrations
            results = await runner.migrate()

            if results:
                logger.info(f"Applied {len(results)} database migration(s)")
                for result in results:
                    logger.info(f"  - v{result.version}: {result.name} ({result.status.value})")
            else:
                status = runner.get_status()
                logger.info(
                    f"Database ready: {status['applied_count']} migration(s) applied, 0 pending"
                )

        # Task 2.3: Run infrastructure repository migrations and connect adapter
        from backend.infrastructure.migrations.initial_schema import (
            run_migrations as run_infra_migrations,
        )

        await run_infra_migrations()

        # Connect DatabaseAdapter for repository layer (same path as migrations)
        try:
            from backend.infrastructure.adapters.database import (
                get_database_adapter,
            )

            db = get_database_adapter(connection_string=config.connection_string)
            if not db._connected:
                await db.connect()
        except Exception as db_err:
            logger.debug("Repository layer DB connect (optional): %s", db_err)
    except Exception as e:
        logger.error(f"Failed to initialize database: {e}", exc_info=True)
        # Don't fail startup - fall back to in-memory if database unavailable

    # Initialize security services (Gap Analysis Fix - Phase 2)
    try:
        from backend.security.key_rotation import get_key_rotation_service
        from backend.security.rbac import get_rbac_service
        from backend.security.session import get_session_manager

        session_mgr = get_session_manager()
        await session_mgr.start()
        logger.info("Session manager started")

        get_rbac_service()
        logger.info("RBAC service initialized")

        key_rotation = get_key_rotation_service()
        await key_rotation.start()
        logger.info("Key rotation service started")

        logger.info("Security services initialized successfully")
    except Exception as e:
        logger.warning(f"Failed to initialize security services: {e}")

    try:
        # Initialize background task scheduler
        try:
            from app.core.tasks.scheduler import TaskPriority, get_scheduler

            scheduler = get_scheduler()
            scheduler.start()

            # Register periodic temp file cleanup task
            from app.core.utils.temp_file_manager import get_temp_file_manager

            temp_manager = get_temp_file_manager()

            def cleanup_temp_files():
                """Periodic temp file cleanup."""
                temp_manager.cleanup_old_files()
                temp_manager.cleanup_by_disk_space()

            scheduler.add_task(
                name="Temp File Cleanup",
                func=cleanup_temp_files,
                interval=temp_manager.cleanup_interval_seconds,
                priority=TaskPriority.LOW,
            )

            logger.info("Background task scheduler started")
        except Exception as e:
            logger.warning(f"Failed to initialize task scheduler: {e}")

        # Register all routes (lazy)
        _register_all_routes()

        # Startup sanity checks: verify critical dependencies and assets
        _perform_startup_sanity_checks()

        # Load all engines from manifests
        try:
            from app.core.engines.router import router as engine_router

            engine_router.load_all_engines("engines")
            engine_count = len(engine_router.list_engines())
            failed_engines = engine_router.get_failed_engines()
            failed_count = len(failed_engines)

            if failed_count == 0:
                logger.info(f"Engine status: {engine_count} loaded, 0 failed")
            else:
                logger.warning(f"Engine status: {engine_count} loaded, {failed_count} failed")
                for engine_id, error in failed_engines.items():
                    logger.warning(f"  - {engine_id}: {error}")
        except Exception as e:
            logger.warning(f"Failed to load engines from manifests: {e}")

        # GAP-B02: Validate route prefixes for conflicts
        try:
            from .route_validator import log_route_conflicts

            if log_route_conflicts(app):
                logger.warning(
                    "Route conflicts detected - some endpoints may be unreachable. "
                    "See warnings above for details."
                )
        except Exception as e:
            logger.warning(f"Failed to validate routes: {e}")

        # Validate OpenAPI contract at startup
        _perform_contract_validation()

        # Load plugins after all routes are registered (lazy import)
        load_all_plugins = _lazy_import_plugins()
        plugin_count = load_all_plugins(app)
        logger.info(f"Loaded {plugin_count} plugin(s) on startup")

        startup_time = (time.time() - startup_start) * 1000
        logger.info(f"FastAPI startup completed in {startup_time:.2f}ms")

        try:
            from .middleware.auth_middleware import AUTH_REQUIRED
            if AUTH_REQUIRED:
                logger.info("Authentication: ENABLED (VOICESTUDIO_REQUIRE_AUTH=true)")
            else:
                logger.warning(
                    "Authentication: DISABLED (local desktop mode). "
                    "Set VOICESTUDIO_REQUIRE_AUTH=true for network deployments."
                )
        # ALLOWED: bare except - auth_middleware is optional in local desktop mode
        except ImportError:
            pass
    except Exception as e:
        logger.error(f"Error during startup: {e}", exc_info=True)


# Registered below after the FastAPI `app` is created.
async def shutdown_event():
    """Graceful shutdown with engine cleanup and 30-second timeout."""
    import asyncio

    # Get shutdown timeout from configuration
    shutdown_timeout = app_config.timeouts.shutdown if app_config else 30.0
    logger.info("Initiating graceful shutdown (timeout: %ds)", shutdown_timeout)

    async def _shutdown_engines():
        """Shutdown all running engines gracefully."""
        try:
            from app.core.runtime.runtime_engine_enhanced import get_engine_lifecycle_manager

            manager = get_engine_lifecycle_manager()
            if manager:
                running_engines = manager.get_running_engines()
                if running_engines:
                    logger.info("Shutting down %d running engine(s)...", len(running_engines))
                    for engine_id in running_engines:
                        try:
                            engine_stop_timeout = (
                                app_config.timeouts.engine_stop if app_config else 10.0
                            )
                            await manager.stop_engine(engine_id, timeout=engine_stop_timeout)
                            logger.info("Engine '%s' stopped", engine_id)
                        except Exception as e:
                            logger.warning("Failed to stop engine '%s': %s", engine_id, e)
                else:
                    logger.info("No running engines to shutdown")
        except ImportError:
            logger.debug("Engine lifecycle manager not available")
        except Exception as e:
            logger.warning("Engine shutdown error: %s", e)

    async def _shutdown_job_queue():
        """Wait for in-flight jobs to complete."""
        try:
            from app.core.runtime.job_queue_enhanced import get_job_queue

            queue = get_job_queue()
            if queue:
                pending = queue.get_pending_count()
                if pending > 0:
                    logger.info("Waiting for %d pending job(s) to complete...", pending)
                    # Give jobs a chance to complete (max 10s)
                    for _ in range(20):
                        if queue.get_pending_count() == 0:
                            break
                        await asyncio.sleep(0.5)
        # ALLOWED: bare except - Optional dependency, import failure is acceptable
        except ImportError:
            pass
        except Exception as e:
            logger.warning("Job queue shutdown error: %s", e)

    async def _shutdown_temp_files():
        """Cleanup temp files."""
        try:
            from app.core.utils.temp_file_manager import get_temp_file_manager

            temp_manager = get_temp_file_manager()
            temp_manager.cleanup_on_shutdown()
            logger.info("Temp file cleanup completed")
        except Exception as e:
            logger.warning("Failed to cleanup temp files: %s", e)

    async def _shutdown_scheduler():
        """Stop background task scheduler."""
        try:
            from app.core.tasks.scheduler import get_scheduler

            scheduler = get_scheduler()
            scheduler.stop()
            logger.info("Background task scheduler stopped")
        except Exception as e:
            logger.warning("Failed to stop task scheduler: %s", e)

    async def _shutdown_database():
        """Close database connections."""
        try:
            from app.core.database.query_optimizer import close_database_connections

            await close_database_connections()
            logger.info("Database connections closed")
        # ALLOWED: bare except - Optional dependency, import failure is acceptable
        except ImportError:
            pass
        except Exception as e:
            logger.warning("Failed to close database connections: %s", e)

    async def _shutdown_security_services():
        """Stop security services (Gap Analysis Fix - Phase 2)."""
        try:
            from backend.security.key_rotation import get_key_rotation_service
            from backend.security.session import get_session_manager

            session_mgr = get_session_manager()
            await session_mgr.stop()
            logger.info("Session manager stopped")

            key_rotation = get_key_rotation_service()
            await key_rotation.stop()
            logger.info("Key rotation service stopped")
        except ImportError:
            logger.debug("Security services module not available")
        except Exception as e:
            logger.warning("Failed to stop security services: %s", e)

    async def _shutdown_lifecycle_orchestrator():
        """Run graceful shutdown orchestrator (Gap Analysis Fix - Phase 2)."""
        try:
            from backend.lifecycle.shutdown import GracefulShutdownOrchestrator

            orchestrator = GracefulShutdownOrchestrator()
            await orchestrator.shutdown()
            logger.info("Lifecycle orchestrator shutdown completed")
        except ImportError:
            logger.debug("Lifecycle orchestrator module not available")
        except Exception as e:
            logger.warning("Lifecycle orchestrator shutdown error: %s", e)

    # Run shutdown sequence with timeout
    try:
        # Phase 1: Wait for in-flight jobs
        await asyncio.wait_for(_shutdown_job_queue(), timeout=shutdown_timeout * 0.3)

        # Phase 2: Shutdown engines
        await asyncio.wait_for(_shutdown_engines(), timeout=shutdown_timeout * 0.4)

        # Phase 3: Cleanup and scheduler
        await asyncio.wait_for(
            asyncio.gather(
                _shutdown_temp_files(),
                _shutdown_scheduler(),
                _shutdown_database(),
                _shutdown_security_services(),
                return_exceptions=True,
            ),
            timeout=shutdown_timeout * 0.3,
        )

        # Phase 4: Lifecycle orchestrator (final cleanup)
        await asyncio.wait_for(_shutdown_lifecycle_orchestrator(), timeout=5)

        logger.info("Graceful shutdown completed successfully")
    except asyncio.TimeoutError:
        logger.warning("Shutdown timed out after %ds - forcing exit", shutdown_timeout)
    except Exception as e:
        logger.error("Error during shutdown: %s", e, exc_info=True)


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
    version="1.0.0",
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


# Register lifecycle hooks now that `app` exists (avoids NameError during module import).
app.add_event_handler("startup", startup_event)
app.add_event_handler("shutdown", shutdown_event)


# Lazy OpenAPI schema generation - only generate when requested
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

logger = logging.getLogger(__name__)


# Performance profiling middleware (imported at top)


# Request size limit middleware
class RequestSizeLimitMiddleware(BaseHTTPMiddleware):
    """Middleware to limit request body size."""

    def __init__(self, app, max_size_mb: float = 100.0):
        super().__init__(app)
        self.max_size_bytes = int(max_size_mb * 1024 * 1024)

    async def dispatch(self, request: Request, call_next):
        # Check Content-Length header if present
        content_length = request.headers.get("content-length")
        if content_length:
            try:
                size = int(content_length)
                if size > self.max_size_bytes:
                    logger.warning(
                        f"Request too large: {size} bytes " f"(max: {self.max_size_bytes} bytes)"
                    )
                    from fastapi import HTTPException

                    raise HTTPException(
                        status_code=413,
                        detail=(
                            f"Request body too large. "
                            f"Maximum size: "
                            f"{self.max_size_bytes / (1024*1024):.1f}MB"
                        ),
                    )
            except ValueError:
                logger.debug(
                    "Invalid content-length header '%s', letting request proceed",
                    content_length,
                )

        return await call_next(request)


# Lazy middleware initialization - middleware will be created on first use
_performance_middleware = None
_request_size_middleware = None
_rate_limit_middleware_loaded = False


def _get_performance_middleware():
    """Lazy initialization of performance middleware."""
    global _performance_middleware
    if _performance_middleware is None:
        PerformanceMonitoringMiddleware = _lazy_import_performance_middleware()
        _performance_middleware = PerformanceMonitoringMiddleware(app, enabled=True)
    return _performance_middleware


def _get_request_size_middleware():
    """Lazy initialization of request size middleware."""
    global _request_size_middleware
    if _request_size_middleware is None:
        _request_size_middleware = RequestSizeLimitMiddleware(app)
    return _request_size_middleware


# Add performance profiling middleware (lazy initialization)
@app.middleware("http")
async def performance_profiling_middleware(request: Request, call_next):
    try:
        middleware = _get_performance_middleware()
        if middleware is None:
            return await call_next(request)
        return await middleware.dispatch(request, call_next)
    except Exception as e:
        logger.warning(f"Performance middleware error: {e}")
        return await call_next(request)


# Add request size limit middleware (lazy initialization)
@app.middleware("http")
async def request_size_limit_middleware(request: Request, call_next):
    try:
        middleware = _get_request_size_middleware()
        if middleware is None:
            return await call_next(request)
        return await middleware.dispatch(request, call_next)
    except Exception as e:
        logger.warning(f"Request size middleware error: {e}")
        return await call_next(request)


# Add request ID middleware (must be first)
@app.middleware("http")
async def request_id_middleware(request: Request, call_next):
    return await add_request_id_middleware(request, call_next)


# API versioning middleware with enhanced version negotiation
@app.middleware("http")
async def api_versioning_middleware(request: Request, call_next):
    path = request.scope.get("path", "")

    # Negotiate API version from request (path, headers, or default)
    negotiated_version = get_version_from_request(request)

    # Store negotiated version in request state for endpoint access
    request.state.api_version = negotiated_version
    request.state.api_version_warnings = []  # Warnings can be added if deprecated

    if path.startswith(API_VERSION_PREFIX):
        versioned_path = path[len(API_VERSION_PREFIX) :] or "/"
        request.scope["path"] = versioned_path
        request.scope["root_path"] = API_VERSION_PREFIX
        response = await call_next(request)
    else:
        response = await call_next(request)
        if path.startswith(LEGACY_API_PREFIX):
            response.headers["Deprecation"] = "true"
            response.headers["Sunset"] = API_SUNSET_DATE
            response.headers["Link"] = (
                f'<{API_VERSION_PREFIX}{path[len(LEGACY_API_PREFIX):]}>; rel="alternate"'
            )

    # Add version headers to all responses
    version_headers = get_version_headers(negotiated_version)
    for header_name, header_value in version_headers.items():
        response.headers[header_name] = header_value

    return response


# Middleware to disable service worker registration in Swagger UI
@app.middleware("http")
async def disable_swagger_service_worker_middleware(request: Request, call_next):
    """
    Inject JavaScript to prevent service worker registration in Swagger UI.
    This fixes the 'InvalidStateError: Failed to register a ServiceWorker' error.
    """
    response = await call_next(request)

    # Only modify responses from /docs endpoint (Swagger UI)
    if request.url.path in {"/docs", f"{API_VERSION_PREFIX}/docs"} and response.status_code == 200:
        # Check if response is HTML
        content_type = response.headers.get("content-type", "")
        if "text/html" in content_type:
            # Read the response body
            body = b""
            async for chunk in response.body_iterator:
                body += chunk

            # Decode to string
            try:
                html_content = body.decode("utf-8")
            except UnicodeDecodeError:
                # If decoding fails, return original response
                import io

                from starlette.responses import StreamingResponse

                return StreamingResponse(
                    io.BytesIO(body),
                    status_code=response.status_code,
                    headers=dict(response.headers),
                    media_type=content_type,
                )

            # Inject script to disable service worker registration before closing </body> tag
            script = """
<script>
// Disable service worker registration to prevent InvalidStateError
if ('serviceWorker' in navigator) {
    // Override register method to prevent registration
    navigator.serviceWorker.register = function() {
        console.log('[Swagger UI] Service worker registration disabled to prevent errors');
        return Promise.reject(new Error('Service worker registration disabled'));
    };

    // Also unregister any existing service workers
    navigator.serviceWorker.getRegistrations().then(function(registrations) {
        for(let registration of registrations) {
            registration.unregister();
        }
    });
}
</script>
"""
            # Insert script before </body> tag
            body_tag = "</body>"
            if body_tag in html_content:
                html_content = html_content.replace(body_tag, script + body_tag, 1)
                # Create new response with modified content
                from fastapi.responses import HTMLResponse

                # Copy headers but exclude content-length since we're changing the body size
                headers = {
                    k: v for k, v in response.headers.items() if k.lower() != "content-length"
                }
                return HTMLResponse(
                    content=html_content,
                    status_code=response.status_code,
                    headers=headers,
                    media_type=content_type,
                )
            else:
                # If no body_tag found, return original response
                import io

                from starlette.responses import StreamingResponse

                return StreamingResponse(
                    io.BytesIO(body),
                    status_code=response.status_code,
                    headers=dict(response.headers),
                    media_type=content_type,
                )

    return response


# Add response caching middleware (after request ID, before rate limiting)
@app.middleware("http")
async def api_response_cache_middleware(request: Request, call_next):
    try:
        _, middleware_func = _lazy_import_response_cache()
        if middleware_func is None:
            # Fallback if import failed
            return await call_next(request)
        return await middleware_func(request, call_next)
    except Exception as e:
        logger.warning(f"Response cache middleware error: {e}")
        return await call_next(request)


# Lazy rate limiting middleware initialization
def _initialize_rate_limiting():
    """Lazy initialization of rate limiting middleware."""
    global _rate_limit_middleware_loaded
    if _rate_limit_middleware_loaded:
        return

    # Skip rate limiting in test mode
    if os.environ.get("VOICESTUDIO_TEST_MODE", "").lower() in ("1", "true", "yes"):
        logger.info("Test mode: rate limiting disabled")
        _rate_limit_middleware_loaded = True
        return

    try:
        from .rate_limiting_enhanced import RateLimitMiddleware

        app.add_middleware(
            RateLimitMiddleware,
            skip_paths=[
                "/health",
                "/api/health",
                f"{API_VERSION_PREFIX}/health",
                "/",
                "/docs",
                f"{API_VERSION_PREFIX}/docs",
                "/openapi.json",
                f"{API_VERSION_PREFIX}/openapi.json",
            ],
        )
        logger.info("Enhanced rate limiting middleware enabled")
        _rate_limit_middleware_loaded = True
    except ImportError:
        logger.warning("Enhanced rate limiting not available, using basic rate limiting")
        # Fallback to basic rate limiting
        from .rate_limiting import rate_limit_middleware

        @app.middleware("http")
        async def basic_rate_limit_middleware(request: Request, call_next):
            return await rate_limit_middleware(request, call_next)

        _rate_limit_middleware_loaded = True


# Add CORS middleware (essential, load immediately)
# Configure CORS with security best practices
# CORS Configuration
# Security: Restrict origins. In production, set CORS_ALLOWED_ORIGINS explicitly.
_cors_env = app_config.cors.allowed_origins if app_config else None
if _cors_env:
    allowed_origins = [origin.strip() for origin in _cors_env.split(",")]
elif app_config and app_config.cors.environment == "production":
    # Production without explicit origins: restrictive default
    allowed_origins = ["http://localhost:8001"]
    logger.warning("CORS_ALLOWED_ORIGINS not set in production; using restrictive default")
else:
    # Development: allow common local origins
    allowed_origins = [
        "http://localhost:8000",
        "http://localhost:8001",
        "http://127.0.0.1:8000",
        "http://127.0.0.1:8001",
    ]

app.add_middleware(
    CORSMiddleware,
    allow_origins=allowed_origins,
    allow_credentials=True,
    allow_methods=["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS"],
    allow_headers=["*", "X-Correlation-ID", "X-API-Version"],
    expose_headers=[
        "X-Request-ID",
        "X-Correlation-ID",
        "X-RateLimit-Remaining",
        "X-RateLimit-Limit",
        "X-RateLimit-Reset",
        # API versioning headers
        "X-API-Version",
        "X-Min-Version",
        "X-Deprecated",
        "X-Sunset-Date",
        "X-API-Version-Warnings",
    ],
    max_age=3600,  # Cache preflight requests for 1 hour
)

# Initialize correlation ID middleware for request tracing
try:
    from backend.api.middleware.correlation_id import (
        CorrelationIdMiddleware,
        setup_correlation_logging,
    )

    app.add_middleware(CorrelationIdMiddleware)
    setup_correlation_logging()
    logger.info("Correlation ID middleware initialized")
except ImportError as e:
    logger.warning(f"Correlation ID middleware not available: {e}")

# Initialize middleware before app starts (must be done before startup_event)
# Initialize validation optimization middleware
try:
    from .validation_middleware import setup_validation_optimization

    setup_validation_optimization(app)
    logger.info("Validation optimization initialized")
except Exception as e:
    logger.warning(f"Failed to initialize validation optimization: {e}")

# Initialize rate limiting middleware
_initialize_rate_limiting()

# Initialize telemetry middleware if enabled
if os.environ.get("VOICESTUDIO_TELEMETRY", "").lower() in ("1", "true", "yes"):
    try:
        from backend.api.middleware.telemetry_middleware import TelemetryMiddleware

        app.add_middleware(TelemetryMiddleware, enabled=True)
        logger.info("Telemetry middleware initialized")
    except ImportError as e:
        logger.debug(f"Telemetry middleware not available: {e}")

# Add input validation middleware for security
try:
    from backend.api.middleware.input_validation import InputValidationMiddleware

    app.add_middleware(
        InputValidationMiddleware,
        enabled=True,
        strict_mode=False,  # Enable strict_mode for SQL injection checks
        skip_paths=["/health", "/api/health", "/docs", "/openapi.json", "/redoc"],
    )
    logger.info("Input validation middleware initialized")
except ImportError as e:
    logger.debug(f"Input validation middleware not available: {e}")

# Add deprecation headers middleware
try:
    from backend.api.middleware.deprecation import DeprecationMiddleware

    app.add_middleware(DeprecationMiddleware, log_deprecation_warnings=True)
    logger.info("Deprecation middleware initialized")
except ImportError as e:
    logger.debug(f"Deprecation middleware not available: {e}")

# Add compression middleware for large responses (lazy initialization)
_compression_middleware_loaded = False


def _initialize_compression_middleware():
    """Lazy initialization of compression middleware."""
    global _compression_middleware_loaded
    if not _compression_middleware_loaded:
        CompressionMiddleware = _lazy_import_compression_middleware()
        app.add_middleware(CompressionMiddleware, min_size=1024)
        _compression_middleware_loaded = True


# Register exception handlers
# Note: VoiceStudioException is a subclass of HTTPException, so it's handled by http_exception_handler
app.add_exception_handler(RequestValidationError, cast(Any, validation_exception_handler))
app.add_exception_handler(StarletteHTTPException, cast(Any, http_exception_handler))
app.add_exception_handler(Exception, general_exception_handler)


# Lazy route registration function
def _register_all_routes():
    """Register all routes lazily during startup."""
    global _ROUTES_LOADED
    if _ROUTES_LOADED:
        return

    logger.info("Loading routes (lazy initialization)...")
    start_time = time.time()

    route_module_names = [
        "advanced_settings",
        "ai_production_assistant",
        "analytics",
        "api_key_manager",
        "articulation",
        "assistant",
        "assistant_run",
        "audio",
        "audio_analysis",
        "audio_audit",
        "auth",
        "automation",
        "backup",
        "batch",
        "dataset",
        "face_swap",
        "dubbing",
        "effects",
        "embedding_explorer",
        "emotion",
        "engine",
        "engine_audit",
        "engines",
        "ensemble",
        "eval_abx",
        "formant",
        "gpu_status",
        "granular",
        "health",
        "help",
        "image_gen",
        "image_search",
        "img_sampler",
        "jobs",
        "lexicon",
        "library",
        "macros",
        "markers",
        "mixer",
        "ml_optimization",
        "model_inspect",
        "models",
        "monitoring",
        "multi_voice_generator",
        "nr",
        "pdf",
        "plugins",
        "presets",
        "profiles",
        "projects",
        "prosody",
        "quality",
        "quality_pipelines",
        "realtime_converter",
        "realtime_visualizer",
        "recording",
        "repair",
        "rvc",
        "safety",
        "scenes",
        "settings",
        "shortcuts",
        "sonography",
        "spatial_audio",
        "spectral",
        "ssml",
        "style_transfer",
        "tags",
        "telemetry",
        "templates",
        "text_speech_editor",
        "tracks",
        "training",
        "training_audit",
        "transcribe",
        "upscaling",
        "video_edit",
        "version",
        "video_gen",
        "voice",
        "voice_browser",
        "voice_cloning_wizard",
        "voice_morph",
        "voice_speech",
        "waveform",
        "workflows",
        # Previously unregistered routes
        "advanced_spectrogram",
        "ai_production_assistant",
        "assistant",
        "assistant_run",
        "dataset_editor",
        "diagnostics",
        "drift",
        "emotion_style",
        "errors",
        "metrics",
        "mix_assistant",
        "multilingual",
        "search",
        "slo",
        "spectrogram",
        "tracing",
        # Phase 7-9 routes (Gap Analysis Fix)
        "feedback",
        "instant_cloning",
        "voice_effects",
        "translation",
        "multi_speaker_dubbing",
        "lip_sync",
        "ai_enhancement",
        "integrations",
        # Phase 15-25 routes (Architecture Gap Remediation)
        "pipeline",
        # Comprehensive Gap Remediation (2026-02-10)
        "realtime_settings",
    ]

    route_modules = {}
    module_base = __package__ or "backend.api"
    critical_routes = {"voice"}
    for module_name in route_module_names:
        try:
            module_path = f"{module_base}.routes.{module_name}"
            route_modules[module_name] = importlib.import_module(module_path)
        except Exception as e:
            logger.error(
                f"Unable to import route '{module_name}': {e}",
                exc_info=True,
            )
            if module_name in critical_routes:
                raise

    def _include_route(module_key: str):
        module = route_modules.get(module_key)
        if module is None:
            return
        router = getattr(module, "router", None)
        if router is None:
            logger.error(f"Route module '{module_key}' missing router")
            return
        app.include_router(router)

    # Authentication routes (must be early for dependency injection)
    _include_route("auth")

    # Core routes (from skeleton)
    _include_route("advanced_settings")
    _include_route("lexicon")
    _include_route("spatial_audio")
    _include_route("style_transfer")
    _include_route("embedding_explorer")
    _include_route("voice")
    _include_route("voice_browser")
    _include_route("voice_speech")
    _include_route("quality")
    _include_route("quality_pipelines")

    if not any(getattr(r, "path", None) == "/api/voice/clone" for r in app.routes):
        raise RuntimeError("Voice routes not registered. Inspect backend.api.routes.voice import.")

    # Management routes
    _include_route("profiles")
    _include_route("projects")
    _include_route("tracks")
    _include_route("audio")
    _include_route("audio_audit")
    _include_route("macros")
    _include_route("workflows")
    _include_route("models")
    _include_route("effects")
    _include_route("batch")
    _include_route("transcribe")
    _include_route("training")
    _include_route("training_audit")
    _include_route("mixer")
    _include_route("ml_optimization")
    _include_route("health")
    _include_route("version")
    _include_route("monitoring")
    _include_route("tracing")
    _include_route("slo")
    _include_route("diagnostics")
    _include_route("drift")
    _include_route("errors")

    # Additional routes
    _include_route("eval_abx")
    _include_route("dataset")
    _include_route("engine")
    _include_route("engines")
    _include_route("engine_audit")
    _include_route("prosody")
    _include_route("emotion")
    _include_route("formant")
    _include_route("spectral")
    _include_route("model_inspect")
    _include_route("granular")
    _include_route("gpu_status")
    _include_route("rvc")
    _include_route("dubbing")
    _include_route("articulation")
    _include_route("nr")
    _include_route("repair")
    _include_route("safety")
    _include_route("img_sampler")
    _include_route("assistant_run")
    _include_route("ai_production_assistant")
    _include_route("image_gen")
    _include_route("image_search")
    _include_route("upscaling")
    _include_route("face_swap")
    _include_route("pdf")
    _include_route("voice_cloning_wizard")
    _include_route("multi_voice_generator")
    _include_route("video_gen")
    _include_route("video_edit")
    _include_route("settings")
    _include_route("recording")
    _include_route("library")
    _include_route("presets")
    _include_route("help")
    _include_route("shortcuts")
    _include_route("tags")
    _include_route("backup")
    _include_route("jobs")
    _include_route("templates")
    _include_route("automation")
    _include_route("scenes")
    _include_route("markers")
    _include_route("audio_analysis")
    _include_route("ensemble")
    _include_route("ssml")
    _include_route("realtime_converter")
    _include_route("sonography")
    _include_route("realtime_visualizer")
    _include_route("text_speech_editor")
    _include_route("assistant")
    _include_route("api_key_manager")
    _include_route("plugins")
    _include_route("analytics")
    _include_route("experiments")

    # Previously missing route inclusions
    _include_route("search")
    _include_route("voice_morph")
    _include_route("waveform")
    _include_route("spectrogram")
    _include_route("dataset_editor")
    _include_route("emotion_style")
    _include_route("multilingual")
    _include_route("mix_assistant")
    _include_route("advanced_spectrogram")
    _include_route("metrics")

    # Gap Remediation Phase 7-9 routes (previously imported but not registered)
    _include_route("feedback")
    _include_route("instant_cloning")
    _include_route("voice_effects")
    _include_route("translation")
    _include_route("multi_speaker_dubbing")
    _include_route("lip_sync")
    _include_route("ai_enhancement")
    _include_route("integrations")
    _include_route("pipeline")
    _include_route("realtime_settings")

    # Register additional sub-routers for UI compatibility
    try:
        from .routes.markers import project_markers_router

        app.include_router(project_markers_router)
        logger.debug("Registered project_markers_router")
    except Exception as e:
        logger.warning(f"Failed to register project_markers_router: {e}")

    try:
        from .routes.effects import project_effects_router

        app.include_router(project_effects_router)
        logger.debug("Registered project_effects_router")
    except Exception as e:
        logger.warning(f"Failed to register project_effects_router: {e}")

    # Register face-swap backward-compat alias (Arch Review 1.4)
    try:
        from .routes.face_swap import deepfake_alias_router

        app.include_router(deepfake_alias_router)
        logger.debug("Registered deepfake-creator alias router")
    except Exception as e:
        logger.warning("Failed to register deepfake alias router: %s", e)

    # Register Plugin Gallery routes (D.1 Enhancement)
    try:
        from .routes.plugin_gallery import router as plugin_gallery_router

        app.include_router(plugin_gallery_router)
        logger.debug("Registered plugin_gallery_router")
    except Exception as e:
        logger.warning(f"Failed to register plugin_gallery_router: {e}")

    # Register Plugin Health routes (must be before plugins catch-all)
    try:
        from .routes.plugin_health import router as plugin_health_router

        app.include_router(plugin_health_router)
        logger.debug("Registered plugin_health_router")
    except Exception as e:
        logger.warning(f"Failed to register plugin_health_router: {e}")

    # Register Plugin routes (catch-all /{plugin_id} — must be AFTER health)
    try:
        from .routes.plugins import router as plugins_router

        app.include_router(plugins_router)
        logger.debug("Registered plugins_router")
    except Exception as e:
        logger.warning(f"Failed to register plugins_router: {e}")

    # Register Marketplace routes (Phase 7 Sprint 1)
    try:
        from .routes.marketplace import router as marketplace_router

        app.include_router(marketplace_router)
        logger.debug("Registered marketplace_router")
    except Exception as e:
        logger.warning(f"Failed to register marketplace_router: {e}")

    # Register Video Enhancement routes (D.2 Enhancement)
    try:
        from .routes.video_enhance import router as video_enhance_router

        app.include_router(video_enhance_router)
        logger.debug("Registered video_enhance_router")
    except Exception as e:
        logger.warning(f"Failed to register video_enhance_router: {e}")

    # Register API v2 routes
    try:
        from .routes.v2 import health_router as v2_health_router

        app.include_router(v2_health_router)
        logger.debug("Registered v2 health router")
    except Exception as e:
        logger.warning(f"Failed to register v2 routes: {e}")

    # Register API v3 routes (StandardResponse envelope format)
    try:
        from .v3 import router as v3_router

        app.include_router(v3_router, prefix="/api")
        logger.debug("Registered v3 router with StandardResponse envelope")
    except Exception as e:
        logger.warning(f"Failed to register v3 routes: {e}")

    # Timeline routes (GAP-API-001)
    try:
        from .routes.timeline import router as timeline_router

        app.include_router(timeline_router)
        logger.debug("Registered timeline router")
    except Exception as e:
        logger.warning(f"Failed to register timeline routes: {e}")

    # Gateway alias routes (GAP-CRIT-001: Endpoint alignment for frontend gateways)
    try:
        from .routes.gateway_aliases import timeline_alias_router, voice_alias_router

        app.include_router(voice_alias_router)
        app.include_router(timeline_alias_router)
        logger.debug("Registered gateway alias routers (VoiceGateway, TimelineGateway)")
    except Exception as e:
        logger.warning(f"Failed to register gateway alias routes: {e}")

    load_time = (time.time() - start_time) * 1000
    logger.info(f"Routes loaded in {load_time:.2f}ms")
    _ROUTES_LOADED = True
    logger.debug(f"Total routes registered: {len(app.routes)}")


@app.websocket("/ws/events")
async def ws_events(ws: WebSocket):
    """Legacy WebSocket endpoint (heartbeat only)."""
    from .ws import events

    await events.stream(ws)


@app.websocket("/ws/realtime")
async def ws_realtime(ws: WebSocket, topics: str | None = None):
    """
    Enhanced WebSocket endpoint for real-time updates.

    Query parameters:
    - topics: Comma-separated list of topics (meters, training, batch, general)
    """
    from .ws import realtime

    topic_list = topics.split(",") if topics else None
    await realtime.connect(ws, topic_list)


@app.websocket("/ws/plugins")
async def ws_plugins(ws: WebSocket):
    """
    WebSocket endpoint for plugin state synchronization.

    Phase 1 Plugin Architecture: Real-time sync between backend and frontend.

    Protocol:
    - On connect: Server sends full sync automatically
    - Client can send:
      - {"type": "sync_request"}: Request full sync
      - {"type": "plugin_command", "command": "...", "plugin_id": "..."}: Execute command
      - {"type": "ping"}: Heartbeat
    - Server sends:
      - {"type": "plugin_sync", "action": "..."}: State updates
      - {"type": "plugin_command_response", ...}: Command results
    """
    from .ws import plugins

    await plugins.plugin_websocket_handler(ws)


@app.get("/")
def root():
    """Root endpoint with version information."""
    version_info = get_version_info()
    return {
        "message": "VoiceStudio Backend API",
        "version": version_info["version"],
        "version_string": get_version_string(),
        "build_date": version_info.get("build_date"),
        "git_commit": version_info.get("git_commit"),
    }


@app.get("/api/version", tags=["versioning"])
def api_version_info(request: Request):
    """
    Get API version information including negotiated version and compatibility.

    Returns:
        - current_version: The current API version
        - min_supported_version: Minimum supported API version
        - negotiated_version: The version negotiated for this request
        - supported_versions: List of all supported versions
        - version_info: Application version details
    """
    version_info = get_version_info()
    negotiated = getattr(request.state, "api_version", CURRENT_VERSION)
    warnings = getattr(request.state, "api_version_warnings", [])

    return {
        "current_version": CURRENT_VERSION.value,
        "min_supported_version": MIN_SUPPORTED_VERSION.value,
        "negotiated_version": negotiated.value,
        "supported_versions": [v.value for v in APIVersion],
        "version_info": {
            "version": version_info["version"],
            "version_string": get_version_string(),
            "build_date": version_info.get("build_date"),
            "git_commit": version_info.get("git_commit"),
        },
        "warnings": warnings,
    }


@app.get("/health")
def health():
    """Basic health check endpoint."""
    from backend.settings import config

    return {
        "status": "ok",
        "version": "1.0",
        "portable_mode": config.portable_mode,
    }


@app.get("/api/health")
def api_health():
    """API health check endpoint with performance metrics."""
    import os

    try:
        # Get system metrics
        import psutil

        process = psutil.Process(os.getpid())
        memory_info = process.memory_info()
        cpu_percent = process.cpu_percent(interval=0.1)

        middleware = _get_performance_middleware()
        version_info = get_version_info()
        return {
            "status": "ok",
            "version": version_info["version"],
            "version_string": get_version_string(),
            "version_info": version_info,
            "metrics": {
                "memory_mb": memory_info.rss / (1024 * 1024),
                "cpu_percent": cpu_percent,
                "request_count": getattr(middleware, "_request_count", 0),
                "slow_request_count": getattr(middleware, "_slow_request_count", 0),
            },
        }
    except Exception as e:
        logger.warning(f"Failed to get health metrics: {e}")
        version_info = get_version_info()
        return {
            "status": "ok",
            "version": version_info["version"],
            "version_string": get_version_string(),
            "metrics": "unavailable",
        }


@app.get("/api/metrics")
def api_metrics():
    """Minimum observability metrics for API, engines, and errors."""
    payload = {
        "timestamp": datetime.utcnow().isoformat(),
        "errors": get_error_metrics(),
        "endpoints": {"enabled": False},
        "engines": {"enabled": False},
    }

    try:
        middleware = _get_performance_middleware()
        if middleware is not None:
            payload["endpoints"] = {
                "stats": middleware.get_stats(),
                "by_endpoint": middleware.get_metrics(),
            }
    except Exception as e:
        payload["endpoints"] = {"enabled": False, "error": str(e)}

    try:
        from app.core.engines.performance_metrics import get_engine_metrics

        metrics = get_engine_metrics()
        payload["engines"] = {
            "summary": metrics.get_summary(),
            "total_engines": len(metrics.get_all_stats()),
        }
    except Exception as e:
        payload["engines"] = {"enabled": False, "error": str(e)}

    return payload


@app.get("/api/metrics/history")
def api_metrics_history(window: str = "24h"):
    """
    Phase 8 WS4: Get metrics history for trend charts.

    Query param: window (e.g. 24h, 48h, 168h for 1 week). Default 24h.
    """
    try:
        from backend.platform.monitoring.metrics_history import get_metrics_history

        hours = 24
        if window.endswith("h"):
            try:
                hours = int(window[:-1])
            except ValueError:
                logger.debug("Invalid window format %s (expected Nh), using default", window)
        elif window.endswith("d"):
            try:
                hours = int(window[:-1]) * 24
            except ValueError:
                logger.debug("Invalid window format %s (expected Nd), using default", window)
        history = get_metrics_history(window_hours=min(hours, 720))
        return {
            "window": window,
            "window_hours": hours,
            "count": len(history),
            "snapshots": history,
        }
    except ImportError as e:
        return {"error": f"Metrics history not available: {e}", "snapshots": []}
    except Exception as e:
        logger.warning(f"Failed to get metrics history: {e}")
        return {"error": str(e), "snapshots": []}


@app.get("/api/cache/stats")
async def cache_stats():
    """Get response cache statistics."""
    try:
        get_response_cache, _ = _lazy_import_response_cache()
        if get_response_cache is None:
            return {"error": "Response cache not initialized"}
        cache = get_response_cache()
        if cache is None:
            return {"error": "Response cache instance not available"}
        return cache.get_stats()
    except Exception as e:
        logger.warning(f"Failed to get cache stats: {e}")
        return {"error": str(e)}


@app.post("/api/cache/clear")
async def clear_cache():
    """Clear all response cache entries."""
    try:
        get_response_cache, _ = _lazy_import_response_cache()
        if get_response_cache is None:
            return {"error": "Response cache not initialized"}
        cache = get_response_cache()
        if cache is None:
            return {"error": "Response cache instance not available"}
        count = len(cache._cache)
        cache.clear()
        return {"message": "Cache cleared", "entries_cleared": count}
    except Exception as e:
        logger.warning(f"Failed to clear cache: {e}")
        return {"error": str(e)}


@app.get("/api/profiler/stats")
def profiler_stats():
    """Get performance profiler statistics."""
    try:
        from app.core.monitoring.profiler import get_profiler

        profiler = get_profiler()
        return profiler.get_stats()
    except Exception as e:
        logger.warning(f"Failed to get profiler stats: {e}")
        return {"error": str(e)}


@app.get("/api/profiler/detailed")
def profiler_detailed():
    """Get detailed performance profiler statistics."""
    try:
        from app.core.monitoring.profiler import get_profiler

        profiler = get_profiler()
        return profiler.get_detailed_stats()
    except Exception as e:
        logger.warning(f"Failed to get detailed profiler stats: {e}")
        return {"error": str(e)}


@app.post("/api/profiler/reset")
def profiler_reset():
    """Reset performance profiler data."""
    try:
        from app.core.monitoring.profiler import get_profiler

        profiler = get_profiler()
        profiler.reset()
        return {"message": "Profiler reset successfully"}
    except Exception as e:
        logger.warning(f"Failed to reset profiler: {e}")
        return {"error": str(e)}


@app.get("/api/engines/metrics")
def engine_metrics():
    """Get engine performance metrics."""
    try:
        from app.core.engines.router import router

        return router.get_engine_performance_stats()
    except Exception as e:
        logger.warning(f"Failed to get engine metrics: {e}")
        return {"error": str(e)}


@app.get("/api/engines/metrics/{engine_name}")
def engine_metrics_detail(engine_name: str):
    """Get performance metrics for a specific engine."""
    try:
        from app.core.engines.performance_metrics import get_engine_metrics

        metrics = get_engine_metrics()
        return metrics.get_engine_stats(engine_name)
    except Exception as e:
        logger.warning(f"Failed to get engine metrics for {engine_name}: {e}")
        return {"error": str(e)}


@app.post("/api/engines/metrics/reset")
def engine_metrics_reset(engine_name: str | None = None):
    """Reset engine performance metrics."""
    try:
        from app.core.engines.performance_metrics import get_engine_metrics

        metrics = get_engine_metrics()
        metrics.clear(engine_name)
        return {"message": f"Metrics reset for {engine_name or 'all engines'}"}
    except Exception as e:
        logger.warning(f"Failed to reset engine metrics: {e}")
        return {"error": str(e)}


@app.get("/api/endpoints/metrics")
def endpoint_metrics():
    """Get API endpoint performance metrics."""
    try:
        middleware = _get_performance_middleware()
        if middleware is None:
            return {"error": "Performance monitoring middleware not initialized"}
        return middleware.get_stats()
    except Exception as e:
        logger.warning(f"Failed to get endpoint metrics: {e}")
        return {"error": str(e)}


@app.get("/api/endpoints/metrics/{endpoint_key:path}")
def endpoint_metrics_detail(endpoint_key: str):
    """Get performance metrics for a specific endpoint."""
    try:
        middleware = _get_performance_middleware()
        if middleware is None:
            return {"error": "Performance monitoring middleware not initialized"}
        return middleware.get_metrics(endpoint_key)
    except Exception as e:
        logger.warning(f"Failed to get endpoint metrics for {endpoint_key}: {e}")
        return {"error": str(e)}


@app.post("/api/endpoints/metrics/reset")
def endpoint_metrics_reset():
    """Reset API endpoint performance metrics."""
    try:
        middleware = _get_performance_middleware()
        if middleware is None:
            return {"error": "Performance monitoring middleware not initialized"}
        middleware.reset()
        return {"message": "Endpoint metrics reset successfully"}
    except Exception as e:
        logger.warning(f"Failed to reset endpoint metrics: {e}")
        return {"error": str(e)}


@app.post("/api/cache/invalidate")
def invalidate_cache(
    pattern: str | None = None,
    tags: str | None = None,
    path_prefix: str | None = None,
):
    """
    Invalidate cache entries by pattern, tags, or path prefix.

    Args:
        pattern: Pattern to match in cache key
        tags: Comma-separated list of tags to invalidate
        path_prefix: Path prefix to invalidate (e.g., "/api/profiles")
    """
    get_response_cache, _ = _lazy_import_response_cache()
    cache = get_response_cache()

    tag_list = tags.split(",") if tags else None
    if tag_list:
        tag_list = [tag.strip() for tag in tag_list]

    count = cache.invalidate(
        pattern=pattern,
        tags=tag_list,
        path_prefix=path_prefix,
    )

    return {
        "message": "Cache invalidated",
        "entries_invalidated": count,
        "pattern": pattern,
        "tags": tag_list,
        "path_prefix": path_prefix,
    }


@app.get("/api/validation/stats")
def validation_stats(model_name: str | None = None):
    """Get validation statistics."""
    try:
        from .validation_optimizer import get_cache_stats, get_validation_stats

        stats = get_validation_stats(model_name)
        cache_stats = get_cache_stats()
        return {
            "validation_stats": stats,
            "cache_stats": cache_stats,
        }
    except Exception as e:
        logger.warning(f"Failed to get validation stats: {e}")
        return {"error": str(e)}


@app.post("/api/validation/cache/clear")
def validation_cache_clear():
    """Clear validation cache."""
    try:
        from .validation_optimizer import clear_schema_cache, clear_validation_cache

        clear_validation_cache()
        clear_schema_cache()
        return {"message": "Validation cache cleared successfully"}
    except Exception as e:
        logger.warning(f"Failed to clear validation cache: {e}")
        return {"error": str(e)}


@app.get("/api/scheduler/stats")
def scheduler_stats():
    """Get background task scheduler statistics."""
    try:
        from app.core.tasks.scheduler import get_scheduler

        scheduler = get_scheduler()
        return scheduler.get_stats()
    except Exception as e:
        logger.warning(f"Failed to get scheduler stats: {e}")
        return {"error": str(e)}


@app.get("/api/scheduler/tasks")
def scheduler_tasks(status: str | None = None, priority: str | None = None):
    """List scheduled tasks."""
    try:
        from app.core.tasks.scheduler import TaskPriority, TaskStatus, get_scheduler

        scheduler = get_scheduler()

        # Parse filters
        status_filter = None
        if status:
            try:
                status_filter = TaskStatus[status.upper()]
            except KeyError:
                return {"error": f"Invalid status: {status}"}

        priority_filter = None
        if priority:
            try:
                priority_filter = TaskPriority[priority.upper()]
            except KeyError:
                return {"error": f"Invalid priority: {priority}"}

        tasks = scheduler.list_tasks(status=status_filter, priority=priority_filter)

        return {
            "tasks": [
                {
                    "id": task.id,
                    "name": task.name,
                    "priority": task.priority.name,
                    "status": task.status.value,
                    "created_at": task.created_at.isoformat(),
                    "scheduled_at": (task.scheduled_at.isoformat() if task.scheduled_at else None),
                    "next_run": (task.next_run.isoformat() if task.next_run else None),
                    "last_run": (task.last_run.isoformat() if task.last_run else None),
                    "interval": task.interval,
                    "retry_count": task.retry_count,
                    "max_retries": task.max_retries,
                    "error": task.error,
                }
                for task in tasks
            ],
            "count": len(tasks),
        }
    except Exception as e:
        logger.warning(f"Failed to list scheduler tasks: {e}")
        return {"error": str(e)}


@app.get("/api/scheduler/tasks/{task_id}")
def scheduler_task_detail(task_id: str):
    """Get details for a specific task."""
    try:
        from app.core.tasks.scheduler import get_scheduler

        scheduler = get_scheduler()
        task = scheduler.get_task(task_id)

        if not task:
            from fastapi import HTTPException

            raise HTTPException(status_code=404, detail="Task not found")

        return {
            "id": task.id,
            "name": task.name,
            "priority": task.priority.name,
            "status": task.status.value,
            "created_at": task.created_at.isoformat(),
            "scheduled_at": (task.scheduled_at.isoformat() if task.scheduled_at else None),
            "next_run": task.next_run.isoformat() if task.next_run else None,
            "last_run": task.last_run.isoformat() if task.last_run else None,
            "interval": task.interval,
            "retry_count": task.retry_count,
            "max_retries": task.max_retries,
            "error": task.error,
            "resource_requirements": task.resource_requirements,
        }
    except HTTPException:
        raise
    except Exception as e:
        logger.warning(f"Failed to get task details: {e}")
        return {"error": str(e)}


@app.post("/api/scheduler/tasks/{task_id}/cancel")
def scheduler_task_cancel(task_id: str):
    """Cancel a scheduled task."""
    try:
        from app.core.tasks.scheduler import get_scheduler

        scheduler = get_scheduler()
        success = scheduler.cancel_task(task_id)

        if not success:
            from fastapi import HTTPException

            raise HTTPException(status_code=404, detail="Task not found")

        return {"message": f"Task {task_id} cancelled successfully"}
    except HTTPException:
        raise
    except Exception as e:
        logger.warning(f"Failed to cancel task: {e}")
        return {"error": str(e)}
