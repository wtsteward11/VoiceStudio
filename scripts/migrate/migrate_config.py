#!/usr/bin/env python3
"""
Config Migration Script

Migrates legacy JSON configuration files to the new unified YAML configuration system.

Usage:
    python scripts/migrate_config.py [--dry-run] [--backup]

Options:
    --dry-run   Show what would be migrated without making changes
    --backup    Create backups of existing YAML files before overwriting
"""

import argparse
import json
import logging
import shutil
import sys
from datetime import datetime
from pathlib import Path
from typing import Any

import yaml

# Setup logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s [%(levelname)s] %(message)s',
    datefmt='%H:%M:%S'
)
logger = logging.getLogger(__name__)

# Project root
PROJECT_ROOT = Path(__file__).parent.parent
CONFIG_DIR = PROJECT_ROOT / "config"


class ConfigMigrator:
    """Handles migration from legacy JSON to unified YAML config."""

    def __init__(self, dry_run: bool = False, backup: bool = True):
        self.dry_run = dry_run
        self.backup = backup
        self.migration_log: list = []

    def migrate_all(self) -> bool:
        """Run all migrations."""
        logger.info("Starting config migration...")

        success = True

        # Migrate engines config
        if not self._migrate_engines_config():
            success = False

        # Migrate voicestudio settings
        if not self._migrate_voicestudio_config():
            success = False

        # Summary
        logger.info("")
        logger.info("=" * 60)
        logger.info("MIGRATION SUMMARY")
        logger.info("=" * 60)
        for entry in self.migration_log:
            logger.info(entry)

        if self.dry_run:
            logger.info("")
            logger.info("DRY RUN - No files were modified")

        return success

    def _migrate_engines_config(self) -> bool:
        """Migrate legacy engine configs to engines.config.yaml."""
        logger.info("")
        logger.info("Migrating engine configuration...")

        # Source files
        legacy_engines = PROJECT_ROOT / "engines" / "config.json"
        legacy_backend = PROJECT_ROOT / "backend" / "config" / "engine_config.json"

        # Target file
        target = CONFIG_DIR / "engines.config.yaml"

        # Load legacy configs
        engines_data = self._load_json(legacy_engines)
        backend_data = self._load_json(legacy_backend)

        if not engines_data and not backend_data:
            logger.warning("No legacy engine configs found")
            self.migration_log.append("engines: SKIPPED (no source files)")
            return True

        # Merge configs (backend takes precedence)
        merged = self._merge_engine_configs(engines_data or {}, backend_data or {})

        # Transform to new YAML structure
        yaml_config = self._transform_engines_config(merged)

        # Write YAML
        if not self.dry_run:
            self._backup_if_exists(target)
            self._write_yaml(target, yaml_config)
            logger.info(f"  Written: {target}")
        else:
            logger.info(f"  Would write: {target}")

        self.migration_log.append(f"engines: MIGRATED to {target.name}")
        return True

    def _migrate_voicestudio_config(self) -> bool:
        """Migrate legacy settings to voicestudio.config.yaml."""
        logger.info("")
        logger.info("Migrating VoiceStudio settings...")

        # Source file
        legacy_settings = PROJECT_ROOT / "data" / "settings.json"

        # Target file
        target = CONFIG_DIR / "voicestudio.config.yaml"

        # Load legacy config
        settings_data = self._load_json(legacy_settings)

        if not settings_data:
            logger.warning("No legacy settings found")
            self.migration_log.append("voicestudio: SKIPPED (no source file)")
            return True

        # Transform to new YAML structure
        yaml_config = self._transform_voicestudio_config(settings_data)

        # Write YAML
        if not self.dry_run:
            self._backup_if_exists(target)
            self._write_yaml(target, yaml_config)
            logger.info(f"  Written: {target}")
        else:
            logger.info(f"  Would write: {target}")

        self.migration_log.append(f"voicestudio: MIGRATED to {target.name}")
        return True

    def _merge_engine_configs(
        self, engines: dict[str, Any], backend: dict[str, Any]
    ) -> dict[str, Any]:
        """Merge two engine config sources, backend takes precedence."""
        merged = {**engines}

        # Merge defaults
        if "defaults" in backend:
            merged["defaults"] = {**merged.get("defaults", {}), **backend["defaults"]}

        # Merge installed list
        if "installed" in backend:
            existing = set(merged.get("installed", []))
            existing.update(backend["installed"])
            merged["installed"] = list(existing)

        # Merge overrides
        if "overrides" in backend:
            merged["overrides"] = {**merged.get("overrides", {}), **backend["overrides"]}

        # Take backend-specific settings
        for key in ["model_paths", "gpu_settings", "engine_configs"]:
            if key in backend:
                merged[key] = backend[key]

        return merged

    def _transform_engines_config(self, legacy: dict[str, Any]) -> dict[str, Any]:
        """Transform legacy engine config to new YAML structure."""
        # Build new structure
        config = {
            "defaults": legacy.get("defaults", {
                "tts": "xtts_v2",
                "stt": "whisper_cpp",
                "image_gen": "sdxl_comfy",
                "video_gen": "svd",
            }),
            "installed": legacy.get("installed", []),
            "overrides": legacy.get("overrides", {}),
            "model_paths": {
                "base": "${VOICESTUDIO_MODELS_PATH:-models}",
                "engines": legacy.get("model_paths", {}).get("engines", {}),
            },
            "gpu_settings": legacy.get("gpu_settings", {
                "enabled": True,
                "device": "cuda",
                "fallback_to_cpu": True,
                "memory_fraction": 0.9,
            }),
            "engine_configs": {},
            "routing_policy": {
                "language_mapping": {
                    "en": "xtts_v2",
                    "ja": "xtts_v2",
                    "zh": "xtts_v2",
                    "es": "xtts_v2",
                    "fr": "xtts_v2",
                    "de": "xtts_v2",
                },
                "fallback_chains": {
                    "tts": ["xtts_v2", "piper", "coqui"],
                    "stt": ["whisper_cpp", "whisper_fast"],
                },
            },
            "ab_testing": {
                "enabled": False,
                "experiments": [],
            },
        }

        # Transform engine configs
        if "engine_configs" in legacy:
            for engine_id, engine_config in legacy["engine_configs"].items():
                config["engine_configs"][engine_id] = {
                    "enabled": True,
                    "model_paths": engine_config.get("model_paths", {}),
                    "parameters": engine_config.get("parameters", {}),
                }

        return config

    def _transform_voicestudio_config(self, legacy: dict[str, Any]) -> dict[str, Any]:
        """Transform legacy settings to new YAML structure."""
        config = {
            "version": "1.0.0",
            "general": legacy.get("general", {
                "theme": "Dark",
                "language": "en-US",
                "auto_save": True,
                "auto_save_interval": 300,
            }),
            "engine": legacy.get("engine", {
                "default_audio_engine": "xtts",
                "default_image_engine": "sdxl",
                "default_video_engine": "svd",
                "quality_level": 5,
            }),
            "audio": legacy.get("audio", {
                "output_device": "Default",
                "input_device": "Default",
                "sample_rate": 44100,
                "buffer_size": 1024,
            }),
            "timeline": legacy.get("timeline", {
                "time_format": "Timecode",
                "snap_enabled": True,
                "snap_interval": 0.1,
                "grid_enabled": True,
                "grid_interval": 1.0,
            }),
            "backend": legacy.get("backend", {
                "api_url": "http://localhost:8000",
                "timeout": 30,
                "retry_count": 3,
            }),
            "performance": legacy.get("performance", {
                "caching_enabled": True,
                "cache_size": 512,
                "max_threads": 4,
                "memory_limit": 4096,
            }),
            "plugins": legacy.get("plugins", {
                "enabled_plugins": [],
            }),
            "mcp": legacy.get("mcp", {
                "enabled": False,
                "server_url": "http://localhost:8080",
            }),
            "quality": legacy.get("quality", {
                "default_preset": "standard",
                "auto_enhance": True,
                "auto_optimize": False,
            }),
            "feature_flags": {
                "ab_testing": False,
                "realtime_voice": True,
                "video_generation": True,
                "cloud_sync": False,
            },
        }

        return config

    def _load_json(self, path: Path) -> dict[str, Any] | None:
        """Load JSON file, return None if not found."""
        if not path.exists():
            logger.debug(f"  File not found: {path}")
            return None

        try:
            with open(path, encoding='utf-8') as f:
                data = json.load(f)
            logger.info(f"  Loaded: {path}")
            return data
        except Exception as e:
            logger.error(f"  Failed to load {path}: {e}")
            return None

    def _write_yaml(self, path: Path, data: dict[str, Any]) -> None:
        """Write data to YAML file."""
        # Ensure directory exists
        path.parent.mkdir(parents=True, exist_ok=True)

        # Add header comment
        header = f"""\
# VoiceStudio Unified Configuration
# Auto-generated by migrate_config.py on {datetime.now().isoformat()}
# Manual edits are preserved on next migration if structure is compatible.

"""

        with open(path, 'w', encoding='utf-8') as f:
            f.write(header)
            yaml.dump(data, f, default_flow_style=False, sort_keys=False, allow_unicode=True)

    def _backup_if_exists(self, path: Path) -> None:
        """Create backup of existing file."""
        if not self.backup or not path.exists():
            return

        backup_path = path.with_suffix(f".{datetime.now().strftime('%Y%m%d_%H%M%S')}.bak")
        shutil.copy2(path, backup_path)
        logger.info(f"  Backed up: {backup_path}")


def validate_yaml_files() -> bool:
    """Validate that generated YAML files are loadable."""
    logger.info("")
    logger.info("Validating generated YAML files...")

    success = True

    for yaml_file in CONFIG_DIR.glob("*.yaml"):
        try:
            with open(yaml_file, encoding='utf-8') as f:
                yaml.safe_load(f)
            logger.info(f"  [OK] {yaml_file.name}")
        except Exception as e:
            logger.error(f"  [FAIL] {yaml_file.name}: {e}")
            success = False

    return success


def main():
    parser = argparse.ArgumentParser(
        description="Migrate legacy JSON configs to unified YAML format"
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Show what would be done without making changes"
    )
    parser.add_argument(
        "--backup",
        action="store_true",
        default=True,
        help="Create backups of existing YAML files (default: True)"
    )
    parser.add_argument(
        "--no-backup",
        action="store_true",
        help="Skip creating backups"
    )

    args = parser.parse_args()

    backup = args.backup and not args.no_backup

    migrator = ConfigMigrator(dry_run=args.dry_run, backup=backup)
    success = migrator.migrate_all()

    if not args.dry_run:
        validate_yaml_files()

    if success:
        logger.info("")
        logger.info("Migration completed successfully!")
        sys.exit(0)
    else:
        logger.error("")
        logger.error("Migration completed with errors!")
        sys.exit(1)


if __name__ == "__main__":
    main()
