"""
Audio download service for URL-to-file caching.

Downloads audio from URLs to temp directory. Writes happen in this service
(not in routes) for artifact spine compliance.
"""

from __future__ import annotations

import hashlib
import logging
from pathlib import Path
from urllib.parse import urlparse

from backend.config.path_config import get_path

logger = logging.getLogger(__name__)

HAS_HTTPX = False
try:
    import httpx

    HAS_HTTPX = True
except ImportError:
    pass


async def download_audio_to_temp(url: str, timeout: float = 30.0) -> Path | None:
    """
    Download a file from URL and cache it under get_path("temp").

    Args:
        url: The URL to download from
        timeout: Download timeout in seconds

    Returns:
        Path to the downloaded file, or None if download failed
    """
    if not HAS_HTTPX:
        logger.warning("httpx not available for URL downloads")
        return None

    try:
        base_dir = get_path("temp") / "voicestudio_url_cache"
        base_dir.mkdir(parents=True, exist_ok=True)

        url_hash = hashlib.md5(url.encode()).hexdigest()[:16]
        parsed = urlparse(url)
        ext = Path(parsed.path).suffix or ".wav"
        cache_path = base_dir / f"{url_hash}{ext}"

        if cache_path.exists():
            logger.debug("Using cached file for %s: %s", url, cache_path)
            return cache_path

        async with httpx.AsyncClient(timeout=timeout) as client:
            response = await client.get(url, follow_redirects=True)
            response.raise_for_status()

            content_type = response.headers.get("content-type", "")
            if not any(
                t in content_type.lower()
                for t in ["audio", "octet-stream", "wav", "mp3", "flac"]
            ):
                logger.warning("Unexpected content type for audio URL: %s", content_type)

            with open(cache_path, "wb") as f:
                f.write(response.content)

            logger.info("Downloaded %d bytes from %s to %s", len(response.content), url, cache_path)
            return cache_path

    except Exception as e:
        logger.error("Failed to download URL %s: %s", url, e)
        return None
