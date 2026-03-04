"""
Keyboard Shortcuts Routes

Endpoints for managing keyboard shortcuts.
Supports CRUD operations and conflict detection.
"""

from __future__ import annotations

import asyncio
import logging
from typing import Any

from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/shortcuts", tags=["shortcuts"])

# In-memory shortcuts storage (replace with database in production)
from backend.services.persistent_store import PersistentStore

_shortcuts: PersistentStore = PersistentStore("shortcuts")
_state_lock = asyncio.Lock()


class ShortcutKey(BaseModel):
    """Keyboard shortcut key combination."""

    key: str  # e.g., "N", "Space", "Enter"
    modifiers: list[str] = []  # e.g., ["Ctrl", "Shift"]


class KeyboardShortcut(BaseModel):
    """A keyboard shortcut definition."""

    id: str
    key: str  # Display string like "Ctrl+N"
    key_code: str  # Internal key code
    modifiers: list[str] = []
    description: str
    category: str
    panel_id: str | None = None
    action_id: str | None = None  # Command/action to execute
    is_custom: bool = False  # User-defined vs system default


class ShortcutConflict(BaseModel):
    """Information about a shortcut conflict."""

    shortcut_id: str
    conflicting_shortcut_id: str
    key: str


class ShortcutUpdateRequest(BaseModel):
    """Request to update a keyboard shortcut."""

    key: str | None = None
    key_code: str | None = None
    modifiers: list[str] | None = None
    description: str | None = None


# Initialize default shortcuts
def _initialize_default_shortcuts():
    """Initialize default keyboard shortcuts."""
    default_shortcuts = [
        {
            "id": "file.new",
            "key": "Ctrl+N",
            "key_code": "N",
            "modifiers": ["Ctrl"],
            "description": "New project",
            "category": "file",
            "panel_id": None,
            "action_id": "file.new",
            "is_custom": False,
        },
        {
            "id": "file.open",
            "key": "Ctrl+O",
            "key_code": "O",
            "modifiers": ["Ctrl"],
            "description": "Open project",
            "category": "file",
            "panel_id": None,
            "action_id": "file.open",
            "is_custom": False,
        },
        {
            "id": "file.save",
            "key": "Ctrl+S",
            "key_code": "S",
            "modifiers": ["Ctrl"],
            "description": "Save project",
            "category": "file",
            "panel_id": None,
            "action_id": "file.save",
            "is_custom": False,
        },
        {
            "id": "playback.play",
            "key": "Space",
            "key_code": "Space",
            "modifiers": [],
            "description": "Play/Pause",
            "category": "playback",
            "panel_id": "timeline",
            "action_id": "playback.play",
            "is_custom": False,
        },
        {
            "id": "playback.stop",
            "key": "S",
            "key_code": "S",
            "modifiers": [],
            "description": "Stop",
            "category": "playback",
            "panel_id": "timeline",
            "action_id": "playback.stop",
            "is_custom": False,
        },
        {
            "id": "edit.undo",
            "key": "Ctrl+Z",
            "key_code": "Z",
            "modifiers": ["Ctrl"],
            "description": "Undo",
            "category": "edit",
            "panel_id": None,
            "action_id": "edit.undo",
            "is_custom": False,
        },
        {
            "id": "edit.redo",
            "key": "Ctrl+Y",
            "key_code": "Y",
            "modifiers": ["Ctrl"],
            "description": "Redo",
            "category": "edit",
            "panel_id": None,
            "action_id": "edit.redo",
            "is_custom": False,
        },
        {
            "id": "nav.commandpalette",
            "key": "Ctrl+P",
            "key_code": "P",
            "modifiers": ["Ctrl"],
            "description": "Command palette",
            "category": "navigation",
            "panel_id": None,
            "action_id": "nav.commandpalette",
            "is_custom": False,
        },
        {
            "id": "help.show",
            "key": "F1",
            "key_code": "F1",
            "modifiers": [],
            "description": "Show help",
            "category": "general",
            "panel_id": None,
            "action_id": "help.show",
            "is_custom": False,
        },
        {
            "id": "synthesis.start",
            "key": "Ctrl+Enter",
            "key_code": "Enter",
            "modifiers": ["Ctrl"],
            "description": "Start synthesis",
            "category": "synthesis",
            "panel_id": "voice_synthesis",
            "action_id": "synthesis.start",
            "is_custom": False,
        },
    ]

    for shortcut in default_shortcuts:
        _shortcuts[shortcut["id"]] = shortcut


# Initialize on module load
_initialize_default_shortcuts()


def _check_conflict(
    key_code: str, modifiers: list[str], exclude_id: str | None = None
) -> str | None:
    """Check if a shortcut key combination conflicts with existing shortcuts."""
    for shortcut_id, shortcut in _shortcuts.items():
        if exclude_id and shortcut_id == exclude_id:
            continue

        if shortcut.get("key_code") == key_code and set(shortcut.get("modifiers", [])) == set(
            modifiers
        ):
            return shortcut_id

    return None


@router.get("", response_model=list[KeyboardShortcut])
@cache_response(ttl=300)  # Cache for 5 minutes (shortcuts are relatively static)
async def get_shortcuts(
    category: str | None = Query(None),
    panel_id: str | None = Query(None),
    include_custom: bool = Query(True),
):
    """Get all keyboard shortcuts, optionally filtered."""
    shortcuts = list(_shortcuts.values())

    if category:
        shortcuts = [s for s in shortcuts if s.get("category") == category]

    if panel_id:
        shortcuts = [
            s for s in shortcuts if s.get("panel_id") == panel_id or s.get("panel_id") is None
        ]

    if not include_custom:
        shortcuts = [s for s in shortcuts if not s.get("is_custom", False)]

    return [KeyboardShortcut(**shortcut) for shortcut in shortcuts]


# NOTE: Specific routes must be defined BEFORE parameterized routes to avoid
# FastAPI matching "check-conflict" or "categories" as shortcut_id values.


@router.get("/check-conflict")
@cache_response(ttl=60)  # Cache for 60 seconds (conflicts may change)
async def check_conflict(
    key_code: str = Query(...),
    modifiers: list[str] = Query(...),
    exclude_id: str | None = Query(None),
):
    """Check if a key combination conflicts with existing shortcuts."""
    conflict_id = _check_conflict(key_code, modifiers, exclude_id)
    if conflict_id:
        return {
            "has_conflict": True,
            "conflicting_shortcut": KeyboardShortcut(**_shortcuts[conflict_id]),
        }

    return {"has_conflict": False}


@router.get("/categories")
@cache_response(ttl=600)  # Cache for 10 minutes (categories are static)
async def get_shortcut_categories():
    """Get list of shortcut categories."""
    categories = set()
    for shortcut in _shortcuts.values():
        cat = shortcut.get("category")
        if cat:
            categories.add(cat)

    return {"categories": sorted(categories)}


@router.get("/{shortcut_id}", response_model=KeyboardShortcut)
@cache_response(ttl=300)  # Cache for 5 minutes (shortcut info is static)
async def get_shortcut(shortcut_id: str):
    """Get a specific keyboard shortcut."""
    if shortcut_id not in _shortcuts:
        raise HTTPException(status_code=404, detail="Shortcut not found")

    return KeyboardShortcut(**_shortcuts[shortcut_id])


@router.put("/{shortcut_id}", response_model=KeyboardShortcut)
async def update_shortcut(shortcut_id: str, request: ShortcutUpdateRequest):
    """Update a keyboard shortcut."""
    if shortcut_id not in _shortcuts:
        raise HTTPException(status_code=404, detail="Shortcut not found")

    shortcut = _shortcuts[shortcut_id].copy()

    # Check if key combination is being changed
    if request.key_code or request.modifiers is not None:
        new_key_code = request.key_code or shortcut.get("key_code")
        new_modifiers = (
            request.modifiers if request.modifiers is not None else shortcut.get("modifiers", [])
        )

        # Check for conflicts
        conflict_id = _check_conflict(
            str(new_key_code) if new_key_code else "", new_modifiers, exclude_id=shortcut_id
        )
        if conflict_id:
            conflict_desc = _shortcuts[conflict_id].get("description", "unknown")
            raise HTTPException(
                status_code=409,
                detail=f"Shortcut conflicts with '{conflict_desc}'",
            )

        shortcut["key_code"] = new_key_code
        shortcut["modifiers"] = new_modifiers

        # Update display key
        if request.key:
            shortcut["key"] = request.key
        else:
            # Generate display key from modifiers and key_code
            parts = new_modifiers.copy()
            parts.append(str(new_key_code) if new_key_code else "")
            shortcut["key"] = "+".join(parts)

    if request.description is not None:
        shortcut["description"] = request.description

    _shortcuts[shortcut_id] = shortcut
    return KeyboardShortcut(**shortcut)


@router.post("", response_model=KeyboardShortcut)
async def create_shortcut(shortcut: KeyboardShortcut):
    """Create a new custom keyboard shortcut."""
    if shortcut.id in _shortcuts:
        raise HTTPException(status_code=409, detail="Shortcut ID already exists")

    # Check for conflicts
    conflict_id = _check_conflict(shortcut.key_code, shortcut.modifiers)
    if conflict_id:
        conflict_desc = _shortcuts[conflict_id].get("description", "unknown")
        raise HTTPException(
            status_code=409,
            detail=f"Shortcut conflicts with '{conflict_desc}'",
        )

    shortcut_dict = shortcut.dict()
    shortcut_dict["is_custom"] = True
    _shortcuts[shortcut.id] = shortcut_dict

    return KeyboardShortcut(**shortcut_dict)


@router.delete("/{shortcut_id}")
async def delete_shortcut(shortcut_id: str):
    """Delete a keyboard shortcut (only custom shortcuts can be deleted)."""
    if shortcut_id not in _shortcuts:
        raise HTTPException(status_code=404, detail="Shortcut not found")

    shortcut = _shortcuts[shortcut_id]
    if not shortcut.get("is_custom", False):
        raise HTTPException(status_code=403, detail="Cannot delete system shortcuts")

    del _shortcuts[shortcut_id]
    return {"success": True}


# NOTE: /reset-all must be defined BEFORE /{shortcut_id}/reset
@router.post("/reset-all")
async def reset_all_shortcuts():
    """Reset all shortcuts to defaults."""
    # Remove custom shortcuts
    custom_ids = [sid for sid, s in _shortcuts.items() if s.get("is_custom", False)]
    for sid in custom_ids:
        del _shortcuts[sid]

    # Reinitialize defaults
    _initialize_default_shortcuts()

    return {"success": True, "reset_count": len(custom_ids)}


@router.post("/{shortcut_id}/reset")
async def reset_shortcut(shortcut_id: str):
    """Reset a shortcut to its default value."""
    if shortcut_id not in _shortcuts:
        raise HTTPException(status_code=404, detail="Shortcut not found")

    # Reinitialize to get default
    _initialize_default_shortcuts()

    if shortcut_id not in _shortcuts:
        raise HTTPException(status_code=404, detail="Default shortcut not found")

    return KeyboardShortcut(**_shortcuts[shortcut_id])
