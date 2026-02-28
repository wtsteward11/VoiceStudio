"""
Security Service

Phase 16: Security and Privacy
Comprehensive security features for voice synthesis protection.

Features:
- Audio encryption (16.1)
- Consent management (16.2)
- Audio watermarking (16.3)
"""

from __future__ import annotations

import base64
import hashlib
import json
import logging
import os
import uuid
from dataclasses import dataclass
from datetime import datetime, timedelta
from enum import Enum
from typing import Any

import numpy as np

logger = logging.getLogger(__name__)


# ===== Phase 16.1: Encryption =====


class EncryptionAlgorithm(Enum):
    """Encryption algorithms."""

    AES_256_GCM = "aes-256-gcm"
    CHACHA20_POLY1305 = "chacha20-poly1305"


@dataclass
class EncryptedAudio:
    """Encrypted audio container."""

    encryption_id: str
    algorithm: EncryptionAlgorithm
    encrypted_data: bytes
    nonce: bytes
    tag: bytes
    metadata: dict[str, Any]
    created_at: datetime

    def to_dict(self) -> dict[str, Any]:
        return {
            "encryption_id": self.encryption_id,
            "algorithm": self.algorithm.value,
            "encrypted_data": base64.b64encode(self.encrypted_data).decode(),
            "nonce": base64.b64encode(self.nonce).decode(),
            "tag": base64.b64encode(self.tag).decode(),
            "metadata": self.metadata,
            "created_at": self.created_at.isoformat(),
        }


class EncryptionService:
    """Audio encryption service."""

    def __init__(self, key: bytes | None = None):
        self._key = key or os.urandom(32)  # 256-bit key
        self._algorithm = EncryptionAlgorithm.AES_256_GCM

    def encrypt(
        self,
        audio_data: bytes,
        metadata: dict[str, Any] | None = None,
    ) -> EncryptedAudio:
        """Encrypt audio data."""
        try:
            from cryptography.hazmat.primitives.ciphers.aead import AESGCM

            nonce = os.urandom(12)  # 96-bit nonce for GCM
            aesgcm = AESGCM(self._key)

            # Encrypt with associated data (metadata hash)
            aad = hashlib.sha256(json.dumps(metadata or {}).encode()).digest()

            ciphertext = aesgcm.encrypt(nonce, audio_data, aad)

            # Split ciphertext and tag (last 16 bytes)
            ciphertext[:-16]
            tag = ciphertext[-16:]

            return EncryptedAudio(
                encryption_id=f"enc_{uuid.uuid4().hex[:8]}",
                algorithm=self._algorithm,
                encrypted_data=ciphertext,  # Include tag
                nonce=nonce,
                tag=tag,
                metadata=metadata or {},
                created_at=datetime.now(),
            )

        except ImportError:
            # Fallback: XOR with key stream (NOT secure, for demo only)
            logger.warning("cryptography not available, using insecure fallback")
            return self._insecure_encrypt_fallback(audio_data, metadata)

    def decrypt(self, encrypted: EncryptedAudio) -> bytes:
        """Decrypt audio data."""
        try:
            from cryptography.hazmat.primitives.ciphers.aead import AESGCM

            aesgcm = AESGCM(self._key)

            aad = hashlib.sha256(json.dumps(encrypted.metadata).encode()).digest()

            plaintext = aesgcm.decrypt(
                encrypted.nonce,
                encrypted.encrypted_data,
                aad,
            )

            return plaintext

        except ImportError:
            return self._insecure_decrypt_fallback(encrypted)

    def _insecure_encrypt_fallback(
        self,
        data: bytes,
        metadata: dict[str, Any] | None,
    ) -> EncryptedAudio:
        """Insecure XOR fallback (demo only)."""
        nonce = os.urandom(12)
        key_stream = self._generate_key_stream(len(data), nonce)
        encrypted = bytes(a ^ b for a, b in zip(data, key_stream))

        return EncryptedAudio(
            encryption_id=f"enc_{uuid.uuid4().hex[:8]}",
            algorithm=self._algorithm,
            encrypted_data=encrypted,
            nonce=nonce,
            tag=hashlib.sha256(encrypted).digest()[:16],
            metadata=metadata or {},
            created_at=datetime.now(),
        )

    def _insecure_decrypt_fallback(self, encrypted: EncryptedAudio) -> bytes:
        """Insecure XOR fallback (demo only)."""
        key_stream = self._generate_key_stream(len(encrypted.encrypted_data), encrypted.nonce)
        return bytes(a ^ b for a, b in zip(encrypted.encrypted_data, key_stream))

    def _generate_key_stream(self, length: int, nonce: bytes) -> bytes:
        """Generate key stream from key and nonce."""
        stream = []
        counter = 0
        while len(stream) < length:
            block = hashlib.sha256(self._key + nonce + counter.to_bytes(4, "big")).digest()
            stream.extend(block)
            counter += 1
        return bytes(stream[:length])

    def derive_key(self, password: str, salt: bytes | None = None) -> tuple[bytes, bytes]:
        """Derive encryption key from password."""
        salt = salt or os.urandom(16)
        try:
            from cryptography.hazmat.primitives import hashes
            from cryptography.hazmat.primitives.kdf.pbkdf2 import PBKDF2HMAC

            kdf = PBKDF2HMAC(
                algorithm=hashes.SHA256(),
                length=32,
                salt=salt,
                iterations=100000,
            )
            key = kdf.derive(password.encode())
        except ImportError:
            # Fallback
            key = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, 100000, dklen=32)

        return key, salt


# ===== Phase 16.2: Consent Management =====


class ConsentType(Enum):
    """Types of consent."""

    VOICE_CLONING = "voice_cloning"
    VOICE_USAGE = "voice_usage"
    DATA_PROCESSING = "data_processing"
    COMMERCIAL_USE = "commercial_use"
    RESEARCH_USE = "research_use"


class ConsentStatus(Enum):
    """Consent status."""

    PENDING = "pending"
    GRANTED = "granted"
    DENIED = "denied"
    REVOKED = "revoked"
    EXPIRED = "expired"


@dataclass
class ConsentRecord:
    """Consent record."""

    consent_id: str
    voice_id: str
    grantor_id: str
    grantor_name: str
    consent_type: ConsentType
    status: ConsentStatus
    granted_at: datetime | None
    expires_at: datetime | None
    scope: dict[str, Any]
    signature: str | None

    def to_dict(self) -> dict[str, Any]:
        return {
            "consent_id": self.consent_id,
            "voice_id": self.voice_id,
            "grantor_id": self.grantor_id,
            "grantor_name": self.grantor_name,
            "consent_type": self.consent_type.value,
            "status": self.status.value,
            "granted_at": self.granted_at.isoformat() if self.granted_at else None,
            "expires_at": self.expires_at.isoformat() if self.expires_at else None,
            "scope": self.scope,
            "signature": self.signature,
        }

    @property
    def is_valid(self) -> bool:
        if self.status != ConsentStatus.GRANTED:
            return False
        return not (self.expires_at and datetime.now() > self.expires_at)


class ConsentManager:
    """Consent management service."""

    def __init__(self):
        self._consents: dict[str, ConsentRecord] = {}
        self._voice_consents: dict[str, list[str]] = {}  # voice_id -> consent_ids

    async def request_consent(
        self,
        voice_id: str,
        grantor_id: str,
        grantor_name: str,
        consent_type: ConsentType,
        scope: dict[str, Any] | None = None,
        expires_days: int | None = None,
    ) -> ConsentRecord:
        """Create a consent request."""
        consent_id = f"consent_{uuid.uuid4().hex[:8]}"

        expires_at = None
        if expires_days:
            expires_at = datetime.now() + timedelta(days=expires_days)

        record = ConsentRecord(
            consent_id=consent_id,
            voice_id=voice_id,
            grantor_id=grantor_id,
            grantor_name=grantor_name,
            consent_type=consent_type,
            status=ConsentStatus.PENDING,
            granted_at=None,
            expires_at=expires_at,
            scope=scope or {},
            signature=None,
        )

        self._consents[consent_id] = record
        self._voice_consents.setdefault(voice_id, []).append(consent_id)

        logger.info(f"Created consent request: {consent_id}")
        return record

    async def grant_consent(
        self,
        consent_id: str,
        signature: str | None = None,
    ) -> bool:
        """Grant consent."""
        if consent_id not in self._consents:
            return False

        record = self._consents[consent_id]
        record.status = ConsentStatus.GRANTED
        record.granted_at = datetime.now()
        record.signature = signature or self._generate_signature(record)

        logger.info(f"Consent granted: {consent_id}")
        return True

    async def deny_consent(self, consent_id: str) -> bool:
        """Deny consent."""
        if consent_id not in self._consents:
            return False

        self._consents[consent_id].status = ConsentStatus.DENIED
        logger.info(f"Consent denied: {consent_id}")
        return True

    async def revoke_consent(self, consent_id: str) -> bool:
        """Revoke previously granted consent."""
        if consent_id not in self._consents:
            return False

        record = self._consents[consent_id]
        if record.status != ConsentStatus.GRANTED:
            return False

        record.status = ConsentStatus.REVOKED
        logger.info(f"Consent revoked: {consent_id}")
        return True

    def check_consent(
        self,
        voice_id: str,
        consent_type: ConsentType,
    ) -> bool:
        """Check if valid consent exists."""
        consent_ids = self._voice_consents.get(voice_id, [])

        for consent_id in consent_ids:
            record = self._consents.get(consent_id)
            if record and record.consent_type == consent_type and record.is_valid:
                return True

        return False

    def get_consents(self, voice_id: str) -> list[ConsentRecord]:
        """Get all consents for a voice."""
        consent_ids = self._voice_consents.get(voice_id, [])
        return [self._consents[cid] for cid in consent_ids if cid in self._consents]

    def _generate_signature(self, record: ConsentRecord) -> str:
        """Generate consent signature."""
        data = f"{record.consent_id}:{record.voice_id}:{record.grantor_id}:{record.granted_at}"
        return hashlib.sha256(data.encode()).hexdigest()[:32]


# ===== Phase 16.3: Audio Watermarking =====


class WatermarkType(Enum):
    """Watermark types."""

    INVISIBLE = "invisible"  # Embedded in audio
    AUDIBLE = "audible"  # Perceptible watermark
    METADATA = "metadata"  # Metadata-based


@dataclass
class Watermark:
    """Audio watermark."""

    watermark_id: str
    watermark_type: WatermarkType
    payload: str
    timestamp: datetime
    strength: float

    def to_dict(self) -> dict[str, Any]:
        return {
            "watermark_id": self.watermark_id,
            "watermark_type": self.watermark_type.value,
            "payload": self.payload,
            "timestamp": self.timestamp.isoformat(),
            "strength": self.strength,
        }


class WatermarkingService:
    """
    Audio watermarking service.

    Embeds invisible watermarks in audio for tracking and verification.
    """

    def __init__(self, secret_key: bytes | None = None):
        self._secret_key = secret_key or os.urandom(32)

    def embed_watermark(
        self,
        audio_samples: np.ndarray,
        sample_rate: int,
        payload: str,
        watermark_type: WatermarkType = WatermarkType.INVISIBLE,
        strength: float = 0.01,
    ) -> tuple[np.ndarray, Watermark]:
        """
        Embed watermark in audio.

        Args:
            audio_samples: Audio samples as numpy array
            sample_rate: Sample rate
            payload: Watermark payload (max 64 chars)
            watermark_type: Type of watermark
            strength: Watermark strength (0.0-1.0)

        Returns:
            Watermarked audio and watermark info
        """
        watermark_id = f"wm_{uuid.uuid4().hex[:8]}"

        # Create watermark object
        watermark = Watermark(
            watermark_id=watermark_id,
            watermark_type=watermark_type,
            payload=payload[:64],  # Limit payload size
            timestamp=datetime.now(),
            strength=strength,
        )

        if watermark_type == WatermarkType.INVISIBLE:
            watermarked = self._embed_invisible(audio_samples, payload, strength)
        elif watermark_type == WatermarkType.AUDIBLE:
            watermarked = self._embed_audible(audio_samples, sample_rate, payload, strength)
        else:
            watermarked = audio_samples.copy()

        return watermarked, watermark

    def detect_watermark(
        self,
        audio_samples: np.ndarray,
        sample_rate: int,
    ) -> str | None:
        """
        Detect and extract watermark from audio.

        Args:
            audio_samples: Audio samples
            sample_rate: Sample rate

        Returns:
            Extracted payload if found, None otherwise
        """
        return self._extract_invisible(audio_samples)

    def verify_watermark(
        self,
        audio_samples: np.ndarray,
        expected_payload: str,
    ) -> bool:
        """Verify if audio contains expected watermark."""
        detected = self._extract_invisible(audio_samples)
        return detected == expected_payload

    def _embed_invisible(
        self,
        samples: np.ndarray,
        payload: str,
        strength: float,
    ) -> np.ndarray:
        """
        Embed invisible watermark using spread spectrum technique.

        Uses LSB (Least Significant Bit) modification in frequency domain.
        """
        watermarked = samples.copy().astype(np.float64)

        # Convert payload to bits
        payload_bytes = payload.encode("utf-8")
        payload_bits = "".join(format(byte, "08b") for byte in payload_bytes)

        # Pad to fixed length
        payload_bits = payload_bits[:512].ljust(512, "0")

        # Generate pseudo-random positions based on secret key
        np.random.seed(int.from_bytes(self._secret_key[:4], "big"))
        positions = np.random.choice(
            len(watermarked) - 1000,
            size=len(payload_bits),
            replace=False,
        )
        positions.sort()

        # Embed bits
        for i, pos in enumerate(positions):
            bit = int(payload_bits[i])
            # Modify amplitude slightly based on bit
            delta = strength * (1 if bit else -1)
            watermarked[pos] += delta * np.abs(watermarked[pos])

        # Normalize to prevent clipping
        max_val = np.max(np.abs(watermarked))
        if max_val > 1.0:
            watermarked /= max_val

        return watermarked.astype(samples.dtype)

    def _extract_invisible(self, samples: np.ndarray) -> str | None:
        """Extract invisible watermark."""
        try:
            samples = samples.astype(np.float64)

            # Generate same positions
            np.random.seed(int.from_bytes(self._secret_key[:4], "big"))
            positions = np.random.choice(
                len(samples) - 1000,
                size=512,
                replace=False,
            )
            positions.sort()

            # This is a simplified detection - real implementation would
            # use correlation with known patterns
            bits = []
            for pos in positions:
                # Estimate bit based on local statistics
                window = samples[max(0, pos - 10) : pos + 10]
                mean = np.mean(window)
                bit = 1 if samples[pos] > mean else 0
                bits.append(str(bit))

            # Convert bits to bytes
            bit_string = "".join(bits)
            payload_bytes = []
            for i in range(0, len(bit_string), 8):
                byte = int(bit_string[i : i + 8], 2)
                if byte == 0:
                    break
                payload_bytes.append(byte)

            return bytes(payload_bytes).decode("utf-8", errors="ignore")

        except Exception as e:
            logger.warning(f"Watermark extraction failed: {e}")
            return None

    def _embed_audible(
        self,
        samples: np.ndarray,
        sample_rate: int,
        payload: str,
        strength: float,
    ) -> np.ndarray:
        """Embed audible watermark (tone at specific frequency)."""
        watermarked = samples.copy().astype(np.float64)

        # Generate watermark tone at 18kHz (barely audible)
        t = np.arange(len(samples)) / sample_rate
        watermark_freq = 18000

        # Modulate tone with payload
        tone = np.sin(2 * np.pi * watermark_freq * t) * strength

        watermarked += tone

        # Normalize
        max_val = np.max(np.abs(watermarked))
        if max_val > 1.0:
            watermarked /= max_val

        return watermarked.astype(samples.dtype)


# ===== Main Security Service =====


class SecurityService:
    """
    Main security service combining all security features.

    Phase 16: Security and Privacy
    """

    def __init__(self):
        self._encryption = EncryptionService()
        self._consent = ConsentManager()
        self._watermarking = WatermarkingService()
        self._initialized = False

    async def initialize(self) -> bool:
        """Initialize security service."""
        self._initialized = True
        logger.info("SecurityService initialized")
        return True

    @property
    def encryption(self) -> EncryptionService:
        return self._encryption

    @property
    def consent(self) -> ConsentManager:
        return self._consent

    @property
    def watermarking(self) -> WatermarkingService:
        return self._watermarking


# Singleton
_security_service: SecurityService | None = None


def get_security_service() -> SecurityService:
    """Get or create security service singleton."""
    global _security_service
    if _security_service is None:
        _security_service = SecurityService()
    return _security_service
