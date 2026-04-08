"""
Quality Management API Routes
Quality optimization, presets, and comparison endpoints
"""

from __future__ import annotations

import asyncio
import logging
import tempfile
import uuid
from datetime import datetime, timezone
from typing import Any, Optional

from fastapi import APIRouter, File, HTTPException, Query, UploadFile
from pydantic import BaseModel

from backend.core.security.file_validation import (
    FileValidationError,
    validate_audio_file,
)
from backend.ml.models.engine_service import get_engine_service
from backend.services.quality_history_models import QualityHistoryEntry

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/quality", tags=["quality"])

# Quality logic lives in backend.services.quality_service (no route imports)


# Request/Response Models
class QualityAnalysisRequest(BaseModel):
    """Request for quality analysis."""

    mos_score: float | None = None
    similarity: float | None = None
    naturalness: float | None = None
    snr_db: float | None = None
    target_tier: str = "standard"


class QualityAnalysisResponse(BaseModel):
    """Response from quality analysis."""

    meets_target: bool
    quality_score: float
    deficiencies: list[dict[str, Any]]
    recommendations: list[dict[str, Any]]


class QualityOptimizationRequest(BaseModel):
    """Request for quality optimization."""

    metrics: dict[str, Any]
    current_params: dict[str, Any]
    target_tier: str = "standard"


class QualityOptimizationResponse(BaseModel):
    """Response from quality optimization."""

    optimized_params: dict[str, Any]
    analysis: dict[str, Any]


class QualityPresetResponse(BaseModel):
    """Response for quality preset information."""

    name: str
    description: str
    target_metrics: dict[str, float]
    parameters: dict[str, Any]


class QualityComparisonRequest(BaseModel):
    """Request for quality comparison."""

    samples: list[dict[str, Any]]  # List of {name, audio_path, metadata}


class QualityComparisonResponse(BaseModel):
    """Response from quality comparison."""

    total_samples: int
    rankings: dict[int, dict[str, Any]]
    statistics: dict[str, dict[str, float]]
    best_samples: dict[str, dict[str, Any]]
    comparison_table: list[dict[str, Any]]


class BenchmarkRequest(BaseModel):
    """Request for quality benchmarking."""

    profile_id: str | None = None
    reference_audio_id: str | None = None
    test_text: str
    language: str = "en"
    engines: list[str] | None = None  # If None, benchmark all engines
    enhance_quality: bool = True


class BenchmarkResult(BaseModel):
    """Result for a single engine benchmark."""

    engine: str
    success: bool
    error: str | None = None
    quality_metrics: dict[str, Any] = {}
    performance: dict[str, Any] = {}


class BenchmarkResponse(BaseModel):
    """Response from quality benchmarking."""

    results: list[BenchmarkResult]
    total_engines: int
    successful_engines: int
    benchmark_id: str | None = None  # For tracking historical benchmarks


# Quality History Models (QualityHistoryEntry lives in backend.services.quality_history_models)


class QualityHistoryRequest(BaseModel):
    """Request to store a quality history entry."""

    profile_id: str
    project_id: str | None = None  # Project ID for filtering (B.1 enhancement)
    engine: str
    metrics: dict[str, Any]
    quality_score: float
    synthesis_text: str | None = None
    audio_url: str | None = None
    enhanced_quality: bool = False
    metadata: dict[str, Any] | None = None


class QualityHistoryResponse(BaseModel):
    """Response containing quality history entries."""

    entries: list[QualityHistoryEntry]
    total: int


class QualityTrendsResponse(BaseModel):
    """Response containing quality trends for a profile."""

    profile_id: str
    time_range: str
    trends: dict[str, list[dict[str, Any]]]  # metric_name -> [{timestamp, value}]
    statistics: dict[str, dict[str, float]]  # metric_name -> {avg, min, max, trend}
    best_entry: QualityHistoryEntry | None = None
    worst_entry: QualityHistoryEntry | None = None


# Quality history: service owns storage and cleanup
from backend.services.quality_history_service import (
    get_entries as get_quality_history_entries,
)
from backend.services.quality_history_service import (
    get_quality_history,
)
from backend.services.quality_history_service import (
    store_entry as store_quality_history_entry,
)


@router.get("/presets", response_model=dict[str, QualityPresetResponse])
@cache_response(ttl=300)  # Cache for 5 minutes (presets are relatively static)
async def list_presets():
    """
    List all available quality presets.

    Returns:
        Dictionary of preset names to preset configurations
    """
    try:
        from backend.services.quality_service import list_presets_with_details

        presets = list_presets_with_details()
        return {
            name: QualityPresetResponse(**info)
            for name, info in presets.items()
        }
    except RuntimeError as e:
        if "not available" in str(e).lower():
            raise HTTPException(status_code=503, detail=str(e)) from e
        raise HTTPException(status_code=500, detail=str(e)) from e
    except Exception as e:
        logger.error(f"Failed to list presets: {e}")
        raise HTTPException(status_code=500, detail=str(e)) from e


@router.get("/presets/{preset_name}", response_model=QualityPresetResponse)
@cache_response(ttl=300)  # Cache for 5 minutes (preset info is relatively static)
async def get_preset(preset_name: str):
    """
    Get information about a specific quality preset.

    Args:
        preset_name: Preset name (fast, standard, high, ultra, professional)

    Returns:
        Preset configuration
    """
    try:
        from backend.services.quality_service import get_preset_info

        info = get_preset_info(preset_name)
        return QualityPresetResponse(**info)
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e)) from e
    except RuntimeError as e:
        if "not available" in str(e).lower():
            raise HTTPException(status_code=503, detail=str(e)) from e
        raise HTTPException(status_code=500, detail=str(e)) from e
    except Exception as e:
        logger.error(f"Failed to get preset: {e}")
        raise HTTPException(status_code=500, detail=str(e)) from e


@router.post("/analyze", response_model=QualityAnalysisResponse)
async def analyze_quality(req: QualityAnalysisRequest):
    """
    Analyze quality metrics and determine if optimization is needed.

    Args:
        req: Quality metrics and target tier

    Returns:
        Analysis results with recommendations
    """
    try:
        from backend.services.quality_service import (
            QualityAnalysisRequest as ServiceRequest,
        )
        from backend.services.quality_service import (
            analyze_quality as svc_analyze,
        )

        service_req = ServiceRequest(
            mos_score=req.mos_score,
            similarity=req.similarity,
            naturalness=req.naturalness,
            snr_db=req.snr_db,
            target_tier=req.target_tier,
        )
        result = await svc_analyze(service_req)
        return QualityAnalysisResponse(
            meets_target=result.meets_target,
            quality_score=result.quality_score,
            deficiencies=result.deficiencies,
            recommendations=result.recommendations,
        )
    except RuntimeError as e:
        if "not available" in str(e).lower():
            raise HTTPException(status_code=503, detail=str(e)) from e
        raise HTTPException(status_code=500, detail=str(e)) from e
    except Exception as e:
        logger.error(f"Quality analysis failed: {e}")
        raise HTTPException(status_code=500, detail=str(e)) from e


@router.post("/optimize", response_model=QualityOptimizationResponse)
async def optimize_quality(req: QualityOptimizationRequest):
    """
    Optimize synthesis parameters based on quality metrics.

    Args:
        req: Current metrics and parameters

    Returns:
        Optimized parameters and analysis
    """
    try:
        from backend.services.quality_service import optimize_quality as svc_optimize

        optimized_params, analysis = svc_optimize(
            metrics=req.metrics,
            current_params=req.current_params,
            target_tier=req.target_tier,
        )
        return QualityOptimizationResponse(
            optimized_params=optimized_params,
            analysis=analysis,
        )
    except RuntimeError as e:
        if "not available" in str(e).lower():
            raise HTTPException(status_code=503, detail=str(e)) from e
        raise HTTPException(status_code=500, detail=str(e)) from e
    except Exception as e:
        logger.error(f"Quality optimization failed: {e}")
        raise HTTPException(status_code=500, detail=str(e)) from e


@router.post("/compare", response_model=QualityComparisonResponse)
async def compare_quality(
    audio_files: list[UploadFile] = File(...),
    reference_audio: UploadFile | None = File(None),
):
    """
    Compare quality metrics across multiple audio samples.

    Args:
        audio_files: List of audio files to compare
        reference_audio: Optional reference audio for similarity

    Returns:
        Comparison results with rankings and statistics
    """
    try:
        from backend.services.quality_service import compare_quality_samples

        ref_path = None
        if reference_audio:
            ref_content = await reference_audio.read()
            try:
                validate_audio_file(ref_content, filename=reference_audio.filename)
            except FileValidationError as e:
                raise HTTPException(
                    status_code=400,
                    detail=f"Invalid reference audio file: {e.message}",
                ) from e
            with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as ref_file:
                ref_file.write(ref_content)
                ref_path = ref_file.name

        samples = []
        for audio_file in audio_files:
            content = await audio_file.read()
            try:
                validate_audio_file(content, filename=audio_file.filename)
            except FileValidationError as e:
                raise HTTPException(
                    status_code=400,
                    detail=f"Invalid audio file '{audio_file.filename}': {e.message}",
                ) from e
            with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as tmp_file:
                tmp_file.write(content)
                tmp_path = tmp_file.name
            metadata = {
                "filename": audio_file.filename,
                "content_type": audio_file.content_type,
            }
            samples.append((audio_file.filename or f"sample_{len(samples)}", tmp_path, ref_path, metadata))

        results = compare_quality_samples(samples)
        return QualityComparisonResponse(
            total_samples=results["total_samples"],
            rankings=results["rankings"],
            statistics=results["statistics"],
            best_samples=results["best_samples"],
            comparison_table=results["comparison_table"],
        )
    except RuntimeError as e:
        if "not available" in str(e).lower():
            raise HTTPException(status_code=503, detail=str(e)) from e
        raise HTTPException(status_code=500, detail=str(e)) from e
    except Exception as e:
        logger.error(f"Quality comparison failed: {e}")
        raise HTTPException(status_code=500, detail=str(e)) from e


@router.get("/engine-recommendation")
@cache_response(ttl=60)  # Cache for 60 seconds (recommendations may change)
async def get_engine_recommendation(
    target_tier: str = "standard",
    min_mos_score: float | None = None,
    min_similarity: float | None = None,
    min_naturalness: float | None = None,
):
    """
    Get recommended engine based on quality requirements.

    Args:
        target_tier: Quality tier (fast, standard, high, ultra)
        min_mos_score: Minimum MOS score required
        min_similarity: Minimum similarity required
        min_naturalness: Minimum naturalness required

    Returns:
        Recommended engine name and reasoning
    """
    try:
        from backend.services.quality_service import suggest_engine

        target_metrics = {}
        if min_mos_score is not None:
            target_metrics["mos_score"] = min_mos_score
        if min_similarity is not None:
            target_metrics["similarity"] = min_similarity
        if min_naturalness is not None:
            target_metrics["naturalness"] = min_naturalness

        recommended_engine = suggest_engine(
            target_tier=target_tier,
            target_metrics=target_metrics if target_metrics else None,
        )
        return {
            "recommended_engine": recommended_engine,
            "target_tier": target_tier,
            "target_metrics": target_metrics,
            "reasoning": f"Engine '{recommended_engine}' best matches quality requirements for tier '{target_tier}'",
        }
    except RuntimeError as e:
        if "not available" in str(e).lower():
            raise HTTPException(status_code=503, detail=str(e)) from e
        raise HTTPException(status_code=500, detail=str(e)) from e
    except Exception as e:
        logger.error(f"Engine recommendation failed: {e}")
        raise HTTPException(status_code=500, detail=str(e)) from e


@router.post("/benchmark", response_model=BenchmarkResponse)
async def run_benchmark(request: BenchmarkRequest):
    """
    Run quality benchmark across multiple engines.

    Implements IDEA 52: Quality Benchmarking and Comparison Tool.

    Args:
        request: Benchmark request with test text, profile/audio, and engine list

    Returns:
        Benchmark results for all engines
    """
    try:
        from backend.services.quality_benchmark_service import (
            BenchmarkReferenceNotFoundError,
            resolve_benchmark_reference,
        )
        from backend.services.quality_benchmark_service import (
            run_benchmark as run_benchmark_svc,
        )

        try:
            reference_audio_path = resolve_benchmark_reference(
                request.profile_id, request.reference_audio_id
            )
        except BenchmarkReferenceNotFoundError as e:
            raise HTTPException(status_code=e.status_code, detail=e.message) from e

        data = run_benchmark_svc(
            reference_audio_path=reference_audio_path,
            test_text=request.test_text,
            language=request.language,
            engines=request.engines,
            enhance_quality=request.enhance_quality,
        )

        return BenchmarkResponse(
            results=[BenchmarkResult(**r) for r in data["results"]],
            total_engines=data["total_engines"],
            successful_engines=data["successful_engines"],
            benchmark_id=data["benchmark_id"],
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Benchmark execution failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Benchmark failed: {e!s}")


@router.get("/dashboard")
@cache_response(ttl=30)  # Cache for 30 seconds (dashboard aggregates data)
async def get_quality_dashboard(project_id: str | None = None, days: int = 30) -> dict[str, Any]:
    """
    Get quality metrics dashboard data.

    Implements IDEA 49: Quality Metrics Visualization Dashboard.

    Args:
        project_id: Optional project ID to filter by
        days: Number of days to include in trends (default: 30)

    Returns:
        Dashboard data with overview, trends, distribution, and alerts
    """
    try:
        from backend.services.quality_dashboard_service import get_dashboard_data
        from backend.services.quality_history_service import get_all_entries_flat

        all_entries = get_all_entries_flat()
        return get_dashboard_data(all_entries, project_id=project_id, days=days)
    except Exception as e:
        logger.error(f"Failed to generate quality dashboard: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to generate dashboard: {e!s}")


# Quality History Endpoints (IDEA 30)


@router.post("/history", response_model=QualityHistoryEntry)
async def store_quality_history(request: QualityHistoryRequest):
    """
    Store a quality history entry for a voice profile.

    Implements IDEA 30: Voice Profile Quality History.

    Args:
        request: Quality history entry data

    Returns:
        Stored quality history entry
    """
    try:
        # Create entry with project_id if provided (B.1 enhancement)
        entry = QualityHistoryEntry(
            id=str(uuid.uuid4()),
            profile_id=request.profile_id,
            project_id=request.project_id,
            timestamp=datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
            engine=request.engine,
            metrics=request.metrics,
            quality_score=request.quality_score,
            synthesis_text=request.synthesis_text,
            audio_url=request.audio_url,
            enhanced_quality=request.enhanced_quality,
            metadata=request.metadata,
        )

        # Store entry (service handles cleanup)
        store_quality_history_entry(request.profile_id, entry)

        logger.debug(f"Stored quality history entry {entry.id} for profile {request.profile_id}")

        return entry

    except Exception as e:
        logger.error(f"Failed to store quality history: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to store quality history: {e!s}")


@router.get("/history/{profile_id}", response_model=QualityHistoryResponse)
@cache_response(ttl=60)  # Cache for 60 seconds (history may update)
async def get_quality_history(
    profile_id: str,
    project_id: str | None = None,  # B.1 enhancement: filter by project
    limit: int | None = None,
    start_date: str | None = None,
    end_date: str | None = None,
):
    """
    Get quality history for a voice profile.

    Implements IDEA 30: Voice Profile Quality History.

    Args:
        profile_id: Voice profile ID
        project_id: Optional project ID to filter by (B.1 enhancement)
        limit: Maximum number of entries to return (default: all)
        start_date: Start date filter (ISO format, optional)
        end_date: End date filter (ISO format, optional)

    Returns:
        Quality history entries for the profile
    """
    try:
        entries = get_quality_history_entries(profile_id)

        # Apply project_id filter if provided (B.1 enhancement)
        if project_id:
            entries = [
                e
                for e in entries
                if (hasattr(e, "project_id") and e.project_id == project_id)
                or (
                    hasattr(e, "metadata")
                    and isinstance(e.metadata, dict)
                    and e.metadata.get("project_id") == project_id
                )
            ]

        # Apply date filters if provided
        if start_date or end_date:
            filtered_entries = []
            for entry in entries:
                entry_date = entry.timestamp
                if start_date and entry_date < start_date:
                    continue
                if end_date and entry_date > end_date:
                    continue
                filtered_entries.append(entry)
            entries = filtered_entries

        # Sort by timestamp (newest first)
        entries.sort(key=lambda e: e.timestamp, reverse=True)

        # Apply limit
        if limit and limit > 0:
            entries = entries[:limit]

        return QualityHistoryResponse(entries=entries, total=len(entries))

    except Exception as e:
        logger.error(f"Failed to get quality history: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get quality history: {e!s}")


@router.get("/history/{profile_id}/trends", response_model=QualityTrendsResponse)
@cache_response(ttl=60)  # Cache for 60 seconds (trends may update)
async def get_quality_trends(profile_id: str, time_range: str = "30d"):
    """
    Get quality trends for a voice profile.

    Implements IDEA 30: Voice Profile Quality History.
    Delegates to quality_trends_service for trends, statistics, best/worst.
    """
    try:
        from backend.services.quality_trends_service import (
            get_quality_trends as compute_quality_trends,
        )

        result = compute_quality_trends(profile_id, time_range)
        return QualityTrendsResponse(**result)
    except Exception as e:
        logger.error(f"Failed to get quality trends: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get quality trends: {e!s}")


# Text Analysis and Quality Recommendations (IDEA 53)
class TextAnalysisRequest(BaseModel):
    """Request for text analysis."""

    text: str
    language: str | None = "en"


class TextAnalysisResponse(BaseModel):
    """Response from text analysis."""

    complexity: str
    content_type: str
    word_count: int
    sentence_count: int
    character_count: int
    avg_words_per_sentence: float
    has_dialogue: bool
    has_technical_terms: bool
    detected_emotions: list[str]
    language: str


class QualityRecommendationRequest(BaseModel):
    """Request for quality recommendations."""

    text: str
    language: str | None = "en"
    available_engines: list[str] | None = None
    target_quality: float | None = None


class QualityRecommendationResponse(BaseModel):
    """Response with quality recommendations."""

    recommended_engine: str
    recommended_quality_mode: str
    recommended_enhance_quality: bool
    predicted_quality_score: float
    reasoning: str
    confidence: float
    text_analysis: TextAnalysisResponse


@router.post("/analyze-text", response_model=TextAnalysisResponse)
async def analyze_text_endpoint(request: TextAnalysisRequest):
    """
    Analyze text content for adaptive quality optimization (IDEA 53).

    Analyzes text for complexity, content type, and characteristics
    to help determine optimal quality settings.

    Args:
        request: Text analysis request with text and language

    Returns:
        Text analysis results
    """
    try:
        from backend.services.quality_text_service import analyze_text

        result = analyze_text(request.text, request.language or "en")

        return TextAnalysisResponse(**result.to_dict())

    except Exception as e:
        logger.error(f"Failed to analyze text: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to analyze text: {e!s}")


@router.post("/recommend-quality", response_model=QualityRecommendationResponse)
async def recommend_quality_endpoint(request: QualityRecommendationRequest):
    """
    Get quality recommendations based on text analysis (IDEA 53).

    Analyzes text and recommends optimal engine, quality mode,
    and settings for best quality output.

    Args:
        request: Quality recommendation request

    Returns:
        Quality recommendations with reasoning
    """
    try:
        from backend.services.quality_text_service import analyze_text, recommend_quality_settings

        # Analyze text
        text_analysis = analyze_text(request.text, request.language or "en")

        # Get recommendations
        recommendation = recommend_quality_settings(
            text_analysis, request.available_engines, request.target_quality
        )

        return QualityRecommendationResponse(
            recommended_engine=recommendation.recommended_engine,
            recommended_quality_mode=recommendation.recommended_quality_mode,
            recommended_enhance_quality=recommendation.recommended_enhance_quality,
            predicted_quality_score=recommendation.predicted_quality_score,
            reasoning=recommendation.reasoning,
            confidence=recommendation.confidence,
            text_analysis=TextAnalysisResponse(**text_analysis.to_dict()),
        )

    except Exception as e:
        logger.error(f"Failed to get quality recommendations: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get quality recommendations: {e!s}")


# Quality Degradation Detection (IDEA 56)
class QualityBaselineResponse(BaseModel):
    """Response for quality baseline."""

    profile_id: str
    baseline_quality: float
    baseline_date: str
    sample_count: int
    metrics: dict[str, Any]


class QualityTrendResponse(BaseModel):
    """Response for quality trend analysis."""

    trend: str
    average_quality: float | None
    trend_direction: str | None
    data_points: int
    first_half_avg: float | None = None
    second_half_avg: float | None = None


# Quality Degradation Detection Models (IDEA 56)
class QualityDegradationAlertResponse(BaseModel):
    """Response model for a quality degradation alert."""

    severity: str
    degradation_percentage: float
    metric_name: str
    current_value: float
    baseline_value: float
    time_window_days: int
    recommendation: str
    confidence: float


class QualityDegradationResponse(BaseModel):
    """Response for quality degradation detection (IDEA 56)."""

    profile_id: str
    has_degradation: bool
    alerts: list[QualityDegradationAlertResponse]
    time_window_days: int


@router.get("/degradation/{profile_id}", response_model=QualityDegradationResponse)
async def check_quality_degradation(
    profile_id: str,
    time_window_days: int = 7,
    degradation_threshold_percent: float = 10.0,
    critical_threshold_percent: float = 25.0,
):
    """
    Check for quality degradation in a voice profile (IDEA 56).

    Compares recent quality metrics against baseline to detect degradation.

    Args:
        profile_id: Voice profile ID to check
        time_window_days: Number of recent days to analyze (default: 7)
        degradation_threshold_percent: Percentage drop to trigger warning (default: 10.0%)
        critical_threshold_percent: Percentage drop to trigger critical alert (default: 25.0%)

    Returns:
        QualityDegradationResponse with alerts if any detected
    """
    try:
        from backend.services.quality_degradation_service import detect_quality_degradation

        # Get quality history
        entries = get_quality_history_entries(profile_id)
        if not entries:
            return QualityDegradationResponse(
                profile_id=profile_id,
                has_degradation=False,
                alerts=[],
                time_window_days=time_window_days,
            )

        # Convert QualityHistoryEntry objects to dicts
        history_dicts = []
        for entry in entries:
            entry_dict = {
                "profile_id": profile_id,
                "timestamp": entry.timestamp,
                "metrics": entry.metrics if isinstance(entry.metrics, dict) else {},
                "quality_score": entry.quality_score,
            }
            history_dicts.append(entry_dict)

        # Detect degradation (will calculate baseline if needed)
        alerts = detect_quality_degradation(
            history_dicts,
            baseline=None,  # Will be calculated automatically
            time_window_days=time_window_days,
            degradation_threshold_percent=degradation_threshold_percent,
            critical_threshold_percent=critical_threshold_percent,
        )

        # Convert alerts to response format
        alert_responses = [QualityDegradationAlertResponse(**alert.to_dict()) for alert in alerts]

        return QualityDegradationResponse(
            profile_id=profile_id,
            has_degradation=len(alert_responses) > 0,
            alerts=alert_responses,
            time_window_days=time_window_days,
        )

    except Exception as e:
        logger.error(f"Failed to check quality degradation: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to check quality degradation: {e!s}")


@router.get("/baseline/{profile_id}", response_model=Optional[QualityBaselineResponse])
async def get_quality_baseline(profile_id: str, time_period_days: int = 30):
    """
    Get quality baseline for a voice profile (IDEA 56).

    Calculates the baseline quality metrics from historical data.

    Args:
        profile_id: Voice profile ID
        time_period_days: Number of days to use for baseline calculation (default: 30)

    Returns:
        QualityBaselineResponse with baseline data
    """
    try:
        from backend.services.quality_degradation_service import calculate_quality_baseline

        # Get quality history
        entries = get_quality_history_entries(profile_id)
        if not entries:
            return None

        # Convert QualityHistoryEntry objects to dicts
        history_dicts = []
        for entry in entries:
            entry_dict = {
                "profile_id": profile_id,
                "timestamp": entry.timestamp,
                "metrics": entry.metrics if isinstance(entry.metrics, dict) else {},
                "quality_score": entry.quality_score,
            }
            history_dicts.append(entry_dict)

        # Calculate baseline
        baseline = calculate_quality_baseline(
            history_dicts, time_period_days=time_period_days, min_samples=5
        )

        if not baseline:
            return None

        # Convert baseline to response format matching frontend expectations
        # Frontend expects: baseline_quality, baseline_date, metrics
        # Utility returns: baseline_quality_score, calculated_at, baseline_metrics
        return QualityBaselineResponse(
            profile_id=baseline.profile_id,
            baseline_quality=baseline.baseline_quality_score,
            baseline_date=baseline.calculated_at,
            sample_count=baseline.sample_count,
            metrics={
                k: float(v) if isinstance(v, (int, float)) else v
                for k, v in baseline.baseline_metrics.items()
            },
        )

    except Exception as e:
        logger.error(f"Failed to get quality baseline: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get quality baseline: {e!s}")


# Quality Consistency Monitoring (IDEA 59)
class QualityStandardRequest(BaseModel):
    """Request to set quality standard for a project."""

    project_id: str
    standard_name: str = "professional"  # professional, high, standard, minimum


class QualityConsistencyReport(BaseModel):
    """Quality consistency report for a project."""

    project_id: str
    has_data: bool
    time_period_days: int
    total_samples: int | None = None
    consistency_score: float | None = None
    is_consistent: bool | None = None
    statistics: dict[str, Any] | None = None
    violations: list[dict[str, Any]] | None = None
    trends: dict[str, str] | None = None
    recommendations: list[dict[str, Any]] | None = None
    message: str | None = None


class ProjectQualityTrendsResponse(BaseModel):
    """Quality trends response for project consistency."""

    project_id: str
    has_data: bool
    time_period_days: int
    daily_averages: dict[str, dict[str, dict[str, float]]] | None = None
    overall_trend: str | None = None
    message: str | None = None


class AllProjectsConsistencyResponse(BaseModel):
    """Response for all projects consistency check."""

    total_projects: int
    projects_with_data: int
    consistent_projects: int
    overall_consistency: float
    total_samples: int
    total_violations: int
    projects: dict[str, QualityConsistencyReport]


@router.post("/consistency/standard")
async def set_quality_standard(request: QualityStandardRequest):
    """
    Set quality standard for a project (IDEA 59).

    Args:
        request: QualityStandardRequest with project_id and standard_name

    Returns:
        Success message
    """
    try:
        from backend.services.quality_consistency_service import get_quality_consistency_monitor

        monitor = get_quality_consistency_monitor()
        success = monitor.set_quality_standard(request.project_id, request.standard_name)

        if success:
            return {
                "message": f"Quality standard '{request.standard_name}' set for project {request.project_id}"
            }
        else:
            raise HTTPException(status_code=400, detail="Failed to set quality standard")

    except Exception as e:
        logger.error(f"Failed to set quality standard: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to set quality standard: {e!s}")


@router.post("/consistency/record")
async def record_quality_metrics(
    project_id: str,
    profile_id: str | None = None,
    audio_id: str | None = None,
    metrics: dict[str, Any] | None = None,
):
    """
    Record quality metrics for consistency tracking (IDEA 59).

    Args:
        project_id: Project identifier
        profile_id: Voice profile identifier (optional)
        audio_id: Audio identifier (optional)
        metrics: Quality metrics dictionary

    Returns:
        Success message
    """
    try:
        from backend.services.quality_consistency_service import get_quality_consistency_monitor

        if metrics is None:
            raise HTTPException(status_code=400, detail="Metrics are required")

        monitor = get_quality_consistency_monitor()
        success = monitor.record_quality_metrics(
            project_id=project_id,
            profile_id=profile_id,
            metrics=metrics,
            audio_id=audio_id,
        )

        if success:
            return {"message": "Quality metrics recorded successfully"}
        else:
            raise HTTPException(status_code=400, detail="Failed to record quality metrics")

    except Exception as e:
        logger.error(f"Failed to record quality metrics: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to record quality metrics: {e!s}")


@router.get("/consistency/{project_id}", response_model=QualityConsistencyReport)
async def check_project_consistency(project_id: str, time_period_days: int = 30):
    """
    Check quality consistency for a project (IDEA 59).

    Args:
        project_id: Project identifier
        time_period_days: Number of days to analyze (default: 30)

    Returns:
        QualityConsistencyReport
    """
    try:
        from backend.services.quality_consistency_service import get_quality_consistency_monitor

        monitor = get_quality_consistency_monitor()
        report = monitor.check_quality_consistency(project_id, time_period_days)

        return QualityConsistencyReport(**report)

    except Exception as e:
        logger.error(f"Failed to check project consistency: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to check project consistency: {e!s}")


@router.get("/consistency/all", response_model=AllProjectsConsistencyResponse)
async def check_all_projects_consistency(time_period_days: int = 30):
    """
    Check quality consistency across all projects (IDEA 59).

    Args:
        time_period_days: Number of days to analyze (default: 30)

    Returns:
        AllProjectsConsistencyResponse
    """
    try:
        from backend.services.quality_consistency_service import get_quality_consistency_monitor

        monitor = get_quality_consistency_monitor()
        report = monitor.check_all_projects_consistency(time_period_days)

        # Convert project reports to QualityConsistencyReport objects
        projects = {
            pid: QualityConsistencyReport(**proj_report)
            for pid, proj_report in report["projects"].items()
        }

        return AllProjectsConsistencyResponse(
            total_projects=report["total_projects"],
            projects_with_data=report["projects_with_data"],
            consistent_projects=report["consistent_projects"],
            overall_consistency=report["overall_consistency"],
            total_samples=report["total_samples"],
            total_violations=report["total_violations"],
            projects=projects,
        )

    except Exception as e:
        logger.error(f"Failed to check all projects consistency: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to check all projects consistency: {e!s}",
        )


@router.get("/consistency/{project_id}/trends", response_model=ProjectQualityTrendsResponse)
async def get_project_quality_trends(project_id: str, time_period_days: int = 30):
    """
    Get quality trends for a project (IDEA 59).

    Args:
        project_id: Project identifier
        time_period_days: Number of days to analyze (default: 30)

    Returns:
        ProjectQualityTrendsResponse
    """
    try:
        from backend.services.quality_consistency_service import get_quality_consistency_monitor

        monitor = get_quality_consistency_monitor()
        trends = monitor.get_quality_trends(project_id, time_period_days)

        return ProjectQualityTrendsResponse(**trends)

    except Exception as e:
        logger.error(f"Failed to get project quality trends: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get project quality trends: {e!s}")


# Advanced Quality Metrics Visualization (IDEA 60)
class QualityHeatmapRequest(BaseModel):
    """Request for quality heatmap."""

    quality_data: list[dict[str, Any]]
    x_dimension: str = "engine"
    y_dimension: str = "profile"
    metric: str = "mos_score"


class QualityHeatmapResponse(BaseModel):
    """Response for quality heatmap."""

    x_dimension: str
    y_dimension: str
    metric: str
    x_values: list[str]
    y_values: list[str]
    matrix: dict[str, dict[str, Any]]
    min_value: float
    max_value: float


class QualityCorrelationResponse(BaseModel):
    """Response for quality correlations."""

    metrics: list[str]
    correlations: dict[str, dict[str, float]]


class QualityAnomalyResponse(BaseModel):
    """Response for quality anomalies."""

    metric: str
    threshold_std: float
    anomalies: list[dict[str, Any]]
    total_samples: int
    anomaly_count: int


class QualityPredictionRequest(BaseModel):
    """Request for quality prediction."""

    input_factors: dict[str, Any]
    quality_data: list[dict[str, Any]] | None = None


class QualityPredictionResponse(BaseModel):
    """Response for quality prediction."""

    input_factors: dict[str, Any]
    predicted_metrics: dict[str, float | None]
    confidence: float
    sample_count: int


class QualityInsight(BaseModel):
    """Quality insight."""

    type: str  # positive, warning, info
    title: str
    message: str
    priority: str  # high, medium, low
    action: str | None = None


class QualityInsightsResponse(BaseModel):
    """Response for quality insights."""

    insights: list[QualityInsight]
    time_period_days: int
    total_samples: int


@router.post("/visualization/heatmap", response_model=QualityHeatmapResponse)
async def get_quality_heatmap(request: QualityHeatmapRequest):
    """
    Get quality heatmap data (IDEA 60).

    Args:
        request: QualityHeatmapRequest with quality data and dimensions

    Returns:
        QualityHeatmapResponse
    """
    try:
        from backend.services.quality_visualization_service import calculate_quality_heatmap

        heatmap = calculate_quality_heatmap(
            quality_data=request.quality_data,
            x_dimension=request.x_dimension,
            y_dimension=request.y_dimension,
            metric=request.metric,
        )

        return QualityHeatmapResponse(**heatmap)

    except Exception as e:
        logger.error(f"Failed to calculate quality heatmap: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to calculate quality heatmap: {e!s}")


@router.post("/visualization/correlations", response_model=QualityCorrelationResponse)
async def get_quality_correlations(quality_data: list[dict[str, Any]]):
    """
    Get quality metric correlations (IDEA 60).

    Args:
        quality_data: List of quality records

    Returns:
        QualityCorrelationResponse
    """
    try:
        from backend.services.quality_visualization_service import calculate_quality_correlations

        correlations = calculate_quality_correlations(quality_data)

        return QualityCorrelationResponse(**correlations)

    except Exception as e:
        logger.error(f"Failed to calculate quality correlations: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to calculate quality correlations: {e!s}",
        )


@router.post("/visualization/anomalies", response_model=QualityAnomalyResponse)
async def detect_quality_anomalies_endpoint(
    quality_data: list[dict[str, Any]],
    metric: str = "mos_score",
    threshold_std: float = 2.0,
):
    """
    Detect quality anomalies (IDEA 60).

    Args:
        quality_data: List of quality records
        metric: Metric to analyze
        threshold_std: Standard deviation threshold

    Returns:
        QualityAnomalyResponse
    """
    try:
        from backend.services.quality_visualization_service import detect_quality_anomalies

        anomalies = detect_quality_anomalies(
            quality_data=quality_data, metric=metric, threshold_std=threshold_std
        )

        return QualityAnomalyResponse(
            metric=metric,
            threshold_std=threshold_std,
            anomalies=anomalies,
            total_samples=len(quality_data),
            anomaly_count=len(anomalies),
        )

    except Exception as e:
        logger.error(f"Failed to detect quality anomalies: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to detect quality anomalies: {e!s}")


@router.post("/visualization/predict", response_model=QualityPredictionResponse)
async def predict_quality_endpoint(request: QualityPredictionRequest):
    """
    Predict quality based on input factors (IDEA 60).

    Args:
        request: QualityPredictionRequest with input factors and optional quality data

    Returns:
        QualityPredictionResponse
    """
    try:
        from backend.services.quality_visualization_service import predict_quality

        # Use provided quality data or get from consistency monitor
        quality_data = request.quality_data
        if not quality_data:
            from backend.services.quality_consistency_service import get_quality_consistency_monitor

            monitor = get_quality_consistency_monitor()
            # Get quality history from monitor
            quality_data = []
            for project_id, history in monitor.quality_history.items():
                for record in history:
                    quality_data.append(
                        {
                            "project_id": project_id,
                            "profile_id": record.get("profile_id"),
                            "engine": request.input_factors.get("engine"),
                            "metrics": record.get("metrics", {}),
                        }
                    )

        prediction = predict_quality(quality_data, request.input_factors)

        return QualityPredictionResponse(**prediction)

    except Exception as e:
        logger.error(f"Failed to predict quality: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to predict quality: {e!s}")


@router.post("/visualization/insights", response_model=QualityInsightsResponse)
async def get_quality_insights(quality_data: list[dict[str, Any]], time_period_days: int = 30):
    """
    Get quality insights and recommendations (IDEA 60).

    Args:
        quality_data: List of quality records
        time_period_days: Time period for analysis

    Returns:
        QualityInsightsResponse
    """
    try:
        from backend.services.quality_visualization_service import generate_quality_insights

        insights_data = generate_quality_insights(quality_data, time_period_days)

        insights = [QualityInsight(**insight) for insight in insights_data]

        return QualityInsightsResponse(
            insights=insights,
            time_period_days=time_period_days,
            total_samples=len(quality_data),
        )

    except Exception as e:
        logger.error(f"Failed to generate quality insights: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to generate quality insights: {e!s}")


@router.post("/visualization/export/heatmap")
async def export_quality_heatmap(
    request: QualityHeatmapRequest,
    format: str = "json",
):
    """
    Export quality heatmap data.

    Args:
        quality_data: List of quality records
        x_dimension: Dimension for X axis
        y_dimension: Dimension for Y axis
        metric: Metric to visualize
        format: Export format (json, csv)

    Returns:
        Exported data in requested format
    """
    try:
        import csv
        import io

        from backend.services.quality_visualization_service import calculate_quality_heatmap

        heatmap = calculate_quality_heatmap(
            quality_data=request.quality_data,
            x_dimension=request.x_dimension,
            y_dimension=request.y_dimension,
            metric=request.metric,
        )

        if format.lower() == "csv":
            # Generate CSV
            output = io.StringIO()
            writer = csv.writer(output)

            # Header
            writer.writerow([request.x_dimension, request.y_dimension, request.metric, "count"])

            # Data rows
            for _cell_key, cell_data in heatmap["matrix"].items():
                writer.writerow(
                    [
                        cell_data["x"],
                        cell_data["y"],
                        cell_data["value"],
                        cell_data["count"],
                    ]
                )

            from fastapi.responses import Response

            return Response(
                content=output.getvalue(),
                media_type="text/csv",
                headers={
                    "Content-Disposition": (
                        f'attachment; filename="quality_heatmap_{request.metric}.csv"'
                    )
                },
            )
        else:
            # JSON format
            return heatmap

    except Exception as e:
        logger.error(f"Failed to export quality heatmap: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to export quality heatmap: {e!s}")


@router.post("/visualization/export/correlations")
async def export_quality_correlations(
    quality_data: list[dict[str, Any]], format: str = Query("json")
):
    """
    Export quality correlation matrix.

    Args:
        quality_data: List of quality records
        format: Export format (json, csv)

    Returns:
        Exported correlation data
    """
    try:
        import csv
        import io

        from backend.services.quality_visualization_service import calculate_quality_correlations

        correlations = calculate_quality_correlations(quality_data)

        if format.lower() == "csv":
            # Generate CSV
            output = io.StringIO()
            writer = csv.writer(output)

            # Header row
            header = ["Metric"] + correlations["metrics"]
            writer.writerow(header)

            # Data rows
            for metric1 in correlations["metrics"]:
                row = [metric1]
                for metric2 in correlations["metrics"]:
                    row.append(correlations["correlations"][metric1].get(metric2, 0.0))
                writer.writerow(row)

            from fastapi.responses import Response

            return Response(
                content=output.getvalue(),
                media_type="text/csv",
                headers={
                    "Content-Disposition": ('attachment; filename="quality_correlations.csv"')
                },
            )
        else:
            # JSON format
            return correlations

    except Exception as e:
        logger.error(f"Failed to export quality correlations: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to export quality correlations: {e!s}",
        )


@router.post("/visualization/export/anomalies")
async def export_quality_anomalies(
    quality_data: list[dict[str, Any]],
    metric: str = Query("mos_score"),
    threshold_std: float = Query(2.0),
    format: str = Query("json"),
):
    """
    Export quality anomaly data.

    Args:
        quality_data: List of quality records
        metric: Metric to analyze
        threshold_std: Standard deviation threshold
        format: Export format (json, csv)

    Returns:
        Exported anomaly data
    """
    try:
        import csv
        import io

        from backend.services.quality_visualization_service import detect_quality_anomalies

        anomalies = detect_quality_anomalies(
            quality_data=quality_data, metric=metric, threshold_std=threshold_std
        )

        if format.lower() == "csv":
            # Generate CSV
            output = io.StringIO()
            writer = csv.writer(output)

            # Header
            writer.writerow(
                [
                    "index",
                    "metric",
                    "value",
                    "mean",
                    "std",
                    "z_score",
                    "deviation",
                ]
            )

            # Data rows
            for anomaly in anomalies:
                writer.writerow(
                    [
                        anomaly["index"],
                        anomaly["metric"],
                        anomaly["value"],
                        anomaly["mean"],
                        anomaly["std"],
                        anomaly["z_score"],
                        anomaly["deviation"],
                    ]
                )

            from fastapi.responses import Response

            return Response(
                content=output.getvalue(),
                media_type="text/csv",
                headers={
                    "Content-Disposition": (
                        f'attachment; filename="quality_anomalies_{metric}.csv"'
                    )
                },
            )
        else:
            # JSON format
            return {
                "metric": metric,
                "threshold_std": threshold_std,
                "anomalies": anomalies,
                "total_samples": len(quality_data),
                "anomaly_count": len(anomalies),
            }

    except Exception as e:
        logger.error(f"Failed to export quality anomalies: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to export quality anomalies: {e!s}")


@router.post("/visualization/export/insights")
async def export_quality_insights(
    quality_data: list[dict[str, Any]],
    time_period_days: int = Query(30),
    format: str = Query("json"),
):
    """
    Export quality insights.

    Args:
        quality_data: List of quality records
        time_period_days: Time period for analysis
        format: Export format (json, csv)

    Returns:
        Exported insights data
    """
    try:
        import csv
        import io

        from backend.services.quality_visualization_service import generate_quality_insights

        insights_data = generate_quality_insights(quality_data, time_period_days)

        if format.lower() == "csv":
            # Generate CSV
            output = io.StringIO()
            writer = csv.writer(output)

            # Header
            writer.writerow(["type", "title", "message", "priority", "action"])

            # Data rows
            for insight in insights_data:
                writer.writerow(
                    [
                        insight.get("type", ""),
                        insight.get("title", ""),
                        insight.get("message", ""),
                        insight.get("priority", ""),
                        insight.get("action", ""),
                    ]
                )

            from fastapi.responses import Response

            return Response(
                content=output.getvalue(),
                media_type="text/csv",
                headers={"Content-Disposition": ('attachment; filename="quality_insights.csv"')},
            )
        else:
            # JSON format
            return {
                "insights": insights_data,
                "time_period_days": time_period_days,
                "total_samples": len(quality_data),
            }

    except Exception as e:
        logger.error(f"Failed to export quality insights: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to export quality insights: {e!s}")
