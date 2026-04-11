"""
Speech-to-speech conversion orchestration (GAP-051).

Canonical authority for batch STS: resolve source artifact, resolve target RVC checkpoint,
invoke RVCEngine.convert_voice, register output via create_audio_artifact_from_file.
"""

from __future__ import annotations

import asyncio
import logging
import os
import tempfile
import uuid
from pathlib import Path

from backend.api.models_additional import SpeechToSpeechRequest, SpeechToSpeechResponse
from backend.core.exceptions import ServiceError
from backend.services.audio_artifacts import AudioRegistry, create_audio_artifact_from_file
from backend.services.model_provenance_service import get_model_provenance_service
from backend.services.profile_storage_service import get_profile_dir
from backend.services.trust_audit_service import get_trust_audit_service

logger = logging.getLogger(__name__)


def _find_rvc_checkpoint_for_profile(profile_id: str) -> str | None:
    """Return first .pth under the profile directory, if any."""
    try:
        root = get_profile_dir(profile_id)
    except ValueError:
        return None
    if not root.is_dir():
        return None
    for pth in sorted(root.rglob("*.pth")):
        if pth.is_file():
            return str(pth)
    return None


def _wav_duration_seconds(path: str | Path) -> float:
    try:
        import soundfile as sf  # type: ignore[import-untyped]

        info = sf.info(str(path))
        return float(info.frames) / float(info.samplerate) if info.samplerate else 0.0
    except Exception as ex:
        logger.warning("Duration probe failed for %s: %s", path, ex)
        return 0.0


def _try_embed_watermark(wav_path: str) -> tuple[bool, str | None]:
    """Attempt sample-level watermark embedding on the WAV at *wav_path*.

    Returns (watermark_applied, watermark_method).  On failure the original
    file is left untouched and ``(False, None)`` is returned — the STS
    conversion still succeeds without watermarking.
    """
    try:
        import soundfile as sf  # type: ignore[import-untyped]

        from backend.services.security_service import get_security_service

        samples, sr = sf.read(wav_path, dtype="float64")
        svc = get_security_service().watermarking
        payload = f"vs:{Path(wav_path).stem}:{uuid.uuid4().hex[:8]}"
        watermarked, _wm = svc.embed_watermark(
            samples, sr, payload, strength=0.01,
        )
        sf.write(wav_path, watermarked, sr, subtype="PCM_16")
        return True, "invisible_lsb"
    except Exception as exc:
        logger.warning("Watermark embedding skipped (non-blocking): %s", exc)
        return False, None


class SpeechToSpeechService:
    """Batch speech-to-speech conversion (RVC)."""

    @staticmethod
    async def convert(
        request: SpeechToSpeechRequest,
        *,
        auth_subject: str | None = None,
        correlation_id: str | None = None,
    ) -> SpeechToSpeechResponse:
        _trust = get_trust_audit_service()

        async def _audit_sts(
            *,
            audio_id: str | None,
            result: str,
            reason_code: str | None,
            watermark_applied: bool | None,
        ) -> None:
            await _trust.record_sts_conversion(
                request=request,
                audio_id=audio_id,
                result=result,
                reason_code=reason_code,
                auth_subject=auth_subject,
                correlation_id=correlation_id,
                watermark_applied=watermark_applied,
            )

        # Consent gate — must be explicit for any voice identity transformation
        if not request.consent_acknowledged:
            await _audit_sts(
                audio_id=None,
                result="denied",
                reason_code="CONSENT_REQUIRED",
                watermark_applied=None,
            )
            raise ServiceError(
                400,
                {
                    "code": "CONSENT_REQUIRED",
                    "message": (
                        "You must acknowledge that you have permission to transform "
                        "this voice before conversion proceeds."
                    ),
                },
            )

        if request.consent_id and request.consent_id.strip():
            try:
                from backend.services.security_service import ConsentStatus, get_security_service

                _svc = get_security_service()
                _record = _svc.consent.get_consent_by_id(request.consent_id.strip())
                if not _record:
                    await _audit_sts(
                        audio_id=None,
                        result="denied",
                        reason_code="CONSENT_NOT_FOUND",
                        watermark_applied=None,
                    )
                    raise ServiceError(
                        403,
                        {"code": "CONSENT_NOT_FOUND", "message": "Consent record not found."},
                    )
                if _record.status != ConsentStatus.GRANTED:
                    await _audit_sts(
                        audio_id=None,
                        result="denied",
                        reason_code="CONSENT_NOT_GRANTED",
                        watermark_applied=None,
                    )
                    raise ServiceError(
                        403,
                        {
                            "code": "CONSENT_NOT_GRANTED",
                            "message": f"Consent not granted (status={_record.status.value}).",
                        },
                    )
                if not _record.is_valid:
                    await _audit_sts(
                        audio_id=None,
                        result="denied",
                        reason_code="CONSENT_EXPIRED",
                        watermark_applied=None,
                    )
                    raise ServiceError(
                        403,
                        {"code": "CONSENT_EXPIRED", "message": "Consent expired or revoked."},
                    )
            except ServiceError:
                raise
            except Exception as _ce:
                logger.warning("Consent record validation error: %s", _ce)

        source_path = AudioRegistry.get_path(request.source_audio_id)
        if not source_path or not os.path.isfile(source_path):
            raise ServiceError(
                404,
                {
                    "code": "SOURCE_AUDIO_NOT_FOUND",
                    "message": f"Source audio not found for id '{request.source_audio_id}'.",
                },
            )

        target_model = _find_rvc_checkpoint_for_profile(request.target_voice_profile_id)
        if target_model is None:
            logger.info(
                "No .pth under profile %s; RVC will use engine default weights.",
                request.target_voice_profile_id,
            )

        try:
            from backend.ml.models.engine_service import get_engine_service
        except ImportError as e:
            logger.error("Engine service unavailable: %s", e)
            raise ServiceError(
                503,
                {
                    "code": "ENGINE_SERVICE_UNAVAILABLE",
                    "message": "Speech-to-speech engine is not available.",
                },
            ) from e

        engine_service = get_engine_service()
        rvc = engine_service.get_rvc_engine()
        if rvc is None or not getattr(rvc, "is_available", lambda: True)():
            raise ServiceError(
                503,
                {
                    "code": "RVC_UNAVAILABLE",
                    "message": "RVC voice conversion engine is not available.",
                },
            )

        pitch_i = round(request.pitch_shift)

        def _run_convert() -> tuple[str, float, float | None, bool, str | None]:
            fd, out_path = tempfile.mkstemp(suffix=".wav")
            os.close(fd)
            try:
                rvc.convert_voice(
                    source_audio=source_path,
                    target_speaker_model=target_model,
                    output_path=out_path,
                    pitch_shift=pitch_i,
                    enhance_quality=False,
                    calculate_quality=False,
                    index_rate=request.index_rate,
                    protect=request.protect,
                )
                if not os.path.isfile(out_path) or os.path.getsize(out_path) == 0:
                    raise RuntimeError("RVC produced no output file")

                wm_applied, wm_method = _try_embed_watermark(out_path)

                duration = _wav_duration_seconds(out_path)
                gen_audio_id = f"sts_{uuid.uuid4().hex[:12]}"
                audio_id, _, metadata = create_audio_artifact_from_file(
                    out_path,
                    created_by="speech_to_speech",
                    audio_id=gen_audio_id,
                    delete_source=True,
                    source=request.source_audio_id,
                    is_transformed=True,
                    transformation_type="speech_to_speech",
                    watermark_applied=wm_applied,
                    watermark_method=wm_method,
                )
                qs: float | None = None
                if isinstance(metadata, dict):
                    raw_qs = metadata.get("quality_score")
                    if isinstance(raw_qs, (int, float)):
                        qs = float(raw_qs)
                return audio_id, duration, qs, wm_applied, wm_method
            except Exception:
                if os.path.isfile(out_path):
                    try:
                        os.unlink(out_path)
                    except OSError as cleanup_err:
                        logger.debug("Temp output cleanup: %s", cleanup_err)
                raise

        try:
            audio_id, duration, quality, wm_applied, wm_method = await asyncio.to_thread(
                _run_convert
            )
        except ServiceError:
            raise
        except Exception as ex:
            logger.error("Speech-to-speech conversion failed: %s", ex, exc_info=True)
            await _audit_sts(
                audio_id=None,
                result="failed",
                reason_code="SPEECH_TO_SPEECH_FAILED",
                watermark_applied=None,
            )
            raise ServiceError(
                500,
                {
                    "code": "SPEECH_TO_SPEECH_FAILED",
                    "message": f"Voice conversion failed: {ex!s}",
                },
            ) from ex

        _prov = get_model_provenance_service()
        _prov_record = _prov.build(
            engine_id="rvc",
            artifact_id=audio_id,
            correlation_id=correlation_id,
            is_transformed=True,
            transformation_type="speech_to_speech",
        )
        await _prov.attach(audio_id, _prov_record)

        await _audit_sts(
            audio_id=audio_id,
            result="success",
            reason_code=None,
            watermark_applied=wm_applied,
        )

        audio_url = f"/api/audio/{audio_id}"
        return SpeechToSpeechResponse(
            audio_id=audio_id,
            audio_url=audio_url,
            duration=float(duration),
            quality_score=quality,
            engine_used="rvc",
            is_transformed=True,
            transformation_type="speech_to_speech",
            source_audio_id=request.source_audio_id,
            disclosure_text=(
                "This audio was transformed from a registered source audio file "
                "using speech-to-speech conversion (RVC). Ensure you have "
                "permission to use and distribute this voice transformation."
            ),
            watermark_applied=wm_applied,
            watermark_method=wm_method,
        )
