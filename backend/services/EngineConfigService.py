"""
Centralized Engine Configuration Management Service

Provides comprehensive engine configuration management including:
- Model paths
- GPU settings
- Default parameters
- Configuration validation
- Engine-specific settings

Supports both:
- UnifiedConfigService (preferred, YAML-based)
- Legacy JSON config (fallback for migration)
"""

from __future__ import annotations

import json
import logging
import os
from pathlib import Path
from typing import Any

from backend.config.path_config import get_models_path

# Try importing UnifiedConfigService
try:
    from backend.services.unified_config import UnifiedConfigService, get_config

    HAS_UNIFIED_CONFIG = True
except ImportError:
    HAS_UNIFIED_CONFIG = False
    get_config = None
    UnifiedConfigService = None

logger = logging.getLogger(__name__)


class EngineConfigService:
    """
    Centralized service for managing engine configurations.

    Handles:
    - Engine defaults and overrides
    - Model paths and storage
    - GPU settings and device configuration
    - Default parameters per engine
    - Configuration validation
    """

    def __init__(self, config_path: str = "backend/config/engine_config.json"):
        """
        Initialize engine configuration service.

        Args:
            config_path: Path to engine configuration file
        """
        self.config_path = Path(config_path)
        self.config: dict[str, Any] = {}
        self.load()

    def load(self):
        """Load configuration from UnifiedConfigService or legacy JSON file."""
        # Try UnifiedConfigService first (new YAML-based config)
        if HAS_UNIFIED_CONFIG:
            try:
                unified = get_config()
                if unified and unified.engines:
                    self.config = self._load_from_unified_config(unified)
                    logger.info("Loaded engine configuration from UnifiedConfigService")
                    return
            except Exception as e:
                logger.debug(f"UnifiedConfigService not available, falling back to JSON: {e}")

        # Fallback to legacy JSON file
        if self.config_path.exists():
            try:
                with open(self.config_path, encoding="utf-8") as f:
                    self.config = json.load(f)
                logger.info(f"Loaded engine configuration from {self.config_path}")
            except Exception as e:
                logger.error(f"Failed to load engine configuration: {e}")
                self.config = self._get_default_config()
                self.save()
        else:
            logger.info(f"Configuration file not found, creating default: {self.config_path}")
            self.config = self._get_default_config()
            self.save()

    def _load_from_unified_config(self, unified) -> dict[str, Any]:
        """Transform UnifiedConfigService.engines to legacy config format."""
        engines_config = unified.engines

        return {
            "defaults": engines_config.defaults if hasattr(engines_config, "defaults") else {},
            "overrides": {},
            "installed": engines_config.installed if hasattr(engines_config, "installed") else [],
            "model_paths": {
                "base": (
                    engines_config.model_paths.base
                    if hasattr(engines_config, "model_paths")
                    else "models"
                ),
                "engines": (
                    engines_config.model_paths.engines
                    if hasattr(engines_config, "model_paths")
                    else {}
                ),
            },
            "gpu_settings": {
                "enabled": (
                    engines_config.gpu_settings.enabled
                    if hasattr(engines_config, "gpu_settings")
                    else True
                ),
                "device": (
                    engines_config.gpu_settings.device
                    if hasattr(engines_config, "gpu_settings")
                    else "cuda"
                ),
                "fallback_to_cpu": (
                    engines_config.gpu_settings.fallback_to_cpu
                    if hasattr(engines_config, "gpu_settings")
                    else True
                ),
                "memory_fraction": (
                    engines_config.gpu_settings.memory_fraction
                    if hasattr(engines_config, "gpu_settings")
                    else 0.9
                ),
            },
            "engine_configs": (
                engines_config.engine_configs if hasattr(engines_config, "engine_configs") else {}
            ),
            "global_settings": {
                "auto_download_models": True,
                "model_cache_enabled": True,
                "parallel_engine_limit": 2,
            },
        }

    def save(self):
        """Save configuration to file atomically (tmp + replace)."""
        try:
            self.config_path.parent.mkdir(parents=True, exist_ok=True)
            tmp_path = self.config_path.with_suffix(self.config_path.suffix + ".tmp")
            with open(tmp_path, "w", encoding="utf-8") as f:
                json.dump(self.config, f, indent=2, ensure_ascii=False)
            os.replace(tmp_path, self.config_path)
            logger.debug(f"Saved engine configuration to {self.config_path}")
        except Exception as e:
            logger.error(f"Failed to save engine configuration: {e}")
            try:
                if tmp_path.exists():
                    tmp_path.unlink()
            # ALLOWED: bare except - Best effort cleanup, failure is acceptable
            except Exception as cleanup_e:
                logger.debug(f"Cleanup of temp file failed (non-critical): {cleanup_e}")

    def _get_default_config(self) -> dict[str, Any]:
        """Get default configuration structure."""
        # Get default models path
        env_models_root = os.getenv("VOICESTUDIO_MODELS_PATH")
        models_base = Path(env_models_root) if env_models_root else Path(get_models_path())

        return {
            "defaults": {
                "tts": "xtts_v2",
                "image_gen": "sdxl_comfy",
                "video_gen": "svd",
                "stt": "whisper_cpp",
            },
            "overrides": {},
            "installed": [],
            "model_paths": {"base": str(models_base), "engines": {}},
            "gpu_settings": {
                "enabled": True,
                "device": "cuda",
                "fallback_to_cpu": True,
                "memory_fraction": 0.9,
            },
            "engine_configs": {},
            "global_settings": {
                "auto_download_models": True,
                "model_cache_enabled": True,
                "parallel_engine_limit": 2,
            },
        }

    def get_default_engine(self, task_type: str) -> str | None:
        """
        Get default engine for a task type.

        Args:
            task_type: Task type (e.g., "tts", "image_gen", "video_gen")

        Returns:
            Engine ID or None
        """
        # Check overrides first
        overrides = self.config.get("overrides", {})
        if task_type in overrides:
            return overrides[task_type]

        # Check defaults
        defaults = self.config.get("defaults", {})
        return defaults.get(task_type)

    def set_default_engine(self, task_type: str, engine_id: str):
        """
        Set default engine for a task type.

        Args:
            task_type: Task type
            engine_id: Engine ID
        """
        if "defaults" not in self.config:
            self.config["defaults"] = {}

        self.config["defaults"][task_type] = engine_id
        self.save()
        logger.info(f"Set default engine for {task_type}: {engine_id}")

    def get_model_path(self, engine_id: str, path_type: str = "base") -> str:
        """
        Get model path for an engine.

        Args:
            engine_id: Engine ID
            path_type: Path type ("base", "cache", "checkpoints", etc.)

        Returns:
            Model path string
        """
        engine_configs = self.config.get("engine_configs", {})
        engine_config = engine_configs.get(engine_id, {})

        # Check engine-specific model path
        if "model_paths" in engine_config and path_type in engine_config["model_paths"]:
            path = engine_config["model_paths"][path_type]
            # Expand environment variables
            return os.path.expandvars(path)

        # Use default model path structure
        base_path = self.config.get("model_paths", {}).get("base", "E:\\VoiceStudio\\models")
        base_path = os.path.expandvars(base_path)
        return str(Path(base_path) / engine_id)

    def set_model_path(self, engine_id: str, path_type: str, path: str):
        """
        Set model path for an engine.

        Args:
            engine_id: Engine ID
            path_type: Path type ("base", "cache", etc.)
            path: Model path
        """
        if "engine_configs" not in self.config:
            self.config["engine_configs"] = {}

        if engine_id not in self.config["engine_configs"]:
            self.config["engine_configs"][engine_id] = {}

        if "model_paths" not in self.config["engine_configs"][engine_id]:
            self.config["engine_configs"][engine_id]["model_paths"] = {}

        self.config["engine_configs"][engine_id]["model_paths"][path_type] = path
        self.save()
        logger.info(f"Set model path for {engine_id}.{path_type}: {path}")

    def get_gpu_settings(self) -> dict[str, Any]:
        """
        Get global GPU settings.

        Returns:
            GPU settings dictionary
        """
        return self.config.get(
            "gpu_settings",
            {
                "enabled": True,
                "device": "cuda",
                "fallback_to_cpu": True,
                "memory_fraction": 0.9,
            },
        ).copy()

    def set_gpu_settings(self, settings: dict[str, Any]):
        """
        Set global GPU settings.

        Args:
            settings: GPU settings dictionary
        """
        if "gpu_settings" not in self.config:
            self.config["gpu_settings"] = {}

        self.config["gpu_settings"].update(settings)
        self.save()
        logger.info(f"Updated GPU settings: {settings}")

    def get_engine_config(self, engine_id: str) -> dict[str, Any]:
        """
        Get complete configuration for an engine.

        Args:
            engine_id: Engine ID

        Returns:
            Engine configuration dictionary
        """
        engine_configs = self.config.get("engine_configs", {})
        return engine_configs.get(engine_id, {}).copy()

    def get_engine_init_kwargs(
        self, engine_id: str, manifest: dict[str, Any] | None = None
    ) -> dict[str, Any]:
        """
        Build engine initialization kwargs from config and manifest schema.

        Args:
            engine_id: Engine ID
            manifest: Optional engine manifest dictionary

        Returns:
            Dictionary of kwargs to pass into engine constructors
        """
        engine_config = self.get_engine_config(engine_id)
        raw_params = engine_config.get("parameters", {})
        if not isinstance(raw_params, dict):
            raw_params = {}

        def _expand(value: Any) -> Any:
            if isinstance(value, str):
                return os.path.expandvars(value)
            return value

        params = {key: _expand(value) for key, value in raw_params.items()}

        schema = None
        if isinstance(manifest, dict):
            schema = manifest.get("config_schema")

        schema_keys = None
        if isinstance(schema, dict) and schema:
            schema_keys = set(schema.keys())
            params = {key: value for key, value in params.items() if key in schema_keys}

        gpu_settings = self.get_gpu_settings()
        if (schema_keys is None or "device" in schema_keys) and "device" not in params:
            params["device"] = (
                gpu_settings.get("device", "cuda") if gpu_settings.get("enabled", True) else "cpu"
            )
        if (schema_keys is None or "gpu" in schema_keys) and "gpu" not in params:
            params["gpu"] = bool(gpu_settings.get("enabled", True))

        return params

    def set_engine_config(self, engine_id: str, config: dict[str, Any]):
        """
        Set configuration for an engine.

        Args:
            engine_id: Engine ID
            config: Configuration dictionary
        """
        if "engine_configs" not in self.config:
            self.config["engine_configs"] = {}

        if engine_id not in self.config["engine_configs"]:
            self.config["engine_configs"][engine_id] = {}

        self.config["engine_configs"][engine_id].update(config)
        self.save()
        logger.info(f"Updated configuration for engine {engine_id}")

    def get_engine_parameter(self, engine_id: str, parameter: str, default: Any = None) -> Any:
        """
        Get a specific parameter for an engine.

        Args:
            engine_id: Engine ID
            parameter: Parameter name
            default: Default value if not found

        Returns:
            Parameter value or default
        """
        engine_config = self.get_engine_config(engine_id)
        return engine_config.get("parameters", {}).get(parameter, default)

    def set_engine_parameter(self, engine_id: str, parameter: str, value: Any):
        """
        Set a specific parameter for an engine.

        Args:
            engine_id: Engine ID
            parameter: Parameter name
            value: Parameter value
        """
        if "engine_configs" not in self.config:
            self.config["engine_configs"] = {}

        if engine_id not in self.config["engine_configs"]:
            self.config["engine_configs"][engine_id] = {}

        if "parameters" not in self.config["engine_configs"][engine_id]:
            self.config["engine_configs"][engine_id]["parameters"] = {}

        self.config["engine_configs"][engine_id]["parameters"][parameter] = value
        self.save()
        logger.info(f"Set parameter {engine_id}.{parameter} = {value}")

    def validate_config(self) -> tuple[bool, list[str]]:
        """
        Validate configuration.

        Returns:
            Tuple of (is_valid, list_of_errors)
        """
        errors = []

        # Validate defaults
        defaults = self.config.get("defaults", {})
        if not isinstance(defaults, dict):
            errors.append("defaults must be a dictionary")
        else:
            # Validate default engine IDs are strings
            for task_type, engine_id in defaults.items():
                if not isinstance(engine_id, str):
                    errors.append(f"Default engine for {task_type} must be a string")

        # Validate overrides
        overrides = self.config.get("overrides", {})
        if not isinstance(overrides, dict):
            errors.append("overrides must be a dictionary")

        # Validate installed engines
        installed = self.config.get("installed", [])
        if not isinstance(installed, list):
            errors.append("installed must be a list")
        else:
            for engine_id in installed:
                if not isinstance(engine_id, str):
                    errors.append(f"Installed engine ID must be a string: {engine_id}")

        # Validate model paths
        model_paths = self.config.get("model_paths", {})
        if not isinstance(model_paths, dict):
            errors.append("model_paths must be a dictionary")
        else:
            if "base" in model_paths:
                base_path = os.path.expandvars(str(model_paths["base"]))
                # Allow environment variables, but validate if expanded
                if not base_path.startswith("%") and not os.path.isabs(base_path):
                    errors.append(
                        f"Model base path must be absolute or use environment variables: {base_path}"
                    )

        # Validate GPU settings
        gpu_settings = self.config.get("gpu_settings", {})
        if not isinstance(gpu_settings, dict):
            errors.append("gpu_settings must be a dictionary")
        else:
            if "device" in gpu_settings:
                device = gpu_settings["device"]
                if device not in ["cuda", "cpu", "auto", "mps"]:
                    errors.append(
                        f"Invalid GPU device: {device}. Must be 'cuda', 'cpu', 'auto', or 'mps'"
                    )

            if "memory_fraction" in gpu_settings:
                fraction = gpu_settings["memory_fraction"]
                if not isinstance(fraction, (int, float)) or not (0.0 < fraction <= 1.0):
                    errors.append(f"GPU memory fraction must be between 0 and 1: {fraction}")

            if "enabled" in gpu_settings and not isinstance(gpu_settings["enabled"], bool):
                errors.append("GPU enabled must be a boolean")

        # Validate engine configs
        engine_configs = self.config.get("engine_configs", {})
        if not isinstance(engine_configs, dict):
            errors.append("engine_configs must be a dictionary")
        else:
            for engine_id, engine_config in engine_configs.items():
                if not isinstance(engine_config, dict):
                    errors.append(f"Engine config for {engine_id} must be a dictionary")
                else:
                    # Validate model_paths in engine config
                    if "model_paths" in engine_config:
                        if not isinstance(engine_config["model_paths"], dict):
                            errors.append(f"Engine {engine_id} model_paths must be a dictionary")

                    # Validate parameters in engine config
                    if "parameters" in engine_config:
                        if not isinstance(engine_config["parameters"], dict):
                            errors.append(f"Engine {engine_id} parameters must be a dictionary")

        # Validate global settings
        global_settings = self.config.get("global_settings", {})
        if not isinstance(global_settings, dict):
            errors.append("global_settings must be a dictionary")
        else:
            if "parallel_engine_limit" in global_settings:
                limit = global_settings["parallel_engine_limit"]
                if not isinstance(limit, int) or limit < 1:
                    errors.append(f"parallel_engine_limit must be a positive integer: {limit}")

        return len(errors) == 0, errors

    def get_installed_engines(self) -> list[str]:
        """
        Get list of installed engine IDs.

        Returns:
            List of engine IDs
        """
        return self.config.get("installed", []).copy()

    def add_installed_engine(self, engine_id: str):
        """
        Add engine to installed list.

        Args:
            engine_id: Engine ID
        """
        if "installed" not in self.config:
            self.config["installed"] = []

        if engine_id not in self.config["installed"]:
            self.config["installed"].append(engine_id)
            self.save()
            logger.info(f"Added installed engine: {engine_id}")

    def remove_installed_engine(self, engine_id: str):
        """
        Remove engine from installed list.

        Args:
            engine_id: Engine ID
        """
        if "installed" in self.config and engine_id in self.config["installed"]:
            self.config["installed"].remove(engine_id)
            self.save()
            logger.info(f"Removed installed engine: {engine_id}")

    def is_installed(self, engine_id: str) -> bool:
        """
        Check if engine is installed.

        Args:
            engine_id: Engine ID

        Returns:
            True if installed, False otherwise
        """
        return engine_id in self.get_installed_engines()

    def get_all_config(self) -> dict[str, Any]:
        """Get complete configuration dictionary."""
        return self.config.copy()

    def reset_to_defaults(self):
        """Reset configuration to defaults."""
        self.config = self._get_default_config()
        self.save()
        logger.info("Reset engine configuration to defaults")

    def get_global_settings(self) -> dict[str, Any]:
        """
        Get global engine settings.

        Returns:
            Global settings dictionary
        """
        return self.config.get(
            "global_settings",
            {
                "auto_download_models": True,
                "model_cache_enabled": True,
                "parallel_engine_limit": 2,
            },
        ).copy()

    def set_global_settings(self, settings: dict[str, Any]):
        """
        Set global engine settings.

        Args:
            settings: Global settings dictionary
        """
        if "global_settings" not in self.config:
            self.config["global_settings"] = {}

        self.config["global_settings"].update(settings)
        self.save()
        logger.info(f"Updated global settings: {settings}")

    def get_all_engine_configs(self) -> dict[str, dict[str, Any]]:
        """
        Get all engine configurations.

        Returns:
            Dictionary mapping engine IDs to their configurations
        """
        return self.config.get("engine_configs", {}).copy()

    def ensure_engine_config(self, engine_id: str) -> dict[str, Any]:
        """
        Ensure engine has a configuration entry, creating default if missing.

        Args:
            engine_id: Engine ID

        Returns:
            Engine configuration dictionary
        """
        if "engine_configs" not in self.config:
            self.config["engine_configs"] = {}

        if engine_id not in self.config["engine_configs"]:
            # Create default config for engine
            base_path = self.get_model_path(engine_id, "base")
            self.config["engine_configs"][engine_id] = {
                "model_paths": {"base": base_path},
                "parameters": {},
            }
            self.save()
            logger.info(f"Created default configuration for engine {engine_id}")

        return self.config["engine_configs"][engine_id].copy()


# Global service instance
_service_instance: EngineConfigService | None = None


def get_engine_config_service(
    config_path: str = "backend/config/engine_config.json",
) -> EngineConfigService:
    """
    Get global engine configuration service instance.

    Args:
        config_path: Path to config file

    Returns:
        EngineConfigService instance
    """
    global _service_instance
    if _service_instance is None:
        _service_instance = EngineConfigService(config_path)
    return _service_instance
