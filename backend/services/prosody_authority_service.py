"""
Canonical prosody transform authority (GAP-023).

Single decision + execution path for pitch/rate/volume on numpy waveforms.
Routes delegate; no silent skip of requested DSP.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass

import numpy as np

from backend.audio.audio_utils import pitch_shift_audio, time_stretch_audio

logger = logging.getLogger(__name__)


class ProsodyAuthorityError(Exception):
    """Transform could not be applied honestly."""

    def __init__(self, *, status_code: int, message: str) -> None:
        super().__init__(message)
        self.status_code = status_code
        self.message = message


@dataclass
class ProsodyTransformResult:
    """Output of a prosody transform."""

    audio: np.ndarray
    diagnostics: dict[str, object]


def _apply_volume(audio: np.ndarray, volume: float) -> np.ndarray:
    if volume == 1.0:
        return audio
    out = audio * volume
    max_val = float(np.max(np.abs(out)))
    if max_val > 1.0:
        out = out / max_val
    return out


def apply_transform(
    audio: np.ndarray,
    sample_rate: int,
    *,
    pitch: float = 1.0,
    rate: float = 1.0,
    volume: float = 1.0,
    context: str = "prosody",
) -> ProsodyTransformResult:
    """
    Apply pitch (multiplier), rate, and volume to a mono/stereo float waveform.

    Pitch mapping matches legacy prosody route: semitones = 12 * (pitch - 1.0).

    Raises:
        ProsodyAuthorityError: 503 if DSP deps missing for requested op; 500 on DSP failure.
    """
    if sample_rate <= 0:
        raise ProsodyAuthorityError(status_code=400, message="Invalid sample_rate")

    work = np.asarray(audio, dtype=np.float64)
    applied: list[str] = []
    skipped: list[dict[str, str]] = []
    warnings: list[str] = []
    errors: list[str] = []

    want_pitch = pitch != 1.0
    want_rate = rate != 1.0
    want_volume = volume != 1.0

    if not want_pitch and not want_rate and not want_volume:
        return ProsodyTransformResult(
            audio=work.copy(),
            diagnostics={
                "action": "none",
                "applied_operations": [],
                "skipped_operations": [
                    {"operation": "all", "reason": "identity_request"},
                ],
                "warnings": [],
                "errors": [],
                "pitch_factor": pitch,
                "rate_factor": rate,
                "volume_factor": volume,
                "context": context,
            },
        )

    if want_pitch:
        semitones = 12.0 * (pitch - 1.0)
        try:
            work = pitch_shift_audio(work, sample_rate, semitones)
            applied.append("pitch_shift")
            logger.info(
                "[%s] Applied pitch_shift semitones=%.4f (pitch_factor=%.4f)",
                context,
                semitones,
                pitch,
            )
        except ImportError as e:
            raise ProsodyAuthorityError(
                status_code=503,
                message=(
                    "Pitch modification requires librosa (and optionally pyrubberband). "
                    f"ImportError: {e!s}"
                ),
            ) from e
        except Exception as e:
            logger.exception("[%s] Pitch shift failed", context)
            raise ProsodyAuthorityError(
                status_code=500,
                message=f"Pitch shift failed: {e!s}",
            ) from e

    if want_rate:
        try:
            work = time_stretch_audio(
                work, sample_rate, rate=rate, preserve_pitch=True
            )
            applied.append("time_stretch")
            logger.info("[%s] Applied time_stretch rate=%.4f", context, rate)
        except ImportError as e:
            raise ProsodyAuthorityError(
                status_code=503,
                message=(
                    "Rate modification requires librosa (and optionally pyrubberband). "
                    f"ImportError: {e!s}"
                ),
            ) from e
        except Exception as e:
            logger.exception("[%s] Time stretch failed", context)
            raise ProsodyAuthorityError(
                status_code=500,
                message=f"Rate modification failed: {e!s}",
            ) from e

    if want_volume:
        work = _apply_volume(work, volume)
        applied.append("gain")
        logger.info("[%s] Applied gain volume=%.4f", context, volume)

    action = "applied" if applied else "none"

    return ProsodyTransformResult(
        audio=work,
        diagnostics={
            "action": action,
            "applied_operations": applied,
            "skipped_operations": skipped,
            "warnings": warnings,
            "errors": errors,
            "pitch_factor": pitch,
            "rate_factor": rate,
            "volume_factor": volume,
            "context": context,
        },
    )


def prosody_control_request_factors(
    pitch_contour: list[float] | None,
    rhythm_adjustments: dict[str, float] | None,
) -> tuple[float, float]:
    """Map prosody-control request fields to pitch/rate multipliers."""
    pitch_f = 1.0
    if pitch_contour:
        if len(pitch_contour) == 0:
            pitch_f = 1.0
        else:
            pitch_f = float(sum(pitch_contour) / len(pitch_contour))
    rate_f = 1.0
    if rhythm_adjustments:
        rate_f = float(
            rhythm_adjustments.get("rate", rhythm_adjustments.get("tempo", 1.0))
        )
    return pitch_f, rate_f
