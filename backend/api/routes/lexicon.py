"""
Pronunciation Lexicon Routes

Endpoints for managing pronunciation lexicons and custom word pronunciations.
"""

from __future__ import annotations

import logging

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from backend.ml.models.engine_service import get_engine_service

from ..optimization import cache_response
from ..voice_speech import Phonemizer

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/lexicon", tags=["lexicon"])

# In-memory lexicons (replace with database in production)
_lexicons: dict[str, dict] = {}
_lexicon_entries: dict[str, list[dict]] = {}  # lexicon_id -> entries


class LexiconEntry(BaseModel):
    """A lexicon entry for word pronunciation."""

    word: str
    pronunciation: str  # IPA or phoneme string
    part_of_speech: str | None = None
    language: str = "en"
    notes: str | None = None


class Lexicon(BaseModel):
    """A pronunciation lexicon."""

    lexicon_id: str
    name: str
    language: str = "en"
    description: str | None = None
    entry_count: int = 0
    created: str
    modified: str


class LexiconCreateRequest(BaseModel):
    """Request to create a lexicon."""

    name: str
    language: str = "en"
    description: str | None = None


class LexiconEntryCreateRequest(BaseModel):
    """Request to create a lexicon entry."""

    word: str
    pronunciation: str
    part_of_speech: str | None = None
    notes: str | None = None


class LexiconSearchRequest(BaseModel):
    """Request to search lexicon entries."""

    query: str
    language: str | None = None
    part_of_speech: str | None = None


@router.post("/lexicons", response_model=Lexicon)
async def create_lexicon(request: LexiconCreateRequest):
    """Create a new pronunciation lexicon."""
    import uuid
    from datetime import datetime

    try:
        lexicon_id = f"lexicon-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        lexicon = Lexicon(
            lexicon_id=lexicon_id,
            name=request.name,
            language=request.language,
            description=request.description,
            entry_count=0,
            created=now,
            modified=now,
        )

        _lexicons[lexicon_id] = lexicon.model_dump()
        _lexicon_entries[lexicon_id] = []
        logger.info(f"Created lexicon: {lexicon_id}")

        return lexicon
    except Exception as e:
        logger.error(f"Failed to create lexicon: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to create lexicon: {e!s}",
        ) from e


@router.get("/lexicons", response_model=list[Lexicon])
@cache_response(ttl=60)  # Cache for 60 seconds (lexicon list doesn't change frequently)
async def list_lexicons(language: str | None = None):
    """List all lexicons, optionally filtered by language."""
    lexicons = list(_lexicons.values())
    if language:
        lexicons = [lex for lex in lexicons if lex.get("language") == language]

    # Update entry counts
    for lexicon in lexicons:
        lexicon_id = lexicon["lexicon_id"]
        lexicon["entry_count"] = len(_lexicon_entries.get(lexicon_id, []))

    return [Lexicon(**lex) for lex in lexicons]


@router.get("/lexicons/{lexicon_id}", response_model=Lexicon)
@cache_response(ttl=300)  # Cache for 5 minutes (lexicon info is relatively static)
async def get_lexicon(lexicon_id: str):
    """Get a lexicon by ID."""
    if lexicon_id not in _lexicons:
        raise HTTPException(status_code=404, detail="Lexicon not found")

    lexicon_data = _lexicons[lexicon_id].copy()
    lexicon_data["entry_count"] = len(_lexicon_entries.get(lexicon_id, []))
    return Lexicon(**lexicon_data)


@router.put("/lexicons/{lexicon_id}", response_model=Lexicon)
async def update_lexicon(lexicon_id: str, request: LexiconCreateRequest):
    """Update a lexicon."""
    if lexicon_id not in _lexicons:
        raise HTTPException(status_code=404, detail="Lexicon not found")

    from datetime import datetime

    lexicon_data = _lexicons[lexicon_id]
    lexicon = Lexicon(**lexicon_data)
    lexicon.name = request.name
    lexicon.language = request.language
    lexicon.description = request.description
    lexicon.modified = datetime.utcnow().isoformat()
    lexicon.entry_count = len(_lexicon_entries.get(lexicon_id, []))

    _lexicons[lexicon_id] = lexicon.model_dump()
    logger.info(f"Updated lexicon: {lexicon_id}")

    return lexicon


@router.delete("/lexicons/{lexicon_id}")
async def delete_lexicon(lexicon_id: str):
    """Delete a lexicon."""
    if lexicon_id not in _lexicons:
        raise HTTPException(status_code=404, detail="Lexicon not found")

    del _lexicons[lexicon_id]
    _lexicon_entries.pop(lexicon_id, None)
    logger.info(f"Deleted lexicon: {lexicon_id}")
    return {"success": True}


@router.post("/lexicons/{lexicon_id}/entries", response_model=LexiconEntry)
async def create_lexicon_entry(lexicon_id: str, request: LexiconEntryCreateRequest):
    """Add an entry to a lexicon."""
    if lexicon_id not in _lexicons:
        raise HTTPException(status_code=404, detail="Lexicon not found")

    from datetime import datetime

    lexicon = Lexicon(**_lexicons[lexicon_id])
    entry = LexiconEntry(
        word=request.word,
        pronunciation=request.pronunciation,
        part_of_speech=request.part_of_speech,
        language=lexicon.language,
        notes=request.notes,
    )

    if lexicon_id not in _lexicon_entries:
        _lexicon_entries[lexicon_id] = []

    # Check for duplicate
    existing = [e for e in _lexicon_entries[lexicon_id] if e.get("word") == request.word]
    if existing:
        raise HTTPException(
            status_code=400,
            detail=f"Entry for word '{request.word}' already exists",
        )

    _lexicon_entries[lexicon_id].append(entry.model_dump())
    lexicon.entry_count = len(_lexicon_entries[lexicon_id])
    lexicon.modified = datetime.utcnow().isoformat()
    _lexicons[lexicon_id] = lexicon.model_dump()

    logger.info(f"Added entry to lexicon {lexicon_id}: {request.word}")
    return entry


@router.get("/lexicons/{lexicon_id}/entries", response_model=list[LexiconEntry])
@cache_response(ttl=60)  # Cache for 60 seconds (entries may change but not frequently)
async def list_lexicon_entries(
    lexicon_id: str,
    word: str | None = None,
    part_of_speech: str | None = None,
):
    """List entries in a lexicon."""
    if lexicon_id not in _lexicons:
        raise HTTPException(status_code=404, detail="Lexicon not found")

    entries = _lexicon_entries.get(lexicon_id, [])

    # Filter by word if provided
    if word:
        entries = [e for e in entries if e.get("word") == word]

    # Filter by part of speech if provided
    if part_of_speech:
        entries = [e for e in entries if e.get("part_of_speech") == part_of_speech]

    return [LexiconEntry(**e) for e in entries]


@router.put("/lexicons/{lexicon_id}/entries/{word}", response_model=LexiconEntry)
async def update_lexicon_entry(lexicon_id: str, word: str, request: LexiconEntryCreateRequest):
    """Update a lexicon entry."""
    if lexicon_id not in _lexicons:
        raise HTTPException(status_code=404, detail="Lexicon not found")

    if lexicon_id not in _lexicon_entries:
        raise HTTPException(status_code=404, detail="Entry not found")

    entries = _lexicon_entries[lexicon_id]
    entry_index = next((i for i, e in enumerate(entries) if e.get("word") == word), None)

    if entry_index is None:
        raise HTTPException(status_code=404, detail="Entry not found")

    from datetime import datetime

    lexicon = Lexicon(**_lexicons[lexicon_id])
    entry = LexiconEntry(
        word=request.word,
        pronunciation=request.pronunciation,
        part_of_speech=request.part_of_speech,
        language=lexicon.language,
        notes=request.notes,
    )

    entries[entry_index] = entry.model_dump()
    lexicon.modified = datetime.utcnow().isoformat()
    _lexicons[lexicon_id] = lexicon.model_dump()

    logger.info(f"Updated entry in lexicon {lexicon_id}: {word}")
    return entry


@router.delete("/lexicons/{lexicon_id}/entries/{word}")
async def delete_lexicon_entry(lexicon_id: str, word: str):
    """Delete a lexicon entry."""
    if lexicon_id not in _lexicons:
        raise HTTPException(status_code=404, detail="Lexicon not found")

    if lexicon_id not in _lexicon_entries:
        raise HTTPException(status_code=404, detail="Entry not found")

    entries = _lexicon_entries[lexicon_id]
    entry_index = next((i for i, e in enumerate(entries) if e.get("word") == word), None)

    if entry_index is None:
        raise HTTPException(status_code=404, detail="Entry not found")

    from datetime import datetime

    entries.pop(entry_index)
    lexicon = Lexicon(**_lexicons[lexicon_id])
    lexicon.entry_count = len(entries)
    lexicon.modified = datetime.utcnow().isoformat()
    _lexicons[lexicon_id] = lexicon.model_dump()

    logger.info(f"Deleted entry from lexicon {lexicon_id}: {word}")
    return {"success": True}


@router.post("/search")
async def search_lexicon_entries(request: LexiconSearchRequest):
    """Search across all lexicons for entries matching query."""
    results = []

    for lexicon_id, entries in _lexicon_entries.items():
        lexicon = Lexicon(**_lexicons[lexicon_id])

        # Filter by language if specified
        if request.language and lexicon.language != request.language:
            continue

        # Search entries
        for entry_data in entries:
            entry = LexiconEntry(**entry_data)

            # Filter by part of speech if specified
            if request.part_of_speech and entry.part_of_speech != request.part_of_speech:
                continue

            # Match word or pronunciation
            query_lower = request.query.lower()
            if query_lower in entry.word.lower() or query_lower in entry.pronunciation.lower():
                results.append(
                    {
                        "lexicon_id": lexicon_id,
                        "lexicon_name": lexicon.name,
                        "entry": entry.model_dump(),
                    }
                )

    return {"results": results, "count": len(results)}


# Simplified endpoints for Pronunciation Lexicon Panel
@router.post("/add")
async def add_lexicon_entry(request: LexiconEntryCreateRequest):
    """Add a lexicon entry (simplified endpoint for panel)."""
    from datetime import datetime

    try:
        # Use default lexicon or create one if none exists
        default_lexicon_id = "default-lexicon"
        if default_lexicon_id not in _lexicons:
            now = datetime.utcnow().isoformat()
            default_lexicon = Lexicon(
                lexicon_id=default_lexicon_id,
                name="Default Lexicon",
                language=request.language if hasattr(request, "language") else "en",
                description="Default pronunciation lexicon",
                entry_count=0,
                created=now,
                modified=now,
            )
            _lexicons[default_lexicon_id] = default_lexicon.model_dump()
            _lexicon_entries[default_lexicon_id] = []

        # Add entry
        lexicon = Lexicon(**_lexicons[default_lexicon_id])
        entry = LexiconEntry(
            word=request.word,
            pronunciation=request.pronunciation,
            part_of_speech=request.part_of_speech,
            language=lexicon.language,
            notes=request.notes,
        )

        # Check for duplicates
        if default_lexicon_id in _lexicon_entries:
            existing = [
                e
                for e in _lexicon_entries[default_lexicon_id]
                if e.get("word", "").lower() == request.word.lower()
            ]
            if existing:
                # Update existing entry
                for i, e in enumerate(_lexicon_entries[default_lexicon_id]):
                    if e.get("word", "").lower() == request.word.lower():
                        _lexicon_entries[default_lexicon_id][i] = entry.model_dump()
                        break
            else:
                _lexicon_entries[default_lexicon_id].append(entry.model_dump())

        lexicon.entry_count = len(_lexicon_entries[default_lexicon_id])
        lexicon.modified = datetime.utcnow().isoformat()
        _lexicons[default_lexicon_id] = lexicon.model_dump()

        logger.info(f"Added lexicon entry: {request.word}")
        return entry
    except Exception as e:
        logger.error(f"Failed to add lexicon entry: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to add lexicon entry: {e!s}",
        ) from e


@router.put("/update")
async def update_lexicon_entry_simple(request: LexiconEntryCreateRequest):
    """Update a lexicon entry (simplified endpoint for panel)."""
    from datetime import datetime

    try:
        default_lexicon_id = "default-lexicon"
        if default_lexicon_id not in _lexicons:
            raise HTTPException(
                status_code=404,
                detail="Lexicon not found",
            )

        if default_lexicon_id not in _lexicon_entries:
            _lexicon_entries[default_lexicon_id] = []

        entries = _lexicon_entries[default_lexicon_id]
        found = False

        for i, entry_data in enumerate(entries):
            if entry_data.get("word", "").lower() == request.word.lower():
                lexicon = Lexicon(**_lexicons[default_lexicon_id])
                entry = LexiconEntry(
                    word=request.word,
                    pronunciation=request.pronunciation,
                    part_of_speech=request.part_of_speech,
                    language=lexicon.language,
                    notes=request.notes,
                )
                entries[i] = entry.model_dump()
                found = True
                break

        if not found:
            raise HTTPException(
                status_code=404,
                detail=f"Entry '{request.word}' not found",
            )

        lexicon = Lexicon(**_lexicons[default_lexicon_id])
        lexicon.modified = datetime.utcnow().isoformat()
        _lexicons[default_lexicon_id] = lexicon.model_dump()

        # Find and return the updated entry
        updated_entry_data = next(
            (e for e in entries if e.get("word", "").lower() == request.word.lower()),
            None,
        )
        if updated_entry_data:
            logger.info(f"Updated lexicon entry: {request.word}")
            return LexiconEntry(**updated_entry_data)
        else:
            raise HTTPException(
                status_code=500,
                detail="Failed to retrieve updated entry",
            )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to update lexicon entry: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to update lexicon entry: {e!s}",
        ) from e


@router.delete("/remove/{word}")
async def remove_lexicon_entry(word: str):
    """Remove a lexicon entry (simplified endpoint for panel)."""
    from datetime import datetime

    try:
        default_lexicon_id = "default-lexicon"
        if default_lexicon_id not in _lexicons:
            raise HTTPException(
                status_code=404,
                detail="Lexicon not found",
            )

        if default_lexicon_id not in _lexicon_entries:
            raise HTTPException(
                status_code=404,
                detail=f"Entry '{word}' not found",
            )

        entries = _lexicon_entries[default_lexicon_id]
        original_count = len(entries)
        entries[:] = [e for e in entries if e.get("word", "").lower() != word.lower()]

        if len(entries) == original_count:
            raise HTTPException(
                status_code=404,
                detail=f"Entry '{word}' not found",
            )

        lexicon = Lexicon(**_lexicons[default_lexicon_id])
        lexicon.entry_count = len(entries)
        lexicon.modified = datetime.utcnow().isoformat()
        _lexicons[default_lexicon_id] = lexicon.model_dump()

        logger.info(f"Removed lexicon entry: {word}")
        return {"success": True}
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to remove lexicon entry: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to remove lexicon entry: {e!s}",
        ) from e


@router.get("/list")
async def list_lexicon_entries_simple(language: str | None = None):
    """List all lexicon entries (simplified endpoint for panel)."""
    try:
        default_lexicon_id = "default-lexicon"
        if default_lexicon_id not in _lexicons:
            return []

        entries = _lexicon_entries.get(default_lexicon_id, [])
        result = [LexiconEntry(**e) for e in entries]

        if language:
            result = [e for e in result if e.language == language]

        return result
    except Exception as e:
        logger.error(f"Failed to list lexicon entries: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to list lexicon entries: {e!s}",
        ) from e


class PhonemeEstimateRequest(BaseModel):
    """Request to estimate phonemes."""

    word: str | None = None
    audio_id: str | None = None
    language: str = "en"


@router.post("/phoneme")
async def estimate_phonemes(request: PhonemeEstimateRequest):
    """Estimate phonemes from word or audio."""
    try:
        if request.word:
            word_lower = request.word.lower().strip()

            # Try to use phonemization libraries first
            pronunciation = None
            confidence = 0.5
            method = "estimation"

            # Try phonemizer library first (best quality)
            try:
                phonemizer = Phonemizer()
                if phonemizer.phonemizer_available:
                    # Try phonemizer with espeak backend
                    phonemes = phonemizer.phonemize_with_phonemizer(
                        request.word,
                        language=request.language,
                        backend="espeak",
                        separator=" ",
                    )
                    if phonemes and phonemes.strip():
                        pronunciation = f"/{phonemes.strip()}/"
                        confidence = 0.9
                        method = "phonemizer"
            except Exception as e:
                logger.debug(f"Phonemizer failed: {e}")

            # Try gruut as alternative
            if not pronunciation:
                try:
                    phonemizer = Phonemizer()
                    if phonemizer.gruut_available:
                        words = phonemizer.phonemize_with_gruut(
                            request.word,
                            language=request.language,
                        )
                        if words and len(words) > 0:
                            phonemes_str = words[0].get("phonemes_str", "")
                            if phonemes_str:
                                pronunciation = f"/{phonemes_str}/"
                                confidence = 0.85
                                method = "gruut"
                except Exception as e:
                    logger.debug(f"Gruut failed: {e}")

            # Fallback: Try espeak-ng system command
            if not pronunciation:
                try:
                    import subprocess

                    result = subprocess.run(
                        ["espeak", "-q", "--ipa", "-v", request.language, word_lower],
                        capture_output=True,
                        text=True,
                        timeout=2,
                    )
                    if result.returncode == 0 and result.stdout.strip():
                        pronunciation = f"/{result.stdout.strip()}/"
                        confidence = 0.85
                        method = "espeak"
                except (ImportError, FileNotFoundError, subprocess.TimeoutExpired):
                    ...

            # Fallback: Use common phoneme mappings
            if not pronunciation:
                phoneme_map = {
                    "gui": "/ˈɡuː.i/",
                    "api": "/ˈeɪ.pi.aɪ/",
                    "ui": "/juː.aɪ/",
                    "http": "/eɪtʃ.tiː.tiː.piː/",
                    "https": "/eɪtʃ.tiː.tiː.piː.ɛs/",
                    "url": "/juː.ɑː.ɛl/",
                    "www": "/dʌb.əl.juː.dʌb.əl.juː.dʌb.əl.juː/",
                }

                if word_lower in phoneme_map:
                    pronunciation = phoneme_map[word_lower]
                    confidence = 0.8
                else:
                    # Simple rule-based approximation
                    # Convert common letter patterns to phonemes
                    pronunciation = _estimate_phonemes_simple(word_lower)
                    confidence = 0.6

            return {
                "word": request.word,
                "pronunciation": pronunciation,
                "confidence": confidence,
                "method": method,
            }

        elif request.audio_id:
            # Use ASR to transcribe, then estimate phonemes
            try:
                import os

                from .voice import _audio_storage

                if request.audio_id not in _audio_storage:
                    raise HTTPException(
                        status_code=404,
                        detail=f"Audio file '{request.audio_id}' not found",
                    )

                audio_path = _audio_storage[request.audio_id]
                if not os.path.exists(audio_path):
                    raise HTTPException(
                        status_code=404,
                        detail=f"Audio file at '{audio_path}' does not exist",
                    )

                # Try to use Whisper for transcription (ADR-008 compliant)
                try:
                    engine_service = get_engine_service()
                    engine = engine_service.get_whisper_engine()
                    if not engine:
                        raise Exception("Whisper engine not available")
                    transcription = engine.transcribe(audio_path, language=request.language)

                    if transcription and transcription.get("text"):
                        word = transcription["text"].strip()
                        # Estimate phonemes from transcribed word
                        pronunciation = _estimate_phonemes_simple(word.lower())
                        return {
                            "word": word,
                            "pronunciation": pronunciation,
                            "confidence": 0.7,
                            "method": "audio_analysis",
                        }
                except Exception as e:
                    logger.warning(f"Failed to transcribe audio: {e}")

                # Fallback
                return {
                    "word": "unknown",
                    "pronunciation": "/ʌn.noʊn/",
                    "confidence": 0.3,
                    "method": "fallback",
                }
            except Exception as e:
                logger.error(f"Failed to process audio for phoneme estimation: {e}")
                raise HTTPException(
                    status_code=500,
                    detail=f"Failed to estimate phonemes from audio: {e!s}",
                )

        else:
            raise HTTPException(
                status_code=400,
                detail="Either 'word' or 'audio_id' must be provided",
            )
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Failed to estimate phonemes: {e}")
        raise HTTPException(
            status_code=500,
            detail=f"Failed to estimate phonemes: {e!s}",
        ) from e


def _estimate_phonemes_simple(word: str) -> str:
    """Simple rule-based phoneme estimation."""
    # Basic letter-to-phoneme rules for English
    # This is a simplified approximation - production would use proper G2P model

    phonemes = []
    i = 0
    word_len = len(word)

    while i < word_len:
        char = word[i]
        next_char = word[i + 1] if i + 1 < word_len else ""

        # Common patterns
        if char == "a":
            if next_char == "i":
                phonemes.append("eɪ")
                i += 2
            elif next_char == "u":
                phonemes.append("ɔː")
                i += 2
            else:
                phonemes.append("æ")
                i += 1
        elif char == "e":
            if next_char == "e":
                phonemes.append("iː")
                i += 2
            else:
                phonemes.append("ɛ")
                i += 1
        elif char == "i":
            if next_char == "e":
                phonemes.append("aɪ")
                i += 2
            else:
                phonemes.append("ɪ")
                i += 1
        elif char == "o":
            if next_char == "o":
                phonemes.append("uː")
                i += 2
            elif next_char == "u":
                phonemes.append("aʊ")
                i += 2
            else:
                phonemes.append("ɒ")
                i += 1
        elif char == "u":
            phonemes.append("juː")
            i += 1
        elif char == "c":
            if next_char == "h":
                phonemes.append("tʃ")
                i += 2
            else:
                phonemes.append("k")
                i += 1
        elif char == "g":
            phonemes.append("ɡ")
            i += 1
        elif char == "p":
            phonemes.append("p")
            i += 1
        elif char == "t":
            phonemes.append("t")
            i += 1
        elif char == "k":
            phonemes.append("k")
            i += 1
        elif char == "b":
            phonemes.append("b")
            i += 1
        elif char == "d":
            phonemes.append("d")
            i += 1
        elif char == "f":
            phonemes.append("f")
            i += 1
        elif char == "v":
            phonemes.append("v")
            i += 1
        elif char == "s":
            phonemes.append("s")
            i += 1
        elif char == "z":
            phonemes.append("z")
            i += 1
        elif char == "h":
            phonemes.append("h")
            i += 1
        elif char == "m":
            phonemes.append("m")
            i += 1
        elif char == "n":
            phonemes.append("n")
            i += 1
        elif char == "l":
            phonemes.append("l")
            i += 1
        elif char == "r":
            phonemes.append("r")
            i += 1
        elif char == "w":
            phonemes.append("w")
            i += 1
        elif char == "y":
            phonemes.append("j")
            i += 1
        else:
            # Unknown character, skip
            i += 1

    if not phonemes:
        return f"/{word}/"

    return f"/{'.'.join(phonemes)}/"
