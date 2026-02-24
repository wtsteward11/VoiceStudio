"""
Engine Audit API Routes

Provides endpoints for engine auditing and enhancement tracking.

Architecture Note:
    This route uses EngineService for engine access, following Clean Architecture
    patterns. Direct imports from app.core.engines are avoided.
"""

import logging
from typing import Any

from fastapi import APIRouter, Depends, HTTPException

from backend.ml.models.engine_service import IEngineService, get_engine_service

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/engines/audit", tags=["engines"])


@router.get("/all", summary="Audit all engines")
def audit_all_engines(
    engine_service: IEngineService = Depends(get_engine_service),
) -> dict[str, Any]:
    """
    Audit all registered engines for completeness and enhancements.

    Returns:
        Dictionary with audit results for all engines
    """
    try:
        result = engine_service.audit_all_engines()
        if "error" in result:
            raise HTTPException(status_code=500, detail=result["error"])
        return result
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to audit engines: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to audit engines: {e!s}")


@router.get("/summary", summary="Get audit summary")
def get_audit_summary(
    engine_service: IEngineService = Depends(get_engine_service),
) -> dict[str, Any]:
    """
    Get summary of engine audits.

    Returns:
        Summary statistics
    """
    try:
        result = engine_service.get_audit_summary()
        if "error" in result:
            raise HTTPException(status_code=500, detail=result["error"])
        return result
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get audit summary: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get audit summary: {e!s}")


@router.get("/needing-attention", summary="Get engines needing attention")
def get_engines_needing_attention(
    min_score: float = 70.0,
    engine_service: IEngineService = Depends(get_engine_service),
) -> dict[str, Any]:
    """
    Get engines that need attention (score below threshold).

    Args:
        min_score: Minimum acceptable score (default: 70.0)

    Returns:
        List of engines needing attention
    """
    try:
        registry = engine_service.get_engine_registry()
        auditor = engine_service.get_engine_auditor()

        if registry is None or auditor is None:
            raise HTTPException(status_code=500, detail="Engine audit not available")

        # Get all engines
        engines = registry.get_all_engines()

        # Audit all engines
        auditor.audit_all_engines(engines)

        # Get engines needing attention
        needing_attention = auditor.get_engines_needing_attention(min_score)

        # Convert to dictionaries
        results = []
        for result in needing_attention:
            results.append(
                {
                    "engine_name": result.engine_name,
                    "score": result.score,
                    "missing_methods": result.missing_methods,
                    "missing_features": result.missing_features,
                    "optimization_opportunities": result.optimization_opportunities,
                    "quality_enhancements": result.quality_enhancements,
                }
            )

        return {
            "engines": results,
            "count": len(results),
            "min_score": min_score,
        }
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get engines needing attention: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get engines needing attention: {e!s}",
        )


@router.get("/report", summary="Generate enhancement report")
def generate_enhancement_report(
    engine_service: IEngineService = Depends(get_engine_service),
) -> dict[str, str]:
    """
    Generate a markdown report of enhancement opportunities.

    Returns:
        Markdown report
    """
    try:
        registry = engine_service.get_engine_registry()
        auditor = engine_service.get_engine_auditor()

        if registry is None or auditor is None:
            raise HTTPException(status_code=500, detail="Engine audit not available")

        # Get all engines
        engines = registry.get_all_engines()

        # Audit all engines
        auditor.audit_all_engines(engines)

        # Generate report
        report = auditor.generate_enhancement_report()

        return {"report": report}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to generate report: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to generate report: {e!s}")
