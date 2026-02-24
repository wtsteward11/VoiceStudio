"""
Unit tests for BackupService.

Tests backup creation, restoration, listing, and error handling.
"""

from __future__ import annotations

import asyncio
import shutil
from pathlib import Path
from unittest.mock import patch

import pytest

from backend.services.backup_service import (
    BackupConfig,
    BackupService,
    BackupStatus,
    BackupType,
)


class TestBackupService:
    """Tests for BackupService."""

    @pytest.fixture
    def backup_config(self, tmp_path: Path) -> BackupConfig:
        """Create backup config with test paths."""
        data_dir = tmp_path / "data"
        data_dir.mkdir(parents=True, exist_ok=True)
        (data_dir / "projects").mkdir(parents=True, exist_ok=True)
        (data_dir / "projects" / "test_project.json").write_text("{}")
        (data_dir / "test_file.txt").write_text("hello world")

        return BackupConfig(
            backup_path=str(tmp_path / "backups"),
            retention_days=7,
            max_backups=5,
            compression=False,
            verify_after_backup=False,
            source_paths=[
                str(data_dir / "projects"),
                str(data_dir / "test_file.txt"),
            ],
        )

    @pytest.fixture
    def backup_service(self, backup_config: BackupConfig) -> BackupService:
        """Create BackupService instance with test config."""
        return BackupService(config=backup_config)

    def test_import(self) -> None:
        """Test that BackupService can be imported."""
        from backend.services.backup_service import BackupService

        assert BackupService is not None

    def test_list_backups_empty(self, backup_service: BackupService) -> None:
        """Test listing backups when none exist."""
        backups = backup_service.list_backups()
        assert backups == []
        assert isinstance(backups, list)

    @pytest.mark.asyncio
    async def test_create_backup(
        self,
        backup_service: BackupService,
        backup_config: BackupConfig,
    ) -> None:
        """Test backup creation."""
        manifest = await backup_service.create_backup(
            backup_type=BackupType.FULL,
            message="test backup",
        )

        assert manifest is not None
        assert manifest.backup_id
        assert manifest.backup_type == BackupType.FULL
        assert manifest.status in (BackupStatus.COMPLETED, BackupStatus.VERIFIED)
        assert manifest.file_count >= 2
        assert manifest.size_bytes > 0
        assert manifest.completed_at is not None
        assert manifest.source_paths == backup_config.source_paths
        assert manifest.metadata.get("message") == "test backup"

    @pytest.mark.asyncio
    async def test_list_backups_after_create(
        self,
        backup_service: BackupService,
    ) -> None:
        """Test backup listing after creating backups."""
        await backup_service.create_backup(message="first")
        await asyncio.sleep(1.1)  # Ensure distinct backup IDs (format: %Y%m%d_%H%M%S)
        await backup_service.create_backup(message="second")

        backups = backup_service.list_backups()
        assert len(backups) == 2
        assert backups[0].created_at >= backups[1].created_at

    @pytest.mark.asyncio
    async def test_restore_backup(
        self,
        backup_service: BackupService,
        backup_config: BackupConfig,
        tmp_path: Path,
    ) -> None:
        """Test backup restoration to target path."""
        manifest = await backup_service.create_backup(message="restore test")
        assert manifest.status in (BackupStatus.COMPLETED, BackupStatus.VERIFIED)

        target = tmp_path / "restored"
        result = await backup_service.restore_backup(
            backup_id=manifest.backup_id,
            target_path=str(target),
        )

        assert result is True
        assert target.exists()
        assert (target / "projects").exists()
        assert (target / "projects" / "test_project.json").exists()
        assert (target / "test_file.txt").exists()
        assert (target / "test_file.txt").read_text() == "hello world"

    @pytest.mark.asyncio
    async def test_restore_nonexistent_backup(
        self,
        backup_service: BackupService,
    ) -> None:
        """Test restore fails for nonexistent backup ID."""
        result = await backup_service.restore_backup(backup_id="nonexistent_20250101_000000")
        assert result is False

    @pytest.mark.asyncio
    async def test_create_backup_error_handling(
        self,
        backup_service: BackupService,
    ) -> None:
        """Test backup creation handles errors gracefully."""
        with patch.object(
            shutil,
            "copy2",
            side_effect=OSError("Simulated disk error"),
        ):
            manifest = await backup_service.create_backup(message="should fail")

        assert manifest.status == BackupStatus.FAILED
        assert manifest.error_message is not None
        assert "Simulated disk error" in manifest.error_message

    @pytest.mark.asyncio
    async def test_get_stats(
        self,
        backup_service: BackupService,
    ) -> None:
        """Test backup statistics."""
        await backup_service.create_backup(message="stats test")

        stats = backup_service.get_stats()
        assert "backup_count" in stats
        assert "total_size_mb" in stats
        assert "verified_count" in stats
        assert "retention_days" in stats
        assert "max_backups" in stats
        assert stats["backup_count"] >= 1
        assert stats["total_size_mb"] >= 0
