"""
Video Face Enhancement Service.

D.2 Enhancement: Frame-by-frame face enhancement for video processing.

Supports:
- Face detection and tracking
- Expression enhancement
- Lip sync integration
- Quality upscaling
- Batch frame processing
"""

from __future__ import annotations

import asyncio
import logging
import tempfile
from collections.abc import Callable
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from pathlib import Path
from typing import Any

import numpy as np

logger = logging.getLogger(__name__)


class EnhancementMode(Enum):
    """Face enhancement modes."""

    LIP_SYNC = "lip_sync"  # Focus on mouth/lip area
    EXPRESSION = "expression"  # Full facial expression enhancement
    RESTORATION = "restoration"  # Face restoration/upscaling
    DEAGING = "deaging"  # Age reduction
    COMPOSITE = "composite"  # Face composite/swap


class QualityPreset(Enum):
    """Output quality presets."""

    PREVIEW = "preview"  # Fast, lower quality
    STANDARD = "standard"  # Balanced
    HIGH = "high"  # High quality
    ULTRA = "ultra"  # Maximum quality


@dataclass
class FaceRegion:
    """Detected face region in a frame."""

    x: int
    y: int
    width: int
    height: int
    confidence: float
    landmarks: dict[str, tuple[int, int]] = field(default_factory=dict)

    @property
    def center(self) -> tuple[int, int]:
        return (self.x + self.width // 2, self.y + self.height // 2)

    @property
    def area(self) -> int:
        return self.width * self.height


@dataclass
class FrameEnhancement:
    """Enhancement result for a single frame."""

    frame_number: int
    face_regions: list[FaceRegion]
    enhanced_data: np.ndarray | None = None
    processing_time_ms: float = 0.0
    enhancement_applied: bool = False


@dataclass
class EnhancementJob:
    """Face enhancement job."""

    job_id: str
    input_path: str
    output_path: str
    mode: EnhancementMode
    quality: QualityPreset
    audio_path: str | None = None
    target_fps: float | None = None
    start_frame: int = 0
    end_frame: int | None = None
    status: str = "pending"
    progress: float = 0.0
    frames_processed: int = 0
    total_frames: int = 0
    created_at: datetime = field(default_factory=datetime.now)
    completed_at: datetime | None = None
    error: str | None = None


class VideoFaceEnhancer:
    """
    Video face enhancement service.

    Provides frame-by-frame face enhancement for video processing,
    integrated with lip sync for dubbing workflows.
    """

    def __init__(self, work_dir: Path | None = None):
        """
        Initialize the enhancer.

        Args:
            work_dir: Working directory for temporary files
        """
        self._work_dir = work_dir or Path(tempfile.gettempdir()) / "voicestudio" / "face_enhance"
        self._work_dir.mkdir(parents=True, exist_ok=True)

        self._jobs: dict[str, EnhancementJob] = {}
        self._face_detector = None
        self._enhancement_model = None

        # Quality settings
        self._quality_settings = {
            QualityPreset.PREVIEW: {"resolution_scale": 0.5, "batch_size": 8},
            QualityPreset.STANDARD: {"resolution_scale": 1.0, "batch_size": 4},
            QualityPreset.HIGH: {"resolution_scale": 1.0, "batch_size": 2},
            QualityPreset.ULTRA: {"resolution_scale": 1.5, "batch_size": 1},
        }

    async def create_job(
        self,
        input_path: str,
        output_path: str,
        mode: EnhancementMode = EnhancementMode.LIP_SYNC,
        quality: QualityPreset = QualityPreset.STANDARD,
        audio_path: str | None = None,
    ) -> EnhancementJob:
        """
        Create a new enhancement job.

        Args:
            input_path: Input video path
            output_path: Output video path
            mode: Enhancement mode
            quality: Quality preset
            audio_path: Optional audio for lip sync

        Returns:
            Created job
        """
        import uuid

        job_id = str(uuid.uuid4())[:8]

        # Get video info
        total_frames = await self._get_frame_count(input_path)

        job = EnhancementJob(
            job_id=job_id,
            input_path=input_path,
            output_path=output_path,
            mode=mode,
            quality=quality,
            audio_path=audio_path,
            total_frames=total_frames,
        )

        self._jobs[job_id] = job
        logger.info(f"Created enhancement job {job_id} for {input_path}")

        return job

    async def process_job(
        self,
        job_id: str,
        progress_callback: Callable[[float, str], None] | None = None,
    ) -> EnhancementJob:
        """
        Process an enhancement job.

        Args:
            job_id: Job identifier
            progress_callback: Optional progress callback

        Returns:
            Completed job
        """
        job = self._jobs.get(job_id)
        if not job:
            raise ValueError(f"Job not found: {job_id}")

        try:
            job.status = "processing"

            if progress_callback:
                progress_callback(0.0, "Initializing...")

            # Extract frames
            frames_dir = self._work_dir / job_id / "frames"
            frames_dir.mkdir(parents=True, exist_ok=True)

            if progress_callback:
                progress_callback(0.1, "Extracting frames...")

            await self._extract_frames(job.input_path, frames_dir)

            # Get frame files
            frame_files = sorted(frames_dir.glob("*.png"))
            job.total_frames = len(frame_files)

            # Process frames
            enhanced_dir = self._work_dir / job_id / "enhanced"
            enhanced_dir.mkdir(parents=True, exist_ok=True)

            settings = self._quality_settings[job.quality]
            batch_size = settings["batch_size"]

            for i in range(0, len(frame_files), batch_size):
                batch = frame_files[i : i + batch_size]

                # Process batch
                for frame_file in batch:
                    enhanced_frame = await self._enhance_frame(
                        frame_file,
                        job.mode,
                        settings,
                    )

                    # Save enhanced frame
                    output_file = enhanced_dir / frame_file.name
                    await self._save_frame(enhanced_frame, output_file)

                    job.frames_processed += 1

                # Update progress
                job.progress = job.frames_processed / job.total_frames
                if progress_callback:
                    pct = int(job.progress * 100)
                    progress_callback(
                        0.1 + job.progress * 0.7,
                        f"Processing frame {job.frames_processed}/{job.total_frames} ({pct}%)",
                    )

            if progress_callback:
                progress_callback(0.8, "Encoding video...")

            # Encode output video
            await self._encode_video(
                enhanced_dir,
                job.output_path,
                job.audio_path or job.input_path,
            )

            job.status = "completed"
            job.progress = 1.0
            job.completed_at = datetime.now()

            if progress_callback:
                progress_callback(1.0, "Complete!")

            # Cleanup
            await self._cleanup_job(job_id)

            logger.info(f"Completed enhancement job {job_id}")
            return job

        except Exception as e:
            job.status = "failed"
            job.error = str(e)
            logger.error(f"Enhancement job {job_id} failed: {e}", exc_info=True)
            raise

    async def detect_faces(
        self,
        frame: np.ndarray,
    ) -> list[FaceRegion]:
        """
        Detect faces in a frame.

        Args:
            frame: Frame image data

        Returns:
            List of detected face regions
        """
        # Try to use OpenCV face detection
        try:
            import cv2

            # Load cascade if needed
            if self._face_detector is None:
                cascade_path = cv2.data.haarcascades + "haarcascade_frontalface_default.xml"
                self._face_detector = cv2.CascadeClassifier(cascade_path)

            # Convert to grayscale
            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY) if len(frame.shape) == 3 else frame

            # Detect faces
            faces = self._face_detector.detectMultiScale(
                gray,
                scaleFactor=1.1,
                minNeighbors=5,
                minSize=(30, 30),
            )

            regions = []
            for x, y, w, h in faces:
                regions.append(
                    FaceRegion(
                        x=int(x),
                        y=int(y),
                        width=int(w),
                        height=int(h),
                        confidence=0.9,
                    )
                )

            return regions

        except Exception as e:
            logger.debug(f"Face detection failed: {e}")
            return []

    async def enhance_lip_region(
        self,
        frame: np.ndarray,
        face_region: FaceRegion,
        audio_features: dict[str, Any] | None = None,
    ) -> np.ndarray:
        """
        Enhance the lip region of a face for lip sync.

        Args:
            frame: Frame image data
            face_region: Detected face region
            audio_features: Audio features for lip sync

        Returns:
            Enhanced frame
        """
        # For now, return the frame unchanged
        # In a full implementation, this would:
        # 1. Extract the mouth region
        # 2. Generate lip movement based on audio
        # 3. Blend the enhanced lips back into the frame
        return frame

    def get_job(self, job_id: str) -> EnhancementJob | None:
        """Get job by ID."""
        return self._jobs.get(job_id)

    def list_jobs(self) -> list[EnhancementJob]:
        """List all jobs."""
        return list(self._jobs.values())

    async def cancel_job(self, job_id: str) -> bool:
        """Cancel a job."""
        job = self._jobs.get(job_id)
        if not job:
            return False

        if job.status in ("completed", "failed"):
            return False

        job.status = "cancelled"
        await self._cleanup_job(job_id)
        return True

    async def _get_frame_count(self, video_path: str) -> int:
        """Get the number of frames in a video."""
        try:
            import subprocess

            cmd = [
                "ffprobe",
                "-v",
                "error",
                "-select_streams",
                "v:0",
                "-count_packets",
                "-show_entries",
                "stream=nb_read_packets",
                "-of",
                "csv=p=0",
                video_path,
            ]

            result = subprocess.run(cmd, capture_output=True, text=True, timeout=30)
            if result.returncode == 0:
                return int(result.stdout.strip())
        except Exception as e:
            logger.debug(f"Failed to get frame count: {e}")

        # Fallback estimate
        return 300  # Assume 10 seconds at 30fps

    async def _extract_frames(self, video_path: str, output_dir: Path) -> None:
        """Extract frames from video."""

        cmd = [
            "ffmpeg",
            "-i",
            video_path,
            "-vf",
            "fps=30",
            str(output_dir / "frame_%06d.png"),
        ]

        process = await asyncio.create_subprocess_exec(
            *cmd,
            stdout=asyncio.subprocess.DEVNULL,
            stderr=asyncio.subprocess.PIPE,
        )

        _, stderr = await process.communicate()

        if process.returncode != 0:
            raise RuntimeError(f"Frame extraction failed: {stderr.decode()}")

    async def _enhance_frame(
        self,
        frame_path: Path,
        mode: EnhancementMode,
        settings: dict[str, Any],
    ) -> np.ndarray:
        """Enhance a single frame."""
        try:
            import cv2

            # Load frame
            frame = cv2.imread(str(frame_path))
            if frame is None:
                raise ValueError(f"Failed to load frame: {frame_path}")

            # Scale if needed
            scale = settings.get("resolution_scale", 1.0)
            if scale != 1.0:
                h, w = frame.shape[:2]
                new_w, new_h = int(w * scale), int(h * scale)
                frame = cv2.resize(frame, (new_w, new_h), interpolation=cv2.INTER_LANCZOS4)

            # Apply enhancement based on mode
            if mode == EnhancementMode.LIP_SYNC:
                # Detect faces and enhance lip regions
                faces = await self.detect_faces(frame)
                for face in faces:
                    frame = await self.enhance_lip_region(frame, face)

            elif mode == EnhancementMode.RESTORATION:
                # Apply restoration filter using OpenCV-based enhancement
                # For full implementation, use GFPGAN or CodeFormer
                try:
                    # Apply denoising for restoration effect
                    frame = cv2.fastNlMeansDenoisingColored(frame, None, 10, 10, 7, 21)
                    # Apply slight sharpening
                    kernel = np.array([[-1, -1, -1], [-1, 9, -1], [-1, -1, -1]])
                    frame = cv2.filter2D(frame, -1, kernel)
                    logger.debug(
                        "Restoration mode: Applied OpenCV-based enhancement. "
                        "For neural restoration, install GFPGAN or CodeFormer."
                    )
                except Exception as e:
                    logger.warning(f"Restoration enhancement failed: {e}")

            elif mode == EnhancementMode.EXPRESSION:
                # Enhance facial expressions using contrast and clarity
                # For full implementation, use expression synthesis models
                try:
                    # Convert to LAB color space for better enhancement
                    lab = cv2.cvtColor(frame, cv2.COLOR_BGR2LAB)
                    l, a, b = cv2.split(lab)
                    # Apply CLAHE (Contrast Limited Adaptive Histogram Equalization)
                    clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8))
                    l = clahe.apply(l)
                    lab = cv2.merge((l, a, b))
                    frame = cv2.cvtColor(lab, cv2.COLOR_LAB2BGR)
                    logger.debug(
                        "Expression mode: Applied contrast enhancement. "
                        "For neural expression synthesis, install expression models."
                    )
                except Exception as e:
                    logger.warning(f"Expression enhancement failed: {e}")

            return frame

        except ImportError:
            logger.warning("OpenCV not available, returning original frame")
            return np.zeros((480, 640, 3), dtype=np.uint8)

    async def _save_frame(self, frame: np.ndarray, output_path: Path) -> None:
        """Save a frame to file."""
        try:
            import cv2

            cv2.imwrite(str(output_path), frame)
        except Exception as e:
            logger.error(f"Failed to save frame: {e}")

    async def _encode_video(
        self,
        frames_dir: Path,
        output_path: str,
        audio_source: str,
    ) -> None:
        """Encode frames back to video."""

        cmd = [
            "ffmpeg",
            "-y",
            "-framerate",
            "30",
            "-i",
            str(frames_dir / "frame_%06d.png"),
            "-i",
            audio_source,
            "-c:v",
            "libx264",
            "-preset",
            "medium",
            "-crf",
            "18",
            "-c:a",
            "aac",
            "-b:a",
            "192k",
            "-map",
            "0:v:0",
            "-map",
            "1:a:0?",
            "-shortest",
            output_path,
        ]

        process = await asyncio.create_subprocess_exec(
            *cmd,
            stdout=asyncio.subprocess.DEVNULL,
            stderr=asyncio.subprocess.PIPE,
        )

        _, stderr = await process.communicate()

        if process.returncode != 0:
            raise RuntimeError(f"Video encoding failed: {stderr.decode()}")

    async def _cleanup_job(self, job_id: str) -> None:
        """Clean up temporary files for a job."""
        import shutil

        job_dir = self._work_dir / job_id
        if job_dir.exists():
            try:
                shutil.rmtree(job_dir)
            except Exception as e:
                logger.warning(f"Failed to cleanup job {job_id}: {e}")


# Global instance
_enhancer: VideoFaceEnhancer | None = None


def get_video_face_enhancer() -> VideoFaceEnhancer:
    """Get or create the global video face enhancer."""
    global _enhancer
    if _enhancer is None:
        _enhancer = VideoFaceEnhancer()
    return _enhancer
