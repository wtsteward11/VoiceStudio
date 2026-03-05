"""
MCP server exposing VoiceStudio engine capabilities to external AI agents.

Reads engine manifests directly from disk (no backend dependency).
Runs standalone: python backend/mcp_bridge/engine_discovery_server.py

Protocol: MCP JSON-RPC over HTTP (initialize, tools/list, tools/call)
Default port: 9901
"""

from __future__ import annotations

import json
import logging
import sys
from pathlib import Path
from typing import Any

from fastapi import FastAPI, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse, Response, StreamingResponse

logger = logging.getLogger(__name__)

app = FastAPI(title="VoiceStudio Engine Discovery")
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

ENGINES_DIR = Path(__file__).resolve().parents[2] / "engines"
DEFAULT_PORT = 9901

STREAMING_CAPABILITY_KEYWORDS = frozenset({
    "streaming",
    "real_time_conversion",
    "real_time",
    "low_latency",
    "speech_to_speech",
    "barge_in",
})

_ENGINE_TYPE_MAP = {
    "tts": "tts",
    "stt": "stt",
    "voice_conversion": "vc",
    "voice_cloning": "tts",
    "s2s": "s2s",
}

MCP_TOOLS = [
    {
        "name": "list_engines",
        "description": (
            "List all available VoiceStudio engines with their capabilities, "
            "supported languages, and quality metrics."
        ),
        "inputSchema": {
            "type": "object",
            "properties": {},
        },
    },
    {
        "name": "get_engine_details",
        "description": "Get detailed information about a specific engine including its full manifest, status, and capabilities.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "engine_id": {
                    "type": "string",
                    "description": "The engine identifier (e.g. 'xtts_v2', 'whisper', 'rvc')",
                },
            },
            "required": ["engine_id"],
        },
    },
    {
        "name": "get_streaming_engines",
        "description": "List engines that support real-time streaming or low-latency operation.",
        "inputSchema": {
            "type": "object",
            "properties": {},
        },
    },
]

MCP_SERVER_INSTRUCTIONS = (
    "This server provides read-only access to VoiceStudio's engine registry. "
    "Use list_engines to discover available TTS/STT/voice-conversion engines, "
    "get_engine_details to inspect a specific engine's full manifest, "
    "and get_streaming_engines to find engines suitable for real-time use."
)


# ---------------------------------------------------------------------------
# Manifest loading
# ---------------------------------------------------------------------------

def load_all_manifests(engines_dir: Path | None = None) -> dict[str, dict[str, Any]]:
    """Walk the engines directory and load every engine.manifest.json."""
    root = engines_dir or ENGINES_DIR
    manifests: dict[str, dict[str, Any]] = {}
    if not root.is_dir():
        logger.warning("Engines directory not found: %s", root)
        return manifests

    for manifest_path in root.rglob("engine.manifest.json"):
        try:
            with open(manifest_path, encoding="utf-8") as fh:
                data = json.load(fh)
            eid = data.get("engine_id")
            if eid:
                manifests[eid] = data
        except (json.JSONDecodeError, OSError) as exc:
            logger.warning("Skipping invalid manifest %s: %s", manifest_path, exc)
    return manifests


def _classify_type(manifest: dict[str, Any]) -> str:
    subtype = manifest.get("subtype", "")
    return _ENGINE_TYPE_MAP.get(subtype, subtype or manifest.get("type", "unknown"))


def _extract_quality(manifest: dict[str, Any]) -> dict[str, Any]:
    qf = manifest.get("quality_features", {})
    return {k: v for k, v in qf.items() if v is not None} if qf else {}


def _has_streaming(manifest: dict[str, Any]) -> bool:
    caps = {c.lower() for c in manifest.get("capabilities", [])}
    return bool(caps & STREAMING_CAPABILITY_KEYWORDS)


# ---------------------------------------------------------------------------
# Tool implementations
# ---------------------------------------------------------------------------

def _tool_list_engines(manifests: dict[str, dict[str, Any]]) -> str:
    engines = []
    for eid, m in sorted(manifests.items()):
        engines.append({
            "engine_id": eid,
            "name": m.get("name", eid),
            "type": _classify_type(m),
            "supported_languages": m.get("supported_languages", []),
            "quality_metrics": _extract_quality(m),
            "capabilities": m.get("capabilities", []),
            "implementation_status": m.get("implementation_status", "unknown"),
        })
    return json.dumps({"engines": engines, "count": len(engines)})


def _tool_get_engine_details(manifests: dict[str, dict[str, Any]], engine_id: str) -> str:
    manifest = manifests.get(engine_id)
    if manifest is None:
        return json.dumps({
            "error": f"Engine '{engine_id}' not found",
            "available_engines": sorted(manifests.keys()),
        })

    safe = {k: v for k, v in manifest.items() if k not in ("entry_point",)}
    safe["resolved_type"] = _classify_type(manifest)
    safe["streaming_capable"] = _has_streaming(manifest)
    return json.dumps(safe)


def _tool_get_streaming_engines(manifests: dict[str, dict[str, Any]]) -> str:
    streaming = []
    for eid, m in sorted(manifests.items()):
        if _has_streaming(m):
            caps = m.get("capabilities", [])
            matched = [c for c in caps if c.lower() in STREAMING_CAPABILITY_KEYWORDS]
            streaming.append({
                "engine_id": eid,
                "name": m.get("name", eid),
                "type": _classify_type(m),
                "streaming_capabilities": matched,
            })
    return json.dumps({"engines": streaming, "count": len(streaming)})


# ---------------------------------------------------------------------------
# MCP JSON-RPC endpoint
# ---------------------------------------------------------------------------

_manifests: dict[str, dict[str, Any]] = {}


def _reload_manifests(engines_dir: Path | None = None) -> None:
    global _manifests
    _manifests = load_all_manifests(engines_dir)
    logger.info("Loaded %d engine manifests", len(_manifests))


@app.get("/")
def root():
    return {
        "name": "VoiceStudio Engine Discovery",
        "description": "MCP server for discovering VoiceStudio engine capabilities",
        "mcp_endpoint": "/mcp",
        "engine_count": len(_manifests),
    }


def _sse_stream():
    yield "event: endpoint\ndata: {\"url\": \"/mcp\"}\n\n"


@app.api_route("/mcp", methods=["GET", "POST"])
@app.api_route("/sse", methods=["GET", "POST"])
@app.api_route("/sse/", methods=["GET", "POST"])
async def mcp_endpoint(request: Request):
    """MCP endpoint. GET returns SSE stream; POST handles JSON-RPC."""
    if request.method == "GET":
        return StreamingResponse(
            _sse_stream(),
            media_type="text/event-stream",
            headers={"Cache-Control": "no-cache", "X-Accel-Buffering": "no"},
        )

    try:
        body = await request.json() or {}
    except Exception:
        return JSONResponse(
            {"jsonrpc": "2.0", "error": {"code": -32700, "message": "Parse error"}},
            status_code=400,
        )

    msg_id = body.get("id")
    method = body.get("method")
    params = body.get("params") or {}

    logger.info("[MCP] method=%s id=%s", method, msg_id)

    if msg_id is None and method:
        return Response(status_code=202)

    if method == "ping":
        return {"jsonrpc": "2.0", "id": msg_id, "result": {}}

    if method == "initialize":
        client_version = (params.get("protocolVersion") or "2025-06-18").strip()
        supported = ("2024-11-05", "2025-03-26", "2025-06-18")
        protocol_version = client_version if client_version in supported else "2025-06-18"
        return {
            "jsonrpc": "2.0",
            "id": msg_id,
            "result": {
                "protocolVersion": protocol_version,
                "capabilities": {"tools": {"listChanged": True}},
                "serverInfo": {
                    "name": "voicestudio-engine-discovery",
                    "title": "VoiceStudio Engine Discovery",
                    "version": "1.0.0",
                },
                "instructions": MCP_SERVER_INSTRUCTIONS,
            },
        }

    if method == "tools/list":
        return {
            "jsonrpc": "2.0",
            "id": msg_id,
            "result": {"tools": MCP_TOOLS},
        }

    if method == "resources/list":
        return {"jsonrpc": "2.0", "id": msg_id, "result": {"resources": []}}

    if method == "prompts/list":
        return {"jsonrpc": "2.0", "id": msg_id, "result": {"prompts": []}}

    if method == "resources/read":
        return {
            "jsonrpc": "2.0", "id": msg_id,
            "error": {"code": -32602, "message": "Resource not found"},
        }

    if method == "prompts/get":
        return {
            "jsonrpc": "2.0", "id": msg_id,
            "error": {"code": -32602, "message": "Prompt not found"},
        }

    if method == "tools/call":
        name = params.get("name")
        args = params.get("arguments") or {}

        if name == "list_engines":
            text = _tool_list_engines(_manifests)
        elif name == "get_engine_details":
            engine_id = args.get("engine_id", "")
            if not engine_id:
                return {
                    "jsonrpc": "2.0", "id": msg_id,
                    "result": {
                        "content": [{"type": "text", "text": json.dumps({"error": "engine_id is required"})}],
                        "isError": True,
                    },
                }
            text = _tool_get_engine_details(_manifests, engine_id)
        elif name == "get_streaming_engines":
            text = _tool_get_streaming_engines(_manifests)
        else:
            return {
                "jsonrpc": "2.0", "id": msg_id,
                "error": {"code": -32601, "message": f"Unknown tool: {name}"},
            }

        is_error = "error" in json.loads(text)
        return {
            "jsonrpc": "2.0",
            "id": msg_id,
            "result": {
                "content": [{"type": "text", "text": text}],
                "isError": is_error,
            },
        }

    return {
        "jsonrpc": "2.0", "id": msg_id,
        "error": {"code": -32601, "message": f"Method not found: {method}"},
    }


# ---------------------------------------------------------------------------
# Entrypoint
# ---------------------------------------------------------------------------

def run_server(engines_dir: Path | None = None, port: int = DEFAULT_PORT) -> None:
    """Start the MCP engine discovery server."""
    import uvicorn

    _reload_manifests(engines_dir)
    print(f"Engine Discovery MCP server starting on port {port}")
    print(f"Loaded {len(_manifests)} engine manifests from {engines_dir or ENGINES_DIR}")
    uvicorn.run(app, host="0.0.0.0", port=port)


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")

    port = DEFAULT_PORT
    engines_path = None

    for arg in sys.argv[1:]:
        if arg.startswith("--port="):
            port = int(arg.split("=", 1)[1])
        elif arg.startswith("--engines="):
            engines_path = Path(arg.split("=", 1)[1])
        elif arg.isdigit():
            port = int(arg)

    run_server(engines_dir=engines_path, port=port)
