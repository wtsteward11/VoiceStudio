"""Application lifecycle handlers (startup / shutdown) for VoiceStudio API."""

from __future__ import annotations

import logging
import time
from typing import Any

from fastapi import FastAPI

logger = logging.getLogger(__name__)

# Lazy plugin import
_load_all_plugins = None


def _lazy_import_plugins():
    """Lazy import of plugins module."""
    global _load_all_plugins
    if _load_all_plugins is None:
        from .plugins import load_all_plugins

        _load_all_plugins = load_all_plugins
    return _load_all_plugins


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


def _perform_contract_validation(app: FastAPI):
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


async def on_startup_prepare(app: FastAPI) -> None:
    """
    Blocking startup phase: must complete before the ASGI server accepts traffic.

    Uvicorn/Starlette run this portion of lifespan *before* yielding; keeping it
    bounded lets the desktop health probe (`GET /health`) succeed while heavier
    work runs in `on_startup_heavy`.
    """
    from .route_registry import register_all_routes

    app_config: Any = None
    try:
        from backend.settings import config

        app_config = config
    # ALLOWED: bare except - optional dependency, import failure acceptable
    except (ImportError, AttributeError):
        pass

    app.state.startup_t0 = time.time()

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

        # Durable job queue: reconcile orphaned running/paused rows after migrations
        try:
            from backend.data.repositories.job_repository import (
                JobRepository,
                get_job_repository,
            )
            from backend.services.job_queue_recovery import (
                reconcile_job_history_after_restart,
            )

            repo = get_job_repository()
            if isinstance(repo, JobRepository):
                recovered = await reconcile_job_history_after_restart(repo)
                if recovered:
                    logger.info(
                        "Job queue recovery: marked %s job(s) failed after backend restart",
                        recovered,
                    )
        except Exception as rec_err:
            logger.warning("Job queue recovery skipped: %s", rec_err, exc_info=True)

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
        register_all_routes(app)

        # Wire training progress broadcaster (service layer must not import ws)
        try:
            from backend.api.ws import realtime
            from backend.services.training_broadcaster import set_broadcaster

            class _RealtimeBroadcaster:
                async def broadcast_training_progress(
                    self, training_id: str, progress_data: dict, batch: bool = True
                ) -> None:
                    await realtime.broadcast_training_progress(
                        training_id, progress_data, batch=batch
                    )

            set_broadcaster(_RealtimeBroadcaster())
            logger.info("Training progress broadcaster registered (WebSocket)")
        except ImportError as e:
            logger.debug("WebSocket realtime not available, training progress will not broadcast: %s", e)
    except Exception as e:
        logger.error(f"Error during startup prepare: {e}", exc_info=True)


async def on_startup_heavy(app: FastAPI) -> None:
    """
    Deferred startup work: runs concurrently after the server begins accepting
    connections (see `main._lifespan`). Engine/plugin load must not block
    desktop readiness probes.
    """
    try:
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
        _perform_contract_validation(app)

        # Load plugins after all routes are registered (lazy import)
        load_all_plugins = _lazy_import_plugins()
        plugin_count = load_all_plugins(app)
        logger.info(f"Loaded {plugin_count} plugin(s) on startup")

        startup_start = getattr(app.state, "startup_t0", time.time())
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
        logger.error(f"Error during deferred startup: {e}", exc_info=True)


async def on_startup(app: FastAPI) -> None:
    """Full sequential startup (prepare then heavy) — for tests and manual invocation."""
    await on_startup_prepare(app)
    await on_startup_heavy(app)


async def on_shutdown(app: FastAPI) -> None:
    """Graceful shutdown with engine cleanup and 30-second timeout."""
    import asyncio

    app_config: Any = None
    try:
        from backend.settings import config

        app_config = config
    # ALLOWED: bare except - optional dependency, import failure acceptable
    except (ImportError, AttributeError):
        pass

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
