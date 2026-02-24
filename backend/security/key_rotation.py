"""
API Key Rotation System.

Task 2.1.1: Automated key rotation with grace period.
Manages API key lifecycle with automatic rotation.
"""

from __future__ import annotations

import asyncio
import contextlib
import hashlib
import logging
import secrets
from collections.abc import Awaitable, Callable
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from enum import Enum

logger = logging.getLogger(__name__)


class KeyStatus(Enum):
    """Status of an API key."""

    ACTIVE = "active"
    ROTATING = "rotating"  # Grace period - both old and new work
    EXPIRED = "expired"
    REVOKED = "revoked"


@dataclass
class APIKey:
    """An API key."""

    key_id: str
    key_hash: str  # We store hash, not the actual key
    owner: str
    name: str
    status: KeyStatus
    created_at: datetime
    expires_at: datetime | None = None
    rotated_at: datetime | None = None
    previous_key_hash: str | None = None  # For grace period
    grace_period_ends: datetime | None = None
    permissions: list[str] = field(default_factory=list)
    metadata: dict = field(default_factory=dict)

    @property
    def is_valid(self) -> bool:
        if self.status in (KeyStatus.EXPIRED, KeyStatus.REVOKED):
            return False
        return not (self.expires_at and datetime.now() > self.expires_at)

    @property
    def in_grace_period(self) -> bool:
        if not self.grace_period_ends:
            return False
        return datetime.now() < self.grace_period_ends


@dataclass
class RotationConfig:
    """Configuration for key rotation."""

    rotation_interval_days: int = 90
    grace_period_hours: int = 24
    auto_rotation: bool = True
    notify_before_days: int = 7
    key_length: int = 32


class KeyRotationService:
    """
    API key rotation service.

    Features:
    - Automatic key rotation
    - Grace period for old keys
    - Key lifecycle management
    - Rotation notifications
    - Secure key generation
    """

    def __init__(self, config: RotationConfig | None = None):
        self.config = config or RotationConfig()

        self._keys: dict[str, APIKey] = {}
        self._owner_keys: dict[str, list[str]] = {}
        self._on_rotation: Callable[[APIKey, str], Awaitable[None]] | None = None
        self._on_expiry: Callable[[APIKey], Awaitable[None]] | None = None
        self._running = False
        self._task: asyncio.Task | None = None
        self._lock = asyncio.Lock()

    def set_callbacks(
        self,
        on_rotation: Callable[[APIKey, str], Awaitable[None]] | None = None,
        on_expiry: Callable[[APIKey], Awaitable[None]] | None = None,
    ) -> None:
        """Set rotation and expiry callbacks."""
        self._on_rotation = on_rotation
        self._on_expiry = on_expiry

    async def start(self) -> None:
        """Start the rotation service."""
        self._running = True
        if self.config.auto_rotation:
            self._task = asyncio.create_task(self._rotation_loop())
        logger.info("Key rotation service started")

    async def stop(self) -> None:
        """Stop the rotation service."""
        self._running = False
        if self._task:
            self._task.cancel()
            with contextlib.suppress(asyncio.CancelledError):
                await self._task
        logger.info("Key rotation service stopped")

    async def _rotation_loop(self) -> None:
        """Background loop for automatic rotation."""
        while self._running:
            try:
                await self._check_rotations()
                await asyncio.sleep(3600)  # Check hourly
            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Rotation loop error: {e}")
                await asyncio.sleep(60)

    async def _check_rotations(self) -> None:
        """Check for keys that need rotation."""
        now = datetime.now()
        rotation_threshold = now + timedelta(days=self.config.notify_before_days)

        for key in list(self._keys.values()):
            # Check if expired
            if key.expires_at and now > key.expires_at:
                if key.status == KeyStatus.ACTIVE:
                    key.status = KeyStatus.EXPIRED
                    if self._on_expiry:
                        await self._on_expiry(key)

            # Check grace period
            if (
                key.in_grace_period
                and key.grace_period_ends is not None
                and now > key.grace_period_ends
            ):
                key.previous_key_hash = None
                key.grace_period_ends = None
                key.status = KeyStatus.ACTIVE

            # Check if rotation needed
            if key.status == KeyStatus.ACTIVE and key.expires_at:
                if key.expires_at < rotation_threshold:
                    logger.info(f"Key {key.key_id} expires soon, notifying")

    @staticmethod
    def _generate_key(length: int = 32) -> str:
        """Generate a secure API key."""
        return secrets.token_urlsafe(length)

    @staticmethod
    def _hash_key(key: str) -> str:
        """Hash an API key for storage."""
        return hashlib.sha256(key.encode()).hexdigest()

    async def create_key(
        self,
        owner: str,
        name: str,
        permissions: list[str] | None = None,
        expires_days: int | None = None,
        metadata: dict | None = None,
    ) -> tuple[APIKey, str]:
        """
        Create a new API key.

        Returns:
            Tuple of (APIKey, actual_key_string)
            The key string is only returned once!
        """
        async with self._lock:
            key_string = self._generate_key(self.config.key_length)
            key_hash = self._hash_key(key_string)
            key_id = secrets.token_urlsafe(8)

            expires_at = None
            if expires_days or self.config.rotation_interval_days:
                days = expires_days or self.config.rotation_interval_days
                expires_at = datetime.now() + timedelta(days=days)

            api_key = APIKey(
                key_id=key_id,
                key_hash=key_hash,
                owner=owner,
                name=name,
                status=KeyStatus.ACTIVE,
                created_at=datetime.now(),
                expires_at=expires_at,
                permissions=permissions or [],
                metadata=metadata or {},
            )

            self._keys[key_id] = api_key

            if owner not in self._owner_keys:
                self._owner_keys[owner] = []
            self._owner_keys[owner].append(key_id)

            logger.info(f"Created API key: {key_id} for {owner}")
            return api_key, key_string

    async def validate_key(self, key_string: str) -> APIKey | None:
        """
        Validate an API key.

        Returns:
            APIKey if valid, None otherwise
        """
        key_hash = self._hash_key(key_string)

        for api_key in self._keys.values():
            # Check current key
            if api_key.key_hash == key_hash and api_key.is_valid:
                return api_key

            # Check previous key in grace period
            if api_key.in_grace_period and api_key.previous_key_hash == key_hash:
                return api_key

        return None

    async def rotate_key(
        self,
        key_id: str,
        grace_period_hours: int | None = None,
    ) -> tuple[APIKey | None, str | None]:
        """
        Rotate an API key.

        Returns:
            Tuple of (updated APIKey, new_key_string)
        """
        async with self._lock:
            api_key = self._keys.get(key_id)
            if not api_key:
                return None, None

            # Generate new key
            new_key_string = self._generate_key(self.config.key_length)
            new_key_hash = self._hash_key(new_key_string)

            # Set up grace period
            grace_hours = grace_period_hours or self.config.grace_period_hours

            api_key.previous_key_hash = api_key.key_hash
            api_key.key_hash = new_key_hash
            api_key.status = KeyStatus.ROTATING
            api_key.rotated_at = datetime.now()
            api_key.grace_period_ends = datetime.now() + timedelta(hours=grace_hours)

            # Reset expiry
            if self.config.rotation_interval_days:
                api_key.expires_at = datetime.now() + timedelta(
                    days=self.config.rotation_interval_days
                )

            logger.info(f"Rotated API key: {key_id}")

            if self._on_rotation:
                await self._on_rotation(api_key, new_key_string)

            return api_key, new_key_string

    async def revoke_key(self, key_id: str) -> bool:
        """Revoke an API key."""
        async with self._lock:
            api_key = self._keys.get(key_id)
            if not api_key:
                return False

            api_key.status = KeyStatus.REVOKED
            api_key.previous_key_hash = None

            logger.info(f"Revoked API key: {key_id}")
            return True

    def get_owner_keys(self, owner: str) -> list[APIKey]:
        """Get all keys for an owner."""
        key_ids = self._owner_keys.get(owner, [])
        return [self._keys[kid] for kid in key_ids if kid in self._keys]

    def get_stats(self) -> dict:
        """Get service statistics."""
        by_status: dict[str, int] = {}
        for key in self._keys.values():
            status = key.status.value
            by_status[status] = by_status.get(status, 0) + 1

        return {
            "total_keys": len(self._keys),
            "by_status": by_status,
            "owners": len(self._owner_keys),
            "auto_rotation": self.config.auto_rotation,
            "rotation_interval_days": self.config.rotation_interval_days,
        }
