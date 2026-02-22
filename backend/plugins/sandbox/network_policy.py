"""
Plugin Network Policy Enforcer.

Phase 5A Enhancement: Enforces network egress policies for sandboxed plugins
based on manifest-declared allowed hosts.

The enforcer:
    - Validates outbound connections against allowlist
    - Provides socket wrapper for subprocess injection
    - Supports wildcard domain matching
    - Logs all connection attempts for audit
"""

from __future__ import annotations

import ipaddress
import logging
import re
import socket
import time
from dataclasses import dataclass, field
from enum import Enum
from fnmatch import fnmatch
from typing import Any, Callable, Dict, List, Optional, Set, Tuple
from urllib.parse import urlparse

logger = logging.getLogger(__name__)


class NetworkAction(str, Enum):
    """Actions that can be taken on network requests."""

    ALLOW = "allow"
    DENY = "deny"
    LOG_ONLY = "log_only"


class ConnectionType(str, Enum):
    """Types of network connections."""

    TCP = "tcp"
    UDP = "udp"
    UNIX = "unix"
    UNKNOWN = "unknown"


@dataclass
class NetworkPermissions:
    """
    Network permissions extracted from plugin manifest.

    Corresponds to manifest `security.permissions.network` section.
    """

    # Basic flags
    enabled: bool = False
    allow_localhost: bool = True
    allow_all: bool = False

    # Allowed hosts (exact match or wildcard patterns)
    allowed_hosts: List[str] = field(default_factory=list)

    # Allowed ports (empty = all ports)
    allowed_ports: List[int] = field(default_factory=list)

    # Allowed protocols
    allowed_protocols: List[str] = field(default_factory=lambda: ["https", "wss"])

    @classmethod
    def from_manifest(cls, permissions: Dict[str, Any]) -> NetworkPermissions:
        """
        Create NetworkPermissions from manifest permissions dict.

        Args:
            permissions: The `security.permissions` section from manifest

        Returns:
            NetworkPermissions instance
        """
        network = permissions.get("network", {})

        if isinstance(network, bool):
            # Simple boolean: network: true/false
            return cls(enabled=network, allow_all=network)

        if not isinstance(network, dict):
            return cls()

        return cls(
            enabled=network.get("enabled", False),
            allow_localhost=network.get("allow_localhost", True),
            allow_all=network.get("allow_all", False),
            allowed_hosts=network.get("allowed_hosts", []),
            allowed_ports=network.get("allowed_ports", []),
            allowed_protocols=network.get("allowed_protocols", ["https", "wss"]),
        )

    def allows_host(self, host: str) -> bool:
        """Check if a host is allowed."""
        if not self.enabled:
            return False

        if self.allow_all:
            return True

        # Check localhost
        if self._is_localhost(host):
            return self.allow_localhost

        # Check against allowed hosts
        return any(self._matches_host_pattern(host, pattern) for pattern in self.allowed_hosts)

    def allows_port(self, port: int) -> bool:
        """Check if a port is allowed."""
        if not self.enabled:
            return False

        if self.allow_all or not self.allowed_ports:
            return True

        return port in self.allowed_ports

    def allows_protocol(self, protocol: str) -> bool:
        """Check if a protocol is allowed."""
        if not self.enabled:
            return False

        if self.allow_all:
            return True

        return protocol.lower() in [p.lower() for p in self.allowed_protocols]

    def _is_localhost(self, host: str) -> bool:
        """Check if host is localhost."""
        localhost_names = {"localhost", "127.0.0.1", "::1", "0.0.0.0"}
        return host.lower() in localhost_names

    def _matches_host_pattern(self, host: str, pattern: str) -> bool:
        """Check if host matches a pattern (supports wildcards)."""
        host = host.lower()
        pattern = pattern.lower()

        # Exact match
        if host == pattern:
            return True

        # Wildcard subdomain match (*.example.com)
        if pattern.startswith("*."):
            domain = pattern[2:]
            return host.endswith(f".{domain}") or host == domain

        # fnmatch pattern
        return fnmatch(host, pattern)


@dataclass
class ConnectionAttempt:
    """Record of a connection attempt."""

    timestamp: float
    plugin_id: str
    host: str
    port: int
    connection_type: ConnectionType
    action: NetworkAction
    reason: str

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        return {
            "timestamp": self.timestamp,
            "plugin_id": self.plugin_id,
            "host": self.host,
            "port": self.port,
            "connection_type": self.connection_type.value,
            "action": self.action.value,
            "reason": self.reason,
        }


class NetworkPolicyEnforcer:
    """
    Enforces network egress policies for plugins.

    Validates connection attempts against plugin permissions and maintains
    an audit log of all attempts.
    """

    def __init__(
        self,
        plugin_id: str,
        permissions: NetworkPermissions,
        mode: NetworkAction = NetworkAction.DENY,
    ):
        """
        Initialize the enforcer.

        Args:
            plugin_id: The plugin being monitored
            permissions: Network permissions from manifest
            mode: Default action for unmatched connections (DENY or LOG_ONLY)
        """
        self.plugin_id = plugin_id
        self.permissions = permissions
        self.default_action = mode

        self._attempts: List[ConnectionAttempt] = []
        self._denied_count = 0
        self._allowed_count = 0

    @property
    def attempts(self) -> List[ConnectionAttempt]:
        """Get all connection attempts."""
        return self._attempts.copy()

    @property
    def denied_count(self) -> int:
        """Get count of denied connections."""
        return self._denied_count

    @property
    def allowed_count(self) -> int:
        """Get count of allowed connections."""
        return self._allowed_count

    def check_connection(
        self,
        host: str,
        port: int,
        connection_type: ConnectionType = ConnectionType.TCP,
    ) -> Tuple[bool, str]:
        """
        Check if a connection should be allowed.

        Args:
            host: Target host
            port: Target port
            connection_type: Type of connection

        Returns:
            Tuple of (allowed: bool, reason: str)
        """
        now = time.time()

        # Check if networking is enabled at all
        if not self.permissions.enabled:
            reason = "network access not enabled"
            self._record_attempt(now, host, port, connection_type, NetworkAction.DENY, reason)
            return False, reason

        # Check host
        if not self.permissions.allows_host(host):
            reason = f"host '{host}' not in allowed list"
            self._record_attempt(now, host, port, connection_type, NetworkAction.DENY, reason)
            return False, reason

        # Check port
        if not self.permissions.allows_port(port):
            reason = f"port {port} not in allowed list"
            self._record_attempt(now, host, port, connection_type, NetworkAction.DENY, reason)
            return False, reason

        # Connection allowed
        reason = "connection allowed by policy"
        self._record_attempt(now, host, port, connection_type, NetworkAction.ALLOW, reason)
        return True, reason

    def check_url(self, url: str) -> Tuple[bool, str]:
        """
        Check if a URL connection should be allowed.

        Args:
            url: Full URL to check

        Returns:
            Tuple of (allowed: bool, reason: str)
        """
        try:
            parsed = urlparse(url)
        except Exception:
            return False, "invalid URL format"

        # Check protocol
        if not self.permissions.allows_protocol(parsed.scheme):
            return False, f"protocol '{parsed.scheme}' not allowed"

        # Extract host and port
        host = parsed.hostname or ""
        port = parsed.port

        if port is None:
            # Default ports
            port = 443 if parsed.scheme in ("https", "wss") else 80

        return self.check_connection(host, port, ConnectionType.TCP)

    def _record_attempt(
        self,
        timestamp: float,
        host: str,
        port: int,
        connection_type: ConnectionType,
        action: NetworkAction,
        reason: str,
    ) -> None:
        """Record a connection attempt."""
        attempt = ConnectionAttempt(
            timestamp=timestamp,
            plugin_id=self.plugin_id,
            host=host,
            port=port,
            connection_type=connection_type,
            action=action,
            reason=reason,
        )

        self._attempts.append(attempt)

        if action == NetworkAction.ALLOW:
            self._allowed_count += 1
            logger.debug(f"Network ALLOW: {self.plugin_id} -> {host}:{port} ({reason})")
        else:
            self._denied_count += 1
            logger.warning(f"Network DENY: {self.plugin_id} -> {host}:{port} ({reason})")


class SocketWrapper:
    """
    Wrapper around socket.socket that enforces network policy.

    This can be injected into a subprocess to intercept all socket
    connections and validate them against the policy.
    """

    _original_socket = socket.socket
    _enforcer: Optional[NetworkPolicyEnforcer] = None
    _installed = False

    @classmethod
    def install(cls, enforcer: NetworkPolicyEnforcer) -> None:
        """
        Install the socket wrapper globally.

        Args:
            enforcer: The network policy enforcer to use
        """
        if cls._installed:
            logger.warning("Socket wrapper already installed")
            return

        cls._enforcer = enforcer
        cls._installed = True

        # Replace socket.socket
        socket.socket = cls._create_wrapped_socket()  # type: ignore

        logger.info(f"Socket wrapper installed for {enforcer.plugin_id}")

    @classmethod
    def uninstall(cls) -> None:
        """Remove the socket wrapper and restore original."""
        if not cls._installed:
            return

        socket.socket = cls._original_socket
        cls._enforcer = None
        cls._installed = False

        logger.info("Socket wrapper uninstalled")

    @classmethod
    def _create_wrapped_socket(cls):
        """Create the wrapped socket class."""
        original = cls._original_socket
        enforcer = cls._enforcer

        class PolicySocket(original):
            """Socket subclass that enforces network policy."""

            def connect(self, address):
                """Intercept connect calls to enforce policy."""
                if enforcer is not None:
                    host, port = cls._extract_address(address)

                    allowed, reason = enforcer.check_connection(host, port, ConnectionType.TCP)

                    if not allowed:
                        raise ConnectionRefusedError(f"Network policy violation: {reason}")

                return super().connect(address)

            def connect_ex(self, address):
                """Intercept connect_ex calls to enforce policy."""
                if enforcer is not None:
                    host, port = cls._extract_address(address)

                    allowed, reason = enforcer.check_connection(host, port, ConnectionType.TCP)

                    if not allowed:
                        # Return error code for ECONNREFUSED
                        return 111  # ECONNREFUSED on Linux

                return super().connect_ex(address)

        return PolicySocket

    @classmethod
    def _extract_address(cls, address) -> Tuple[str, int]:
        """Extract host and port from address tuple."""
        if isinstance(address, tuple) and len(address) >= 2:
            return str(address[0]), int(address[1])
        return str(address), 0


def generate_socket_bootstrap(
    plugin_id: str,
    permissions: Dict[str, Any],
) -> str:
    """
    Generate Python code to bootstrap network policy in a subprocess.

    This code can be injected into a plugin subprocess to enforce
    network policy before any plugin code runs.

    Args:
        plugin_id: The plugin identifier
        permissions: The permissions dict from manifest

    Returns:
        Python code string to execute in subprocess
    """
    import json

    permissions_json = json.dumps(permissions)

    return f"""
# VoiceStudio Network Policy Bootstrap
# This code enforces network egress policy for plugin: {plugin_id}

import socket as _original_socket_module
import json

_PLUGIN_ID = "{plugin_id}"
_PERMISSIONS = json.loads('{permissions_json}')

class _NetworkPermissions:
    def __init__(self, perms):
        network = perms.get("network", {{}})
        if isinstance(network, bool):
            self.enabled = network
            self.allow_all = network
            self.allow_localhost = True
            self.allowed_hosts = []
            self.allowed_ports = []
        else:
            self.enabled = network.get("enabled", False)
            self.allow_all = network.get("allow_all", False)
            self.allow_localhost = network.get("allow_localhost", True)
            self.allowed_hosts = network.get("allowed_hosts", [])
            self.allowed_ports = network.get("allowed_ports", [])
    
    def allows_host(self, host):
        if not self.enabled:
            return False
        if self.allow_all:
            return True
        if host.lower() in ("localhost", "127.0.0.1", "::1"):
            return self.allow_localhost
        for pattern in self.allowed_hosts:
            if host.lower() == pattern.lower():
                return True
            if pattern.startswith("*."):
                domain = pattern[2:]
                if host.endswith("." + domain) or host == domain:
                    return True
        return False
    
    def allows_port(self, port):
        if not self.enabled:
            return False
        if self.allow_all or not self.allowed_ports:
            return True
        return port in self.allowed_ports

_perms = _NetworkPermissions(_PERMISSIONS)
_original_socket = _original_socket_module.socket

class _PolicySocket(_original_socket):
    def connect(self, address):
        if isinstance(address, tuple) and len(address) >= 2:
            host, port = str(address[0]), int(address[1])
            if not _perms.allows_host(host):
                raise ConnectionRefusedError(f"Network policy: host '{{host}}' not allowed")
            if not _perms.allows_port(port):
                raise ConnectionRefusedError(f"Network policy: port {{port}} not allowed")
        return super().connect(address)
    
    def connect_ex(self, address):
        if isinstance(address, tuple) and len(address) >= 2:
            host, port = str(address[0]), int(address[1])
            if not _perms.allows_host(host) or not _perms.allows_port(port):
                return 111  # ECONNREFUSED
        return super().connect_ex(address)

_original_socket_module.socket = _PolicySocket
# End network policy bootstrap
"""


# Global registry of enforcers by plugin ID
_enforcers: Dict[str, NetworkPolicyEnforcer] = {}


def get_enforcer(plugin_id: str) -> Optional[NetworkPolicyEnforcer]:
    """Get the network policy enforcer for a plugin."""
    return _enforcers.get(plugin_id)


def create_enforcer(
    plugin_id: str,
    permissions: Dict[str, Any],
) -> NetworkPolicyEnforcer:
    """
    Create and register a network policy enforcer for a plugin.

    Args:
        plugin_id: The plugin identifier
        permissions: The permissions dict from manifest

    Returns:
        The created enforcer
    """
    network_perms = NetworkPermissions.from_manifest(permissions)
    enforcer = NetworkPolicyEnforcer(plugin_id, network_perms)
    _enforcers[plugin_id] = enforcer
    return enforcer


def remove_enforcer(plugin_id: str) -> None:
    """Remove the enforcer for a plugin."""
    _enforcers.pop(plugin_id, None)
