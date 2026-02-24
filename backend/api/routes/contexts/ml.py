"""
ML context router.

Task 2.4: Aggregates engines, models, training, GPU routes.
"""

from fastapi import APIRouter

router = APIRouter(tags=["ML"])


def _register() -> None:
    from backend.api.routes import (
        dataset,
        dataset_editor,
        drift,
        embedding_explorer,
        engine,
        engine_audit,
        engines,
        eval_abx,
        gpu_status,
        ml_optimization,
        model_inspect,
        models,
        pipeline,
        training,
        training_audit,
    )

    router.include_router(engines.router)
    router.include_router(engine.router)
    router.include_router(engine_audit.router)
    router.include_router(models.router)
    router.include_router(model_inspect.router)
    router.include_router(training.router)
    router.include_router(training_audit.router)
    router.include_router(dataset.router)
    router.include_router(dataset_editor.router)
    router.include_router(gpu_status.router)
    router.include_router(ml_optimization.router)
    router.include_router(drift.router)
    router.include_router(eval_abx.router)
    router.include_router(pipeline.router)
    router.include_router(embedding_explorer.router)
