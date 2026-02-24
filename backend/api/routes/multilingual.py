"""
Multilingual Support Routes

Endpoints for managing multi-language voice synthesis and translation.
"""

from __future__ import annotations

import logging
import asyncio

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/multilingual", tags=["multilingual"])

# In-memory language configurations (replace with database in production)
_language_configs: dict[str, dict] = {}
_state_lock = asyncio.Lock()


class LanguageConfig(BaseModel):
    """Language configuration for synthesis."""

    id: str
    language_code: str  # ISO 639-1 code (e.g., "en", "es", "fr")
    language_name: str
    profile_id: str
    voice_settings: dict[str, float] = {}
    created: str  # ISO datetime string


class MultilingualSynthesisRequest(BaseModel):
    """Request for multilingual synthesis."""

    text: str
    source_language: str | None = None  # Auto-detect if None
    target_languages: list[str]  # List of language codes
    profile_ids: dict[str, str] = {}  # language_code -> profile_id
    preserve_emotion: bool = True
    preserve_style: bool = True


class MultilingualSynthesisResponse(BaseModel):
    """Response from multilingual synthesis."""

    audio_ids: dict[str, str]  # language_code -> audio_id
    detected_language: str | None = None
    message: str


class TranslationRequest(BaseModel):
    """Request for text translation."""

    text: str
    source_language: str
    target_language: str


class TranslationResponse(BaseModel):
    """Response from translation."""

    translated_text: str
    source_language: str
    target_language: str
    confidence: float


@router.get("/languages", response_model=list[LanguageConfig])
async def get_language_configs(profile_id: str | None = None):
    """Get all language configurations, optionally filtered by profile."""
    configs = list(_language_configs.values())

    if profile_id:
        configs = [c for c in configs if c.get("profile_id") == profile_id]

    return [
        LanguageConfig(
            id=str(c.get("id", "")),
            language_code=str(c.get("language_code", "")),
            language_name=str(c.get("language_name", "")),
            profile_id=str(c.get("profile_id", "")),
            voice_settings=c.get("voice_settings", {}),
            created=str(c.get("created", "")),
        )
        for c in configs
    ]


@router.get("/languages/{config_id}", response_model=LanguageConfig)
async def get_language_config(config_id: str):
    """Get a specific language configuration."""
    if config_id not in _language_configs:
        raise HTTPException(status_code=404, detail="Language config not found")

    c = _language_configs[config_id]
    return LanguageConfig(
        id=str(c.get("id", "")),
        language_code=str(c.get("language_code", "")),
        language_name=str(c.get("language_name", "")),
        profile_id=str(c.get("profile_id", "")),
        voice_settings=c.get("voice_settings", {}),
        created=str(c.get("created", "")),
    )


@router.post("/synthesize", response_model=MultilingualSynthesisResponse)
async def synthesize_multilingual(request: MultilingualSynthesisRequest):
    """Synthesize text in multiple languages."""
    import uuid

    # In a real implementation, this would:
    # 1. Detect source language if not provided
    # 2. Translate text to target languages
    # 3. Synthesize each translation
    # 4. Return audio IDs for each language

    audio_ids = {}
    for lang_code in request.target_languages:
        audio_ids[lang_code] = f"audio-{uuid.uuid4().hex[:8]}"

    return MultilingualSynthesisResponse(
        audio_ids=audio_ids,
        detected_language=request.source_language or "en",
        message="Multilingual synthesis completed",
    )


@router.post("/translate", response_model=TranslationResponse)
async def translate_text(request: TranslationRequest):
    """
    Translate text between languages.

    This implementation supports multiple translation backends:
    - deep-translator (Google Translate, DeepL, etc.)
    - argos-translate (offline translation)

    Falls back to error if no translation service is available.
    """
    # Normalize language codes (lowercase)
    source_lang = request.source_language.lower() if request.source_language else "auto"
    target_lang = request.target_language.lower()

    # If source and target are the same, return original text
    if source_lang == target_lang and source_lang != "auto":
        return TranslationResponse(
            translated_text=request.text,
            source_language=request.source_language,
            target_language=request.target_language,
            confidence=1.0,
        )

    # Try deep-translator (supports Google Translate, DeepL, etc.)
    try:
        from deep_translator import GoogleTranslator

        # Try Google Translate first (free, no API key needed)
        try:
            # Map language codes if needed
            source_code = source_lang if source_lang != "auto" else "auto"
            target_code = target_lang

            translator = GoogleTranslator(source=source_code, target=target_code)
            translated = translator.translate(request.text)

            logger.info(
                "Translated text using Google Translate: " f"{source_lang} -> {target_lang}"
            )

            return TranslationResponse(
                translated_text=translated,
                source_language=request.source_language,
                target_language=request.target_language,
                confidence=0.9,  # Google Translate confidence estimate
            )
        except Exception as google_error:
            logger.debug(f"Google Translate failed: {google_error}, trying alternatives")
            raise

    except ImportError:
        # deep-translator not installed, try argos-translate (offline)
        try:
            import argostranslate.package
            import argostranslate.translate

            # Check if translation package is installed
            installed_languages = argostranslate.translate.get_installed_languages()
            source_lang_obj = None
            target_lang_obj = None

            # Find matching language objects
            for lang in installed_languages:
                if lang.code == source_lang or (source_lang == "auto" and "en" in lang.code):
                    source_lang_obj = lang
                if lang.code == target_lang:
                    target_lang_obj = lang

            if source_lang == "auto":
                # Try to detect language or default to English
                source_lang_obj = next(
                    (lang for lang in installed_languages if "en" in lang.code),
                    installed_languages[0] if installed_languages else None,
                )

            if not source_lang_obj or not target_lang_obj:
                lang_codes = [lang.code for lang in installed_languages]
                raise ValueError(
                    f"Language pair not available. " f"Installed languages: {lang_codes}"
                )

            translated = argostranslate.translate.translate(
                request.text, source_lang_obj.code, target_lang_obj.code
            )

            logger.info(
                f"Translated text using Argos Translate (offline): "
                f"{source_lang_obj.code} -> {target_lang_obj.code}"
            )

            return TranslationResponse(
                translated_text=translated,
                source_language=request.source_language,
                target_language=request.target_language,
                confidence=0.85,  # Offline translation confidence estimate
            )

        except ImportError:
            # No translation libraries available
            logger.error(
                "No translation libraries available. "
                "Install one of: pip install deep-translator "
                "OR pip install argostranslate"
            )
            raise HTTPException(
                status_code=503,
                detail=(
                    "Translation service is not available. "
                    "Please install a translation library: "
                    "pip install deep-translator (for online translation) "
                    "OR pip install argostranslate (for offline translation)."
                ),
            )
        except Exception as argos_error:
            logger.error(f"Argos Translate failed: {argos_error}")
            raise HTTPException(
                status_code=500,
                detail=f"Translation failed: {argos_error!s}. "
                f"Please ensure language packages are installed.",
            )
    except Exception as e:
        logger.error(f"Translation failed: {e}", exc_info=True)
        raise HTTPException(
            status_code=500,
            detail=(
                f"Translation failed: {e!s}. "
                "Please check your internet connection "
                "or install translation packages."
            ),
        )


@router.get("/supported")
async def get_supported_languages():
    """Get list of supported languages."""
    return {
        "languages": [
            {"code": "en", "name": "English"},
            {"code": "es", "name": "Spanish"},
            {"code": "fr", "name": "French"},
            {"code": "de", "name": "German"},
            {"code": "it", "name": "Italian"},
            {"code": "pt", "name": "Portuguese"},
            {"code": "ru", "name": "Russian"},
            {"code": "ja", "name": "Japanese"},
            {"code": "ko", "name": "Korean"},
            {"code": "zh", "name": "Chinese"},
        ]
    }
