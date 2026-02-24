"""
Voice context router.

Task 2.4: Aggregates voice synthesis, cloning, effects, presets routes.
"""

from fastapi import APIRouter

router = APIRouter(tags=["Voice"])


def _register() -> None:
    """Register all voice-related routes. Called once at startup."""
    from backend.api.routes import (
        articulation,
        dubbing,
        emotion,
        emotion_style,
        ensemble,
        instant_cloning,
        multi_speaker_dubbing,
        multi_voice_generator,
        multilingual,
        presets,
        prosody,
        realtime_converter,
        rvc,
        ssml,
        text_speech_editor,
        translation,
        voice,
        voice_browser,
        voice_cloning_wizard,
        voice_effects,
        voice_morph,
        voice_speech,
    )

    router.include_router(voice.router)
    router.include_router(voice_cloning_wizard.router)
    router.include_router(voice_effects.router)
    router.include_router(voice_browser.router)
    router.include_router(voice_speech.router)
    router.include_router(voice_morph.router)
    router.include_router(presets.router)
    router.include_router(instant_cloning.router)
    router.include_router(multi_voice_generator.router)
    router.include_router(prosody.router)
    router.include_router(articulation.router)
    router.include_router(emotion.router)
    router.include_router(emotion_style.router)
    router.include_router(ssml.router)
    router.include_router(translation.router)
    router.include_router(multilingual.router)
    router.include_router(rvc.router)
    router.include_router(realtime_converter.router)
    router.include_router(ensemble.router)
    router.include_router(text_speech_editor.router)
    router.include_router(dubbing.router)
    router.include_router(multi_speaker_dubbing.router)


# Register on import
_register()
