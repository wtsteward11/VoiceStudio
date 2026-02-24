"""
Allowed API Surface for Plugin-to-Host Communication.

Task 3.3: Explicit API whitelist for plugin hardening. Defines the canonical
set of methods plugins may invoke on the host. All other methods are rejected
with METHOD_NOT_FOUND. Used for validation, documentation, and enforcement.
"""

from __future__ import annotations

from typing import FrozenSet

# Canonical whitelist of plugin-to-host RPC methods.
# Must stay in sync with backend.plugins.sandbox.protocol.HostMethods
# and backend.plugins.sandbox.host_api.HostAPIHandler implementations.
ALLOWED_HOST_METHODS: FrozenSet[str] = frozenset(
    {
        # Audio (plugin -> host)
        "host.audio.play",
        "host.audio.stop",
        "host.audio.getDevices",
        "host.audio.process",
        # UI (plugin -> host)
        "host.ui.notify",
        "host.ui.showDialog",
        "host.ui.updatePanel",
        # Storage (plugin -> host)
        "host.storage.get",
        "host.storage.set",
        "host.storage.delete",
        # Settings (plugin -> host)
        "host.settings.get",
        "host.settings.set",
        # Engine (plugin -> host)
        "host.engine.invoke",
        "host.engine.list",
    }
)

# Notification methods (no response expected) - plugins may send these
ALLOWED_NOTIFICATION_METHODS: FrozenSet[str] = frozenset(
    {
        "notify.log",
        "notify.progress",
        "notify.heartbeat",
    }
)

# Combined set for "is this a valid outgoing method from plugin?"
ALLOWED_PLUGIN_OUTGOING: FrozenSet[str] = ALLOWED_HOST_METHODS | ALLOWED_NOTIFICATION_METHODS


def is_allowed_host_method(method: str) -> bool:
    """Check if a method is in the allowed host API whitelist."""
    return method in ALLOWED_HOST_METHODS


def is_allowed_notification(method: str) -> bool:
    """Check if a method is in the allowed notification whitelist."""
    return method in ALLOWED_NOTIFICATION_METHODS


def is_allowed_plugin_outgoing(method: str) -> bool:
    """Check if a method may be sent from plugin to host (request or notification)."""
    return method in ALLOWED_PLUGIN_OUTGOING


def validate_host_method(method: str) -> None:
    """
    Validate that a method is allowed. Raises ValueError if not.

    Use before sending a request from plugin to host.
    """
    if not is_allowed_host_method(method):
        raise ValueError(
            f"Method '{method}' is not in the allowed host API whitelist. "
            f"Allowed: {sorted(ALLOWED_HOST_METHODS)}"
        )


def validate_notification(method: str) -> None:
    """
    Validate that a notification method is allowed. Raises ValueError if not.
    """
    if not is_allowed_notification(method):
        raise ValueError(
            f"Notification '{method}' is not in the allowed whitelist. "
            f"Allowed: {sorted(ALLOWED_NOTIFICATION_METHODS)}"
        )
