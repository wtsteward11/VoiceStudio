"""
Quality benchmarking service (IDEA 52).

Runs quality benchmarks across multiple engines.
Service layer must not depend on API layer.
"""

from __future__ import annotations

import logging
import os
import tempfile
import time
import uuid
from dataclasses import dataclass
from typing import Any

from backend.ml.models.engine_service import get_engine_service

logger = logging.getLogger(__name__)


class BenchmarkReferenceNotFoundError(Exception):
    """Raised when profile_id or reference_audio_id cannot be resolved to a file path."""

    def __init__(self, message: str, status_code: int = 404):
        super().__init__(message)
        self.message = message
        self.status_code = status_code


def resolve_benchmark_reference(
    profile_id: str | None,
    reference_audio_id: str | None,
) -> str:
    """
    Resolve profile_id or reference_audio_id to an absolute file path.

    Args:
        profile_id: Optional profile ID (lookup via reference_audio_url)
        reference_audio_id: Optional audio ID (resolved via path resolver)

    Returns:
        Absolute path to reference audio file

    Raises:
        BenchmarkReferenceNotFoundError: If resolution fails (404) or neither provided (400)
    """
    if reference_audio_id:
        from backend.services.audio_path_resolver import resolve_audio_path

        path = resolve_audio_path(reference_audio_id)
        if not path:
            raise BenchmarkReferenceNotFoundError(
                f"Reference audio {reference_audio_id} not found",
                status_code=404,
            )
    elif profile_id:
        from backend.services.profile_search_service import get_profiles_proxy

        profiles = get_profiles_proxy()
        if profile_id not in profiles:
            raise BenchmarkReferenceNotFoundError(
                f"Profile {profile_id} not found",
                status_code=404,
            )
        profile = profiles[profile_id]
        path = profile.get("reference_audio_url")
        if not path:
            raise BenchmarkReferenceNotFoundError(
                f"Profile {profile_id} has no reference audio",
                status_code=404,
            )
    else:
        raise BenchmarkReferenceNotFoundError(
            "Either profile_id or reference_audio_id must be provided",
            status_code=400,
        )

    if not os.path.exists(path):
        raise BenchmarkReferenceNotFoundError(
            f"Reference audio file not found: {path}",
            status_code=404,
        )

    return path


@dataclass
class BenchmarkEngineResult:
    """Result for a single engine benchmark."""

    engine: str
    success: bool
    error: str | None
    quality_metrics: dict[str, Any]
    performance: dict[str, Any]


def run_benchmark(
    reference_audio_path: str,
    test_text: str,
    language: str = "en",
    engines: list[str] | None = None,
    enhance_quality: bool = True,
) -> dict[str, Any]:
    """
    Run quality benchmark across engines.

    Args:
        reference_audio_path: Path to reference audio file
        test_text: Text to synthesize
        language: Language code
        engines: List of engine names (default: xtts, chatterbox, tortoise)
        enhance_quality: Whether to enhance quality

    Returns:
        Dict with results, total_engines, successful_engines, benchmark_id
    """
    engine_service = get_engine_service()
    engines_to_test = engines or ["xtts", "chatterbox", "tortoise"]

    results: list[BenchmarkEngineResult] = []
    successful_count = 0

    for engine_name in engines_to_test:
        engine_result = BenchmarkEngineResult(
            engine=engine_name,
            success=False,
            error=None,
            quality_metrics={},
            performance={},
        )

        try:
            engine_instance = engine_service.get_engine(engine_name.lower())
            if engine_instance is None:
                engine_result.error = f"Engine not available: {engine_name}"
                results.append(engine_result)
                continue

            init_start = time.time()
            if not engine_instance.is_initialized():
                engine_instance.initialize()
            init_time = time.time() - init_start

            synth_start = time.time()
            if engine_name.lower() == "xtts":
                audio, metrics = engine_instance.synthesize(
                    text=test_text,
                    speaker_wav=reference_audio_path,
                    language=language,
                    enhance_quality=enhance_quality,
                    calculate_quality=True,
                )
            elif engine_name.lower() == "chatterbox":
                audio, metrics = engine_instance.synthesize(
                    text=test_text,
                    reference_audio=reference_audio_path,
                    language=language,
                    enhance_quality=enhance_quality,
                    calculate_quality=True,
                )
            elif engine_name.lower() == "tortoise":
                audio, metrics = engine_instance.synthesize(
                    text=test_text,
                    speaker_wav=reference_audio_path,
                    enhance_quality=enhance_quality,
                    calculate_quality=True,
                )
            else:
                audio, metrics = engine_instance.synthesize(
                    text=test_text,
                    speaker_wav=reference_audio_path,
                    language=language,
                    enhance_quality=enhance_quality,
                    calculate_quality=True,
                )

            synth_time = time.time() - synth_start

            if not metrics or not isinstance(metrics, dict):
                from backend.services.audio_artifacts.use_cases import wav_array_to_bytes

                wav_bytes = wav_array_to_bytes(audio, 22050)
                with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
                    tmp.write(wav_bytes)
                    tmp_path = tmp.name

                try:
                    all_metrics = engine_service.calculate_all_metrics(
                        audio=tmp_path,
                        reference=reference_audio_path,
                    )
                    metrics = all_metrics
                finally:
                    if os.path.exists(tmp_path):
                        os.unlink(tmp_path)

            engine_result.success = True
            engine_result.quality_metrics = metrics if metrics else {}
            engine_result.performance = {
                "initialization_time": init_time,
                "synthesis_time": synth_time,
                "total_time": init_time + synth_time,
            }
            successful_count += 1

        except Exception as e:
            logger.error("Benchmark failed for %s: %s", engine_name, e)
            engine_result.error = str(e)

        results.append(engine_result)

    benchmark_id = str(uuid.uuid4())

    return {
        "results": [
            {
                "engine": r.engine,
                "success": r.success,
                "error": r.error,
                "quality_metrics": r.quality_metrics,
                "performance": r.performance,
            }
            for r in results
        ],
        "total_engines": len(engines_to_test),
        "successful_engines": successful_count,
        "benchmark_id": benchmark_id,
    }
