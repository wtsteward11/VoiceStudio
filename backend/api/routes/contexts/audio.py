"""
Audio context router.

Task 2.4: Aggregates audio processing, analysis, effects routes.
"""

from fastapi import APIRouter

router = APIRouter(tags=["Audio"])


def _register() -> None:
    from backend.api.routes import (
        advanced_spectrogram,
        audio,
        audio_analysis,
        audio_audit,
        effects,
        formant,
        granular,
        macros,
        mix_assistant,
        mixer,
        nr,
        recording,
        repair,
        sonography,
        spatial_audio,
        spectral,
        spectrogram,
        waveform,
    )

    router.include_router(audio.router)
    router.include_router(audio_analysis.router)
    router.include_router(audio_audit.router)
    router.include_router(effects.router)
    router.include_router(waveform.router)
    router.include_router(spectrogram.router)
    router.include_router(advanced_spectrogram.router)
    router.include_router(spectral.router)
    router.include_router(sonography.router)
    router.include_router(granular.router)
    router.include_router(formant.router)
    router.include_router(nr.router)
    router.include_router(spatial_audio.router)
    router.include_router(recording.router)
    router.include_router(macros.router)
    router.include_router(mix_assistant.router)
    router.include_router(mixer.router)
    router.include_router(repair.router)
