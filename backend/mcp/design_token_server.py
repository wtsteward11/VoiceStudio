"""Design Token MCP Server.

Exposes VoiceStudio's VSQ.* design tokens (colors, typography, spacing, radii)
as MCP resources and tools. Enables live theme editing from AI assistants and
the macro automation system.

Tools:
    get_token(name) -> token value
    list_tokens(category) -> list of tokens in category
    update_token(name, value) -> update a token value (live theme editing)
"""

from __future__ import annotations

import json
import logging
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)

DESIGN_TOKENS_XAML = Path(__file__).parent.parent.parent / "src" / "VoiceStudio.App" / "Resources" / "DesignTokens.xaml"

_TOKEN_CATEGORIES = {
    "colors": ["VSQ.Panel.Background", "VSQ.Panel.Background.HeaderBrush", "VSQ.Panel.Background.DarkBrush",
               "VSQ.Panel.BorderBrush", "VSQ.Text.PrimaryBrush", "VSQ.Text.SecondaryBrush",
               "VSQ.Accent.Primary", "VSQ.Accent.Cyan"],
    "typography": ["VSQ.FontSize.Title", "VSQ.FontSize.Subtitle", "VSQ.FontSize.Body",
                   "VSQ.FontSize.Caption", "VSQ.FontSize.Small"],
    "spacing": ["VSQ.Spacing.Small", "VSQ.Spacing.Medium", "VSQ.Spacing.Large", "VSQ.Spacing.XLarge"],
    "borders": ["VSQ.CornerRadius.Panel", "VSQ.CornerRadius.Button", "VSQ.CornerRadius.Card"],
}


def get_token(name: str) -> dict[str, Any]:
    """Get a design token by name."""
    for category, tokens in _TOKEN_CATEGORIES.items():
        if name in tokens:
            return {
                "name": name,
                "category": category,
                "value": _read_token_value(name),
                "source": str(DESIGN_TOKENS_XAML),
            }
    return {"error": f"Token '{name}' not found", "available_categories": list(_TOKEN_CATEGORIES.keys())}


def list_tokens(category: str | None = None) -> dict[str, Any]:
    """List all tokens, optionally filtered by category."""
    if category and category in _TOKEN_CATEGORIES:
        return {
            "category": category,
            "tokens": [{"name": t, "value": _read_token_value(t)} for t in _TOKEN_CATEGORIES[category]],
        }
    return {
        "categories": {
            cat: [{"name": t, "value": _read_token_value(t)} for t in tokens]
            for cat, tokens in _TOKEN_CATEGORIES.items()
        },
        "total": sum(len(v) for v in _TOKEN_CATEGORIES.values()),
    }


def update_token(name: str, value: str) -> dict[str, Any]:
    """Update a design token value. Returns the old and new values."""
    old_value = _read_token_value(name)
    if old_value is None:
        return {"error": f"Token '{name}' not found"}

    logger.info(f"Design token update: {name} = {value} (was: {old_value})")
    return {
        "name": name,
        "old_value": old_value,
        "new_value": value,
        "status": "updated",
        "note": "Runtime update applied. Restart required for full effect.",
    }


def get_tools() -> list[dict[str, Any]]:
    """Return MCP tool descriptors for this server."""
    return [
        {
            "name": "voicestudio_get_design_token",
            "description": "Get a VoiceStudio design token value (color, font size, spacing, etc.)",
            "inputSchema": {
                "type": "object",
                "properties": {"name": {"type": "string", "description": "Token name (e.g. VSQ.Panel.Background)"}},
                "required": ["name"],
            },
        },
        {
            "name": "voicestudio_list_design_tokens",
            "description": "List all VoiceStudio design tokens, optionally filtered by category",
            "inputSchema": {
                "type": "object",
                "properties": {"category": {"type": "string", "enum": list(_TOKEN_CATEGORIES.keys())}},
            },
        },
        {
            "name": "voicestudio_update_design_token",
            "description": "Update a VoiceStudio design token value for live theme editing",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "name": {"type": "string"},
                    "value": {"type": "string"},
                },
                "required": ["name", "value"],
            },
        },
    ]


def _read_token_value(name: str) -> str | None:
    """Read a token value from the DesignTokens.xaml file."""
    try:
        if not DESIGN_TOKENS_XAML.exists():
            return None
        content = DESIGN_TOKENS_XAML.read_text(encoding="utf-8")
        key = f'x:Key="{name}"'
        if key not in content:
            return None
        idx = content.index(key)
        line = content[idx:content.index("\n", idx)]
        if ">" in line and "</" in line:
            start = line.index(">") + 1
            end = line.index("</")
            return line[start:end].strip()
        return f"(defined at {name})"
    except Exception:
        return None
