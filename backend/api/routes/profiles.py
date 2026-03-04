"""
Voice Profile Management Routes

CRUD operations for voice profiles.
Uses ProfileStore for persistent, disk-backed storage.
"""

from __future__ import annotations

import asyncio
import logging
import time

from fastapi import APIRouter, Depends, HTTPException, Request
from pydantic import BaseModel

from ..deps import ProfileStoreDep
from ..exceptions import ProfileNotFoundException
from ..middleware.auth_middleware import require_auth_if_enabled
from ..models import ApiOk
from ..models_additional import (
    ProfileCreateRequest,
    ReferenceAudioPreprocessRequest,
    ReferenceAudioPreprocessResponse,
    VoiceProfile,
)
from ..optimization import cache_response, get_pagination_params

logger = logging.getLogger(__name__)


# Backward-compatible module-level exports for legacy code that imports _profiles
# These are lazy-initialized to avoid import-time issues
def _get_profiles_wrapper():
    """Get the profile store for legacy imports."""
    from backend.project.management.profile_store import get_profile_store

    return get_profile_store()


class _DictToObject:
    """Wrapper to make dict accessible via attribute access (for legacy code)."""

    def __init__(self, data: dict):
        # Use object.__setattr__ to avoid triggering __getattr__
        object.__setattr__(self, "_data", data or {})

    def __getattr__(self, name):
        data = object.__getattribute__(self, "_data")
        return data.get(name)

    def get(self, key, default=None):
        data = object.__getattribute__(self, "_data")
        return data.get(key, default)

    def __getitem__(self, key):
        data = object.__getattribute__(self, "_data")
        return data[key]

    def __contains__(self, key):
        data = object.__getattribute__(self, "_data")
        return key in data

    def __repr__(self):
        data = object.__getattribute__(self, "_data")
        return f"_DictToObject({data})"


# Module-level proxy for backward compatibility
class _ProfilesProxy:
    """Proxy class to provide backward-compatible dict-like access to profiles."""

    @property
    def _store(self):
        return _get_profiles_wrapper()

    def _wrap(self, data):
        """Wrap dict in object for attribute access."""
        if data is None:
            return None
        return _DictToObject(data)

    def get(self, profile_id, default=None):
        result = self._store.get(profile_id)
        if result is None:
            return default
        return self._wrap(result)

    def __getitem__(self, profile_id):
        result = self._store.get(profile_id)
        if result is None:
            raise KeyError(profile_id)
        return self._wrap(result)

    def __setitem__(self, profile_id: str, profile: object) -> None:
        data = profile.__dict__ if hasattr(profile, "__dict__") else {}
        data["id"] = profile_id
        self._store.save(data)

    def __contains__(self, profile_id: object) -> bool:
        return self._store.get(str(profile_id)) is not None

    def __iter__(self):
        return iter(self._store.list_ids())

    def keys(self):
        return self._store.list_ids()

    def values(self):
        return [self._wrap(p) for p in self._store.list_profiles()]

    def items(self):
        for pid in self._store.list_ids():
            yield pid, self._wrap(self._store.get(pid))


# Export for backward compatibility
_profiles = _ProfilesProxy()
from backend.services.persistent_store import PersistentStore

_profile_timestamps: PersistentStore = PersistentStore("profile_timestamps")
_state_lock = asyncio.Lock()

router = APIRouter(
    prefix="/api/profiles",
    tags=["profiles"],
    dependencies=[Depends(require_auth_if_enabled)],
)


class ProfileUpdateRequest(BaseModel):
    """Request model for updating an existing voice profile."""

    name: str | None = None
    language: str | None = None
    emotion: str | None = None
    tags: list[str] | None = None
    avatar_url: str | None = None


# ProfileStore is now used for persistent storage
# Legacy in-memory dict removed - use ProfileStoreDep dependency instead


@router.get(
    "",
    summary="List voice profiles",
    description="""
    Retrieve a paginated list of all voice profiles.

    **Query Parameters:**
    - `page`: Page number (default: 1)
    - `page_size`: Items per page (default: 50, max: 1000)

    **Response:**
    Returns a paginated list of voice profiles with metadata.

    **Example:**
    ```json
    {
      "items": [
        {
          "id": "profile_123",
          "name": "John Doe Voice",
          "language": "en",
          "quality_score": 4.5
        }
      ],
      "total": 1,
      "page": 1,
      "page_size": 50
    }
    ```
    """,
    responses={
        200: {
            "description": "List of voice profiles",
            "content": {
                "application/json": {
                    "example": {
                        "items": [
                            {
                                "id": "profile_123",
                                "name": "John Doe Voice",
                                "language": "en",
                                "quality_score": 4.5,
                            }
                        ],
                        "total": 1,
                        "page": 1,
                        "page_size": 50,
                    }
                }
            },
        }
    },
)
@cache_response(ttl=60)  # Cache for 60 seconds
def list_profiles(
    request: Request,
    profile_store: ProfileStoreDep,
) -> dict:
    """
    List all voice profiles with pagination.

    Query parameters:
    - page: Page number (default: 1)
    - page_size: Items per page (default: 50, max: 1000)
    """
    try:
        # Get pagination parameters
        pagination = get_pagination_params(request, default_page_size=50)

        # Get all profiles from persistent store
        all_profiles = profile_store.list_profiles(
            limit=pagination.page_size,
            offset=(pagination.page - 1) * pagination.page_size,
        )

        # Convert to Pydantic models
        profile_models = [
            VoiceProfile(
                id=p.get("id", ""),
                name=p.get("name", ""),
                language=p.get("language", "en"),
                quality_score=p.get("quality_score", 0.0),
                tags=p.get("tags", []),
            )
            for p in all_profiles
        ]

        return {
            "items": [p.model_dump() for p in profile_models],
            "total": profile_store.count(),
            "page": pagination.page,
            "page_size": pagination.page_size,
        }
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to list profiles: {e}", exc_info=True)
        from fastapi import status

        from ..exceptions import VoiceStudioException

        raise VoiceStudioException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Failed to list profiles: {e!s}",
            error_code="INTERNAL_SERVER_ERROR",
            recovery_suggestion="Please try again later. If the issue persists, contact support.",
        ) from e


@router.get("/{profile_id}", response_model=VoiceProfile)
@cache_response(ttl=300)  # Cache for 5 minutes
def get_profile(
    profile_id: str,
    profile_store: ProfileStoreDep,
) -> VoiceProfile:
    """Get a specific voice profile."""
    try:
        profile_data = profile_store.get(profile_id)
        if profile_data is None:
            raise ProfileNotFoundException(profile_id)
        return VoiceProfile(
            id=profile_data.get("id", ""),
            name=profile_data.get("name", ""),
            language=profile_data.get("language", "en"),
            emotion=profile_data.get("emotion"),
            quality_score=profile_data.get("quality_score", 0.0),
            tags=profile_data.get("tags", []),
            reference_audio_url=profile_data.get("reference_audio_url"),
            avatar_url=profile_data.get("avatar_url"),
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get profile {profile_id}: {e}", exc_info=True)
        from fastapi import status

        from ..exceptions import VoiceStudioException

        raise VoiceStudioException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Failed to get profile: {e!s}",
            error_code="INTERNAL_SERVER_ERROR",
            recovery_suggestion="Please try again later. If the issue persists, contact support.",
            context={"profile_id": profile_id},
        ) from e


@router.post("", response_model=VoiceProfile)
def create_profile(
    request: Request,
    req: ProfileCreateRequest,
    profile_store: ProfileStoreDep,
) -> VoiceProfile:
    """Create a new voice profile."""
    try:
        # Validate input
        if not req.name or not req.name.strip():
            from ..exceptions import InvalidInputException

            raise InvalidInputException(
                "Profile name is required and cannot be empty", field="name", value=req.name
            )

        if not req.language or not req.language.strip():
            from ..exceptions import InvalidInputException

            raise InvalidInputException(
                "Language is required and cannot be empty", field="language", value=req.language
            )

        import uuid

        profile_id = str(uuid.uuid4())

        owner_user_id = request.headers.get("X-User-ID", "").strip() or "local"
        profile_data = {
            "id": profile_id,
            "name": req.name.strip(),
            "language": req.language.strip(),
            "emotion": req.emotion.strip() if req.emotion else None,
            "tags": [tag.strip() for tag in req.tags] if req.tags else [],
            "quality_score": 0.0,
            "avatar_url": req.avatar_url,
            "created_at": time.time(),
            "owner_user_id": owner_user_id,
        }

        # Save to persistent store
        profile_store.save(profile_data)

        raw_tags = profile_data.get("tags")
        tags_list: list[str] = [str(t) for t in raw_tags] if isinstance(raw_tags, list) else []
        profile = VoiceProfile(
            id=profile_id,
            name=str(profile_data["name"]),
            language=str(profile_data["language"]),
            emotion=str(profile_data["emotion"]) if profile_data.get("emotion") else None,
            tags=tags_list,
            quality_score=0.0,
            avatar_url=str(profile_data["avatar_url"]) if profile_data.get("avatar_url") else None,
        )

        logger.info(f"Created profile: {profile_id} - {profile.name}")
        return profile
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to create profile: {e}", exc_info=True)
        from fastapi import status

        from ..exceptions import VoiceStudioException

        raise VoiceStudioException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Failed to create profile: {e!s}",
            error_code="INTERNAL_SERVER_ERROR",
            recovery_suggestion="Please try again later. If the issue persists, contact support.",
        ) from e


@router.put("/{profile_id}", response_model=VoiceProfile)
def update_profile(
    profile_id: str,
    req: ProfileUpdateRequest,
    profile_store: ProfileStoreDep,
) -> VoiceProfile:
    """Update an existing voice profile."""
    try:
        profile_data = profile_store.get(profile_id)
        if profile_data is None:
            raise ProfileNotFoundException(profile_id)

        # Validate input
        if req.name is not None and (not req.name or not req.name.strip()):
            from ..exceptions import InvalidInputException

            raise InvalidInputException(
                "Profile name cannot be empty", field="name", value=req.name
            )

        if req.language is not None and (not req.language or not req.language.strip()):
            from ..exceptions import InvalidInputException

            raise InvalidInputException(
                "Language cannot be empty", field="language", value=req.language
            )

        # Update fields
        if req.name is not None:
            profile_data["name"] = req.name.strip()
        if req.language is not None:
            profile_data["language"] = req.language.strip()
        if req.emotion is not None:
            profile_data["emotion"] = req.emotion.strip() if req.emotion else None
        if req.tags is not None:
            profile_data["tags"] = [tag.strip() for tag in req.tags] if req.tags else []
        if req.avatar_url is not None:
            profile_data["avatar_url"] = req.avatar_url

        # Save back to persistent store
        profile_store.save(profile_data)

        profile = VoiceProfile(
            id=profile_id,
            name=profile_data.get("name", ""),
            language=profile_data.get("language", "en"),
            emotion=profile_data.get("emotion"),
            quality_score=profile_data.get("quality_score", 0.0),
            tags=profile_data.get("tags", []),
            reference_audio_url=profile_data.get("reference_audio_url"),
            avatar_url=profile_data.get("avatar_url"),
        )

        logger.info(f"Updated profile: {profile_id} - {profile.name}")
        return profile
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to update profile {profile_id}: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to update profile: {e!s}") from e


@router.delete("/{profile_id}", response_model=ApiOk)
def delete_profile(
    profile_id: str,
    profile_store: ProfileStoreDep,
) -> ApiOk:
    """Delete a voice profile."""
    try:
        if profile_store.get(profile_id) is None:
            raise ProfileNotFoundException(profile_id)

        profile_store.delete(profile_id)
        logger.info(f"Deleted profile: {profile_id}")
        return ApiOk()
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to delete profile {profile_id}: {e}", exc_info=True)
        from fastapi import status

        from ..exceptions import VoiceStudioException

        raise VoiceStudioException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Failed to delete profile: {e!s}",
            error_code="INTERNAL_SERVER_ERROR",
            recovery_suggestion="Please try again later. If the issue persists, contact support.",
            context={"profile_id": profile_id},
        ) from e


@router.post(
    "/{profile_id}/preprocess-reference",
    response_model=ReferenceAudioPreprocessResponse,
)
async def preprocess_reference_audio(
    profile_id: str,
    req: ReferenceAudioPreprocessRequest,
    profile_store: ProfileStoreDep,
) -> ReferenceAudioPreprocessResponse:
    """
    Advanced reference audio pre-processing and optimization (IDEA 62).

    Analyzes reference audio for quality issues, enhances it automatically,
    and selects optimal segments for voice cloning.
    """
    import os
    import tempfile
    import uuid

    import numpy as np

    from ..models_additional import (
        ReferenceAudioAnalysis,
        ReferenceAudioPreprocessResponse,
    )

    try:
        # Get profile from persistent store
        profile_data = profile_store.get(profile_id)
        if profile_data is None:
            raise ProfileNotFoundException(profile_id)

        # Convert to VoiceProfile for compatibility
        profile = VoiceProfile(
            id=profile_data.get("id", ""),
            name=profile_data.get("name", ""),
            language=profile_data.get("language", "en"),
            reference_audio_url=profile_data.get("reference_audio_url"),
        )

        # Get reference audio path
        reference_audio_path = req.reference_audio_path
        if not reference_audio_path:
            if profile.reference_audio_url and not profile.reference_audio_url.startswith("http"):
                reference_audio_path = profile.reference_audio_url
            else:
                from backend.services.profile_service import resolve_reference_audio_path

                resolved = resolve_reference_audio_path(profile_id)
                if resolved.exists():
                    reference_audio_path = str(resolved)

        if not reference_audio_path or not os.path.exists(reference_audio_path):
            from ..exceptions import FileNotFoundException

            raise FileNotFoundException(f"Reference audio for profile {profile_id}")

        # Try to load audio analysis libraries
        try:
            import librosa
            import soundfile as sf

            HAS_AUDIO_LIBS = True
        except ImportError:
            HAS_AUDIO_LIBS = False
            logger.warning("librosa/soundfile not available for reference audio pre-processing")

        if not HAS_AUDIO_LIBS:
            from fastapi import status

            from ..exceptions import VoiceStudioException

            raise VoiceStudioException(
                status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
                detail="Audio analysis libraries not available. Install librosa and soundfile.",
                error_code="SERVICE_UNAVAILABLE",
                recovery_suggestion="Please install librosa and soundfile libraries to enable reference audio pre-processing.",
                context={"profile_id": profile_id},
            )

        # Load and analyze original audio
        audio, sample_rate = sf.read(reference_audio_path)
        duration = len(audio) / sample_rate

        # Convert to mono if stereo
        if len(audio.shape) > 1:
            channels = audio.shape[1]
            audio_mono = np.mean(audio, axis=1)
        else:
            channels = 1
            audio_mono = audio

        # Analyze for quality issues
        has_noise = False
        has_clipping = False
        has_distortion = False
        recommendations = []
        quality_score = 10.0  # Start with perfect score

        # Check for clipping
        max_amplitude = np.max(np.abs(audio_mono))
        if max_amplitude >= 0.99:
            has_clipping = True
            quality_score -= 2.0
            recommendations.append("Audio has clipping - reduce input gain")

        # Check for noise (high frequency content when speech should be quiet)
        try:
            # Calculate spectral features
            spectral_centroid = np.mean(
                librosa.feature.spectral_centroid(y=audio_mono, sr=sample_rate)
            )
            spectral_rolloff = np.mean(
                librosa.feature.spectral_rolloff(y=audio_mono, sr=sample_rate)
            )

            # High rolloff with low centroid suggests noise
            if spectral_rolloff > 8000 and spectral_centroid < 1000:
                has_noise = True
                quality_score -= 1.5
                recommendations.append("Background noise detected - apply denoising")
        except (ValueError, RuntimeError, TypeError) as spectral_err:
            logger.debug(f"Spectral analysis for noise detection failed: {spectral_err}")

        # Check for distortion (unusual spectral characteristics)
        try:
            zero_crossing_rate = np.mean(librosa.feature.zero_crossing_rate(audio_mono))
            if zero_crossing_rate > 0.2:  # Unusually high ZCR suggests distortion
                has_distortion = True
                quality_score -= 1.0
                recommendations.append("Possible distortion detected - check audio source")
        except (ValueError, RuntimeError, TypeError) as zcr_err:
            logger.debug(f"Zero crossing rate analysis failed: {zcr_err}")

        # Check sample rate (should be >= 16kHz for good quality)
        if sample_rate < 16000:
            quality_score -= 1.0
            recommendations.append(f"Low sample rate ({sample_rate}Hz) - recommend >= 16kHz")

        # Check duration (should be >= 3 seconds for good cloning)
        if duration < 3.0:
            recommendations.append(
                f"Short duration ({duration:.1f}s) - recommend >= 3 seconds for better cloning"
            )
        elif duration > 30.0:
            recommendations.append(
                f"Long duration ({duration:.1f}s) - consider selecting optimal segments"
            )

        quality_score = max(1.0, min(10.0, quality_score))  # Clamp to 1-10

        original_analysis = ReferenceAudioAnalysis(
            quality_score=quality_score,
            has_noise=has_noise,
            has_clipping=has_clipping,
            has_distortion=has_distortion,
            sample_rate=sample_rate,
            duration=duration,
            channels=channels,
            recommendations=recommendations,
            optimal_segments=None,
        )

        # Process audio if auto_enhance is enabled
        processed_audio = audio_mono.copy()
        improvements_applied = []
        quality_improvement = 0.0

        if req.auto_enhance:
            try:
                # Import audio enhancement (app on path via main.py bootstrap)
                from backend.audio.audio_utils import enhance_voice_quality

                # Enhance audio
                processed_audio = enhance_voice_quality(
                    processed_audio,
                    sample_rate,
                    normalize=True,
                    denoise=has_noise,
                    target_lufs=-23.0,
                )

                if has_noise:
                    improvements_applied.append("Denoising applied")
                improvements_applied.append("Normalization applied")

                # Re-analyze processed audio
                processed_max = np.max(np.abs(processed_audio))
                if processed_max < 0.99 and has_clipping:
                    improvements_applied.append("Clipping reduced")
                    quality_improvement += 0.1

                # Estimate quality improvement
                if has_noise:
                    quality_improvement += 0.15
                if has_clipping:
                    quality_improvement += 0.1

                quality_improvement = min(1.0, quality_improvement)

            except Exception as e:
                logger.warning(f"Audio enhancement failed: {e}")

        # Select optimal segments if requested
        optimal_segments = None
        if req.select_optimal_segments:
            try:
                # Simple segment selection based on RMS energy (voice activity)
                segment_duration = max(req.min_segment_duration or 1.0, 1.0)
                hop_length = int(sample_rate * 0.5)  # 0.5s hop
                frame_length = int(sample_rate * segment_duration)

                segments = []
                for i in range(0, len(processed_audio) - frame_length, hop_length):
                    segment = processed_audio[i : i + frame_length]
                    rms = np.sqrt(np.mean(segment**2))

                    # Prefer segments with good RMS (voice activity) and no clipping
                    if rms > 0.01 and np.max(np.abs(segment)) < 0.95:
                        segments.append(
                            {
                                "start_time": i / sample_rate,
                                "end_time": (i + frame_length) / sample_rate,
                                "duration": segment_duration,
                                "rms_energy": float(rms),
                                "quality_score": float(min(10.0, rms * 100)),
                            }
                        )

                # Sort by quality and select best
                segments.sort(key=lambda x: x["quality_score"], reverse=True)
                optimal_segments = segments[: req.max_segments]

                if optimal_segments:
                    original_analysis.optimal_segments = optimal_segments
                    improvements_applied.append(
                        f"Selected {len(optimal_segments)} optimal segments"
                    )

            except Exception as e:
                logger.warning(f"Segment selection failed: {e}")

        # Save processed audio if enhanced
        processed_audio_id = None
        processed_audio_url = None
        processed_analysis = None

        if req.auto_enhance and improvements_applied:
            from backend.services.audio_artifacts import create_audio_artifact_from_wav_array

            # Ensure audio is in correct format
            if processed_audio.dtype != np.float32:
                processed_audio = processed_audio.astype(np.float32)
            processed_audio = np.clip(processed_audio, -1.0, 1.0)

            processed_audio_id, _, _ = create_audio_artifact_from_wav_array(
                processed_audio,
                sample_rate,
                created_by="profiles_preprocess",
                audio_id=f"processed_{profile_id}_{uuid.uuid4().hex[:8]}",
            )

            processed_audio_url = f"/api/voice/audio/{processed_audio_id}"

            # Create processed analysis
            processed_quality = min(10.0, quality_score + quality_improvement * 2.0)
            processed_analysis = ReferenceAudioAnalysis(
                quality_score=processed_quality,
                has_noise=False if "Denoising" in improvements_applied else has_noise,
                has_clipping=(
                    False if "Clipping reduced" in improvements_applied else has_clipping
                ),
                has_distortion=has_distortion,
                sample_rate=sample_rate,
                duration=duration,
                channels=1,
                recommendations=[],
                optimal_segments=optimal_segments,
            )

        return ReferenceAudioPreprocessResponse(
            processed_audio_id=processed_audio_id or f"original_{profile_id}",
            processed_audio_url=processed_audio_url or f"/api/profiles/{profile_id}/reference",
            original_analysis=original_analysis,
            processed_analysis=processed_analysis,
            improvements_applied=improvements_applied,
            quality_improvement=quality_improvement,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Reference audio pre-processing error: {e}", exc_info=True)
        from fastapi import status

        from ..exceptions import VoiceStudioException

        raise VoiceStudioException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail=f"Reference audio pre-processing failed: {e!s}",
            error_code="AUDIO_PROCESSING_ERROR",
            recovery_suggestion="Please check the audio file format and try again.",
            context={"profile_id": profile_id},
        ) from e
