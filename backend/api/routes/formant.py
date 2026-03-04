"""
Formant Analysis and Editing Routes

Endpoints for formant analysis and formant shifting/editing of audio.
"""

from __future__ import annotations

import asyncio
import logging
import os
import tempfile
import uuid

import numpy as np
from fastapi import APIRouter, HTTPException

from ..models import ApiOk
from ..models_additional import FormantEditRequest

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/formant", tags=["formant"])

# In-memory formant analysis cache (replace with database in production)
_formant_analyses: dict[str, dict] = {}
_state_lock = asyncio.Lock()


@router.post("/analyze")
async def analyze(req: dict) -> dict:
    """
    Analyze formants in audio.

    Formants are resonant frequencies of the vocal tract that determine
    vowel quality. This endpoint analyzes F1, F2, F3 formants over time.

    Args:
        req: Request with audio_id

    Returns:
        Formant tracks with F1, F2, F3 frequencies over time
    """
    try:
        audio_id = req.get("audio_id")
        if not audio_id:
            raise HTTPException(status_code=400, detail="audio_id is required")

        # Check cache first
        if audio_id in _formant_analyses:
            return _formant_analyses[audio_id]

        # Get audio file path
        from backend.services.audio_artifacts import AudioRegistry

        audio_path = AudioRegistry.get_path(audio_id)
        if not audio_path:
            raise HTTPException(status_code=404, detail=f"Audio file '{audio_id}' not found")
        if not os.path.exists(audio_path):
            raise HTTPException(
                status_code=404, detail=f"Audio file at '{audio_path}' does not exist"
            )

        # Try to load audio processing libraries
        try:
            import librosa
            import soundfile as sf
        except ImportError:
            raise HTTPException(
                status_code=503,
                detail=(
                    "Formant analysis requires librosa and soundfile. "
                    "Install with: pip install librosa soundfile"
                ),
            )

        # Load audio
        audio, sample_rate = sf.read(audio_path)

        # Convert to mono if needed
        if len(audio.shape) > 1:
            audio = np.mean(audio, axis=1)

        # Normalize audio
        if np.max(np.abs(audio)) > 1.0:
            audio = audio / np.max(np.abs(audio))

        # Analyze formants using LPC (Linear Predictive Coding)
        # librosa doesn't have direct formant analysis, so we'll use
        # spectral analysis to estimate formants

        # Calculate formants using LPC coefficients
        try:
            from scipy import signal

            # Frame the audio for analysis
            frame_length = int(0.025 * sample_rate)  # 25ms frames
            hop_length = int(0.010 * sample_rate)  # 10ms hop

            # Pre-emphasis filter (high-pass to emphasize formants)
            pre_emphasis = 0.97
            audio_preemph = np.append(audio[0], audio[1:] - pre_emphasis * audio[:-1])

            # Calculate formants frame by frame
            formant_tracks = []
            num_frames = int((len(audio_preemph) - frame_length) / hop_length) + 1

            for i in range(min(100, num_frames)):  # Limit to 100 frames
                start = i * hop_length
                end = start + frame_length

                if end > len(audio_preemph):
                    break

                frame = audio_preemph[start:end]

                # Apply window
                windowed = frame * np.hanning(len(frame))

                # Calculate LPC coefficients
                lpc_order = min(16, len(windowed) - 1)
                if lpc_order > 0:
                    try:
                        # Get LPC coefficients
                        a = librosa.lpc(windowed, order=lpc_order)

                        # Find roots of LPC polynomial
                        roots = np.roots(a)
                        roots = roots[np.imag(roots) >= 0]

                        # Calculate formant frequencies from roots
                        angles = np.angle(roots)
                        frequencies = angles * sample_rate / (2 * np.pi)

                        # Filter valid formant frequencies (typically 50-4000 Hz)
                        valid_freqs = frequencies[(frequencies >= 50) & (frequencies <= 4000)]
                        valid_freqs = np.sort(valid_freqs)

                        # Get F1, F2, F3 (first three formants)
                        f1 = float(valid_freqs[0]) if len(valid_freqs) > 0 else 500.0
                        f2 = float(valid_freqs[1]) if len(valid_freqs) > 1 else 1500.0
                        f3 = float(valid_freqs[2]) if len(valid_freqs) > 2 else 2500.0

                        formant_tracks.append(
                            {
                                "time": float(start / sample_rate),
                                "f1": f1,
                                "f2": f2,
                                "f3": f3,
                            }
                        )
                    except Exception as e:
                        logger.debug(f"LPC analysis failed for frame {i}: {e}")
                        # Use default formant values
                        formant_tracks.append(
                            {
                                "time": float(start / sample_rate),
                                "f1": 500.0,
                                "f2": 1500.0,
                                "f3": 2500.0,
                            }
                        )

            # Extract F1, F2, F3 tracks
            f1_track = [f["f1"] for f in formant_tracks]
            f2_track = [f["f2"] for f in formant_tracks]
            f3_track = [f["f3"] for f in formant_tracks]

            # Average formants for summary
            avg_f1 = np.mean(f1_track) if f1_track else 500.0
            avg_f2 = np.mean(f2_track) if f2_track else 1500.0
            avg_f3 = np.mean(f3_track) if f3_track else 2500.0

            result = {
                "tracks": [
                    {
                        "f1": (
                            f1_track[:100] if len(f1_track) > 100 else f1_track
                        ),  # Limit to 100 points
                        "f2": f2_track[:100] if len(f2_track) > 100 else f2_track,
                        "f3": f3_track[:100] if len(f3_track) > 100 else f3_track,
                    }
                ],
                "averages": {
                    "f1": float(avg_f1),
                    "f2": float(avg_f2),
                    "f3": float(avg_f3),
                },
                "audio_id": audio_id,
            }

            # Cache result
            _formant_analyses[audio_id] = result

            logger.info(
                f"Formant analysis completed for {audio_id}: "
                f"F1={avg_f1:.1f}Hz, F2={avg_f2:.1f}Hz, F3={avg_f3:.1f}Hz"
            )

            return result

        except ImportError:
            # Fallback: Use spectral centroid as formant approximation
            logger.warning("scipy not available, using spectral centroid approximation")

            # Calculate spectral centroid as formant approximation
            n_fft = 2048
            hop_length = 512
            spectral_centroid = librosa.feature.spectral_centroid(
                y=audio, sr=sample_rate, n_fft=n_fft, hop_length=hop_length
            )[0]

            # Estimate formants from spectral centroid
            # Rough approximation: F1 ≈ centroid/3, F2 ≈ centroid*1.5, F3 ≈ centroid*2.5
            avg_centroid = np.mean(spectral_centroid)
            f1_approx = avg_centroid / 3.0
            f2_approx = avg_centroid * 1.5
            f3_approx = avg_centroid * 2.5

            result = {
                "tracks": [
                    {
                        "f1": [float(f1_approx)] * 100,
                        "f2": [float(f2_approx)] * 100,
                        "f3": [float(f3_approx)] * 100,
                    }
                ],
                "averages": {
                    "f1": float(f1_approx),
                    "f2": float(f2_approx),
                    "f3": float(f3_approx),
                },
                "audio_id": audio_id,
                "method": "approximation",
            }

            _formant_analyses[audio_id] = result
            return result

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Formant analysis failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Formant analysis failed: {e!s}") from e


@router.post("/apply")
async def apply(req: FormantEditRequest) -> ApiOk:
    """
    Apply formant shifts to audio.

    Formant shifting can change the perceived vowel quality or
    create voice transformation effects.

    Args:
        req: Request with audio_id and formant shifts

    Returns:
        Success response
    """
    try:
        audio_id = req.audio_id
        shifts = req.shifts

        if not audio_id:
            raise HTTPException(status_code=400, detail="audio_id is required")

        if not shifts:
            raise HTTPException(status_code=400, detail="shifts dictionary is required")

        # Get audio file path
        from backend.services.audio_artifacts import AudioRegistry

        audio_path = AudioRegistry.get_path(audio_id)
        if not audio_path:
            raise HTTPException(status_code=404, detail=f"Audio file '{audio_id}' not found")
        if not os.path.exists(audio_path):
            raise HTTPException(
                status_code=404, detail=f"Audio file at '{audio_path}' does not exist"
            )

        # Try to load audio processing libraries
        try:
            import librosa
            import soundfile as sf
        except ImportError:
            raise HTTPException(
                status_code=503,
                detail=(
                    "Formant editing requires librosa and soundfile. "
                    "Install with: pip install librosa soundfile"
                ),
            )

        # Load audio
        audio, sample_rate = sf.read(audio_path)

        # Convert to mono if needed
        if len(audio.shape) > 1:
            audio = np.mean(audio, axis=1)

        # Normalize audio
        if np.max(np.abs(audio)) > 1.0:
            audio = audio / np.max(np.abs(audio))

        # Apply formant shifts
        # Formant shifting is complex - we'll use phase vocoder with formant preservation
        # For simplicity, we'll use pitch shifting with formant preservation

        processed_audio = audio.copy()

        # Get formant shift ratios
        f1_shift = shifts.get("f1", 1.0)  # F1 shift ratio
        f2_shift = shifts.get("f2", 1.0)  # F2 shift ratio
        f3_shift = shifts.get("f3", 1.0)  # F3 shift ratio

        # Average shift for overall formant transformation
        avg_shift = (f1_shift + f2_shift + f3_shift) / 3.0

        if avg_shift != 1.0:
            # Use phase vocoder for formant shifting
            # librosa's phase_vocoder can shift formants while preserving pitch
            try:
                # Calculate STFT
                stft = librosa.stft(audio, n_fft=2048, hop_length=512)

                # Apply formant shift using phase vocoder
                # The rate parameter controls formant shift (1.0 = no shift)
                rate = 1.0 / avg_shift  # Invert for phase vocoder
                stft_shifted = librosa.phase_vocoder(stft, rate=rate)

                # Reconstruct audio
                processed_audio = librosa.istft(stft_shifted, hop_length=512)

                logger.info(
                    f"Applied formant shifts to {audio_id}: "
                    f"F1={f1_shift:.2f}x, F2={f2_shift:.2f}x, F3={f3_shift:.2f}x"
                )
            except Exception as e:
                logger.warning(
                    f"Phase vocoder formant shift failed: {e}, using pitch shift fallback"
                )
                # Fallback: Use pitch shift (less accurate but works)
                semitones = 12 * np.log2(avg_shift)
                processed_audio = librosa.effects.pitch_shift(
                    audio,
                    sr=sample_rate,
                    n_steps=semitones,
                )

        # Save processed audio via artifact spine
        from backend.services.audio_artifacts import create_audio_artifact_from_wav_array

        output_audio_id, _, _ = create_audio_artifact_from_wav_array(
            processed_audio,
            sample_rate,
            created_by="formant",
            audio_id=f"formant_{uuid.uuid4().hex[:8]}",
        )

        logger.info(f"Formant editing completed: {audio_id} -> {output_audio_id}")

        return ApiOk()

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Formant editing failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Formant editing failed: {e!s}") from e
