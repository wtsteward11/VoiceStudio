"""
Tests for Plugin Audit Logger.

Phase 4 Enhancement: Tests for plugin-specific audit categories
and logging.
"""

from datetime import datetime
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from backend.plugins.audit import (
    PluginAuditCategory,
    PluginAuditEvent,
    PluginAuditLogger,
    get_plugin_audit_logger,
    set_plugin_audit_logger,
)
from backend.services.audit_logger import AuditAction, AuditSeverity


class TestPluginAuditCategory:
    """Tests for PluginAuditCategory enum."""

    def test_lifecycle_categories(self):
        """Test lifecycle categories exist."""
        assert PluginAuditCategory.LIFECYCLE_INSTALL.value == "plugin.lifecycle.install"
        assert PluginAuditCategory.LIFECYCLE_UNINSTALL.value == "plugin.lifecycle.uninstall"
        assert PluginAuditCategory.LIFECYCLE_UPDATE.value == "plugin.lifecycle.update"
        assert PluginAuditCategory.LIFECYCLE_ENABLE.value == "plugin.lifecycle.enable"
        assert PluginAuditCategory.LIFECYCLE_DISABLE.value == "plugin.lifecycle.disable"

    def test_security_categories(self):
        """Test security categories exist."""
        assert (
            PluginAuditCategory.SECURITY_SIGNATURE_VALID.value == "plugin.security.signature_valid"
        )
        assert (
            PluginAuditCategory.SECURITY_PERMISSION_DENIED.value
            == "plugin.security.permission_denied"
        )
        assert (
            PluginAuditCategory.SECURITY_POLICY_VIOLATION.value
            == "plugin.security.policy_violation"
        )
        assert (
            PluginAuditCategory.SECURITY_SANDBOX_VIOLATION.value
            == "plugin.security.sandbox_violation"
        )

    def test_policy_categories(self):
        """Test policy categories exist."""
        assert PluginAuditCategory.POLICY_WHITELIST_ADD.value == "plugin.policy.whitelist_add"
        assert PluginAuditCategory.POLICY_BLACKLIST_ADD.value == "plugin.policy.blacklist_add"
        assert PluginAuditCategory.POLICY_EVALUATION.value == "plugin.policy.evaluation"

    def test_execution_categories(self):
        """Test execution categories exist."""
        assert PluginAuditCategory.EXECUTION_START.value == "plugin.execution.start"
        assert PluginAuditCategory.EXECUTION_COMPLETE.value == "plugin.execution.complete"
        assert PluginAuditCategory.EXECUTION_ERROR.value == "plugin.execution.error"
        assert PluginAuditCategory.EXECUTION_CRASH.value == "plugin.execution.crash"

    def test_marketplace_categories(self):
        """Test marketplace categories exist."""
        assert PluginAuditCategory.MARKETPLACE_DOWNLOAD.value == "plugin.marketplace.download"
        assert PluginAuditCategory.MARKETPLACE_PUBLISH.value == "plugin.marketplace.publish"


class TestPluginAuditEvent:
    """Tests for PluginAuditEvent dataclass."""

    def test_create_event(self):
        """Test creating an audit event."""
        event = PluginAuditEvent(
            category=PluginAuditCategory.LIFECYCLE_INSTALL,
            plugin_id="test.plugin",
            timestamp=datetime.now(),
            severity=AuditSeverity.INFO,
            details={"version": "1.0.0"},
            success=True,
        )
        assert event.plugin_id == "test.plugin"
        assert event.category == PluginAuditCategory.LIFECYCLE_INSTALL
        assert event.success is True

    def test_to_dict(self):
        """Test converting event to dictionary."""
        event = PluginAuditEvent(
            category=PluginAuditCategory.SECURITY_PERMISSION_DENIED,
            plugin_id="test.plugin",
            timestamp=datetime.now(),
            severity=AuditSeverity.WARNING,
            details={"permission": "network.http"},
            success=False,
            error_message="Permission denied",
        )

        d = event.to_dict()
        assert d["category"] == "plugin.security.permission_denied"
        assert d["plugin_id"] == "test.plugin"
        assert d["severity"] == "warning"
        assert d["success"] is False
        assert d["error_message"] == "Permission denied"


class TestPluginAuditLogger:
    """Tests for PluginAuditLogger."""

    @pytest.fixture
    def mock_audit_logger(self):
        """Create a mock audit logger."""
        mock = MagicMock()
        mock.log = AsyncMock(return_value="test-id")
        return mock

    @pytest.fixture
    def plugin_audit(self, mock_audit_logger):
        """Create plugin audit logger with mock."""
        return PluginAuditLogger(audit_logger=mock_audit_logger)

    @pytest.mark.asyncio
    async def test_log_basic(self, plugin_audit, mock_audit_logger):
        """Test basic logging."""
        entry_id = await plugin_audit.log(
            category=PluginAuditCategory.LIFECYCLE_INSTALL,
            plugin_id="test.plugin",
            details={"version": "1.0.0"},
        )

        assert entry_id == "test-id"
        mock_audit_logger.log.assert_called_once()

        # Check call args
        call_kwargs = mock_audit_logger.log.call_args.kwargs
        assert call_kwargs["entity_type"] == "plugin"
        assert call_kwargs["entity_id"] == "test.plugin"
        assert "plugin_category" in call_kwargs["metadata"]

    @pytest.mark.asyncio
    async def test_log_install(self, plugin_audit):
        """Test install logging."""
        await plugin_audit.log_install(
            plugin_id="test.plugin",
            version="1.0.0",
            source="marketplace",
        )

        # Check in-memory events
        events = plugin_audit.get_recent_events(plugin_id="test.plugin")
        assert len(events) == 1
        assert events[0].category == PluginAuditCategory.LIFECYCLE_INSTALL
        assert events[0].details["version"] == "1.0.0"

    @pytest.mark.asyncio
    async def test_log_uninstall(self, plugin_audit):
        """Test uninstall logging."""
        await plugin_audit.log_uninstall(
            plugin_id="test.plugin",
            version="1.0.0",
            reason="User requested",
        )

        events = plugin_audit.get_recent_events()
        assert len(events) == 1
        assert events[0].category == PluginAuditCategory.LIFECYCLE_UNINSTALL

    @pytest.mark.asyncio
    async def test_log_update(self, plugin_audit):
        """Test update logging."""
        await plugin_audit.log_update(
            plugin_id="test.plugin",
            old_version="1.0.0",
            new_version="2.0.0",
        )

        events = plugin_audit.get_recent_events()
        assert events[0].details["old_version"] == "1.0.0"
        assert events[0].details["new_version"] == "2.0.0"

    @pytest.mark.asyncio
    async def test_log_signature_check_valid(self, plugin_audit):
        """Test valid signature logging."""
        await plugin_audit.log_signature_check(
            plugin_id="test.plugin",
            valid=True,
            signer="VoiceStudio",
        )

        events = plugin_audit.get_recent_events()
        assert events[0].category == PluginAuditCategory.SECURITY_SIGNATURE_VALID
        assert events[0].success is True

    @pytest.mark.asyncio
    async def test_log_signature_check_invalid(self, plugin_audit):
        """Test invalid signature logging."""
        await plugin_audit.log_signature_check(
            plugin_id="test.plugin",
            valid=False,
        )

        events = plugin_audit.get_recent_events()
        assert events[0].category == PluginAuditCategory.SECURITY_SIGNATURE_INVALID
        assert events[0].success is False

    @pytest.mark.asyncio
    async def test_log_permission_check_granted(self, plugin_audit):
        """Test permission granted logging."""
        await plugin_audit.log_permission_check(
            plugin_id="test.plugin",
            permission="audio.playback",
            granted=True,
        )

        events = plugin_audit.get_recent_events()
        assert events[0].category == PluginAuditCategory.SECURITY_PERMISSION_GRANTED

    @pytest.mark.asyncio
    async def test_log_permission_check_denied(self, plugin_audit):
        """Test permission denied logging."""
        await plugin_audit.log_permission_check(
            plugin_id="test.plugin",
            permission="network.http",
            granted=False,
            reason="Policy restriction",
        )

        events = plugin_audit.get_recent_events()
        assert events[0].category == PluginAuditCategory.SECURITY_PERMISSION_DENIED
        assert events[0].details["reason"] == "Policy restriction"

    @pytest.mark.asyncio
    async def test_log_policy_violation(self, plugin_audit):
        """Test policy violation logging."""
        await plugin_audit.log_policy_violation(
            plugin_id="malicious.plugin",
            violation_type="blacklisted",
        )

        events = plugin_audit.get_recent_events()
        assert events[0].category == PluginAuditCategory.SECURITY_POLICY_VIOLATION
        assert events[0].severity == AuditSeverity.WARNING

    @pytest.mark.asyncio
    async def test_log_sandbox_violation(self, plugin_audit):
        """Test sandbox violation logging."""
        await plugin_audit.log_sandbox_violation(
            plugin_id="bad.plugin",
            violation_type="filesystem_escape",
            attempted_action="read /etc/passwd",
        )

        events = plugin_audit.get_recent_events()
        assert events[0].category == PluginAuditCategory.SECURITY_SANDBOX_VIOLATION
        assert events[0].severity == AuditSeverity.ERROR

    @pytest.mark.asyncio
    async def test_log_whitelist_change(self, plugin_audit):
        """Test whitelist change logging."""
        await plugin_audit.log_whitelist_change(
            plugin_id="trusted.plugin",
            added=True,
            user_id="admin",
        )

        events = plugin_audit.get_recent_events()
        assert events[0].category == PluginAuditCategory.POLICY_WHITELIST_ADD

    @pytest.mark.asyncio
    async def test_log_blacklist_change(self, plugin_audit):
        """Test blacklist change logging."""
        await plugin_audit.log_blacklist_change(
            plugin_id="malicious.plugin",
            added=True,
            reason="Security vulnerability",
        )

        events = plugin_audit.get_recent_events()
        assert events[0].category == PluginAuditCategory.POLICY_BLACKLIST_ADD
        assert events[0].severity == AuditSeverity.WARNING

    @pytest.mark.asyncio
    async def test_log_policy_evaluation(self, plugin_audit):
        """Test policy evaluation logging."""
        await plugin_audit.log_policy_evaluation(
            plugin_id="test.plugin",
            allowed=True,
            trust_level="VERIFIED",
            applied_rules=["rule:trusted-publisher"],
        )

        events = plugin_audit.get_recent_events()
        assert events[0].category == PluginAuditCategory.POLICY_EVALUATION
        assert events[0].details["trust_level"] == "VERIFIED"

    @pytest.mark.asyncio
    async def test_log_execution_start(self, plugin_audit):
        """Test execution start logging."""
        await plugin_audit.log_execution_start(
            plugin_id="test.plugin",
            method="process_audio",
            correlation_id="corr-123",
        )

        events = plugin_audit.get_recent_events()
        assert events[0].category == PluginAuditCategory.EXECUTION_START
        assert events[0].correlation_id == "corr-123"

    @pytest.mark.asyncio
    async def test_log_execution_complete(self, plugin_audit):
        """Test execution complete logging."""
        await plugin_audit.log_execution_complete(
            plugin_id="test.plugin",
            method="process_audio",
            duration_ms=150.5,
            correlation_id="corr-123",
        )

        events = plugin_audit.get_recent_events()
        assert events[0].category == PluginAuditCategory.EXECUTION_COMPLETE
        assert events[0].details["duration_ms"] == 150.5

    @pytest.mark.asyncio
    async def test_log_execution_error(self, plugin_audit):
        """Test execution error logging."""
        await plugin_audit.log_execution_error(
            plugin_id="test.plugin",
            method="process_audio",
            error="Division by zero",
            stack_trace="Traceback...",
        )

        events = plugin_audit.get_recent_events()
        assert events[0].category == PluginAuditCategory.EXECUTION_ERROR
        assert events[0].severity == AuditSeverity.ERROR

    @pytest.mark.asyncio
    async def test_log_execution_crash(self, plugin_audit):
        """Test execution crash logging."""
        await plugin_audit.log_execution_crash(
            plugin_id="test.plugin",
            exit_code=1,
            signal="SIGSEGV",
        )

        events = plugin_audit.get_recent_events()
        assert events[0].category == PluginAuditCategory.EXECUTION_CRASH
        assert events[0].severity == AuditSeverity.CRITICAL

    @pytest.mark.asyncio
    async def test_log_ipc_request(self, plugin_audit):
        """Test IPC request logging."""
        await plugin_audit.log_ipc_request(
            plugin_id="test.plugin",
            method="host.audio.play",
            request_id="req-123",
        )

        events = plugin_audit.get_recent_events()
        assert events[0].category == PluginAuditCategory.IPC_REQUEST

    @pytest.mark.asyncio
    async def test_log_ipc_error(self, plugin_audit):
        """Test IPC error logging."""
        await plugin_audit.log_ipc_error(
            plugin_id="test.plugin",
            method="host.audio.play",
            error_code=-32603,
            error_message="Internal error",
        )

        events = plugin_audit.get_recent_events()
        assert events[0].category == PluginAuditCategory.IPC_ERROR

    @pytest.mark.asyncio
    async def test_get_recent_events_filtering(self, plugin_audit):
        """Test filtering recent events."""
        await plugin_audit.log_install("plugin-a", "1.0.0")
        await plugin_audit.log_install("plugin-b", "1.0.0")
        await plugin_audit.log_enable("plugin-a")

        # Filter by plugin
        events_a = plugin_audit.get_recent_events(plugin_id="plugin-a")
        assert len(events_a) == 2

        # Filter by category
        install_events = plugin_audit.get_recent_events(
            category=PluginAuditCategory.LIFECYCLE_INSTALL
        )
        assert len(install_events) == 2

    @pytest.mark.asyncio
    async def test_get_security_events(self, plugin_audit):
        """Test getting security events."""
        await plugin_audit.log_install("test.plugin", "1.0.0")
        await plugin_audit.log_signature_check("test.plugin", True)
        await plugin_audit.log_permission_check("test.plugin", "audio", True)
        await plugin_audit.log_sandbox_violation("bad.plugin", "escape")

        security_events = plugin_audit.get_security_events()
        assert len(security_events) == 3  # Excludes install

    @pytest.mark.asyncio
    async def test_get_error_events(self, plugin_audit):
        """Test getting error events."""
        await plugin_audit.log_install("good.plugin", "1.0.0", success=True)
        await plugin_audit.log_install("bad.plugin", "1.0.0", success=False)
        await plugin_audit.log_execution_error("plugin", "method", "error")

        error_events = plugin_audit.get_error_events()
        assert len(error_events) == 2

    @pytest.mark.asyncio
    async def test_get_stats(self, plugin_audit):
        """Test getting statistics."""
        await plugin_audit.log_install("plugin-a", "1.0.0")
        await plugin_audit.log_install("plugin-b", "1.0.0", success=False)
        await plugin_audit.log_enable("plugin-a")

        stats = plugin_audit.get_stats()
        assert stats["total_events"] == 3
        assert stats["success_count"] == 2
        assert stats["failure_count"] == 1

        # Filter by plugin
        stats_a = plugin_audit.get_stats(plugin_id="plugin-a")
        assert stats_a["total_events"] == 2

    def test_category_to_action_mapping(self, plugin_audit):
        """Test category to action mapping."""
        # Lifecycle
        assert (
            plugin_audit._map_category_to_action(PluginAuditCategory.LIFECYCLE_INSTALL)
            == AuditAction.CREATE
        )
        assert (
            plugin_audit._map_category_to_action(PluginAuditCategory.LIFECYCLE_UNINSTALL)
            == AuditAction.DELETE
        )
        assert (
            plugin_audit._map_category_to_action(PluginAuditCategory.LIFECYCLE_UPDATE)
            == AuditAction.UPDATE
        )

        # Execution
        assert (
            plugin_audit._map_category_to_action(PluginAuditCategory.EXECUTION_START)
            == AuditAction.EXECUTE
        )

        # Marketplace
        assert (
            plugin_audit._map_category_to_action(PluginAuditCategory.MARKETPLACE_DOWNLOAD)
            == AuditAction.IMPORT
        )
        assert (
            plugin_audit._map_category_to_action(PluginAuditCategory.MARKETPLACE_PUBLISH)
            == AuditAction.EXPORT
        )


class TestGlobalPluginAuditLogger:
    """Tests for global plugin audit logger."""

    def test_get_plugin_audit_logger(self):
        """Test getting global logger."""
        logger = get_plugin_audit_logger()
        assert isinstance(logger, PluginAuditLogger)

    def test_set_plugin_audit_logger(self):
        """Test setting global logger."""
        mock_audit = MagicMock()
        custom_logger = PluginAuditLogger(audit_logger=mock_audit)
        set_plugin_audit_logger(custom_logger)

        logger = get_plugin_audit_logger()
        assert logger._audit_logger == mock_audit
