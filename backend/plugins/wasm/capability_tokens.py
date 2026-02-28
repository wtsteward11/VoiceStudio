"""
Capability Token System for Wasm Plugin Security.

Phase 6A: Implements fine-grained capability-based security for Wasm plugins.
Each capability grants access to a specific host function or resource.

Design Principles:
1. Principle of Least Privilege: Plugins only get capabilities they need
2. Explicit Grant: All capabilities must be declared in manifest
3. No Ambient Authority: Wasm plugins have no default OS access
4. Composable: Capabilities can be combined into sets

Security Model:
- Plugins declare required capabilities in manifest
- User/admin approves capabilities at install time
- Runtime enforces capabilities on every host function call
- Violations are logged and blocked

Example Manifest:
    {
        "permissions": [
            "audio_read",
            "audio_write",
            "file_read"
        ]
    }

Usage:
    from backend.plugins.wasm.capability_tokens import (
        CapabilityToken,
        CapabilitySet,
        parse_capabilities_from_manifest,
    )

    # Parse from manifest
    caps = parse_capabilities_from_manifest(["audio_read", "file_read"])

    # Check capability
    if caps.has(CapabilityToken.AUDIO_READ):
        # Allow audio read operation
        pass

    # Create from tokens
    caps = CapabilitySet.from_tokens([
        CapabilityToken.AUDIO_READ,
        CapabilityToken.NET_LOCALHOST,
    ])
"""

from __future__ import annotations

import logging
from dataclasses import dataclass, field
from enum import Enum, auto
from typing import FrozenSet, List, Optional, Set

logger = logging.getLogger(__name__)


class CapabilityToken(Enum):
    """
    Individual capability tokens for Wasm plugin permissions.

    Each token grants access to a specific host function or resource.
    Tokens are grouped by category for easier management.
    """

    # File System Capabilities
    FILE_READ = auto()
    FILE_WRITE = auto()
    FILE_DELETE = auto()
    FILE_LIST = auto()

    # Network Capabilities
    NET_LOCALHOST = auto()
    NET_INTERNET = auto()

    # Audio Capabilities
    AUDIO_READ = auto()
    AUDIO_WRITE = auto()
    AUDIO_STREAM = auto()

    # System Capabilities
    SYS_ENV_READ = auto()
    SYS_TIME = auto()
    SYS_RANDOM = auto()

    # Plugin Interop Capabilities
    PLUGIN_IPC = auto()
    PLUGIN_EVENT_EMIT = auto()
    PLUGIN_EVENT_SUBSCRIBE = auto()

    # UI Capabilities (future)
    UI_NOTIFICATION = auto()
    UI_DIALOG = auto()

    # Logging Capabilities
    LOG_INFO = auto()
    LOG_WARN = auto()
    LOG_ERROR = auto()
    LOG_DEBUG = auto()

    def __str__(self) -> str:
        return self.name.lower()

    @classmethod
    def from_string(cls, name: str) -> Optional[CapabilityToken]:
        """
        Parse capability token from string.

        Args:
            name: Capability name (case-insensitive)

        Returns:
            CapabilityToken or None if not found
        """
        normalized = name.upper().replace("-", "_")
        try:
            return cls[normalized]
        except KeyError:
            return None


# Category groupings for UI and documentation
CAPABILITY_CATEGORIES = {
    "filesystem": [
        CapabilityToken.FILE_READ,
        CapabilityToken.FILE_WRITE,
        CapabilityToken.FILE_DELETE,
        CapabilityToken.FILE_LIST,
    ],
    "network": [
        CapabilityToken.NET_LOCALHOST,
        CapabilityToken.NET_INTERNET,
    ],
    "audio": [
        CapabilityToken.AUDIO_READ,
        CapabilityToken.AUDIO_WRITE,
        CapabilityToken.AUDIO_STREAM,
    ],
    "system": [
        CapabilityToken.SYS_ENV_READ,
        CapabilityToken.SYS_TIME,
        CapabilityToken.SYS_RANDOM,
    ],
    "interop": [
        CapabilityToken.PLUGIN_IPC,
        CapabilityToken.PLUGIN_EVENT_EMIT,
        CapabilityToken.PLUGIN_EVENT_SUBSCRIBE,
    ],
    "ui": [
        CapabilityToken.UI_NOTIFICATION,
        CapabilityToken.UI_DIALOG,
    ],
    "logging": [
        CapabilityToken.LOG_INFO,
        CapabilityToken.LOG_WARN,
        CapabilityToken.LOG_ERROR,
        CapabilityToken.LOG_DEBUG,
    ],
}


# Risk levels for capabilities
CAPABILITY_RISK_LEVELS = {
    # Low risk - safe operations
    CapabilityToken.LOG_INFO: "low",
    CapabilityToken.LOG_WARN: "low",
    CapabilityToken.LOG_ERROR: "low",
    CapabilityToken.LOG_DEBUG: "low",
    CapabilityToken.SYS_TIME: "low",
    CapabilityToken.SYS_RANDOM: "low",
    CapabilityToken.AUDIO_READ: "low",
    # Medium risk - limited access
    CapabilityToken.FILE_READ: "medium",
    CapabilityToken.FILE_LIST: "medium",
    CapabilityToken.AUDIO_WRITE: "medium",
    CapabilityToken.AUDIO_STREAM: "medium",
    CapabilityToken.NET_LOCALHOST: "medium",
    CapabilityToken.PLUGIN_EVENT_EMIT: "medium",
    CapabilityToken.PLUGIN_EVENT_SUBSCRIBE: "medium",
    CapabilityToken.UI_NOTIFICATION: "medium",
    CapabilityToken.SYS_ENV_READ: "medium",
    # High risk - sensitive operations
    CapabilityToken.FILE_WRITE: "high",
    CapabilityToken.FILE_DELETE: "high",
    CapabilityToken.NET_INTERNET: "high",
    CapabilityToken.PLUGIN_IPC: "high",
    CapabilityToken.UI_DIALOG: "high",
}


@dataclass(frozen=True)
class CapabilitySet:
    """
    Immutable set of capability tokens.

    Represents the complete set of capabilities granted to a plugin.
    Immutability ensures capabilities cannot be modified after grant.

    Attributes:
        _tokens: Frozen set of granted capability tokens
    """

    _tokens: FrozenSet[CapabilityToken] = field(default_factory=frozenset)

    def has(self, token: CapabilityToken) -> bool:
        """
        Check if capability is granted.

        Args:
            token: Capability to check

        Returns:
            True if capability is granted
        """
        return token in self._tokens

    def has_any(self, *tokens: CapabilityToken) -> bool:
        """
        Check if any of the capabilities are granted.

        Args:
            tokens: Capabilities to check

        Returns:
            True if at least one capability is granted
        """
        return any(t in self._tokens for t in tokens)

    def has_all(self, *tokens: CapabilityToken) -> bool:
        """
        Check if all capabilities are granted.

        Args:
            tokens: Capabilities to check

        Returns:
            True if all capabilities are granted
        """
        return all(t in self._tokens for t in tokens)

    def __contains__(self, token: CapabilityToken) -> bool:
        """Support 'in' operator."""
        return self.has(token)

    def __len__(self) -> int:
        """Return number of capabilities."""
        return len(self._tokens)

    def __iter__(self):
        """Iterate over capabilities."""
        return iter(self._tokens)

    @classmethod
    def from_tokens(cls, tokens: List[CapabilityToken]) -> CapabilitySet:
        """
        Create capability set from list of tokens.

        Args:
            tokens: List of capability tokens

        Returns:
            New CapabilitySet
        """
        return cls(_tokens=frozenset(tokens))

    @classmethod
    def empty(cls) -> CapabilitySet:
        """Create empty capability set."""
        return cls(_tokens=frozenset())

    @classmethod
    def all_logging(cls) -> CapabilitySet:
        """Create capability set with all logging capabilities."""
        return cls.from_tokens(
            [
                CapabilityToken.LOG_INFO,
                CapabilityToken.LOG_WARN,
                CapabilityToken.LOG_ERROR,
                CapabilityToken.LOG_DEBUG,
            ]
        )

    def union(self, other: CapabilitySet) -> CapabilitySet:
        """
        Create new set with capabilities from both sets.

        Args:
            other: Another capability set

        Returns:
            New CapabilitySet with combined capabilities
        """
        return CapabilitySet(_tokens=self._tokens | other._tokens)

    def intersection(self, other: CapabilitySet) -> CapabilitySet:
        """
        Create new set with capabilities present in both sets.

        Args:
            other: Another capability set

        Returns:
            New CapabilitySet with common capabilities
        """
        return CapabilitySet(_tokens=self._tokens & other._tokens)

    def to_list(self) -> List[str]:
        """
        Convert to list of capability names.

        Returns:
            List of capability name strings
        """
        return sorted(str(t) for t in self._tokens)

    def get_risk_level(self) -> str:
        """
        Get highest risk level among granted capabilities.

        Returns:
            "low", "medium", or "high"
        """
        risk_order = {"low": 0, "medium": 1, "high": 2}
        max_risk = "low"

        for token in self._tokens:
            token_risk = CAPABILITY_RISK_LEVELS.get(token, "medium")
            if risk_order.get(token_risk, 1) > risk_order.get(max_risk, 0):
                max_risk = token_risk

        return max_risk

    def get_categories(self) -> Set[str]:
        """
        Get categories of granted capabilities.

        Returns:
            Set of category names
        """
        categories = set()
        for category, tokens in CAPABILITY_CATEGORIES.items():
            if any(t in self._tokens for t in tokens):
                categories.add(category)
        return categories


def parse_capabilities_from_manifest(
    permissions: List[str],
    strict: bool = False,
) -> CapabilitySet:
    """
    Parse capability tokens from manifest permissions list.

    Args:
        permissions: List of permission strings from manifest
        strict: If True, raise on unknown permissions

    Returns:
        CapabilitySet with parsed capabilities

    Raises:
        ValueError: If strict=True and unknown permission found

    Example:
        caps = parse_capabilities_from_manifest([
            "audio_read",
            "audio_write",
            "file_read",
        ])
    """
    tokens: List[CapabilityToken] = []
    unknown: List[str] = []

    for perm in permissions:
        token = CapabilityToken.from_string(perm)
        if token is not None:
            tokens.append(token)
        else:
            unknown.append(perm)

    if unknown:
        if strict:
            raise ValueError(f"Unknown capabilities: {unknown}")
        else:
            logger.warning(f"Unknown capabilities ignored: {unknown}")

    return CapabilitySet.from_tokens(tokens)


def validate_capabilities(
    requested: CapabilitySet,
    allowed: CapabilitySet,
) -> tuple[bool, List[CapabilityToken]]:
    """
    Validate that requested capabilities are allowed.

    Args:
        requested: Capabilities requested by plugin
        allowed: Capabilities allowed by policy

    Returns:
        Tuple of (is_valid, denied_capabilities)
    """
    denied = []
    for token in requested:
        if token not in allowed:
            denied.append(token)

    return len(denied) == 0, denied


def get_capability_description(token: CapabilityToken) -> str:
    """
    Get human-readable description of a capability.

    Args:
        token: Capability token

    Returns:
        Description string
    """
    descriptions = {
        CapabilityToken.FILE_READ: "Read files from the plugin sandbox",
        CapabilityToken.FILE_WRITE: "Write files to the plugin sandbox",
        CapabilityToken.FILE_DELETE: "Delete files in the plugin sandbox",
        CapabilityToken.FILE_LIST: "List files in the plugin sandbox",
        CapabilityToken.NET_LOCALHOST: "Connect to localhost services",
        CapabilityToken.NET_INTERNET: "Connect to internet services",
        CapabilityToken.AUDIO_READ: "Read audio data from the host",
        CapabilityToken.AUDIO_WRITE: "Write audio data to the host",
        CapabilityToken.AUDIO_STREAM: "Stream audio in real-time",
        CapabilityToken.SYS_ENV_READ: "Read environment variables",
        CapabilityToken.SYS_TIME: "Get current system time",
        CapabilityToken.SYS_RANDOM: "Generate random numbers",
        CapabilityToken.PLUGIN_IPC: "Communicate with other plugins",
        CapabilityToken.PLUGIN_EVENT_EMIT: "Emit events to the host",
        CapabilityToken.PLUGIN_EVENT_SUBSCRIBE: "Subscribe to host events",
        CapabilityToken.UI_NOTIFICATION: "Show notifications to user",
        CapabilityToken.UI_DIALOG: "Display dialog windows",
        CapabilityToken.LOG_INFO: "Write info-level logs",
        CapabilityToken.LOG_WARN: "Write warning-level logs",
        CapabilityToken.LOG_ERROR: "Write error-level logs",
        CapabilityToken.LOG_DEBUG: "Write debug-level logs",
    }
    return descriptions.get(token, f"Unknown capability: {token}")
