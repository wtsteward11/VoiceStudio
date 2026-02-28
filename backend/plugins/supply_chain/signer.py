"""
Plugin Package Signing for VoiceStudio.

Phase 5B M4 Enhancement: Provides cryptographic signing capabilities for plugin
packages with local keystore management and key rotation support.

This module implements:
    - Ed25519 key pair generation and storage
    - Local keystore with multiple key support
    - Package signature generation and verification
    - Key rotation with key history tracking
    - Privacy-respecting local-first operation (no cloud key management)

The signing system uses Ed25519 for fast, secure, and compact signatures.
Keys are stored locally in a keystore file with passphrase protection.
"""

from __future__ import annotations

import base64
import hashlib
import json
import logging
import os
import secrets
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timezone
from enum import Enum
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

# Use cryptography library for Ed25519
try:
    from cryptography.fernet import Fernet, InvalidToken
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric.ed25519 import (
        Ed25519PrivateKey,
        Ed25519PublicKey,
    )
    from cryptography.hazmat.primitives.kdf.pbkdf2 import PBKDF2HMAC

    HAS_CRYPTOGRAPHY = True
except ImportError:
    HAS_CRYPTOGRAPHY = False
    Ed25519PrivateKey = None
    Ed25519PublicKey = None

logger = logging.getLogger(__name__)


class KeyStatus(Enum):
    """Status of a signing key."""

    ACTIVE = "active"  # Currently used for signing
    ROTATED = "rotated"  # Previously active, still valid for verification
    REVOKED = "revoked"  # Revoked, should not be used


class SignatureAlgorithm(Enum):
    """Supported signature algorithms."""

    ED25519 = "ed25519"


@dataclass
class KeyMetadata:
    """Metadata for a signing key."""

    key_id: str  # Unique key identifier
    fingerprint: str  # Key fingerprint (hash of public key)
    created_at: str  # ISO 8601 creation timestamp
    status: KeyStatus = KeyStatus.ACTIVE  # Key status
    rotated_at: Optional[str] = None  # When key was rotated (if applicable)
    revoked_at: Optional[str] = None  # When key was revoked (if applicable)
    description: Optional[str] = None  # Optional key description
    algorithm: SignatureAlgorithm = SignatureAlgorithm.ED25519

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        return {
            "key_id": self.key_id,
            "fingerprint": self.fingerprint,
            "created_at": self.created_at,
            "status": self.status.value,
            "rotated_at": self.rotated_at,
            "revoked_at": self.revoked_at,
            "description": self.description,
            "algorithm": self.algorithm.value,
        }

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> KeyMetadata:
        """Create from dictionary."""
        return cls(
            key_id=data.get("key_id", ""),
            fingerprint=data.get("fingerprint", ""),
            created_at=data.get("created_at", ""),
            status=KeyStatus(data.get("status", "active")),
            rotated_at=data.get("rotated_at"),
            revoked_at=data.get("revoked_at"),
            description=data.get("description"),
            algorithm=SignatureAlgorithm(data.get("algorithm", "ed25519")),
        )


@dataclass
class KeyEntry:
    """A key entry in the keystore."""

    metadata: KeyMetadata
    public_key: str  # Base64-encoded public key
    private_key_encrypted: str  # Base64-encoded encrypted private key
    salt: str  # Base64-encoded salt for key derivation

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        return {
            "metadata": self.metadata.to_dict(),
            "public_key": self.public_key,
            "private_key_encrypted": self.private_key_encrypted,
            "salt": self.salt,
        }

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> KeyEntry:
        """Create from dictionary."""
        return cls(
            metadata=KeyMetadata.from_dict(data.get("metadata", {})),
            public_key=data.get("public_key", ""),
            private_key_encrypted=data.get("private_key_encrypted", ""),
            salt=data.get("salt", ""),
        )


@dataclass
class Signature:
    """A cryptographic signature for a package."""

    key_id: str  # Key used for signing
    signature_id: str = ""  # Unique signature identifier (auto-generated)
    algorithm: SignatureAlgorithm = SignatureAlgorithm.ED25519
    signature: str = ""  # Base64-encoded signature
    signed_at: str = ""  # ISO 8601 timestamp
    package_digest: Dict[str, str] = field(default_factory=dict)  # Package hashes

    def __post_init__(self):
        """Initialize signature ID if not set."""
        if not self.signature_id:
            self.signature_id = str(uuid.uuid4())
        if not self.signed_at:
            self.signed_at = datetime.now(timezone.utc).isoformat()

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        return {
            "signature_id": self.signature_id,
            "key_id": self.key_id,
            "algorithm": self.algorithm.value,
            "signature": self.signature,
            "signed_at": self.signed_at,
            "package_digest": self.package_digest,
        }

    def to_json(self, indent: int = 2) -> str:
        """Convert to JSON string."""
        return json.dumps(self.to_dict(), indent=indent)

    def save(self, path: Path) -> None:
        """Save signature to file."""
        path.write_text(self.to_json(), encoding="utf-8")

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> Signature:
        """Create from dictionary."""
        return cls(
            signature_id=data.get("signature_id", ""),
            key_id=data.get("key_id", ""),
            algorithm=SignatureAlgorithm(data.get("algorithm", "ed25519")),
            signature=data.get("signature", ""),
            signed_at=data.get("signed_at", ""),
            package_digest=data.get("package_digest", {}),
        )

    @classmethod
    def load(cls, path: Path) -> Signature:
        """Load signature from file."""
        data = json.loads(path.read_text(encoding="utf-8"))
        return cls.from_dict(data)


class Keystore:
    """
    Local keystore for managing signing keys.

    Provides secure storage for Ed25519 key pairs with:
    - Passphrase-based encryption for private keys
    - Support for multiple keys
    - Key rotation with history
    - Key revocation
    """

    KEYSTORE_VERSION = "1.0"

    def __init__(self, keystore_path: Path):
        """
        Initialize keystore.

        Args:
            keystore_path: Path to the keystore file
        """
        if not HAS_CRYPTOGRAPHY:
            raise ImportError(
                "cryptography library required for signing. "
                "Install with: pip install cryptography"
            )

        self.keystore_path = Path(keystore_path)
        self._keys: Dict[str, KeyEntry] = {}
        self._active_key_id: Optional[str] = None
        self._loaded = False

    def _derive_encryption_key(self, passphrase: str, salt: bytes) -> bytes:
        """Derive encryption key from passphrase using PBKDF2."""
        kdf = PBKDF2HMAC(
            algorithm=hashes.SHA256(),
            length=32,
            salt=salt,
            iterations=600000,  # OWASP recommended minimum
        )
        key = kdf.derive(passphrase.encode("utf-8"))
        return base64.urlsafe_b64encode(key)

    def _encrypt_private_key(
        self, private_key: Ed25519PrivateKey, passphrase: str
    ) -> Tuple[str, str]:
        """
        Encrypt private key with passphrase.

        Returns:
            Tuple of (encrypted_key_b64, salt_b64)
        """
        salt = secrets.token_bytes(16)
        encryption_key = self._derive_encryption_key(passphrase, salt)
        fernet = Fernet(encryption_key)

        private_bytes = private_key.private_bytes(
            encoding=serialization.Encoding.Raw,
            format=serialization.PrivateFormat.Raw,
            encryption_algorithm=serialization.NoEncryption(),
        )

        encrypted = fernet.encrypt(private_bytes)
        return (
            base64.b64encode(encrypted).decode("ascii"),
            base64.b64encode(salt).decode("ascii"),
        )

    def _decrypt_private_key(
        self,
        encrypted_key_b64: str,
        salt_b64: str,
        passphrase: str,
    ) -> Ed25519PrivateKey:
        """Decrypt private key with passphrase."""
        salt = base64.b64decode(salt_b64)
        encryption_key = self._derive_encryption_key(passphrase, salt)
        fernet = Fernet(encryption_key)

        encrypted = base64.b64decode(encrypted_key_b64)
        try:
            decrypted = fernet.decrypt(encrypted)
        except InvalidToken:
            raise ValueError("Invalid passphrase or corrupted key data")

        return Ed25519PrivateKey.from_private_bytes(decrypted)

    def _compute_fingerprint(self, public_key: Ed25519PublicKey) -> str:
        """Compute fingerprint for a public key."""
        public_bytes = public_key.public_bytes(
            encoding=serialization.Encoding.Raw,
            format=serialization.PublicFormat.Raw,
        )
        return hashlib.sha256(public_bytes).hexdigest()[:16]

    def create(self, passphrase: str) -> None:
        """
        Create a new empty keystore.

        Args:
            passphrase: Passphrase to encrypt keys (required but may be empty for dev)
        """
        self._keys = {}
        self._active_key_id = None
        self._save(passphrase)
        self._loaded = True
        logger.info(f"Created new keystore at {self.keystore_path}")

    def load(self, passphrase: str) -> None:
        """
        Load keystore from file.

        Args:
            passphrase: Passphrase to decrypt keys
        """
        if not self.keystore_path.exists():
            raise FileNotFoundError(f"Keystore not found: {self.keystore_path}")

        data = json.loads(self.keystore_path.read_text(encoding="utf-8"))

        version = data.get("version", "1.0")
        if version != self.KEYSTORE_VERSION:
            logger.warning(f"Keystore version mismatch: {version} vs {self.KEYSTORE_VERSION}")

        self._active_key_id = data.get("active_key_id")
        self._keys = {
            key_id: KeyEntry.from_dict(entry) for key_id, entry in data.get("keys", {}).items()
        }
        self._loaded = True
        logger.info(f"Loaded keystore with {len(self._keys)} keys")

    def _save(self, passphrase: str) -> None:
        """Save keystore to file."""
        data = {
            "version": self.KEYSTORE_VERSION,
            "active_key_id": self._active_key_id,
            "keys": {key_id: entry.to_dict() for key_id, entry in self._keys.items()},
        }

        self.keystore_path.parent.mkdir(parents=True, exist_ok=True)
        self.keystore_path.write_text(
            json.dumps(data, indent=2),
            encoding="utf-8",
        )

    def generate_key(
        self,
        passphrase: str,
        description: Optional[str] = None,
        set_active: bool = True,
    ) -> KeyMetadata:
        """
        Generate a new signing key pair.

        Args:
            passphrase: Passphrase to encrypt the private key
            description: Optional key description
            set_active: Whether to set this key as the active signing key

        Returns:
            Metadata for the new key
        """
        # Generate Ed25519 key pair
        private_key = Ed25519PrivateKey.generate()
        public_key = private_key.public_key()

        # Compute key ID and fingerprint
        key_id = str(uuid.uuid4())
        fingerprint = self._compute_fingerprint(public_key)

        # Encrypt private key
        encrypted_private, salt = self._encrypt_private_key(private_key, passphrase)

        # Encode public key
        public_bytes = public_key.public_bytes(
            encoding=serialization.Encoding.Raw,
            format=serialization.PublicFormat.Raw,
        )
        public_b64 = base64.b64encode(public_bytes).decode("ascii")

        # Create metadata
        metadata = KeyMetadata(
            key_id=key_id,
            fingerprint=fingerprint,
            created_at=datetime.now(timezone.utc).isoformat(),
            description=description,
        )

        # Create key entry
        entry = KeyEntry(
            metadata=metadata,
            public_key=public_b64,
            private_key_encrypted=encrypted_private,
            salt=salt,
        )

        self._keys[key_id] = entry

        if set_active:
            # Rotate any existing active key
            if self._active_key_id and self._active_key_id != key_id:
                old_entry = self._keys.get(self._active_key_id)
                if old_entry:
                    old_entry.metadata.status = KeyStatus.ROTATED
                    old_entry.metadata.rotated_at = datetime.now(timezone.utc).isoformat()

            self._active_key_id = key_id

        self._save(passphrase)
        logger.info(f"Generated new key: {key_id} (fingerprint: {fingerprint})")

        return metadata

    def rotate_key(
        self,
        passphrase: str,
        description: Optional[str] = None,
    ) -> KeyMetadata:
        """
        Rotate the active key by generating a new one.

        The old key is marked as rotated but remains valid for verification.

        Args:
            passphrase: Passphrase for key encryption
            description: Optional description for the new key

        Returns:
            Metadata for the new active key
        """
        return self.generate_key(
            passphrase=passphrase,
            description=description or "Rotated key",
            set_active=True,
        )

    def revoke_key(self, key_id: str, passphrase: str) -> None:
        """
        Revoke a key.

        Revoked keys cannot be used for signing or verification.

        Args:
            key_id: ID of the key to revoke
            passphrase: Passphrase for keystore
        """
        if key_id not in self._keys:
            raise KeyError(f"Key not found: {key_id}")

        entry = self._keys[key_id]
        entry.metadata.status = KeyStatus.REVOKED
        entry.metadata.revoked_at = datetime.now(timezone.utc).isoformat()

        # If revoking the active key, clear it
        if self._active_key_id == key_id:
            self._active_key_id = None

        self._save(passphrase)
        logger.info(f"Revoked key: {key_id}")

    def get_active_key_id(self) -> Optional[str]:
        """Get the ID of the active signing key."""
        return self._active_key_id

    def get_key_metadata(self, key_id: str) -> Optional[KeyMetadata]:
        """Get metadata for a key."""
        entry = self._keys.get(key_id)
        return entry.metadata if entry else None

    def list_keys(self) -> List[KeyMetadata]:
        """List all keys in the keystore."""
        return [entry.metadata for entry in self._keys.values()]

    def get_public_key(self, key_id: str) -> Optional[str]:
        """Get the base64-encoded public key."""
        entry = self._keys.get(key_id)
        return entry.public_key if entry else None

    def _get_private_key(self, key_id: str, passphrase: str) -> Ed25519PrivateKey:
        """Get decrypted private key."""
        entry = self._keys.get(key_id)
        if not entry:
            raise KeyError(f"Key not found: {key_id}")

        if entry.metadata.status == KeyStatus.REVOKED:
            raise ValueError(f"Key has been revoked: {key_id}")

        return self._decrypt_private_key(
            entry.private_key_encrypted,
            entry.salt,
            passphrase,
        )

    def export_public_key(self, key_id: str, path: Path) -> None:
        """Export a public key to a file."""
        entry = self._keys.get(key_id)
        if not entry:
            raise KeyError(f"Key not found: {key_id}")

        data = {
            "key_id": key_id,
            "fingerprint": entry.metadata.fingerprint,
            "algorithm": entry.metadata.algorithm.value,
            "public_key": entry.public_key,
            "created_at": entry.metadata.created_at,
        }

        path.write_text(json.dumps(data, indent=2), encoding="utf-8")
        logger.info(f"Exported public key to {path}")


class PackageSigner:
    """
    Signs and verifies plugin packages.

    Uses Ed25519 signatures for fast, secure signing with
    compact signature sizes suitable for embedding in packages.
    """

    def __init__(self, keystore: Keystore):
        """
        Initialize package signer.

        Args:
            keystore: Keystore containing signing keys
        """
        if not HAS_CRYPTOGRAPHY:
            raise ImportError(
                "cryptography library required for signing. "
                "Install with: pip install cryptography"
            )

        self.keystore = keystore

    def _compute_package_digest(self, package_path: Path) -> Dict[str, str]:
        """Compute cryptographic digests for a package."""
        content = package_path.read_bytes()
        return {
            "sha256": hashlib.sha256(content).hexdigest(),
            "sha512": hashlib.sha512(content).hexdigest(),
        }

    def sign(
        self,
        package_path: Path,
        passphrase: str,
        key_id: Optional[str] = None,
    ) -> Signature:
        """
        Sign a package.

        Args:
            package_path: Path to the package file
            passphrase: Passphrase to decrypt the signing key
            key_id: Key ID to use (defaults to active key)

        Returns:
            Signature object
        """
        if not package_path.exists():
            raise FileNotFoundError(f"Package not found: {package_path}")

        # Use active key if not specified
        if key_id is None:
            key_id = self.keystore.get_active_key_id()
            if not key_id:
                raise ValueError("No active signing key. Generate one first.")

        # Get private key
        private_key = self.keystore._get_private_key(key_id, passphrase)

        # Compute package digest
        package_digest = self._compute_package_digest(package_path)

        # Create signature payload
        payload = json.dumps(
            {
                "digest": package_digest,
                "key_id": key_id,
            },
            sort_keys=True,
        ).encode("utf-8")

        # Sign the payload
        signature_bytes = private_key.sign(payload)
        signature_b64 = base64.b64encode(signature_bytes).decode("ascii")

        signature = Signature(
            key_id=key_id,
            signature=signature_b64,
            package_digest=package_digest,
        )

        logger.info(f"Signed package {package_path.name} with key {key_id}")
        return signature

    def verify(
        self,
        package_path: Path,
        signature: Signature,
        public_key_b64: Optional[str] = None,
    ) -> bool:
        """
        Verify a package signature.

        Args:
            package_path: Path to the package file
            signature: Signature to verify
            public_key_b64: Base64-encoded public key (uses keystore if not provided)

        Returns:
            True if signature is valid, False otherwise
        """
        if not package_path.exists():
            logger.error(f"Package not found: {package_path}")
            return False

        # Get public key
        if public_key_b64 is None:
            public_key_b64 = self.keystore.get_public_key(signature.key_id)
            if not public_key_b64:
                logger.error(f"Public key not found for key ID: {signature.key_id}")
                return False

            # Check if key is revoked
            metadata = self.keystore.get_key_metadata(signature.key_id)
            if metadata and metadata.status == KeyStatus.REVOKED:
                logger.error(f"Key has been revoked: {signature.key_id}")
                return False

        # Decode public key
        try:
            public_bytes = base64.b64decode(public_key_b64)
            public_key = Ed25519PublicKey.from_public_bytes(public_bytes)
        except Exception as e:
            logger.error(f"Invalid public key: {e}")
            return False

        # Compute current package digest
        current_digest = self._compute_package_digest(package_path)

        # Verify digest matches
        if current_digest.get("sha256") != signature.package_digest.get("sha256"):
            logger.error("Package digest mismatch")
            return False

        # Reconstruct payload
        payload = json.dumps(
            {
                "digest": signature.package_digest,
                "key_id": signature.key_id,
            },
            sort_keys=True,
        ).encode("utf-8")

        # Verify signature
        try:
            signature_bytes = base64.b64decode(signature.signature)
            public_key.verify(signature_bytes, payload)
            logger.info(f"Signature verified for {package_path.name}")
            return True
        except Exception as e:
            logger.error(f"Signature verification failed: {e}")
            return False


# =============================================================================
# Convenience Functions
# =============================================================================


def create_keystore(keystore_path: Path, passphrase: str) -> Keystore:
    """
    Create a new keystore.

    Args:
        keystore_path: Path for the keystore file
        passphrase: Passphrase for key encryption

    Returns:
        New Keystore instance
    """
    keystore = Keystore(keystore_path)
    keystore.create(passphrase)
    return keystore


def load_keystore(keystore_path: Path, passphrase: str) -> Keystore:
    """
    Load an existing keystore.

    Args:
        keystore_path: Path to the keystore file
        passphrase: Passphrase to decrypt keys

    Returns:
        Loaded Keystore instance
    """
    keystore = Keystore(keystore_path)
    keystore.load(passphrase)
    return keystore


def sign_package(
    package_path: Path,
    keystore_path: Path,
    passphrase: str,
    key_id: Optional[str] = None,
) -> Signature:
    """
    Sign a package using a keystore.

    Args:
        package_path: Path to the package
        keystore_path: Path to the keystore
        passphrase: Passphrase for key access
        key_id: Specific key to use (default: active key)

    Returns:
        Generated signature
    """
    keystore = load_keystore(keystore_path, passphrase)
    signer = PackageSigner(keystore)
    return signer.sign(package_path, passphrase, key_id)


def verify_package(
    package_path: Path,
    signature_path: Path,
    keystore_path: Optional[Path] = None,
    public_key_path: Optional[Path] = None,
    passphrase: Optional[str] = None,
) -> bool:
    """
    Verify a package signature.

    Either keystore_path or public_key_path must be provided.

    Args:
        package_path: Path to the package
        signature_path: Path to the signature file
        keystore_path: Path to keystore (requires passphrase)
        public_key_path: Path to exported public key
        passphrase: Passphrase for keystore (if used)

    Returns:
        True if signature is valid
    """
    signature = Signature.load(signature_path)

    if public_key_path:
        # Use standalone public key
        data = json.loads(public_key_path.read_text(encoding="utf-8"))
        public_key_b64 = data.get("public_key")

        # Create a minimal keystore for verification
        if not HAS_CRYPTOGRAPHY:
            raise ImportError("cryptography library required")

        # Decode and verify directly
        try:
            public_bytes = base64.b64decode(public_key_b64)
            public_key = Ed25519PublicKey.from_public_bytes(public_bytes)
        except Exception as e:
            logger.error(f"Invalid public key: {e}")
            return False

        # Compute current digest
        content = package_path.read_bytes()
        current_digest = {
            "sha256": hashlib.sha256(content).hexdigest(),
            "sha512": hashlib.sha512(content).hexdigest(),
        }

        if current_digest.get("sha256") != signature.package_digest.get("sha256"):
            logger.error("Package digest mismatch")
            return False

        payload = json.dumps(
            {
                "digest": signature.package_digest,
                "key_id": signature.key_id,
            },
            sort_keys=True,
        ).encode("utf-8")

        try:
            signature_bytes = base64.b64decode(signature.signature)
            public_key.verify(signature_bytes, payload)
            return True
        except Exception:
            return False

    elif keystore_path and passphrase:
        keystore = load_keystore(keystore_path, passphrase)
        signer = PackageSigner(keystore)
        return signer.verify(package_path, signature)

    else:
        raise ValueError("Either keystore_path or public_key_path must be provided")


def check_signing_available() -> bool:
    """Check if signing functionality is available."""
    return HAS_CRYPTOGRAPHY


# =============================================================================
# P4-1 / P5-2: Integration Helpers
# =============================================================================


@dataclass
class VerificationResult:
    """Result of package signature verification."""

    valid: bool
    key_id: str = ""
    algorithm: str = ""
    message: str = ""
    signed_at: str = ""
    fingerprint: str = ""

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        return {
            "valid": self.valid,
            "key_id": self.key_id,
            "algorithm": self.algorithm,
            "message": self.message,
            "signed_at": self.signed_at,
            "fingerprint": self.fingerprint,
        }


def find_signature_file(package_path: Path) -> Optional[Path]:
    """
    Find the signature file for a package.

    Searches for signature file in standard locations:
    - package_path.sig
    - package_path.signature
    - package_path.parent / "signature.json"
    - package_path.parent / f"{package_path.stem}.sig"

    Args:
        package_path: Path to the package file or directory

    Returns:
        Path to signature file if found, None otherwise
    """
    if package_path.is_dir():
        # For directories, look for signature.json inside
        sig_file = package_path / "signature.json"
        if sig_file.exists():
            return sig_file

        # Also check manifest.sig
        manifest_sig = package_path / "manifest.sig"
        if manifest_sig.exists():
            return manifest_sig

        return None

    # For files, check standard extensions
    candidates = [
        package_path.with_suffix(".sig"),
        package_path.with_suffix(".signature"),
        package_path.with_suffix(package_path.suffix + ".sig"),
        package_path.parent / "signature.json",
        package_path.parent / f"{package_path.stem}.sig",
    ]

    for candidate in candidates:
        if candidate.exists():
            return candidate

    return None


def find_public_key_file(package_path: Path) -> Optional[Path]:
    """
    Find a public key file for package verification.

    Searches for public key in standard locations:
    - package_path.parent / "public_key.json"
    - package_path.parent / "signing_key.pub"
    - ~/.voicestudio/signing/public_keys/ directory

    Args:
        package_path: Path to the package file or directory

    Returns:
        Path to public key file if found, None otherwise
    """
    search_dir = package_path if package_path.is_dir() else package_path.parent

    # Check local public key files
    local_candidates = [
        search_dir / "public_key.json",
        search_dir / "signing_key.pub",
        search_dir / "publisher_key.json",
    ]

    for candidate in local_candidates:
        if candidate.exists():
            return candidate

    # Check global public keys directory
    global_keys_dir = Path.home() / ".voicestudio" / "signing" / "public_keys"
    if global_keys_dir.exists():
        # Try to find a matching key based on signature key_id
        sig_file = find_signature_file(package_path)
        if sig_file:
            try:
                sig = Signature.load(sig_file)
                key_file = global_keys_dir / f"{sig.key_id}.json"
                if key_file.exists():
                    return key_file
            except Exception as e:
                # GAP-PY-001: Signature load failed, will try fallback key
                logger.debug(f"Failed to load signature from {sig_file}: {e}")

        # Return first available key as fallback
        for key_file in global_keys_dir.glob("*.json"):
            return key_file

    return None


def get_default_keystore_path() -> Path:
    """Get the default keystore path."""
    return Path.home() / ".voicestudio" / "signing" / "keystore.json"


def verify_package_auto(
    package_path: Path,
    keystore_path: Optional[Path] = None,
    passphrase: Optional[str] = None,
) -> VerificationResult:
    """
    Verify a package signature with automatic discovery.

    Automatically finds the signature file and public key/keystore
    to verify the package. This is the recommended function for
    integration with plugin loading and certification workflows.

    Args:
        package_path: Path to the package file or directory
        keystore_path: Optional path to keystore (auto-discovered if not provided)
        passphrase: Optional passphrase for keystore (empty string for dev)

    Returns:
        VerificationResult with verification status and details
    """
    if not HAS_CRYPTOGRAPHY:
        return VerificationResult(
            valid=False,
            message="cryptography library not available",
        )

    if not package_path.exists():
        return VerificationResult(
            valid=False,
            message=f"Package not found: {package_path}",
        )

    # Find signature file
    sig_file = find_signature_file(package_path)
    if not sig_file:
        return VerificationResult(
            valid=False,
            message="No signature file found",
        )

    # Load signature
    try:
        signature = Signature.load(sig_file)
    except Exception as e:
        return VerificationResult(
            valid=False,
            message=f"Failed to load signature: {e}",
        )

    # Try verification with public key first
    public_key_file = find_public_key_file(package_path)
    if public_key_file:
        try:
            is_valid = verify_package(
                package_path=package_path,
                signature_path=sig_file,
                public_key_path=public_key_file,
            )
            if is_valid:
                return VerificationResult(
                    valid=True,
                    key_id=signature.key_id,
                    algorithm=signature.algorithm.value,
                    message="Signature verified with public key",
                    signed_at=signature.signed_at,
                )
        except Exception as e:
            logger.debug(f"Public key verification failed: {e}")

    # Try verification with keystore
    ks_path = keystore_path or get_default_keystore_path()
    if ks_path.exists():
        pw = passphrase if passphrase is not None else ""
        try:
            is_valid = verify_package(
                package_path=package_path,
                signature_path=sig_file,
                keystore_path=ks_path,
                passphrase=pw,
            )
            if is_valid:
                # Get additional metadata from keystore
                keystore = load_keystore(ks_path, pw)
                key_meta = keystore.get_key_metadata(signature.key_id)
                return VerificationResult(
                    valid=True,
                    key_id=signature.key_id,
                    algorithm=signature.algorithm.value,
                    message="Signature verified with keystore",
                    signed_at=signature.signed_at,
                    fingerprint=key_meta.fingerprint if key_meta else "",
                )
        except Exception as e:
            logger.debug(f"Keystore verification failed: {e}")

    return VerificationResult(
        valid=False,
        key_id=signature.key_id,
        algorithm=signature.algorithm.value,
        message="Signature verification failed",
        signed_at=signature.signed_at,
    )


def sign_package_auto(
    package_path: Path,
    keystore_path: Optional[Path] = None,
    passphrase: Optional[str] = None,
    key_id: Optional[str] = None,
    output_path: Optional[Path] = None,
) -> Optional[Signature]:
    """
    Sign a package with automatic keystore discovery.

    Creates or uses an existing keystore to sign the package.
    This is the recommended function for integration with
    packaging and certification workflows.

    Args:
        package_path: Path to the package file
        keystore_path: Optional path to keystore (auto-discovered if not provided)
        passphrase: Optional passphrase (empty string for dev)
        key_id: Optional specific key ID to use
        output_path: Optional output path for signature file

    Returns:
        Signature object if successful, None otherwise
    """
    if not HAS_CRYPTOGRAPHY:
        logger.error("cryptography library not available for signing")
        return None

    if not package_path.exists():
        logger.error(f"Package not found: {package_path}")
        return None

    # Get or create keystore
    ks_path = keystore_path or get_default_keystore_path()
    pw = passphrase if passphrase is not None else ""

    try:
        if ks_path.exists():
            keystore = load_keystore(ks_path, pw)
        else:
            # Create new keystore with a key
            keystore = create_keystore(ks_path, pw)
            keystore.generate_key(pw, description="Auto-generated signing key")
    except Exception as e:
        logger.error(f"Failed to access keystore: {e}")
        return None

    # Ensure we have an active key
    active_key = keystore.get_active_key_id()
    if not active_key:
        try:
            keystore.generate_key(pw, description="Auto-generated signing key")
        except Exception as e:
            logger.error(f"Failed to generate signing key: {e}")
            return None

    # Sign the package
    try:
        signer = PackageSigner(keystore)
        signature = signer.sign(package_path, pw, key_id)
    except Exception as e:
        logger.error(f"Failed to sign package: {e}")
        return None

    # Save signature
    sig_path = output_path
    if sig_path is None:
        if package_path.is_dir():
            sig_path = package_path / "signature.json"
        else:
            sig_path = package_path.with_suffix(package_path.suffix + ".sig")

    try:
        signature.save(sig_path)
        logger.info(f"Signature saved to {sig_path}")
    except Exception as e:
        logger.error(f"Failed to save signature: {e}")
        return None

    return signature
