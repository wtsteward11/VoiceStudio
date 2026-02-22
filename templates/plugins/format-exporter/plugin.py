"""Format exporter plugin template."""

from __future__ import annotations

import base64
import logging
import tempfile
from pathlib import Path
from typing import Any

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel, Field

from app.core.plugins_api import Plugin
from backend.core.audio.conversion import AudioConversionService
from backend.core.audio.formats import AudioFormat
from backend.project.management.export_path_validator import (
    ExportPathRejectedError,
    validate_export_path,
)

logger = logging.getLogger(__name__)


class ExportRequest(BaseModel):
    wav_base64: str = Field(..., description="Input WAV data as base64")
    output_path: str
    options: dict[str, Any] = Field(default_factory=dict)


class ExportResponse(BaseModel):
    ok: bool
    message: str | None = None


class {{CLASS_NAME}}ExporterPlugin(Plugin):
    """Exporter plugin for {{TARGET_FORMAT}} format."""

    def __init__(self, plugin_dir: Path):
        super().__init__(plugin_dir)
        self._converter = AudioConversionService()
        self.router = APIRouter(prefix="/api/plugin/{{PLUGIN_NAME}}", tags=["{{PLUGIN_NAME}}"])

    @property
    def supported_formats(self) -> list[str]:
        return ["{{TARGET_FORMAT}}"]

    @property
    def target_format(self) -> str:
        return "{{TARGET_FORMAT}}"

    def register(self, app) -> None:
        """Register plugin routes with FastAPI."""
        self.router.post("/export", response_model=ExportResponse)(self.export_endpoint)
        self.router.get("/health")(self.health_endpoint)
        app.include_router(self.router)

    async def export(
        self,
        audio_data: bytes,
        output_path: Path,
        options: dict[str, Any],
    ) -> bool:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_input = Path(tmp) / "input.wav"
            tmp_input.write_bytes(audio_data)
            result = await self._converter.convert_to_format(
                input_path=tmp_input,
                output_path=output_path,
                target_format=AudioFormat.{{FORMAT_ENUM}},
                bitrate_kbps=options.get("bitrate_kbps"),
                sample_rate=options.get("sample_rate"),
                channels=options.get("channels"),
            )
            return bool(result.success)

    async def export_endpoint(self, request: ExportRequest) -> ExportResponse:
        try:
            validated_path = validate_export_path(request.output_path)
        except ExportPathRejectedError as e:
            raise HTTPException(status_code=400, detail=str(e)) from e
        ok = await self.export(
            audio_data=base64.b64decode(request.wav_base64),
            output_path=validated_path,
            options=request.options,
        )
        return ExportResponse(ok=ok)

    async def health_endpoint(self) -> dict:
        return {
            "status": "healthy",
            "plugin": self.metadata.name,
            "format": self.target_format,
        }


def register(app, plugin_dir: Path):
    """Plugin entry point called by the plugin loader."""
    try:
        plugin = {{CLASS_NAME}}ExporterPlugin(plugin_dir)
        plugin.register(app)
        plugin.initialize()
        return plugin
    except Exception as e:
        logger.exception("Failed to register plugin %s: %s", plugin_dir.name, e)
        raise
