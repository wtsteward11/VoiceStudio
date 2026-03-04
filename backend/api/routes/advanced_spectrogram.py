"""
Advanced Spectrogram Visualization Routes

Endpoints for advanced spectrogram analysis with multiple views, comparisons,
and advanced processing options.
"""

from __future__ import annotations

import asyncio
import logging
from typing import Any

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(
    prefix="/api/advanced-spectrogram",
    tags=["advanced-spectrogram"],
)

# In-memory spectrogram data (replace with database in production)
_spectrogram_data: dict[str, dict] = {}
_state_lock = asyncio.Lock()


class SpectrogramView(BaseModel):
    """A spectrogram view configuration."""

    id: str
    audio_id: str
    view_type: str  # magnitude, phase, mel, chroma, mfcc
    window_size: int
    hop_length: int
    n_fft: int
    frequency_range: dict[str, float] | None = None
    time_range: dict[str, float] | None = None
    color_scheme: str
    created: str  # ISO datetime string


class SpectrogramComparison(BaseModel):
    """Comparison between multiple spectrograms."""

    id: str
    audio_ids: list[str]
    comparison_type: str  # difference, ratio, correlation
    result_data: dict
    created: str  # ISO datetime string


class AdvancedSpectrogramRequest(BaseModel):
    """Request for advanced spectrogram generation."""

    audio_id: str
    view_type: str = "magnitude"
    window_size: int = 2048
    hop_length: int = 512
    n_fft: int = 2048
    frequency_range: dict[str, float] | None = None
    time_range: dict[str, float] | None = None
    color_scheme: str = "viridis"
    apply_filters: bool = False
    filters: list[str] | None = None


class AdvancedSpectrogramResponse(BaseModel):
    """Response from advanced spectrogram generation."""

    view_id: str
    data_url: str | None = None
    metadata: dict
    message: str


class SpectrogramCompareRequest(BaseModel):
    """Request for spectrogram comparison."""

    audio_ids: list[str]
    comparison_type: str = "difference"


@router.post("/generate", response_model=AdvancedSpectrogramResponse)
async def generate_advanced_spectrogram(request: AdvancedSpectrogramRequest):
    """Generate an advanced spectrogram view."""
    import base64
    import os
    import tempfile
    import uuid
    from datetime import datetime

    try:
        import librosa
        import matplotlib
        import numpy as np

        from backend.audio.audio_utils import load_audio
        from backend.services.audio_artifacts import AudioRegistry

        matplotlib.use("Agg")  # Non-interactive backend
        import matplotlib.pyplot as plt

        # Load audio file
        audio_path = AudioRegistry.get_path(request.audio_id)
        if not audio_path:
            raise HTTPException(
                status_code=404,
                detail=f"Audio file '{request.audio_id}' not found",
            )
        if not os.path.exists(audio_path):
            raise HTTPException(
                status_code=404,
                detail=f"Audio file at '{audio_path}' does not exist",
            )

        # Load audio
        audio, sample_rate = load_audio(audio_path)
        if len(audio.shape) > 1:
            audio = np.mean(audio, axis=1)

        # Apply time range if specified
        if request.time_range:
            start_sample = int(request.time_range.get("start", 0.0) * sample_rate)
            end_sample = int(request.time_range.get("end", len(audio) / sample_rate) * sample_rate)
            audio = audio[start_sample:end_sample]

        # Generate spectrogram based on view_type
        if request.view_type == "magnitude":
            # Standard magnitude spectrogram
            stft = librosa.stft(
                audio,
                n_fft=request.n_fft,
                hop_length=request.hop_length,
                win_length=request.window_size,
            )
            spectrogram = np.abs(stft)
        elif request.view_type == "phase":
            # Phase spectrogram
            stft = librosa.stft(
                audio,
                n_fft=request.n_fft,
                hop_length=request.hop_length,
                win_length=request.window_size,
            )
            spectrogram = np.angle(stft)
        elif request.view_type == "mel":
            # Mel spectrogram
            spectrogram = librosa.feature.melspectrogram(
                y=audio,
                sr=sample_rate,
                n_fft=request.n_fft,
                hop_length=request.hop_length,
                win_length=request.window_size,
            )
            spectrogram = librosa.power_to_db(spectrogram, ref=np.max)
        elif request.view_type == "chroma":
            # Chroma features
            spectrogram = librosa.feature.chroma_stft(
                y=audio,
                sr=sample_rate,
                n_fft=request.n_fft,
                hop_length=request.hop_length,
            )
        elif request.view_type == "mfcc":
            # MFCC features
            spectrogram = librosa.feature.mfcc(
                y=audio,
                sr=sample_rate,
                n_mfcc=13,
                n_fft=request.n_fft,
                hop_length=request.hop_length,
            )
        else:
            # Default to magnitude
            stft = librosa.stft(
                audio,
                n_fft=request.n_fft,
                hop_length=request.hop_length,
                win_length=request.window_size,
            )
            spectrogram = np.abs(stft)

        # Apply frequency range if specified
        if request.frequency_range:
            freqs = librosa.fft_frequencies(sr=sample_rate, n_fft=request.n_fft)
            min_freq = request.frequency_range.get("min", 0.0)
            max_freq = request.frequency_range.get("max", sample_rate / 2)
            freq_mask = (freqs >= min_freq) & (freqs <= max_freq)
            spectrogram = spectrogram[freq_mask, :]

        # Generate spectrogram image
        plt.figure(figsize=(12, 6))
        librosa.display.specshow(
            spectrogram,
            sr=sample_rate,
            hop_length=request.hop_length,
            x_axis="time",
            y_axis="hz",
            cmap=request.color_scheme,
        )
        plt.colorbar(format="%+2.0f dB")
        plt.title(f"{request.view_type.capitalize()} Spectrogram")

        # Save to temporary file
        view_id = f"view-{uuid.uuid4().hex[:8]}"
        with tempfile.NamedTemporaryFile(delete=False, suffix=".png") as tmp:
            output_path = tmp.name
        plt.savefig(output_path, dpi=150, bbox_inches="tight")
        plt.close()

        # Read image and encode as base64
        with open(output_path, "rb") as f:
            image_data = base64.b64encode(f.read()).decode("utf-8")
            data_url = f"data:image/png;base64,{image_data}"

        # Clean up temp file
        os.unlink(output_path)

        now = datetime.utcnow().isoformat()

        view = {
            "id": view_id,
            "audio_id": request.audio_id,
            "view_type": request.view_type,
            "window_size": request.window_size,
            "hop_length": request.hop_length,
            "n_fft": request.n_fft,
            "frequency_range": request.frequency_range,
            "time_range": request.time_range,
            "color_scheme": request.color_scheme,
            "created": now,
        }

        _spectrogram_data[view_id] = view

        return AdvancedSpectrogramResponse(
            view_id=view_id,
            data_url=data_url,
            metadata={
                "view_type": request.view_type,
                "window_size": request.window_size,
                "hop_length": request.hop_length,
                "n_fft": request.n_fft,
                "sample_rate": sample_rate,
                "duration": len(audio) / sample_rate,
            },
            message="Advanced spectrogram generated",
        )
    except Exception as e:
        logger.error(f"Failed to generate spectrogram: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to generate spectrogram: {e!s}",
        ) from e


@router.get("/views/{view_id}", response_model=SpectrogramView)
@cache_response(ttl=300)  # Cache 5min (views are computationally expensive)
async def get_spectrogram_view(view_id: str):
    """Get a spectrogram view."""
    if view_id not in _spectrogram_data:
        raise HTTPException(status_code=404, detail="View not found")

    view = _spectrogram_data[view_id]
    return SpectrogramView(
        id=view["id"],
        audio_id=view["audio_id"],
        view_type=view["view_type"],
        window_size=view["window_size"],
        hop_length=view["hop_length"],
        n_fft=view["n_fft"],
        frequency_range=view.get("frequency_range"),
        time_range=view.get("time_range"),
        color_scheme=view["color_scheme"],
        created=view["created"],
    )


@router.post("/compare", response_model=SpectrogramComparison)
async def compare_spectrograms(request: SpectrogramCompareRequest):
    """Compare multiple spectrograms."""
    import os
    import uuid
    from datetime import datetime

    try:
        import librosa
        import numpy as np

        from backend.audio.audio_utils import load_audio
        from backend.services.audio_artifacts import AudioRegistry

        if len(request.audio_ids) < 2:
            raise HTTPException(
                status_code=400,
                detail="At least 2 audio files required for comparison",
            )

        # Load all audio files and generate spectrograms
        spectrograms = []
        sample_rates = []

        for audio_id in request.audio_ids:
            audio_path = AudioRegistry.get_path(audio_id)
            if not audio_path:
                raise HTTPException(
                    status_code=404,
                    detail=f"Audio file '{audio_id}' not found",
                )
            if not os.path.exists(audio_path):
                raise HTTPException(
                    status_code=404,
                    detail=f"Audio file at '{audio_path}' does not exist",
                )

            # Load audio
            audio, sample_rate = load_audio(audio_path)
            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)

            # Generate magnitude spectrogram
            stft = librosa.stft(audio, n_fft=2048, hop_length=512)
            spectrogram = np.abs(stft)

            spectrograms.append(spectrogram)
            sample_rates.append(sample_rate)

        # Perform comparison based on type
        result_data: dict[str, Any] = {}

        if request.comparison_type == "difference":
            # Calculate difference between first two spectrograms
            if len(spectrograms) >= 2:
                # Resize to common dimensions
                min_freq_bins = min(s.shape[0] for s in spectrograms[:2])
                min_time_frames = min(s.shape[1] for s in spectrograms[:2])

                spec1 = spectrograms[0][:min_freq_bins, :min_time_frames]
                spec2 = spectrograms[1][:min_freq_bins, :min_time_frames]

                difference = np.abs(spec1 - spec2)
                result_data = {
                    "difference_mean": float(np.mean(difference)),
                    "difference_max": float(np.max(difference)),
                    "difference_std": float(np.std(difference)),
                    "similarity": float(1.0 - (np.mean(difference) / np.mean(spec1))),
                }

        elif request.comparison_type == "ratio":
            # Calculate ratio between first two spectrograms
            if len(spectrograms) >= 2:
                min_freq_bins = min(s.shape[0] for s in spectrograms[:2])
                min_time_frames = min(s.shape[1] for s in spectrograms[:2])

                spec1 = spectrograms[0][:min_freq_bins, :min_time_frames]
                spec2 = spectrograms[1][:min_freq_bins, :min_time_frames]

                # Avoid division by zero
                ratio = np.where(spec2 > 0, spec1 / spec2, 0)
                result_data = {
                    "ratio_mean": float(np.mean(ratio)),
                    "ratio_max": float(np.max(ratio)),
                    "ratio_min": float(np.min(ratio[ratio > 0])),
                }

        elif request.comparison_type == "correlation":
            # Calculate correlation between all pairs
            correlations = []
            for i in range(len(spectrograms)):
                for j in range(i + 1, len(spectrograms)):
                    spec1 = spectrograms[i]
                    spec2 = spectrograms[j]

                    # Resize to common dimensions
                    min_freq_bins = min(spec1.shape[0], spec2.shape[0])
                    min_time_frames = min(spec1.shape[1], spec2.shape[1])

                    spec1_flat = spec1[:min_freq_bins, :min_time_frames].flatten()
                    spec2_flat = spec2[:min_freq_bins, :min_time_frames].flatten()

                    # Calculate Pearson correlation
                    if len(spec1_flat) > 1:
                        correlation = np.corrcoef(spec1_flat, spec2_flat)[0, 1]
                        correlations.append(
                            {
                                "audio_id_1": request.audio_ids[i],
                                "audio_id_2": request.audio_ids[j],
                                "correlation": float(correlation),
                            }
                        )

            correlation_values = (
                np.array([c["correlation"] for c in correlations], dtype=float)
                if correlations
                else np.array([], dtype=float)
            )
            mean_corr = np.mean(correlation_values).item() if len(correlation_values) > 0 else 0.0
            result_data = {
                "correlations": correlations,
                "mean_correlation": mean_corr,
            }

        comparison_id = f"comp-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        return SpectrogramComparison(
            id=comparison_id,
            audio_ids=request.audio_ids,
            comparison_type=request.comparison_type,
            result_data=result_data,
            created=now,
        )
    except Exception as e:
        logger.error(f"Failed to compare spectrograms: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to compare: {e!s}",
        ) from e


@router.get("/view-types")
@cache_response(ttl=600)  # Cache for 10 minutes (view types are static)
async def get_view_types():
    """Get available spectrogram view types."""
    return {
        "view_types": [
            {"id": "magnitude", "name": "Magnitude Spectrogram"},
            {"id": "phase", "name": "Phase Spectrogram"},
            {"id": "mel", "name": "Mel Spectrogram"},
            {"id": "chroma", "name": "Chroma Features"},
            {"id": "mfcc", "name": "MFCC Features"},
            {"id": "constant_q", "name": "Constant-Q Transform"},
            {"id": "harmonic_percussive", "name": "Harmonic-Percussive"},
        ]
    }


@router.delete("/views/{view_id}")
async def delete_spectrogram_view(view_id: str):
    """Delete a spectrogram view."""
    if view_id not in _spectrogram_data:
        raise HTTPException(status_code=404, detail="View not found")

    del _spectrogram_data[view_id]
    logger.info(f"Deleted spectrogram view: {view_id}")
    return {"success": True}


@router.get("/export/{view_id}")
async def export_spectrogram(
    view_id: str,
    format: str = "png",
):
    """
    Export spectrogram view as image or data.

    Args:
        view_id: Spectrogram view ID
        format: Export format (png, json, csv)

    Returns:
        Exported spectrogram data
    """
    try:
        if view_id not in _spectrogram_data:
            raise HTTPException(status_code=404, detail="View not found")

        view = _spectrogram_data[view_id]

        if format == "json":
            # Return view metadata as JSON
            return {
                "view_id": view["id"],
                "audio_id": view["audio_id"],
                "view_type": view["view_type"],
                "window_size": view["window_size"],
                "hop_length": view["hop_length"],
                "n_fft": view["n_fft"],
                "frequency_range": view.get("frequency_range"),
                "time_range": view.get("time_range"),
                "color_scheme": view["color_scheme"],
                "created": view["created"],
            }
        elif format == "csv":
            # Regenerate spectrogram and export as CSV
            import csv
            import io
            import os

            import librosa
            import numpy as np

            from backend.audio.audio_utils import load_audio
            from backend.services.audio_artifacts import AudioRegistry

            audio_id = view["audio_id"]
            audio_path = AudioRegistry.get_path(audio_id)
            if not audio_path:
                raise HTTPException(
                    status_code=404,
                    detail=f"Audio file '{audio_id}' not found",
                )
            if not os.path.exists(audio_path):
                raise HTTPException(
                    status_code=404,
                    detail=f"Audio file at '{audio_path}' does not exist",
                )

            # Load audio and regenerate spectrogram
            audio, sample_rate = load_audio(audio_path)
            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)

            # Apply time range if specified
            if view.get("time_range"):
                start_sample = int(view["time_range"].get("start", 0.0) * sample_rate)
                end_sample = int(
                    view["time_range"].get("end", len(audio) / sample_rate) * sample_rate
                )
                audio = audio[start_sample:end_sample]

            # Generate spectrogram
            if view["view_type"] == "magnitude":
                stft = librosa.stft(
                    audio,
                    n_fft=view["n_fft"],
                    hop_length=view["hop_length"],
                    win_length=view["window_size"],
                )
                spectrogram = np.abs(stft)
            elif view["view_type"] == "mel":
                spectrogram = librosa.feature.melspectrogram(
                    y=audio,
                    sr=sample_rate,
                    n_fft=view["n_fft"],
                    hop_length=view["hop_length"],
                    win_length=view["window_size"],
                )
                spectrogram = librosa.power_to_db(spectrogram, ref=np.max)
            else:
                stft = librosa.stft(
                    audio,
                    n_fft=view["n_fft"],
                    hop_length=view["hop_length"],
                    win_length=view["window_size"],
                )
                spectrogram = np.abs(stft)

            # Export as CSV
            output = io.StringIO()
            writer = csv.writer(output)

            # Header: frequency bins as columns, time frames as rows
            freqs = librosa.fft_frequencies(sr=sample_rate, n_fft=view["n_fft"])
            header = ["time"] + [f"freq_{f:.1f}Hz" for f in freqs[: spectrogram.shape[0]]]
            writer.writerow(header)

            # Data rows
            time_axis = librosa.frames_to_time(
                np.arange(spectrogram.shape[1]),
                sr=sample_rate,
                hop_length=view["hop_length"],
            )
            for t_idx, time_val in enumerate(time_axis):
                row = [time_val] + [
                    float(spectrogram[f_idx, t_idx]) for f_idx in range(spectrogram.shape[0])
                ]
                writer.writerow(row)

            from fastapi.responses import Response

            return Response(
                content=output.getvalue(),
                media_type="text/csv",
                headers={
                    "Content-Disposition": (f'attachment; filename="spectrogram_{view_id}.csv"')
                },
            )
        else:
            # PNG format - return data URL from view
            raise HTTPException(
                status_code=400,
                detail=("PNG export not available via this endpoint. " "Use /generate endpoint."),
            )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to export spectrogram: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=f"Failed to export spectrogram: {e!s}",
        ) from e
