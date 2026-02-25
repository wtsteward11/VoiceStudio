"""Engine Capability MCP Server.

Exposes VoiceStudio's engine capabilities as MCP tools, enabling AI assistants
and external tools to synthesize voice, clone voices, transcribe audio, and
analyze quality through the MCP protocol.

All tools delegate to the backend REST API internally.
"""

from __future__ import annotations

import logging
from typing import Any

logger = logging.getLogger(__name__)

_BACKEND_BASE = "http://localhost:8000"


def get_tools() -> list[dict[str, Any]]:
    """Return MCP tool descriptors for engine capabilities."""
    return [
        {
            "name": "voicestudio_synthesize_voice",
            "description": "Synthesize speech from text using a VoiceStudio engine",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "text": {"type": "string", "description": "Text to synthesize"},
                    "profile_id": {"type": "string", "description": "Voice profile ID"},
                    "engine": {"type": "string", "description": "Engine name (xtts_v2, piper, chatterbox, etc.)"},
                    "speed": {"type": "number", "default": 1.0},
                    "temperature": {"type": "number", "default": 0.75},
                },
                "required": ["text"],
            },
        },
        {
            "name": "voicestudio_clone_voice",
            "description": "Initiate voice cloning from reference audio",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "audio_url": {"type": "string", "description": "URL or path to reference audio"},
                    "name": {"type": "string", "description": "Name for the cloned voice profile"},
                    "engine": {"type": "string", "default": "xtts_v2"},
                },
                "required": ["audio_url", "name"],
            },
        },
        {
            "name": "voicestudio_transcribe_audio",
            "description": "Transcribe audio to text using speech-to-text engine",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "audio_url": {"type": "string", "description": "URL or path to audio file"},
                    "engine": {"type": "string", "default": "whisper"},
                    "language": {"type": "string", "description": "ISO language code"},
                },
                "required": ["audio_url"],
            },
        },
        {
            "name": "voicestudio_analyze_audio",
            "description": "Analyze audio quality (MOS, SNR, spectral features)",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "audio_url": {"type": "string", "description": "URL or path to audio file"},
                },
                "required": ["audio_url"],
            },
        },
        {
            "name": "voicestudio_list_voices",
            "description": "List all available voice profiles",
            "inputSchema": {"type": "object", "properties": {}},
        },
        {
            "name": "voicestudio_list_engines",
            "description": "List all available synthesis engines and their status",
            "inputSchema": {"type": "object", "properties": {}},
        },
    ]


async def execute_tool(tool_name: str, arguments: dict[str, Any]) -> dict[str, Any]:
    """Execute an MCP tool by delegating to the backend REST API."""
    try:
        import httpx

        async with httpx.AsyncClient(base_url=_BACKEND_BASE, timeout=30.0) as client:
            if tool_name == "voicestudio_synthesize_voice":
                resp = await client.post("/api/voice/synthesize", json=arguments)
                return resp.json()

            elif tool_name == "voicestudio_clone_voice":
                resp = await client.post("/api/voice/clone", json=arguments)
                return resp.json()

            elif tool_name == "voicestudio_transcribe_audio":
                resp = await client.post("/api/transcribe", json=arguments)
                return resp.json()

            elif tool_name == "voicestudio_analyze_audio":
                resp = await client.post("/api/audio/analyze", json=arguments)
                return resp.json()

            elif tool_name == "voicestudio_list_voices":
                resp = await client.get("/api/profiles")
                return resp.json()

            elif tool_name == "voicestudio_list_engines":
                resp = await client.get("/api/engines/list")
                return resp.json()

            else:
                return {"error": f"Unknown tool: {tool_name}"}

    except ImportError:
        return {"error": "httpx not installed. Run: pip install httpx"}
    except Exception as e:
        logger.error(f"MCP tool execution failed: {tool_name}: {e}")
        return {"error": str(e), "tool": tool_name}
