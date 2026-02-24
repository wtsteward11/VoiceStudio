"""
Articulation Analysis Routes

Endpoints for analyzing speech articulation patterns and identifying issues.
"""

from __future__ import annotations

import logging
import os
from typing import Any

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from ..models_additional import ArticulationAnalyzeRequest


class ArticulationIssue(BaseModel):
    """An articulation issue detected in audio."""

    t: float
    type: str
    severity: str
    message: str


class ArticulationAnalyzeResponse(BaseModel):
    """Response model for articulation analysis."""

    issues: list[ArticulationIssue]


logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/articulation", tags=["articulation"])


@router.post("/analyze", response_model=ArticulationAnalyzeResponse)
async def analyze(req: ArticulationAnalyzeRequest) -> ArticulationAnalyzeResponse:
    """
    Analyze articulation patterns in audio.

    Detects speech articulation issues such as:
    - Mispronunciations
    - Clipping/distortion
    - Timing issues
    - Formant problems

    Returns detected issues with timestamps and types.
    """
    try:
        # Get audio file path
        audio_id = req.audio_id
        if not audio_id:
            raise HTTPException(status_code=400, detail="audio_id is required")

        # Try to load audio analysis libraries
        try:
            import librosa
            import numpy as np
            import soundfile as sf
        except ImportError:
            raise HTTPException(
                status_code=503,
                detail=(
                    "Articulation analysis requires librosa and soundfile. "
                    "Install with: pip install librosa soundfile"
                ),
            )

        # Get audio file path from audio storage
        from .audio import _get_audio_path

        audio_path = _get_audio_path(audio_id)
        if not audio_path or not os.path.exists(audio_path):
            raise HTTPException(status_code=404, detail=f"Audio file not found: {audio_id}")

        # Load audio
        audio, sample_rate = sf.read(audio_path)

        # Convert to mono if needed
        if len(audio.shape) > 1:
            audio = np.mean(audio, axis=1)

        # Normalize audio
        if np.max(np.abs(audio)) > 1.0:
            audio = audio / np.max(np.abs(audio))

        # Analyze articulation patterns
        issues: list[dict[str, Any]] = []

        # 1. Detect clipping/distortion (samples at or near maximum)
        clipped_samples = np.sum(np.abs(audio) > 0.95)
        if clipped_samples > len(audio) * 0.01:  # More than 1% clipped
            issues.append(
                {
                    "t": 0.0,  # Start time
                    "type": "clipping",
                    "severity": ("high" if clipped_samples > len(audio) * 0.05 else "medium"),
                    "message": f"Audio clipping detected: {clipped_samples} samples",
                }
            )

        # 2. Detect silence regions (potential articulation gaps)
        # Use RMS energy to detect silence
        frame_length = 2048
        hop_length = 512
        rms = librosa.feature.rms(y=audio, frame_length=frame_length, hop_length=hop_length)[0]
        silence_threshold = np.percentile(rms, 10)  # Bottom 10% is considered silence

        # Find long silence regions (>0.5 seconds)
        times = librosa.frames_to_time(np.arange(len(rms)), sr=sample_rate, hop_length=hop_length)
        silence_frames = np.where(rms < silence_threshold)[0]

        if len(silence_frames) > 0:
            # Group consecutive silence frames
            silence_regions = []
            start_idx = silence_frames[0]
            for i in range(1, len(silence_frames)):
                if silence_frames[i] - silence_frames[i - 1] > 1:
                    # Gap in silence - end of region
                    duration = times[silence_frames[i - 1]] - times[start_idx]
                    if duration > 0.5:  # More than 0.5 seconds
                        silence_regions.append(
                            {
                                "start": float(times[start_idx]),
                                "duration": float(duration),
                            }
                        )
                    start_idx = silence_frames[i]

            # Check last region
            if len(silence_frames) > 1:
                duration = times[silence_frames[-1]] - times[start_idx]
                if duration > 0.5:
                    silence_regions.append(
                        {"start": float(times[start_idx]), "duration": float(duration)}
                    )

            for region in silence_regions[:3]:  # Limit to first 3
                issues.append(
                    {
                        "t": region["start"],
                        "type": "silence",
                        "severity": "medium" if region["duration"] < 1.0 else "high",
                        "message": f"Long silence detected: {region['duration']:.2f}s",
                    }
                )

        # 3. Detect potential mispronunciations using pitch tracking
        # Use integrated PitchTracker for better accuracy
        try:
            from ..audio_processing import PitchTracker

            pitch_tracker = PitchTracker()

            # Use crepe or pyin for pitch tracking
            if pitch_tracker.crepe_available:
                # crepe returns (time, frequency) tuple
                time_array, frequency_array = pitch_tracker.track_pitch_crepe(audio, sample_rate)
                f0 = frequency_array
            elif pitch_tracker.pyin_available:
                # pyin returns (time, frequency, voiced) tuple
                _time_array, frequency_array, _voiced = pitch_tracker.track_pitch_pyin(
                    audio, sample_rate, fmin=50, fmax=400
                )
                f0 = frequency_array
            else:
                # Fallback to librosa yin
                f0 = librosa.yin(audio, fmin=50, fmax=400)

            # Ensure f0 is numpy array
            if isinstance(f0, list):
                f0 = np.array(f0)
            elif not isinstance(f0, np.ndarray):
                f0 = np.array([f0]) if f0 is not None else np.array([])

            f0_clean = f0[f0 > 0] if len(f0) > 0 else np.array([])

            if len(f0_clean) > 0:
                # Detect unusual F0 variations (potential articulation issues)
                f0_std = np.std(f0_clean)
                f0_mean = np.mean(f0_clean)

                # Flag regions with extreme F0 variations
                if f0_std > f0_mean * 0.5:  # High variation
                    issues.append(
                        {
                            "t": 0.0,
                            "type": "pitch_instability",
                            "severity": "medium",
                            "message": "Unstable pitch detected (potential articulation issue)",
                        }
                    )
        except Exception as e:
            logger.debug(f"Could not analyze pitch: {e}")

        # 4. Detect distortion using spectral analysis
        try:
            # Calculate spectral flatness (high = noise-like, low = tonal)
            n_fft = 2048
            hop_length = 512

            # Spectral flatness
            spectral_flatness = librosa.feature.spectral_flatness(
                y=audio, n_fft=n_fft, hop_length=hop_length
            )[0]

            # High flatness might indicate distortion or noise
            if np.mean(spectral_flatness) > 0.5:
                issues.append(
                    {
                        "t": 0.0,
                        "type": "distortion",
                        "severity": "medium",
                        "message": "Potential audio distortion detected",
                    }
                )
        except Exception as e:
            logger.debug(f"Could not analyze spectral characteristics: {e}")

        logger.info(
            f"Articulation analysis completed for {audio_id}: " f"{len(issues)} issues found"
        )

        return ArticulationAnalyzeResponse(issues=[ArticulationIssue(**issue) for issue in issues])

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Articulation analysis failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Articulation analysis failed: {e!s}")
