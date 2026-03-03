"""Route registration for the VoiceStudio API."""

from __future__ import annotations

import importlib
import logging
import time

from fastapi import FastAPI, Request, WebSocket

from .version_info import get_version_info, get_version_string
from .versioning import CURRENT_VERSION, MIN_SUPPORTED_VERSION, APIVersion

logger = logging.getLogger(__name__)

# Lazy route imports - routes will be imported during startup
_ROUTES_LOADED = False


def register_all_routes(app: FastAPI) -> None:
    """Register all API routes on the FastAPI application."""
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
        "consent",
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
        # Phase X-A: Intelligent Engine Orchestrator
        "orchestrator",
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
    # Note: /api/metrics is registered by register_observability_routes() in main.py.
    # Do NOT register the metrics route module here — it causes I-1 duplication.

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

    # Phase X-A: Intelligent Engine Orchestrator
    _include_route("orchestrator")

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

    # Note: plugins router is already registered via _include_route("plugins") above.
    # Do NOT register it again here — duplicate registration causes I-1 violations.

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

    # Register API v2 routes (import directly to avoid v2/__init__ route-to-route import)
    try:
        from .routes.v2.health import router as v2_health_router

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

    # --- WebSocket endpoints ---

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

    # --- Basic app info / health endpoints ---

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

        from .middleware_setup import get_performance_middleware

        try:
            # Get system metrics
            import psutil

            process = psutil.Process(os.getpid())
            memory_info = process.memory_info()
            cpu_percent = process.cpu_percent(interval=0.1)

            middleware = get_performance_middleware()
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

    load_time = (time.time() - start_time) * 1000
    logger.info(f"Routes loaded in {load_time:.2f}ms")
    _ROUTES_LOADED = True
    logger.debug(f"Total routes registered: {len(app.routes)}")
