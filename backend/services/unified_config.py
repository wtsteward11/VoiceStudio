"""
Unified Configuration Service for VoiceStudio.

Provides centralized access to all configuration files with:
- YAML file loading with environment variable expansion
- Schema validation
- Caching and hot-reload support
- Type-safe configuration access

Consolidates settings from:
- config/voicestudio.config.yaml (app settings)
- config/engines.config.yaml (engine routing)
- config/deployment.config.yaml (deployment settings)
"""

from __future__ import annotations

import json
import logging
import os
import re
import threading
from dataclasses import dataclass, field
from functools import lru_cache
from pathlib import Path
from typing import Any, TypeVar

try:
    import yaml
except ImportError:
    yaml = None  # type: ignore

logger = logging.getLogger(__name__)

T = TypeVar("T")


# ==============================================================================
# Configuration Data Classes
# ==============================================================================


@dataclass
class PathsConfig:
    """Path configuration."""

    data_root: str = "data"
    models_root: str = "models"
    cache_root: str = "cache"
    logs_root: str = "logs"
    plugins_root: str = "plugins"


@dataclass
class GeneralConfig:
    """General application settings."""

    theme: str = "Dark"
    language: str = "en-US"
    auto_save: bool = True
    auto_save_interval: int = 300


@dataclass
class AudioConfig:
    """Audio device settings."""

    output_device: str = "Default"
    input_device: str = "Default"
    sample_rate: int = 44100
    buffer_size: int = 1024


@dataclass
class PerformanceConfig:
    """Performance tuning settings."""

    caching_enabled: bool = True
    cache_size_mb: int = 512
    max_threads: int = 4
    memory_limit_mb: int = 4096


@dataclass
class QualityConfig:
    """Quality settings."""

    default_preset: str = "standard"
    auto_enhance: bool = True
    min_mos_score: float = 3.5
    min_similarity: float = 0.75
    show_metrics: bool = True


@dataclass
class VoiceStudioConfig:
    """Main application configuration."""

    version: str = "1.0.0"
    paths: PathsConfig = field(default_factory=PathsConfig)
    general: GeneralConfig = field(default_factory=GeneralConfig)
    audio: AudioConfig = field(default_factory=AudioConfig)
    performance: PerformanceConfig = field(default_factory=PerformanceConfig)
    quality: QualityConfig = field(default_factory=QualityConfig)
    feature_flags: dict[str, bool] = field(default_factory=dict)


@dataclass
class RoutingPolicy:
    """Engine routing policy."""

    language_mapping: dict[str, str] = field(default_factory=dict)
    fallback_chains: dict[str, list[str]] = field(default_factory=dict)
    quality_tiers: dict[str, dict[str, Any]] = field(default_factory=dict)


@dataclass
class ABExperiment:
    """A/B testing experiment."""

    id: str
    description: str = ""
    engines: list[str] = field(default_factory=list)
    weights: list[float] = field(default_factory=list)
    active: bool = False
    metrics: list[str] = field(default_factory=list)


@dataclass
class ABTestingConfig:
    """A/B testing configuration."""

    enabled: bool = False
    experiments: list[ABExperiment] = field(default_factory=list)


@dataclass
class GPUSettings:
    """GPU and hardware settings."""

    enabled: bool = True
    device: str = "cuda"
    fallback_to_cpu: bool = True
    memory_fraction: float = 0.9
    parallel_engine_limit: int = 2


@dataclass
class EnginesConfig:
    """Engine configuration."""

    version: str = "1.0.0"
    defaults: dict[str, str] = field(default_factory=dict)
    routing_policy: RoutingPolicy = field(default_factory=RoutingPolicy)
    ab_testing: ABTestingConfig = field(default_factory=ABTestingConfig)
    gpu_settings: GPUSettings = field(default_factory=GPUSettings)
    global_settings: dict[str, Any] = field(default_factory=dict)
    engine_overrides: dict[str, dict[str, Any]] = field(default_factory=dict)


@dataclass
class BackendConfig:
    """Backend service configuration."""

    host: str = "127.0.0.1"
    port: int = 8000
    workers: int = 4
    timeout: int = 30
    retry_count: int = 3


@dataclass
class LoggingConfig:
    """Logging configuration."""

    level: str = "INFO"
    format: str = "text"
    file_enabled: bool = True
    file_path: str = "logs/voicestudio.log"


@dataclass
class TelemetryConfig:
    """Telemetry configuration."""

    enabled: bool = False
    prometheus_enabled: bool = False
    metrics_port: int = 9090


@dataclass
class DeploymentConfig:
    """Deployment configuration."""

    version: str = "1.0.0"
    environment: str = "development"
    backend: BackendConfig = field(default_factory=BackendConfig)
    logging: LoggingConfig = field(default_factory=LoggingConfig)
    telemetry: TelemetryConfig = field(default_factory=TelemetryConfig)


# ==============================================================================
# Environment Variable Expansion
# ==============================================================================


def expand_env_vars(value: str) -> str:
    """
    Expand environment variables in a string.

    Supports format: ${VAR_NAME:default_value}

    Args:
        value: String potentially containing env var references

    Returns:
        String with env vars expanded
    """
    if not isinstance(value, str):
        return value

    pattern = r"\$\{([^}:]+)(?::([^}]*))?\}"

    def replacer(match: re.Match) -> str:
        var_name = match.group(1)
        default = match.group(2) or ""
        return os.environ.get(var_name, default)

    return re.sub(pattern, replacer, value)


def expand_env_vars_recursive(obj: Any) -> Any:
    """Recursively expand environment variables in a nested structure."""
    if isinstance(obj, str):
        return expand_env_vars(obj)
    elif isinstance(obj, dict):
        return {k: expand_env_vars_recursive(v) for k, v in obj.items()}
    elif isinstance(obj, list):
        return [expand_env_vars_recursive(item) for item in obj]
    return obj


# ==============================================================================
# Configuration Loader
# ==============================================================================


class ConfigLoader:
    """Loads and parses configuration files."""

    def __init__(self, config_dir: Path | None = None):
        """
        Initialize the config loader.

        Args:
            config_dir: Path to config directory. Defaults to project's config/ folder.
        """
        if config_dir is None:
            # Find project root (look for VoiceStudio.sln or pyproject.toml)
            current = Path(__file__).resolve()
            for parent in current.parents:
                if (parent / "VoiceStudio.sln").exists() or (parent / "pyproject.toml").exists():
                    config_dir = parent / "config"
                    break
            else:
                config_dir = Path("config")

        self.config_dir = Path(config_dir)
        self._cache: dict[str, Any] = {}
        self._lock = threading.Lock()

    def _load_yaml(self, filename: str) -> dict[str, Any]:
        """Load a YAML file with environment variable expansion."""
        if yaml is None:
            raise ImportError(
                "PyYAML is required for YAML configuration. Install with: pip install pyyaml"
            )

        filepath = self.config_dir / filename
        if not filepath.exists():
            logger.warning(f"Config file not found: {filepath}")
            return {}

        try:
            with open(filepath, encoding="utf-8") as f:
                content = yaml.safe_load(f)

            if content is None:
                return {}

            # Expand environment variables
            return expand_env_vars_recursive(content)

        except yaml.YAMLError as e:
            logger.error(f"Error parsing YAML file {filepath}: {e}")
            raise

    def _load_json(self, filepath: Path) -> dict[str, Any]:
        """Load a JSON file with environment variable expansion."""
        if not filepath.exists():
            logger.warning(f"Config file not found: {filepath}")
            return {}

        try:
            with open(filepath, encoding="utf-8") as f:
                content = json.load(f)

            return expand_env_vars_recursive(content)

        except json.JSONDecodeError as e:
            logger.error(f"Error parsing JSON file {filepath}: {e}")
            raise

    def load_voicestudio_config(self, use_cache: bool = True) -> dict[str, Any]:
        """Load the main VoiceStudio configuration."""
        cache_key = "voicestudio"

        with self._lock:
            if use_cache and cache_key in self._cache:
                return self._cache[cache_key]

            config = self._load_yaml("voicestudio.config.yaml")
            self._cache[cache_key] = config
            return config

    def load_engines_config(self, use_cache: bool = True) -> dict[str, Any]:
        """Load the engines configuration."""
        cache_key = "engines"

        with self._lock:
            if use_cache and cache_key in self._cache:
                return self._cache[cache_key]

            config = self._load_yaml("engines.config.yaml")
            self._cache[cache_key] = config
            return config

    def load_deployment_config(self, use_cache: bool = True) -> dict[str, Any]:
        """Load the deployment configuration."""
        cache_key = "deployment"

        with self._lock:
            if use_cache and cache_key in self._cache:
                return self._cache[cache_key]

            config = self._load_yaml("deployment.config.yaml")
            self._cache[cache_key] = config
            return config

    def reload_all(self) -> None:
        """Force reload all configuration files."""
        with self._lock:
            self._cache.clear()

        self.load_voicestudio_config(use_cache=False)
        self.load_engines_config(use_cache=False)
        self.load_deployment_config(use_cache=False)

        logger.info("All configuration files reloaded")


# ==============================================================================
# Unified Configuration Service
# ==============================================================================


class UnifiedConfigService:
    """
    Unified configuration service providing type-safe access to all settings.

    Usage:
        config = UnifiedConfigService()

        # Access main config
        theme = config.voicestudio.general.theme

        # Access engine config
        default_tts = config.engines.defaults.get("tts")

        # Access deployment config
        port = config.deployment.backend.port

        # Get feature flag
        if config.is_feature_enabled("ab_testing"):
            ...

        # Get engine for language
        engine = config.get_engine_for_language("zh", "tts")

        # Get fallback chain
        chain = config.get_fallback_chain("tts")
    """

    _instance: UnifiedConfigService | None = None
    _lock = threading.Lock()

    def __new__(cls, config_dir: Path | None = None) -> UnifiedConfigService:
        """Singleton pattern for the config service."""
        with cls._lock:
            if cls._instance is None:
                cls._instance = super().__new__(cls)
                cls._instance._initialized = False
            return cls._instance

    def __init__(self, config_dir: Path | None = None):
        """Initialize the unified config service."""
        if getattr(self, "_initialized", False):
            return

        self._loader = ConfigLoader(config_dir)
        self._voicestudio: VoiceStudioConfig | None = None
        self._engines: EnginesConfig | None = None
        self._deployment: DeploymentConfig | None = None

        self._load_configs()
        self._initialized = True

    def _load_configs(self) -> None:
        """Load all configuration files into typed dataclasses."""
        self._voicestudio = self._parse_voicestudio_config(self._loader.load_voicestudio_config())
        self._engines = self._parse_engines_config(self._loader.load_engines_config())
        self._deployment = self._parse_deployment_config(self._loader.load_deployment_config())

    def _parse_voicestudio_config(self, data: dict[str, Any]) -> VoiceStudioConfig:
        """Parse raw dict into VoiceStudioConfig."""
        if not data:
            return VoiceStudioConfig()

        return VoiceStudioConfig(
            version=data.get("version", "1.0.0"),
            paths=PathsConfig(**data.get("paths", {})),
            general=GeneralConfig(**data.get("general", {})),
            audio=AudioConfig(**data.get("audio", {})),
            performance=PerformanceConfig(**data.get("performance", {})),
            quality=QualityConfig(
                default_preset=data.get("quality", {}).get("default_preset", "standard"),
                auto_enhance=data.get("quality", {}).get("auto_enhance", True),
                min_mos_score=data.get("quality", {}).get("min_mos_score", 3.5),
                min_similarity=data.get("quality", {}).get("min_similarity", 0.75),
                show_metrics=data.get("quality", {}).get("show_metrics", True),
            ),
            feature_flags=data.get("feature_flags", {}),
        )

    def _parse_engines_config(self, data: dict[str, Any]) -> EnginesConfig:
        """Parse raw dict into EnginesConfig."""
        if not data:
            return EnginesConfig()

        routing_data = data.get("routing_policy", {})
        routing = RoutingPolicy(
            language_mapping=routing_data.get("language_mapping", {}),
            fallback_chains=routing_data.get("fallback_chains", {}),
            quality_tiers=routing_data.get("quality_tiers", {}),
        )

        ab_data = data.get("ab_testing", {})
        experiments = [
            ABExperiment(
                id=exp.get("id", ""),
                description=exp.get("description", ""),
                engines=exp.get("engines", []),
                weights=exp.get("weights", []),
                active=exp.get("active", False),
                metrics=exp.get("metrics", []),
            )
            for exp in ab_data.get("experiments", [])
        ]
        ab_testing = ABTestingConfig(
            enabled=ab_data.get("enabled", False),
            experiments=experiments,
        )

        gpu_data = data.get("gpu_settings", {})
        gpu_settings = GPUSettings(
            enabled=gpu_data.get("enabled", True),
            device=gpu_data.get("device", "cuda"),
            fallback_to_cpu=gpu_data.get("fallback_to_cpu", True),
            memory_fraction=gpu_data.get("memory_fraction", 0.9),
            parallel_engine_limit=gpu_data.get("parallel_engine_limit", 2),
        )

        return EnginesConfig(
            version=data.get("version", "1.0.0"),
            defaults=data.get("defaults", {}),
            routing_policy=routing,
            ab_testing=ab_testing,
            gpu_settings=gpu_settings,
            global_settings=data.get("global_settings", {}),
            engine_overrides=data.get("engine_overrides", {}),
        )

    def _parse_deployment_config(self, data: dict[str, Any]) -> DeploymentConfig:
        """Parse raw dict into DeploymentConfig."""
        if not data:
            return DeploymentConfig()

        backend_data = data.get("backend", {})
        backend = BackendConfig(
            host=backend_data.get("host", "127.0.0.1"),
            port=backend_data.get("port", 8000),
            workers=backend_data.get("workers", 4),
            timeout=backend_data.get("timeout", 30),
            retry_count=backend_data.get("retry_count", 3),
        )

        logging_data = data.get("logging", {})
        logging_config = LoggingConfig(
            level=logging_data.get("level", "INFO"),
            format=logging_data.get("format", "text"),
            file_enabled=logging_data.get("file", {}).get("enabled", True),
            file_path=logging_data.get("file", {}).get("path", "logs/voicestudio.log"),
        )

        telemetry_data = data.get("telemetry", {})
        telemetry = TelemetryConfig(
            enabled=telemetry_data.get("enabled", False),
            prometheus_enabled=telemetry_data.get("prometheus", {}).get("enabled", False),
            metrics_port=telemetry_data.get("prometheus", {}).get("port", 9090),
        )

        return DeploymentConfig(
            version=data.get("version", "1.0.0"),
            environment=data.get("environment", "development"),
            backend=backend,
            logging=logging_config,
            telemetry=telemetry,
        )

    @property
    def voicestudio(self) -> VoiceStudioConfig:
        """Get the main VoiceStudio configuration."""
        if self._voicestudio is None:
            self._load_configs()
        return self._voicestudio  # type: ignore

    @property
    def engines(self) -> EnginesConfig:
        """Get the engines configuration."""
        if self._engines is None:
            self._load_configs()
        return self._engines  # type: ignore

    @property
    def deployment(self) -> DeploymentConfig:
        """Get the deployment configuration."""
        if self._deployment is None:
            self._load_configs()
        return self._deployment  # type: ignore

    def reload(self) -> None:
        """Reload all configuration files."""
        self._loader.reload_all()
        self._load_configs()
        logger.info("UnifiedConfigService: Configuration reloaded")

    def is_feature_enabled(self, feature_name: str) -> bool:
        """Check if a feature flag is enabled."""
        return self.voicestudio.feature_flags.get(feature_name, False)

    def get_default_engine(self, task_type: str) -> str | None:
        """Get the default engine for a task type."""
        return self.engines.defaults.get(task_type)

    def get_engine_for_language(self, language: str, task_type: str = "tts") -> str:
        """
        Get the recommended engine for a specific language.

        Args:
            language: ISO language code (e.g., "en", "zh", "ja")
            task_type: Type of task (e.g., "tts", "stt")

        Returns:
            Engine ID for the language, or default engine if not mapped
        """
        # Check language mapping
        engine = self.engines.routing_policy.language_mapping.get(language)
        if engine:
            return engine

        # Fall back to default for task type
        return self.engines.defaults.get(task_type, "xtts_v2")

    def get_fallback_chain(self, task_type: str) -> list[str]:
        """Get the fallback chain for a task type."""
        return self.engines.routing_policy.fallback_chains.get(task_type, [])

    def get_quality_tier_engine(self, tier: str, task_type: str = "tts") -> str | None:
        """Get the engine for a specific quality tier."""
        tier_config = self.engines.routing_policy.quality_tiers.get(tier, {})
        return tier_config.get(task_type)

    def get_engine_override(self, engine_id: str) -> dict[str, Any]:
        """Get per-engine configuration overrides."""
        return self.engines.engine_overrides.get(engine_id, {})

    def is_engine_enabled(self, engine_id: str) -> bool:
        """Check if an engine is enabled."""
        override = self.get_engine_override(engine_id)
        return override.get("enabled", True)

    def get_active_ab_experiments(self) -> list[ABExperiment]:
        """Get all active A/B testing experiments."""
        if not self.engines.ab_testing.enabled:
            return []
        return [exp for exp in self.engines.ab_testing.experiments if exp.active]

    def is_development(self) -> bool:
        """Check if running in development mode."""
        return self.deployment.environment == "development"

    def is_production(self) -> bool:
        """Check if running in production mode."""
        return self.deployment.environment == "production"


# ==============================================================================
# Module-level convenience functions
# ==============================================================================


@lru_cache(maxsize=1)
def get_config() -> UnifiedConfigService:
    """Get the singleton configuration service instance."""
    return UnifiedConfigService()


def reload_config() -> None:
    """Reload all configuration files."""
    get_config().reload()
    get_config.cache_clear()


# For backwards compatibility with existing code
def get_engine_config() -> dict[str, Any]:
    """Get engine configuration as a dict (legacy compatibility)."""
    return get_config()._loader.load_engines_config()


def get_app_settings() -> dict[str, Any]:
    """Get app settings as a dict (legacy compatibility)."""
    return get_config()._loader.load_voicestudio_config()
