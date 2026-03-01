"""
VoiceStudio Backend Feature Verification Script.

Enumerates all API route modules, calls health/status endpoints,
and generates a feature availability matrix report.

Usage:
    py -3.12 scripts/verify_backend_features.py [--output report.json] [--base-url http://127.0.0.1:8000]
"""

import argparse
import json
import sys
import time
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any

# Add project root to path
sys.path.insert(0, str(Path(__file__).parent.parent))

try:
    import requests
except ImportError:
    print("ERROR: requests module not installed. Run: pip install requests")
    sys.exit(1)


@dataclass
class EndpointResult:
    """Result of testing an endpoint."""
    endpoint: str
    method: str
    status: int | None
    response_time_ms: float
    success: bool
    error: str | None = None
    response_preview: str | None = None


@dataclass
class RouteModule:
    """Information about a route module."""
    name: str
    file_path: str
    endpoints_tested: list[EndpointResult] = field(default_factory=list)
    healthy: bool = False
    notes: str = ""


# Known API route modules and their key endpoints - COMPREHENSIVE LIST (118 modules, 600+ endpoints)
ROUTE_MODULES = {
    # ==================== CORE AUDIO/VOICE ====================
    "audio": {
        "file": "audio.py",
        "endpoints": [
            ("GET", "/api/audio/formats"),
            ("GET", "/api/audio/devices"),
            ("GET", "/api/audio/info"),
            ("POST", "/api/audio/upload"),
            ("POST", "/api/audio/convert"),
            ("GET", "/api/audio/download"),
        ],
        "category": "Core Audio",
    },
    "voice": {
        "file": "voice.py",
        "endpoints": [
            ("GET", "/api/voice/engines"),
            ("POST", "/api/voice/synthesize"),
            ("POST", "/api/voice/analyze"),
            ("GET", "/api/voice/languages"),
            ("GET", "/api/voice/speakers"),
            ("POST", "/api/voice/clone"),
        ],
        "category": "Core Voice",
    },
    "voice_cloning_wizard": {
        "file": "voice_cloning_wizard.py",
        "endpoints": [
            ("GET", "/api/voice-cloning/status"),
            ("POST", "/api/voice-cloning/start"),
            ("POST", "/api/voice-cloning/upload-reference"),
            ("POST", "/api/voice-cloning/analyze"),
            ("POST", "/api/voice-cloning/clone"),
            ("GET", "/api/voice-cloning/progress"),
        ],
        "category": "Voice Cloning",
    },
    "profiles": {
        "file": "profiles.py",
        "endpoints": [
            ("GET", "/api/profiles"),
            ("POST", "/api/profiles"),
            ("GET", "/api/profiles/{id}"),
            ("PUT", "/api/profiles/{id}"),
            ("DELETE", "/api/profiles/{id}"),
        ],
        "category": "Profiles",
    },
    "transcribe": {
        "file": "transcribe.py",
        "endpoints": [
            ("GET", "/api/transcribe/engines"),
            ("POST", "/api/transcribe"),
            ("GET", "/api/transcribe/languages"),
            ("GET", "/api/transcribe/result/{id}"),
            ("POST", "/api/transcribe/detect-language"),
        ],
        "category": "Transcription",
    },

    # ==================== ENGINES ====================
    "engine": {
        "file": "engine.py",
        "endpoints": [
            ("GET", "/api/engine/list"),
            ("GET", "/api/engine/status"),
            ("GET", "/api/engine/capabilities"),
        ],
        "category": "Engines",
    },
    "engines": {
        "file": "engines.py",
        "endpoints": [
            ("GET", "/api/engines"),
            ("GET", "/api/engines/status"),
            ("GET", "/api/engines/{type}"),
            ("GET", "/api/engines/{engine}/capabilities"),
            ("POST", "/api/engines/{engine}/init"),
            ("POST", "/api/engines/{engine}/unload"),
        ],
        "category": "Engines",
    },
    "engine_audit": {
        "file": "engine_audit.py",
        "endpoints": [
            ("GET", "/api/engine-audit/status"),
            ("GET", "/api/engine-audit/history"),
            ("POST", "/api/engine-audit/run"),
        ],
        "category": "Engine Audit",
    },

    # ==================== JOBS & PROCESSING ====================
    "jobs": {
        "file": "jobs.py",
        "endpoints": [
            ("GET", "/api/jobs"),
            ("GET", "/api/jobs/{id}"),
            ("POST", "/api/jobs/cancel/{id}"),
            ("DELETE", "/api/jobs/{id}"),
            ("GET", "/api/jobs/active"),
            ("GET", "/api/jobs/history"),
        ],
        "category": "Jobs",
    },
    "batch": {
        "file": "batch.py",
        "endpoints": [
            ("GET", "/api/batch/status"),
            ("POST", "/api/batch/synthesis"),
            ("POST", "/api/batch/transcription"),
            ("GET", "/api/batch/{id}/progress"),
            ("POST", "/api/batch/{id}/cancel"),
        ],
        "category": "Batch Processing",
    },
    "pipeline": {
        "file": "pipeline.py",
        "endpoints": [
            ("GET", "/api/pipeline/status"),
            ("POST", "/api/pipeline/run"),
            ("GET", "/api/pipeline/templates"),
        ],
        "category": "Pipeline",
    },
    "automation": {
        "file": "automation.py",
        "endpoints": [
            ("GET", "/api/automation/workflows"),
            ("POST", "/api/automation/execute"),
            ("GET", "/api/automation/status"),
            ("GET", "/api/automation/history"),
        ],
        "category": "Automation",
    },
    "workflows": {
        "file": "workflows.py",
        "endpoints": [
            ("GET", "/api/workflows"),
            ("POST", "/api/workflows"),
            ("GET", "/api/workflows/{id}"),
            ("PUT", "/api/workflows/{id}"),
            ("DELETE", "/api/workflows/{id}"),
        ],
        "category": "Workflows",
    },
    "macros": {
        "file": "macros.py",
        "endpoints": [
            ("GET", "/api/macros"),
            ("POST", "/api/macros"),
            ("GET", "/api/macros/{id}"),
            ("PUT", "/api/macros/{id}"),
            ("DELETE", "/api/macros/{id}"),
            ("POST", "/api/macros/{id}/execute"),
        ],
        "category": "Macros",
    },

    # ==================== LIBRARY & MEDIA ====================
    "library": {
        "file": "library.py",
        "endpoints": [
            ("GET", "/api/library/items"),
            ("GET", "/api/library/stats"),
            ("POST", "/api/library/import"),
            ("DELETE", "/api/library/{id}"),
            ("GET", "/api/library/search"),
        ],
        "category": "Library",
    },
    "recording": {
        "file": "recording.py",
        "endpoints": [
            ("POST", "/api/recording/start"),
            ("POST", "/api/recording/stop"),
            ("GET", "/api/recording/status"),
            ("GET", "/api/recording/devices"),
        ],
        "category": "Recording",
    },
    "tags": {
        "file": "tags.py",
        "endpoints": [
            ("GET", "/api/tags"),
            ("POST", "/api/tags"),
            ("PUT", "/api/tags/{id}"),
            ("DELETE", "/api/tags/{id}"),
        ],
        "category": "Tags",
    },
    "search": {
        "file": "search.py",
        "endpoints": [
            ("GET", "/api/search"),
        ],
        "category": "Search",
    },

    # ==================== IMAGE & VIDEO ====================
    "image_gen": {
        "file": "image_gen.py",
        "endpoints": [
            ("GET", "/api/image-gen/models"),
            ("POST", "/api/image-gen/generate"),
            ("GET", "/api/image-gen/status"),
            ("GET", "/api/image-gen/history"),
        ],
        "category": "Image Generation",
    },
    "image_search": {
        "file": "image_search.py",
        "endpoints": [
            ("GET", "/api/image-search"),
            ("POST", "/api/image-search/upload"),
            ("GET", "/api/image-search/similar"),
        ],
        "category": "Image Search",
    },
    "video_gen": {
        "file": "video_gen.py",
        "endpoints": [
            ("GET", "/api/video-gen/status"),
            ("POST", "/api/video-gen/generate"),
            ("GET", "/api/video-gen/models"),
            ("GET", "/api/video-gen/progress"),
        ],
        "category": "Video Generation",
    },
    "video_edit": {
        "file": "video_edit.py",
        "endpoints": [
            ("GET", "/api/video-edit/info"),
            ("POST", "/api/video-edit/process"),
        ],
        "category": "Video Edit",
    },
    "video_enhance": {
        "file": "video_enhance.py",
        "endpoints": [
            ("GET", "/api/video-enhance/status"),
            ("POST", "/api/video-enhance/process"),
            ("GET", "/api/video-enhance/models"),
        ],
        "category": "Video Enhancement",
    },
    "upscaling": {
        "file": "upscaling.py",
        "endpoints": [
            ("GET", "/api/upscaling/models"),
            ("POST", "/api/upscaling/process"),
            ("GET", "/api/upscaling/status"),
        ],
        "category": "Upscaling",
    },
    "lip_sync": {
        "file": "lip_sync.py",
        "endpoints": [
            ("GET", "/api/lip-sync/status"),
            ("POST", "/api/lip-sync/generate"),
            ("GET", "/api/lip-sync/models"),
        ],
        "category": "Lip Sync",
    },
    "deepfake_creator": {
        "file": "deepfake_creator.py",
        "endpoints": [
            ("GET", "/api/deepfake/status"),
            ("POST", "/api/deepfake/create"),
            ("GET", "/api/deepfake/models"),
        ],
        "category": "Deepfake",
    },

    # ==================== SETTINGS & CONFIG ====================
    "settings": {
        "file": "settings.py",
        "endpoints": [
            ("GET", "/api/settings"),
            ("PUT", "/api/settings"),
            ("GET", "/api/settings/{category}"),
            ("PUT", "/api/settings/{category}"),
        ],
        "category": "Settings",
    },
    "advanced_settings": {
        "file": "advanced_settings.py",
        "endpoints": [
            ("GET", "/api/advanced-settings"),
            ("PUT", "/api/advanced-settings"),
        ],
        "category": "Advanced Settings",
    },
    "api_key_manager": {
        "file": "api_key_manager.py",
        "endpoints": [
            ("GET", "/api/api-keys"),
            ("POST", "/api/api-keys"),
            ("DELETE", "/api/api-keys/{id}"),
            ("GET", "/api/api-keys/validate"),
        ],
        "category": "API Keys",
    },
    "shortcuts": {
        "file": "shortcuts.py",
        "endpoints": [
            ("GET", "/api/shortcuts"),
            ("PUT", "/api/shortcuts"),
            ("POST", "/api/shortcuts/reset"),
        ],
        "category": "Shortcuts",
    },
    "presets": {
        "file": "presets.py",
        "endpoints": [
            ("GET", "/api/presets"),
            ("POST", "/api/presets"),
            ("GET", "/api/presets/{id}"),
            ("PUT", "/api/presets/{id}"),
            ("DELETE", "/api/presets/{id}"),
        ],
        "category": "Presets",
    },
    "templates": {
        "file": "templates.py",
        "endpoints": [
            ("GET", "/api/templates"),
            ("POST", "/api/templates"),
        ],
        "category": "Templates",
    },
    "backup": {
        "file": "backup.py",
        "endpoints": [
            ("GET", "/api/backup/list"),
            ("POST", "/api/backup/create"),
            ("POST", "/api/backup/restore"),
            ("DELETE", "/api/backup/{id}"),
        ],
        "category": "Backup",
    },

    # ==================== SYSTEM & HEALTH ====================
    "health": {
        "file": "health.py",
        "endpoints": [
            ("GET", "/api/health"),
            ("GET", "/api/health/detailed"),
            ("GET", "/api/health/engines"),
            ("GET", "/api/health/gpu"),
            ("GET", "/api/health/backend"),
        ],
        "category": "Health",
    },
    "diagnostics": {
        "file": "diagnostics.py",
        "endpoints": [
            ("GET", "/api/diagnostics"),
            ("GET", "/api/diagnostics/logs"),
            ("GET", "/api/diagnostics/system"),
            ("POST", "/api/diagnostics/test"),
        ],
        "category": "Diagnostics",
    },
    "gpu_status": {
        "file": "gpu_status.py",
        "endpoints": [
            ("GET", "/api/gpu/status"),
            ("GET", "/api/gpu/memory"),
        ],
        "category": "GPU",
    },
    "metrics": {
        "file": "metrics.py",
        "endpoints": [
            ("GET", "/api/metrics"),
            ("GET", "/api/metrics/prometheus"),
        ],
        "category": "Metrics",
    },
    "monitoring": {
        "file": "monitoring.py",
        "endpoints": [
            ("GET", "/api/monitoring/status"),
            ("GET", "/api/monitoring/alerts"),
            ("GET", "/api/monitoring/history"),
        ],
        "category": "Monitoring",
    },
    "telemetry": {
        "file": "telemetry.py",
        "endpoints": [
            ("GET", "/api/telemetry/status"),
            ("POST", "/api/telemetry/event"),
        ],
        "category": "Telemetry",
    },
    "slo": {
        "file": "slo.py",
        "endpoints": [
            ("GET", "/api/slo/dashboard"),
            ("GET", "/api/slo/targets"),
            ("GET", "/api/slo/history"),
        ],
        "category": "SLO",
    },
    "analytics": {
        "file": "analytics.py",
        "endpoints": [
            ("GET", "/api/analytics/dashboard"),
            ("GET", "/api/analytics/usage"),
            ("GET", "/api/analytics/performance"),
        ],
        "category": "Analytics",
    },
    "errors": {
        "file": "errors.py",
        "endpoints": [
            ("GET", "/api/errors"),
            ("GET", "/api/errors/recent"),
            ("GET", "/api/errors/{id}"),
        ],
        "category": "Errors",
    },
    "tracing": {
        "file": "tracing.py",
        "endpoints": [
            ("GET", "/api/tracing/status"),
            ("GET", "/api/tracing/spans"),
            ("POST", "/api/tracing/start"),
        ],
        "category": "Tracing",
    },
    "version": {
        "file": "version.py",
        "endpoints": [
            ("GET", "/api/version"),
            ("GET", "/api/version/check-update"),
        ],
        "category": "Version",
    },

    # ==================== EFFECTS & PROCESSING ====================
    "effects": {
        "file": "effects.py",
        "endpoints": [
            ("GET", "/api/effects"),
            ("GET", "/api/effects/{id}"),
            ("POST", "/api/effects/apply"),
            ("GET", "/api/effects/presets"),
        ],
        "category": "Effects",
    },
    "voice_effects": {
        "file": "voice_effects.py",
        "endpoints": [
            ("GET", "/api/voice-effects"),
            ("POST", "/api/voice-effects/apply"),
            ("GET", "/api/voice-effects/presets"),
        ],
        "category": "Voice Effects",
    },
    "emotion": {
        "file": "emotion.py",
        "endpoints": [
            ("GET", "/api/emotion/presets"),
            ("POST", "/api/emotion/analyze"),
            ("POST", "/api/emotion/apply"),
            ("GET", "/api/emotion/supported"),
        ],
        "category": "Emotion Control",
    },
    "emotion_style": {
        "file": "emotion_style.py",
        "endpoints": [
            ("GET", "/api/emotion-style/presets"),
            ("POST", "/api/emotion-style/apply"),
        ],
        "category": "Emotion Style",
    },
    "prosody": {
        "file": "prosody.py",
        "endpoints": [
            ("GET", "/api/prosody/presets"),
            ("POST", "/api/prosody/apply"),
            ("POST", "/api/prosody/analyze"),
        ],
        "category": "Prosody",
    },
    "style_transfer": {
        "file": "style_transfer.py",
        "endpoints": [
            ("GET", "/api/style-transfer/models"),
            ("POST", "/api/style-transfer/apply"),
            ("GET", "/api/style-transfer/presets"),
        ],
        "category": "Style Transfer",
    },
    "voice_morph": {
        "file": "voice_morph.py",
        "endpoints": [
            ("GET", "/api/voice-morph/models"),
            ("POST", "/api/voice-morph/process"),
            ("GET", "/api/voice-morph/presets"),
        ],
        "category": "Voice Morph",
    },
    "spatial_audio": {
        "file": "spatial_audio.py",
        "endpoints": [
            ("GET", "/api/spatial-audio/presets"),
            ("POST", "/api/spatial-audio/apply"),
            ("GET", "/api/spatial-audio/scenes"),
        ],
        "category": "Spatial Audio",
    },
    "rvc": {
        "file": "rvc.py",
        "endpoints": [
            ("GET", "/api/rvc/models"),
            ("POST", "/api/rvc/convert"),
            ("GET", "/api/rvc/status"),
        ],
        "category": "Voice Conversion",
    },
    "ai_enhancement": {
        "file": "ai_enhancement.py",
        "endpoints": [
            ("GET", "/api/ai-enhancement/models"),
            ("POST", "/api/ai-enhancement/process"),
            ("GET", "/api/ai-enhancement/status"),
        ],
        "category": "AI Enhancement",
    },

    # ==================== TRAINING ====================
    "training": {
        "file": "training.py",
        "endpoints": [
            ("GET", "/api/training/jobs"),
            ("POST", "/api/training/start"),
            ("GET", "/api/training/{id}/status"),
            ("POST", "/api/training/{id}/stop"),
            ("GET", "/api/training/config"),
        ],
        "category": "Training",
    },
    "training_audit": {
        "file": "training_audit.py",
        "endpoints": [
            ("GET", "/api/training-audit/status"),
            ("GET", "/api/training-audit/history"),
        ],
        "category": "Training Audit",
    },
    "dataset": {
        "file": "dataset.py",
        "endpoints": [
            ("GET", "/api/datasets"),
            ("POST", "/api/datasets"),
            ("GET", "/api/datasets/{id}"),
            ("DELETE", "/api/datasets/{id}"),
        ],
        "category": "Datasets",
    },
    "dataset_editor": {
        "file": "dataset_editor.py",
        "endpoints": [
            ("GET", "/api/dataset-editor/{id}"),
            ("PUT", "/api/dataset-editor/{id}"),
            ("POST", "/api/dataset-editor/validate"),
        ],
        "category": "Dataset Editor",
    },

    # ==================== REAL-TIME ====================
    "realtime_converter": {
        "file": "realtime_converter.py",
        "endpoints": [
            ("GET", "/api/realtime/status"),
            ("POST", "/api/realtime/start"),
            ("POST", "/api/realtime/stop"),
            ("GET", "/api/realtime/devices"),
            ("GET", "/api/realtime/models"),
        ],
        "category": "Real-Time",
    },
    "realtime_settings": {
        "file": "realtime_settings.py",
        "endpoints": [
            ("GET", "/api/realtime-settings"),
            ("PUT", "/api/realtime-settings"),
        ],
        "category": "Real-Time Settings",
    },
    "realtime_visualizer": {
        "file": "realtime_visualizer.py",
        "endpoints": [
            ("GET", "/api/realtime-visualizer/status"),
            ("GET", "/api/realtime-visualizer/data"),
        ],
        "category": "Real-Time Visualizer",
    },

    # ==================== TIMELINE & MIXER ====================
    "timeline": {
        "file": "timeline.py",
        "endpoints": [
            ("GET", "/api/timeline/tracks"),
            ("POST", "/api/timeline/tracks"),
            ("GET", "/api/timeline/state"),
            ("PUT", "/api/timeline/state"),
        ],
        "category": "Timeline",
    },
    "tracks": {
        "file": "tracks.py",
        "endpoints": [
            ("GET", "/api/tracks"),
            ("POST", "/api/tracks"),
            ("PUT", "/api/tracks/{id}"),
            ("DELETE", "/api/tracks/{id}"),
        ],
        "category": "Tracks",
    },
    "markers": {
        "file": "markers.py",
        "endpoints": [
            ("GET", "/api/markers"),
            ("POST", "/api/markers"),
            ("DELETE", "/api/markers/{id}"),
        ],
        "category": "Markers",
    },
    "scenes": {
        "file": "scenes.py",
        "endpoints": [
            ("GET", "/api/scenes"),
            ("POST", "/api/scenes"),
            ("PUT", "/api/scenes/{id}"),
        ],
        "category": "Scenes",
    },
    "mixer": {
        "file": "mixer.py",
        "endpoints": [
            ("GET", "/api/mixer/channels"),
            ("PUT", "/api/mixer/channels"),
            ("GET", "/api/mixer/state"),
            ("PUT", "/api/mixer/state"),
        ],
        "category": "Mixer",
    },
    "mix_scene": {
        "file": "mix_scene.py",
        "endpoints": [
            ("GET", "/api/mix-scene"),
        ],
        "category": "Mix Scene",
    },
    "mix_assistant": {
        "file": "mix_assistant.py",
        "endpoints": [
            ("GET", "/api/mix-assistant/suggestions"),
            ("POST", "/api/mix-assistant/apply"),
        ],
        "category": "Mix Assistant",
    },

    # ==================== AUDIO ANALYSIS ====================
    "audio_analysis": {
        "file": "audio_analysis.py",
        "endpoints": [
            ("POST", "/api/audio-analysis/analyze"),
            ("GET", "/api/audio-analysis/features"),
            ("POST", "/api/audio-analysis/compare"),
        ],
        "category": "Audio Analysis",
    },
    "audio_audit": {
        "file": "audio_audit.py",
        "endpoints": [
            ("GET", "/api/audio-audit/status"),
            ("POST", "/api/audio-audit/run"),
        ],
        "category": "Audio Audit",
    },
    "spectrogram": {
        "file": "spectrogram.py",
        "endpoints": [
            ("GET", "/api/spectrogram"),
            ("POST", "/api/spectrogram/generate"),
            ("GET", "/api/spectrogram/settings"),
        ],
        "category": "Spectrogram",
    },
    "advanced_spectrogram": {
        "file": "advanced_spectrogram.py",
        "endpoints": [
            ("GET", "/api/advanced-spectrogram/settings"),
            ("POST", "/api/advanced-spectrogram/generate"),
        ],
        "category": "Advanced Spectrogram",
    },
    "waveform": {
        "file": "waveform.py",
        "endpoints": [
            ("GET", "/api/waveform"),
            ("POST", "/api/waveform/generate"),
        ],
        "category": "Waveform",
    },
    "sonography": {
        "file": "sonography.py",
        "endpoints": [
            ("GET", "/api/sonography/status"),
            ("POST", "/api/sonography/generate"),
        ],
        "category": "Sonography",
    },

    # ==================== LEXICON & TEXT ====================
    "lexicon": {
        "file": "lexicon.py",
        "endpoints": [
            ("GET", "/api/lexicon"),
            ("POST", "/api/lexicon"),
            ("PUT", "/api/lexicon/{id}"),
            ("DELETE", "/api/lexicon/{id}"),
            ("POST", "/api/lexicon/import"),
        ],
        "category": "Lexicon",
    },
    "ssml": {
        "file": "ssml.py",
        "endpoints": [
            ("POST", "/api/ssml/validate"),
            ("POST", "/api/ssml/parse"),
            ("GET", "/api/ssml/examples"),
        ],
        "category": "SSML",
    },
    "text_highlighting": {
        "file": "text_highlighting.py",
        "endpoints": [
            ("GET", "/api/text-highlighting/rules"),
            ("POST", "/api/text-highlighting/apply"),
        ],
        "category": "Text Highlighting",
    },
    "text_speech_editor": {
        "file": "text_speech_editor.py",
        "endpoints": [
            ("GET", "/api/text-speech-editor/state"),
            ("PUT", "/api/text-speech-editor/state"),
            ("POST", "/api/text-speech-editor/sync"),
        ],
        "category": "Text/Speech Editor",
    },
    "script_editor": {
        "file": "script_editor.py",
        "endpoints": [
            ("GET", "/api/script-editor/state"),
            ("PUT", "/api/script-editor/state"),
            ("POST", "/api/script-editor/export"),
        ],
        "category": "Script Editor",
    },
    "multilingual": {
        "file": "multilingual.py",
        "endpoints": [
            ("GET", "/api/multilingual/languages"),
            ("POST", "/api/multilingual/detect"),
            ("GET", "/api/multilingual/support"),
        ],
        "category": "Multilingual",
    },
    "translation": {
        "file": "translation.py",
        "endpoints": [
            ("GET", "/api/translation/languages"),
            ("POST", "/api/translation/translate"),
            ("GET", "/api/translation/status"),
        ],
        "category": "Translation",
    },

    # ==================== QUALITY ====================
    "quality": {
        "file": "quality.py",
        "endpoints": [
            ("GET", "/api/quality/metrics"),
            ("POST", "/api/quality/analyze"),
            ("GET", "/api/quality/benchmarks"),
            ("POST", "/api/quality/compare"),
        ],
        "category": "Quality",
    },
    "quality_pipelines": {
        "file": "quality_pipelines.py",
        "endpoints": [
            ("GET", "/api/quality-pipelines"),
            ("POST", "/api/quality-pipelines/run"),
        ],
        "category": "Quality Pipelines",
    },
    "eval_abx": {
        "file": "eval_abx.py",
        "endpoints": [
            ("GET", "/api/eval-abx/tests"),
            ("POST", "/api/eval-abx/run"),
            ("GET", "/api/eval-abx/results"),
        ],
        "category": "A/B Testing",
    },

    # ==================== PROJECTS ====================
    "projects": {
        "file": "projects.py",
        "endpoints": [
            ("GET", "/api/projects"),
            ("POST", "/api/projects"),
            ("GET", "/api/projects/{id}"),
            ("PUT", "/api/projects/{id}"),
            ("DELETE", "/api/projects/{id}"),
        ],
        "category": "Projects",
    },

    # ==================== CLONING & VOICES ====================
    "instant_cloning": {
        "file": "instant_cloning.py",
        "endpoints": [
            ("GET", "/api/instant-clone/status"),
            ("POST", "/api/instant-clone/clone"),
            ("GET", "/api/instant-clone/voices"),
        ],
        "category": "Quick Clone",
    },
    "voice_browser": {
        "file": "voice_browser.py",
        "endpoints": [
            ("GET", "/api/voice-browser/voices"),
            ("GET", "/api/voice-browser/categories"),
            ("GET", "/api/voice-browser/search"),
        ],
        "category": "Voice Browser",
    },
    "multi_voice_generator": {
        "file": "multi_voice_generator.py",
        "endpoints": [
            ("GET", "/api/multi-voice/speakers"),
            ("POST", "/api/multi-voice/generate"),
            ("GET", "/api/multi-voice/presets"),
        ],
        "category": "Multi-Voice",
    },
    "multi_speaker_dubbing": {
        "file": "multi_speaker_dubbing.py",
        "endpoints": [
            ("GET", "/api/multi-speaker-dubbing/status"),
            ("POST", "/api/multi-speaker-dubbing/process"),
        ],
        "category": "Multi-Speaker Dubbing",
    },
    "dubbing": {
        "file": "dubbing.py",
        "endpoints": [
            ("GET", "/api/dubbing/status"),
            ("POST", "/api/dubbing/process"),
        ],
        "category": "Dubbing",
    },
    "embedding_explorer": {
        "file": "embedding_explorer.py",
        "endpoints": [
            ("GET", "/api/embeddings/status"),
            ("GET", "/api/embeddings/list"),
            ("POST", "/api/embeddings/search"),
        ],
        "category": "Embeddings",
    },

    # ==================== MODELS ====================
    "models": {
        "file": "models.py",
        "endpoints": [
            ("GET", "/api/models"),
            ("GET", "/api/models/{id}"),
            ("POST", "/api/models/download"),
            ("DELETE", "/api/models/{id}"),
        ],
        "category": "Models",
    },
    "model_inspect": {
        "file": "model_inspect.py",
        "endpoints": [
            ("GET", "/api/model-inspect/{id}"),
        ],
        "category": "Model Inspect",
    },
    "ml_optimization": {
        "file": "ml_optimization.py",
        "endpoints": [
            ("GET", "/api/ml-optimization/status"),
            ("POST", "/api/ml-optimization/optimize"),
        ],
        "category": "ML Optimization",
    },

    # ==================== PLUGINS & MCP ====================
    "plugins": {
        "file": "plugins.py",
        "endpoints": [
            ("GET", "/api/plugins"),
            ("POST", "/api/plugins/install"),
            ("DELETE", "/api/plugins/{id}"),
            ("GET", "/api/plugins/{id}/config"),
        ],
        "category": "Plugins",
    },
    "plugin_gallery": {
        "file": "plugin_gallery.py",
        "endpoints": [
            ("GET", "/api/plugin-gallery"),
            ("GET", "/api/plugin-gallery/categories"),
            ("GET", "/api/plugin-gallery/search"),
        ],
        "category": "Plugin Gallery",
    },
    "mcp_dashboard": {
        "file": "mcp_dashboard.py",
        "endpoints": [
            ("GET", "/api/mcp/status"),
            ("GET", "/api/mcp/servers"),
            ("POST", "/api/mcp/connect"),
        ],
        "category": "MCP",
    },

    # ==================== ASSISTANT & HELP ====================
    "assistant": {
        "file": "assistant.py",
        "endpoints": [
            ("GET", "/api/assistant/status"),
            ("POST", "/api/assistant/query"),
            ("GET", "/api/assistant/history"),
        ],
        "category": "Assistant",
    },
    "assistant_run": {
        "file": "assistant_run.py",
        "endpoints": [
            ("POST", "/api/assistant-run"),
            ("GET", "/api/assistant-run/{id}"),
        ],
        "category": "Assistant Run",
    },
    "ai_production_assistant": {
        "file": "ai_production_assistant.py",
        "endpoints": [
            ("GET", "/api/ai-production/status"),
            ("POST", "/api/ai-production/suggest"),
        ],
        "category": "AI Production",
    },
    "help": {
        "file": "help.py",
        "endpoints": [
            ("GET", "/api/help/topics"),
            ("GET", "/api/help/search"),
            ("GET", "/api/help/{topic}"),
        ],
        "category": "Help",
    },
    "todo_panel": {
        "file": "todo_panel.py",
        "endpoints": [
            ("GET", "/api/todo"),
            ("POST", "/api/todo"),
            ("PUT", "/api/todo/{id}"),
            ("DELETE", "/api/todo/{id}"),
        ],
        "category": "Todo",
    },
    "feedback": {
        "file": "feedback.py",
        "endpoints": [
            ("POST", "/api/feedback"),
            ("GET", "/api/feedback/status"),
        ],
        "category": "Feedback",
    },

    # ==================== INTEGRATIONS ====================
    "integrations": {
        "file": "integrations.py",
        "endpoints": [
            ("GET", "/api/integrations"),
            ("GET", "/api/integrations/{id}"),
            ("POST", "/api/integrations/connect"),
            ("DELETE", "/api/integrations/{id}"),
        ],
        "category": "Integrations",
    },
    "auth": {
        "file": "auth.py",
        "endpoints": [
            ("POST", "/api/auth/login"),
            ("POST", "/api/auth/logout"),
            ("GET", "/api/auth/status"),
        ],
        "category": "Auth",
    },

    # ==================== EXPERIMENTS ====================
    "experiments": {
        "file": "experiments.py",
        "endpoints": [
            ("GET", "/api/experiments"),
            ("POST", "/api/experiments"),
            ("GET", "/api/experiments/{id}"),
            ("PUT", "/api/experiments/{id}"),
        ],
        "category": "Experiments",
    },

    # ==================== SPECIALIZED ====================
    "ensemble": {
        "file": "ensemble.py",
        "endpoints": [
            ("GET", "/api/ensemble/status"),
            ("POST", "/api/ensemble/synthesize"),
            ("GET", "/api/ensemble/engines"),
        ],
        "category": "Ensemble",
    },
    "formant": {
        "file": "formant.py",
        "endpoints": [
            ("POST", "/api/formant/analyze"),
        ],
        "category": "Formant",
    },
    "granular": {
        "file": "granular.py",
        "endpoints": [
            ("POST", "/api/granular/process"),
        ],
        "category": "Granular",
    },
    "spectral": {
        "file": "spectral.py",
        "endpoints": [
            ("POST", "/api/spectral/process"),
        ],
        "category": "Spectral",
    },
    "articulation": {
        "file": "articulation.py",
        "endpoints": [
            ("POST", "/api/articulation/analyze"),
        ],
        "category": "Articulation",
    },
    "nr": {
        "file": "nr.py",
        "endpoints": [
            ("POST", "/api/noise-reduction/process"),
            ("GET", "/api/noise-reduction/models"),
        ],
        "category": "Noise Reduction",
    },
    "repair": {
        "file": "repair.py",
        "endpoints": [
            ("POST", "/api/repair/process"),
        ],
        "category": "Audio Repair",
    },
    "voice_speech": {
        "file": "voice_speech.py",
        "endpoints": [
            ("GET", "/api/voice-speech/status"),
            ("POST", "/api/voice-speech/process"),
        ],
        "category": "Voice/Speech",
    },
    "ultimate_dashboard": {
        "file": "ultimate_dashboard.py",
        "endpoints": [
            ("GET", "/api/ultimate-dashboard"),
            ("GET", "/api/ultimate-dashboard/widgets"),
        ],
        "category": "Dashboard",
    },
    "safety": {
        "file": "safety.py",
        "endpoints": [
            ("POST", "/api/safety/check"),
        ],
        "category": "Safety",
    },
    "reward": {
        "file": "reward.py",
        "endpoints": [
            ("GET", "/api/reward/status"),
            ("POST", "/api/reward/calculate"),
        ],
        "category": "Reward",
    },
    "adr": {
        "file": "adr.py",
        "endpoints": [
            ("GET", "/api/adr"),
        ],
        "category": "ADR",
    },
    "docs": {
        "file": "docs.py",
        "endpoints": [
            ("GET", "/api/docs"),
            ("GET", "/api/docs/openapi"),
        ],
        "category": "Docs",
    },
    "pdf": {
        "file": "pdf.py",
        "endpoints": [
            ("POST", "/api/pdf/generate"),
            ("GET", "/api/pdf/{id}"),
        ],
        "category": "PDF",
    },
    "img_sampler": {
        "file": "img_sampler.py",
        "endpoints": [
            ("POST", "/api/img-sampler/sample"),
        ],
        "category": "Image Sampler",
    },
}


class BackendVerifier:
    """Verifies backend feature availability."""

    def __init__(self, base_url: str = "http://127.0.0.1:8000", timeout: float = 10.0):
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout
        self.results: dict[str, RouteModule] = {}
        self.start_time: datetime | None = None
        self.end_time: datetime | None = None

    def check_backend_health(self) -> bool:
        """Check if backend is reachable."""
        try:
            response = requests.get(f"{self.base_url}/api/health", timeout=5)
            return response.status_code < 500
        except Exception:
            return False

    def test_endpoint(self, method: str, endpoint: str) -> EndpointResult:
        """Test a single endpoint."""
        url = f"{self.base_url}{endpoint}"
        start = time.perf_counter()

        try:
            if method == "GET":
                response = requests.get(url, timeout=self.timeout)
            elif method == "POST":
                response = requests.post(url, json={}, timeout=self.timeout)
            else:
                response = requests.request(method, url, timeout=self.timeout)

            elapsed = (time.perf_counter() - start) * 1000

            # Get response preview
            preview = None
            try:
                data = response.json()
                preview = json.dumps(data, indent=2)[:500]
            except Exception:
                preview = response.text[:500] if response.text else None

            return EndpointResult(
                endpoint=endpoint,
                method=method,
                status=response.status_code,
                response_time_ms=elapsed,
                success=response.status_code < 400,
                response_preview=preview,
            )

        except requests.Timeout:
            elapsed = (time.perf_counter() - start) * 1000
            return EndpointResult(
                endpoint=endpoint,
                method=method,
                status=None,
                response_time_ms=elapsed,
                success=False,
                error="Timeout",
            )
        except requests.RequestException as e:
            elapsed = (time.perf_counter() - start) * 1000
            return EndpointResult(
                endpoint=endpoint,
                method=method,
                status=None,
                response_time_ms=elapsed,
                success=False,
                error=str(e),
            )

    def verify_route_module(self, name: str, config: dict) -> RouteModule:
        """Verify a single route module."""
        module = RouteModule(
            name=name,
            file_path=config["file"],
        )

        endpoints = config.get("endpoints", [])
        all_success = True

        for method, endpoint in endpoints:
            result = self.test_endpoint(method, endpoint)
            module.endpoints_tested.append(result)
            if not result.success:
                all_success = False

        module.healthy = all_success and len(module.endpoints_tested) > 0
        module.notes = config.get("category", "")

        return module

    def verify_all(self) -> dict[str, RouteModule]:
        """Verify all route modules."""
        self.start_time = datetime.now()
        self.results = {}

        print(f"Backend URL: {self.base_url}")
        print(f"Testing {len(ROUTE_MODULES)} route modules...\n")

        for name, config in ROUTE_MODULES.items():
            print(f"  Testing {name}...", end=" ", flush=True)
            module = self.verify_route_module(name, config)
            self.results[name] = module

            if module.healthy:
                print(f"OK ({len(module.endpoints_tested)} endpoints)")
            else:
                failed = [e for e in module.endpoints_tested if not e.success]
                print(f"FAILED ({len(failed)}/{len(module.endpoints_tested)} failed)")

        self.end_time = datetime.now()
        return self.results

    def get_summary(self) -> dict[str, Any]:
        """Get verification summary."""
        total_modules = len(self.results)
        healthy_modules = sum(1 for m in self.results.values() if m.healthy)

        total_endpoints = sum(len(m.endpoints_tested) for m in self.results.values())
        successful_endpoints = sum(
            sum(1 for e in m.endpoints_tested if e.success)
            for m in self.results.values()
        )

        # Group by category
        by_category: dict[str, list[str]] = {}
        for name, module in self.results.items():
            category = module.notes or "Other"
            if category not in by_category:
                by_category[category] = []
            status = "OK" if module.healthy else "FAIL"
            by_category[category].append(f"{name}: {status}")

        return {
            "backend_url": self.base_url,
            "verification_time": self.end_time.isoformat() if self.end_time else None,
            "duration_seconds": (self.end_time - self.start_time).total_seconds() if self.end_time and self.start_time else 0,
            "summary": {
                "total_modules": total_modules,
                "healthy_modules": healthy_modules,
                "module_health_rate": healthy_modules / total_modules if total_modules > 0 else 0,
                "total_endpoints": total_endpoints,
                "successful_endpoints": successful_endpoints,
                "endpoint_success_rate": successful_endpoints / total_endpoints if total_endpoints > 0 else 0,
            },
            "by_category": by_category,
            "modules": {
                name: {
                    "file": module.file_path,
                    "category": module.notes,
                    "healthy": module.healthy,
                    "endpoints": [
                        {
                            "method": e.method,
                            "endpoint": e.endpoint,
                            "status": e.status,
                            "success": e.success,
                            "response_time_ms": e.response_time_ms,
                            "error": e.error,
                        }
                        for e in module.endpoints_tested
                    ],
                }
                for name, module in self.results.items()
            },
        }

    def export_report(self, output_path: Path) -> Path:
        """Export verification report to JSON."""
        output_path = Path(output_path)
        output_path.parent.mkdir(parents=True, exist_ok=True)

        report = self.get_summary()

        with open(output_path, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2)

        return output_path

    def print_summary(self):
        """Print summary to console."""
        summary = self.get_summary()

        print("\n" + "=" * 60)
        print("BACKEND FEATURE VERIFICATION SUMMARY")
        print("=" * 60)

        s = summary["summary"]
        print(f"\nModules: {s['healthy_modules']}/{s['total_modules']} healthy ({s['module_health_rate']*100:.1f}%)")
        print(f"Endpoints: {s['successful_endpoints']}/{s['total_endpoints']} successful ({s['endpoint_success_rate']*100:.1f}%)")

        print("\nBy Category:")
        for category, modules in summary["by_category"].items():
            print(f"  {category}:")
            for module_status in modules:
                print(f"    - {module_status}")

        # List failures
        failures = [
            (name, module)
            for name, module in self.results.items()
            if not module.healthy
        ]

        if failures:
            print("\nFailed Modules:")
            for name, module in failures:
                print(f"  {name}:")
                for endpoint in module.endpoints_tested:
                    if not endpoint.success:
                        print(f"    - {endpoint.method} {endpoint.endpoint}: {endpoint.status or endpoint.error}")


def main():
    parser = argparse.ArgumentParser(description="Verify VoiceStudio backend features")
    parser.add_argument("--base-url", default="http://127.0.0.1:8000", help="Backend API base URL")
    parser.add_argument("--output", default=".buildlogs/validation/api_coverage/backend_verification.json",
                        help="Output report path")
    parser.add_argument("--timeout", type=float, default=10.0, help="Request timeout in seconds")
    args = parser.parse_args()

    verifier = BackendVerifier(base_url=args.base_url, timeout=args.timeout)

    # Check backend health first
    print("Checking backend health...")
    if not verifier.check_backend_health():
        print(f"ERROR: Backend not reachable at {args.base_url}")
        print("Please start the backend with: py -3.12 -m uvicorn backend.api.main:app --port 8000")
        sys.exit(1)

    print("Backend is healthy. Starting verification...\n")

    # Run verification
    verifier.verify_all()

    # Print summary
    verifier.print_summary()

    # Export report
    report_path = verifier.export_report(Path(args.output))
    print(f"\nReport saved to: {report_path}")

    # Exit with error if any modules failed
    summary = verifier.get_summary()
    if summary["summary"]["healthy_modules"] < summary["summary"]["total_modules"]:
        sys.exit(1)

    sys.exit(0)


if __name__ == "__main__":
    main()
