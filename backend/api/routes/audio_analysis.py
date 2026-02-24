"""
Advanced Audio Analysis Routes

Endpoints for comprehensive audio analysis including spectral,
temporal, and perceptual metrics.
"""

from __future__ import annotations

import logging
import os
import time
from typing import Any

import numpy as np
from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel

from ..audio_processing import (
    AudioMetadataExtractor,
    PitchTracker,
    WaveletAnalyzer,
)
from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/audio-analysis", tags=["audio-analysis"])

# In-memory analysis results storage (replace with database in production)
_analysis_results: dict[str, dict] = {}
_analysis_timestamps: dict[str, float] = {}  # audio_id -> creation_time
_MAX_ANALYSIS_RESULTS = 500  # Maximum number of cached analysis results
_ANALYSIS_CACHE_TTL = 3600  # Cache TTL in seconds (1 hour)


def _cleanup_old_analysis_results():
    """
    Clean up old analysis results from storage to prevent memory accumulation.

    Removes:
    - Results older than ANALYSIS_CACHE_TTL
    - Results beyond MAX_ANALYSIS_RESULTS (oldest first)
    """
    current_time = time.time()
    to_remove = []

    # Find results that are too old
    for audio_id, timestamp in _analysis_timestamps.items():
        age = current_time - timestamp
        if age > _ANALYSIS_CACHE_TTL:
            to_remove.append(audio_id)

    # If storage is too large, remove oldest results
    if len(_analysis_results) > _MAX_ANALYSIS_RESULTS:
        # Sort by timestamp (oldest first)
        sorted_items = sorted(
            _analysis_timestamps.items(),
            key=lambda x: x[1],
        )
        # Remove oldest results until we're under the limit
        excess = len(_analysis_results) - _MAX_ANALYSIS_RESULTS
        for audio_id, _ in sorted_items[:excess]:
            if audio_id not in to_remove:
                to_remove.append(audio_id)

    # Remove results
    for audio_id in to_remove:
        _analysis_results.pop(audio_id, None)
        _analysis_timestamps.pop(audio_id, None)

    if to_remove:
        logger.info(f"Cleaned up {len(to_remove)} old analysis results")


class SpectralAnalysis(BaseModel):
    """Spectral analysis results."""

    centroid: float  # Spectral centroid in Hz
    rolloff: float  # Spectral rolloff in Hz
    flux: float  # Spectral flux
    zero_crossing_rate: float
    bandwidth: float  # Spectral bandwidth in Hz
    flatness: float  # Spectral flatness
    kurtosis: float  # Spectral kurtosis
    skewness: float  # Spectral skewness


class TemporalAnalysis(BaseModel):
    """Temporal analysis results."""

    rms: float  # RMS energy
    zero_crossing_rate: float
    attack_time: float | None = None  # Attack time in seconds
    decay_time: float | None = None  # Decay time in seconds
    sustain_level: float | None = None
    release_time: float | None = None  # Release time in seconds


class PerceptualAnalysis(BaseModel):
    """Perceptual analysis results."""

    loudness_lufs: float  # Integrated LUFS
    peak_lufs: float  # Peak LUFS
    true_peak_db: float  # True peak in dB
    dynamic_range: float  # Dynamic range in dB
    crest_factor: float  # Crest factor
    lra: float | None = None  # Loudness range


class AudioAnalysisResult(BaseModel):
    """Complete audio analysis result."""

    audio_id: str
    sample_rate: int
    duration: float
    channels: int
    spectral: SpectralAnalysis
    temporal: TemporalAnalysis
    perceptual: PerceptualAnalysis
    created: str  # ISO datetime string


@router.get("/{audio_id}", response_model=AudioAnalysisResult)
@cache_response(ttl=300)  # Cache for 5 minutes (analysis results are static for a given audio file)
async def get_audio_analysis(
    audio_id: str,
    include_spectral: bool = Query(True),
    include_temporal: bool = Query(True),
    include_perceptual: bool = Query(True),
):
    """Get comprehensive audio analysis for an audio file."""
    try:
        from datetime import datetime

        import numpy as np

        # Check cache first
        if audio_id in _analysis_results:
            analysis = _analysis_results[audio_id]
            # Check if cached result is still valid
            if audio_id in _analysis_timestamps:
                age = time.time() - _analysis_timestamps[audio_id]
                if age <= _ANALYSIS_CACHE_TTL:
                    # Return cached result
                    spectral_dict = analysis.get("spectral", {})
                    temporal_dict = analysis.get("temporal", {})
                    perceptual_dict = analysis.get("perceptual", {})
                    return AudioAnalysisResult(
                        audio_id=str(analysis.get("audio_id", "")),
                        sample_rate=int(analysis.get("sample_rate", 44100)),
                        duration=float(analysis.get("duration", 0.0)),
                        channels=int(analysis.get("channels", 1)),
                        spectral=SpectralAnalysis(**spectral_dict),
                        temporal=TemporalAnalysis(**temporal_dict),
                        perceptual=PerceptualAnalysis(**perceptual_dict),
                        created=str(analysis.get("created", "")),
                    )
                else:
                    # Result expired, remove it
                    del _analysis_results[audio_id]
                    del _analysis_timestamps[audio_id]

        # Get audio file path
        from .audio import _get_audio_path

        audio_path = _get_audio_path(audio_id)
        if not audio_path or not os.path.exists(audio_path):
            raise HTTPException(status_code=404, detail=f"Audio file not found: {audio_id}")

        # Try to load audio analysis libraries
        try:
            import librosa
            import soundfile as sf

            HAS_AUDIO_LIBS = True
        except ImportError:
            HAS_AUDIO_LIBS = False
            logger.warning("librosa/soundfile not available. Using fallback analysis.")

        if HAS_AUDIO_LIBS:
            # Load audio file
            audio, sample_rate = sf.read(audio_path)
            duration = len(audio) / sample_rate

            # Convert to mono if stereo
            if len(audio.shape) > 1:
                channels = audio.shape[1]
                audio_mono = np.mean(audio, axis=1)
            else:
                channels = 1
                audio_mono = audio

            # Ensure audio is float32 and normalized
            if audio_mono.dtype != np.float32:
                audio_mono = audio_mono.astype(np.float32)
            if np.max(np.abs(audio_mono)) > 1.0:
                audio_mono = audio_mono / np.max(np.abs(audio_mono))

            # Compute spectral analysis
            spectral_dict = {}
            if include_spectral:
                # Compute STFT
                n_fft = 2048
                hop_length = 512
                stft = librosa.stft(audio_mono, n_fft=n_fft, hop_length=hop_length)
                magnitude = np.abs(stft)

                # Spectral centroid
                spectral_centroid = float(
                    np.mean(
                        librosa.feature.spectral_centroid(
                            y=audio_mono,
                            sr=sample_rate,
                            n_fft=n_fft,
                            hop_length=hop_length,
                        )
                    )
                )

                # Spectral rolloff
                spectral_rolloff = float(
                    np.mean(
                        librosa.feature.spectral_rolloff(
                            y=audio_mono,
                            sr=sample_rate,
                            n_fft=n_fft,
                            hop_length=hop_length,
                        )
                    )
                )

                # Spectral flux (rate of change of spectral magnitude)
                spectral_flux = float(np.mean(np.diff(magnitude, axis=1)))

                # Zero crossing rate (spectral)
                zcr_spectral = float(np.mean(librosa.feature.zero_crossing_rate(audio_mono)))

                # Spectral bandwidth
                spectral_bandwidth = float(
                    np.mean(
                        librosa.feature.spectral_bandwidth(
                            y=audio_mono,
                            sr=sample_rate,
                            n_fft=n_fft,
                            hop_length=hop_length,
                        )
                    )
                )

                # Spectral flatness
                spectral_flatness = float(
                    np.mean(
                        librosa.feature.spectral_flatness(
                            y=audio_mono, n_fft=n_fft, hop_length=hop_length
                        )
                    )
                )

                # Spectral kurtosis and skewness (from magnitude distribution)
                magnitude_flat = magnitude.flatten()
                spectral_kurtosis = float(
                    np.mean((magnitude_flat - np.mean(magnitude_flat)) ** 4)
                    / (np.std(magnitude_flat) ** 4)
                )
                spectral_skewness = float(
                    np.mean((magnitude_flat - np.mean(magnitude_flat)) ** 3)
                    / (np.std(magnitude_flat) ** 3)
                )

                spectral_dict = {
                    "centroid": spectral_centroid,
                    "rolloff": spectral_rolloff,
                    "flux": abs(spectral_flux),
                    "zero_crossing_rate": zcr_spectral,
                    "bandwidth": spectral_bandwidth,
                    "flatness": spectral_flatness,
                    "kurtosis": spectral_kurtosis,
                    "skewness": spectral_skewness,
                }
            else:
                spectral_dict = {
                    "centroid": 0.0,
                    "rolloff": 0.0,
                    "flux": 0.0,
                    "zero_crossing_rate": 0.0,
                    "bandwidth": 0.0,
                    "flatness": 0.0,
                    "kurtosis": 0.0,
                    "skewness": 0.0,
                }

            # Compute temporal analysis
            temporal_dict = {}
            if include_temporal:
                # RMS energy
                rms = float(np.mean(librosa.feature.rms(y=audio_mono)[0]))

                # Zero crossing rate (temporal)
                zcr_temporal = float(np.mean(librosa.feature.zero_crossing_rate(audio_mono)[0]))

                # Envelope analysis for ADSR
                from scipy.signal import hilbert

                envelope = np.abs(hilbert(audio_mono))
                envelope_norm = envelope / np.max(envelope) if np.max(envelope) > 0 else envelope

                # Simple ADSR estimation
                attack_samples = int(0.01 * sample_rate)  # 10ms attack
                decay_samples = int(0.1 * sample_rate)  # 100ms decay
                release_samples = int(0.2 * sample_rate)  # 200ms release

                attack_time = (
                    attack_samples / sample_rate if attack_samples < len(envelope_norm) else None
                )
                decay_time = (
                    decay_samples / sample_rate if decay_samples < len(envelope_norm) else None
                )
                sustain_level = (
                    float(
                        np.mean(
                            envelope_norm[len(envelope_norm) // 4 : 3 * len(envelope_norm) // 4]
                        )
                    )
                    if len(envelope_norm) > 0
                    else None
                )
                release_time = (
                    release_samples / sample_rate if release_samples < len(envelope_norm) else None
                )

                temporal_dict = {
                    "rms": rms,
                    "zero_crossing_rate": zcr_temporal,
                    "attack_time": attack_time,
                    "decay_time": decay_time,
                    "sustain_level": sustain_level,
                    "release_time": release_time,
                }
            else:
                temporal_dict = {
                    "rms": 0.0,
                    "zero_crossing_rate": 0.0,
                    "attack_time": None,
                    "decay_time": None,
                    "sustain_level": None,
                    "release_time": None,
                }

            # Compute perceptual analysis
            perceptual_dict = {}
            if include_perceptual:
                # Get RMS from temporal analysis if available
                rms_value = temporal_dict.get("rms", 0.5) if include_temporal else 0.5

                # Simple loudness estimation (LUFS approximation)
                # In production, use pyloudnorm or similar for accurate LUFS
                rms_db = 20 * np.log10(rms_value + 1e-10)  # Avoid log(0)
                loudness_lufs = rms_db - 23.0  # Rough approximation

                # Peak level
                peak = float(np.max(np.abs(audio_mono)))
                peak_db = 20 * np.log10(peak + 1e-10)
                peak_lufs = peak_db - 23.0

                # True peak (oversampled peak)
                true_peak = peak  # Simplified - would need oversampling for true peak
                true_peak_db = 20 * np.log10(true_peak + 1e-10)

                # Dynamic range
                min_level = (
                    float(np.min(np.abs(audio_mono[audio_mono != 0])))
                    if np.any(audio_mono != 0)
                    else 1e-10
                )
                min_db = 20 * np.log10(min_level + 1e-10)
                dynamic_range = peak_db - min_db

                # Crest factor (peak to RMS ratio)
                crest_factor = peak_db - rms_db if rms_db > -np.inf else 0.0

                # Loudness range (LRA) - simplified
                lra = dynamic_range * 0.6  # Rough approximation

                perceptual_dict = {
                    "loudness_lufs": float(loudness_lufs),
                    "peak_lufs": float(peak_lufs),
                    "true_peak_db": float(true_peak_db),
                    "dynamic_range": float(dynamic_range),
                    "crest_factor": float(crest_factor),
                    "lra": float(lra),
                }
            else:
                perceptual_dict = {
                    "loudness_lufs": 0.0,
                    "peak_lufs": 0.0,
                    "true_peak_db": 0.0,
                    "dynamic_range": 0.0,
                    "crest_factor": 0.0,
                    "lra": None,
                }

            # Store analysis result
            analysis = {
                "audio_id": audio_id,
                "sample_rate": sample_rate,
                "duration": duration,
                "channels": channels,
                "spectral": spectral_dict,
                "temporal": temporal_dict,
                "perceptual": perceptual_dict,
                "created": datetime.utcnow().isoformat(),
            }
            _analysis_results[audio_id] = analysis
            _analysis_timestamps[audio_id] = time.time()

            # Clean up old results if needed
            if len(_analysis_results) > _MAX_ANALYSIS_RESULTS:
                _cleanup_old_analysis_results()
        else:
            # Required libraries not available - return proper error
            logger.error(
                f"Audio analysis libraries not available for {audio_id}. librosa and soundfile required."
            )
            raise HTTPException(
                status_code=503,
                detail=(
                    "Audio analysis requires librosa and soundfile libraries. "
                    "Please install with: pip install librosa soundfile"
                ),
            )

        spectral_dict = analysis.get("spectral", {})
        temporal_dict = analysis.get("temporal", {})
        perceptual_dict = analysis.get("perceptual", {})

        return AudioAnalysisResult(
            audio_id=str(analysis.get("audio_id", "")),
            sample_rate=int(analysis.get("sample_rate", 44100)),
            duration=float(analysis.get("duration", 0.0)),
            channels=int(analysis.get("channels", 1)),
            spectral=SpectralAnalysis(**spectral_dict),
            temporal=TemporalAnalysis(**temporal_dict),
            perceptual=PerceptualAnalysis(**perceptual_dict),
            created=str(analysis.get("created", "")),
        )
    except Exception as e:
        logger.error(f"Failed to analyze audio: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/{audio_id}/analyze")
async def analyze_audio(audio_id: str):
    """Trigger analysis for an audio file."""
    import asyncio
    import uuid

    # Validate audio exists
    from .audio import _get_audio_path

    audio_path = _get_audio_path(audio_id)
    if not audio_path or not os.path.exists(audio_path):
        raise HTTPException(status_code=404, detail=f"Audio file not found: {audio_id}")

    # Create analysis job
    job_id = f"analysis_{uuid.uuid4().hex[:8]}"

    # Start analysis in background
    async def _process_analysis():
        try:
            # Get analysis result (this will compute and cache it)
            await get_audio_analysis(audio_id)
            # Result is automatically cached by get_audio_analysis
            logger.info(f"Audio analysis completed for {audio_id}, job {job_id}")
        except Exception as e:
            logger.error(f"Audio analysis failed for {audio_id}: {e}")

    asyncio.create_task(_process_analysis())

    return {
        "job_id": job_id,
        "audio_id": audio_id,
        "status": "queued",
        "message": "Analysis job queued for processing",
    }


@router.get("/{audio_id}/compare")
@cache_response(
    ttl=300
)  # Cache for 5 minutes (comparison results are static for given audio files)
async def compare_audio_analysis(audio_id: str, reference_audio_id: str):
    """Compare analysis results between two audio files."""
    # Get analysis results for both audio files
    analysis1 = await get_audio_analysis(audio_id)
    analysis2 = await get_audio_analysis(reference_audio_id)

    # Compute differences
    spectral_diff = {
        "centroid": abs(analysis1.spectral.centroid - analysis2.spectral.centroid),
        "rolloff": abs(analysis1.spectral.rolloff - analysis2.spectral.rolloff),
        "flux": abs(analysis1.spectral.flux - analysis2.spectral.flux),
        "bandwidth": abs(analysis1.spectral.bandwidth - analysis2.spectral.bandwidth),
    }

    temporal_diff = {
        "rms": abs(analysis1.temporal.rms - analysis2.temporal.rms),
        "zero_crossing_rate": abs(
            analysis1.temporal.zero_crossing_rate - analysis2.temporal.zero_crossing_rate
        ),
    }

    perceptual_diff = {
        "loudness_lufs": abs(
            analysis1.perceptual.loudness_lufs - analysis2.perceptual.loudness_lufs
        ),
        "dynamic_range": abs(
            analysis1.perceptual.dynamic_range - analysis2.perceptual.dynamic_range
        ),
        "crest_factor": abs(analysis1.perceptual.crest_factor - analysis2.perceptual.crest_factor),
    }

    # Calculate overall similarity (0.0 = identical, 1.0 = completely different)
    # Normalize differences
    normalized_spectral = sum(spectral_diff.values()) / (
        len(spectral_diff) * 1000.0
    )  # Rough normalization
    normalized_temporal = sum(temporal_diff.values()) / (len(temporal_diff) * 1.0)
    normalized_perceptual = sum(perceptual_diff.values()) / (len(perceptual_diff) * 20.0)

    overall_similarity = 1.0 - min(
        1.0, (normalized_spectral + normalized_temporal + normalized_perceptual) / 3.0
    )

    return {
        "audio_id_1": audio_id,
        "audio_id_2": reference_audio_id,
        "spectral_differences": spectral_diff,
        "temporal_differences": temporal_diff,
        "perceptual_differences": perceptual_diff,
        "overall_similarity": round(overall_similarity, 3),
        "summary": {
            "most_different_metric": (
                max(
                    [
                        (k, v)
                        for k, v in {
                            **spectral_diff,
                            **temporal_diff,
                            **perceptual_diff,
                        }.items()
                    ],
                    key=lambda x: x[1],
                )[0]
                if spectral_diff or temporal_diff or perceptual_diff
                else None
            ),
            "similarity_score": round(overall_similarity, 3),
        },
    }


# New endpoints for free library integrations


class PitchAnalysisResult(BaseModel):
    """Pitch analysis result."""

    times: list[float]
    frequencies: list[float]
    statistics: dict[str, float]
    method: str  # "crepe" or "pyin"


@router.get("/{audio_id}/pitch")
@cache_response(ttl=300)
async def get_pitch_analysis(
    audio_id: str,
    method: str = Query("crepe", description="Pitch tracking method: crepe or pyin"),
):
    """Get pitch analysis for an audio file."""
    from .audio import _get_audio_path

    audio_path = _get_audio_path(audio_id)
    if not audio_path or not os.path.exists(audio_path):
        raise HTTPException(status_code=404, detail=f"Audio file not found: {audio_id}")

    try:
        import soundfile as sf

        # Load audio
        audio, sample_rate = sf.read(audio_path)

        # Convert to mono if stereo
        if len(audio.shape) > 1:
            audio = np.mean(audio, axis=1)

        # Initialize pitch tracker
        pitch_tracker = PitchTracker()

        if method.lower() == "crepe" and pitch_tracker.crepe_available:
            times, frequencies = pitch_tracker.track_pitch_crepe(audio, sample_rate)
            stats = pitch_tracker.get_pitch_statistics(frequencies, times)
            return PitchAnalysisResult(
                times=times.tolist(),
                frequencies=frequencies.tolist(),
                statistics=stats,
                method="crepe",
            )
        elif method.lower() == "pyin" and pitch_tracker.pyin_available:
            f0, voiced_flag, _voiced_prob = pitch_tracker.track_pitch_pyin(audio, sample_rate)
            # Convert to times array
            hop_length = 512
            times = np.arange(len(f0)) * hop_length / sample_rate
            # Filter out unvoiced frames
            valid_f0 = f0[voiced_flag]
            valid_times = times[voiced_flag]
            stats = pitch_tracker.get_pitch_statistics(valid_f0, valid_times)
            return PitchAnalysisResult(
                times=valid_times.tolist(),
                frequencies=valid_f0.tolist(),
                statistics=stats,
                method="pyin",
            )
        else:
            raise HTTPException(
                status_code=400,
                detail=f"Method '{method}' not available. Available: "
                f"{'crepe' if pitch_tracker.crepe_available else ''} "
                f"{'pyin' if pitch_tracker.pyin_available else ''}",
            )
    except Exception as e:
        logger.error(f"Error in pitch analysis: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to analyze pitch: {e!s}")


@router.get("/{audio_id}/metadata")
@cache_response(ttl=600)
async def get_audio_metadata(audio_id: str):
    """Get metadata for an audio file."""
    from .audio import _get_audio_path

    audio_path = _get_audio_path(audio_id)
    if not audio_path or not os.path.exists(audio_path):
        raise HTTPException(status_code=404, detail=f"Audio file not found: {audio_id}")

    try:
        extractor = AudioMetadataExtractor()
        metadata = extractor.extract_metadata(audio_path)
        return metadata
    except Exception as e:
        logger.error(f"Error extracting metadata: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to extract metadata: {e!s}")


class WaveletAnalysisResult(BaseModel):
    """Wavelet analysis result."""

    features: dict[str, Any]
    num_levels: int
    wavelet: str


@router.get("/{audio_id}/wavelet")
@cache_response(ttl=300)
async def get_wavelet_analysis(
    audio_id: str,
    wavelet: str = Query("db4", description="Wavelet name (e.g., db4, haar)"),
):
    """Get wavelet analysis for an audio file."""
    from .audio import _get_audio_path

    audio_path = _get_audio_path(audio_id)
    if not audio_path or not os.path.exists(audio_path):
        raise HTTPException(status_code=404, detail=f"Audio file not found: {audio_id}")

    try:
        import soundfile as sf

        # Load audio
        audio, _sample_rate = sf.read(audio_path)

        # Convert to mono if stereo
        if len(audio.shape) > 1:
            audio = np.mean(audio, axis=1)

        # Initialize wavelet analyzer
        analyzer = WaveletAnalyzer()

        # Check if wavelet is available
        available_wavelets = analyzer.get_available_wavelets()
        if wavelet not in available_wavelets:
            raise HTTPException(
                status_code=400,
                detail=f"Wavelet '{wavelet}' not available. "
                f"Available: {', '.join(available_wavelets[:10])}",
            )

        # Extract features
        features = analyzer.get_wavelet_features(audio, wavelet=wavelet)

        return WaveletAnalysisResult(
            features=features,
            num_levels=features["num_levels"],
            wavelet=wavelet,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error in wavelet analysis: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to analyze wavelet: {e!s}")
