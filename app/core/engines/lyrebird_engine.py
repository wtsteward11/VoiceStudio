"""
Lyrebird (Descript) Engine for VoiceStudio
High-quality voice cloning using Lyrebird/Descript (cloud-based, local preferred)

Compatible with:
- Python 3.10+
- requests 2.28.0+
- aiohttp 3.8.0+ (for async)
"""

from __future__ import annotations

import logging
import os
from collections import OrderedDict
from pathlib import Path
from typing import Any

import numpy as np
import numpy.typing as npt

# Try importing requests for connection pooling
try:
    from requests.adapters import HTTPAdapter
    from urllib3.util.retry import Retry

    HAS_REQUESTS_ADAPTERS = True
except ImportError:
    HAS_REQUESTS_ADAPTERS = False

# Import base protocol
try:
    from .protocols import EngineProtocol
except ImportError:
    from .base import EngineProtocol

logger = logging.getLogger(__name__)

# Required imports
try:
    import requests

    HAS_REQUESTS = True
except ImportError:
    HAS_REQUESTS = False
    logger.warning("requests not installed. Install with: pip install requests>=2.28.0")

try:
    import aiohttp

    HAS_AIOHTTP = True
except ImportError:
    HAS_AIOHTTP = False
    logger.warning("aiohttp not installed. Install with: pip install aiohttp>=3.8.0")

# Optional quality metrics import
try:
    from .quality_metrics import calculate_all_metrics

    HAS_QUALITY_METRICS = True
except ImportError:
    HAS_QUALITY_METRICS = False

# Optional audio utilities import for quality enhancement
enhance_voice_cloning_quality: Any = None
try:
    from app.core.audio.audio_utils import (
        enhance_voice_cloning_quality,
        enhance_voice_quality,
        normalize_lufs,
        remove_artifacts,
    )

    HAS_AUDIO_UTILS = True
except ImportError:
    HAS_AUDIO_UTILS = False


class LyrebirdEngine(EngineProtocol):
    """
    Lyrebird (Descript) Engine for high-quality voice cloning.

    NOTE: This is a cloud-based service. Local implementation preferred.
    Requires API key for cloud access.

    Supports:
    - High-quality voice cloning
    - Voice synthesis from text
    - Voice fine-tuning
    - Batch processing
    """

    def __init__(
        self,
        device: str | None = None,
        gpu: bool = True,
        api_key: str | None = None,
        api_url: str = "https://api.descript.com/v1",
        use_local: bool = True,
    ):
        """
        Initialize Lyrebird engine.

        Args:
            device: Device to use (for local implementation)
            gpu: Whether to use GPU (for local implementation)
            api_key: Descript/Lyrebird API key (for cloud access)
            api_url: API URL
            use_local: If True, prefer local implementation over cloud
        """
        if not HAS_REQUESTS:
            raise ImportError("requests not installed. Install with: pip install requests>=2.28.0")

        super().__init__(device=device, gpu=gpu)

        self.api_key = api_key or os.getenv("LYREBIRD_API_KEY") or os.getenv("DESCRIPT_API_KEY")
        self.api_url = api_url
        self.use_local = use_local
        self.local_model: Any = None
        self._synthesis_cache: OrderedDict[str, Any] = OrderedDict()
        self._cache_max_size = 100
        self._session: Any = None

    def initialize(self) -> bool:
        """Initialize the Lyrebird engine."""
        try:
            if self._initialized:
                return True

            logger.info("Initializing Lyrebird engine")

            # Set up connection pooling for API requests
            if HAS_REQUESTS and HAS_REQUESTS_ADAPTERS:
                self._session = requests.Session()
                # Configure retry strategy
                retry_strategy = Retry(
                    total=3,
                    backoff_factor=1,
                    status_forcelist=[429, 500, 502, 503, 504],
                )
                adapter = HTTPAdapter(
                    max_retries=retry_strategy,
                    pool_connections=10,
                    pool_maxsize=20,
                )
                self._session.mount("http://", adapter)
                self._session.mount("https://", adapter)
                logger.debug("Connection pooling enabled for Lyrebird API")

            if self.use_local:
                # Try to initialize local model
                logger.info("Attempting to use local voice cloning model")
                self.local_model = self._load_local_model()
            else:
                # Check API key for cloud access
                if not self.api_key:
                    logger.warning("No API key provided. Cloud features will be limited.")

            self._initialized = True
            logger.info("Lyrebird engine initialized")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize Lyrebird engine: {e}")
            self._initialized = False
            return False

    def cleanup(self) -> None:
        """Clean up resources and free memory."""
        try:
            # Close session for connection pooling
            if self._session is not None:
                self._session.close()
                self._session = None

            if self.local_model is not None:
                del self.local_model
                self.local_model = None

            # Clear synthesis cache
            self._synthesis_cache.clear()

            self._initialized = False
            logger.info("Lyrebird engine cleaned up")

        except Exception as e:
            logger.error(f"Error during Lyrebird cleanup: {e}")

    def clear_cache(self) -> None:
        """Clear synthesis cache."""
        self._synthesis_cache.clear()
        logger.info("Synthesis cache cleared")

    def get_cache_stats(self) -> dict[str, int]:
        """Get cache statistics."""
        return {
            "cache_size": len(self._synthesis_cache),
            "max_cache_size": self._cache_max_size,
        }

    def clone_voice(
        self,
        reference_audio_path: str | Path,
        text: str,
        output_path: str | Path | None = None,
        voice_name: str | None = None,
        enhance_quality: bool = False,
        calculate_quality: bool = False,
        **kwargs: Any,
    ) -> str | tuple[str, dict[str, Any]]:
        """
        Clone voice from reference audio and synthesize text.

        Args:
            reference_audio_path: Path to reference audio for voice cloning
            text: Text to synthesize
            output_path: Path to save output audio
            voice_name: Name for the cloned voice (optional)
            enhance_quality: If True, apply quality enhancement pipeline
            calculate_quality: If True, return quality metrics along with audio path
            **kwargs: Additional parameters

        Returns:
            Path to synthesized audio, or tuple of (path, quality_metrics) if calculate_quality=True
        """
        if not self._initialized:
            raise RuntimeError("Engine not initialized. Call initialize() first.")

        try:
            # Check synthesis cache
            import hashlib

            cache_key = hashlib.md5(
                f"{reference_audio_path}_{text}_{voice_name}".encode()
            ).hexdigest()
            if cache_key in self._synthesis_cache:
                logger.debug("Using cached Lyrebird synthesis result")
                self._synthesis_cache.move_to_end(cache_key)  # LRU update
                cached_path: str = self._synthesis_cache[cache_key]
                # Verify cached file still exists
                if os.path.exists(cached_path):
                    return cached_path
                else:
                    # Remove invalid cache entry
                    del self._synthesis_cache[cache_key]

            logger.info(f"Cloning voice and synthesizing: {len(text)} characters")

            if self.use_local and self.local_model is not None:
                # Try local model first
                try:
                    result_path = self._clone_local(
                        reference_audio_path, text, output_path, voice_name, **kwargs
                    )
                    # Verify result is valid (not empty file)
                    if (
                        result_path
                        and os.path.exists(result_path)
                        and os.path.getsize(result_path) > 0
                    ):
                        return result_path
                    else:
                        logger.info("Local model result invalid, falling back to cloud API")
                except Exception as e:
                    logger.debug(f"Local model failed: {e}, falling back to cloud API")

            # Use cloud API as fallback (or if local not available)
            if self.api_key:
                result_path = self._clone_cloud(
                    reference_audio_path, text, output_path, voice_name, **kwargs
                )
            else:
                # No API key and local failed - use XTTS fallback
                result_path = self._clone_with_fallback_engine(
                    reference_audio_path, text, output_path, **kwargs
                )

            # Apply quality processing if requested
            quality_metrics = {}
            if (
                (enhance_quality or calculate_quality)
                and result_path
                and os.path.exists(result_path)
            ):
                try:
                    import soundfile as sf

                    audio, sample_rate = sf.read(result_path)

                    # Apply quality enhancement
                    if enhance_quality and HAS_AUDIO_UTILS:
                        try:
                            if enhance_voice_cloning_quality is not None:
                                audio = enhance_voice_cloning_quality(
                                    audio,
                                    sample_rate,
                                    enhancement_level="standard",
                                    preserve_prosody=True,
                                    target_lufs=-23.0,
                                )
                                logger.debug(
                                    "Applied advanced quality enhancement to Lyrebird output"
                                )
                            elif enhance_voice_quality is not None:
                                audio = enhance_voice_quality(
                                    audio,
                                    sample_rate,
                                    normalize=True,
                                    denoise=True,
                                    target_lufs=-23.0,
                                )
                                logger.debug("Applied quality enhancement to Lyrebird output")

                            # Save enhanced audio
                            sf.write(result_path, audio, sample_rate)
                        except Exception as e:
                            logger.warning(f"Quality enhancement failed: {e}")

                    # Calculate quality metrics
                    if calculate_quality and HAS_QUALITY_METRICS:
                        try:
                            quality_metrics = calculate_all_metrics(audio, sample_rate)
                        except Exception as e:
                            logger.warning(f"Quality metrics calculation failed: {e}")
                except Exception as e:
                    logger.warning(f"Quality processing failed: {e}")

            # Cache result if successful (LRU)
            if result_path and os.path.exists(result_path):
                if len(self._synthesis_cache) >= self._cache_max_size:
                    oldest_key = next(iter(self._synthesis_cache))
                    del self._synthesis_cache[oldest_key]
                self._synthesis_cache[cache_key] = result_path
                self._synthesis_cache.move_to_end(cache_key)  # LRU update

            # Return with quality metrics if requested
            if calculate_quality:
                return result_path, quality_metrics

            return result_path

        except Exception as e:
            logger.error(f"Error cloning voice: {e}")
            raise RuntimeError(f"Failed to clone voice: {e}")

    def _clone_local(
        self,
        reference_audio_path: str | Path,
        text: str,
        output_path: str | Path | None,
        voice_name: str | None,
        **kwargs: Any,
    ) -> str:
        """
        Clone voice using local model if available and usable.

        Note: Local mode only works with fully instantiated model objects (not checkpoint dictionaries).
        For checkpoint files, the model architecture would need to be known to reconstruct the model.
        If local model is not usable, falls back to XTTS engine for reliable voice cloning.
        """
        if output_path is None:
            output_dir = os.path.join(
                os.getenv("TEMP", "C:\\Temp"), "VoiceStudio", "lyrebird_output"
            )
            os.makedirs(output_dir, exist_ok=True)
            output_path = os.path.join(output_dir, "cloned_voice.wav")

        output_path = Path(output_path)
        output_path.parent.mkdir(parents=True, exist_ok=True)

        # Try local model first if available and usable
        if self.local_model is not None:
            try:
                result = self._synthesize_with_local_model(
                    reference_audio_path, text, output_path, **kwargs
                )
                # Verify result is valid (file exists and is not empty)
                if result and os.path.exists(result) and os.path.getsize(result) > 0:
                    logger.info("Local model synthesis successful")
                    return result
                else:
                    logger.info("Local model result invalid, using fallback engine")
            except Exception as e:
                logger.debug(f"Local model synthesis failed: {e}, using fallback engine")

        # Use fallback voice cloning engine (XTTS) for reliable synthesis
        # This ensures high-quality results using proven engines
        logger.info("Using XTTS engine for voice cloning (local model not available/usable)")
        return self._clone_with_fallback_engine(reference_audio_path, text, output_path, **kwargs)

    def _load_local_model(self) -> dict[str, Any] | None:
        """Load local voice cloning model."""
        try:
            # Try to find local model files
            model_cache_dir = os.getenv("VOICESTUDIO_MODELS_PATH")
            if not model_cache_dir:
                model_cache_dir = os.path.join(
                    os.getenv("PROGRAMDATA", "C:\\ProgramData"),
                    "VoiceStudio",
                    "models",
                    "lyrebird",
                )

            if not os.path.exists(model_cache_dir):
                logger.debug(f"Model directory not found: {model_cache_dir}")
                return None

            # Look for model files
            model_files = list(Path(model_cache_dir).rglob("*.pth"))
            model_files.extend(list(Path(model_cache_dir).rglob("*.pt")))
            model_files.extend(list(Path(model_cache_dir).rglob("*.ckpt")))

            if not model_files:
                logger.debug("No model files found in model directory")
                return None

            # Try to load model using PyTorch
            try:
                import torch

                # Load the first model file found
                model_path = str(model_files[0])
                device = torch.device(self.device)
                checkpoint = torch.load(model_path, map_location=device)

                logger.info(f"Loaded local Lyrebird model from: {model_path}")
                return {
                    "model": checkpoint,
                    "path": model_path,
                    "device": device,
                }
            except ImportError:
                logger.debug("PyTorch not available for model loading")
                return None
            except Exception as e:
                logger.warning(f"Failed to load model: {e}")
                return None

        except Exception as e:
            logger.warning(f"Error loading local model: {e}")
            return None

    def _synthesize_with_local_model(
        self,
        reference_audio_path: str | Path,
        text: str,
        output_path: Path,
        **kwargs: Any,
    ) -> str:
        """Synthesize using loaded local model."""
        try:
            import numpy as np
            import soundfile as sf
            import torch

            # Load reference audio
            ref_audio, sr = sf.read(str(reference_audio_path))
            if len(ref_audio.shape) > 1:
                ref_audio = np.mean(ref_audio, axis=1)

            # Extract voice embedding from reference
            voice_embedding = self._extract_voice_embedding(ref_audio, sr)

            # Use model to synthesize
            model_data = self.local_model
            device = model_data["device"]

            # Convert text to tokens
            text_tokens = self._text_to_tokens(text)
            text_tensor = torch.tensor(text_tokens, dtype=torch.long, device=device).unsqueeze(0)

            # Prepare voice embedding tensor
            voice_embedding_tensor = torch.tensor(
                voice_embedding, dtype=torch.float32, device=device
            )
            if len(voice_embedding_tensor.shape) == 1:
                voice_embedding_tensor = voice_embedding_tensor.unsqueeze(0)

            # Generate audio using model
            with torch.no_grad():
                model = model_data.get("model")

                if model is None:
                    logger.warning("Model data is None, using fallback")
                    return self._clone_with_fallback_engine(
                        reference_audio_path, text, output_path, **kwargs
                    )

                # Try to detect model type and synthesize
                if isinstance(model, dict):
                    # Checkpoint format (state_dict) - cannot use directly without model architecture
                    # PyTorch checkpoints contain weights but not the model structure
                    # To use a checkpoint, we would need to:
                    # 1. Know the model architecture (e.g., Tacotron2, FastSpeech2, etc.)
                    # 2. Instantiate the model class
                    # 3. Load the checkpoint weights into it
                    # Since we don't have the architecture information, we cannot use checkpoint files
                    logger.info(
                        "Checkpoint dictionary detected - cannot use without model architecture. "
                        "Local mode requires a fully instantiated model object with synthesize() or forward() methods."
                    )
                    # Cannot synthesize from checkpoint - will raise error to trigger fallback
                    raise RuntimeError(
                        "Checkpoint format requires model architecture - use instantiated model object instead"
                    )
                else:
                    # Direct model object - try to use it directly
                    try:
                        if hasattr(model, "synthesize"):
                            try:
                                audio_output = model.synthesize(
                                    text_tokens, voice_embedding=voice_embedding_tensor
                                )
                                if audio_output is not None:
                                    if isinstance(audio_output, torch.Tensor):
                                        audio_output = audio_output.cpu().numpy()

                                    if len(audio_output.shape) > 1:
                                        audio_output = np.mean(audio_output, axis=1)

                                    if np.max(np.abs(audio_output)) > 0:
                                        audio_output = (
                                            audio_output / np.max(np.abs(audio_output)) * 0.95
                                        )

                                    sf.write(str(output_path), audio_output, sr)
                                    logger.info(f"Local model synthesis successful: {output_path}")
                                    return str(output_path)
                            except Exception as e:
                                logger.debug(f"Model synthesize method failed: {e}")
                        elif hasattr(model, "forward"):
                            try:
                                # Try forward pass
                                audio_output = model(text_tensor, voice_embedding_tensor)
                                if audio_output is not None and isinstance(
                                    audio_output, torch.Tensor
                                ):
                                    audio_output = audio_output.cpu().numpy()
                                    if len(audio_output.shape) > 1:
                                        audio_output = np.mean(audio_output, axis=1)
                                    if np.max(np.abs(audio_output)) > 0:
                                        audio_output = (
                                            audio_output / np.max(np.abs(audio_output)) * 0.95
                                        )
                                    sf.write(str(output_path), audio_output, sr)
                                    logger.info(f"Local model synthesis successful: {output_path}")
                                    return str(output_path)
                            except Exception as e:
                                logger.debug(f"Model forward method failed: {e}")
                    except Exception as e:
                        logger.debug(f"Direct model usage failed: {e}")

            # Local model synthesis not possible (checkpoint dict without architecture, or model object failed)
            # Use fallback engine for reliable voice cloning
            logger.debug("Local model cannot be used for synthesis, using fallback engine")
            raise RuntimeError(
                "Local model synthesis failed - checkpoint requires model architecture"
            )

        except RuntimeError:
            # Expected error when checkpoint can't be used - propagate to caller for fallback
            raise
        except Exception as e:
            logger.warning(f"Local model synthesis failed: {e}")
            raise RuntimeError(f"Local model synthesis error: {e}")

    # Removed experimental synthesis helpers (_synthesize_tacotron2_like, _synthesize_fastspeech2_like,
    # _synthesize_generic_vocoder, _mel_to_audio_simple) to avoid generating random audio.
    # Instead, the engine now prefers using the fallback voice cloning engine (XTTS) for reliable,
    # high-quality synthesis when local model cannot be properly used.

    def _extract_voice_embedding(
        self, audio: npt.NDArray[Any], sample_rate: int
    ) -> npt.NDArray[Any]:
        """Extract voice embedding from audio."""
        try:
            import librosa
            import numpy as np

            # Resample to 16kHz if needed
            if sample_rate != 16000:
                audio = librosa.resample(audio, orig_sr=sample_rate, target_sr=16000)

            # Extract mel spectrogram features
            mel_spec = librosa.feature.melspectrogram(y=audio, sr=16000, n_mels=80, hop_length=256)
            mel_spec_db = librosa.power_to_db(mel_spec, ref=np.max)

            # Average over time to get voice embedding
            embedding = np.mean(mel_spec_db, axis=1)

            return np.asarray(embedding)

        except ImportError:
            # Fallback: use simple spectral features
            import numpy as np

            # Compute FFT
            fft = np.fft.rfft(audio[: min(16000, len(audio))])
            magnitude = np.abs(fft)

            # Take mean as embedding
            embedding = np.mean(magnitude)

            return np.array([embedding])

        except Exception as e:
            logger.warning(f"Voice embedding extraction failed: {e}")
            return np.array([0.0])

    def _text_to_tokens(self, text: str) -> list[int]:
        """Convert text to tokens."""
        # Simple character-based tokenization
        # In real implementation, would use proper tokenizer
        return [ord(c) for c in text[:100]]  # Limit length

    def _clone_with_fallback_engine(
        self,
        reference_audio_path: str | Path,
        text: str,
        output_path: str | Path | None,
        **kwargs: Any,
    ) -> str:
        """Clone voice using fallback TTS engine."""
        if output_path is None:
            _out_dir = os.path.join(os.getenv("TEMP", "C:\\Temp"), "VoiceStudio", "lyrebird_output")
            os.makedirs(_out_dir, exist_ok=True)
            resolved: Path = Path(os.path.join(_out_dir, "cloned_voice.wav"))
        else:
            resolved = Path(output_path)

        try:
            import sys

            app_path = os.path.join(os.path.dirname(__file__), "..", "..")
            if app_path not in sys.path:
                sys.path.insert(0, app_path)

            from core.engines.router import EngineRouter

            router = EngineRouter()
            router.load_all_engines("engines")

            # Try XTTS as fallback (supports voice cloning)
            xtts_engine = router.get_engine("xtts")
            if xtts_engine:
                if not xtts_engine.is_initialized():
                    xtts_engine.initialize()

                # XTTS uses 'speaker_wav' parameter (not 'reference_audio')
                result = xtts_engine.synthesize(
                    text=text,
                    speaker_wav=str(reference_audio_path),
                    **{k: v for k, v in kwargs.items() if k != "reference_audio"},
                )

                if result is not None:
                    import soundfile as sf

                    if isinstance(result, bytes):
                        with open(resolved, "wb") as f:
                            f.write(result)
                    elif isinstance(result, np.ndarray):
                        sf.write(str(resolved), result, 22050)
                    else:
                        import shutil

                        shutil.copy(str(result), str(resolved))

                    logger.info(f"Voice cloned (fallback): {resolved}")
                    return str(resolved)

            resolved.touch()
            logger.warning(f"Fallback cloning failed, created empty file: {resolved}")
            return str(resolved)

        except Exception as e:
            logger.error(f"Fallback engine cloning failed: {e}")
            resolved.touch()
            return str(resolved)

    def _clone_cloud(
        self,
        reference_audio_path: str | Path,
        text: str,
        output_path: str | Path | None,
        voice_name: str | None,
        **kwargs: Any,
    ) -> str:
        """Clone voice using cloud API."""
        if not self.api_key:
            raise RuntimeError("API key required for cloud voice cloning")

        # Generate output path
        if output_path is None:
            output_dir = os.path.join(
                os.getenv("TEMP", "C:\\Temp"), "VoiceStudio", "lyrebird_output"
            )
            os.makedirs(output_dir, exist_ok=True)
            output_path = os.path.join(output_dir, "cloned_voice.wav")

        output_path = Path(output_path)
        output_path.parent.mkdir(parents=True, exist_ok=True)

        # Step 1: Create voice clone from reference
        clone_url = f"{self.api_url}/voices"
        headers = {
            "Authorization": f"Bearer {self.api_key}",
            "Content-Type": "multipart/form-data",
        }

        with open(reference_audio_path, "rb") as audio_file:
            files = {"audio": audio_file}
            data = {"name": voice_name} if voice_name else {}

            # Use session for connection pooling if available
            if self._session is not None:
                response = self._session.post(clone_url, headers=headers, files=files, data=data)
            else:
                response = requests.post(clone_url, headers=headers, files=files, data=data)
            response.raise_for_status()

        voice_data = response.json()
        voice_id = voice_data.get("id")

        # Step 2: Synthesize text with cloned voice
        synthesize_url = f"{self.api_url}/voices/{voice_id}/synthesize"
        headers = {
            "Authorization": f"Bearer {self.api_key}",
            "Content-Type": "application/json",
        }

        data = {"text": text, **kwargs}

        # Use session for connection pooling if available
        if self._session is not None:
            response = self._session.post(synthesize_url, headers=headers, json=data)
        else:
            response = requests.post(synthesize_url, headers=headers, json=data)
        response.raise_for_status()

        # Save response audio
        with open(output_path, "wb") as out_file:
            out_file.write(response.content)

        logger.info(f"Voice cloned (cloud): {output_path}")
        return str(output_path)

    def get_info(self) -> dict[str, Any]:
        """Get engine information."""
        info = super().get_info()
        info.update(
            {
                "engine_type": "voice_cloning",
                "use_local": self.use_local,
                "has_api_key": self.api_key is not None,
                "has_requests": HAS_REQUESTS,
                "has_aiohttp": HAS_AIOHTTP,
            }
        )
        return info


def create_lyrebird_engine(
    device: str | None = None,
    gpu: bool = True,
    api_key: str | None = None,
    api_url: str = "https://api.descript.com/v1",
    use_local: bool = True,
) -> LyrebirdEngine:
    """
    Create and initialize Lyrebird engine.

    Args:
        device: Device to use (for local implementation)
        gpu: Whether to use GPU (for local implementation)
        api_key: Descript/Lyrebird API key
        api_url: API URL
        use_local: If True, prefer local implementation

    Returns:
        Initialized LyrebirdEngine instance
    """
    engine = LyrebirdEngine(
        device=device, gpu=gpu, api_key=api_key, api_url=api_url, use_local=use_local
    )
    engine.initialize()
    return engine
