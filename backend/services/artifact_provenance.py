"""
Provenance and usage recording for audio artifact producers.

Trust audit: All routes that call _register_audio_file must also record
provenance and usage. Use record_artifact_provenance_and_usage after
_register_audio_file.

Policy (strict vs best-effort) is centralized in backend.services.provenance_policy.
"""

from __future__ import annotations

import logging
import os

from backend.services.provenance_policy import POLICY, ProvenancePolicy

logger = logging.getLogger(__name__)


def _get_wav_duration_seconds(path: str) -> float | None:
    """Get duration of WAV file in seconds."""
    try:
        import wave

        with wave.open(path, "rb") as wav_file:
            frames = wav_file.getnframes()
            sample_rate = wav_file.getframerate()
            if sample_rate:
                return frames / float(sample_rate)
    except Exception as e:
        logger.debug("Duration check failed for %s: %s", path, e)
    return None


def record_artifact_provenance_and_usage(
    output_path: str,
    model_used: str,
    duration_seconds: float | None = None,
    transformation_meta: dict | None = None,
) -> None:
    """
    Write provenance sidecar and record usage for an audio artifact.

    Call after _register_audio_file. If duration_seconds is not provided,
    attempts to read from the output file (WAV).

    Policy: If POLICY is STRICT, failures re-raise. If BEST_EFFORT, log and continue.
    """
    _do_provenance(output_path, model_used, transformation_meta)
    _do_usage(output_path, model_used, duration_seconds)


def _do_provenance(
    output_path: str,
    model_used: str,
    transformation_meta: dict | None = None,
) -> None:
    """Write provenance sidecar. Respects POLICY for failure handling."""
    try:
        from backend.services.security_service import write_provenance_sidecar

        if output_path and os.path.exists(output_path):
            tm = transformation_meta or {}
            write_provenance_sidecar(
                output_base_path=output_path,
                model_used=model_used,
                is_transformed=bool(tm.get("is_transformed", False)),
                transformation_type=tm.get("transformation_type"),
                source_reference_id=tm.get("source_reference_id"),
                watermark_applied=bool(tm.get("watermark_applied", False)),
                watermark_method=tm.get("watermark_method"),
            )
    except Exception as e:
        if POLICY == ProvenancePolicy.STRICT:
            raise
        logger.warning("Provenance write failed (%s): %s", model_used, e)


def _do_usage(
    output_path: str, model_used: str, duration_seconds: float | None
) -> None:
    """Record usage stats. Respects POLICY for failure handling."""
    try:
        from backend.services.usage_stats import record_synthesis_minutes

        duration = duration_seconds
        if duration is None:
            duration = _get_wav_duration_seconds(output_path)
        if duration and duration > 0:
            record_synthesis_minutes(duration / 60.0)
    except Exception as e:
        if POLICY == ProvenancePolicy.STRICT:
            raise
        logger.warning("Usage stats increment failed (%s): %s", model_used, e)
