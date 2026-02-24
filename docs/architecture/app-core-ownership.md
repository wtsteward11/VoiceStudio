# VoiceStudio app/core Sub-Package Ownership and Phasing

**Last Updated**: 2026-02-24
**Phase**: Phase 3A -- API Layer Foundation (integration/reintroduce-v1.0.2)

## Overview

The `app/core/` package is the shared infrastructure layer used by both the API layer
and engine layers. It is structured to allow partial application -- safe sub-packages can
be applied without engine dependencies.

## Applied in Phase 3A (Zero Engine Dependency)

| Sub-package | Purpose |
|---|---|
| `app/core/config/` | YAML-based config loader |
| `app/core/audit/` | Structured audit logging, debug notifications |
| `app/core/resilience/` | CircuitBreaker, RetryStrategy, HealthCheck, GracefulDegradation |
| `app/core/monitoring/` | ErrorTracker, StructuredLogger, Metrics, Profiler |
| `app/core/infrastructure/` | ContentHashCache, RealtimeRouter, SmartDiscovery |
| `app/core/utils/` | TempFileManager, TextProcessor, ProgressReporter |
| `app/core/tasks/` | TaskScheduler (background jobs, no engine deps) |
| `app/core/models/` | ModelCache, ModelStorage |
| `app/core/security/` | SecurityAudit, Watermarking, Database (deepfake_detector deferred) |
| `app/core/plugins_api/` | Plugin base classes |
| `app/core/nlp/` | TextProcessing (NLTK/spacy guarded) |
| `app/core/database/` | QueryOptimizer |

## Deferred -- Engine Dependency (Phase 6)

| Sub-package | Reason |
|---|---|
| `app/core/engines/` | Imports torch, TTS, transformers directly |
| `app/core/runtime/` | RuntimeEngine imports engine modules |
| `app/core/pipeline/` | Orchestrator imports engine router |
| `app/core/supervisor/` | Imports from app.core.engines |
| `app/core/training/` | ML training -- torch, XTTS |
| `app/core/tts/` | TTS utilities -- coqui-tts |
| `app/core/god_tier/` | NeuralAudioProcessor -- torch |
| `app/core/governance/` | AI governor -- imports engine router |

## Deferred -- Audio Processing (Phase 5)

| Sub-package | Reason |
|---|---|
| `app/core/audio/` | librosa, soundfile, numpy (heavy compiled deps) |

## Deferred -- ML Audit Required

| Sub-package | Reason |
|---|---|
| `app/core/security/deepfake_detector.py` | `import numpy as np` at line 15 |

## Rules

1. Any module in `app/core/` that imports from `app.core.engines.*` or
   `app.core.runtime.*` must NOT be applied before Phase 6.
2. All modules in the safe set above use only stdlib or already-verified deps.
3. Do NOT confuse `app/core/resilience/circuit_breaker.py` (safe, Phase 3A)
   with `backend/core/circuit_breaker.py` (applied in Phase 1). Both exist.
