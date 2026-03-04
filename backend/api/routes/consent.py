"""
Voice consent API routes (Item 22: Trust and Safety).

Exposes consent record creation, listing, and revocation.
"""

from __future__ import annotations

import logging

from fastapi import APIRouter, HTTPException

from backend.api.models_additional import (
    VoiceConsentCreate,
    VoiceConsentRecord,
    VoiceConsentRevoke,
)
from backend.services.security_service import (
    ConsentStatus,
    ConsentType,
    get_security_service,
)

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/consent", tags=["consent"])


def _record_to_response(record):
    """Map ConsentRecord to VoiceConsentRecord API model."""
    return VoiceConsentRecord(
        consent_id=record.consent_id,
        voice_id=record.voice_id,
        grantor_id=record.grantor_id,
        grantor_name=record.grantor_name,
        consent_type=record.consent_type.value,
        status=record.status.value,
        granted_at=record.granted_at.isoformat() if record.granted_at else None,
        expires_at=record.expires_at.isoformat() if record.expires_at else None,
        reference_audio_hash=record.reference_audio_hash,
        allowed_uses=record.scope or None,
        signature=record.signature,
    )


@router.get("/voice/{voice_id}", response_model=list[VoiceConsentRecord])
async def list_consents_for_voice(voice_id: str):
    """List all consent records for a voice."""
    svc = get_security_service()
    consents = svc.consent.get_consents(voice_id)
    return [_record_to_response(r) for r in consents]


@router.post("/request", response_model=VoiceConsentRecord)
async def request_consent(body: VoiceConsentCreate):
    """Create a consent request (pending until granted)."""
    svc = get_security_service()
    try:
        consent_type = ConsentType(body.consent_type)
    except ValueError:
        consent_type = ConsentType.VOICE_CLONING
    record = await svc.consent.request_consent(
        voice_id=body.voice_id,
        grantor_id=body.grantor_id,
        grantor_name=body.grantor_name,
        consent_type=consent_type,
        scope=body.allowed_uses,
        expires_days=body.expires_days,
        reference_audio_hash=body.reference_audio_hash,
    )
    return _record_to_response(record)


@router.post("/grant/{consent_id}", response_model=VoiceConsentRecord)
async def grant_consent(consent_id: str):
    """Grant a pending consent."""
    svc = get_security_service()
    record = svc.consent.get_consent_by_id(consent_id)
    if not record:
        raise HTTPException(status_code=404, detail="Consent not found")
    if record.status != ConsentStatus.PENDING:
        raise HTTPException(
            status_code=400,
            detail=f"Consent is not pending (status={record.status.value})",
        )
    ok = await svc.consent.grant_consent(consent_id)
    if not ok:
        raise HTTPException(status_code=500, detail="Failed to grant consent")
    record = svc.consent.get_consent_by_id(consent_id)
    return _record_to_response(record)


@router.post("/revoke", response_model=dict)
async def revoke_consent(body: VoiceConsentRevoke):
    """Revoke a granted consent."""
    svc = get_security_service()
    ok = await svc.consent.revoke_consent(body.consent_id)
    if not ok:
        raise HTTPException(
            status_code=400,
            detail="Consent not found or not granted; cannot revoke",
        )
    return {"status": "revoked", "consent_id": body.consent_id}


@router.get("/{consent_id}", response_model=VoiceConsentRecord)
async def get_consent(consent_id: str):
    """Get a consent record by ID."""
    svc = get_security_service()
    record = svc.consent.get_consent_by_id(consent_id)
    if not record:
        raise HTTPException(status_code=404, detail="Consent not found")
    return _record_to_response(record)
