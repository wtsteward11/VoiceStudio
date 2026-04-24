#!/usr/bin/env python3
"""
Per-Engine Virtual Environment Manager (TD-015).

Phase 9 Gap Resolution (2026-02-10):
Creates and manages isolated virtual environments for engines with
conflicting dependencies (e.g., torch versions).

Usage:
    python scripts/engines/create_engine_venv.py --list
    python scripts/engines/create_engine_venv.py --engine chatterbox
    python scripts/engines/create_engine_venv.py --family torch26
    python scripts/engines/create_engine_venv.py --all

Venv Families:
- torch24: Engines requiring torch 2.4.x (XTTS, RVC)
- torch26: Engines requiring torch 2.6.x (Chatterbox)
- cpu_only: Lightweight engines without GPU (Piper, Coqui)
- diffusers: Image/video generation (Stable Diffusion)
- whisper: Transcription engines
- translation: SeamlessM4T, MarianMT
"""

# -----------------------------------------------------------------------------
# Naming note (GAP-062): parallel venv naming schemes
# -----------------------------------------------------------------------------
# This script uses **provisioning family keys** such as ``torch24`` and ``torch26``
# under ``runtime/venvs/<family>/`` (see ``VENV_FAMILIES`` below).
# At **runtime**, ``app/core/runtime/venv_family_manager.py`` uses a different
# enum namespace: ``venv_core_tts``, ``venv_advanced_tts``, ``venv_stt``, etc.
# **Slice 17B:** ``VenvFamily.ADVANCED_TTS`` resolves to ``runtime/venvs/torch26`` (same tree
# as family key ``torch26`` here) — see ``ADVANCED_TTS_PROVISION_DIRNAME`` in
# ``app/core/runtime/venv_family_manager.py``.
# **Slice 18B:** ``VenvFamily.TORTOISE`` resolves to ``runtime/venvs/tortoise`` (family key
# ``tortoise`` here) — see ``TORTOISE_TTS_PROVISION_DIRNAME`` in ``venv_family_manager.py``.
# **Slice 19F:** ``VenvFamily.OPENVOICE`` resolves to ``runtime/venvs/openvoice`` (family key
# ``openvoice`` here) — see ``OPENVOICE_PROVISION_DIRNAME`` in ``venv_family_manager.py``.
# Do not assume ``torch24`` string equals ``venv_core_tts`` without reading both files.
# -----------------------------------------------------------------------------

from __future__ import annotations

import argparse
import json
import logging
import os
import platform
import shutil
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path

# Setup logging
logging.basicConfig(level=logging.INFO, format="%(levelname)s: %(message)s")
logger = logging.getLogger(__name__)

# Project root
PROJECT_ROOT = Path(__file__).parent.parent.parent
VENVS_DIR = PROJECT_ROOT / "runtime" / "venvs"
ENGINES_DIR = PROJECT_ROOT / "engines"
# Slice 19I / ADR-055: vendored myshell-openvoice (patched; see runtime/vendor/myshell-openvoice).
OPENVOICE_VENDOR_SRC = PROJECT_ROOT / "runtime" / "vendor" / "myshell-openvoice"


@dataclass
class VenvFamily:
    """Definition of a virtual environment family."""
    name: str
    description: str
    python_version: str = "3.10"  # Default Python version
    requirements: list[str] = field(default_factory=list)
    post_install: list[str] = field(default_factory=list)  # Commands to run after install
    engines: list[str] = field(default_factory=list)  # Engines in this family


# Venv family definitions
VENV_FAMILIES: dict[str, VenvFamily] = {
    "torch24": VenvFamily(
        name="torch24",
        description="PyTorch 2.4.x for XTTS, RVC, and stable models",
        python_version="3.10",
        requirements=[
            "torch>=2.4.0,<2.5.0",
            "torchaudio>=2.4.0,<2.5.0",
            "numpy<2.0",  # Compatibility
            "TTS>=0.22.0",
            "rvc-python>=0.2.0",
        ],
        engines=["xtts", "xtts_v2", "rvc", "emotion"],
    ),
    "torch26": VenvFamily(
        name="torch26",
        description="PyTorch 2.6.x for Chatterbox and newer models",
        python_version="3.10",
        # Order matters: numpy/torch before chatterbox-tts (pkuseg build needs numpy).
        requirements=[
            "numpy>=2.0",
            "torch>=2.6.0",
            "torchaudio>=2.6.0",
            "chatterbox-tts>=0.1.0",
        ],
        engines=["chatterbox"],
    ),
    "tortoise": VenvFamily(
        name="tortoise",
        description="Tortoise TTS isolated stack (Slice 18B; incompatible transformers pin vs Coqui XTTS)",
        python_version="3.10",
        requirements=[
            "numpy>=1.24.0",
            "torch>=2.2.2",
            "torchaudio>=2.2.2",
            "scipy>=1.10.0",
            "soundfile>=0.12.0",
            "tortoise-tts>=2.0.0",
        ],
        engines=["tortoise"],
    ),
    "openvoice": VenvFamily(
        name="openvoice",
        description="OpenVoice isolated stack (Slice 19F; myshell-openvoice decoupled from Chatterbox torch26)",
        python_version="3.11",
        requirements=[
            "numpy>=1.24.0,<2.0",
            "torch>=2.2.2",
            "torchaudio>=2.2.2",
            "soundfile>=0.12.0",
            "librosa>=0.10.0",
            # Pinned to upstream 74a1d147, patched in-repo (ADR-055); not stock git+ install.
            f"-e {OPENVOICE_VENDOR_SRC}",
        ],
        engines=["openvoice"],
    ),
    "cpu_only": VenvFamily(
        name="cpu_only",
        description="Lightweight CPU-only engines",
        python_version="3.10",
        requirements=[
            "piper-tts>=1.2.0",
            "numpy<2.0",
            "scipy>=1.10.0",
        ],
        engines=["piper", "espeak"],
    ),
    "whisper": VenvFamily(
        name="whisper",
        description="Whisper transcription models",
        python_version="3.10",
        requirements=[
            "openai-whisper>=20231117",
            "torch>=2.0.0",
            "numpy<2.0",
            "ffmpeg-python>=0.2.0",
        ],
        engines=["whisper_tiny", "whisper_base", "whisper_small", "whisper_medium", "whisper_large"],
    ),
    "translation": VenvFamily(
        name="translation",
        description="Translation and S2S models",
        python_version="3.10",
        requirements=[
            "transformers>=4.36.0",
            "torch>=2.0.0",
            "sentencepiece>=0.1.99",
            "protobuf>=3.20.0",
        ],
        engines=["seamless", "marianmt", "translation"],
    ),
    "diffusers": VenvFamily(
        name="diffusers",
        description="Image/video generation models",
        python_version="3.10",
        requirements=[
            "diffusers>=0.25.0",
            "torch>=2.0.0",
            "transformers>=4.36.0",
            "accelerate>=0.25.0",
        ],
        engines=["stable_diffusion", "sadtalker", "wav2lip"],
    ),
}

# Engine to family mapping
ENGINE_TO_FAMILY: dict[str, str] = {}
for family_name, family in VENV_FAMILIES.items():
    for engine in family.engines:
        ENGINE_TO_FAMILY[engine] = family_name


def get_python_executable() -> str:
    """Get the appropriate Python executable for ``python -m venv`` and pip installs."""
    override = os.environ.get("VOICESTUDIO_ENGINE_VENV_PYTHON")
    if override:
        return override

    if platform.system() == "Windows":
        local = Path(os.environ.get("LOCALAPPDATA", "")) / "Programs" / "Python"
        for ver in ("Python311", "Python310", "Python312"):
            candidate = local / ver / "python.exe"
            if candidate.exists():
                return str(candidate)

    if platform.system() == "Windows":
        return "python"
    return "python3.10"


def create_venv(family: VenvFamily, force: bool = False) -> bool:
    """
    Create a virtual environment for a family.

    Args:
        family: The venv family definition
        force: If True, recreate even if exists

    Returns:
        True if successful
    """
    venv_path = VENVS_DIR / family.name

    if venv_path.exists():
        if force:
            logger.info(f"Removing existing venv: {venv_path}")
            shutil.rmtree(venv_path)
        else:
            logger.info(f"Venv already exists: {venv_path}")
            return True

    logger.info(f"Creating venv for '{family.name}': {family.description}")

    # Create venv
    try:
        python = get_python_executable()
        subprocess.run(
            [python, "-m", "venv", str(venv_path)],
            check=True,
            capture_output=True,
        )
    except subprocess.CalledProcessError as e:
        logger.error(f"Failed to create venv: {e.stderr.decode()}")
        return False

    # Get pip path
    if platform.system() == "Windows":
        pip = venv_path / "Scripts" / "pip.exe"
    else:
        pip = venv_path / "bin" / "pip"

    # Upgrade pip
    try:
        subprocess.run(
            [str(pip), "install", "--upgrade", "pip"],
            check=True,
            capture_output=True,
        )
    except subprocess.CalledProcessError as e:
        logger.warning(f"Failed to upgrade pip: {e}")

    # Install requirements
    if family.requirements:
        logger.info(f"Installing requirements for {family.name}...")
        requirements_file = venv_path / "requirements.txt"
        requirements_file.write_text("\n".join(family.requirements))

        try:
            if family.name in ("torch26", "tortoise", "openvoice") and len(family.requirements) >= 2:
                # Staged install: numpy/torch stack first so chatterbox-tts / tortoise-tts
                # transitive builds run with numpy available in the build environment.
                base_file = venv_path / f"requirements-{family.name}-base.txt"
                base_file.write_text("\n".join(family.requirements[:-1]))
                subprocess.run(
                    [str(pip), "install", "-r", str(base_file)],
                    check=True,
                )
                last_req = family.requirements[-1]
                if last_req.startswith("-e "):
                    path_part = last_req[3:].strip()
                    install_cmd = [str(pip), "install", "-e", path_part]
                else:
                    install_cmd = [str(pip), "install", last_req]
                subprocess.run(
                    install_cmd,
                    check=True,
                )
            else:
                subprocess.run(
                    [str(pip), "install", "-r", str(requirements_file)],
                    check=True,
                )
        except subprocess.CalledProcessError as e:
            logger.error(f"Failed to install requirements: {e}")
            return False

    # Run post-install commands
    for cmd in family.post_install:
        logger.info(f"Running post-install: {cmd}")
        try:
            import shlex
            cmd_list = shlex.split(cmd) if isinstance(cmd, str) else cmd
            subprocess.run(cmd_list, check=True, cwd=str(venv_path))
        except subprocess.CalledProcessError as e:
            logger.warning(f"Post-install command failed: {e}")

    # Write family metadata
    metadata = {
        "family": family.name,
        "description": family.description,
        "python_version": family.python_version,
        "engines": family.engines,
        "requirements": family.requirements,
    }
    (venv_path / "family.json").write_text(json.dumps(metadata, indent=2))

    logger.info(f"Successfully created venv: {family.name}")
    return True


def get_engine_venv_path(engine_name: str) -> Path | None:
    """
    Get the venv path for an engine.

    Args:
        engine_name: Engine identifier

    Returns:
        Path to venv if found, None otherwise
    """
    family_name = ENGINE_TO_FAMILY.get(engine_name.lower())
    if not family_name:
        return None

    venv_path = VENVS_DIR / family_name
    if not venv_path.exists():
        return None

    return venv_path


def get_engine_python(engine_name: str) -> Path | None:
    """
    Get the Python executable for an engine.

    Args:
        engine_name: Engine identifier

    Returns:
        Path to Python executable if found
    """
    venv_path = get_engine_venv_path(engine_name)
    if not venv_path:
        return None

    if platform.system() == "Windows":
        python = venv_path / "Scripts" / "python.exe"
    else:
        python = venv_path / "bin" / "python"

    if python.exists():
        return python
    return None


def list_families() -> None:
    """Print information about venv families."""
    print("\n=== Virtual Environment Families (TD-015) ===\n")

    for name, family in VENV_FAMILIES.items():
        venv_path = VENVS_DIR / name
        status = "EXISTS" if venv_path.exists() else "NOT CREATED"

        print(f"{name} [{status}]")
        print(f"  Description: {family.description}")
        print(f"  Python: {family.python_version}")
        print(f"  Engines: {', '.join(family.engines)}")
        print(f"  Path: {venv_path}")
        print()


def list_engines() -> None:
    """Print engine to family mapping."""
    print("\n=== Engine to Family Mapping ===\n")

    for engine, family in sorted(ENGINE_TO_FAMILY.items()):
        venv_path = get_engine_venv_path(engine)
        status = "READY" if venv_path else "PENDING"
        print(f"  {engine:20s} -> {family:15s} [{status}]")


def main():
    parser = argparse.ArgumentParser(
        description="Manage per-engine virtual environments (TD-015)",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )

    parser.add_argument(
        "--list", "-l",
        action="store_true",
        help="List all venv families and their status",
    )

    parser.add_argument(
        "--engines", "-e",
        action="store_true",
        help="List engine to family mapping",
    )

    parser.add_argument(
        "--family", "-f",
        type=str,
        help="Create venv for a specific family",
    )

    parser.add_argument(
        "--engine",
        type=str,
        help="Create venv for a specific engine's family",
    )

    parser.add_argument(
        "--all", "-a",
        action="store_true",
        help="Create all venv families",
    )

    parser.add_argument(
        "--force",
        action="store_true",
        help="Force recreate venv even if exists",
    )

    parser.add_argument(
        "--check",
        type=str,
        help="Check if venv exists for an engine",
    )

    args = parser.parse_args()

    # Ensure venvs directory exists
    VENVS_DIR.mkdir(parents=True, exist_ok=True)

    if args.list:
        list_families()
        return 0

    if args.engines:
        list_engines()
        return 0

    if args.check:
        venv_path = get_engine_venv_path(args.check)
        if venv_path:
            print(f"Venv exists for {args.check}: {venv_path}")
            return 0
        else:
            print(f"No venv for {args.check}")
            return 1

    if args.family:
        if args.family not in VENV_FAMILIES:
            logger.error(f"Unknown family: {args.family}")
            logger.info(f"Available: {', '.join(VENV_FAMILIES.keys())}")
            return 1

        family = VENV_FAMILIES[args.family]
        success = create_venv(family, force=args.force)
        return 0 if success else 1

    if args.engine:
        family_name = ENGINE_TO_FAMILY.get(args.engine.lower())
        if not family_name:
            logger.error(f"Unknown engine: {args.engine}")
            logger.info(f"Known engines: {', '.join(ENGINE_TO_FAMILY.keys())}")
            return 1

        family = VENV_FAMILIES[family_name]
        success = create_venv(family, force=args.force)
        return 0 if success else 1

    if args.all:
        success_count = 0
        for family in VENV_FAMILIES.values():
            if create_venv(family, force=args.force):
                success_count += 1

        print(f"\nCreated {success_count}/{len(VENV_FAMILIES)} venvs")
        return 0 if success_count == len(VENV_FAMILIES) else 1

    # Default: show help
    parser.print_help()
    return 0


if __name__ == "__main__":
    sys.exit(main())
