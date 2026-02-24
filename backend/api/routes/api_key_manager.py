"""
API Key Manager Routes

Endpoints for managing API keys for external services.

Security: All endpoints require authentication when auth is enabled.
"""

from __future__ import annotations

import logging
import asyncio
from datetime import datetime

from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel

from backend.security.api_key_store import load_api_keys, save_api_keys

from ..middleware.auth_middleware import require_auth_if_enabled
from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/api-keys", tags=["api-keys"])

# In-memory cache; persisted to JSON file (Phase 7 Sprint 2)
_api_keys: dict[str, APIKey] = {}
_state_lock = asyncio.Lock()


def _load_persisted_keys() -> None:
    """Load API keys from persistent store on startup."""
    global _api_keys
    try:
        stored = load_api_keys()
        for key_id, data in stored.items():
            if not isinstance(data, dict):
                continue
            # Ensure required fields exist
            data.setdefault("metadata", {})
            _api_keys[key_id] = APIKey(**data)
        if stored:
            logger.info("Loaded %d API keys from persistent store", len(stored))
    except Exception as e:
        logger.warning("Failed to load persisted API keys: %s", e)


def _persist_keys() -> None:
    """Save API keys to persistent store."""
    try:
        data = {k: v.model_dump() for k, v in _api_keys.items()}
        if save_api_keys(data):
            logger.debug("Persisted %d API keys", len(data))
    except Exception as e:
        logger.warning("Failed to persist API keys: %s", e)


# Load on module init
_load_persisted_keys()


class APIKey(BaseModel):
    """API key information."""

    key_id: str
    service_name: str  # OpenAI, ElevenLabs, Voice.ai, etc.
    key_value: str  # Encrypted in production
    description: str | None = None
    created_at: str
    last_used: str | None = None
    is_active: bool = True
    usage_count: int = 0
    metadata: dict[str, str] = {}


class APIKeyCreateRequest(BaseModel):
    """Request to create a new API key."""

    service_name: str
    key_value: str
    description: str | None = None
    metadata: dict[str, str] = {}


class APIKeyUpdateRequest(BaseModel):
    """Request to update an API key."""

    key_value: str | None = None
    description: str | None = None
    is_active: bool | None = None
    metadata: dict[str, str] = {}


class APIKeyResponse(BaseModel):
    """API key response (key_value is masked)."""

    key_id: str
    service_name: str
    key_value_masked: str  # Shows only last 4 characters
    description: str | None = None
    created_at: str
    last_used: str | None = None
    is_active: bool = True
    usage_count: int = 0
    metadata: dict[str, str] = {}


def _mask_key(key_value: str) -> str:
    """Mask API key value (show only last 4 characters)."""
    if len(key_value) <= 4:
        return "*" * len(key_value)
    return "*" * (len(key_value) - 4) + key_value[-4:]


def _generate_key_id() -> str:
    """Generate a unique key ID."""
    import uuid

    return f"key-{uuid.uuid4().hex[:8]}"


@router.get(
    "", response_model=list[APIKeyResponse], dependencies=[Depends(require_auth_if_enabled)]
)
@cache_response(ttl=60)  # Cache for 60 seconds (API key list may change)
async def list_api_keys():
    """List all API keys."""
    try:
        keys = []
        for key in _api_keys.values():
            # Decrypt to mask original value
            try:
                decrypted = _decrypt_key(key.key_value)
                masked_value = _mask_key(decrypted)
            except Exception:
                # If decryption fails, mask encrypted value
                masked_value = _mask_key(key.key_value)

            keys.append(
                APIKeyResponse(
                    key_id=key.key_id,
                    service_name=key.service_name,
                    key_value_masked=masked_value,
                    description=key.description,
                    created_at=key.created_at,
                    last_used=key.last_used,
                    is_active=key.is_active,
                    usage_count=key.usage_count,
                    metadata=key.metadata,
                )
            )
        return sorted(keys, key=lambda k: k.service_name)
    except Exception as e:
        logger.error(f"Failed to list API keys: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to list API keys: {e!s}",
        ) from e


@router.get(
    "/{key_id}", response_model=APIKeyResponse, dependencies=[Depends(require_auth_if_enabled)]
)
@cache_response(ttl=60)  # Cache for 60 seconds (API key info may change)
async def get_api_key(key_id: str):
    """Get a specific API key."""
    try:
        if key_id not in _api_keys:
            raise HTTPException(status_code=404, detail=f"API key '{key_id}' not found")

        key = _api_keys[key_id]

        # Decrypt to mask original value
        try:
            decrypted = _decrypt_key(key.key_value)
            masked_value = _mask_key(decrypted)
        except Exception:
            # If decryption fails, mask encrypted value
            masked_value = _mask_key(key.key_value)

        return APIKeyResponse(
            key_id=key.key_id,
            service_name=key.service_name,
            key_value_masked=masked_value,
            description=key.description,
            created_at=key.created_at,
            last_used=key.last_used,
            is_active=key.is_active,
            usage_count=key.usage_count,
            metadata=key.metadata,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to get API key: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to get API key: {e!s}",
        ) from e


def _encrypt_key(key_value: str) -> str:
    """Encrypt API key value."""
    try:
        import base64
        import os

        from cryptography.fernet import Fernet

        encryption_key_bytes: bytes | None = None
        encryption_key_str = os.getenv("API_KEY_ENCRYPTION_KEY")
        if not encryption_key_str:
            key_file = os.path.join(
                os.path.dirname(__file__), "..", "..", "..", ".api_key_encryption.key"
            )
            if os.path.exists(key_file):
                with open(key_file, "rb") as f:
                    encryption_key_bytes = f.read()
            else:
                encryption_key_bytes = Fernet.generate_key()
                os.makedirs(os.path.dirname(key_file), exist_ok=True)
                with open(key_file, "wb") as f:
                    f.write(encryption_key_bytes)
                logger.warning("Generated new encryption key. Store securely in production!")
        else:
            encryption_key_bytes = encryption_key_str.encode()

        fernet = Fernet(encryption_key_bytes)
        encrypted = fernet.encrypt(key_value.encode())
        return base64.b64encode(encrypted).decode()
    except ImportError:
        # Fallback: simple base64 encoding (not secure, but better than plain text)
        import base64

        logger.warning("cryptography not available. Using base64 encoding (not secure).")
        return base64.b64encode(key_value.encode()).decode()
    except Exception as e:
        logger.error(f"Failed to encrypt key: {e}")
        # Fallback to base64
        import base64

        return base64.b64encode(key_value.encode()).decode()


def _decrypt_key(encrypted_value: str) -> str:
    """Decrypt API key value."""
    try:
        import base64
        import os

        from cryptography.fernet import Fernet

        encryption_key_env = os.getenv("API_KEY_ENCRYPTION_KEY")
        encryption_key: bytes | None = None

        if encryption_key_env:
            encryption_key = encryption_key_env.encode()
        else:
            key_file = os.path.join(
                os.path.dirname(__file__),
                "..",
                "..",
                "..",
                ".api_key_encryption.key",
            )
            if os.path.exists(key_file):
                with open(key_file, "rb") as f:
                    encryption_key = f.read()
            else:
                raise ValueError("Encryption key not found")

        if encryption_key is None:
            raise ValueError("Encryption key not available")

        fernet = Fernet(encryption_key)
        encrypted = base64.b64decode(encrypted_value.encode())
        return fernet.decrypt(encrypted).decode()
    except ImportError:
        # Fallback: base64 decoding
        import base64

        return base64.b64decode(encrypted_value.encode()).decode()
    except Exception as e:
        logger.error(f"Failed to decrypt key: {e}")
        raise ValueError(f"Failed to decrypt key: {e!s}")


def _validate_key_format(service_name: str, key_value: str) -> tuple[bool, str | None]:
    """Validate API key format for a service."""
    service_name_lower = service_name.lower()

    if "openai" in service_name_lower:
        # OpenAI keys start with sk- and are 51 characters
        if not key_value.startswith("sk-") or len(key_value) < 20:
            return (
                False,
                "OpenAI key must start with 'sk-' and be at least 20 characters",
            )
    elif "elevenlabs" in service_name_lower:
        # ElevenLabs keys are typically 32+ characters
        if len(key_value) < 20:
            return False, "ElevenLabs key must be at least 20 characters"
    elif "azure" in service_name_lower:
        # Azure Speech - format: region:key
        parts = key_value.split(":")
        if len(parts) != 2 or len(parts[0]) == 0 or len(parts[1]) == 0:
            return False, "Azure key must be in format 'region:key'"
    elif "google" in service_name_lower or "gcp" in service_name_lower:
        # Google Cloud - JSON key
        try:
            import json

            json.loads(key_value)
        except json.JSONDecodeError:
            return False, "Google Cloud key must be valid JSON"
    elif "aws" in service_name_lower or "polly" in service_name_lower:
        # AWS - format: access_key:secret_key
        parts = key_value.split(":")
        if len(parts) != 2 or len(parts[0]) == 0 or len(parts[1]) == 0:
            return False, "AWS key must be in format 'access_key:secret_key'"
    elif "deepgram" in service_name_lower:
        # Deepgram keys start with token_ or dg_
        if not key_value.startswith(("token_", "dg_")) or len(key_value) < 20:
            return (
                False,
                "Deepgram key must start with 'token_' or 'dg_' and be at least 20 characters",
            )
    elif "assemblyai" in service_name_lower:
        # AssemblyAI keys are typically 32+ characters
        if len(key_value) < 20:
            return False, "AssemblyAI key must be at least 20 characters"
    else:
        # Generic validation
        if len(key_value) < 10:
            return False, "API key must be at least 10 characters"

    return True, None


@router.post("", response_model=APIKeyResponse, dependencies=[Depends(require_auth_if_enabled)])
async def create_api_key(request: APIKeyCreateRequest):
    """Create a new API key."""
    try:
        if not request.service_name or not request.key_value:
            raise HTTPException(status_code=400, detail="Service name and key value are required")

        # Validate key format
        is_valid, error_message = _validate_key_format(request.service_name, request.key_value)
        if not is_valid:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid API key format: {error_message}",
            )

        # Encrypt the key value
        encrypted_key = _encrypt_key(request.key_value)

        key_id = _generate_key_id()
        now = datetime.utcnow().isoformat()

        key = APIKey(
            key_id=key_id,
            service_name=request.service_name,
            key_value=encrypted_key,  # Encrypted
            description=request.description,
            created_at=now,
            is_active=True,
            metadata=request.metadata,
        )

        _api_keys[key_id] = key
        _persist_keys()

        logger.info(f"Created API key for service: {request.service_name}")

        return APIKeyResponse(
            key_id=key.key_id,
            service_name=key.service_name,
            key_value_masked=_mask_key(request.key_value),  # Mask original, not encrypted
            description=key.description,
            created_at=key.created_at,
            last_used=key.last_used,
            is_active=key.is_active,
            usage_count=key.usage_count,
            metadata=key.metadata,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to create API key: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to create API key: {e!s}",
        ) from e


@router.put(
    "/{key_id}", response_model=APIKeyResponse, dependencies=[Depends(require_auth_if_enabled)]
)
async def update_api_key(key_id: str, request: APIKeyUpdateRequest):
    """Update an API key."""
    try:
        if key_id not in _api_keys:
            raise HTTPException(status_code=404, detail=f"API key '{key_id}' not found")

        key = _api_keys[key_id]

        if request.key_value is not None:
            # Validate key format
            is_valid, error_message = _validate_key_format(key.service_name, request.key_value)
            if not is_valid:
                raise HTTPException(
                    status_code=400,
                    detail=f"Invalid API key format: {error_message}",
                )
            # Encrypt the key value
            key.key_value = _encrypt_key(request.key_value)
        if request.description is not None:
            key.description = request.description
        if request.is_active is not None:
            key.is_active = request.is_active
        if request.metadata:
            key.metadata.update(request.metadata)

        _api_keys[key_id] = key
        _persist_keys()

        logger.info(f"Updated API key: {key_id}")

        return APIKeyResponse(
            key_id=key.key_id,
            service_name=key.service_name,
            key_value_masked=_mask_key(key.key_value),
            description=key.description,
            created_at=key.created_at,
            last_used=key.last_used,
            is_active=key.is_active,
            usage_count=key.usage_count,
            metadata=key.metadata,
        )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to update API key: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to update API key: {e!s}",
        ) from e


@router.delete("/{key_id}", dependencies=[Depends(require_auth_if_enabled)])
async def delete_api_key(key_id: str):
    """Delete an API key."""
    try:
        if key_id not in _api_keys:
            raise HTTPException(status_code=404, detail=f"API key '{key_id}' not found")

        del _api_keys[key_id]
        _persist_keys()
        logger.info(f"Deleted API key: {key_id}")

        return {"message": f"API key '{key_id}' deleted successfully"}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to delete API key: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to delete API key: {e!s}",
        ) from e


@router.post("/{key_id}/validate", dependencies=[Depends(require_auth_if_enabled)])
async def validate_api_key(key_id: str):
    """Validate an API key by making a test API call."""
    try:
        if key_id not in _api_keys:
            raise HTTPException(status_code=404, detail=f"API key '{key_id}' not found")

        key = _api_keys[key_id]

        # Decrypt key for validation
        try:
            decrypted_key = _decrypt_key(key.key_value)
        except Exception as e:
            logger.error(f"Failed to decrypt key for validation: {e}")
            return {
                "valid": False,
                "message": f"Failed to decrypt key: {e!s}",
                "last_used": None,
            }

        # Validate API key by making a test request to the service
        now = datetime.utcnow().isoformat()
        is_valid = False
        error_message = None

        try:
            service_name_lower = key.service_name.lower()

            if "openai" in service_name_lower:
                # Validate OpenAI API key
                import httpx

                async with httpx.AsyncClient() as client:
                    response = await client.get(
                        "https://api.openai.com/v1/models",
                        headers={"Authorization": f"Bearer {decrypted_key}"},
                        timeout=5.0,
                    )
                    is_valid = response.status_code == 200
                    if not is_valid:
                        error_message = f"OpenAI API returned {response.status_code}"

            elif "elevenlabs" in service_name_lower:
                # Validate ElevenLabs API key
                import httpx

                async with httpx.AsyncClient() as client:
                    response = await client.get(
                        "https://api.elevenlabs.io/v1/user",
                        headers={"xi-api-key": decrypted_key},
                        timeout=5.0,
                    )
                    is_valid = response.status_code == 200
                    if not is_valid:
                        error_message = f"ElevenLabs API returned {response.status_code}"

            elif "azure" in service_name_lower:
                # Azure Speech - validate format (region and key)
                parts = decrypted_key.split(":")
                is_valid = len(parts) == 2 and len(parts[0]) > 0 and len(parts[1]) > 0
                if not is_valid:
                    error_message = "Azure key must be in format 'region:key'"

            elif "google" in service_name_lower or "gcp" in service_name_lower:
                # Google Cloud - validate JSON key format
                try:
                    import json

                    json.loads(decrypted_key)
                    is_valid = True
                except json.JSONDecodeError:
                    is_valid = False
                    error_message = "Google Cloud key must be valid JSON"

            elif "aws" in service_name_lower or "polly" in service_name_lower:
                # AWS - validate format (access key and secret)
                parts = decrypted_key.split(":")
                is_valid = len(parts) == 2 and len(parts[0]) > 0 and len(parts[1]) > 0
                if not is_valid:
                    error_message = "AWS key must be in format 'access_key:secret_key'"

            elif "deepgram" in service_name_lower:
                # Deepgram - validate format (starts with token)
                is_valid = len(decrypted_key) > 20 and decrypted_key.startswith(("token_", "dg_"))
                if not is_valid:
                    error_message = "Deepgram key format invalid"

            elif "assemblyai" in service_name_lower:
                # AssemblyAI - validate format
                is_valid = len(decrypted_key) > 20
                if not is_valid:
                    error_message = "AssemblyAI key format invalid"

            else:
                # Generic validation - check basic format
                is_valid = len(decrypted_key) >= 10
                if not is_valid:
                    error_message = "API key too short"

        except Exception as e:
            logger.warning(f"API key validation error for {key.service_name}: {e}")
            is_valid = False
            error_message = f"Validation error: {e!s}"

        # Update key usage
        if is_valid:
            key.last_used = now
            key.usage_count += 1
            _api_keys[key_id] = key
            _persist_keys()
            logger.info(f"Validated API key: {key_id} for {key.service_name}")
        else:
            logger.warning(f"API key validation failed: {key_id} for {key.service_name}")

        return {
            "valid": is_valid,
            "message": (
                f"API key for {key.service_name} is valid"
                if is_valid
                else f"API key validation failed: {error_message or 'Invalid key format'}"
            ),
            "last_used": now if is_valid else None,
        }
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to validate API key: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to validate API key: {e!s}",
        ) from e


@router.get(
    "/services/list", response_model=list[str], dependencies=[Depends(require_auth_if_enabled)]
)
@cache_response(ttl=600)  # Cache for 10 minutes (supported services are static)
async def list_supported_services():
    """List all supported service names."""
    return [
        "OpenAI",
        "ElevenLabs",
        "Voice.ai",
        "Lyrebird",
        "Azure Speech",
        "Google Cloud TTS",
        "AWS Polly",
        "Deepgram",
        "AssemblyAI",
        "Rev.ai",
        "Other",
    ]
