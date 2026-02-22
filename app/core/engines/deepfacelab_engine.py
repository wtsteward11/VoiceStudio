"""
DeepFaceLab Engine for VoiceStudio
Face replacement/swap using DeepFaceLab (gated feature)

Compatible with:
- Python 3.10+
- TensorFlow 2.8.0+
- opencv-python 4.5.0+
"""

from __future__ import annotations

import logging
import os
from pathlib import Path
from typing import Any

import numpy as np
import numpy.typing as npt
from PIL import Image

# Import base protocol
try:
    from .protocols import EngineProtocol
except ImportError:
    from .base import EngineProtocol

logger = logging.getLogger(__name__)

# Required imports
try:
    import cv2

    HAS_CV2 = True
except ImportError:
    HAS_CV2 = False
    logger.warning("opencv-python not installed. Install with: pip install opencv-python")

# Try importing opencv-contrib for extended features
try:
    import cv2

    # Check if contrib modules are available
    HAS_CV2_CONTRIB = hasattr(cv2, "face") or hasattr(cv2, "xfeatures2d")
except (ImportError, AttributeError):
    HAS_CV2_CONTRIB = False
    logger.debug(
        "opencv-contrib-python not fully available. " "Some advanced features will be limited."
    )

try:
    import tensorflow as tf

    HAS_TENSORFLOW = True
except ImportError:
    HAS_TENSORFLOW = False
    logger.warning("tensorflow not installed. Install with: pip install tensorflow>=2.8.0")

# Try importing insightface for face recognition
try:
    import insightface

    HAS_INSIGHTFACE = True
except ImportError:
    HAS_INSIGHTFACE = False
    insightface = None
    logger.debug(
        "insightface not installed. Advanced face recognition will be limited. "
        "Install with: pip install insightface>=0.7.3"
    )

# Try importing face_alignment
try:
    import face_alignment

    HAS_FACE_ALIGNMENT = True
except ImportError:
    HAS_FACE_ALIGNMENT = False

# cv2.data is valid at runtime but absent from type stubs
_cv2_data: Any = getattr(cv2, "data", None) if HAS_CV2 else None


class DeepFaceLabEngine(EngineProtocol):
    """
    DeepFaceLab Engine for face replacement/swap.

    WARNING: This is a gated feature requiring user consent.
    Face swap features must be used responsibly and ethically.

    Supports:
    - Face detection and alignment
    - Face replacement/swap
    - Face blending
    - Video face swap
    """

    def __init__(
        self,
        device: str | None = None,
        gpu: bool = True,
        model_path: str | None = None,
        require_consent: bool = True,
    ):
        """
        Initialize DeepFaceLab engine.

        Args:
            device: Device to use ('cuda', 'cpu', or None for auto)
            gpu: Whether to use GPU if available
            model_path: Path to DeepFaceLab model checkpoint (optional)
            require_consent: If True, require explicit user consent before use
        """
        if not HAS_CV2:
            raise ImportError("opencv-python required. Install with: pip install opencv-python")

        super().__init__(device=device, gpu=gpu)

        self.model_path = model_path
        self.require_consent = require_consent
        self.consent_given = False
        self.model: Any = None

        # InsightFace model for face recognition (if available)
        self.insightface_app = None
        if HAS_INSIGHTFACE:
            try:
                # Initialize InsightFace app for face recognition
                self.insightface_app = insightface.app.FaceAnalysis(
                    name="buffalo_l",
                    providers=["CUDAExecutionProvider", "CPUExecutionProvider"],
                )
                self.insightface_app.prepare(ctx_id=0 if gpu else -1, det_size=(640, 640))
                logger.info("InsightFace initialized for face recognition")
            except Exception as e:
                logger.warning(f"Failed to initialize InsightFace: {e}")
                self.insightface_app = None

        # Caching for performance
        self._result_cache: dict[str, Any] = {}
        self._face_detection_cache: dict[str, Any] = {}
        self._cache_max_size = 100  # Maximum number of cached results

        # Note: DeepFaceLab typically uses TensorFlow, not PyTorch
        # Device handling is different for TensorFlow
        if device:
            self.device = device
        else:
            self.device = (
                "gpu"
                if (gpu and HAS_TENSORFLOW and tf.config.list_physical_devices("GPU"))
                else "cpu"
            )

    def initialize(self) -> bool:
        """Initialize the DeepFaceLab model."""
        try:
            if self._initialized:
                return True

            logger.info("Initializing DeepFaceLab engine")

            # Validate required dependencies
            if not HAS_TENSORFLOW:
                error_msg = (
                    "TensorFlow is required for DeepFaceLab engine. "
                    "Install with: pip install tensorflow>=2.8.0"
                )
                logger.error(error_msg)
                raise ImportError(error_msg)

            # Check consent if required
            if self.require_consent and not self.consent_given:
                logger.warning(
                    "DeepFaceLab requires user consent. Call set_consent(True) before use."
                )
                return False

            model_cache_dir = os.getenv("VOICESTUDIO_MODELS_PATH")
            if not model_cache_dir:
                model_cache_dir = os.path.join(
                    os.getenv("PROGRAMDATA", "C:\\ProgramData"),
                    "VoiceStudio",
                    "models",
                    "deepfacelab",
                )
            os.makedirs(model_cache_dir, exist_ok=True)

            # Load DeepFaceLab model
            self.model = self._load_model(model_cache_dir)
            self._initialized = True
            logger.info(f"DeepFaceLab engine initialized (device: {self.device})")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize DeepFaceLab model: {e}")
            self._initialized = False
            return False

    def set_consent(self, consent: bool) -> None:
        """Set user consent for face swap feature."""
        self.consent_given = consent
        if consent:
            logger.info("User consent granted for DeepFaceLab")
        else:
            logger.info("User consent revoked for DeepFaceLab")
            if self._initialized:
                self.cleanup()

    def cleanup(self) -> None:
        """Clean up resources and free memory."""
        try:
            if self.model is not None:
                del self.model
                self.model = None

            if HAS_TENSORFLOW:
                import tensorflow as tf

                tf.keras.backend.clear_session()

            try:
                import torch

                if torch.cuda.is_available():
                    torch.cuda.empty_cache()
            except ImportError:
                ...

            # Clear caches
            self._result_cache.clear()
            self._face_detection_cache.clear()

            self._initialized = False
            logger.info("DeepFaceLab engine cleaned up")

        except Exception as e:
            logger.error(f"Error during DeepFaceLab cleanup: {e}")

    def swap_face(
        self,
        source_image: str | Path | Image.Image,
        target_image: str | Path | Image.Image,
        output_path: str | Path | None = None,
        blend_factor: float = 0.5,
        **kwargs: Any,
    ) -> str:
        """
        Swap face from source image to target image.

        Args:
            source_image: Image containing face to extract
            target_image: Image to place face onto
            output_path: Path to save output image
            blend_factor: Blending factor (0.0 = full swap, 1.0 = original)
            **kwargs: Additional parameters

        Returns:
            Path to output image
        """
        if not self._initialized:
            raise RuntimeError("Engine not initialized. Call initialize() first.")

        if self.require_consent and not self.consent_given:
            raise RuntimeError("User consent required. Call set_consent(True) first.")

        try:
            # Check result cache
            import hashlib

            source_key = (
                str(source_image)
                if isinstance(source_image, (str, Path))
                else hashlib.md5(np.array(source_image).tobytes()).hexdigest()
            )
            target_key = (
                str(target_image)
                if isinstance(target_image, (str, Path))
                else hashlib.md5(np.array(target_image).tobytes()).hexdigest()
            )
            cache_key = hashlib.md5(
                f"{source_key}_{target_key}_{blend_factor}".encode()
            ).hexdigest()

            if cache_key in self._result_cache:
                logger.debug("Using cached DeepFaceLab result")
                cached_path: str = self._result_cache[cache_key]
                if os.path.exists(cached_path):
                    return cached_path
                else:
                    del self._result_cache[cache_key]

            # Load images
            if isinstance(source_image, (str, Path)):
                source_raw = cv2.imread(str(source_image))
                if source_raw is None:
                    raise ValueError(f"Failed to read source image: {source_image}")
                source_img = cv2.cvtColor(source_raw, cv2.COLOR_BGR2RGB)
            else:
                source_img = np.array(source_image)

            if isinstance(target_image, (str, Path)):
                target_raw = cv2.imread(str(target_image))
                if target_raw is None:
                    raise ValueError(f"Failed to read target image: {target_image}")
                target_img = cv2.cvtColor(target_raw, cv2.COLOR_BGR2RGB)
            else:
                target_img = np.array(target_image)

            logger.info("Performing face swap")

            # Detect and extract faces (with caching)
            source_hash = hashlib.md5(source_img.tobytes()).hexdigest()
            if source_hash in self._face_detection_cache:
                logger.debug("Using cached source face detection")
                source_face = self._face_detection_cache[source_hash]
            else:
                source_face = self._detect_and_extract_face(source_img)
                if source_face is not None and len(self._face_detection_cache) < 200:
                    self._face_detection_cache[source_hash] = source_face

            target_hash = hashlib.md5(target_img.tobytes()).hexdigest()
            target_cache_key = f"{target_hash}_bbox"
            if target_cache_key in self._face_detection_cache:
                logger.debug("Using cached target face detection")
                target_face_data = self._face_detection_cache[target_cache_key]
            else:
                target_face_data = self._detect_and_extract_face(target_img, return_bbox=True)
                if target_face_data is not None and len(self._face_detection_cache) < 200:
                    self._face_detection_cache[target_cache_key] = target_face_data

            if source_face is None:
                raise ValueError("No face detected in source image")
            if target_face_data is None:
                raise ValueError("No face detected in target image")

            target_face, target_bbox = target_face_data

            # Perform face swap using DeepFaceLab model or fallback method
            swapped_face = self._swap_face_model(source_face, target_face)

            # Blend if needed
            if blend_factor > 0.0:
                swapped_face = self._blend_faces(swapped_face, target_face, blend_factor)

            # Composite back onto target image
            result = self._composite_face(target_img, swapped_face, target_bbox)

            # Generate output path
            if output_path is None:
                output_dir = os.path.join(
                    os.getenv("TEMP", "C:\\Temp"), "VoiceStudio", "deepfacelab_output"
                )
                os.makedirs(output_dir, exist_ok=True)
                output_path = os.path.join(output_dir, "face_swap_result.jpg")

            output_path = Path(output_path)
            output_path.parent.mkdir(parents=True, exist_ok=True)

            # Save result
            result_bgr = cv2.cvtColor(result, cv2.COLOR_RGB2BGR)
            cv2.imwrite(str(output_path), result_bgr)

            # Cache result if successful
            if os.path.exists(output_path):
                if len(self._result_cache) >= self._cache_max_size:
                    oldest_key = next(iter(self._result_cache))
                    del self._result_cache[oldest_key]
                self._result_cache[cache_key] = str(output_path)

            logger.info(f"Face swap completed: {output_path}")
            return str(output_path)

        except Exception as e:
            logger.error(f"Error performing face swap: {e}")
            raise RuntimeError(f"Failed to perform face swap: {e}")

    def _detect_face_insightface(self, image: npt.NDArray[Any]) -> dict[str, Any] | None:
        """
        Detect face using InsightFace (if available).

        Args:
            image: Input image array

        Returns:
            Dictionary with face detection data or None if not found
        """
        if not HAS_INSIGHTFACE or self.insightface_app is None:
            return None

        try:
            # InsightFace expects BGR format
            if len(image.shape) == 3 and image.shape[2] == 3:
                # Assume RGB, convert to BGR
                image_bgr = cv2.cvtColor(image, cv2.COLOR_RGB2BGR)
            else:
                image_bgr = image

            # Detect faces
            faces = self.insightface_app.get(image_bgr)

            if not faces:
                return None

            # Use first detected face
            face = faces[0]

            # Extract face data
            bbox = face.bbox.astype(int)  # [x1, y1, x2, y2]
            landmarks = face.kps.astype(int)  # 5 keypoints
            embedding = face.embedding  # 512-dimensional embedding

            return {
                "bbox": bbox,
                "landmarks": landmarks,
                "embedding": embedding,
                "detection_confidence": face.det_score,
            }
        except Exception as e:
            logger.warning(f"InsightFace face detection failed: {e}")
            return None

    def _detect_and_extract_face(
        self, image: npt.NDArray[Any], return_bbox: bool = False
    ) -> npt.NDArray[Any] | None | tuple[npt.NDArray[Any], dict[str, Any]] | None:
        """
        Detect and extract face from image.

        Uses InsightFace if available for better accuracy,
        otherwise falls back to OpenCV's Haar cascade.
        """
        if not HAS_CV2:
            raise ImportError("opencv-python required")

        # Try InsightFace first (more accurate)
        if HAS_INSIGHTFACE and self.insightface_app is not None:
            try:
                insightface_result = self._detect_face_insightface(image)
                if insightface_result:
                    bbox_array = insightface_result["bbox"]  # [x1, y1, x2, y2]
                    x1, y1, x2, y2 = bbox_array
                    x, y, w, h = x1, y1, x2 - x1, y2 - y1

                    # Add padding
                    padding = 20
                    x = max(0, x - padding)
                    y = max(0, y - padding)
                    w = min(image.shape[1] - x, w + 2 * padding)
                    h = min(image.shape[0] - y, h + 2 * padding)

                    face_image = image[y : y + h, x : x + w]
                    bbox = {"x": x, "y": y, "width": w, "height": h}

                    if return_bbox:
                        return face_image, bbox
                    return face_image
            except Exception as e:
                logger.debug(f"InsightFace detection failed: {e}, using OpenCV fallback")

        # Fallback to OpenCV's face detector
        face_cascade = cv2.CascadeClassifier(
            _cv2_data.haarcascades + "haarcascade_frontalface_default.xml"
        )
        gray = cv2.cvtColor(image, cv2.COLOR_RGB2GRAY)
        faces = face_cascade.detectMultiScale(gray, 1.3, 5)

        if len(faces) == 0:
            return None

        # Use first detected face
        x, y, w, h = faces[0]

        # Add padding
        padding = 20
        x = max(0, x - padding)
        y = max(0, y - padding)
        w = min(image.shape[1] - x, w + 2 * padding)
        h = min(image.shape[0] - y, h + 2 * padding)

        face_image = image[y : y + h, x : x + w]
        bbox = {"x": x, "y": y, "width": w, "height": h}

        if return_bbox:
            return face_image, bbox
        return face_image

    def _swap_face_model(
        self, source_face: npt.NDArray[Any], target_face: npt.NDArray[Any]
    ) -> npt.NDArray[Any]:
        """Perform face swap using model."""
        try:
            # Use model if available
            if self.model is not None:
                return self._swap_with_model(source_face, target_face)

            # If no model loaded, use fallback method (this is acceptable - model may not be available)
            logger.debug("No model loaded, using fallback face swap method")
            return self._swap_with_fallback(source_face, target_face)

        except ImportError:
            # Re-raise dependency errors - don't fall back for missing dependencies
            raise
        except Exception as e:
            # Only use simple resize fallback for exceptional runtime errors
            logger.warning(f"Face swap failed: {e}, using simple resize as last resort")
            target_h, target_w = target_face.shape[:2]
            swapped = cv2.resize(source_face, (target_w, target_h))
            return swapped

    def _load_model(self, model_cache_dir: str) -> dict[str, Any] | None:
        """Load DeepFaceLab model."""
        try:
            # Look for model files
            model_path = self.model_path
            if not model_path:
                # Search in cache directory
                model_files = list(Path(model_cache_dir).rglob("*.h5"))
                model_files.extend(list(Path(model_cache_dir).rglob("*.pb")))
                model_files.extend(list(Path(model_cache_dir).rglob("*.pth")))
                if model_files:
                    model_path = str(model_files[0])

            if model_path and os.path.exists(model_path) and HAS_TENSORFLOW:
                import tensorflow as tf

                # Configure TensorFlow device
                if self.device == "gpu":
                    gpus = tf.config.experimental.list_physical_devices("GPU")
                    if gpus:
                        try:
                            for gpu in gpus:
                                tf.config.experimental.set_memory_growth(gpu, True)
                        except RuntimeError:
                            ...

                # Load model
                if model_path.endswith(".h5"):
                    model = tf.keras.models.load_model(model_path)
                elif model_path.endswith(".pb"):
                    # Load saved model
                    model = tf.saved_model.load(model_path)
                else:
                    # Try to load as checkpoint
                    model = tf.keras.models.load_model(model_path)

                logger.info(f"Loaded DeepFaceLab model from: {model_path}")
                return {"model": model, "path": model_path, "device": self.device}

            return None

        except Exception as e:
            logger.warning(f"Failed to load DeepFaceLab model: {e}")
            return None

    def _swap_with_model(
        self, source_face: npt.NDArray[Any], target_face: npt.NDArray[Any]
    ) -> npt.NDArray[Any]:
        """Perform face swap using loaded model."""
        if not HAS_TENSORFLOW:
            error_msg = (
                "TensorFlow is required for DeepFaceLab model inference. "
                "Install with: pip install tensorflow>=2.8.0"
            )
            logger.error(error_msg)
            raise ImportError(error_msg)

        try:
            import tensorflow as tf

            model_data = self.model
            model = model_data["model"]

            # Preprocess faces
            source_processed = self._preprocess_face(source_face)
            target_processed = self._preprocess_face(target_face)

            # Convert to tensors
            source_tensor = tf.convert_to_tensor(source_processed, dtype=tf.float32)
            target_tensor = tf.convert_to_tensor(target_processed, dtype=tf.float32)

            # Apply model
            if isinstance(model, tf.keras.Model):
                # Keras model
                swapped_tensor = model([source_tensor, target_tensor])
            else:
                # Saved model or other format
                swapped_tensor = model([source_tensor, target_tensor])

            # Postprocess
            swapped = self._postprocess_face(swapped_tensor.numpy())

            return swapped

        except Exception as e:
            # Only use fallback for runtime errors (model inference failures)
            # Not for missing dependencies - those should fail fast
            logger.error(f"Model face swap failed: {e}")
            logger.warning("Attempting fallback method due to model inference error")
            return self._swap_with_fallback(source_face, target_face)

    def _swap_with_fallback(
        self, source_face: npt.NDArray[Any], target_face: npt.NDArray[Any]
    ) -> npt.NDArray[Any]:
        """Perform face swap using fallback method (alignment and blending)."""
        try:
            # Align faces
            source_aligned = self._align_face(source_face)
            target_aligned = self._align_face(target_face)

            # Resize to match
            target_h, target_w = target_aligned.shape[:2]
            source_resized = cv2.resize(source_aligned, (target_w, target_h))

            # Apply color correction to match target
            source_corrected = self._color_correct(source_resized, target_aligned)

            # Blend edges for seamless integration
            swapped = self._blend_edges(source_corrected, target_aligned)

            return swapped

        except Exception as e:
            logger.warning(f"Fallback face swap failed: {e}, using simple resize")
            target_h, target_w = target_face.shape[:2]
            return cv2.resize(source_face, (target_w, target_h))

    def _preprocess_face(self, face: npt.NDArray[Any]) -> npt.NDArray[Any]:
        """Preprocess face for model input."""
        # Resize to standard size (256x256 for most models)
        face_resized = cv2.resize(face, (256, 256))
        # Normalize to [0, 1]
        face_normalized = face_resized.astype(np.float32) / 255.0
        return face_normalized

    def _postprocess_face(self, face: npt.NDArray[Any]) -> npt.NDArray[Any]:
        """Postprocess model output."""
        # Denormalize from [0, 1] to [0, 255]
        face_denormalized = np.clip(face * 255.0, 0, 255).astype(np.uint8)
        return face_denormalized

    def _align_face(self, face: npt.NDArray[Any]) -> npt.NDArray[Any]:
        """Align face using landmarks."""
        try:
            if HAS_FACE_ALIGNMENT and HAS_CV2:
                import face_alignment

                fa = face_alignment.FaceAlignment(
                    face_alignment.LandmarksType.TWO_D,
                    flip_input=False,
                    device=self.device if self.device != "gpu" else "cuda",
                )
                landmarks = fa.get_landmarks(face)
                if landmarks is not None and len(landmarks) > 0:
                    # Extract key facial landmarks for alignment
                    lm = landmarks[0]

                    # Define reference points for alignment (standard face positions)
                    # Left eye center, right eye center, nose tip, mouth center
                    left_eye = lm[36:42].mean(axis=0)  # Left eye landmarks
                    right_eye = lm[42:48].mean(axis=0)  # Right eye landmarks
                    nose_tip = lm[30]  # Nose tip
                    lm[48:68].mean(axis=0)  # Mouth landmarks

                    # Define target positions (normalized face)
                    h, w = face.shape[:2]
                    target_size = max(h, w)

                    # Standard face alignment: eyes horizontal, centered
                    target_left_eye = np.array([target_size * 0.35, target_size * 0.4])
                    target_right_eye = np.array([target_size * 0.65, target_size * 0.4])
                    target_nose = np.array([target_size * 0.5, target_size * 0.5])
                    target_mouth = np.array([target_size * 0.5, target_size * 0.65])

                    # Compute transformation matrix using similarity transform
                    # Use eyes and nose for alignment
                    source_pts = np.array([left_eye, right_eye, nose_tip], dtype=np.float32)

                    target_pts = np.array(
                        [target_left_eye, target_right_eye, target_nose],
                        dtype=np.float32,
                    )

                    # Compute similarity transform (rotation, scale, translation)
                    transform_matrix = cv2.getAffineTransform(source_pts, target_pts)

                    # Apply transformation
                    aligned_face = cv2.warpAffine(
                        face,
                        transform_matrix,
                        (target_size, target_size),
                        flags=cv2.INTER_LINEAR,
                        borderMode=cv2.BORDER_REPLICATE,
                    )

                    # Crop to face region (remove excessive padding)
                    # Use mouth position to determine crop bounds
                    mouth_y = int(target_mouth[1])
                    crop_top = max(0, int(target_size * 0.1))
                    crop_bottom = min(target_size, mouth_y + int(target_size * 0.2))
                    crop_left = max(0, int(target_size * 0.15))
                    crop_right = min(target_size, int(target_size * 0.85))

                    aligned_face = aligned_face[crop_top:crop_bottom, crop_left:crop_right]

                    # Resize to standard size if needed
                    if aligned_face.shape[0] != h or aligned_face.shape[1] != w:
                        aligned_face = cv2.resize(
                            aligned_face, (w, h), interpolation=cv2.INTER_LINEAR
                        )

                    return aligned_face

            # Fallback: use OpenCV face detection for basic alignment
            elif HAS_CV2:
                try:
                    # Load face detector
                    face_cascade_path = (
                        _cv2_data.haarcascades + "haarcascade_frontalface_default.xml"
                    )
                    face_cascade = cv2.CascadeClassifier(face_cascade_path)

                    if face_cascade.empty():
                        return face

                    gray = cv2.cvtColor(face, cv2.COLOR_RGB2GRAY) if len(face.shape) == 3 else face
                    faces = face_cascade.detectMultiScale(gray, 1.1, 4)

                    if len(faces) > 0:
                        x, y, w, h = faces[0]
                        # Extract face region
                        face_roi = face[y : y + h, x : x + w]
                        # Resize to standard size
                        h_orig, w_orig = face.shape[:2]
                        face_aligned = cv2.resize(
                            face_roi, (w_orig, h_orig), interpolation=cv2.INTER_LINEAR
                        )
                        return face_aligned
                except Exception as e:
                    logger.debug(f"OpenCV face detection alignment failed: {e}")

            return face

        except Exception as e:
            logger.warning(f"Face alignment failed: {e}")
            return face

    def _color_correct(
        self, source_face: npt.NDArray[Any], target_face: npt.NDArray[Any]
    ) -> npt.NDArray[Any]:
        """Apply color correction to match target face."""
        try:
            # Compute mean and std for both faces
            source_mean = source_face.mean(axis=(0, 1))
            source_std = source_face.std(axis=(0, 1)) + 1e-8

            target_mean = target_face.mean(axis=(0, 1))
            target_std = target_face.std(axis=(0, 1)) + 1e-8

            # Apply color transfer
            corrected = (source_face - source_mean) * (target_std / source_std) + target_mean
            return np.asarray(np.clip(corrected, 0, 255), dtype=np.uint8)

        except Exception:
            return source_face

    def _blend_edges(
        self, swapped_face: npt.NDArray[Any], original_face: npt.NDArray[Any]
    ) -> npt.NDArray[Any]:
        """Blend edges for seamless integration."""
        try:
            # Create mask for edge blending
            h, w = swapped_face.shape[:2]
            mask = np.ones((h, w), dtype=np.float32)

            # Create gradient mask (feather edges)
            feather_size = min(h, w) // 10
            for i in range(feather_size):
                alpha = i / feather_size
                mask[i, :] *= alpha
                mask[-i - 1, :] *= alpha
                mask[:, i] *= alpha
                mask[:, -i - 1] *= alpha

            # Blend
            mask_3d = np.stack([mask] * 3, axis=2)
            blended = (
                swapped_face.astype(np.float32) * mask_3d
                + original_face.astype(np.float32) * (1 - mask_3d)
            ).astype(np.uint8)

            return blended

        except Exception:
            return swapped_face

    def _blend_faces(
        self, swapped_face: npt.NDArray[Any], original_face: npt.NDArray[Any], blend_factor: float
    ) -> npt.NDArray[Any]:
        """Blend swapped face with original."""
        blended = swapped_face * (1.0 - blend_factor) + original_face * blend_factor
        return np.asarray(blended, dtype=np.uint8)

    def _composite_face(
        self, background: npt.NDArray[Any], face: npt.NDArray[Any], bbox: dict[str, Any]
    ) -> npt.NDArray[Any]:
        """Composite face back onto background."""
        result = background.copy()

        # Resize face to match bbox
        face_resized = cv2.resize(face, (bbox["width"], bbox["height"]))

        # Simple composite (in production, use proper blending with mask)
        result[
            bbox["y"] : bbox["y"] + bbox["height"],
            bbox["x"] : bbox["x"] + bbox["width"],
        ] = face_resized

        return result

    def get_info(self) -> dict[str, Any]:
        """Get engine information."""
        info = super().get_info()
        info.update(
            {
                "engine_type": "video_face_swap",
                "requires_consent": self.require_consent,
                "consent_given": self.consent_given,
                "has_tensorflow": HAS_TENSORFLOW,
            }
        )
        return info


def create_deepfacelab_engine(
    device: str | None = None,
    gpu: bool = True,
    model_path: str | None = None,
    require_consent: bool = True,
) -> DeepFaceLabEngine:
    """
    Create and initialize DeepFaceLab engine.

    Args:
        device: Device to use
        gpu: Whether to use GPU
        model_path: Path to DeepFaceLab model checkpoint
        require_consent: If True, require explicit user consent

    Returns:
        Initialized DeepFaceLabEngine instance
    """
    engine = DeepFaceLabEngine(
        device=device, gpu=gpu, model_path=model_path, require_consent=require_consent
    )
    engine.initialize()
    return engine
