from __future__ import annotations

r"""
So-VITS-SVC 4.0 Engine for VoiceStudio
Voice conversion using So-VITS-SVC 4.0 checkpoints

So-VITS-SVC 4.0 provides high-quality voice conversion with checkpoint-based models.
Checkpoint layout: models\checkpoints\<project>\model.pth + config.json

Compatible with:
- Python 3.10+
- PyTorch 2.0+
- So-VITS-SVC 4.0 checkpoints
"""

import json
import logging
import os
import string
import subprocess
import tempfile
from pathlib import Path
from typing import Any

import numpy as np

# Optional dependency placeholders (overridden by actual imports when available)
torch: Any = None
librosa: Any = None
enhance_voice_quality: Any = None
normalize_lufs: Any = None
remove_artifacts: Any = None
calculate_all_metrics: Any = None

# Initialize logger early
logger = logging.getLogger(__name__)


def _format_template_to_args(template: str, **kwargs: str | int) -> list[str]:
    """Build subprocess args list from format template without shell parsing.

    Parses {placeholder} template and produces [executable, arg1, arg2, ...]
    with each value as a single argument. Avoids shell=True and shlex.split.
    """
    formatter = string.Formatter()
    args: list[str] = []
    for literal, field_name, _format_spec, _conversion in formatter.parse(template):
        if literal:
            args.extend(literal.split())
        if field_name is not None:
            val = kwargs.get(field_name, "")
            args.append(str(val) if val is not None else "")
    return args


# Try importing PyTorch
try:
    import torch

    HAS_TORCH = True
except ImportError:
    HAS_TORCH = False
    logger.warning("PyTorch not available. So-VITS-SVC engine will be limited.")

# Try importing librosa for audio processing
try:
    import librosa

    HAS_LIBROSA = True
except ImportError:
    HAS_LIBROSA = False
    logger.warning("librosa not available. Audio processing will be limited.")

# Try importing soundfile
try:
    import soundfile as _sf_mod

    HAS_SOUNDFILE = True
except ImportError:
    HAS_SOUNDFILE = False
    _sf_mod = None
    logger.warning("soundfile not available. Audio I/O will be limited.")
sf = _sf_mod

# Try importing audio utilities for quality enhancement
try:
    from app.core.audio.audio_utils import enhance_voice_quality, normalize_lufs, remove_artifacts

    HAS_AUDIO_UTILS = True
except ImportError:
    HAS_AUDIO_UTILS = False
    logger.warning("Audio utilities not available. Quality enhancement will be limited.")

# Try importing quality metrics
try:
    from .quality_metrics import calculate_all_metrics

    HAS_QUALITY_METRICS = True
except ImportError:
    HAS_QUALITY_METRICS = False
    logger.warning("Quality metrics not available. Quality calculation will be limited.")

# Import base protocol
try:
    from .protocols import EngineProtocol
except ImportError:
    from .base import EngineProtocol


class SoVITSSVCEngine(EngineProtocol):
    r"""
    So-VITS-SVC 4.0 Voice Conversion Engine.

    Features:
    - Voice conversion using So-VITS-SVC 4.0 checkpoints
    - Checkpoint layout: models\checkpoints\<project>\model.pth + config.json
    - Real-time voice conversion support
    - High-quality voice transformation
    """

    DEFAULT_SAMPLE_RATE = 22050

    def __init__(
        self,
        checkpoint_path: str | None = None,
        config_path: str | None = None,
        project_name: str | None = None,
        device: str | None = None,
        gpu: bool = True,
        sample_rate: int = 22050,
        infer_command: str | None = None,
        infer_workdir: str | None = None,
        allow_passthrough: bool = False,
    ):
        """
        Initialize So-VITS-SVC engine.

        Args:
            checkpoint_path: Path to model.pth checkpoint file
            config_path: Path to config.json file
            project_name: Project name (used to construct default paths)
            device: Device to use ('cuda', 'cpu', or None for auto)
            gpu: Whether to use GPU if available
            sample_rate: Sample rate for processing (default 22050)
            infer_command: Optional inference command template
            infer_workdir: Optional working directory for inference command
            allow_passthrough: Allow passthrough when inference unavailable
        """
        super().__init__(device=device, gpu=gpu)

        # Override device if GPU requested and available
        if gpu and HAS_TORCH and torch is not None and torch.cuda.is_available():
            if self.device == "cpu":
                self.device = "cuda"

        # Determine checkpoint and config paths
        models_root = os.getenv("VOICESTUDIO_MODELS_PATH", r"E:\VoiceStudio\models")

        if checkpoint_path:
            self.checkpoint_path = Path(checkpoint_path)
        elif project_name:
            self.checkpoint_path = Path(models_root) / "checkpoints" / project_name / "model.pth"
        else:
            # Default to MyVoiceProj
            self.checkpoint_path = Path(models_root) / "checkpoints" / "MyVoiceProj" / "model.pth"

        if config_path:
            self.config_path = Path(config_path)
        else:
            # Default to config.json in same directory as checkpoint
            self.config_path = self.checkpoint_path.parent / "config.json"

        self.sample_rate = sample_rate
        self.model = None
        self.config = None
        self._initialized = False
        self.infer_command = infer_command or os.getenv("SOVITS_SVC_INFER_COMMAND")
        self.infer_workdir = infer_workdir or os.getenv("SOVITS_SVC_WORKDIR")
        self.allow_passthrough = allow_passthrough

    def initialize(self) -> bool:
        """
        Initialize So-VITS-SVC model and load checkpoint.

        Returns:
            True if initialization successful, False otherwise
        """
        try:
            if self._initialized:
                return True

            if not HAS_TORCH or torch is None:
                if self.infer_command:
                    logger.info(
                        "PyTorch not available; using external So-VITS-SVC inference command."
                    )
                else:
                    logger.error("PyTorch is required for So-VITS-SVC engine")
                    return False

            # Verify checkpoint and config exist
            if not self.checkpoint_path.exists():
                logger.error(
                    f"So-VITS-SVC checkpoint not found: {self.checkpoint_path}. "
                    f"Place model.pth at: {self.checkpoint_path}"
                )
                return False

            if not self.config_path.exists():
                logger.error(
                    f"So-VITS-SVC config not found: {self.config_path}. "
                    f"Place config.json at: {self.config_path}"
                )
                return False

            # Load config
            try:
                with open(self.config_path, encoding="utf-8") as f:
                    self.config = json.load(f)
                logger.info(f"Loaded So-VITS-SVC config from {self.config_path}")
            except Exception as e:
                logger.error(f"Failed to load So-VITS-SVC config: {e}")
                return False

            if self.infer_command:
                self._initialized = True
                logger.info(
                    "So-VITS-SVC inference command configured; " "skipping torch checkpoint load."
                )
                return True

            # Load checkpoint
            try:
                device_obj = torch.device(self.device)
                checkpoint = torch.load(
                    str(self.checkpoint_path),
                    map_location=device_obj,
                )
                self.model = checkpoint
                logger.info(
                    f"Loaded So-VITS-SVC checkpoint from {self.checkpoint_path} "
                    f"(device: {self.device})"
                )
            except Exception as e:
                logger.error(f"Failed to load So-VITS-SVC checkpoint: {e}")
                return False

            self._initialized = True
            logger.info("So-VITS-SVC engine initialized successfully")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize So-VITS-SVC engine: {e}")
            self._initialized = False
            return False

    def cleanup(self):
        """Clean up resources and free memory."""
        if self.model is not None:
            del self.model
            self.model = None
        if torch is not None and torch.cuda.is_available():
            torch.cuda.empty_cache()
        self._initialized = False
        logger.info("So-VITS-SVC engine cleaned up")

    def _load_audio_path(self, path: str) -> tuple[np.ndarray, int] | None:
        if HAS_LIBROSA and librosa is not None:
            audio, sr = librosa.load(path, sr=self.sample_rate)
            return np.asarray(audio), int(sr)
        if HAS_SOUNDFILE and sf is not None:
            audio, sr = sf.read(path)
            return np.asarray(audio), int(sr)
        return None

    def _run_infer_command(
        self,
        input_path: str,
        output_path: str,
        pitch_shift: int,
        *,
        checkpoint_path: Path | None = None,
        config_path: Path | None = None,
    ) -> None:
        if not self.infer_command:
            raise RuntimeError("So-VITS-SVC inference command not configured.")

        checkpoint = checkpoint_path or self.checkpoint_path
        config = config_path or self.config_path
        format_kwargs: dict[str, str | int] = {
            "input": input_path,
            "output": output_path,
            "checkpoint": str(checkpoint) if checkpoint else "",
            "config": str(config) if config else "",
            "pitch_shift": pitch_shift,
            "device": self.device,
            "sample_rate": self.sample_rate,
        }
        args = _format_template_to_args(self.infer_command, **format_kwargs)
        result = subprocess.run(
            args,
            shell=False,
            cwd=self.infer_workdir or None,
            capture_output=True,
            text=True,
        )
        if result.returncode != 0:
            stdout = (result.stdout or "").strip()
            stderr = (result.stderr or "").strip()
            details = "; ".join(part for part in (stdout, stderr) if part)
            if details:
                raise RuntimeError(
                    f"So-VITS-SVC inference failed (exit {result.returncode}): {details}"
                )
            raise RuntimeError(f"So-VITS-SVC inference failed (exit {result.returncode}).")

    def convert_voice(
        self,
        source_audio: str | Path | np.ndarray,
        target_speaker_model: str | None = None,
        output_path: str | None = None,
        pitch_shift: int = 0,
        enhance_quality: bool = True,
        calculate_quality: bool = False,
        **kwargs,
    ) -> np.ndarray | tuple[np.ndarray, dict[str, Any]] | None:
        """
        Convert voice using So-VITS-SVC 4.0 model.

        Args:
            source_audio: Source audio file path or numpy array
            target_speaker_model: Target speaker model (optional, uses loaded model if None)
            output_path: Optional output file path
            pitch_shift: Pitch shift in semitones (-12 to 12)
            enhance_quality: If True, apply quality enhancement
            calculate_quality: If True, return quality metrics
            **kwargs: Additional parameters

        Returns:
            Converted audio array or None if conversion failed,
            or tuple of (audio, quality_metrics) if calculate_quality=True
        """
        temp_input_path = None
        temp_output_path = None

        # Lazy load models if needed
        if not self._initialized and not self.initialize():
            return None

        try:
            # Load source audio
            if isinstance(source_audio, (str, Path)):
                if HAS_LIBROSA:
                    audio, sr = librosa.load(str(source_audio), sr=self.sample_rate)
                elif HAS_SOUNDFILE and sf is not None:
                    audio, sr = sf.read(str(source_audio))
                    if sr != self.sample_rate:
                        logger.warning(
                            f"Sample rate mismatch: {sr} != {self.sample_rate}. "
                            "Resampling not available without librosa."
                        )
                else:
                    logger.error("Cannot load audio: librosa or soundfile required")
                    return None
            else:
                audio = source_audio
                sr = self.sample_rate

            # Validate audio
            if len(audio) == 0:
                logger.error("Source audio is empty")
                return None

            # Convert to mono if stereo
            if len(audio.shape) > 1:
                audio = np.mean(audio, axis=1)

            # Convert to float32
            if audio.dtype != np.float32:
                audio = audio.astype(np.float32)

            conversion_warning = None
            converted_audio = None

            # Prepare input/output paths for external inference if configured
            if HAS_SOUNDFILE and sf is not None:
                temp_input_path = tempfile.mktemp(suffix=".wav")
                sf.write(temp_input_path, audio, self.sample_rate)
                input_path = temp_input_path
            elif isinstance(source_audio, (str, Path)):
                input_path = str(source_audio)
            else:
                logger.error("So-VITS-SVC inference requires soundfile to write temp input.")
                return None

            infer_output_path = output_path or tempfile.mktemp(suffix=".wav")
            if output_path is None:
                temp_output_path = infer_output_path

            if self.infer_command:
                active_checkpoint = self.checkpoint_path
                active_config = self.config_path
                if target_speaker_model:
                    target_path = Path(target_speaker_model)
                    if target_path.is_dir():
                        active_checkpoint = target_path / "model.pth"
                        active_config = target_path / "config.json"
                    else:
                        active_checkpoint = target_path
                        active_config = target_path.parent / "config.json"

                if not active_checkpoint or not active_checkpoint.exists():
                    logger.error(
                        "So-VITS-SVC checkpoint not found: %s",
                        active_checkpoint,
                    )
                    return None
                if not active_config or not active_config.exists():
                    logger.error(
                        "So-VITS-SVC config not found: %s",
                        active_config,
                    )
                    return None
                try:
                    self._run_infer_command(
                        input_path,
                        infer_output_path,
                        pitch_shift,
                        checkpoint_path=active_checkpoint,
                        config_path=active_config,
                    )
                except Exception as e:
                    logger.error(f"So-VITS-SVC inference failed: {e}")
                    if not self.allow_passthrough:
                        return None
                    conversion_warning = str(e)
                if os.path.exists(infer_output_path):
                    loaded = self._load_audio_path(infer_output_path)
                    if loaded is None:
                        logger.error(
                            "So-VITS-SVC inference produced audio but no loader is available."
                        )
                        if not self.allow_passthrough:
                            return None
                    else:
                        converted_audio, out_sr = loaded
                        if out_sr != self.sample_rate and HAS_LIBROSA and librosa:
                            converted_audio = librosa.resample(
                                converted_audio,
                                orig_sr=out_sr,
                                target_sr=self.sample_rate,
                            )
            else:
                logger.error(
                    "So-VITS-SVC inference command not configured. "
                    "Set SOVITS_SVC_INFER_COMMAND or pass infer_command."
                )
                if not self.allow_passthrough:
                    return None
                conversion_warning = "So-VITS-SVC inference not configured"

            if converted_audio is None:
                if not self.allow_passthrough:
                    return None
                converted_audio = audio.copy()

            if converted_audio.dtype != np.float32:
                converted_audio = converted_audio.astype(np.float32)

            # Apply quality processing if requested (matching RVC engine pattern)
            if enhance_quality or calculate_quality:
                quality_result = self._process_audio_quality(
                    converted_audio,
                    self.sample_rate,
                    enhance_quality,
                    calculate_quality,
                )
                if isinstance(quality_result, tuple):
                    enhanced_audio, quality_metrics = quality_result
                    if conversion_warning and isinstance(quality_metrics, dict):
                        quality_metrics["conversion_warning"] = conversion_warning
                    if output_path:
                        if HAS_SOUNDFILE and sf is not None:
                            sf.write(output_path, enhanced_audio, self.sample_rate)
                        else:
                            logger.error("Cannot save audio: soundfile required")
                            return None
                        if calculate_quality:
                            return enhanced_audio, quality_metrics
                        return None
                    if calculate_quality:
                        return enhanced_audio, quality_metrics
                    return enhanced_audio
                else:
                    converted_audio = quality_result
                    if output_path:
                        if HAS_SOUNDFILE and sf is not None:
                            sf.write(output_path, converted_audio, self.sample_rate)
                        else:
                            logger.error("Cannot save audio: soundfile required")
                            return None
                        return None
                    return converted_audio

            # Save to file if requested
            if output_path:
                if HAS_SOUNDFILE and sf is not None:
                    sf.write(output_path, converted_audio, self.sample_rate)
                    logger.info(f"Audio saved to: {output_path}")
                else:
                    logger.error("Cannot save audio: soundfile required")
                    return None
                return None

            return converted_audio

        except Exception as e:
            logger.error(f"So-VITS-SVC voice conversion failed: {e}", exc_info=True)
            return None
        finally:
            for temp_path in (temp_input_path, temp_output_path):
                if temp_path and os.path.exists(temp_path):
                    try:
                        os.remove(temp_path)
                    except Exception:
                        logger.debug(
                            "Failed to remove temporary So-VITS-SVC file: %s",
                            temp_path,
                        )

    def convert_realtime(
        self,
        audio_chunk: np.ndarray,
        target_speaker_model: str | None = None,
        pitch_shift: int = 0,
        **kwargs,
    ) -> np.ndarray:
        """
        Real-time voice conversion for streaming.

        Args:
            audio_chunk: Audio chunk to convert (numpy array)
            target_speaker_model: Path to target speaker model
            pitch_shift: Pitch shift in semitones
            **kwargs: Additional parameters

        Returns:
            Converted audio chunk
        """
        if not self._initialized and not self.initialize():
            return audio_chunk  # Return original if initialization fails

        try:
            # Validate input
            if len(audio_chunk) == 0:
                return audio_chunk

            # Real-time conversion requires a streaming inference path and stateful buffering.
            # Current behavior returns the original audio chunk.
            logger.warning(
                "So-VITS-SVC 4.0 real-time conversion is disabled; returning input audio chunk."
            )
            return audio_chunk

        except Exception as e:
            logger.error(f"Real-time So-VITS-SVC conversion failed: {e}")
            return audio_chunk  # Return original on error

    def _process_audio_quality(
        self,
        audio: np.ndarray,
        sample_rate: int,
        enhance: bool,
        calculate: bool,
    ) -> np.ndarray | tuple[np.ndarray, dict[str, Any]]:
        """
        Process audio for quality enhancement and/or metrics calculation.

        Matches RVC engine quality processing pattern for consistency.

        Args:
            audio: Audio array to process
            sample_rate: Sample rate in Hz
            enhance: If True, apply quality enhancement
            calculate: If True, calculate quality metrics

        Returns:
            Enhanced audio (if enhance=True), or tuple of (audio, quality_metrics) if calculate=True
        """
        quality_metrics = {}

        if enhance and HAS_AUDIO_UTILS:
            try:
                # Advanced quality enhancement pipeline (matching RVC pattern)
                # Step 1: Voice quality enhancement
                audio = enhance_voice_quality(audio, sample_rate, normalize=True, denoise=True)

                # Step 2: LUFS normalization for broadcast standards
                audio = normalize_lufs(audio, sample_rate, target_lufs=-23.0)

                # Step 3: Artifact removal
                audio = remove_artifacts(audio, sample_rate)

                logger.debug("Quality enhancement applied to So-VITS-SVC output")
            except Exception as e:
                logger.warning(f"Quality enhancement failed: {e}")

        if calculate and HAS_QUALITY_METRICS and calculate_all_metrics is not None:
            try:
                # Calculate comprehensive quality metrics
                quality_metrics = calculate_all_metrics(
                    audio, sample_rate=self.sample_rate, use_cache=False
                )

                # Add So-VITS-SVC-specific quality indicators
                if HAS_LIBROSA:
                    try:
                        # Calculate spectral quality indicators
                        spectral_centroid = np.mean(
                            librosa.feature.spectral_centroid(y=audio, sr=sample_rate)[0]
                        )
                        spectral_rolloff = np.mean(
                            librosa.feature.spectral_rolloff(y=audio, sr=sample_rate)[0]
                        )
                        zero_crossing_rate = np.mean(librosa.feature.zero_crossing_rate(audio)[0])

                        quality_metrics["spectral_centroid"] = float(spectral_centroid)
                        quality_metrics["spectral_rolloff"] = float(spectral_rolloff)
                        quality_metrics["zero_crossing_rate"] = float(zero_crossing_rate)
                    except Exception as e:
                        logger.debug(f"Spectral quality indicators failed: {e}")

            except Exception as e:
                logger.warning(f"Quality metrics calculation failed: {e}")
                quality_metrics = {}

        if calculate:
            return audio, quality_metrics
        return audio

    def get_info(self) -> dict[str, Any]:
        """Get engine information."""
        info = super().get_info()
        info.update(
            {
                "engine_type": "So-VITS-SVC 4.0",
                "checkpoint_path": (str(self.checkpoint_path) if self.checkpoint_path else None),
                "config_path": str(self.config_path) if self.config_path else None,
                "sample_rate": self.sample_rate,
                "inference_configured": bool(self.infer_command),
                "inference_workdir": self.infer_workdir,
                "allow_passthrough": self.allow_passthrough,
            }
        )
        return info


def create_sovits_svc_engine(
    checkpoint_path: str | None = None,
    config_path: str | None = None,
    project_name: str | None = None,
    device: str | None = None,
    gpu: bool = True,
    **kwargs,
) -> SoVITSSVCEngine:
    """
    Factory function to create So-VITS-SVC engine.

    Args:
        checkpoint_path: Path to model.pth checkpoint file
        config_path: Path to config.json file
        project_name: Project name (used to construct default paths)
        device: Device to use ('cuda', 'cpu', or None for auto)
        gpu: Whether to use GPU if available
        **kwargs: Additional parameters

    Returns:
        SoVITSSVCEngine instance
    """
    return SoVITSSVCEngine(
        checkpoint_path=checkpoint_path,
        config_path=config_path,
        project_name=project_name,
        device=device,
        gpu=gpu,
        **kwargs,
    )
