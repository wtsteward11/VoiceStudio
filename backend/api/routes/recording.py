"""
Audio Recording Routes

Endpoints for audio recording functionality.
Supports real-time recording with configurable sample rate,
channels, and format. Accepts audio chunks from the frontend
and writes them to WAV files.
"""

from __future__ import annotations

import asyncio
import logging
import os
import threading
import time
import uuid
import wave
from datetime import datetime

from fastapi import APIRouter, File, HTTPException, UploadFile
from pydantic import BaseModel

from backend.config.path_config import get_path

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/recording", tags=["recording"])

# In-memory storage for active recordings
_active_recordings: dict[str, dict] = {}
_state_lock = asyncio.Lock()
_recording_lock = threading.Lock()

# Try to import sounddevice for device enumeration
try:
    import sounddevice as sd

    HAS_SOUNDDEVICE = True
except ImportError:
    HAS_SOUNDDEVICE = False
    logger.debug("sounddevice not available. " "Device enumeration will be limited.")


class RecordingStartRequest(BaseModel):
    """Request to start recording."""

    sample_rate: int = 44100
    channels: int = 1  # 1 = mono, 2 = stereo
    bit_depth: int = 16  # 16 or 24
    format: str = "wav"  # "wav" or "pcm"
    project_id: str | None = None
    filename: str | None = None


class RecordingStatusResponse(BaseModel):
    """Recording status response."""

    recording_id: str
    is_recording: bool
    duration: float  # seconds
    sample_rate: int
    channels: int
    bit_depth: int
    format: str
    file_path: str | None = None
    audio_id: str | None = None


class RecordingStopResponse(BaseModel):
    """Response when stopping recording."""

    recording_id: str
    audio_id: str
    audio_url: str
    file_path: str
    duration: float
    sample_rate: int
    channels: int
    file_size: int


@router.post("/start", response_model=RecordingStatusResponse)
async def start_recording(request: RecordingStartRequest):
    """
    Start a new audio recording session.

    Returns a recording_id that can be used to stop/status the recording.
    """
    recording_id = str(uuid.uuid4())

    # Create recording directory if it doesn't exist
    recording_dir = str(get_path("recordings"))
    os.makedirs(recording_dir, exist_ok=True)

    # Generate filename
    if request.filename:
        filename = request.filename
        if not filename.endswith(".wav"):
            filename = f"{filename}.wav"
    else:
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        filename = f"recording_{timestamp}.wav"

    file_path = os.path.join(recording_dir, filename)

    # Initialize WAV file for writing
    try:
        wave_file = wave.open(file_path, "wb")
        wave_file.setnchannels(request.channels)
        wave_file.setsampwidth(request.bit_depth // 8)
        wave_file.setframerate(request.sample_rate)
    except Exception as e:
        logger.error(f"Failed to create WAV file: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to initialize recording file: {e!s}",
        )

    # Initialize recording state
    with _recording_lock:
        _active_recordings[recording_id] = {
            "recording_id": recording_id,
            "file_path": file_path,
            "sample_rate": request.sample_rate,
            "channels": request.channels,
            "bit_depth": request.bit_depth,
            "format": request.format,
            "project_id": request.project_id,
            "start_time": time.time(),
            "is_recording": True,
            "wave_file": wave_file,
            "audio_id": None,
            "chunks_received": 0,
        }

    logger.info(f"Started recording {recording_id} to {file_path}")

    return RecordingStatusResponse(
        recording_id=recording_id,
        is_recording=True,
        duration=0.0,
        sample_rate=request.sample_rate,
        channels=request.channels,
        bit_depth=request.bit_depth,
        format=request.format,
        file_path=file_path,
    )


@router.get("/{recording_id}/status", response_model=RecordingStatusResponse)
@cache_response(ttl=2)  # Cache for 2 seconds (recording status changes frequently)
async def get_recording_status(recording_id: str):
    """Get the current status of a recording."""
    with _recording_lock:
        if recording_id not in _active_recordings:
            raise HTTPException(status_code=404, detail="Recording not found")

        recording = _active_recordings[recording_id]
        duration = time.time() - recording["start_time"] if recording["is_recording"] else 0.0

        return RecordingStatusResponse(
            recording_id=recording_id,
            is_recording=recording["is_recording"],
            duration=duration,
            sample_rate=recording["sample_rate"],
            channels=recording["channels"],
            bit_depth=recording["bit_depth"],
            format=recording["format"],
            file_path=recording.get("file_path"),
            audio_id=recording.get("audio_id"),
        )


@router.post("/{recording_id}/chunk")
async def append_audio_chunk(
    recording_id: str,
    audio_data: UploadFile = File(...),
):
    """
    Append an audio chunk to an active recording.

    The audio data should be raw PCM bytes matching the recording's format.
    """
    with _recording_lock:
        if recording_id not in _active_recordings:
            raise HTTPException(status_code=404, detail="Recording not found")

        recording = _active_recordings[recording_id]

        if not recording["is_recording"]:
            raise HTTPException(status_code=400, detail="Recording is not active")

        wave_file = recording.get("wave_file")
        if wave_file is None:
            raise HTTPException(status_code=500, detail="Recording file not initialized")

        try:
            # Read audio chunk data
            chunk_data = await audio_data.read()

            # Write chunk to WAV file
            wave_file.writeframes(chunk_data)
            wave_file.flush()  # Ensure data is written

            recording["chunks_received"] = recording.get("chunks_received", 0) + 1

            logger.debug(
                f"Received chunk {recording['chunks_received']} " f"for recording {recording_id}"
            )

            return {
                "success": True,
                "chunks_received": recording["chunks_received"],
                "recording_id": recording_id,
            }
        except Exception as e:
            logger.error(f"Failed to write audio chunk: {e}")
            raise HTTPException(
                status_code=500,
                detail=f"Failed to write audio chunk: {e!s}",
            )


@router.post("/{recording_id}/stop", response_model=RecordingStopResponse)
async def stop_recording(recording_id: str):
    """
    Stop a recording and return the audio file information.

    Closes the WAV file and registers it in the audio storage system.
    """
    with _recording_lock:
        if recording_id not in _active_recordings:
            raise HTTPException(status_code=404, detail="Recording not found")

        recording = _active_recordings[recording_id]

        if not recording["is_recording"]:
            raise HTTPException(status_code=400, detail="Recording is not active")

        # Mark as stopped
        recording["is_recording"] = False
        duration = time.time() - recording["start_time"]

        # Close the WAV file
        wave_file = recording.get("wave_file")
        if wave_file:
            try:
                wave_file.close()
            except Exception as e:
                logger.warning(f"Error closing WAV file: {e}")

        file_path = recording["file_path"]

        # Verify file exists and get size
        if not os.path.exists(file_path):
            logger.warning(f"Recording file not found: {file_path}")
            raise HTTPException(status_code=500, detail="Recording file was not created")

        file_size = os.path.getsize(file_path)

        # Generate audio_id for the recorded file
        audio_id = f"recording_{recording_id}"

        # Store audio_id in recording
        recording["audio_id"] = audio_id

        # Register in audio storage
        try:
            from backend.services.audio_artifacts import AudioRegistry

            AudioRegistry.register(audio_id, file_path)
        except Exception as e:
            logger.warning(f"Could not register audio in storage: {e}")

        logger.info(
            f"Stopped recording {recording_id}, duration: {duration:.2f}s, "
            f"chunks: {recording.get('chunks_received', 0)}, "
            f"size: {file_size} bytes"
        )

        return RecordingStopResponse(
            recording_id=recording_id,
            audio_id=audio_id,
            audio_url=f"/api/voice/audio/{audio_id}",
            file_path=file_path,
            duration=duration,
            sample_rate=recording["sample_rate"],
            channels=recording["channels"],
            file_size=file_size,
        )


@router.delete("/{recording_id}")
async def cancel_recording(recording_id: str):
    """Cancel an active recording and delete the file."""
    with _recording_lock:
        if recording_id not in _active_recordings:
            raise HTTPException(status_code=404, detail="Recording not found")

        recording = _active_recordings[recording_id]

        # Close WAV file if open
        wave_file = recording.get("wave_file")
        if wave_file:
            try:
                wave_file.close()
            except Exception as e:
                logger.warning(f"Error closing WAV file during cancel: {e}")

        file_path = recording.get("file_path")

        # Delete the file if it exists
        if file_path and os.path.exists(file_path):
            try:
                os.remove(file_path)
            except Exception as e:
                logger.warning(f"Failed to delete recording file: {e}")

        # Remove from active recordings
        del _active_recordings[recording_id]

        logger.info(f"Cancelled recording {recording_id}")

        return {"success": True, "message": "Recording cancelled"}


@router.get("/devices")
@cache_response(ttl=60)  # Cache for 60 seconds (devices may change)
async def get_recording_devices():
    """
    Get list of available audio input devices.

    Uses sounddevice if available, otherwise returns default device info.
    """
    devices = []

    if HAS_SOUNDDEVICE:
        try:
            # Get all input devices
            host_apis = sd.query_hostapis()
            input_devices = sd.query_devices(kind="input")

            for idx, device in enumerate(input_devices):
                if device["max_input_channels"] > 0:
                    # Get supported sample rates (common ones)
                    sample_rates = []
                    for rate in [44100, 48000, 96000, 192000]:
                        try:
                            sd.check_device(idx, samplerate=rate)
                            sample_rates.append(rate)
                        except Exception:
                            ...

                    # If no specific rates work, use default
                    if not sample_rates:
                        sample_rates = [device.get("default_samplerate", 44100)]

                    devices.append(
                        {
                            "id": str(idx),
                            "name": device["name"],
                            "channels": device["max_input_channels"],
                            "sample_rates": sample_rates,
                            "host_api": (
                                host_apis[device["hostapi"]]["name"]
                                if device["hostapi"] < len(host_apis)
                                else "Unknown"
                            ),
                        }
                    )
        except Exception as e:
            logger.warning(f"Failed to enumerate devices with sounddevice: {e}")
            # Fall through to default device

    # If no devices found or sounddevice not available, return default
    if not devices:
        devices = [
            {
                "id": "default",
                "name": "Default Input Device",
                "channels": 2,
                "sample_rates": [44100, 48000, 96000],
                "host_api": "System Default",
            }
        ]

    return {"devices": devices}
