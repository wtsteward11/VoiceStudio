"""
Speaker Encoder Engine for VoiceStudio
Extract speaker embeddings and compare speaker similarity

Compatible with:
- Python 3.10+
- resemblyzer>=0.1.4
- speechbrain>=0.5.0
- torch>=2.0.0
"""

from __future__ import annotations

import hashlib
import logging
from collections import OrderedDict
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import numpy as np

# Try importing general model cache
_model_cache = None
try:
    from app.core.models.cache import get_model_cache

    _model_cache = get_model_cache(max_models=2, max_memory_mb=1024.0)  # 1GB max
    HAS_MODEL_CACHE = True
except ImportError:
    HAS_MODEL_CACHE = False

logger = logging.getLogger(__name__)

# Log cache availability
if not HAS_MODEL_CACHE:
    logger.debug("General model cache not available, using Speaker Encoder-specific cache")

# Fallback: Speaker Encoder-specific cache (for backward compatibility)
_SPEAKER_ENCODER_MODEL_CACHE: OrderedDict = OrderedDict()
_MAX_CACHE_SIZE = 2  # Maximum number of models to cache in memory


def _get_cache_key(backend: str, device: str) -> str:
    """Generate cache key for Speaker Encoder model."""
    return f"speaker_encoder::{backend}::{device}"


def _get_cached_speaker_encoder_model(backend: str, device: str):
    """Get cached Speaker Encoder model if available."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        cached = _model_cache.get("speaker_encoder", backend, device=device)
        if cached is not None:
            return cached

    # Fallback to Speaker Encoder-specific cache
    cache_key = _get_cache_key(backend, device)
    if cache_key in _SPEAKER_ENCODER_MODEL_CACHE:
        _SPEAKER_ENCODER_MODEL_CACHE.move_to_end(cache_key)
        return _SPEAKER_ENCODER_MODEL_CACHE[cache_key]
    return None


def _cache_speaker_encoder_model(backend: str, device: str, encoder_data: dict):
    """Cache Speaker Encoder model with LRU eviction."""
    # Try general model cache first
    if HAS_MODEL_CACHE and _model_cache is not None:
        try:
            _model_cache.set("speaker_encoder", backend, encoder_data, device=device)
            return
        except Exception as e:
            logger.warning(f"Failed to cache in general cache: {e}, using fallback")

    # Fallback to Speaker Encoder-specific cache
    cache_key = _get_cache_key(backend, device)

    if cache_key in _SPEAKER_ENCODER_MODEL_CACHE:
        _SPEAKER_ENCODER_MODEL_CACHE.move_to_end(cache_key)
        return

    _SPEAKER_ENCODER_MODEL_CACHE[cache_key] = encoder_data

    # Evict oldest if cache full
    if len(_SPEAKER_ENCODER_MODEL_CACHE) > _MAX_CACHE_SIZE:
        oldest_key, oldest_encoder = _SPEAKER_ENCODER_MODEL_CACHE.popitem(last=False)
        try:
            del oldest_encoder
            if HAS_TORCH and torch.cuda.is_available():
                torch.cuda.empty_cache()
            logger.debug(f"Evicted Speaker Encoder model from cache: {oldest_key}")
        except Exception as e:
            logger.warning(f"Error evicting Speaker Encoder model from cache: {e}")

    logger.debug(
        f"Cached Speaker Encoder model: {cache_key} (cache size: {len(_SPEAKER_ENCODER_MODEL_CACHE)})"
    )


# Import base protocol
try:
    from .protocols import EngineProtocol
except ImportError:
    from .base import EngineProtocol

logger = logging.getLogger(__name__)

# Try importing optional dependencies
try:
    import torch

    HAS_TORCH = True
except ImportError:
    HAS_TORCH = False
    logger.warning("torch not installed. Install with: pip install torch")

try:
    import soundfile as sf

    HAS_SOUNDFILE = True
except ImportError:
    HAS_SOUNDFILE = False
    logger.warning("soundfile not installed. Install with: pip install soundfile")

try:
    from resemblyzer import VoiceEncoder, preprocess_wav

    HAS_RESEMBLYZER = True
except ImportError:
    HAS_RESEMBLYZER = False
    logger.warning("resemblyzer not installed. " "Install with: pip install resemblyzer")

try:
    from speechbrain.inference.speaker import EncoderClassifier

    HAS_SPEECHBRAIN = True
except (ImportError, AttributeError):
    HAS_SPEECHBRAIN = False
    logger.warning(
        "speechbrain not available. "
        "Install with: pip install speechbrain "
        "(Note: requires compatible torchaudio version)"
    )

try:
    import librosa

    HAS_LIBROSA = True
except ImportError:
    HAS_LIBROSA = False
    logger.warning("librosa not installed. Install with: pip install librosa")

# Try importing umap-learn for embedding visualization
try:
    import umap

    HAS_UMAP = True
except ImportError:
    HAS_UMAP = False
    umap = None
    logger.debug("umap-learn not installed. Embedding visualization will be limited.")


class SpeakerEncoderEngine(EngineProtocol):
    """
    Speaker Encoder Engine for extracting speaker embeddings
    and comparing similarity.

    Supports:
    - Multiple backends (resemblyzer, speechbrain).
    - Speaker embedding extraction
    - Speaker similarity comparison
    - Embedding caching for performance
    - Batch processing
    """

    # Default sample rate for preprocessing
    DEFAULT_SAMPLE_RATE = 16000

    # Embedding dimensions for different backends
    RESEMBLYZER_DIM = 256
    SPEECHBRAIN_DIM = 192

    def __init__(
        self,
        backend: str = "resemblyzer",
        device: str | None = None,
        gpu: bool = True,
        enable_cache: bool = True,
        cache_size: int = 1000,
    ):
        """
        Initialize Speaker Encoder engine.

        Args:
            backend: Backend to use ('resemblyzer' or 'speechbrain')
            device: Device to use ('cuda', 'cpu', or None for auto)
            gpu: Whether to use GPU if available
            enable_cache: Enable embedding cache
            cache_size: Maximum cache size
        """
        super().__init__(device=device, gpu=gpu)

        self.backend = backend.lower()
        self.enable_cache = enable_cache
        self.cache_size = cache_size
        self.lazy_load = True
        self.batch_size = 4
        self._model_caching_enabled = True

        # Backend models
        self.resemblyzer_encoder = None
        self.speechbrain_encoder = None

        # Embedding cache (key: audio hash, value: embedding) - LRU
        self._embedding_cache: OrderedDict[str, np.ndarray] = OrderedDict()

        # Validate backend
        if self.backend == "resemblyzer" and not HAS_RESEMBLYZER:
            logger.warning("resemblyzer not available, falling back to speechbrain")
            self.backend = "speechbrain"

        if self.backend == "speechbrain" and not HAS_SPEECHBRAIN:
            if HAS_RESEMBLYZER:
                logger.warning("speechbrain not available, falling back to resemblyzer")
                self.backend = "resemblyzer"
            else:
                raise ImportError(
                    "Neither resemblyzer nor speechbrain is available. "
                    "Install at least one: pip install resemblyzer speechbrain"
                )

    def _load_model(self) -> bool:
        """Load model with caching support."""
        # Check cache first
        if self._model_caching_enabled:
            cached_encoders = _get_cached_speaker_encoder_model(self.backend, self.device)
            if cached_encoders is not None:
                logger.debug(f"Using cached Speaker Encoder model: {self.backend}")
                self.resemblyzer_encoder = cached_encoders.get("resemblyzer")
                self.speechbrain_encoder = cached_encoders.get("speechbrain")
                self._initialized = True
                return True

        logger.info(f"Loading Speaker Encoder " f"(backend: {self.backend}, device: {self.device})")

        # Initialize resemblyzer backend
        if self.backend == "resemblyzer" and HAS_RESEMBLYZER:
            try:
                self.resemblyzer_encoder = VoiceEncoder(device=self.device)
                logger.info("Resemblyzer encoder loaded")
            except Exception as e:
                logger.error(f"Failed to load resemblyzer encoder: {e}")
                if HAS_SPEECHBRAIN:
                    logger.info("Falling back to speechbrain")
                    self.backend = "speechbrain"
                else:
                    return False

        # Initialize speechbrain backend
        if self.backend == "speechbrain" and HAS_SPEECHBRAIN:
            try:
                self.speechbrain_encoder = EncoderClassifier.from_hparams(
                    source="speechbrain/spkrec-ecapa-voxceleb",
                    savedir=("pretrained_models/" "spkrec-ecapa-voxceleb"),
                    run_opts={"device": self.device},
                )
                logger.info("SpeechBrain encoder loaded")
            except Exception as e:
                logger.error(f"Failed to load speechbrain encoder: {e}")
                if HAS_RESEMBLYZER:
                    logger.info("Falling back to resemblyzer")
                    self.backend = "resemblyzer"
                    try:
                        self.resemblyzer_encoder = VoiceEncoder(device=self.device)
                    except Exception as e2:
                        logger.error(f"Failed to load resemblyzer fallback: {e2}")
                        return False
                else:
                    return False

        # Cache encoders
        if self._model_caching_enabled:
            _cache_speaker_encoder_model(
                self.backend,
                self.device,
                {
                    "resemblyzer": self.resemblyzer_encoder,
                    "speechbrain": self.speechbrain_encoder,
                },
            )

        self._initialized = True
        logger.info("Speaker Encoder initialized successfully")
        return True

    def initialize(self) -> bool:
        """
        Initialize the speaker encoder model.

        Returns:
            True if initialization successful, False otherwise
        """
        try:
            if self._initialized:
                return True

            # Lazy loading: defer until first use
            if self.lazy_load:
                logger.debug("Lazy loading enabled, model will be loaded on first use")
                return True

            return self._load_model()

        except Exception as e:
            logger.error(f"Failed to initialize Speaker Encoder: {e}")
            self._initialized = False
            return False

    def extract_embedding(
        self,
        audio: str | Path | np.ndarray,
        sample_rate: int | None = None,
        use_cache: bool = True,
        normalize: bool = True,
        extract_features: bool = True,
    ) -> np.ndarray | None:
        """
        Extract speaker embedding from audio.

        Args:
            audio: Audio file path or numpy array
            sample_rate: Sample rate (auto-detect if None)
            use_cache: Use cached embedding if available

        Returns:
            Speaker embedding vector or None if extraction failed
        """
        # Lazy load model if needed
        if not self._initialized and not self._load_model():
            return None

        try:
            # Generate cache key
            cache_key = None
            if use_cache and self.enable_cache:
                if isinstance(audio, (str, Path)):
                    cache_key = self._get_file_hash(audio)
                elif isinstance(audio, np.ndarray):
                    cache_key = self._get_array_hash(audio)

                if cache_key and cache_key in self._embedding_cache:
                    logger.debug("Using cached embedding")
                    self._embedding_cache.move_to_end(cache_key)  # LRU update
                    return self._embedding_cache[cache_key].copy()

            # Load audio
            if isinstance(audio, (str, Path)):
                if not HAS_SOUNDFILE:
                    logger.error("soundfile required for file loading")
                    return None
                audio_array, sr = sf.read(str(audio))
                if sample_rate is None:
                    sample_rate = sr
            else:
                audio_array = audio
                if sample_rate is None:
                    sample_rate = self.DEFAULT_SAMPLE_RATE

            # Convert to mono if stereo
            if len(audio_array.shape) > 1:
                audio_array = np.mean(audio_array, axis=1)

            # Convert to float32
            if audio_array.dtype != np.float32:
                audio_array = audio_array.astype(np.float32)

            # Extract embedding based on backend
            if self.backend == "resemblyzer" and self.resemblyzer_encoder:
                embedding = self._extract_resemblyzer_embedding(audio_array, sample_rate)
            elif self.backend == "speechbrain" and self.speechbrain_encoder:
                embedding = self._extract_speechbrain_embedding(audio_array, sample_rate)
            else:
                logger.error("No valid encoder backend available")
                return None

            if embedding is None:
                return None

            # Extract additional acoustic features if requested
            if extract_features and HAS_LIBROSA:
                try:
                    acoustic_features = self._extract_acoustic_features(audio_array, sample_rate)
                    # Concatenate with embedding for richer representation
                    embedding = np.concatenate([embedding, acoustic_features])
                    logger.debug(
                        f"Enhanced embedding with acoustic features: {len(embedding)} dims"
                    )
                except Exception as e:
                    logger.debug(f"Acoustic feature extraction failed: {e}, using base embedding")

            # Normalize embedding if requested
            if normalize:
                norm = np.linalg.norm(embedding) + 1e-8
                embedding = embedding / norm

            # Cache embedding
            if cache_key and self.enable_cache:
                self._cache_embedding(cache_key, embedding)

            return embedding

        except Exception as e:
            logger.error(f"Failed to extract speaker embedding: {e}")
            return None

    def _extract_resemblyzer_embedding(
        self, audio: np.ndarray, sample_rate: int
    ) -> np.ndarray | None:
        """Extract embedding using resemblyzer."""
        try:
            # Preprocess audio (resample to 16kHz if needed)
            if sample_rate != 16000:
                if HAS_LIBROSA:
                    audio = librosa.resample(
                        audio,
                        orig_sr=sample_rate,
                        target_sr=16000,
                    )
                    sample_rate = 16000
                else:
                    logger.warning(
                        "librosa not available for resampling. "
                        "Audio may not be at correct sample rate."
                    )

            # Preprocess with resemblyzer
            preprocessed = preprocess_wav(audio, sample_rate)

            # Extract embedding
            if self.resemblyzer_encoder is None:
                logger.error("Resemblyzer encoder not loaded")
                return None
            embedding: np.ndarray = self.resemblyzer_encoder.embed_utterance(preprocessed)

            return embedding

        except Exception as e:
            logger.error(f"Resemblyzer embedding extraction failed: {e}")
            return None

    def _extract_speechbrain_embedding(
        self, audio: np.ndarray, sample_rate: int
    ) -> np.ndarray | None:
        """Extract embedding using speechbrain."""
        try:
            # Resample to 16kHz if needed
            if sample_rate != 16000:
                if HAS_LIBROSA:
                    audio = librosa.resample(
                        audio,
                        orig_sr=sample_rate,
                        target_sr=16000,
                    )
                    sample_rate = 16000
                else:
                    logger.warning(
                        "librosa not available for resampling. "
                        "Audio may not be at correct sample rate."
                    )

            # Convert to tensor
            audio_tensor = torch.tensor(audio, device=self.device).unsqueeze(0)

            # Extract embedding with inference mode for better performance
            if self.speechbrain_encoder is None:
                logger.error("SpeechBrain encoder not loaded")
                return None
            with torch.inference_mode():  # Faster than no_grad
                embedding_tensor = self.speechbrain_encoder.encode_batch(audio_tensor)

            # Convert to numpy
            embedding: np.ndarray = embedding_tensor.squeeze(0).cpu().numpy()

            # Normalize
            norm = np.linalg.norm(embedding) + 1e-8
            embedding = embedding / norm

            return embedding

        except Exception as e:
            logger.error(f"SpeechBrain embedding extraction failed: {e}")
            return None

    def _extract_acoustic_features(self, audio: np.ndarray, sample_rate: int) -> np.ndarray:
        """
        Extract additional acoustic features to enhance speaker embedding.

        Extracts MFCC, spectral features, and prosodic features for richer
        voice representation.

        Args:
            audio: Audio array
            sample_rate: Sample rate

        Returns:
            Concatenated acoustic feature vector
        """
        if not HAS_LIBROSA:
            return np.array([])

        try:
            features = []

            # MFCC features (13 coefficients)
            mfcc = librosa.feature.mfcc(y=audio, sr=sample_rate, n_mfcc=13)
            features.append(np.mean(mfcc, axis=1))  # Mean across time
            features.append(np.std(mfcc, axis=1))  # Std across time

            # Spectral features
            spectral_centroid = librosa.feature.spectral_centroid(y=audio, sr=sample_rate)
            features.append([np.mean(spectral_centroid), np.std(spectral_centroid)])

            spectral_rolloff = librosa.feature.spectral_rolloff(y=audio, sr=sample_rate)
            features.append([np.mean(spectral_rolloff), np.std(spectral_rolloff)])

            zero_crossing_rate = librosa.feature.zero_crossing_rate(audio)
            features.append([np.mean(zero_crossing_rate), np.std(zero_crossing_rate)])

            # Chroma features (pitch class)
            chroma = librosa.feature.chroma_stft(y=audio, sr=sample_rate)
            features.append(np.mean(chroma, axis=1))  # Mean chroma

            # Prosodic features (pitch/F0)
            try:
                f0, voiced_flag, _voiced_probs = librosa.pyin(
                    audio,
                    fmin=float(librosa.note_to_hz("C2")),
                    fmax=float(librosa.note_to_hz("C7")),
                )
                f0_clean = f0[~np.isnan(f0)]
                if len(f0_clean) > 0:
                    features.append(
                        [
                            np.mean(f0_clean),
                            np.std(f0_clean),
                            np.median(f0_clean),
                            np.sum(voiced_flag) / len(voiced_flag),  # Voiced ratio
                        ]
                    )
                else:
                    features.append([0.0, 0.0, 0.0, 0.0])
            except Exception:
                features.append([0.0, 0.0, 0.0, 0.0])

            # Concatenate all features
            acoustic_features = np.concatenate(features)

            # Normalize features
            acoustic_features = (acoustic_features - np.mean(acoustic_features)) / (
                np.std(acoustic_features) + 1e-8
            )

            return np.asarray(acoustic_features)

        except Exception as e:
            logger.debug(f"Acoustic feature extraction failed: {e}")
            return np.array([])

    def compare_speakers(
        self,
        audio1: str | Path | np.ndarray,
        audio2: str | Path | np.ndarray,
        sample_rate1: int | None = None,
        sample_rate2: int | None = None,
    ) -> float | None:
        """
        Compare similarity between two speakers.

        Args:
            audio1: First audio file or array
            audio2: Second audio file or array
            sample_rate1: Sample rate for first audio
            sample_rate2: Sample rate for second audio

        Returns:
            Similarity score (0.0 to 1.0) or None if comparison failed
        """
        try:
            # Extract embeddings
            embedding1 = self.extract_embedding(audio1, sample_rate1)
            embedding2 = self.extract_embedding(audio2, sample_rate2)

            if embedding1 is None or embedding2 is None:
                return None

            # Calculate cosine similarity
            similarity = self._cosine_similarity(embedding1, embedding2)

            return float(similarity)

        except Exception as e:
            logger.error(f"Speaker comparison failed: {e}")
            return None

    def _cosine_similarity(self, embedding1: np.ndarray, embedding2: np.ndarray) -> float:
        """Calculate cosine similarity between embeddings."""
        # Normalize embeddings
        norm1 = np.linalg.norm(embedding1)
        norm2 = np.linalg.norm(embedding2)

        if norm1 == 0 or norm2 == 0:
            return 0.0

        # Cosine similarity
        similarity = np.dot(embedding1, embedding2) / (norm1 * norm2)

        # Clamp to [0, 1] range
        similarity = max(0.0, min(1.0, similarity))

        return float(similarity)

    def extract_batch_embeddings(
        self,
        audio_list: list[str | Path | np.ndarray],
        sample_rates: list[int | None] | None = None,
        batch_size: int | None = None,
    ) -> list[np.ndarray | None]:
        """
        Extract embeddings for multiple audio files with optimized batch processing.

        Args:
            audio_list: List of audio files or arrays
            sample_rates: Optional list of sample rates
            batch_size: Number of items to process in parallel (default: self.batch_size)

        Returns:
            List of embeddings (None for failed extractions)
        """
        # Lazy load model if needed
        if not self._initialized and not self._load_model():
            return [None] * len(audio_list)

        if sample_rates is None:
            sample_rates = [None] * len(audio_list)

        # Use configured batch size if not specified
        actual_batch_size = batch_size if batch_size is not None else self.batch_size

        # Process in parallel batches for better performance
        def extract_single(args):
            audio, sr = args
            try:
                return self.extract_embedding(audio, sr)
            except Exception as e:
                logger.error(f"Batch embedding extraction failed: {e}")
                return None

        # Use ThreadPoolExecutor for parallel processing
        with ThreadPoolExecutor(max_workers=actual_batch_size) as executor:
            embeddings = list(executor.map(extract_single, zip(audio_list, sample_rates)))

        # Clear GPU cache periodically
        if (
            HAS_TORCH
            and torch.cuda.is_available()
            and (len(audio_list) % (actual_batch_size * 2) == 0)
        ):
            torch.cuda.empty_cache()

        return embeddings

    def find_similar_speakers(
        self,
        reference_audio: str | Path | np.ndarray,
        candidate_audio_list: list[str | Path | np.ndarray],
        threshold: float = 0.7,
        sample_rate: int | None = None,
    ) -> list[tuple[int, float]]:
        """
        Find speakers similar to reference.

        Args:
            reference_audio: Reference audio
            candidate_audio_list: List of candidate audio files
            threshold: Similarity threshold (0.0 to 1.0)
            sample_rate: Sample rate (if all audio has same rate)

        Returns:
            List of (index, similarity) tuples for speakers above threshold
        """
        try:
            # Extract reference embedding
            ref_embedding = self.extract_embedding(reference_audio, sample_rate)

            if ref_embedding is None:
                return []

            # Extract candidate embeddings
            candidate_embeddings = self.extract_batch_embeddings(
                candidate_audio_list,
                ([sample_rate] * len(candidate_audio_list) if sample_rate else None),
            )

            # Compare and filter
            similar_speakers = []

            for idx, cand_embedding in enumerate(candidate_embeddings):
                if cand_embedding is None:
                    continue

                similarity = self._cosine_similarity(ref_embedding, cand_embedding)

                if similarity >= threshold:
                    similar_speakers.append((idx, similarity))

            # Sort by similarity (descending)
            similar_speakers.sort(key=lambda x: x[1], reverse=True)

            return similar_speakers

        except Exception as e:
            logger.error(f"Find similar speakers failed: {e}")
            return []

    def _get_file_hash(self, file_path: str | Path) -> str | None:
        """Generate hash for file (based on path and mtime)."""
        try:
            path = Path(file_path)
            if not path.exists():
                return None

            # Use path and modification time for cache key
            stat = path.stat()
            abs_path = path.absolute()
            key_string = f"{abs_path}_{stat.st_mtime}"
            return hashlib.md5(key_string.encode()).hexdigest()

        except Exception as e:
            logger.warning(f"Failed to generate file hash: {e}")
            return None

    def _get_array_hash(self, audio: np.ndarray) -> str | None:
        """Generate hash for audio array."""
        try:
            # Use array shape and first/last samples for hash
            first = audio[0] if len(audio) > 0 else 0
            last = audio[-1] if len(audio) > 0 else 0
            key_string = f"{audio.shape}_{first}_{last}"
            return hashlib.md5(key_string.encode()).hexdigest()

        except Exception as e:
            logger.warning(f"Failed to generate array hash: {e}")
            return None

    def _cache_embedding(self, key: str, embedding: np.ndarray):
        """Cache embedding with LRU eviction."""
        # Remove oldest entries if cache is full (LRU)
        if len(self._embedding_cache) >= self.cache_size:
            # Remove first (oldest) entry
            oldest_key = next(iter(self._embedding_cache))
            del self._embedding_cache[oldest_key]

        self._embedding_cache[key] = embedding.copy()
        self._embedding_cache.move_to_end(key)  # LRU update

    def clear_cache(self):
        """Clear embedding cache."""
        self._embedding_cache.clear()
        logger.info("Embedding cache cleared")

    def get_cache_stats(self) -> dict[str, int]:
        """Get cache statistics."""
        return {
            "cache_size": len(self._embedding_cache),
            "max_cache_size": self.cache_size,
            "cache_enabled": self.enable_cache,
        }

    def set_batch_size(self, batch_size: int):
        """Set batch size for batch operations."""
        self.batch_size = max(1, batch_size)
        logger.info(f"Batch size set to {self.batch_size}")

    def set_model_caching_enabled(self, enable: bool = True) -> None:
        """Enable or disable model caching."""
        self._model_caching_enabled = enable
        logger.info(f"Model caching {'enabled' if enable else 'disabled'}")

    def enable_model_caching(self, enable: bool = True) -> None:
        """Enable or disable model caching (alias for set_model_caching_enabled)."""
        self.set_model_caching_enabled(enable)

    def _get_memory_usage(self) -> dict[str, float]:
        """Get GPU memory usage in MB."""
        if not HAS_TORCH or not torch.cuda.is_available():
            return {"gpu_memory_mb": 0.0, "gpu_memory_allocated_mb": 0.0}

        return {
            "gpu_memory_mb": torch.cuda.get_device_properties(0).total_memory / 1024**2,
            "gpu_memory_allocated_mb": torch.cuda.memory_allocated(0) / 1024**2,
        }

    def cleanup(self):
        """Clean up resources."""
        try:
            # Don't delete cached models, just clear references
            self.resemblyzer_encoder = None
            self.speechbrain_encoder = None

            # Clear CUDA cache if using GPU
            if HAS_TORCH and torch.cuda.is_available():
                torch.cuda.empty_cache()

            self._embedding_cache.clear()
            self._initialized = False
            logger.info("Speaker Encoder engine cleaned up")
        except Exception as e:
            logger.warning(f"Error during Speaker Encoder cleanup: {e}")

    def visualize_embeddings(
        self,
        embeddings: list[np.ndarray],
        n_components: int = 2,
        n_neighbors: int = 15,
        min_dist: float = 0.1,
    ) -> np.ndarray | None:
        """
        Visualize embeddings using UMAP dimensionality reduction.

        Args:
            embeddings: List of embedding vectors
            n_components: Number of dimensions for output (2 or 3)
            n_neighbors: Number of neighbors for UMAP (default: 15)
            min_dist: Minimum distance between points (default: 0.1)

        Returns:
            2D or 3D reduced embeddings as numpy array, or None if umap not available
        """
        if not HAS_UMAP or umap is None:
            logger.warning("umap-learn not available. " "Install with: pip install umap-learn")
            return None

        if not embeddings or len(embeddings) == 0:
            logger.warning("No embeddings provided for visualization")
            return None

        try:
            # Convert list to numpy array
            embeddings_array = np.array(embeddings)

            # Validate dimensions
            if len(embeddings_array.shape) != 2:
                logger.error(
                    f"Invalid embeddings shape: {embeddings_array.shape}. "
                    "Expected 2D array (n_samples, n_features)"
                )
                return None

            # Create UMAP reducer
            reducer = umap.UMAP(
                n_components=n_components,
                n_neighbors=min(n_neighbors, len(embeddings) - 1),
                min_dist=min_dist,
                random_state=42,
            )

            # Fit and transform embeddings
            reduced_embeddings = reducer.fit_transform(embeddings_array)

            logger.debug(
                f"Reduced {len(embeddings)} embeddings from "
                f"{embeddings_array.shape[1]}D to {n_components}D"
            )

            return np.asarray(reduced_embeddings)

        except Exception as e:
            logger.error(f"Failed to visualize embeddings with UMAP: {e}")
            return None

    def get_info(self) -> dict:
        """Get engine information."""
        info = super().get_info()
        info.update(
            {
                "backend": self.backend,
                "cache_enabled": self.enable_cache,
                "cache_size": len(self._embedding_cache),
                "max_cache_size": self.cache_size,
                "has_resemblyzer": HAS_RESEMBLYZER,
                "has_speechbrain": HAS_SPEECHBRAIN,
                "has_umap": HAS_UMAP,
            }
        )
        return info


def create_speaker_encoder_engine(
    backend: str = "resemblyzer",
    device: str | None = None,
    gpu: bool = True,
    enable_cache: bool = True,
) -> SpeakerEncoderEngine:
    """
    Factory function to create a Speaker Encoder engine instance.

    Args:
        backend: Backend to use ('resemblyzer' or 'speechbrain')
        device: Device to use ('cuda', 'cpu', or None for auto)
        gpu: Whether to use GPU if available
        enable_cache: Enable embedding cache

    Returns:
        Initialized SpeakerEncoderEngine instance
    """
    engine = SpeakerEncoderEngine(
        backend=backend, device=device, gpu=gpu, enable_cache=enable_cache
    )
    engine.initialize()
    return engine
