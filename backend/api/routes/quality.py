"""
Quality Management API Routes
Quality optimization, presets, and comparison endpoints
"""

from __future__ import annotations

import logging
import tempfile
import uuid
from datetime import datetime
from typing import Any

from fastapi import APIRouter, File, HTTPException, Query, UploadFile
from pydantic import BaseModel

from backend.core.security.file_validation import (
    FileValidationError,
    validate_audio_file,
)
from backend.ml.models.engine_service import get_engine_service

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/quality", tags=["quality"])

# Quality optimization via EngineService (ADR-008 compliant)
HAS_QUALITY_OPTIMIZATION = False
HAS_QUALITY_PRESETS = False
HAS_QUALITY_COMPARISON = False
_quality_engine_service: Any = None

try:
    _quality_engine_service = get_engine_service()
    presets = _quality_engine_service.get_quality_presets()
    HAS_QUALITY_PRESETS = len(presets) > 0
    HAS_QUALITY_OPTIMIZATION = True
    HAS_QUALITY_COMPARISON = True
    logger.info(f"Quality EngineService initialized with {len(presets)} presets")
except Exception as e:
    logger.warning(f"Quality optimization modules not available: {e}")

try:
    from app.core.engines.quality_comparison import QualityComparison as QualityComparison
    from app.core.engines.quality_optimizer import QualityOptimizer as QualityOptimizer
    from app.core.engines.quality_optimizer import (
        optimize_synthesis_for_quality as optimize_synthesis_for_quality,
    )
    from app.core.engines.quality_presets import get_preset_description as get_preset_description
    from app.core.engines.quality_presets import (
        get_preset_target_metrics as get_preset_target_metrics,
    )
    from app.core.engines.quality_presets import get_quality_preset as get_quality_preset
    from app.core.engines.quality_presets import (
        get_synthesis_params_from_preset as get_synthesis_params_from_preset,
    )
    from app.core.engines.quality_presets import list_quality_presets as list_quality_presets
except ImportError:  # ALLOWED: bare except - optional quality presets
    pass


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


# Quality History Models
class QualityHistoryEntry(BaseModel):
    """Quality history entry for a voice profile."""

    id: str
    profile_id: str
    project_id: str | None = None  # Project ID for filtering (B.1 enhancement)
    timestamp: str  # ISO format datetime string
    engine: str
    metrics: dict[str, Any]
    quality_score: float
    synthesis_text: str | None = None
    audio_url: str | None = None
    enhanced_quality: bool = False
    metadata: dict[str, Any] | None = None


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


# In-memory storage for quality history (replace with database in production)
_quality_history: dict[str, list[QualityHistoryEntry]] = {}  # profile_id -> list of entries
_MAX_HISTORY_ENTRIES_PER_PROFILE = 1000  # Maximum entries per profile
_MAX_TOTAL_ENTRIES = 10000  # Maximum total entries across all profiles


def _cleanup_old_history():
    """
    Clean up old quality history entries to prevent memory accumulation.

    Removes oldest entries when limits are exceeded.
    """
    global _quality_history

    # First, clean up per-profile limits
    for profile_id, entries in list(_quality_history.items()):
        if len(entries) > _MAX_HISTORY_ENTRIES_PER_PROFILE:
            # Sort by timestamp (oldest first) and remove excess
            entries.sort(key=lambda e: e.timestamp)
            excess = len(entries) - _MAX_HISTORY_ENTRIES_PER_PROFILE
            _quality_history[profile_id] = entries[excess:]
            logger.debug(
                f"Cleaned up {excess} old quality history entries for profile {profile_id}"
            )

    # Then, clean up total limit across all profiles
    total_entries = sum(len(entries) for entries in _quality_history.values())
    if total_entries > _MAX_TOTAL_ENTRIES:
        # Collect all entries with profile_id for sorting
        all_entries = []
        for profile_id, entries in _quality_history.items():
            for entry in entries:
                all_entries.append((profile_id, entry))

        # Sort by timestamp (oldest first)
        all_entries.sort(key=lambda x: x[1].timestamp)

        # Remove oldest entries until under limit
        excess = total_entries - _MAX_TOTAL_ENTRIES
        removed = 0
        for profile_id, entry in all_entries[:excess]:
            if profile_id in _quality_history:
                try:
                    _quality_history[profile_id].remove(entry)
                    removed += 1
                except ValueError:
                    pass  # Entry already removed

        # Clean up empty profiles
        _quality_history = {pid: entries for pid, entries in _quality_history.items() if entries}

        logger.debug(f"Cleaned up {removed} old quality history entries globally")


@router.get("/presets", response_model=dict[str, QualityPresetResponse])
@cache_response(ttl=300)  # Cache for 5 minutes (presets are relatively static)
async def list_presets():
    """
    List all available quality presets.

    Returns:
        Dictionary of preset names to preset configurations
    """
    if not HAS_QUALITY_PRESETS:
        raise HTTPException(status_code=503, detail="Quality presets not available")

    try:
        presets = list_quality_presets()
        result = {}

        for name, config in presets.items():
            # Get synthesis parameters for default engine
            params = get_synthesis_params_from_preset(name)
            result[name] = QualityPresetResponse(
                name=name,
                description=config.get("description", ""),
                target_metrics=config.get("target_metrics", {}),
                parameters=params,
            )

        return result

    except Exception as e:
        logger.error(f"Failed to list presets: {e}")
        raise HTTPException(status_code=500, detail=str(e))


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
    if not HAS_QUALITY_PRESETS:
        raise HTTPException(status_code=503, detail="Quality presets not available")

    try:
        preset = get_quality_preset(preset_name)
        if not preset:
            raise HTTPException(status_code=404, detail=f"Preset '{preset_name}' not found")

        params = get_synthesis_params_from_preset(preset_name)

        return QualityPresetResponse(
            name=preset_name,
            description=get_preset_description(preset_name),
            target_metrics=get_preset_target_metrics(preset_name),
            parameters=params,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get preset: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/analyze", response_model=QualityAnalysisResponse)
async def analyze_quality(req: QualityAnalysisRequest):
    """
    Analyze quality metrics and determine if optimization is needed.

    Args:
        req: Quality metrics and target tier

    Returns:
        Analysis results with recommendations
    """
    if not HAS_QUALITY_OPTIMIZATION:
        raise HTTPException(status_code=503, detail="Quality optimization not available")

    try:
        metrics = {
            "mos_score": req.mos_score,
            "similarity": req.similarity,
            "naturalness": req.naturalness,
            "snr_db": req.snr_db,
        }

        # Remove None values
        metrics = {k: v for k, v in metrics.items() if v is not None}

        optimizer = QualityOptimizer(target_tier=req.target_tier)
        analysis = optimizer.analyze_quality(metrics)

        return QualityAnalysisResponse(
            meets_target=analysis["meets_target"],
            quality_score=analysis["quality_score"],
            deficiencies=analysis["deficiencies"],
            recommendations=analysis["recommendations"],
        )

    except Exception as e:
        logger.error(f"Quality analysis failed: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/optimize", response_model=QualityOptimizationResponse)
async def optimize_quality(req: QualityOptimizationRequest):
    """
    Optimize synthesis parameters based on quality metrics.

    Args:
        req: Current metrics and parameters

    Returns:
        Optimized parameters and analysis
    """
    if not HAS_QUALITY_OPTIMIZATION:
        raise HTTPException(status_code=503, detail="Quality optimization not available")

    try:
        optimized_params, analysis = optimize_synthesis_for_quality(
            metrics=req.metrics,
            current_params=req.current_params,
            target_tier=req.target_tier,
        )

        return QualityOptimizationResponse(
            optimized_params=optimized_params,
            analysis=analysis,
        )

    except Exception as e:
        logger.error(f"Quality optimization failed: {e}")
        raise HTTPException(status_code=500, detail=str(e))


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
    if not HAS_QUALITY_COMPARISON:
        raise HTTPException(status_code=503, detail="Quality comparison not available")

    try:
        comparison = QualityComparison()

        # Save and validate reference audio if provided
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

        # Process each audio file
        for audio_file in audio_files:
            # Read and validate audio file
            content = await audio_file.read()
            try:
                validate_audio_file(content, filename=audio_file.filename)
            except FileValidationError as e:
                raise HTTPException(
                    status_code=400,
                    detail=f"Invalid audio file '{audio_file.filename}': {e.message}",
                ) from e
            # Save to temporary file
            with tempfile.NamedTemporaryFile(delete=False, suffix=".wav") as tmp_file:
                tmp_file.write(content)
                tmp_path = tmp_file.name

            # Add to comparison
            metadata = {
                "filename": audio_file.filename,
                "content_type": audio_file.content_type,
            }
            comparison.add_sample(
                name=audio_file.filename or f"sample_{len(comparison.comparisons)}",
                audio=tmp_path,
                reference_audio=ref_path,
                metadata=metadata,
            )

        # Compare
        results = comparison.compare()

        return QualityComparisonResponse(
            total_samples=results["total_samples"],
            rankings=results["rankings"],
            statistics=results["statistics"],
            best_samples=results["best_samples"],
            comparison_table=results["comparison_table"],
        )

    except Exception as e:
        logger.error(f"Quality comparison failed: {e}")
        raise HTTPException(status_code=500, detail=str(e))


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
    if not HAS_QUALITY_OPTIMIZATION:
        raise HTTPException(status_code=503, detail="Quality optimization not available")

    try:
        optimizer = QualityOptimizer(target_tier=target_tier)

        # Build target metrics
        target_metrics = {}
        if min_mos_score:
            target_metrics["mos_score"] = min_mos_score
        if min_similarity:
            target_metrics["similarity"] = min_similarity
        if min_naturalness:
            target_metrics["naturalness"] = min_naturalness

        # Get recommendation
        recommended_engine = optimizer.suggest_engine(target_metrics if target_metrics else None)

        return {
            "recommended_engine": recommended_engine,
            "target_tier": target_tier,
            "target_metrics": target_metrics or optimizer.target_metrics,
            "reasoning": f"Engine '{recommended_engine}' best matches quality requirements for tier '{target_tier}'",
        }

    except Exception as e:
        logger.error(f"Engine recommendation failed: {e}")
        raise HTTPException(status_code=500, detail=str(e))


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
        # Get engine service (ADR-008 compliant)
        import os

        engine_service = get_engine_service()

        # Get reference audio path
        reference_audio_path = None
        if request.reference_audio_id:
            # Get audio file path from storage
            from ..routes.voice import _audio_storage

            if request.reference_audio_id in _audio_storage:
                reference_audio_path = _audio_storage[request.reference_audio_id]
            else:
                raise HTTPException(
                    status_code=404,
                    detail=f"Reference audio {request.reference_audio_id} not found",
                )
        elif request.profile_id:
            # Get profile reference audio
            from ..routes.profiles import _profiles

            if request.profile_id in _profiles:
                profile = _profiles[request.profile_id]
                if profile.get("reference_audio_url"):
                    # Extract file path from URL or storage
                    reference_audio_path = profile["reference_audio_url"]
                else:
                    raise HTTPException(
                        status_code=404,
                        detail=f"Profile {request.profile_id} has no reference audio",
                    )
            else:
                raise HTTPException(
                    status_code=404, detail=f"Profile {request.profile_id} not found"
                )
        else:
            raise HTTPException(
                status_code=400,
                detail="Either profile_id or reference_audio_id must be provided",
            )

        if not os.path.exists(reference_audio_path):
            raise HTTPException(
                status_code=404,
                detail=f"Reference audio file not found: {reference_audio_path}",
            )

        # Determine which engines to benchmark
        engines_to_test = request.engines or ["xtts", "chatterbox", "tortoise"]

        results = []
        successful_count = 0

        # Benchmark each engine
        for engine_name in engines_to_test:
            engine_result = BenchmarkResult(engine=engine_name, success=False)

            try:
                # Get engine instance via EngineService (ADR-008 compliant)
                engine_instance = engine_service.get_engine(engine_name.lower())
                if engine_instance is None:
                    engine_result.error = f"Engine not available: {engine_name}"
                    results.append(engine_result)
                    continue

                # Initialize engine
                import time

                init_start = time.time()
                if not engine_instance.is_initialized():
                    engine_instance.initialize()
                init_time = time.time() - init_start

                # Synthesize
                synth_start = time.time()
                if engine_name.lower() == "xtts":
                    audio, metrics = engine_instance.synthesize(
                        text=request.test_text,
                        speaker_wav=reference_audio_path,
                        language=request.language,
                        enhance_quality=request.enhance_quality,
                        calculate_quality=True,
                    )
                elif engine_name.lower() == "chatterbox":
                    audio, metrics = engine_instance.synthesize(
                        text=request.test_text,
                        reference_audio=reference_audio_path,
                        language=request.language,
                        enhance_quality=request.enhance_quality,
                        calculate_quality=True,
                    )
                elif engine_name.lower() == "tortoise":
                    audio, metrics = engine_instance.synthesize(
                        text=request.test_text,
                        speaker_wav=reference_audio_path,
                        enhance_quality=request.enhance_quality,
                        calculate_quality=True,
                    )

                synth_time = time.time() - synth_start

                # Calculate metrics if not provided
                if not metrics or not isinstance(metrics, dict):
                    import soundfile as sf

                    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
                        sf.write(tmp.name, audio, 22050)
                        tmp_path = tmp.name

                    try:
                        # Calculate metrics via EngineService
                        all_metrics = engine_service.calculate_all_metrics(
                            audio=tmp_path,
                            reference=reference_audio_path,
                        )
                        metrics = all_metrics
                    finally:
                        if os.path.exists(tmp_path):
                            os.unlink(tmp_path)

                # Build result
                engine_result.success = True
                engine_result.quality_metrics = metrics if metrics else {}
                engine_result.performance = {
                    "initialization_time": init_time,
                    "synthesis_time": synth_time,
                    "total_time": init_time + synth_time,
                }
                successful_count += 1

            except Exception as e:
                logger.error(f"Benchmark failed for {engine_name}: {e}")
                engine_result.error = str(e)

            results.append(engine_result)

        # Generate benchmark ID for tracking
        import uuid

        benchmark_id = str(uuid.uuid4())

        return BenchmarkResponse(
            results=results,
            total_engines=len(engines_to_test),
            successful_engines=successful_count,
            benchmark_id=benchmark_id,
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
        from datetime import datetime, timedelta

        # Get all quality history entries
        all_entries = []
        for _profile_id, entries in _quality_history.items():
            all_entries.extend(entries)

        # Filter by date range
        cutoff_date = datetime.utcnow() - timedelta(days=days)
        recent_entries = [
            e
            for e in all_entries
            if datetime.fromisoformat(e.timestamp.replace("Z", "+00:00")) >= cutoff_date
        ]

        # Filter by project if specified (B.1 enhancement - project_id now a first-class field)
        if project_id:
            filtered_entries = []
            for entry in recent_entries:
                # Check direct project_id field first (new entries)
                if hasattr(entry, "project_id") and entry.project_id == project_id:
                    filtered_entries.append(entry)
                # Fallback: check metadata for backward compatibility (legacy entries)
                elif hasattr(entry, "metadata") and isinstance(entry.metadata, dict):
                    if entry.metadata.get("project_id") == project_id:
                        filtered_entries.append(entry)
            # Only use filtered entries if we found matches, otherwise return empty
            # (this is stricter than before - if project_id is specified, we respect it)
            recent_entries = filtered_entries

        if not recent_entries:
            return {
                "overview": {
                    "total_samples": 0,
                    "average_mos": 0.0,
                    "average_similarity": 0.0,
                    "average_naturalness": 0.0,
                },
                "trends": {
                    "mos_trend": [],
                    "similarity_trend": [],
                    "naturalness_trend": [],
                },
                "distribution": {
                    "mos_distribution": {},
                    "quality_tiers": {"excellent": 0, "good": 0, "fair": 0, "poor": 0},
                },
                "alerts": [],
                "insights": [],
            }

        # Calculate overview metrics
        mos_scores = [
            e.metrics.get("mos_score", 0) for e in recent_entries if e.metrics.get("mos_score")
        ]
        similarity_scores = [
            e.metrics.get("similarity", 0) for e in recent_entries if e.metrics.get("similarity")
        ]
        naturalness_scores = [
            e.metrics.get("naturalness", 0) for e in recent_entries if e.metrics.get("naturalness")
        ]

        avg_mos = sum(mos_scores) / len(mos_scores) if mos_scores else 0.0
        avg_similarity = (
            sum(similarity_scores) / len(similarity_scores) if similarity_scores else 0.0
        )
        avg_naturalness = (
            sum(naturalness_scores) / len(naturalness_scores) if naturalness_scores else 0.0
        )

        # Calculate trends (daily averages)
        daily_data: dict[Any, dict[str, list[Any]]] = {}
        for entry in recent_entries:
            entry_date = datetime.fromisoformat(entry.timestamp.replace("Z", "+00:00")).date()
            if entry_date not in daily_data:
                daily_data[entry_date] = {
                    "mos": [],
                    "similarity": [],
                    "naturalness": [],
                }

            if entry.metrics.get("mos_score"):
                daily_data[entry_date]["mos"].append(entry.metrics["mos_score"])
            if entry.metrics.get("similarity"):
                daily_data[entry_date]["similarity"].append(entry.metrics["similarity"])
            if entry.metrics.get("naturalness"):
                daily_data[entry_date]["naturalness"].append(entry.metrics["naturalness"])

        # Build trend data
        sorted_dates = sorted(daily_data.keys())
        mos_trend = [
            {
                "date": str(d),
                "value": (
                    sum(daily_data[d]["mos"]) / len(daily_data[d]["mos"])
                    if daily_data[d]["mos"]
                    else 0.0
                ),
            }
            for d in sorted_dates
        ]
        similarity_trend = [
            {
                "date": str(d),
                "value": (
                    sum(daily_data[d]["similarity"]) / len(daily_data[d]["similarity"])
                    if daily_data[d]["similarity"]
                    else 0.0
                ),
            }
            for d in sorted_dates
        ]
        naturalness_trend = [
            {
                "date": str(d),
                "value": (
                    sum(daily_data[d]["naturalness"]) / len(daily_data[d]["naturalness"])
                    if daily_data[d]["naturalness"]
                    else 0.0
                ),
            }
            for d in sorted_dates
        ]

        # Calculate distribution
        mos_distribution: dict[int, int] = {}
        for score in mos_scores:
            bucket = int(score)
            mos_distribution[bucket] = mos_distribution.get(bucket, 0) + 1

        # Quality tiers
        excellent = sum(1 for s in mos_scores if s >= 4.5)
        good = sum(1 for s in mos_scores if 3.5 <= s < 4.5)
        fair = sum(1 for s in mos_scores if 2.5 <= s < 3.5)
        poor = sum(1 for s in mos_scores if s < 2.5)

        # Generate alerts
        alerts = []
        if avg_mos < 3.0:
            alerts.append(
                {
                    "type": "warning",
                    "message": f"Average MOS score ({avg_mos:.2f}) is below acceptable threshold (3.0)",
                    "severity": "high",
                }
            )
        if avg_similarity < 0.7:
            alerts.append(
                {
                    "type": "warning",
                    "message": f"Average similarity ({avg_similarity:.2f}) is below target (0.7)",
                    "severity": "medium",
                }
            )

        # Generate insights
        insights = []
        if len(recent_entries) > 10:
            insights.append(
                {
                    "type": "statistic",
                    "message": f"Analyzed {len(recent_entries)} quality samples over the last {days} days",
                }
            )
        if avg_mos >= 4.0:
            insights.append(
                {
                    "type": "positive",
                    "message": "Quality metrics are above target thresholds",
                }
            )

        return {
            "overview": {
                "total_samples": len(recent_entries),
                "average_mos": round(avg_mos, 2),
                "average_similarity": round(avg_similarity, 2),
                "average_naturalness": round(avg_naturalness, 2),
            },
            "trends": {
                "mos_trend": mos_trend,
                "similarity_trend": similarity_trend,
                "naturalness_trend": naturalness_trend,
            },
            "distribution": {
                "mos_distribution": mos_distribution,
                "quality_tiers": {
                    "excellent": excellent,
                    "good": good,
                    "fair": fair,
                    "poor": poor,
                },
            },
            "alerts": alerts,
            "insights": insights,
        }
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
            timestamp=datetime.utcnow().isoformat() + "Z",
            engine=request.engine,
            metrics=request.metrics,
            quality_score=request.quality_score,
            synthesis_text=request.synthesis_text,
            audio_url=request.audio_url,
            enhanced_quality=request.enhanced_quality,
            metadata=request.metadata,
        )

        # Store entry
        if request.profile_id not in _quality_history:
            _quality_history[request.profile_id] = []

        _quality_history[request.profile_id].append(entry)

        # Cleanup old entries periodically
        if len(_quality_history[request.profile_id]) % 100 == 0:
            _cleanup_old_history()

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
        entries = _quality_history.get(profile_id, [])

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
    Calculates trends, statistics, and identifies best/worst samples.

    Args:
        profile_id: Voice profile ID
        time_range: Time range for trends (7d, 30d, 90d, 1y, all)

    Returns:
        Quality trends and statistics for the profile
    """
    try:
        entries = _quality_history.get(profile_id, [])

        if not entries:
            return QualityTrendsResponse(
                profile_id=profile_id,
                time_range=time_range,
                trends={},
                statistics={},
                best_entry=None,
                worst_entry=None,
            )

        # Calculate days from time range
        days = {"7d": 7, "30d": 30, "90d": 90, "1y": 365, "all": 999999}.get(time_range, 30)

        # Filter entries by time range
        from datetime import datetime, timedelta

        cutoff_date = (datetime.utcnow() - timedelta(days=days)).isoformat() + "Z"
        filtered_entries = [e for e in entries if e.timestamp >= cutoff_date or time_range == "all"]

        if not filtered_entries:
            return QualityTrendsResponse(
                profile_id=profile_id,
                time_range=time_range,
                trends={},
                statistics={},
                best_entry=None,
                worst_entry=None,
            )

        # Build trends for each metric
        metrics_to_track = ["mos_score", "similarity", "naturalness", "quality_score"]
        trends: dict[str, list[dict[str, Any]]] = {}
        statistics: dict[str, dict[str, float]] = {}

        for metric in metrics_to_track:
            metric_values: list[dict[str, Any]] = []
            for entry in filtered_entries:
                # Get metric value from entry
                value = None
                if metric == "quality_score":
                    value = entry.quality_score
                elif metric in entry.metrics:
                    val = entry.metrics[metric]
                    if isinstance(val, (int, float)):
                        value = float(val)

                if value is not None:
                    metric_values.append({"timestamp": entry.timestamp, "value": value})

            # Sort by timestamp
            metric_values.sort(key=lambda x: str(x["timestamp"]))

            trends[metric] = metric_values

            # Calculate statistics
            if metric_values:
                values: list[float] = [float(v["value"]) for v in metric_values]
                avg = sum(values) / len(values)
                min_val = min(values)
                max_val = max(values)

                # Calculate trend (slope of linear regression)
                trend = 0.0
                if len(values) > 1:
                    x_mean = (len(values) - 1) / 2.0
                    y_mean = avg
                    numerator = sum((i - x_mean) * (values[i] - y_mean) for i in range(len(values)))
                    denominator = sum((i - x_mean) ** 2 for i in range(len(values)))
                    if denominator != 0:
                        trend = numerator / denominator

                statistics[metric] = {
                    "avg": avg,
                    "min": min_val,
                    "max": max_val,
                    "trend": trend,
                }

        # Find best and worst entries (by quality_score)
        best_entry = max(filtered_entries, key=lambda e: e.quality_score, default=None)
        worst_entry = min(filtered_entries, key=lambda e: e.quality_score, default=None)

        return QualityTrendsResponse(
            profile_id=profile_id,
            time_range=time_range,
            trends=trends,
            statistics=statistics,
            best_entry=best_entry,
            worst_entry=worst_entry,
        )

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
        from api.utils.text_analysis import analyze_text

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
        from api.utils.quality_recommendations import recommend_quality_settings
        from api.utils.text_analysis import analyze_text

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
        from api.utils.quality_degradation import (
            detect_quality_degradation,
        )

        # Get quality history
        entries = _quality_history.get(profile_id, [])
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


@router.get("/baseline/{profile_id}", response_model=QualityBaselineResponse | None)
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
        from api.utils.quality_degradation import calculate_quality_baseline

        # Get quality history
        entries = _quality_history.get(profile_id, [])
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
        from api.utils.quality_consistency import get_quality_consistency_monitor

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
        from api.utils.quality_consistency import get_quality_consistency_monitor

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
        from api.utils.quality_consistency import get_quality_consistency_monitor

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
        from api.utils.quality_consistency import get_quality_consistency_monitor

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
        from api.utils.quality_consistency import get_quality_consistency_monitor

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
        from api.utils.quality_visualization import calculate_quality_heatmap

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
        from api.utils.quality_visualization import calculate_quality_correlations

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
        from api.utils.quality_visualization import detect_quality_anomalies

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
        from api.utils.quality_visualization import predict_quality

        # Use provided quality data or get from consistency monitor
        quality_data = request.quality_data
        if not quality_data:
            from api.utils.quality_consistency import get_quality_consistency_monitor

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
        from api.utils.quality_visualization import generate_quality_insights

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

        from api.utils.quality_visualization import calculate_quality_heatmap

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

        from api.utils.quality_visualization import calculate_quality_correlations

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

        from api.utils.quality_visualization import detect_quality_anomalies

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

        from api.utils.quality_visualization import generate_quality_insights

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
