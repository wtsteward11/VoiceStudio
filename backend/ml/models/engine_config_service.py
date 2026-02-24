"""
Engine Configuration Service - manages engine_config.json.

Provides a service-layer abstraction over the engine configuration file,
following the same pattern as EngineService for dependency injection.

Architecture:
    Routes (API) -> EngineConfigService -> backend/config/engine_config.json
"""

import json
import logging
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

logger = logging.getLogger(__name__)

_CONFIG_PATH = Path(__file__).resolve().parents[2] / "config" / "engine_config.json"


class EngineConfigService:
    """Service for reading and updating engine configuration."""

    def __init__(self, config_path: Optional[Path] = None) -> None:
        self._config_path = config_path or _CONFIG_PATH
        self._config: Dict[str, Any] = {}
        self._load()

    def _load(self) -> None:
        try:
            if self._config_path.exists():
                self._config = json.loads(self._config_path.read_text(encoding="utf-8"))
            else:
                logger.warning("Engine config not found at %s, using defaults", self._config_path)
                self._config = {"defaults": {}, "gpu_settings": {}, "engine_configs": {}}
        except Exception as exc:
            logger.error("Failed to load engine config: %s", exc)
            self._config = {"defaults": {}, "gpu_settings": {}, "engine_configs": {}}

    def _save(self) -> None:
        try:
            self._config_path.parent.mkdir(parents=True, exist_ok=True)
            self._config_path.write_text(
                json.dumps(self._config, indent=2, ensure_ascii=False) + "\n",
                encoding="utf-8",
            )
        except Exception as exc:
            logger.error("Failed to save engine config: %s", exc)

    @property
    def config(self) -> Dict[str, Any]:
        return self._config

    def get_all_config(self) -> Dict[str, Any]:
        return dict(self._config)

    def get_engine_config(self, engine_id: str) -> Dict[str, Any]:
        return self._config.get("engine_configs", {}).get(engine_id, {})

    def update_engine_config(self, engine_id: str, updates: Dict[str, Any]) -> Dict[str, Any]:
        configs = self._config.setdefault("engine_configs", {})
        engine_cfg = configs.setdefault(engine_id, {})
        engine_cfg.update(updates)
        self._save()
        return engine_cfg

    def get_gpu_settings(self) -> Dict[str, Any]:
        return self._config.get("gpu_settings", {})

    def update_gpu_settings(self, updates: Dict[str, Any]) -> Dict[str, Any]:
        gpu = self._config.setdefault("gpu_settings", {})
        gpu.update(updates)
        self._save()
        return gpu

    def set_default_engine(self, task_type: str, engine_id: str) -> None:
        defaults = self._config.setdefault("defaults", {})
        defaults[task_type] = engine_id
        self._save()

    def validate_config(self) -> Tuple[bool, List[str]]:
        errors: List[str] = []
        if "defaults" not in self._config:
            errors.append("Missing 'defaults' section")
        if "gpu_settings" not in self._config:
            errors.append("Missing 'gpu_settings' section")
        return (len(errors) == 0, errors)


_instance: Optional[EngineConfigService] = None


def get_engine_config_service() -> EngineConfigService:
    """Singleton accessor for dependency injection."""
    global _instance
    if _instance is None:
        _instance = EngineConfigService()
    return _instance
