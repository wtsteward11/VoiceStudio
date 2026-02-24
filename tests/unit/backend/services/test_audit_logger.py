"""
Unit tests for backend/services/audit_logger.py.

Tests:
- Log entry creation
- Different log levels (severity)
- Log retrieval via query
- Log filtering (entity_type, action, user_id)
- Sensitive data masking
"""

import json
import tempfile
from datetime import datetime
from pathlib import Path

import pytest

from backend.services.audit_logger import (
    AuditAction,
    AuditConfig,
    AuditEntry,
    AuditLogger,
    AuditSeverity,
)


@pytest.fixture
def temp_audit_dir():
    """Create a temporary directory for audit log storage."""
    with tempfile.TemporaryDirectory() as tmpdir:
        yield Path(tmpdir)


@pytest.fixture
def audit_config(temp_audit_dir):
    """Create AuditConfig with temp storage and sync writes for tests."""
    return AuditConfig(
        storage_path=str(temp_audit_dir),
        async_writes=False,
    )


@pytest.fixture
def audit_logger(audit_config):
    """Create an AuditLogger instance with test config."""
    return AuditLogger(config=audit_config)


class TestAuditLoggerCreation:
    """Tests for log entry creation."""

    @pytest.mark.asyncio
    async def test_log_entry_creation_returns_id(self, audit_logger, temp_audit_dir):
        """Test that logging creates an entry and returns a valid ID."""
        entry_id = await audit_logger.log(
            action=AuditAction.CREATE,
            entity_type="voice_profile",
            entity_id="profile-123",
            new_value={"name": "Test Voice", "language": "en"},
        )

        assert entry_id is not None
        assert len(entry_id) == 36  # UUID format

        today = datetime.now().strftime("%Y-%m-%d")
        log_file = temp_audit_dir / f"audit_{today}.jsonl"
        assert log_file.exists()

    @pytest.mark.asyncio
    async def test_log_create_convenience_method(self, audit_logger, temp_audit_dir):
        """Test log_create convenience method."""
        entry_id = await audit_logger.log_create(
            entity_type="project",
            entity_id="proj-456",
            new_value={"name": "My Project", "tracks": []},
        )

        assert entry_id is not None

        today = datetime.now().strftime("%Y-%m-%d")
        log_file = temp_audit_dir / f"audit_{today}.jsonl"
        with open(log_file) as f:
            line = f.readline()
            entry = json.loads(line)
            assert entry["action"] == "create"
            assert entry["entity_type"] == "project"
            assert entry["entity_id"] == "proj-456"


class TestAuditLoggerSeverity:
    """Tests for different log levels."""

    @pytest.mark.asyncio
    async def test_log_different_severity_levels(self, audit_logger, temp_audit_dir):
        """Test that different severity levels are stored correctly."""
        severities = [
            AuditSeverity.DEBUG,
            AuditSeverity.INFO,
            AuditSeverity.WARNING,
            AuditSeverity.ERROR,
            AuditSeverity.CRITICAL,
        ]

        for severity in severities:
            await audit_logger.log(
                action=AuditAction.CONFIG_CHANGE,
                entity_type="settings",
                severity=severity,
                metadata={"level": severity.value},
            )

        today = datetime.now().strftime("%Y-%m-%d")
        log_file = temp_audit_dir / f"audit_{today}.jsonl"

        with open(log_file) as f:
            lines = f.readlines()

        assert len(lines) == 5

        stored_severities = set()
        for line in lines:
            entry = json.loads(line)
            stored_severities.add(entry["severity"])

        assert stored_severities == {"debug", "info", "warning", "error", "critical"}


class TestAuditLoggerRetrieval:
    """Tests for log retrieval."""

    @pytest.mark.asyncio
    async def test_log_retrieval_via_query(self, audit_logger):
        """Test that logged entries can be retrieved via query."""
        await audit_logger.log(
            action=AuditAction.CREATE,
            entity_type="clip",
            entity_id="clip-001",
            new_value={"duration": 5.2},
        )
        await audit_logger.log(
            action=AuditAction.UPDATE,
            entity_type="clip",
            entity_id="clip-001",
            old_value={"duration": 5.2},
            new_value={"duration": 6.0},
        )

        entries = await audit_logger.query(limit=10)

        assert len(entries) >= 2
        assert all(isinstance(e, AuditEntry) for e in entries)
        assert entries[0].entity_type == "clip"
        assert entries[0].entity_id == "clip-001"

    @pytest.mark.asyncio
    async def test_get_entity_history(self, audit_logger):
        """Test get_entity_history returns entity-specific entries."""
        await audit_logger.log_create(
            entity_type="voice",
            entity_id="voice-789",
            new_value={"name": "Voice A"},
        )
        await audit_logger.log_update(
            entity_type="voice",
            entity_id="voice-789",
            old_value={"name": "Voice A"},
            new_value={"name": "Voice A Updated"},
        )

        history = await audit_logger.get_entity_history(
            entity_type="voice",
            entity_id="voice-789",
            limit=10,
        )

        assert len(history) >= 2
        for entry in history:
            assert entry.entity_type == "voice"
            assert entry.entity_id == "voice-789"


class TestAuditLoggerFiltering:
    """Tests for log filtering."""

    @pytest.mark.asyncio
    async def test_filter_by_entity_type(self, audit_logger):
        """Test filtering logs by entity_type."""
        await audit_logger.log(
            action=AuditAction.CREATE,
            entity_type="profile",
            entity_id="p1",
            new_value={},
        )
        await audit_logger.log(
            action=AuditAction.CREATE,
            entity_type="project",
            entity_id="proj1",
            new_value={},
        )
        await audit_logger.log(
            action=AuditAction.CREATE,
            entity_type="profile",
            entity_id="p2",
            new_value={},
        )

        profile_entries = await audit_logger.query(
            entity_type="profile",
            limit=10,
        )

        assert len(profile_entries) == 2
        assert all(e.entity_type == "profile" for e in profile_entries)

    @pytest.mark.asyncio
    async def test_filter_by_action(self, audit_logger):
        """Test filtering logs by action type."""
        await audit_logger.log_create(
            entity_type="item",
            entity_id="item-1",
            new_value={},
        )
        await audit_logger.log_delete(
            entity_type="item",
            entity_id="item-2",
            old_value={},
        )
        await audit_logger.log_create(
            entity_type="item",
            entity_id="item-3",
            new_value={},
        )

        create_entries = await audit_logger.query(
            action=AuditAction.CREATE,
            entity_type="item",
            limit=10,
        )

        assert len(create_entries) == 2
        assert all(e.action == AuditAction.CREATE for e in create_entries)

    @pytest.mark.asyncio
    async def test_filter_by_user_id(self, audit_logger):
        """Test filtering logs by user_id."""
        await audit_logger.log(
            action=AuditAction.LOGIN,
            entity_type="user",
            entity_id="user-alpha",
            user_id="user-alpha",
            success=True,
        )
        await audit_logger.log(
            action=AuditAction.LOGIN,
            entity_type="user",
            entity_id="user-beta",
            user_id="user-beta",
            success=True,
        )

        alpha_entries = await audit_logger.query(
            user_id="user-alpha",
            limit=10,
        )

        assert len(alpha_entries) >= 1
        assert all(e.user_id == "user-alpha" for e in alpha_entries)


class TestAuditLoggerSensitiveData:
    """Tests for sensitive data masking."""

    @pytest.mark.asyncio
    async def test_sensitive_fields_masked(self, audit_logger, temp_audit_dir):
        """Test that sensitive fields are masked in stored entries."""
        await audit_logger.log(
            action=AuditAction.CREATE,
            entity_type="credential",
            entity_id="cred-1",
            new_value={
                "username": "alice",
                "password": "secret123",
                "api_key": "sk-abc123",
            },
        )

        today = datetime.now().strftime("%Y-%m-%d")
        log_file = temp_audit_dir / f"audit_{today}.jsonl"

        with open(log_file) as f:
            entry = json.loads(f.readline())

        assert entry["new_value"]["username"] == "alice"
        assert entry["new_value"]["password"] == "***MASKED***"
        assert entry["new_value"]["api_key"] == "***MASKED***"


class TestAuditLoggerStats:
    """Tests for audit statistics."""

    @pytest.mark.asyncio
    async def test_get_stats(self, audit_logger):
        """Test get_stats returns meaningful statistics."""
        await audit_logger.log(
            action=AuditAction.CREATE,
            entity_type="test",
            entity_id="stats-check",
            new_value={},
        )

        stats = audit_logger.get_stats()

        assert "file_count" in stats
        assert "total_size_mb" in stats
        assert "retention_days" in stats
        assert "async_writes" in stats
        assert stats["file_count"] >= 1
        assert stats["retention_days"] == 90
