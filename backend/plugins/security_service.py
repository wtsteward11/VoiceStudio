"""
Security Service — signing policy enforcement and signature verification.

Extracted from plugin_service.py monolith.
Integrates cryptographic signature verification into the plugin install/update flow
(Phase 5B / P4-1).
"""

from __future__ import annotations

import logging
from collections.abc import Callable
from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from backend.plugins.lifecycle_manager import LifecycleManager
    from backend.plugins.plugin_registry import PluginIndex

logger = logging.getLogger(__name__)

# Phase 5B: Signature verification imports (optional dependency)
verify_package_auto: Callable[..., Any] | None = None
VerificationResult: type | None = None
SIGNING_AVAILABLE = False

try:
    from backend.plugins.supply_chain.signer import VerificationResult as _VerificationResult
    from backend.plugins.supply_chain.signer import (
        check_signing_available,
    )
    from backend.plugins.supply_chain.signer import verify_package_auto as _verify_package_auto

    verify_package_auto = _verify_package_auto
    VerificationResult = _VerificationResult
    SIGNING_AVAILABLE = check_signing_available()
except ImportError:  # ALLOWED: bare except - optional signing
    pass


class SecurityService:
    """Signing policy enforcement and plugin signature verification.

    Responsible for:
    - Verifying cryptographic signatures on plugin packages
    - Enforcing signing policy (required vs optional signatures)
    - Gating plugin load on verification result
    """

    def __init__(
        self,
        plugin_index: PluginIndex,
        lifecycle_manager: LifecycleManager,
    ) -> None:
        self._index = plugin_index
        self._lifecycle = lifecycle_manager

    def verify_plugin_signature(
        self,
        plugin_id: str,
        require_signature: bool = False,
    ) -> dict[str, Any]:
        """Verify the cryptographic signature of a plugin.

        Args:
            plugin_id: Plugin identifier.
            require_signature: If True, unsigned plugins are rejected.

        Returns:
            Dictionary with verification result including verified, signed,
            key_id, and message fields.
        """
        if not SIGNING_AVAILABLE or verify_package_auto is None:
            return {
                "verified": False,
                "signed": False,
                "key_id": "",
                "message": "Signing functionality not available",
                "error": not require_signature,
            }

        plugin_info = self._index.get_plugin(plugin_id)
        if not plugin_info:
            return {
                "verified": False,
                "signed": False,
                "key_id": "",
                "message": f"Plugin not found: {plugin_id}",
                "error": True,
            }

        result = verify_package_auto(plugin_info.path)

        return {
            "verified": result.valid,
            "signed": bool(result.key_id),
            "key_id": result.key_id,
            "algorithm": result.algorithm,
            "signed_at": result.signed_at,
            "fingerprint": result.fingerprint,
            "message": result.message,
            "error": require_signature and not result.valid,
        }

    async def load_plugin_with_verification(
        self,
        plugin_id: str,
        require_signature: bool = False,
    ) -> dict[str, Any]:
        """Load a plugin with optional signature verification.

        Verifies the plugin's signature first, then delegates to the lifecycle
        manager for actual loading.

        Args:
            plugin_id: Plugin identifier.
            require_signature: If True, reject unsigned/invalid plugins.

        Returns:
            Dictionary with load result and verification status.
        """
        result: dict[str, Any] = {
            "loaded": False,
            "verification": None,
            "error": None,
        }

        verification = self.verify_plugin_signature(plugin_id, require_signature)
        result["verification"] = verification

        if verification.get("error"):
            result["error"] = verification["message"]
            logger.warning(f"Plugin {plugin_id} rejected: {verification['message']}")
            return result

        if verification["signed"]:
            if verification["verified"]:
                logger.info(
                    f"Plugin {plugin_id} signature verified (key: {verification['key_id']})"
                )
            else:
                logger.warning(f"Plugin {plugin_id} signature invalid: {verification['message']}")
        else:
            logger.debug(f"Plugin {plugin_id} is unsigned")

        loaded = await self._lifecycle.load_plugin(plugin_id)
        result["loaded"] = loaded

        if not loaded:
            plugin_info = self._index.get_plugin(plugin_id)
            result["error"] = plugin_info.error_message if plugin_info else "Unknown error"

        return result
