"""
Video Editing API Routes
Handles video editing operations (trim, split, effects, transitions, export)
"""

from __future__ import annotations

import logging
import os
import subprocess
import uuid
from pathlib import Path

from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/video/edit", tags=["video", "editing"])

# Output directory for edited videos (use path_config to avoid repo writes)
from backend.config.path_config import get_path

OUTPUT_DIR = get_path("data") / "videos" / "edited"
OUTPUT_DIR.mkdir(parents=True, exist_ok=True)


class VideoEditRequest(BaseModel):
    operation: str  # trim, split, effect, transition, export, resize, add_audio, upscale
    input_path: str | None = None
    output_path: str | None = None
    start_time: float | None = None
    end_time: float | None = None
    split_time: float | None = None
    effect: str | None = None
    transition: str | None = None
    duration: float | None = None
    format: str | None = None
    quality: int | None = None
    width: int | None = None
    height: int | None = None
    audio_path: str | None = None
    scale: float | None = None


class VideoEditResponse(BaseModel):
    success: bool
    output_path: str | None = None
    message: str | None = None


class VideoInfo(BaseModel):
    duration: float
    width: int
    height: int
    fps: float
    format: str | None = None


def check_ffmpeg() -> bool:
    """Check if FFmpeg is available."""
    try:
        result = subprocess.run(["ffmpeg", "-version"], capture_output=True, timeout=5)
        return result.returncode == 0
    except (FileNotFoundError, subprocess.TimeoutExpired):
        return False


def get_video_info(video_path: str) -> VideoInfo:
    """Get video information using FFprobe."""
    try:
        if not os.path.exists(video_path):
            raise HTTPException(status_code=404, detail=f"Video file not found: {video_path}")

        # Use ffprobe to get video info
        cmd = [
            "ffprobe",
            "-v",
            "error",
            "-select_streams",
            "v:0",
            "-show_entries",
            "stream=width,height,r_frame_rate,duration",
            "-of",
            "json",
            video_path,
        ]

        result = subprocess.run(cmd, capture_output=True, text=True, timeout=30)

        if result.returncode != 0:
            raise HTTPException(
                status_code=500,
                detail=f"Failed to get video info: {result.stderr}",
            )

        import json

        data = json.loads(result.stdout)

        stream = data.get("streams", [{}])[0]
        width = stream.get("width", 0)
        height = stream.get("height", 0)
        fps_str = stream.get("r_frame_rate", "0/1")
        duration = float(stream.get("duration", 0))

        # Parse FPS
        if "/" in fps_str:
            num, den = map(int, fps_str.split("/"))
            fps = num / den if den != 0 else 0
        else:
            fps = float(fps_str)

        return VideoInfo(
            duration=duration,
            width=width,
            height=height,
            fps=fps,
            format=Path(video_path).suffix[1:],
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get video info: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get video info: {e!s}")


def trim_video(input_path: str, output_path: str, start_time: float, end_time: float) -> bool:
    """Trim video using FFmpeg."""
    try:
        cmd = [
            "ffmpeg",
            "-i",
            input_path,
            "-ss",
            str(start_time),
            "-t",
            str(end_time - start_time),
            "-c",
            "copy",
            "-y",  # Overwrite output file
            output_path,
        ]

        result = subprocess.run(cmd, capture_output=True, text=True, timeout=300)

        if result.returncode != 0:
            logger.error(f"FFmpeg error: {result.stderr}")
            return False

        return True
    except Exception as e:
        logger.error(f"Failed to trim video: {e}", exc_info=True)
        return False


def split_video(input_path: str, output_path: str, split_time: float) -> bool:
    """Split video at a specific time."""
    try:
        # Create two output files
        base_path = Path(output_path)
        part1_path = base_path.parent / f"{base_path.stem}_part1{base_path.suffix}"
        part2_path = base_path.parent / f"{base_path.stem}_part2{base_path.suffix}"

        # First part: from start to split_time
        cmd1 = [
            "ffmpeg",
            "-i",
            input_path,
            "-t",
            str(split_time),
            "-c",
            "copy",
            "-y",
            str(part1_path),
        ]

        # Second part: from split_time to end
        cmd2 = [
            "ffmpeg",
            "-i",
            input_path,
            "-ss",
            str(split_time),
            "-c",
            "copy",
            "-y",
            str(part2_path),
        ]

        result1 = subprocess.run(cmd1, capture_output=True, text=True, timeout=300)
        result2 = subprocess.run(cmd2, capture_output=True, text=True, timeout=300)

        if result1.returncode != 0 or result2.returncode != 0:
            logger.error(f"FFmpeg split error: {result1.stderr} | {result2.stderr}")
            return False

        # Return path to first part (or combine them)
        return True
    except Exception as e:
        logger.error(f"Failed to split video: {e}", exc_info=True)
        return False


def apply_effect(input_path: str, output_path: str, effect: str) -> bool:
    """Apply video effect using FFmpeg."""
    try:
        effect_filters = {
            "Brightness": "eq=brightness=0.1",
            "Contrast": "eq=contrast=1.2",
            "Saturation": "eq=saturation=1.2",
            "Blur": "boxblur=5:1",
            "Sharpen": "unsharp=5:5:1.0:5:5:0.0",
            "Grayscale": "hue=s=0",
            "Sepia": "colorchannelmixer=.393:.769:.189:0:.349:.686:.168:0:.272:.534:.131",
            "Vignette": "vignette=PI/4",
        }

        filter_str = effect_filters.get(effect)
        if not filter_str:
            logger.error(f"Unknown effect: {effect}")
            return False

        cmd = [
            "ffmpeg",
            "-i",
            input_path,
            "-vf",
            filter_str,
            "-y",
            output_path,
        ]

        result = subprocess.run(cmd, capture_output=True, text=True, timeout=300)

        if result.returncode != 0:
            logger.error(f"FFmpeg effect error: {result.stderr}")
            return False

        return True
    except Exception as e:
        logger.error(f"Failed to apply effect: {e}", exc_info=True)
        return False


def apply_transition(
    input_path: str, output_path: str, transition: str, duration: float = 1.0
) -> bool:
    """Apply transition effect using FFmpeg."""
    try:
        # For simplicity, we'll apply fade in/out
        # More complex transitions would require multiple video inputs
        if transition == "Fade In":
            filter_str = f"fade=t=in:st=0:d={duration}"
        elif transition == "Fade Out":
            info = get_video_info(input_path)
            fade_start = info.duration - duration
            filter_str = f"fade=t=out:st={fade_start}:d={duration}"
        elif transition == "Cross Fade":
            # Would need two videos for cross fade
            filter_str = "fade=t=in:st=0:d=0.5"
        else:
            # Default: fade in
            filter_str = f"fade=t=in:st=0:d={duration}"

        cmd = [
            "ffmpeg",
            "-i",
            input_path,
            "-vf",
            filter_str,
            "-y",
            output_path,
        ]

        result = subprocess.run(cmd, capture_output=True, text=True, timeout=300)

        if result.returncode != 0:
            logger.error(f"FFmpeg transition error: {result.stderr}")
            return False

        return True
    except Exception as e:
        logger.error(f"Failed to apply transition: {e}", exc_info=True)
        return False


def resize_video(input_path: str, output_path: str, width: int, height: int) -> bool:
    """Resize video using FFmpeg."""
    try:
        cmd = [
            "ffmpeg",
            "-i",
            input_path,
            "-vf",
            f"scale={width}:{height}",
            "-c:v",
            "libx264",
            "-preset",
            "medium",
            "-crf",
            "23",
            "-y",
            output_path,
        ]

        result = subprocess.run(cmd, capture_output=True, text=True, timeout=600)

        if result.returncode != 0:
            logger.error(f"FFmpeg resize error: {result.stderr}")
            return False

        return True
    except Exception as e:
        logger.error(f"Failed to resize video: {e}", exc_info=True)
        return False


def add_audio_to_video(input_path: str, output_path: str, audio_path: str) -> bool:
    """Add audio track to video using FFmpeg."""
    try:
        if not os.path.exists(audio_path):
            logger.error(f"Audio file not found: {audio_path}")
            return False

        cmd = [
            "ffmpeg",
            "-i",
            input_path,
            "-i",
            audio_path,
            "-c:v",
            "copy",
            "-c:a",
            "aac",
            "-map",
            "0:v:0",
            "-map",
            "1:a:0",
            "-shortest",
            "-y",
            output_path,
        ]

        result = subprocess.run(cmd, capture_output=True, text=True, timeout=600)

        if result.returncode != 0:
            logger.error(f"FFmpeg add audio error: {result.stderr}")
            return False

        return True
    except Exception as e:
        logger.error(f"Failed to add audio to video: {e}", exc_info=True)
        return False


def upscale_video(input_path: str, output_path: str, scale: float) -> bool:
    """Upscale video using FFmpeg."""
    try:
        # Get original video dimensions
        info = get_video_info(input_path)
        new_width = int(info.width * scale)
        new_height = int(info.height * scale)

        # Ensure even dimensions (required for some codecs)
        new_width = new_width - (new_width % 2)
        new_height = new_height - (new_height % 2)

        cmd = [
            "ffmpeg",
            "-i",
            input_path,
            "-vf",
            f"scale={new_width}:{new_height}",
            "-c:v",
            "libx264",
            "-preset",
            "slow",
            "-crf",
            "18",
            "-y",
            output_path,
        ]

        result = subprocess.run(cmd, capture_output=True, text=True, timeout=600)

        if result.returncode != 0:
            logger.error(f"FFmpeg upscale error: {result.stderr}")
            return False

        return True
    except Exception as e:
        logger.error(f"Failed to upscale video: {e}", exc_info=True)
        return False


def export_video(input_path: str, output_path: str, format: str, quality: int = 5) -> bool:
    """Export video in specified format and quality."""
    try:
        # Map quality (1-10) to FFmpeg quality settings
        quality_map = {
            1: ("-crf", "28"),  # Low quality
            2: ("-crf", "26"),
            3: ("-crf", "24"),
            4: ("-crf", "22"),
            5: ("-crf", "20"),  # Medium quality
            6: ("-crf", "18"),
            7: ("-crf", "16"),
            8: ("-crf", "14"),
            9: ("-crf", "12"),
            10: ("-crf", "10"),  # High quality
        }

        quality_param, quality_value = quality_map.get(quality, ("-crf", "20"))

        # Format-specific codec selection
        codec_map = {
            "mp4": ("-c:v", "libx264", "-c:a", "aac"),
            "avi": ("-c:v", "libx264", "-c:a", "mp3"),
            "mov": ("-c:v", "libx264", "-c:a", "aac"),
            "mkv": ("-c:v", "libx264", "-c:a", "aac"),
            "webm": ("-c:v", "libvpx-vp9", "-c:a", "libopus"),
        }

        codec_params = codec_map.get(format.lower(), ("-c:v", "libx264", "-c:a", "aac"))

        cmd = [
            "ffmpeg",
            "-i",
            input_path,
            *codec_params,
            quality_param,
            quality_value,
            "-y",
            output_path,
        ]

        result = subprocess.run(cmd, capture_output=True, text=True, timeout=600)

        if result.returncode != 0:
            logger.error(f"FFmpeg export error: {result.stderr}")
            return False

        return True
    except Exception as e:
        logger.error(f"Failed to export video: {e}", exc_info=True)
        return False


@router.get("/info")
@cache_response(ttl=300)  # Cache for 5 minutes (video info is static for a given file)
async def get_video_info_endpoint(path: str = Query(..., description="Path to video file")):
    """Get video information."""
    try:
        if not check_ffmpeg():
            raise HTTPException(status_code=503, detail="FFmpeg is not available")

        info = get_video_info(path)
        return info
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get video info: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to get video info: {e!s}")


@router.post("", response_model=VideoEditResponse)
async def edit_video(request: VideoEditRequest):
    """Perform video editing operation."""
    try:
        if not check_ffmpeg():
            raise HTTPException(status_code=503, detail="FFmpeg is not available")

        if not request.input_path or not os.path.exists(request.input_path):
            raise HTTPException(
                status_code=404,
                detail=f"Input video not found: {request.input_path}",
            )

        # Generate output path if not provided
        if not request.output_path:
            ext = request.format or Path(request.input_path).suffix
            output_filename = f"{uuid.uuid4()}{ext}"
            request.output_path = str(OUTPUT_DIR / output_filename)

        success = False
        message = ""

        if request.operation == "trim":
            if request.start_time is None or request.end_time is None:
                raise HTTPException(
                    status_code=400,
                    detail="start_time and end_time required for trim operation",
                )
            success = trim_video(
                request.input_path,
                request.output_path,
                request.start_time,
                request.end_time,
            )
            message = "Video trimmed successfully" if success else "Failed to trim video"

        elif request.operation == "split":
            if request.split_time is None:
                raise HTTPException(
                    status_code=400,
                    detail="split_time required for split operation",
                )
            success = split_video(request.input_path, request.output_path, request.split_time)
            message = "Video split successfully" if success else "Failed to split video"

        elif request.operation == "effect":
            if not request.effect:
                raise HTTPException(
                    status_code=400,
                    detail="effect required for effect operation",
                )
            success = apply_effect(request.input_path, request.output_path, request.effect)
            message = (
                f"Effect '{request.effect}' applied successfully"
                if success
                else f"Failed to apply effect '{request.effect}'"
            )

        elif request.operation == "transition":
            if not request.transition:
                raise HTTPException(
                    status_code=400,
                    detail="transition required for transition operation",
                )
            duration = request.duration or 1.0
            success = apply_transition(
                request.input_path,
                request.output_path,
                request.transition,
                duration,
            )
            message = (
                f"Transition '{request.transition}' applied successfully"
                if success
                else f"Failed to apply transition '{request.transition}'"
            )

        elif request.operation == "export":
            format = request.format or "mp4"
            quality = request.quality or 5
            success = export_video(request.input_path, request.output_path, format, quality)
            message = (
                f"Video exported as {format} successfully"
                if success
                else f"Failed to export video as {format}"
            )

        elif request.operation == "resize":
            if request.width is None or request.height is None:
                raise HTTPException(
                    status_code=400,
                    detail="width and height required for resize operation",
                )
            success = resize_video(
                request.input_path,
                request.output_path,
                request.width,
                request.height,
            )
            message = "Video resized successfully" if success else "Failed to resize video"

        elif request.operation == "add_audio":
            if not request.audio_path:
                raise HTTPException(
                    status_code=400,
                    detail="audio_path required for add_audio operation",
                )
            success = add_audio_to_video(
                request.input_path, request.output_path, request.audio_path
            )
            message = "Audio added successfully" if success else "Failed to add audio"

        elif request.operation == "upscale":
            if request.scale is None:
                raise HTTPException(
                    status_code=400,
                    detail="scale required for upscale operation",
                )
            success = upscale_video(request.input_path, request.output_path, request.scale)
            message = "Video upscaled successfully" if success else "Failed to upscale video"

        else:
            raise HTTPException(
                status_code=400,
                detail=f"Unknown operation: {request.operation}",
            )

        return VideoEditResponse(
            success=success,
            output_path=request.output_path if success else None,
            message=message,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to edit video: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to edit video: {e!s}")
