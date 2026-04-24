# Copyright (c) VoiceStudio. All rights reserved.
# Licensed under the MIT License.

"""
Centralized configuration for VoiceStudio backend.

This module provides a single source of truth for all configuration values,
replacing scattered env var reads across the codebase.

Usage:
    from backend.core.settings import settings

    models_path = settings.models_path
    port = settings.backend_port
"""

from __future__ import annotations

import os
from functools import lru_cache
from pathlib import Path

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class VoiceStudioSettings(BaseSettings):
    """
    VoiceStudio backend configuration.

    All settings can be overridden via environment variables with VOICESTUDIO_ prefix.
    Example: VOICESTUDIO_BACKEND_PORT=8001
    """

    # Server settings
    backend_host: str = Field(default="127.0.0.1", description="Backend server host")
    backend_port: int = Field(default=8000, description="Backend server port")
    debug: bool = Field(default=False, description="Enable debug mode")
    log_level: str = Field(default="INFO", description="Logging level")

    # Model paths
    models_path: str = Field(
        default_factory=lambda: os.environ.get(
            "VOICESTUDIO_MODELS_PATH",
            str(Path(os.environ.get("PROGRAMDATA", "C:/ProgramData")) / "VoiceStudio" / "models"),
        ),
        description="Root path for all model files",
    )

    # Cache settings
    hf_home: str | None = Field(default=None, description="HuggingFace cache directory")
    torch_home: str | None = Field(default=None, description="PyTorch cache directory")

    # Resource limits
    vram_limit_mb: int = Field(default=8192, description="Maximum VRAM usage in MB")
    max_concurrent_engines: int = Field(
        default=3, description="Maximum concurrent engine instances"
    )
    engine_idle_timeout_seconds: int = Field(
        default=300, description="Idle timeout before engine cleanup"
    )

    # Rate limiting
    rate_limit_enabled: bool = Field(default=True, description="Enable API rate limiting")
    rate_limit_requests_per_minute: int = Field(default=60, description="Requests per minute limit")
    rate_limit_localhost_exempt: bool = Field(
        default=True,
        description="Exempt 127.0.0.1/localhost from rate limits (desktop dev mode)",
    )

    # Telemetry (opt-in only)
    telemetry_enabled: bool = Field(default=False, description="Enable telemetry (opt-in)")

    # Feature flags
    enable_experimental_engines: bool = Field(
        default=False, description="Enable experimental engines"
    )
    enable_gpu_acceleration: bool = Field(default=True, description="Enable GPU acceleration")

    # API settings
    api_version: str = Field(default="v1", description="Current API version")
    max_request_size_mb: int = Field(default=100, description="Maximum request body size in MB")

    # CORS settings
    cors_allowed_origins: str = Field(
        default="*", description="Comma-separated list of allowed CORS origins"
    )

    model_config = SettingsConfigDict(
        env_prefix="VOICESTUDIO_",
        env_file=".env",
        env_file_encoding="utf-8",
        case_sensitive=False,
    )

    @property
    def hf_cache_path(self) -> Path:
        """Get HuggingFace cache path."""
        if self.hf_home:
            return Path(self.hf_home)
        return Path(self.models_path) / "hf_cache"

    @property
    def torch_cache_path(self) -> Path:
        """Get PyTorch cache path."""
        if self.torch_home:
            return Path(self.torch_home)
        return Path(self.models_path) / "torch"

    @property
    def cors_origins_list(self) -> list[str]:
        """Get CORS origins as a list."""
        if self.cors_allowed_origins == "*":
            return ["*"]
        return [origin.strip() for origin in self.cors_allowed_origins.split(",")]

    def ensure_directories(self) -> None:
        """Create required directories if they don't exist."""
        directories = [
            Path(self.models_path),
            self.hf_cache_path,
            self.torch_cache_path,
            Path(self.models_path) / "xtts",
            Path(self.models_path) / "piper",
            Path(self.models_path) / "whisper",
        ]
        for directory in directories:
            directory.mkdir(parents=True, exist_ok=True)


@lru_cache
def get_settings() -> VoiceStudioSettings:
    """
    Get cached settings instance.

    Returns:
        VoiceStudioSettings instance (cached for performance)
    """
    return VoiceStudioSettings()


# Default settings instance
settings = get_settings()
