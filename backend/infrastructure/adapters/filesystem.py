"""
File System Adapter.

Task 3.2.4: Adapter for file system operations.
"""

from __future__ import annotations

import asyncio
import hashlib
import logging
import shutil
from pathlib import Path
from typing import Any

from backend.config.path_config import get_path
from backend.infrastructure.adapters.base import Adapter

logger = logging.getLogger(__name__)


class FileSystemAdapter(Adapter):
    """
    Adapter for file system operations.

    Provides async file I/O with path validation.
    """

    def __init__(self, base_path: Path | None = None):
        """
        Initialize file system adapter.

        Args:
            base_path: Base path for all operations
        """
        super().__init__("FileSystem")

        self._base_path = base_path or get_path("data")
        self._base_path.mkdir(parents=True, exist_ok=True)

    async def connect(self) -> bool:
        """Verify file system access."""
        try:
            # Check if base path is accessible
            self._base_path.mkdir(parents=True, exist_ok=True)
            self._connected = True
            return True
        except Exception as e:
            self._logger.error(f"File system access failed: {e}")
            return False

    async def disconnect(self) -> bool:
        """No-op for file system."""
        self._connected = False
        return True

    async def health_check(self) -> dict[str, Any]:
        """Check file system health."""
        try:
            # Check disk space
            stat = shutil.disk_usage(self._base_path)

            return {
                "connected": self._connected,
                "base_path": str(self._base_path),
                "total_bytes": stat.total,
                "used_bytes": stat.used,
                "free_bytes": stat.free,
                "usage_percent": (stat.used / stat.total) * 100,
            }
        except Exception as e:
            return {
                "connected": False,
                "error": str(e),
            }

    def _resolve_path(self, path: str) -> Path:
        """Resolve and validate a path."""
        resolved = (self._base_path / path).resolve()

        # Security: ensure path is within base path
        if not str(resolved).startswith(str(self._base_path.resolve())):
            raise ValueError(f"Path outside base directory: {path}")

        return resolved

    async def read_file(self, path: str) -> bytes:
        """
        Read a file.

        Args:
            path: Relative path

        Returns:
            File contents
        """
        resolved = self._resolve_path(path)

        # Use asyncio for non-blocking I/O
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, resolved.read_bytes)

    async def write_file(self, path: str, data: bytes) -> None:
        """
        Write a file.

        Args:
            path: Relative path
            data: File contents
        """
        resolved = self._resolve_path(path)
        resolved.parent.mkdir(parents=True, exist_ok=True)

        loop = asyncio.get_event_loop()
        await loop.run_in_executor(None, resolved.write_bytes, data)

    async def delete_file(self, path: str) -> bool:
        """
        Delete a file.

        Args:
            path: Relative path

        Returns:
            True if deleted
        """
        resolved = self._resolve_path(path)

        if resolved.exists():
            resolved.unlink()
            return True
        return False

    async def list_files(
        self,
        path: str = "",
        pattern: str = "*",
    ) -> list[str]:
        """
        List files in a directory.

        Args:
            path: Relative directory path
            pattern: Glob pattern

        Returns:
            List of file paths
        """
        resolved = self._resolve_path(path)

        if not resolved.is_dir():
            return []

        files = []
        for item in resolved.glob(pattern):
            if item.is_file():
                rel_path = item.relative_to(self._base_path)
                files.append(str(rel_path))

        return files

    async def exists(self, path: str) -> bool:
        """Check if a path exists."""
        resolved = self._resolve_path(path)
        return resolved.exists()

    async def get_file_info(self, path: str) -> dict[str, Any]:
        """
        Get file information.

        Args:
            path: Relative path

        Returns:
            File info dict
        """
        resolved = self._resolve_path(path)

        if not resolved.exists():
            return {"exists": False}

        stat = resolved.stat()

        return {
            "exists": True,
            "size": stat.st_size,
            "created": stat.st_ctime,
            "modified": stat.st_mtime,
            "is_file": resolved.is_file(),
            "is_dir": resolved.is_dir(),
        }

    async def get_hash(self, path: str, algorithm: str = "sha256") -> str:
        """
        Get file hash.

        Args:
            path: Relative path
            algorithm: Hash algorithm

        Returns:
            Hex digest
        """
        data = await self.read_file(path)
        hasher = hashlib.new(algorithm)
        hasher.update(data)
        return hasher.hexdigest()

    async def copy_file(self, src: str, dst: str) -> None:
        """Copy a file."""
        src_path = self._resolve_path(src)
        dst_path = self._resolve_path(dst)

        dst_path.parent.mkdir(parents=True, exist_ok=True)

        loop = asyncio.get_event_loop()
        await loop.run_in_executor(None, shutil.copy2, src_path, dst_path)

    async def move_file(self, src: str, dst: str) -> None:
        """Move a file."""
        src_path = self._resolve_path(src)
        dst_path = self._resolve_path(dst)

        dst_path.parent.mkdir(parents=True, exist_ok=True)

        loop = asyncio.get_event_loop()
        await loop.run_in_executor(None, shutil.move, src_path, dst_path)
