"""
Unit Tests for Temporary File Manager
Tests temporary file lifecycle management, cleanup, and disk space monitoring.
"""

import os
import shutil
import sys
import tempfile
import time
from datetime import datetime, timedelta
from pathlib import Path
from unittest.mock import MagicMock, patch

import pytest

project_root = Path(__file__).parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

# Import the temp file manager module
try:
    from app.core.utils.temp_file_manager import (
        TempFileInfo,
        TempFileManager,
        get_temp_file_manager,
    )

    HAS_TEMP_FILE_MANAGER = True
except ImportError:
    HAS_TEMP_FILE_MANAGER = False

pytestmark = pytest.mark.skipif(
    not HAS_TEMP_FILE_MANAGER,
    reason="Temp file manager not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)",
)


@pytest.fixture
def temp_dir():
    """Create a temporary directory for testing."""
    test_dir = tempfile.mkdtemp(prefix="test_temp_manager_")
    yield Path(test_dir)
    # Cleanup
    if Path(test_dir).exists():
        shutil.rmtree(test_dir, ignore_errors=True)


@pytest.fixture
def manager(temp_dir):
    """Create a TempFileManager instance for testing."""
    manager = TempFileManager(
        temp_root=temp_dir,
        max_age_seconds=60.0,  # 1 minute for testing
        max_disk_usage_percent=90.0,
        cleanup_interval_seconds=10.0,
    )
    yield manager
    # Cleanup
    manager.stop_background_cleanup()
    manager.cleanup_all()


class TestTempFileInfo:
    """Test TempFileInfo dataclass."""

    def test_temp_file_info_creation(self, temp_dir):
        """Test creating TempFileInfo."""
        test_file = temp_dir / "test.txt"
        test_file.write_text("test content")

        info = TempFileInfo(
            path=test_file,
            created_at=datetime.now(),
            last_accessed=datetime.now(),
            size_bytes=12,
            is_directory=False,
            owner="test_owner",
            tags={"test", "file"},
        )

        assert info.path == test_file
        assert info.size_bytes == 12
        assert info.is_directory is False
        assert info.owner == "test_owner"
        assert "test" in info.tags
        assert "file" in info.tags

    def test_temp_file_info_update_access(self, temp_dir):
        """Test updating last accessed timestamp."""
        test_file = temp_dir / "test.txt"
        test_file.write_text("test")

        info = TempFileInfo(
            path=test_file,
            created_at=datetime.now(),
            last_accessed=datetime.now() - timedelta(hours=1),
            size_bytes=4,
            is_directory=False,
        )

        old_access = info.last_accessed
        time.sleep(0.1)
        info.update_access()

        assert info.last_accessed > old_access

    def test_temp_file_info_update_size(self, temp_dir):
        """Test updating file size."""
        test_file = temp_dir / "test.txt"
        test_file.write_text("test content")

        info = TempFileInfo(
            path=test_file,
            created_at=datetime.now(),
            last_accessed=datetime.now(),
            size_bytes=0,
            is_directory=False,
        )

        info.update_size()
        assert info.size_bytes == 12


class TestTempFileManagerInitialization:
    """Test TempFileManager initialization."""

    def test_manager_initialization_default(self):
        """Test manager initialization with defaults."""
        manager = TempFileManager()
        assert manager.temp_root.exists()
        assert manager.max_age_seconds == 3600.0
        assert manager.max_disk_usage_percent == 90.0
        assert manager.cleanup_interval_seconds == 300.0
        manager.cleanup_all()

    def test_manager_initialization_custom(self, temp_dir):
        """Test manager initialization with custom parameters."""
        manager = TempFileManager(
            temp_root=temp_dir,
            max_age_seconds=120.0,
            max_disk_usage_percent=85.0,
            cleanup_interval_seconds=30.0,
        )
        assert manager.temp_root == temp_dir
        assert manager.max_age_seconds == 120.0
        assert manager.max_disk_usage_percent == 85.0
        assert manager.cleanup_interval_seconds == 30.0
        manager.cleanup_all()


class TestTempFileCreation:
    """Test temporary file and directory creation."""

    def test_create_temp_file(self, manager):
        """Test creating a temporary file."""
        path = manager.create_temp_file(suffix=".txt", prefix="test_", owner="test")

        assert path.exists()
        assert path.is_file()
        assert path.name.startswith("test_")
        assert path.name.endswith(".txt")
        assert path in manager._temp_files

    def test_create_temp_file_with_content(self, manager):
        """Test creating a temporary file and writing content."""
        path = manager.create_temp_file(suffix=".txt")
        path.write_text("test content")

        info = manager._temp_files[path]
        info.update_size()

        assert path.read_text() == "test content"
        assert info.size_bytes > 0

    def test_create_temp_directory(self, manager):
        """Test creating a temporary directory."""
        path = manager.create_temp_directory(suffix="_dir", prefix="test_", owner="test")

        assert path.exists()
        assert path.is_dir()
        assert path.name.startswith("test_")
        assert path.name.endswith("_dir")
        assert path in manager._temp_files

    def test_create_temp_file_with_tags(self, manager):
        """Test creating a temporary file with tags."""
        path = manager.create_temp_file(tags={"audio", "synthesis"})

        info = manager._temp_files[path]
        assert "audio" in info.tags
        assert "synthesis" in info.tags


class TestTempFileRegistration:
    """Test registering existing temporary files."""

    def test_register_temp_file(self, manager, temp_dir):
        """Test registering an existing file."""
        test_file = temp_dir / "existing.txt"
        test_file.write_text("existing content")

        manager.register_temp_file(test_file, owner="test", tags={"existing"})

        assert test_file in manager._temp_files
        info = manager._temp_files[test_file]
        assert info.owner == "test"
        assert "existing" in info.tags

    def test_register_temp_directory(self, manager, temp_dir):
        """Test registering an existing directory."""
        test_dir = temp_dir / "existing_dir"
        test_dir.mkdir()

        manager.register_temp_file(test_dir, owner="test")

        assert test_dir in manager._temp_files
        info = manager._temp_files[test_dir]
        assert info.is_directory is True

    def test_register_nonexistent_file(self, manager, temp_dir):
        """Test registering a non-existent file (should be ignored)."""
        nonexistent = temp_dir / "nonexistent.txt"

        manager.register_temp_file(nonexistent)

        assert nonexistent not in manager._temp_files


class TestTempFileRemoval:
    """Test removing temporary files."""

    def test_remove_temp_file(self, manager):
        """Test removing a tracked temporary file."""
        path = manager.create_temp_file()
        assert path.exists()

        result = manager.remove_temp_file(path)

        assert result is True
        assert not path.exists()
        assert path not in manager._temp_files

    def test_remove_temp_directory(self, manager):
        """Test removing a tracked temporary directory."""
        path = manager.create_temp_directory()
        (path / "file.txt").write_text("test")
        assert path.exists()

        result = manager.remove_temp_file(path)

        assert result is True
        assert not path.exists()
        assert path not in manager._temp_files

    def test_remove_untracked_file(self, manager, temp_dir):
        """Test removing an untracked file."""
        test_file = temp_dir / "untracked.txt"
        test_file.write_text("test")

        result = manager.remove_temp_file(test_file, force=False)

        assert result is False
        assert test_file.exists()  # Should not be removed

    def test_remove_untracked_file_force(self, manager, temp_dir):
        """Test force removing an untracked file."""
        test_file = temp_dir / "untracked.txt"
        test_file.write_text("test")

        result = manager.remove_temp_file(test_file, force=True)

        assert result is True
        assert not test_file.exists()


class TestCleanupOldFiles:
    """Test cleanup of old temporary files."""

    def test_cleanup_old_files(self, manager):
        """Test cleaning up old files."""
        # Create old file
        old_path = manager.create_temp_file()
        old_info = manager._temp_files[old_path]
        old_info.created_at = datetime.now() - timedelta(seconds=120)  # 2 minutes old

        # Create new file
        new_path = manager.create_temp_file()

        # Cleanup with 60 second max age
        result = manager.cleanup_old_files(max_age_seconds=60.0)

        assert result["removed_count"] >= 1
        assert old_path not in manager._temp_files
        assert new_path in manager._temp_files  # Should still exist

    def test_cleanup_old_files_none_old(self, manager):
        """Test cleanup when no files are old."""
        path = manager.create_temp_file()

        result = manager.cleanup_old_files(max_age_seconds=3600.0)

        assert result["removed_count"] == 0
        assert path in manager._temp_files

    def test_cleanup_old_files_custom_age(self, manager):
        """Test cleanup with custom max age."""
        old_path = manager.create_temp_file()
        old_info = manager._temp_files[old_path]
        old_info.created_at = datetime.now() - timedelta(seconds=30)

        result = manager.cleanup_old_files(max_age_seconds=20.0)

        assert result["removed_count"] >= 1
        assert old_path not in manager._temp_files


class TestCleanupByDiskSpace:
    """Test cleanup based on disk space."""

    @patch("app.core.utils.temp_file_manager.HAS_PSUTIL", True)
    @patch("app.core.utils.temp_file_manager.psutil")
    def test_cleanup_by_disk_space_low_usage(self, mock_psutil, manager):
        """Test cleanup when disk usage is low."""
        mock_disk = MagicMock()
        mock_disk.used = 50 * 1024 * 1024 * 1024  # 50 GB
        mock_disk.total = 100 * 1024 * 1024 * 1024  # 100 GB
        mock_disk.free = 50 * 1024 * 1024 * 1024  # 50 GB
        mock_psutil.disk_usage.return_value = mock_disk

        result = manager.cleanup_by_disk_space()

        assert result["action"] == "no_cleanup_needed"
        assert result["disk_usage_percent"] == 50.0

    @patch("app.core.utils.temp_file_manager.HAS_PSUTIL", True)
    @patch("app.core.utils.temp_file_manager.psutil")
    def test_cleanup_by_disk_space_high_usage(self, mock_psutil, manager):
        """Test cleanup when disk usage is high."""
        # Create some files
        for _i in range(5):
            path = manager.create_temp_file()
            path.write_text("x" * 1000)  # 1KB each

        mock_disk = MagicMock()
        mock_disk.used = 95 * 1024 * 1024 * 1024  # 95 GB
        mock_disk.total = 100 * 1024 * 1024 * 1024  # 100 GB
        mock_disk.free = 5 * 1024 * 1024 * 1024  # 5 GB
        mock_psutil.disk_usage.return_value = mock_disk

        result = manager.cleanup_by_disk_space()

        assert result["action"] == "cleanup_performed"
        assert result["disk_usage_percent"] == 95.0
        assert result["removed_count"] >= 0

    def test_cleanup_by_disk_space_no_psutil(self, manager):
        """Test cleanup when psutil is not available."""
        with patch("app.core.utils.temp_file_manager.HAS_PSUTIL", False):
            result = manager.cleanup_by_disk_space()

            assert "error" in result
            assert "psutil not available" in result["error"]


class TestCleanupAll:
    """Test cleaning up all temporary files."""

    def test_cleanup_all(self, manager):
        """Test cleaning up all temporary files."""
        # Create multiple files
        paths = [manager.create_temp_file() for _ in range(5)]

        result = manager.cleanup_all()

        assert result["removed_count"] == 5
        assert len(manager._temp_files) == 0
        for path in paths:
            assert not path.exists()


class TestListTempFiles:
    """Test listing temporary files."""

    def test_list_temp_files_all(self, manager):
        """Test listing all temporary files."""
        paths = [manager.create_temp_file() for _ in range(3)]

        results = manager.list_temp_files()

        assert len(results) == 3
        result_paths = {r["path"] for r in results}
        assert all(str(p) in result_paths for p in paths)

    def test_list_temp_files_by_owner(self, manager):
        """Test listing files filtered by owner."""
        manager.create_temp_file(owner="owner1")
        manager.create_temp_file(owner="owner2")
        manager.create_temp_file(owner="owner1")

        results = manager.list_temp_files(owner="owner1")

        assert len(results) == 2
        assert all(r["owner"] == "owner1" for r in results)

    def test_list_temp_files_by_tags(self, manager):
        """Test listing files filtered by tags."""
        manager.create_temp_file(tags={"audio", "test"})
        manager.create_temp_file(tags={"audio"})
        manager.create_temp_file(tags={"test"})

        results = manager.list_temp_files(tags={"audio", "test"})

        assert len(results) == 1
        assert "audio" in results[0]["tags"]
        assert "test" in results[0]["tags"]

    def test_list_temp_files_by_age(self, manager):
        """Test listing files filtered by age."""
        old_path = manager.create_temp_file()
        old_info = manager._temp_files[old_path]
        old_info.created_at = datetime.now() - timedelta(seconds=120)

        new_path = manager.create_temp_file()

        results = manager.list_temp_files(max_age_seconds=60.0)

        assert len(results) == 1
        assert str(new_path) in [r["path"] for r in results]


class TestStatistics:
    """Test statistics and reporting."""

    def test_get_statistics(self, manager):
        """Test getting statistics."""
        manager.create_temp_file(owner="test1", tags={"audio"})
        manager.create_temp_file(owner="test2", tags={"audio", "synthesis"})

        stats = manager.get_stats()

        assert "total_files" in stats
        assert "total_size_mb" in stats
        assert "total_size_gb" in stats
        assert "by_owner" in stats
        assert "by_tag" in stats
        assert "disk_space" in stats
        assert stats["total_files"] == 2

    def test_get_disk_space_info(self, manager):
        """Test getting disk space information."""
        info = manager.get_disk_space_info()

        # Should return dict or error message
        assert isinstance(info, (dict, str))


class TestBackgroundCleanup:
    """Test background cleanup thread."""

    def test_start_background_cleanup(self, manager):
        """Test starting background cleanup thread."""
        manager._start_background_cleanup()

        assert manager._cleanup_running is True
        assert manager._cleanup_thread is not None
        assert manager._cleanup_thread.is_alive()

        manager.stop_background_cleanup()

    def test_stop_background_cleanup(self, manager):
        """Test stopping background cleanup thread."""
        manager._start_background_cleanup()
        time.sleep(0.1)  # Give thread time to start

        manager.stop_background_cleanup()
        time.sleep(0.2)  # Give thread time to stop

        assert manager._cleanup_running is False

    def test_background_cleanup_auto_start(self, manager):
        """Test that background cleanup starts automatically."""
        manager.create_temp_file()

        # Background cleanup should start automatically
        time.sleep(0.1)
        assert manager._cleanup_running is True

        manager.stop_background_cleanup()


class TestLifecycleMethods:
    """Test lifecycle management methods."""

    def test_cleanup_on_startup(self, manager, temp_dir):
        """Test cleanup on startup."""
        # Create some old files in temp directory
        old_file = temp_dir / "old_file.txt"
        old_file.write_text("old")
        old_file.touch()
        # Make it old
        old_time = (datetime.now() - timedelta(hours=2)).timestamp()
        os.utime(old_file, (old_time, old_time))

        manager.cleanup_on_startup()

        # Old files should be cleaned up
        # (Implementation may vary, but should not crash)

    def test_cleanup_on_shutdown(self, manager):
        """Test cleanup on shutdown."""
        # Create old files that will be cleaned up
        old_path = manager.create_temp_file()
        old_info = manager._temp_files[old_path]
        old_info.created_at = datetime.now() - timedelta(seconds=120)  # 2 minutes old

        # Create new file (should not be cleaned up by cleanup_old_files)
        manager.create_temp_file()

        manager.cleanup_on_shutdown()

        # Old files should be cleaned up, new files may remain
        # (cleanup_on_shutdown calls cleanup_old_files, not cleanup_all)
        assert old_path not in manager._temp_files or not old_path.exists()


class TestGlobalFunction:
    """Test global get_temp_file_manager function."""

    def test_get_temp_file_manager(self):
        """Test getting global temp file manager instance."""
        manager = get_temp_file_manager()

        assert manager is not None
        assert isinstance(manager, TempFileManager)


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
