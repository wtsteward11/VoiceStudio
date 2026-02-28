"""
Plugin Audit Logger.

Phase 4 Enhancement: Plugin-specific audit categories and logging
for comprehensive plugin governance and compliance tracking.
"""

import logging
from dataclasses import dataclass
from datetime import datetime
from enum import Enum
from typing import Any, Dict, List, Optional

from backend.services.audit_logger import (
    AuditAction,
    AuditEntry,
    AuditLogger,
    AuditSeverity,
    get_audit_logger,
)

logger = logging.getLogger(__name__)


class PluginAuditCategory(Enum):
    """Plugin-specific audit categories."""

    # Lifecycle events
    LIFECYCLE_INSTALL = "plugin.lifecycle.install"
    LIFECYCLE_UNINSTALL = "plugin.lifecycle.uninstall"
    LIFECYCLE_UPDATE = "plugin.lifecycle.update"
    LIFECYCLE_ENABLE = "plugin.lifecycle.enable"
    LIFECYCLE_DISABLE = "plugin.lifecycle.disable"
    LIFECYCLE_ACTIVATE = "plugin.lifecycle.activate"
    LIFECYCLE_DEACTIVATE = "plugin.lifecycle.deactivate"

    # Security events
    SECURITY_SIGNATURE_VALID = "plugin.security.signature_valid"
    SECURITY_SIGNATURE_INVALID = "plugin.security.signature_invalid"
    SECURITY_PERMISSION_GRANTED = "plugin.security.permission_granted"
    SECURITY_PERMISSION_DENIED = "plugin.security.permission_denied"
    SECURITY_POLICY_VIOLATION = "plugin.security.policy_violation"
    SECURITY_SANDBOX_VIOLATION = "plugin.security.sandbox_violation"
    SECURITY_TRUST_LEVEL_CHANGE = "plugin.security.trust_level_change"

    # Policy events
    POLICY_WHITELIST_ADD = "plugin.policy.whitelist_add"
    POLICY_WHITELIST_REMOVE = "plugin.policy.whitelist_remove"
    POLICY_BLACKLIST_ADD = "plugin.policy.blacklist_add"
    POLICY_BLACKLIST_REMOVE = "plugin.policy.blacklist_remove"
    POLICY_EVALUATION = "plugin.policy.evaluation"
    POLICY_UPDATE = "plugin.policy.update"

    # Execution events
    EXECUTION_START = "plugin.execution.start"
    EXECUTION_COMPLETE = "plugin.execution.complete"
    EXECUTION_ERROR = "plugin.execution.error"
    EXECUTION_TIMEOUT = "plugin.execution.timeout"
    EXECUTION_CRASH = "plugin.execution.crash"

    # IPC events
    IPC_REQUEST = "plugin.ipc.request"
    IPC_RESPONSE = "plugin.ipc.response"
    IPC_ERROR = "plugin.ipc.error"

    # Resource events
    RESOURCE_ACCESS = "plugin.resource.access"
    RESOURCE_DENIED = "plugin.resource.denied"
    RESOURCE_QUOTA_EXCEEDED = "plugin.resource.quota_exceeded"

    # Marketplace events
    MARKETPLACE_DOWNLOAD = "plugin.marketplace.download"
    MARKETPLACE_PUBLISH = "plugin.marketplace.publish"
    MARKETPLACE_REVIEW = "plugin.marketplace.review"


@dataclass
class PluginAuditEvent:
    """A plugin-specific audit event."""

    category: PluginAuditCategory
    plugin_id: str
    timestamp: datetime
    severity: AuditSeverity
    details: Dict[str, Any]
    success: bool
    error_message: Optional[str] = None
    correlation_id: Optional[str] = None
    session_id: Optional[str] = None
    user_id: Optional[str] = None

    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary."""
        return {
            "category": self.category.value,
            "plugin_id": self.plugin_id,
            "timestamp": self.timestamp.isoformat(),
            "severity": self.severity.value,
            "details": self.details,
            "success": self.success,
            "error_message": self.error_message,
            "correlation_id": self.correlation_id,
            "session_id": self.session_id,
            "user_id": self.user_id,
        }


class PluginAuditLogger:
    """
    Plugin-specific audit logger.

    Wraps the base AuditLogger with plugin-specific categories
    and convenience methods.
    """

    def __init__(self, audit_logger: Optional[AuditLogger] = None):
        """
        Initialize the plugin audit logger.

        Args:
            audit_logger: Base audit logger instance. If None, uses global.
        """
        self._audit_logger = audit_logger or get_audit_logger()
        self._in_memory_events: List[PluginAuditEvent] = []
        self._max_in_memory = 1000

    async def log(
        self,
        category: PluginAuditCategory,
        plugin_id: str,
        details: Optional[Dict[str, Any]] = None,
        success: bool = True,
        error_message: Optional[str] = None,
        severity: AuditSeverity = AuditSeverity.INFO,
        correlation_id: Optional[str] = None,
        session_id: Optional[str] = None,
        user_id: Optional[str] = None,
    ) -> str:
        """
        Log a plugin audit event.

        Args:
            category: The audit category
            plugin_id: The plugin's identifier
            details: Additional event details
            success: Whether the event represents success
            error_message: Error message if applicable
            severity: Event severity
            correlation_id: ID for correlating related events
            session_id: User session ID
            user_id: User ID if applicable

        Returns:
            Audit entry ID
        """
        # Create in-memory event
        event = PluginAuditEvent(
            category=category,
            plugin_id=plugin_id,
            timestamp=datetime.now(),
            severity=severity,
            details=details or {},
            success=success,
            error_message=error_message,
            correlation_id=correlation_id,
            session_id=session_id,
            user_id=user_id,
        )

        # Store in memory for quick access
        self._in_memory_events.append(event)
        if len(self._in_memory_events) > self._max_in_memory:
            self._in_memory_events = self._in_memory_events[-self._max_in_memory // 2 :]

        # Map to base audit action
        base_action = self._map_category_to_action(category)

        # Log to base audit logger
        metadata = {
            "plugin_category": category.value,
            "correlation_id": correlation_id,
            **event.details,
        }

        return await self._audit_logger.log(
            action=base_action,
            entity_type="plugin",
            entity_id=plugin_id,
            user_id=user_id,
            session_id=session_id,
            metadata=metadata,
            success=success,
            error_message=error_message,
            severity=severity,
        )

    def _map_category_to_action(self, category: PluginAuditCategory) -> AuditAction:
        """Map plugin category to base audit action."""
        category_to_action = {
            # Lifecycle mappings
            PluginAuditCategory.LIFECYCLE_INSTALL: AuditAction.CREATE,
            PluginAuditCategory.LIFECYCLE_UNINSTALL: AuditAction.DELETE,
            PluginAuditCategory.LIFECYCLE_UPDATE: AuditAction.UPDATE,
            PluginAuditCategory.LIFECYCLE_ENABLE: AuditAction.UPDATE,
            PluginAuditCategory.LIFECYCLE_DISABLE: AuditAction.UPDATE,
            PluginAuditCategory.LIFECYCLE_ACTIVATE: AuditAction.EXECUTE,
            PluginAuditCategory.LIFECYCLE_DEACTIVATE: AuditAction.EXECUTE,
            # Security mappings
            PluginAuditCategory.SECURITY_SIGNATURE_VALID: AuditAction.READ,
            PluginAuditCategory.SECURITY_SIGNATURE_INVALID: AuditAction.READ,
            PluginAuditCategory.SECURITY_PERMISSION_GRANTED: AuditAction.READ,
            PluginAuditCategory.SECURITY_PERMISSION_DENIED: AuditAction.READ,
            PluginAuditCategory.SECURITY_POLICY_VIOLATION: AuditAction.EXECUTE,
            PluginAuditCategory.SECURITY_SANDBOX_VIOLATION: AuditAction.EXECUTE,
            PluginAuditCategory.SECURITY_TRUST_LEVEL_CHANGE: AuditAction.UPDATE,
            # Policy mappings
            PluginAuditCategory.POLICY_WHITELIST_ADD: AuditAction.UPDATE,
            PluginAuditCategory.POLICY_WHITELIST_REMOVE: AuditAction.UPDATE,
            PluginAuditCategory.POLICY_BLACKLIST_ADD: AuditAction.UPDATE,
            PluginAuditCategory.POLICY_BLACKLIST_REMOVE: AuditAction.UPDATE,
            PluginAuditCategory.POLICY_EVALUATION: AuditAction.READ,
            PluginAuditCategory.POLICY_UPDATE: AuditAction.CONFIG_CHANGE,
            # Execution mappings
            PluginAuditCategory.EXECUTION_START: AuditAction.EXECUTE,
            PluginAuditCategory.EXECUTION_COMPLETE: AuditAction.EXECUTE,
            PluginAuditCategory.EXECUTION_ERROR: AuditAction.EXECUTE,
            PluginAuditCategory.EXECUTION_TIMEOUT: AuditAction.EXECUTE,
            PluginAuditCategory.EXECUTION_CRASH: AuditAction.EXECUTE,
            # IPC mappings
            PluginAuditCategory.IPC_REQUEST: AuditAction.EXECUTE,
            PluginAuditCategory.IPC_RESPONSE: AuditAction.EXECUTE,
            PluginAuditCategory.IPC_ERROR: AuditAction.EXECUTE,
            # Resource mappings
            PluginAuditCategory.RESOURCE_ACCESS: AuditAction.READ,
            PluginAuditCategory.RESOURCE_DENIED: AuditAction.READ,
            PluginAuditCategory.RESOURCE_QUOTA_EXCEEDED: AuditAction.READ,
            # Marketplace mappings
            PluginAuditCategory.MARKETPLACE_DOWNLOAD: AuditAction.IMPORT,
            PluginAuditCategory.MARKETPLACE_PUBLISH: AuditAction.EXPORT,
            PluginAuditCategory.MARKETPLACE_REVIEW: AuditAction.CREATE,
        }
        return category_to_action.get(category, AuditAction.EXECUTE)

    # Lifecycle convenience methods

    async def log_install(
        self,
        plugin_id: str,
        version: str,
        source: Optional[str] = None,
        success: bool = True,
        error_message: Optional[str] = None,
        **kwargs,
    ) -> str:
        """Log plugin installation."""
        return await self.log(
            category=PluginAuditCategory.LIFECYCLE_INSTALL,
            plugin_id=plugin_id,
            details={"version": version, "source": source},
            success=success,
            error_message=error_message,
            severity=AuditSeverity.INFO if success else AuditSeverity.ERROR,
            **kwargs,
        )

    async def log_uninstall(
        self,
        plugin_id: str,
        version: str,
        reason: Optional[str] = None,
        **kwargs,
    ) -> str:
        """Log plugin uninstallation."""
        return await self.log(
            category=PluginAuditCategory.LIFECYCLE_UNINSTALL,
            plugin_id=plugin_id,
            details={"version": version, "reason": reason},
            **kwargs,
        )

    async def log_update(
        self,
        plugin_id: str,
        old_version: str,
        new_version: str,
        success: bool = True,
        **kwargs,
    ) -> str:
        """Log plugin update."""
        return await self.log(
            category=PluginAuditCategory.LIFECYCLE_UPDATE,
            plugin_id=plugin_id,
            details={"old_version": old_version, "new_version": new_version},
            success=success,
            severity=AuditSeverity.INFO if success else AuditSeverity.ERROR,
            **kwargs,
        )

    async def log_enable(self, plugin_id: str, **kwargs) -> str:
        """Log plugin enable."""
        return await self.log(
            category=PluginAuditCategory.LIFECYCLE_ENABLE,
            plugin_id=plugin_id,
            **kwargs,
        )

    async def log_disable(self, plugin_id: str, reason: Optional[str] = None, **kwargs) -> str:
        """Log plugin disable."""
        return await self.log(
            category=PluginAuditCategory.LIFECYCLE_DISABLE,
            plugin_id=plugin_id,
            details={"reason": reason},
            **kwargs,
        )

    # Security convenience methods

    async def log_signature_check(
        self,
        plugin_id: str,
        valid: bool,
        signer: Optional[str] = None,
        **kwargs,
    ) -> str:
        """Log signature verification."""
        category = (
            PluginAuditCategory.SECURITY_SIGNATURE_VALID
            if valid
            else PluginAuditCategory.SECURITY_SIGNATURE_INVALID
        )
        return await self.log(
            category=category,
            plugin_id=plugin_id,
            details={"signer": signer},
            success=valid,
            severity=AuditSeverity.INFO if valid else AuditSeverity.WARNING,
            **kwargs,
        )

    async def log_permission_check(
        self,
        plugin_id: str,
        permission: str,
        granted: bool,
        reason: Optional[str] = None,
        **kwargs,
    ) -> str:
        """Log permission check."""
        category = (
            PluginAuditCategory.SECURITY_PERMISSION_GRANTED
            if granted
            else PluginAuditCategory.SECURITY_PERMISSION_DENIED
        )
        return await self.log(
            category=category,
            plugin_id=plugin_id,
            details={"permission": permission, "reason": reason},
            success=granted,
            severity=AuditSeverity.DEBUG if granted else AuditSeverity.WARNING,
            **kwargs,
        )

    async def log_policy_violation(
        self,
        plugin_id: str,
        violation_type: str,
        details: Optional[Dict[str, Any]] = None,
        **kwargs,
    ) -> str:
        """Log policy violation."""
        return await self.log(
            category=PluginAuditCategory.SECURITY_POLICY_VIOLATION,
            plugin_id=plugin_id,
            details={"violation_type": violation_type, **(details or {})},
            success=False,
            severity=AuditSeverity.WARNING,
            **kwargs,
        )

    async def log_sandbox_violation(
        self,
        plugin_id: str,
        violation_type: str,
        attempted_action: Optional[str] = None,
        **kwargs,
    ) -> str:
        """Log sandbox violation."""
        return await self.log(
            category=PluginAuditCategory.SECURITY_SANDBOX_VIOLATION,
            plugin_id=plugin_id,
            details={
                "violation_type": violation_type,
                "attempted_action": attempted_action,
            },
            success=False,
            severity=AuditSeverity.ERROR,
            **kwargs,
        )

    # Policy convenience methods

    async def log_whitelist_change(
        self,
        plugin_id: str,
        added: bool,
        user_id: Optional[str] = None,
        **kwargs,
    ) -> str:
        """Log whitelist change."""
        category = (
            PluginAuditCategory.POLICY_WHITELIST_ADD
            if added
            else PluginAuditCategory.POLICY_WHITELIST_REMOVE
        )
        return await self.log(
            category=category,
            plugin_id=plugin_id,
            user_id=user_id,
            **kwargs,
        )

    async def log_blacklist_change(
        self,
        plugin_id: str,
        added: bool,
        reason: Optional[str] = None,
        user_id: Optional[str] = None,
        **kwargs,
    ) -> str:
        """Log blacklist change."""
        category = (
            PluginAuditCategory.POLICY_BLACKLIST_ADD
            if added
            else PluginAuditCategory.POLICY_BLACKLIST_REMOVE
        )
        return await self.log(
            category=category,
            plugin_id=plugin_id,
            details={"reason": reason},
            user_id=user_id,
            severity=AuditSeverity.WARNING if added else AuditSeverity.INFO,
            **kwargs,
        )

    async def log_policy_evaluation(
        self,
        plugin_id: str,
        allowed: bool,
        trust_level: str,
        applied_rules: List[str],
        **kwargs,
    ) -> str:
        """Log policy evaluation result."""
        return await self.log(
            category=PluginAuditCategory.POLICY_EVALUATION,
            plugin_id=plugin_id,
            details={
                "allowed": allowed,
                "trust_level": trust_level,
                "applied_rules": applied_rules,
            },
            success=allowed,
            severity=AuditSeverity.DEBUG,
            **kwargs,
        )

    # Execution convenience methods

    async def log_execution_start(
        self,
        plugin_id: str,
        method: str,
        correlation_id: Optional[str] = None,
        **kwargs,
    ) -> str:
        """Log plugin execution start."""
        return await self.log(
            category=PluginAuditCategory.EXECUTION_START,
            plugin_id=plugin_id,
            details={"method": method},
            correlation_id=correlation_id,
            **kwargs,
        )

    async def log_execution_complete(
        self,
        plugin_id: str,
        method: str,
        duration_ms: float,
        correlation_id: Optional[str] = None,
        **kwargs,
    ) -> str:
        """Log plugin execution completion."""
        return await self.log(
            category=PluginAuditCategory.EXECUTION_COMPLETE,
            plugin_id=plugin_id,
            details={"method": method, "duration_ms": duration_ms},
            correlation_id=correlation_id,
            **kwargs,
        )

    async def log_execution_error(
        self,
        plugin_id: str,
        method: str,
        error: str,
        stack_trace: Optional[str] = None,
        correlation_id: Optional[str] = None,
        **kwargs,
    ) -> str:
        """Log plugin execution error."""
        return await self.log(
            category=PluginAuditCategory.EXECUTION_ERROR,
            plugin_id=plugin_id,
            details={"method": method, "stack_trace": stack_trace},
            success=False,
            error_message=error,
            severity=AuditSeverity.ERROR,
            correlation_id=correlation_id,
            **kwargs,
        )

    async def log_execution_crash(
        self,
        plugin_id: str,
        exit_code: Optional[int] = None,
        signal: Optional[str] = None,
        **kwargs,
    ) -> str:
        """Log plugin crash."""
        return await self.log(
            category=PluginAuditCategory.EXECUTION_CRASH,
            plugin_id=plugin_id,
            details={"exit_code": exit_code, "signal": signal},
            success=False,
            severity=AuditSeverity.CRITICAL,
            **kwargs,
        )

    # IPC convenience methods

    async def log_ipc_request(
        self,
        plugin_id: str,
        method: str,
        request_id: str,
        **kwargs,
    ) -> str:
        """Log IPC request."""
        return await self.log(
            category=PluginAuditCategory.IPC_REQUEST,
            plugin_id=plugin_id,
            details={"method": method, "request_id": request_id},
            severity=AuditSeverity.DEBUG,
            **kwargs,
        )

    async def log_ipc_error(
        self,
        plugin_id: str,
        method: str,
        error_code: int,
        error_message: str,
        **kwargs,
    ) -> str:
        """Log IPC error."""
        return await self.log(
            category=PluginAuditCategory.IPC_ERROR,
            plugin_id=plugin_id,
            details={"method": method, "error_code": error_code},
            success=False,
            error_message=error_message,
            severity=AuditSeverity.ERROR,
            **kwargs,
        )

    # Query methods

    def get_recent_events(
        self,
        plugin_id: Optional[str] = None,
        category: Optional[PluginAuditCategory] = None,
        limit: int = 100,
    ) -> List[PluginAuditEvent]:
        """
        Get recent plugin audit events from in-memory cache.

        Args:
            plugin_id: Filter by plugin ID
            category: Filter by category
            limit: Maximum events to return

        Returns:
            List of matching events
        """
        events = self._in_memory_events

        if plugin_id:
            events = [e for e in events if e.plugin_id == plugin_id]

        if category:
            events = [e for e in events if e.category == category]

        return events[-limit:]

    def get_security_events(
        self,
        plugin_id: Optional[str] = None,
        limit: int = 100,
    ) -> List[PluginAuditEvent]:
        """Get security-related events."""
        security_categories = {
            PluginAuditCategory.SECURITY_SIGNATURE_VALID,
            PluginAuditCategory.SECURITY_SIGNATURE_INVALID,
            PluginAuditCategory.SECURITY_PERMISSION_GRANTED,
            PluginAuditCategory.SECURITY_PERMISSION_DENIED,
            PluginAuditCategory.SECURITY_POLICY_VIOLATION,
            PluginAuditCategory.SECURITY_SANDBOX_VIOLATION,
            PluginAuditCategory.SECURITY_TRUST_LEVEL_CHANGE,
        }

        events = [e for e in self._in_memory_events if e.category in security_categories]

        if plugin_id:
            events = [e for e in events if e.plugin_id == plugin_id]

        return events[-limit:]

    def get_error_events(
        self,
        plugin_id: Optional[str] = None,
        limit: int = 100,
    ) -> List[PluginAuditEvent]:
        """Get error events."""
        events = [e for e in self._in_memory_events if not e.success]

        if plugin_id:
            events = [e for e in events if e.plugin_id == plugin_id]

        return events[-limit:]

    def get_stats(self, plugin_id: Optional[str] = None) -> Dict[str, Any]:
        """Get audit statistics."""
        events = self._in_memory_events
        if plugin_id:
            events = [e for e in events if e.plugin_id == plugin_id]

        category_counts: Dict[str, int] = {}
        severity_counts: Dict[str, int] = {}
        success_count = 0
        failure_count = 0

        for event in events:
            cat = event.category.value
            category_counts[cat] = category_counts.get(cat, 0) + 1

            sev = event.severity.value
            severity_counts[sev] = severity_counts.get(sev, 0) + 1

            if event.success:
                success_count += 1
            else:
                failure_count += 1

        return {
            "total_events": len(events),
            "success_count": success_count,
            "failure_count": failure_count,
            "category_counts": category_counts,
            "severity_counts": severity_counts,
        }


# Global plugin audit logger
_plugin_audit_logger: Optional[PluginAuditLogger] = None


def get_plugin_audit_logger() -> PluginAuditLogger:
    """Get the global plugin audit logger."""
    global _plugin_audit_logger
    if _plugin_audit_logger is None:
        _plugin_audit_logger = PluginAuditLogger()
    return _plugin_audit_logger


def set_plugin_audit_logger(logger: PluginAuditLogger) -> None:
    """Set the global plugin audit logger."""
    global _plugin_audit_logger
    _plugin_audit_logger = logger
