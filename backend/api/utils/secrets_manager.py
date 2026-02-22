"""
Secrets Manager
Centralizes secrets management using environment variables and secure storage.
"""

from __future__ import annotations

import logging
import os
from typing import Any

logger = logging.getLogger(__name__)

# Try to import secure storage libraries
try:
    import keyring

    HAS_KEYRING = True
except ImportError:
    HAS_KEYRING = False
    keyring = None

Fernet: Any = None
HAS_CRYPTOGRAPHY = False
try:
    from cryptography.fernet import Fernet as _Fernet

    Fernet = _Fernet
    HAS_CRYPTOGRAPHY = True
except ImportError:  # ALLOWED: bare except - optional cryptography
    pass


class SecretsManager:
    """Manages secrets using environment variables and secure storage."""

    def __init__(self, service_name: str = "voicestudio"):
        """
        Initialize secrets manager.

        Args:
            service_name: Service name for keyring storage
        """
        self.service_name = service_name
        self._cache: dict[str, str | None] = {}

    def get_secret(
        self,
        key: str,
        default: str | None = None,
        use_keyring: bool = True,
        use_env: bool = True,
    ) -> str | None:
        """
        Get secret value.

        Priority:
        1. Environment variable
        2. Keyring (if available)
        3. Default value

        Args:
            key: Secret key
            default: Default value if not found
            use_keyring: Whether to use keyring
            use_env: Whether to use environment variables

        Returns:
            Secret value or default
        """
        # Check cache first
        if key in self._cache:
            return self._cache[key]

        value = None

        # Try environment variable first
        if use_env:
            env_key = key.upper().replace("-", "_")
            value = os.getenv(env_key) or os.getenv(key)

        # Try keyring if not found
        if not value and use_keyring and HAS_KEYRING:
            try:
                value = keyring.get_password(self.service_name, key)
            except Exception as e:
                logger.debug(f"Failed to get secret from keyring: {e}")

        # Use default if not found
        if value is None:
            value = default

        # Cache the value
        self._cache[key] = value

        return value

    def set_secret(self, key: str, value: str, use_keyring: bool = True) -> bool:
        """
        Set secret value.

        Args:
            key: Secret key
            value: Secret value
            use_keyring: Whether to use keyring

        Returns:
            True if successful
        """
        # Store in keyring if available
        if use_keyring and HAS_KEYRING:
            try:
                keyring.set_password(self.service_name, key, value)
                self._cache[key] = value
                return True
            except Exception as e:
                logger.warning(f"Failed to set secret in keyring: {e}")
                return False

        return False

    def delete_secret(self, key: str, use_keyring: bool = True) -> bool:
        """
        Delete secret value.

        Args:
            key: Secret key
            use_keyring: Whether to use keyring

        Returns:
            True if successful
        """
        # Remove from cache
        if key in self._cache:
            del self._cache[key]

        # Remove from keyring if available
        if use_keyring and HAS_KEYRING:
            try:
                keyring.delete_password(self.service_name, key)
                return True
            except Exception as e:
                logger.debug(f"Failed to delete secret from keyring: {e}")
                return False

        return False

    def list_secrets(self, use_keyring: bool = True) -> list:
        """
        List all secret keys (keyring only, env vars not enumerable).

        Args:
            use_keyring: Whether to use keyring

        Returns:
            List of secret keys
        """
        if use_keyring and HAS_KEYRING:
            try:
                # Note: keyring doesn't support listing, so we can't enumerate
                # This would require maintaining a separate index
                return []
            except Exception:
                return []
        return []


# Global secrets manager instance
_secrets_manager = None


def get_secrets_manager() -> SecretsManager:
    """Get global secrets manager instance."""
    global _secrets_manager
    if _secrets_manager is None:
        _secrets_manager = SecretsManager()
    return _secrets_manager


def get_secret(key: str, default: str | None = None) -> str | None:
    """
    Get secret value (convenience function).

    Args:
        key: Secret key
        default: Default value if not found

    Returns:
        Secret value or default
    """
    return get_secrets_manager().get_secret(key, default=default)
