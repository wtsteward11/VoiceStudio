"""
Content-addressed audio cache for VoiceStudio backend.

This service provides deduplication of audio artifacts by storing files keyed by
SHA-256 content hash. When saving audio to projects, identical files are
deduplicated by referencing the cached copy instead of copying duplicates.

Cache structure:
  <cache_root>/audio/<hash_prefix>/<hash>.wav

Where <hash_prefix> is the first 2 characters of the hash for directory sharding.
"""

from __future__ import annotations

import hashlib
import logging
import os
import shutil
import threading
from pathlib import Path

logger = logging.getLogger(__name__)

ENV_CACHE_DIR = "VOICESTUDIO_CACHE_DIR"
CACHE_SUBDIR = "audio"
HASH_PREFIX_LENGTH = 2  # First 2 hex chars for directory sharding


class ContentAddressedAudioCache:
    """
    Content-addressed cache for audio files.

    - Stores audio files by SHA-256 hash: `<cache_root>/audio/<prefix>/<hash>.wav`
    - Deduplicates identical audio across projects
    - Thread-safe operations
    """

    def __init__(self, cache_dir: str | None = None):
        """
        Initialize content-addressed audio cache.

        Args:
            cache_dir: Optional override for cache root directory
        """
        self.cache_dir = self._resolve_cache_dir(cache_dir)
        self.audio_cache_dir = self.cache_dir / CACHE_SUBDIR
        self._lock = threading.RLock()
        self.audio_cache_dir.mkdir(parents=True, exist_ok=True)

    @staticmethod
    def _resolve_cache_dir(cache_dir: str | None) -> Path:
        if cache_dir:
            return Path(cache_dir)

        env_dir = os.getenv(ENV_CACHE_DIR)
        if env_dir:
            return Path(env_dir)

        return Path.home() / ".voicestudio" / "cache"

    def _compute_hash(self, file_path: Path, chunk_size: int = 8192) -> str:
        """
        Compute SHA-256 hash of file content.

        Args:
            file_path: Path to audio file
            chunk_size: Chunk size for reading

        Returns:
            Hexadecimal hash string
        """
        hash_obj = hashlib.sha256()
        with open(file_path, "rb") as f:
            while True:
                chunk = f.read(chunk_size)
                if not chunk:
                    break
                hash_obj.update(chunk)
        return hash_obj.hexdigest()

    def _hash_to_path(self, hash_value: str) -> Path:
        """
        Convert hash to cache file path with directory sharding.

        Args:
            hash_value: Hexadecimal hash string

        Returns:
            Path to cached file
        """
        prefix = hash_value[:HASH_PREFIX_LENGTH]
        return self.audio_cache_dir / prefix / f"{hash_value}.wav"

    def get_or_store(self, source_path: Path) -> tuple[Path, str]:
        """
        Get cached file path for content, or store if not cached.

        Computes hash of source file. If a cached file with that hash exists,
        returns the cached path. Otherwise, copies source to cache and returns
        the new cached path.

        Args:
            source_path: Path to source audio file

        Returns:
            Tuple of (cached_file_path, hash_value)
        """
        if not source_path.exists():
            raise FileNotFoundError(f"Source file not found: {source_path}")

        hash_value = self._compute_hash(source_path)
        cached_path = self._hash_to_path(hash_value)

        with self._lock:
            if cached_path.exists():
                logger.debug(
                    f"Cache hit for hash {hash_value[:16]}... (source: {source_path.name})"
                )
                return cached_path, hash_value

            cached_path.parent.mkdir(parents=True, exist_ok=True)
            try:
                shutil.copy2(source_path, cached_path)
                logger.debug(
                    f"Cached audio with hash {hash_value[:16]}... (source: {source_path.name})"
                )
            except Exception as e:
                logger.error(f"Failed to cache audio file {source_path}: {e}")
                raise

        return cached_path, hash_value

    def get_by_hash(self, hash_value: str) -> Path | None:
        """
        Get cached file path by hash, if it exists.

        Args:
            hash_value: Hexadecimal hash string

        Returns:
            Path to cached file, or None if not found
        """
        cached_path = self._hash_to_path(hash_value)
        if cached_path.exists():
            return cached_path
        return None

    def ensure_cached(self, source_path: Path) -> Path:
        """
        Ensure source file is in cache; return cached path.

        Convenience method that calls get_or_store and returns only the path.

        Args:
            source_path: Path to source audio file

        Returns:
            Path to cached file
        """
        cached_path, _ = self.get_or_store(source_path)
        return cached_path


_service_instance: ContentAddressedAudioCache | None = None


def get_audio_cache(cache_dir: str | None = None) -> ContentAddressedAudioCache:
    """
    Get global ContentAddressedAudioCache instance.

    Args:
        cache_dir: Optional override for cache root directory
    """
    global _service_instance
    if _service_instance is None:
        _service_instance = ContentAddressedAudioCache(cache_dir=cache_dir)
    return _service_instance


def reset_audio_cache() -> None:
    """Reset the global ContentAddressedAudioCache instance (used for test isolation)."""
    global _service_instance
    _service_instance = None
