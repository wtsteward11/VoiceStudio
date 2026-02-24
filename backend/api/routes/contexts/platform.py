"""
Platform context router.

Task 2.4: Aggregates health, monitoring, settings, telemetry routes.
"""

from fastapi import APIRouter

router = APIRouter(tags=["Platform"])


def _register() -> None:
    from backend.api.routes import (
        advanced_settings,
        api_key_manager,
        auth,
        backup,
        diagnostics,
        errors,
        feedback,
        health,
        help,
        integrations,
        metrics,
        monitoring,
        settings,
        shortcuts,
        slo,
        telemetry,
        tracing,
        version,
    )

    router.include_router(health.router)
    router.include_router(monitoring.router)
    router.include_router(metrics.router)
    router.include_router(tracing.router)
    router.include_router(slo.router)
    router.include_router(telemetry.router)
    router.include_router(settings.router)
    router.include_router(advanced_settings.router)
    router.include_router(backup.router)
    router.include_router(version.router)
    router.include_router(diagnostics.router)
    router.include_router(errors.router)
    router.include_router(feedback.router)
    router.include_router(shortcuts.router)
    router.include_router(help.router)
    router.include_router(integrations.router)
    router.include_router(auth.router)
    router.include_router(api_key_manager.router)
