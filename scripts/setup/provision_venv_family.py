#!/usr/bin/env python
"""
Provision a venv family for VoiceStudio engines.

Usage:
    python scripts/provision_venv_family.py --family advanced_tts
    python scripts/provision_venv_family.py --family core_tts --force
    python scripts/provision_venv_family.py --list
"""

import argparse
import logging
import sys

from app.core.runtime.venv_family_manager import (
    FAMILY_CONFIGS,
    VenvFamily,
    get_venv_manager,
)

logging.basicConfig(level=logging.INFO, format="%(levelname)s: %(message)s")
logger = logging.getLogger(__name__)


def list_families():
    """List all available venv families."""
    print("\nAvailable venv families:")
    print("-" * 60)
    manager = get_venv_manager()

    for family, config in FAMILY_CONFIGS.items():
        exists = manager.is_venv_created(family)
        status = "EXISTS" if exists else "NOT CREATED"
        print(f"\n{family.value} [{status}]")
        print(f"  Description: {config.description}")
        print(f"  Requirements: {config.requirements_file}")
        print(f"  Engines: {', '.join(config.engines)}")
        print(f"  GPU Required: {config.gpu_required}")
        print(f"  Est. Size: {config.estimated_size_gb} GB")


def provision_family(family_name: str, force: bool = False, install_deps: bool = True):
    """Provision a specific venv family."""
    # Map family name to enum
    family_map = {
        "core_tts": VenvFamily.CORE_TTS,
        "advanced_tts": VenvFamily.ADVANCED_TTS,
        "stt": VenvFamily.STT,
    }

    if family_name not in family_map:
        logger.error(f"Unknown family: {family_name}")
        logger.info(f"Valid families: {', '.join(family_map.keys())}")
        return False

    family = family_map[family_name]
    manager = get_venv_manager()
    config = FAMILY_CONFIGS[family]

    logger.info(f"Provisioning venv family: {family.value}")
    logger.info(f"  Description: {config.description}")
    logger.info(f"  Requirements: {config.requirements_file}")

    # Check if exists
    if manager.is_venv_created(family):
        if force:
            logger.info("Venv exists, recreating (--force specified)")
        else:
            logger.info("Venv already exists. Use --force to recreate.")
            return True

    # Create venv
    logger.info("Creating virtual environment...")
    if not manager.create_venv(family, force=force):
        logger.error("Failed to create venv")
        return False

    logger.info("Venv created successfully")

    # Install dependencies
    if install_deps:
        logger.info("Installing requirements...")
        if manager.install_requirements(family):
            logger.info("Requirements installed successfully")
        else:
            logger.warning("Failed to install requirements (may need manual installation)")
            logger.info(f"  pip install -r config/venv_families/{config.requirements_file}")

    # Verify
    python_exe = manager.get_python_executable(family)
    if python_exe.exists():
        logger.info(f"Python executable: {python_exe}")
        logger.info("Venv family provisioned successfully!")
        return True
    else:
        logger.error("Python executable not found after provisioning")
        return False


def main():
    parser = argparse.ArgumentParser(description="Provision venv families for VoiceStudio")
    parser.add_argument("--family", type=str, help="Family to provision (core_tts, advanced_tts, stt)")
    parser.add_argument("--force", action="store_true", help="Force recreate if exists")
    parser.add_argument("--no-deps", action="store_true", help="Skip dependency installation")
    parser.add_argument("--list", action="store_true", help="List available families")

    args = parser.parse_args()

    if args.list:
        list_families()
        return 0

    if not args.family:
        parser.print_help()
        print("\nUse --list to see available families")
        return 1

    success = provision_family(
        args.family,
        force=args.force,
        install_deps=not args.no_deps
    )

    return 0 if success else 1


if __name__ == "__main__":
    sys.exit(main())
