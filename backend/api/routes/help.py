"""
Help System Routes

Endpoints for help content, tutorials, and documentation.
Supports help topics, keyboard shortcuts, and search.
"""

from __future__ import annotations

import asyncio
import json
import logging
import os

from fastapi import APIRouter, HTTPException, Query
from pydantic import BaseModel

from ..optimization import cache_response

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/help", tags=["help"])

# Help content storage with JSON persistence
_help_topics: dict[str, dict] = {}
_keyboard_shortcuts: dict[str, dict] = {}
_state_lock = asyncio.Lock()
_help_data_file: str | None = None


def _get_help_data_path() -> str:
    """Get path to help data persistence file."""
    global _help_data_file
    if _help_data_file is None:
        data_dir = os.getenv(
            "VOICESTUDIO_DATA_DIR", os.path.join(os.path.expanduser("~"), ".voicestudio", "data")
        )
        os.makedirs(data_dir, exist_ok=True)
        _help_data_file = os.path.join(data_dir, "help_data.json")
    return _help_data_file


def _load_help_data():
    """Load help topics and shortcuts from JSON file."""
    global _help_topics, _keyboard_shortcuts
    help_file = _get_help_data_path()

    if os.path.exists(help_file):
        try:
            with open(help_file, encoding="utf-8") as f:
                data = json.load(f)
                _help_topics = data.get("topics", {})
                _keyboard_shortcuts = data.get("shortcuts", {})
            logger.info(
                f"Loaded {len(_help_topics)} help topics and {len(_keyboard_shortcuts)} shortcuts from {help_file}"
            )
        except Exception as e:
            logger.warning(f"Failed to load help data from {help_file}: {e}")
            _help_topics = {}
            _keyboard_shortcuts = {}
    else:
        logger.debug(f"Help data file not found: {help_file}, using defaults")


def _save_help_data():
    """Save help topics and shortcuts to JSON file."""
    help_file = _get_help_data_path()
    try:
        data = {"topics": _help_topics, "shortcuts": _keyboard_shortcuts, "version": "1.0"}
        with open(help_file, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2, ensure_ascii=False)
        logger.debug(f"Saved help data to {help_file}")
    except Exception as e:
        logger.warning(f"Failed to save help data to {help_file}: {e}")


class HelpTopic(BaseModel):
    """A help topic."""

    id: str
    title: str
    category: str
    content: str
    keywords: list[str] = []
    related_topics: list[str] = []
    panel_id: str | None = None  # Associated panel


class KeyboardShortcut(BaseModel):
    """A keyboard shortcut."""

    key: str
    description: str
    category: str
    panel_id: str | None = None  # Panel-specific shortcut


class HelpSearchRequest(BaseModel):
    """Request to search help content."""

    query: str
    category: str | None = None
    panel_id: str | None = None
    limit: int = 50


class HelpSearchResponse(BaseModel):
    """Response from help search."""

    topics: list[HelpTopic]
    total: int


# Initialize default help topics
def _initialize_default_help():
    """Initialize default help topics."""
    default_topics = [
        {
            "id": "getting_started",
            "title": "Getting Started",
            "category": "general",
            "content": """
# Getting Started with VoiceStudio

Welcome to VoiceStudio! This guide will help you get started.

## Basic Workflow

1. **Create a Voice Profile**: Record or upload reference audio
2. **Synthesize Speech**: Enter text and generate speech
3. **Edit Audio**: Use the timeline and effects mixer
4. **Export**: Save your final audio

## Key Panels

- **Profiles**: Manage voice profiles
- **Timeline**: Edit and arrange audio clips
- **Effects Mixer**: Apply effects and mix audio
- **Analyzer**: Analyze audio quality
            """,
            "keywords": ["getting started", "tutorial", "beginner", "basics"],
            "related_topics": ["voice_profiles", "synthesis", "timeline"],
            "panel_id": None,
        },
        {
            "id": "voice_profiles",
            "title": "Voice Profiles",
            "category": "features",
            "content": """
# Voice Profiles

Voice profiles are the foundation of voice cloning in VoiceStudio.

## Creating Profiles

1. Click "Create Profile" in the Profiles panel
2. Upload reference audio (minimum 3 seconds)
3. Wait for analysis to complete
4. Your profile is ready to use

## Profile Quality

- Higher quality reference audio = better results
- Clean, clear speech works best
- Multiple speakers can be used for ensemble synthesis
            """,
            "keywords": ["voice", "profile", "cloning", "reference"],
            "related_topics": ["synthesis", "quality"],
            "panel_id": "profiles",
        },
        {
            "id": "synthesis",
            "title": "Voice Synthesis",
            "category": "features",
            "content": """
# Voice Synthesis

Generate speech from text using AI voice cloning.

## Engines

- **XTTS v2**: Fast, high-quality synthesis
- **Tortoise TTS**: Highest quality, slower
- **Chatterbox**: Balanced quality and speed

## Parameters

- **Temperature**: Controls randomness (lower = more consistent)
- **Top P**: Nucleus sampling threshold
- **Speed**: Speech rate adjustment
            """,
            "keywords": ["synthesis", "tts", "text to speech", "generation"],
            "related_topics": ["voice_profiles", "engines"],
            "panel_id": "voice_synthesis",
        },
        {
            "id": "timeline",
            "title": "Timeline Editing",
            "category": "features",
            "content": """
# Timeline Editing

The timeline is where you arrange and edit audio clips.

## Basic Operations

- **Add Track**: Create new audio tracks
- **Import Audio**: Load audio files
- **Trim Clips**: Cut and adjust clip boundaries
- **Move Clips**: Drag to rearrange

## Keyboard Shortcuts

- **Space**: Play/Pause
- **Ctrl+Z**: Undo
- **Ctrl+Y**: Redo
            """,
            "keywords": ["timeline", "editing", "tracks", "clips"],
            "related_topics": ["effects", "mixer"],
            "panel_id": "timeline",
        },
        {
            "id": "effects",
            "title": "Audio Effects",
            "category": "features",
            "content": """
# Audio Effects

Apply professional audio effects to enhance your recordings.

## Available Effects

- **EQ**: Equalization for frequency shaping
- **Compressor**: Dynamic range control
- **Reverb**: Add space and depth
- **Delay**: Echo and delay effects
- **Filter**: Low/high/band-pass filtering
- **Normalize**: Level adjustment
- **Denoise**: Remove background noise

## Effect Chain

Effects are applied in order. Drag to reorder.
            """,
            "keywords": ["effects", "processing", "audio", "fx"],
            "related_topics": ["mixer", "timeline"],
            "panel_id": "effects_mixer",
        },
    ]

    for topic in default_topics:
        _help_topics[topic["id"]] = topic

    # Initialize default keyboard shortcuts
    default_shortcuts = [
        {
            "key": "Ctrl+N",
            "description": "New project",
            "category": "file",
            "panel_id": None,
        },
        {
            "key": "Ctrl+O",
            "description": "Open project",
            "category": "file",
            "panel_id": None,
        },
        {
            "key": "Ctrl+S",
            "description": "Save project",
            "category": "file",
            "panel_id": None,
        },
        {
            "key": "Space",
            "description": "Play/Pause",
            "category": "playback",
            "panel_id": "timeline",
        },
        {
            "key": "Ctrl+Enter",
            "description": "Start synthesis",
            "category": "synthesis",
            "panel_id": "voice_synthesis",
        },
        {"key": "Ctrl+Z", "description": "Undo", "category": "edit", "panel_id": None},
        {"key": "Ctrl+Y", "description": "Redo", "category": "edit", "panel_id": None},
        {
            "key": "F1",
            "description": "Show help",
            "category": "general",
            "panel_id": None,
        },
        {
            "key": "Ctrl+P",
            "description": "Command palette",
            "category": "general",
            "panel_id": None,
        },
        {
            "key": "Escape",
            "description": "Close dialog/overlay",
            "category": "general",
            "panel_id": None,
        },
    ]

    for shortcut in default_shortcuts:
        key = shortcut["key"]
        _keyboard_shortcuts[key] = shortcut


# Load persisted data on module load
_load_help_data()

# Initialize defaults only if no persisted data exists
if not _help_topics:
    _initialize_default_help()
    _save_help_data()  # Save defaults on first run


@router.get("/topics", response_model=list[HelpTopic])
@cache_response(ttl=300)  # Cache for 5 minutes (help topics are relatively static)
async def get_help_topics(category: str | None = Query(None), panel_id: str | None = Query(None)):
    """Get all help topics, optionally filtered."""
    topics = list(_help_topics.values())

    if category:
        topics = [t for t in topics if t.get("category") == category]

    if panel_id:
        topics = [t for t in topics if t.get("panel_id") == panel_id or t.get("panel_id") is None]

    return [HelpTopic(**topic) for topic in topics]


@router.get("/topics/{topic_id}", response_model=HelpTopic)
@cache_response(ttl=600)  # Cache for 10 minutes (help content is static)
async def get_help_topic(topic_id: str):
    """Get a specific help topic."""
    if topic_id not in _help_topics:
        raise HTTPException(status_code=404, detail="Help topic not found")

    return HelpTopic(**_help_topics[topic_id])


@router.post("/topics", response_model=HelpTopic)
async def create_help_topic(topic: HelpTopic):
    """Create or update a help topic."""
    _help_topics[topic.id] = topic.model_dump()
    _save_help_data()
    return topic


@router.put("/topics/{topic_id}", response_model=HelpTopic)
async def update_help_topic(topic_id: str, topic: HelpTopic):
    """Update a help topic."""
    if topic_id != topic.id:
        raise HTTPException(status_code=400, detail="Topic ID mismatch")

    if topic_id not in _help_topics:
        raise HTTPException(status_code=404, detail="Help topic not found")

    _help_topics[topic_id] = topic.model_dump()
    _save_help_data()
    return topic


@router.delete("/topics/{topic_id}")
async def delete_help_topic(topic_id: str):
    """Delete a help topic."""
    if topic_id not in _help_topics:
        raise HTTPException(status_code=404, detail="Help topic not found")

    del _help_topics[topic_id]
    _save_help_data()
    return {"message": "Help topic deleted"}


@router.post("/shortcuts", response_model=KeyboardShortcut)
async def create_keyboard_shortcut(shortcut: KeyboardShortcut):
    """Create or update a keyboard shortcut."""
    _keyboard_shortcuts[shortcut.key] = shortcut.model_dump()
    _save_help_data()
    return shortcut


@router.put("/shortcuts/{key}", response_model=KeyboardShortcut)
async def update_keyboard_shortcut(key: str, shortcut: KeyboardShortcut):
    """Update a keyboard shortcut."""
    if key != shortcut.key:
        raise HTTPException(status_code=400, detail="Shortcut key mismatch")

    if key not in _keyboard_shortcuts:
        raise HTTPException(status_code=404, detail="Keyboard shortcut not found")

    _keyboard_shortcuts[key] = shortcut.model_dump()
    _save_help_data()
    return shortcut


@router.delete("/shortcuts/{key}")
async def delete_keyboard_shortcut(key: str):
    """Delete a keyboard shortcut."""
    if key not in _keyboard_shortcuts:
        raise HTTPException(status_code=404, detail="Keyboard shortcut not found")

    del _keyboard_shortcuts[key]
    _save_help_data()
    return {"message": "Keyboard shortcut deleted"}


@router.get("/search", response_model=HelpSearchResponse)
@cache_response(ttl=300)  # Cache for 5 minutes (search results are relatively static)
async def search_help(
    query: str = Query(..., description="Search query"),
    category: str | None = Query(None),
    panel_id: str | None = Query(None),
    limit: int = Query(50, ge=1, le=100),
):
    """Search help topics."""
    query_lower = query.lower()
    matching_topics = []

    for topic in _help_topics.values():
        # Filter by category
        if category and topic.get("category") != category:
            continue

        # Filter by panel
        if panel_id and topic.get("panel_id") not in (panel_id, None):
            continue

        # Search in title, content, and keywords
        title_match = query_lower in topic.get("title", "").lower()
        content_match = query_lower in topic.get("content", "").lower()
        keyword_match = any(query_lower in keyword.lower() for keyword in topic.get("keywords", []))

        if title_match or content_match or keyword_match:
            matching_topics.append(topic)

    # Sort by relevance (title matches first)
    matching_topics.sort(
        key=lambda t: (
            query_lower not in t.get("title", "").lower(),
            query_lower not in " ".join(t.get("keywords", [])).lower(),
        )
    )

    total = len(matching_topics)
    limited = matching_topics[:limit]

    return HelpSearchResponse(topics=[HelpTopic(**topic) for topic in limited], total=total)


@router.get("/shortcuts", response_model=list[KeyboardShortcut])
@cache_response(ttl=300)  # Cache for 5 minutes (shortcuts are relatively static)
async def get_keyboard_shortcuts(
    category: str | None = Query(None), panel_id: str | None = Query(None)
):
    """Get keyboard shortcuts, optionally filtered."""
    shortcuts = list(_keyboard_shortcuts.values())

    if category:
        shortcuts = [s for s in shortcuts if s.get("category") == category]

    if panel_id:
        shortcuts = [
            s for s in shortcuts if s.get("panel_id") == panel_id or s.get("panel_id") is None
        ]

    return [KeyboardShortcut(**shortcut) for shortcut in shortcuts]


@router.get("/categories")
@cache_response(ttl=600)  # Cache for 10 minutes (categories are static)
async def get_help_categories():
    """Get list of help categories."""
    categories = set()
    for topic in _help_topics.values():
        cat = topic.get("category")
        if cat:
            categories.add(cat)

    return {"categories": sorted(categories)}


@router.get("/panel/{panel_id}")
@cache_response(ttl=600)  # Cache for 10 minutes (panel help is static)
async def get_panel_help(panel_id: str):
    """Get help content for a specific panel."""
    topics = [
        HelpTopic(**topic) for topic in _help_topics.values() if topic.get("panel_id") == panel_id
    ]

    shortcuts = [
        KeyboardShortcut(**shortcut)
        for shortcut in _keyboard_shortcuts.values()
        if shortcut.get("panel_id") == panel_id
    ]

    return {"topics": topics, "shortcuts": shortcuts, "panel_id": panel_id}
