"""FLAC exporter plugin."""

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
    wav_base64: str = Field(..., description="Input WAV bytes encoded as base64")
    output_path: str
    options: dict[str, Any] = Field(default_factory=dict)


class ExportResponse(BaseModel):
    ok: bool


class FlacExporterPlugin(Plugin):
    """FLAC format exporter plugin.

    Uses :class:`AudioConversionService` to convert WAV input to lossless
    FLAC.  Validates output paths against the configured export root.
    Exposes ``POST /export`` and ``GET /health``.
    """

    def __init__(self, plugin_dir: Path):
        super().__init__(plugin_dir)
        self._converter = AudioConversionService()
        self.router = APIRouter(prefix="/api/plugin/export_flac", tags=["export_flac"])

    @property
    def supported_formats(self) -> list[str]:
        """Return list of supported export formats."""
        return ["flac"]

    @property
    def target_format(self) -> str:
        """Return the primary target format."""
        return "flac"

    def register(self, app) -> None:
        """Register plugin routes with FastAPI."""
        self.router.post("/export", response_model=ExportResponse)(self.export_endpoint)
        self.router.get("/health")(self.health_endpoint)
        app.include_router(self.router)

    async def export(self, audio_data: bytes, output_path: Path, options: dict[str, Any]) -> bool:
        """Export audio data to FLAC format."""
        with tempfile.TemporaryDirectory() as tmp:
            temp_input = Path(tmp) / "input.wav"
            temp_input.write_bytes(audio_data)
            result = await self._converter.convert_to_format(
                input_path=temp_input,
                output_path=output_path,
                target_format=AudioFormat.FLAC,
                bitrate_kbps=options.get("bitrate_kbps"),
                sample_rate=options.get("sample_rate"),
                channels=options.get("channels"),
            )
            return bool(result.success)

    async def export_endpoint(self, request: ExportRequest) -> ExportResponse:
        """Handle export requests."""
        try:
            output_path = validate_export_path(request.output_path)
        except ExportPathRejectedError as e:
            logger.warning("Export path rejected: %s", e)
            raise HTTPException(status_code=400, detail=str(e)) from e
        ok = await self.export(
            audio_data=base64.b64decode(request.wav_base64),
            output_path=output_path,
            options=request.options,
        )
        return ExportResponse(ok=ok)

    async def health_endpoint(self) -> dict[str, str]:
        """Return health status."""
        return {"status": "healthy", "plugin": self.name}


def register(app, plugin_dir: Path):
    """Plugin entry point called by the plugin loader."""
    try:
        plugin = FlacExporterPlugin(plugin_dir)
        plugin.register(app)
        plugin.initialize()
        return plugin
    except Exception as e:
        logger.exception("Failed to register plugin %s: %s", plugin_dir.name, e)
        raise
