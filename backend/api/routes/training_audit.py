"""
Training Module Audit API Routes

Provides endpoints for training module auditing and enhancement tracking.
"""

import logging
from typing import Any

from fastapi import APIRouter, HTTPException

from backend.training.facade import (
    AutoTrainer,
    ParameterOptimizer,
    TrainingModuleAuditor,
    TrainingProgressMonitor,
    UnifiedTrainer,
)

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/training/audit", tags=["training"])

# Module registry
TRAINING_MODULES = {
    "unified_trainer": UnifiedTrainer,
    "auto_trainer": AutoTrainer,
    "parameter_optimizer": ParameterOptimizer,
    "training_progress_monitor": TrainingProgressMonitor,
}


@router.get("/all", summary="Audit all training modules")
def audit_all_modules() -> dict[str, Any]:
    """
    Audit all training modules for completeness and enhancements.

    Returns:
        Dictionary with audit results for all modules
    """
    try:
        auditor = TrainingModuleAuditor()

        # Audit all modules
        results = auditor.audit_all_modules(TRAINING_MODULES)

        # Convert results to dictionaries
        results_dict = {}
        for name, result in results.items():
            results_dict[name] = {
                "module_name": result.module_name,
                "is_complete": result.is_complete,
                "score": result.score,
                "missing_features": result.missing_features,
                "optimization_opportunities": result.optimization_opportunities,
                "error_handling_issues": result.error_handling_issues,
                "performance_issues": result.performance_issues,
                "features": {
                    "analytics": result.has_analytics,
                    "checkpoint_management": result.has_checkpoint_management,
                    "progress_monitoring": result.has_progress_monitoring,
                    "parameter_optimization": result.has_parameter_optimization,
                    "quality_tracking": result.has_quality_tracking,
                },
            }

        return {
            "modules": results_dict,
            "summary": auditor.get_audit_summary(),
        }
    except Exception as e:
        logger.error(f"Failed to audit training modules: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to audit training modules: {e!s}",
        )


@router.get("/summary", summary="Get audit summary")
def get_audit_summary() -> dict[str, Any]:
    """
    Get summary of training module audits.

    Returns:
        Summary statistics
    """
    try:
        auditor = TrainingModuleAuditor()

        # Audit all modules
        auditor.audit_all_modules(TRAINING_MODULES)

        # Get summary
        return auditor.get_audit_summary()
    except Exception as e:
        logger.error(f"Failed to get audit summary: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get audit summary: {e!s}")


@router.get("/needing-attention", summary="Get modules needing attention")
def get_modules_needing_attention(min_score: float = 70.0) -> dict[str, Any]:
    """
    Get modules that need attention (score below threshold).

    Args:
        min_score: Minimum acceptable score (default: 70.0)

    Returns:
        List of modules needing attention
    """
    try:
        auditor = TrainingModuleAuditor()

        # Audit all modules
        auditor.audit_all_modules(TRAINING_MODULES)

        # Get modules needing attention
        needing_attention = auditor.get_modules_needing_attention(min_score)

        # Convert to dictionaries
        results = []
        for result in needing_attention:
            results.append(
                {
                    "module_name": result.module_name,
                    "score": result.score,
                    "missing_features": result.missing_features,
                    "optimization_opportunities": result.optimization_opportunities,
                    "error_handling_issues": result.error_handling_issues,
                    "performance_issues": result.performance_issues,
                }
            )

        return {
            "modules": results,
            "count": len(results),
            "min_score": min_score,
        }
    except Exception as e:
        logger.error(f"Failed to get modules needing attention: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get modules needing attention: {e!s}",
        )


@router.get("/report", summary="Generate enhancement report")
def generate_enhancement_report() -> dict[str, str]:
    """
    Generate a markdown report of enhancement opportunities.

    Returns:
        Markdown report
    """
    try:
        auditor = TrainingModuleAuditor()

        # Audit all modules
        auditor.audit_all_modules(TRAINING_MODULES)

        # Generate report
        report = auditor.generate_enhancement_report()

        return {"report": report}
    except Exception as e:
        logger.error(f"Failed to generate report: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to generate report: {e!s}")
