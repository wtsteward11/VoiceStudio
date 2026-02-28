"""
Engine Integration Service

Phase 17: Integration Improvements
Unified engine management and external tool integration.

Features:
- New engine integration (17.1)
- External tool integration (17.2)
- Engine health monitoring
- Capability discovery
"""

from __future__ import annotations

import asyncio
import contextlib
import json
import logging
import subprocess
from dataclasses import dataclass
from datetime import datetime
from enum import Enum
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)


class EngineStatus(Enum):
    """Engine status."""

    UNKNOWN = "unknown"
    AVAILABLE = "available"
    LOADING = "loading"
    READY = "ready"
    BUSY = "busy"
    ERROR = "error"
    UNAVAILABLE = "unavailable"


class EngineCapability(Enum):
    """Engine capabilities."""

    TTS = "tts"  # Text-to-speech
    STT = "stt"  # Speech-to-text
    VOICE_CLONING = "voice_cloning"
    VOICE_CONVERSION = "voice_conversion"
    EMOTION_CONTROL = "emotion_control"
    MULTI_SPEAKER = "multi_speaker"
    STREAMING = "streaming"
    REAL_TIME = "real_time"
    MULTILINGUAL = "multilingual"


@dataclass
class EngineInfo:
    """Engine information."""

    engine_id: str
    name: str
    version: str
    capabilities: set[EngineCapability]
    status: EngineStatus
    languages: list[str]
    sample_rates: list[int]
    requires_gpu: bool
    model_path: str | None
    config: dict[str, Any]
    last_health_check: datetime | None
    error_message: str | None = None

    def to_dict(self) -> dict[str, Any]:
        return {
            "engine_id": self.engine_id,
            "name": self.name,
            "version": self.version,
            "capabilities": [c.value for c in self.capabilities],
            "status": self.status.value,
            "languages": self.languages,
            "sample_rates": self.sample_rates,
            "requires_gpu": self.requires_gpu,
            "model_path": self.model_path,
            "config": self.config,
            "last_health_check": (
                self.last_health_check.isoformat() if self.last_health_check else None
            ),
            "error_message": self.error_message,
        }


@dataclass
class ExternalTool:
    """External tool configuration."""

    tool_id: str
    name: str
    executable_path: str
    version: str | None
    is_available: bool
    capabilities: list[str]
    config: dict[str, Any]

    def to_dict(self) -> dict[str, Any]:
        return {
            "tool_id": self.tool_id,
            "name": self.name,
            "executable_path": self.executable_path,
            "version": self.version,
            "is_available": self.is_available,
            "capabilities": self.capabilities,
            "config": self.config,
        }


class EngineIntegrationService:
    """
    Engine integration and management service.

    Phase 17: Integration Improvements

    Features:
    - Dynamic engine discovery
    - Health monitoring
    - Capability-based routing
    - External tool integration
    """

    def __init__(self, engines_dir: Path | None = None):
        self._engines_dir = engines_dir or Path("engines")
        self._engines: dict[str, EngineInfo] = {}
        self._external_tools: dict[str, ExternalTool] = {}
        self._health_check_interval = 60  # seconds
        self._health_check_task: asyncio.Task | None = None
        self._initialized = False

        logger.info("EngineIntegrationService created")

    async def initialize(self) -> bool:
        """Initialize the engine integration service."""
        if self._initialized:
            return True

        try:
            # Discover engines
            await self._discover_engines()

            # Discover external tools
            await self._discover_external_tools()

            # Start health monitoring
            self._health_check_task = asyncio.create_task(self._health_monitor_loop())

            self._initialized = True
            logger.info(f"EngineIntegrationService initialized with {len(self._engines)} engines")
            return True

        except Exception as e:
            logger.error(f"Failed to initialize EngineIntegrationService: {e}")
            return False

    # ===== Phase 17.1: Engine Integration =====

    async def _discover_engines(self):
        """Discover available engines from manifests."""
        # Built-in engines
        builtin_engines = [
            EngineInfo(
                engine_id="xtts_v2",
                name="XTTS v2",
                version="2.0.0",
                capabilities={
                    EngineCapability.TTS,
                    EngineCapability.VOICE_CLONING,
                    EngineCapability.MULTILINGUAL,
                    EngineCapability.STREAMING,
                },
                status=EngineStatus.AVAILABLE,
                languages=[
                    "en",
                    "es",
                    "fr",
                    "de",
                    "it",
                    "pt",
                    "pl",
                    "tr",
                    "ru",
                    "nl",
                    "cs",
                    "ar",
                    "zh",
                    "ja",
                    "ko",
                ],
                sample_rates=[22050, 24000],
                requires_gpu=True,
                model_path=None,
                config={"default_speaker": None},
                last_health_check=None,
            ),
            EngineInfo(
                engine_id="bark",
                name="Bark",
                version="1.0.0",
                capabilities={
                    EngineCapability.TTS,
                    EngineCapability.EMOTION_CONTROL,
                    EngineCapability.MULTILINGUAL,
                },
                status=EngineStatus.AVAILABLE,
                languages=[
                    "en",
                    "de",
                    "es",
                    "fr",
                    "hi",
                    "it",
                    "ja",
                    "ko",
                    "pl",
                    "pt",
                    "ru",
                    "tr",
                    "zh",
                ],
                sample_rates=[24000],
                requires_gpu=True,
                model_path=None,
                config={"use_small_models": False},
                last_health_check=None,
            ),
            EngineInfo(
                engine_id="piper",
                name="Piper",
                version="1.2.0",
                capabilities={
                    EngineCapability.TTS,
                    EngineCapability.STREAMING,
                    EngineCapability.REAL_TIME,
                },
                status=EngineStatus.AVAILABLE,
                languages=["en", "de", "es", "fr"],
                sample_rates=[16000, 22050],
                requires_gpu=False,
                model_path=None,
                config={"model_quality": "medium"},
                last_health_check=None,
            ),
            EngineInfo(
                engine_id="chatterbox",
                name="Chatterbox TTS",
                version="0.1.0",
                capabilities={
                    EngineCapability.TTS,
                    EngineCapability.VOICE_CLONING,
                    EngineCapability.EMOTION_CONTROL,
                },
                status=EngineStatus.AVAILABLE,
                languages=["en"],
                sample_rates=[24000],
                requires_gpu=True,
                model_path=None,
                config={},
                last_health_check=None,
            ),
            EngineInfo(
                engine_id="rvc",
                name="RVC (Retrieval Voice Conversion)",
                version="2.0.0",
                capabilities={
                    EngineCapability.VOICE_CONVERSION,
                    EngineCapability.REAL_TIME,
                },
                status=EngineStatus.AVAILABLE,
                languages=["*"],  # Language agnostic
                sample_rates=[40000, 48000],
                requires_gpu=True,
                model_path=None,
                config={"pitch_shift": 0},
                last_health_check=None,
            ),
            EngineInfo(
                engine_id="whisper",
                name="Whisper",
                version="large-v3",
                capabilities={
                    EngineCapability.STT,
                    EngineCapability.MULTILINGUAL,
                },
                status=EngineStatus.AVAILABLE,
                languages=["*"],
                sample_rates=[16000],
                requires_gpu=True,
                model_path=None,
                config={"model_size": "large-v3"},
                last_health_check=None,
            ),
            EngineInfo(
                engine_id="vosk",
                name="Vosk",
                version="0.3.45",
                capabilities={
                    EngineCapability.STT,
                    EngineCapability.REAL_TIME,
                    EngineCapability.STREAMING,
                },
                status=EngineStatus.AVAILABLE,
                languages=["en", "de", "fr", "es", "it", "nl", "pt", "ru", "zh"],
                sample_rates=[16000],
                requires_gpu=False,
                model_path=None,
                config={"model": "vosk-model-en-us-0.22"},
                last_health_check=None,
            ),
        ]

        for engine in builtin_engines:
            self._engines[engine.engine_id] = engine

        # Load from manifests
        if self._engines_dir.exists():
            for manifest_path in self._engines_dir.glob("**/*.manifest.json"):
                try:
                    await self._load_engine_manifest(manifest_path)
                except Exception as e:
                    logger.warning(f"Failed to load engine manifest {manifest_path}: {e}")

    async def _load_engine_manifest(self, manifest_path: Path):
        """Load engine from manifest file."""
        with open(manifest_path) as f:
            manifest = json.load(f)

        engine_id = manifest.get("id", manifest_path.stem.replace(".manifest", ""))

        capabilities = set()
        for cap in manifest.get("capabilities", []):
            try:
                capabilities.add(EngineCapability(cap))
            except ValueError:
                logger.debug("Unknown capability '%s' in engine manifest", cap)

        engine = EngineInfo(
            engine_id=engine_id,
            name=manifest.get("name", engine_id),
            version=manifest.get("version", "unknown"),
            capabilities=capabilities,
            status=EngineStatus.AVAILABLE,
            languages=manifest.get("languages", []),
            sample_rates=manifest.get("sample_rates", [22050]),
            requires_gpu=manifest.get("requires_gpu", False),
            model_path=manifest.get("model_path"),
            config=manifest.get("config", {}),
            last_health_check=None,
        )

        self._engines[engine_id] = engine
        logger.info(f"Loaded engine from manifest: {engine_id}")

    async def register_engine(self, engine: EngineInfo):
        """Register a new engine."""
        self._engines[engine.engine_id] = engine
        logger.info(f"Registered engine: {engine.engine_id}")

    async def unregister_engine(self, engine_id: str):
        """Unregister an engine."""
        if engine_id in self._engines:
            del self._engines[engine_id]
            logger.info(f"Unregistered engine: {engine_id}")

    def get_engine(self, engine_id: str) -> EngineInfo | None:
        """Get engine by ID."""
        return self._engines.get(engine_id)

    def list_engines(
        self,
        capability: EngineCapability | None = None,
        status: EngineStatus | None = None,
    ) -> list[EngineInfo]:
        """List engines with optional filtering."""
        engines = list(self._engines.values())

        if capability:
            engines = [e for e in engines if capability in e.capabilities]

        if status:
            engines = [e for e in engines if e.status == status]

        return engines

    def get_engines_for_task(self, task_type: str) -> list[EngineInfo]:
        """Get engines suitable for a specific task."""
        capability_map = {
            "synthesis": EngineCapability.TTS,
            "transcription": EngineCapability.STT,
            "cloning": EngineCapability.VOICE_CLONING,
            "conversion": EngineCapability.VOICE_CONVERSION,
        }

        capability = capability_map.get(task_type)
        if not capability:
            return []

        return self.list_engines(capability=capability, status=EngineStatus.READY)

    async def check_engine_health(self, engine_id: str) -> bool:
        """Check health of a specific engine."""
        engine = self._engines.get(engine_id)
        if not engine:
            return False

        try:
            # Simulate health check
            # In real implementation, would call engine's health endpoint
            await asyncio.sleep(0.1)

            engine.status = EngineStatus.READY
            engine.last_health_check = datetime.now()
            engine.error_message = None

            return True

        except Exception as e:
            engine.status = EngineStatus.ERROR
            engine.error_message = str(e)
            return False

    async def _health_monitor_loop(self):
        """Continuous health monitoring loop."""
        while True:
            try:
                await asyncio.sleep(self._health_check_interval)

                for engine_id in list(self._engines.keys()):
                    await self.check_engine_health(engine_id)

            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Health monitor error: {e}")

    # ===== Phase 17.2: External Tool Integration =====

    async def _discover_external_tools(self):
        """Discover external tools."""
        tools_to_check = [
            (
                "ffmpeg",
                ["ffmpeg", "-version"],
                ["audio_processing", "video_processing", "format_conversion"],
            ),
            ("ffprobe", ["ffprobe", "-version"], ["media_analysis"]),
            ("sox", ["sox", "--version"], ["audio_processing", "effects"]),
            ("imagemagick", ["magick", "-version"], ["image_processing"]),
            ("yt-dlp", ["yt-dlp", "--version"], ["media_download"]),
        ]

        for tool_id, version_cmd, capabilities in tools_to_check:
            tool = await self._check_tool(tool_id, version_cmd, capabilities)
            self._external_tools[tool_id] = tool

    async def _check_tool(
        self,
        tool_id: str,
        version_cmd: list[str],
        capabilities: list[str],
    ) -> ExternalTool:
        """Check if external tool is available."""
        try:
            result = subprocess.run(
                version_cmd,
                capture_output=True,
                text=True,
                timeout=5,
            )

            is_available = result.returncode == 0
            version = None

            if is_available:
                # Extract version from output
                output = result.stdout + result.stderr
                for line in output.split("\n"):
                    if "version" in line.lower():
                        version = line.strip()[:50]
                        break

            return ExternalTool(
                tool_id=tool_id,
                name=tool_id.title(),
                executable_path=version_cmd[0],
                version=version,
                is_available=is_available,
                capabilities=capabilities if is_available else [],
                config={},
            )

        except Exception:
            return ExternalTool(
                tool_id=tool_id,
                name=tool_id.title(),
                executable_path=version_cmd[0],
                version=None,
                is_available=False,
                capabilities=[],
                config={},
            )

    async def register_external_tool(
        self,
        tool_id: str,
        name: str,
        executable_path: str,
        capabilities: list[str],
        config: dict[str, Any] | None = None,
    ) -> ExternalTool:
        """Register an external tool."""
        # Verify tool exists
        try:
            result = subprocess.run(
                [executable_path, "--version"],
                capture_output=True,
                timeout=5,
            )
            is_available = result.returncode == 0
        except Exception:
            is_available = False

        tool = ExternalTool(
            tool_id=tool_id,
            name=name,
            executable_path=executable_path,
            version=None,
            is_available=is_available,
            capabilities=capabilities,
            config=config or {},
        )

        self._external_tools[tool_id] = tool
        logger.info(f"Registered external tool: {tool_id}")

        return tool

    def get_external_tool(self, tool_id: str) -> ExternalTool | None:
        """Get external tool by ID."""
        return self._external_tools.get(tool_id)

    def list_external_tools(self, available_only: bool = True) -> list[ExternalTool]:
        """List external tools."""
        tools = list(self._external_tools.values())
        if available_only:
            tools = [t for t in tools if t.is_available]
        return tools

    async def run_external_tool(
        self,
        tool_id: str,
        args: list[str],
        input_data: bytes | None = None,
        timeout: int = 300,
    ) -> subprocess.CompletedProcess:
        """Run an external tool."""
        tool = self._external_tools.get(tool_id)
        if not tool or not tool.is_available:
            raise ValueError(f"Tool not available: {tool_id}")

        cmd = [tool.executable_path, *args]

        result = subprocess.run(
            cmd,
            input=input_data,
            capture_output=True,
            timeout=timeout,
        )

        return result

    # ===== Cleanup =====

    async def cleanup(self):
        """Cleanup resources."""
        if self._health_check_task:
            self._health_check_task.cancel()
            with contextlib.suppress(asyncio.CancelledError):
                await self._health_check_task

        logger.info("EngineIntegrationService cleaned up")


# Singleton
_engine_integration_service: EngineIntegrationService | None = None


def get_engine_integration_service() -> EngineIntegrationService:
    """Get or create engine integration service singleton."""
    global _engine_integration_service
    if _engine_integration_service is None:
        _engine_integration_service = EngineIntegrationService()
    return _engine_integration_service
