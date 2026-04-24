"""
Venv Family Manager

Manages multiple Python virtual environments for different engine families.
Implements TD-015: 8 engine families for complete dependency isolation.

Families:
- venv_core_tts: XTTS, Silero, OpenAI TTS, Fish Speech, Mars5, Parler TTS
- venv_tortoise: Tortoise TTS (isolated stack; Slice 18B — incompatible transformers pin vs Coqui/XTTS)
- venv_advanced_tts: Chatterbox, F5-TTS, GPT-SoVITS, Bark
- venv_openvoice: OpenVoice (Slice 19F — isolated from torch26 / Chatterbox stack)
- venv_fast_tts: Piper, Parakeet, eSpeak-NG, Festival, MaryTTS, RHVoice (CPU-friendly)
- venv_stt: Whisper, Whisper.cpp, Vosk, Aeneas
- venv_voice_conversion: RVC, So-VITS-SVC, Mockingbird, Speaker Encoder
- venv_image: SDXL, Fooocus, RealESRGAN, SD-CPU, LocalAI
- venv_comfy: ComfyUI, InvokeAI, Automatic1111
- venv_video: SadTalker, FOMM, Deforum, SVD, DeepFaceLab
"""

from __future__ import annotations

import logging
import os
import subprocess
import sys
from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)

# GAP-062: ``scripts/engines/create_engine_venv.py`` provisions Chatterbox under
# ``runtime/venvs/torch26`` (family key ``torch26``). Runtime must resolve
# ``VenvFamily.ADVANCED_TTS`` to that same tree so preflight matches provisioning.
ADVANCED_TTS_PROVISION_DIRNAME = "torch26"
# Slice 18B: tortoise-tts pins transformers incompatible with Coqui/XTTS in core venv.
TORTOISE_TTS_PROVISION_DIRNAME = "tortoise"
# Slice 19F: OpenVoice isolated venv (myshell-openvoice / PyAV chain; ADR-054).
OPENVOICE_PROVISION_DIRNAME = "openvoice"


class VenvFamily(Enum):
    """Virtual environment family identifiers."""

    # Audio TTS families
    CORE_TTS = "venv_core_tts"
    ADVANCED_TTS = "venv_advanced_tts"
    TORTOISE = "venv_tortoise"
    OPENVOICE = "venv_openvoice"
    FAST_TTS = "venv_fast_tts"

    # Speech-to-text
    STT = "venv_stt"

    # Voice processing
    VOICE_CONVERSION = "venv_voice_conversion"

    # Image generation
    IMAGE = "venv_image"
    COMFY = "venv_comfy"

    # Video synthesis
    VIDEO = "venv_video"


@dataclass
class FamilyConfig:
    """Configuration for a venv family."""

    family: VenvFamily
    requirements_file: str
    python_version: str = "3.11"
    description: str = ""
    engines: list[str] = field(default_factory=list)
    gpu_required: bool = False
    estimated_size_gb: float = 0.0


# Family configurations
FAMILY_CONFIGS: dict[VenvFamily, FamilyConfig] = {
    VenvFamily.CORE_TTS: FamilyConfig(
        family=VenvFamily.CORE_TTS,
        requirements_file="requirements-core-tts.txt",
        python_version="3.11",
        description="Core TTS engines with stable torch 2.2.x",
        engines=[
            "xtts_v2",
            "silero",
            "openai_tts",
            "fish_speech",
            "mars5",
            "parler_tts",
            "openvoice_v2",
            "tacotron2",  # Tacotron 2 via Coqui TTS
        ],
        gpu_required=True,
        estimated_size_gb=8.0,
    ),
    VenvFamily.ADVANCED_TTS: FamilyConfig(
        family=VenvFamily.ADVANCED_TTS,
        requirements_file="requirements-advanced-tts.txt",
        python_version="3.11",
        description="Advanced TTS with latest models (torch 2.6+, SM 120 compatible)",
        engines=["chatterbox", "f5_tts", "gpt_sovits", "bark"],
        gpu_required=True,
        estimated_size_gb=10.0,
    ),
    VenvFamily.TORTOISE: FamilyConfig(
        family=VenvFamily.TORTOISE,
        requirements_file="requirements-tortoise.txt",
        python_version="3.11",
        description="Tortoise TTS isolated stack (Slice 18B; transformers pin vs Coqui XTTS)",
        engines=["tortoise"],
        gpu_required=True,
        estimated_size_gb=6.0,
    ),
    VenvFamily.OPENVOICE: FamilyConfig(
        family=VenvFamily.OPENVOICE,
        requirements_file="requirements-openvoice.txt",
        python_version="3.11",
        description="OpenVoice isolated stack (Slice 19F; decoupled from Chatterbox torch26)",
        engines=["openvoice"],
        gpu_required=True,
        estimated_size_gb=8.0,
    ),
    VenvFamily.FAST_TTS: FamilyConfig(
        family=VenvFamily.FAST_TTS,
        requirements_file="requirements-fast-tts.txt",
        python_version="3.11",
        description="Lightweight CPU-friendly TTS engines",
        engines=["piper", "parakeet", "espeak_ng", "festival", "marytts", "rhvoice"],
        gpu_required=False,
        estimated_size_gb=2.0,
    ),
    VenvFamily.STT: FamilyConfig(
        family=VenvFamily.STT,
        requirements_file="requirements-stt.txt",
        python_version="3.11",
        description="Speech-to-text and alignment engines",
        engines=["whisper", "whisper_cpp", "whisper_ui", "vosk", "aeneas"],
        gpu_required=False,
        estimated_size_gb=4.0,
    ),
    VenvFamily.VOICE_CONVERSION: FamilyConfig(
        family=VenvFamily.VOICE_CONVERSION,
        requirements_file="requirements-voice-conversion.txt",
        python_version="3.11",
        description="Voice conversion and cloning engines",
        engines=["rvc", "rvc_v2", "so_vits_svc", "sovits_svc", "mockingbird", "speaker_encoder"],
        gpu_required=True,
        estimated_size_gb=6.0,
    ),
    VenvFamily.IMAGE: FamilyConfig(
        family=VenvFamily.IMAGE,
        requirements_file="requirements-image.txt",
        python_version="3.11",
        description="Image generation with diffusers",
        engines=[
            "sdxl",
            "sd_cpu",
            "fastsd_cpu",
            "fooocus",
            "openjourney",
            "realistic_vision",
            "realesrgan",
            "sdnext",
            "localai",
        ],
        gpu_required=True,
        estimated_size_gb=12.0,
    ),
    VenvFamily.COMFY: FamilyConfig(
        family=VenvFamily.COMFY,
        requirements_file="requirements-comfy.txt",
        python_version="3.11",
        description="Node-based image workflow engines",
        engines=["comfyui", "sdxl_comfy", "invokeai", "automatic1111"],
        gpu_required=True,
        estimated_size_gb=8.0,
    ),
    VenvFamily.VIDEO: FamilyConfig(
        family=VenvFamily.VIDEO,
        requirements_file="requirements-video.txt",
        python_version="3.11",
        description="Video synthesis and lip-sync engines",
        engines=[
            "sadtalker",
            "fomm",
            "deforum",
            "svd",
            "deepfacelab",
            "moviepy",
            "ffmpeg_ai",
            "video_creator",
        ],
        gpu_required=True,
        estimated_size_gb=10.0,
    ),
}

# Engine to family mapping (reverse lookup)
ENGINE_TO_FAMILY: dict[str, VenvFamily] = {}
for family, config in FAMILY_CONFIGS.items():
    for engine_id in config.engines:
        ENGINE_TO_FAMILY[engine_id] = family


class VenvFamilyManager:
    """
    Manages virtual environment families for engine isolation.

    Features:
    - Lazy venv creation (on first use)
    - Family-based engine isolation
    - GPU-optimized venv support
    - Cross-platform compatibility
    """

    def __init__(
        self,
        venvs_root: Path | None = None,
        requirements_root: Path | None = None,
        advanced_tts_venv_path: Path | None = None,
        tortoise_venv_path: Path | None = None,
        openvoice_venv_path: Path | None = None,
    ):
        """
        Initialize venv family manager.

        Args:
            venvs_root: Root directory for venvs (default: project_root/.venvs)
            requirements_root: Root directory for requirements files (default: project_root/config/venv_families)
            advanced_tts_venv_path: Optional override for ``ADVANCED_TTS`` on-disk venv (tests / tooling).
                When omitted, uses ``project_root/runtime/venvs/{ADVANCED_TTS_PROVISION_DIRNAME}`` (``create_engine_venv``).
            tortoise_venv_path: Optional override for ``TORTOISE`` isolated venv (tests / tooling).
            openvoice_venv_path: Optional override for ``OPENVOICE`` isolated venv (tests / tooling).
        """
        self.project_root = Path(__file__).parent.parent.parent.parent
        self.venvs_root = venvs_root or self.project_root / ".venvs"
        self.requirements_root = requirements_root or self.project_root / "config" / "venv_families"
        self._advanced_tts_venv_path_override = advanced_tts_venv_path
        self._tortoise_venv_path_override = tortoise_venv_path
        self._openvoice_venv_path_override = openvoice_venv_path

        # Ensure directories exist
        self.venvs_root.mkdir(parents=True, exist_ok=True)
        self.requirements_root.mkdir(parents=True, exist_ok=True)

        # Track created venvs
        self._created_venvs: dict[VenvFamily, bool] = {}

        logger.info(f"VenvFamilyManager initialized: venvs={self.venvs_root}")

    def get_family_for_engine(self, engine_id: str) -> VenvFamily | None:
        """
        Get the venv family for an engine.

        Args:
            engine_id: Engine identifier

        Returns:
            VenvFamily or None if engine not mapped
        """
        return ENGINE_TO_FAMILY.get(engine_id)

    def get_venv_path(self, family: VenvFamily) -> Path:
        """Get the path to a family's venv."""
        if family == VenvFamily.ADVANCED_TTS:
            if self._advanced_tts_venv_path_override is not None:
                return self._advanced_tts_venv_path_override
            return self.project_root / "runtime" / "venvs" / ADVANCED_TTS_PROVISION_DIRNAME
        if family == VenvFamily.TORTOISE:
            if self._tortoise_venv_path_override is not None:
                return self._tortoise_venv_path_override
            return self.project_root / "runtime" / "venvs" / TORTOISE_TTS_PROVISION_DIRNAME
        if family == VenvFamily.OPENVOICE:
            if self._openvoice_venv_path_override is not None:
                return self._openvoice_venv_path_override
            return self.project_root / "runtime" / "venvs" / OPENVOICE_PROVISION_DIRNAME
        return self.venvs_root / family.value

    def get_python_executable(self, family: VenvFamily) -> Path:
        """Get the Python executable path for a family's venv."""
        venv_path = self.get_venv_path(family)
        if sys.platform == "win32":
            return venv_path / "Scripts" / "python.exe"
        return venv_path / "bin" / "python"

    def get_pip_executable(self, family: VenvFamily) -> Path:
        """Get the pip executable path for a family's venv."""
        venv_path = self.get_venv_path(family)
        if sys.platform == "win32":
            return venv_path / "Scripts" / "pip.exe"
        return venv_path / "bin" / "pip"

    def is_venv_created(self, family: VenvFamily) -> bool:
        """Check if a family's venv exists."""
        python_exe = self.get_python_executable(family)
        return python_exe.exists()

    def create_venv(
        self,
        family: VenvFamily,
        python_executable: str | None = None,
        force: bool = False,
    ) -> bool:
        """
        Create a virtual environment for a family.

        Args:
            family: The venv family to create
            python_executable: Path to Python interpreter (default: sys.executable)
            force: If True, recreate even if exists

        Returns:
            True if created successfully
        """
        venv_path = self.get_venv_path(family)

        if self.is_venv_created(family) and not force:
            logger.info(f"Venv {family.value} already exists at {venv_path}")
            return True

        # Use specified Python or find appropriate version
        python_exe = python_executable or self._find_python(FAMILY_CONFIGS[family].python_version)

        logger.info(f"Creating venv {family.value} at {venv_path} using {python_exe}")

        try:
            # Create venv
            result = subprocess.run(
                [python_exe, "-m", "venv", str(venv_path)],
                capture_output=True,
                text=True,
                timeout=120,
            )

            if result.returncode != 0:
                logger.error(f"Failed to create venv: {result.stderr}")
                return False

            # Upgrade pip
            pip_exe = self.get_pip_executable(family)
            subprocess.run(
                [str(pip_exe), "install", "--upgrade", "pip"],
                capture_output=True,
                timeout=120,
            )

            self._created_venvs[family] = True
            logger.info(f"Venv {family.value} created successfully")
            return True

        except subprocess.TimeoutExpired:
            logger.error(f"Timeout creating venv {family.value}")
            return False
        except Exception as e:
            logger.error(f"Error creating venv {family.value}: {e}")
            return False

    def install_requirements(self, family: VenvFamily) -> bool:
        """
        Install requirements for a family's venv.

        Args:
            family: The venv family

        Returns:
            True if installed successfully
        """
        if not self.is_venv_created(family):
            logger.error(f"Venv {family.value} does not exist")
            return False

        config = FAMILY_CONFIGS[family]
        requirements_file = self.requirements_root / config.requirements_file

        if not requirements_file.exists():
            logger.warning(f"Requirements file not found: {requirements_file}")
            return False

        pip_exe = self.get_pip_executable(family)

        logger.info(f"Installing requirements for {family.value} from {requirements_file}")

        try:
            result = subprocess.run(
                [str(pip_exe), "install", "-r", str(requirements_file)],
                capture_output=True,
                text=True,
                timeout=600,  # 10 minutes for large installs
            )

            if result.returncode != 0:
                logger.error(f"Failed to install requirements: {result.stderr}")
                return False

            logger.info(f"Requirements installed for {family.value}")
            return True

        except subprocess.TimeoutExpired:
            logger.error(f"Timeout installing requirements for {family.value}")
            return False
        except Exception as e:
            logger.error(f"Error installing requirements for {family.value}: {e}")
            return False

    def ensure_venv_ready(self, family: VenvFamily) -> bool:
        """
        Ensure a family's venv is created and has requirements installed.

        This is the main entry point for lazy venv creation.

        Args:
            family: The venv family

        Returns:
            True if venv is ready
        """
        if not self.is_venv_created(family) and not self.create_venv(family):
            return False

        # Check if requirements are installed (simple check: look for marker file)
        marker_file = self.get_venv_path(family) / ".requirements_installed"
        if not marker_file.exists():
            if not self.install_requirements(family):
                return False
            marker_file.touch()

        return True

    def run_in_venv(
        self,
        family: VenvFamily,
        command: list[str],
        cwd: Path | None = None,
        env: dict[str, str] | None = None,
        timeout: int = 300,
    ) -> subprocess.CompletedProcess:
        """
        Run a command in a family's venv.

        Args:
            family: The venv family
            command: Command to run (first element should be script/module)
            cwd: Working directory
            env: Additional environment variables
            timeout: Timeout in seconds

        Returns:
            CompletedProcess result
        """
        if not self.ensure_venv_ready(family):
            raise RuntimeError(f"Failed to prepare venv {family.value}")

        python_exe = self.get_python_executable(family)

        # Build environment
        run_env = os.environ.copy()
        if env:
            run_env.update(env)

        # Add venv to PATH
        venv_path = self.get_venv_path(family)
        if sys.platform == "win32":
            run_env["PATH"] = f"{venv_path / 'Scripts'};{run_env.get('PATH', '')}"
        else:
            run_env["PATH"] = f"{venv_path / 'bin'}:{run_env.get('PATH', '')}"

        # Run command
        full_command = [str(python_exe), *command]

        return subprocess.run(
            full_command,
            cwd=cwd or self.project_root,
            env=run_env,
            capture_output=True,
            text=True,
            timeout=timeout,
        )

    def get_status(self) -> dict[str, Any]:
        """Get status of all venv families."""
        status = {}
        for family, config in FAMILY_CONFIGS.items():
            venv_path = self.get_venv_path(family)
            status[family.value] = {
                "exists": self.is_venv_created(family),
                "path": str(venv_path),
                "description": config.description,
                "engines": config.engines,
                "gpu_required": config.gpu_required,
                "estimated_size_gb": config.estimated_size_gb,
            }
        return status

    def _find_python(self, version: str) -> str:
        """Find a Python executable matching the version."""
        # Try common locations on Windows
        if sys.platform == "win32":
            candidates = [
                Path(os.environ.get("LOCALAPPDATA", ""))
                / "Programs"
                / "Python"
                / f"Python{version.replace('.', '')}"
                / "python.exe",
                Path(f"C:/Python{version.replace('.', '')}/python.exe"),
                Path(sys.executable),
            ]
            for candidate in candidates:
                if candidate.exists():
                    return str(candidate)

        # Default to current Python
        return sys.executable


# Global manager instance
_venv_manager: VenvFamilyManager | None = None


def get_venv_manager() -> VenvFamilyManager:
    """Get or create global venv family manager instance."""
    global _venv_manager
    if _venv_manager is None:
        _venv_manager = VenvFamilyManager()
    return _venv_manager
