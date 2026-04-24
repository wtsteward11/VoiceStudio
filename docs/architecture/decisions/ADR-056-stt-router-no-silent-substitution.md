# ADR-056: STT router — no silent cross-engine substitution

**Status:** Accepted  
**Date:** 2026-04-23  

## Context

`EngineRouter` exposed a multi-engine STT fallback list (`whisper_cpp` → `faster_whisper` → `vosk`) and load-based alternatives derived from that chain. Manifest STT id for faster-whisper is **`whisper`**, while YAML used **`faster_whisper`**, creating drift. This contradicted **no-fallbacks** for implicit substitution when a caller intends a specific STT engine.

## Decision

1. **STT fallback chain** resolves to **exactly one** engine id: `defaults.stt` from unified config (or built-in default `whisper_cpp`). YAML multi-entry STT lists are **not** used for router substitution.
2. **`select_engine_with_fallback(..., explicit_engine_id=...)`** attempts **only** that id (normalized), fail-closed on failure.
3. **`_get_lower_load_alternative`** returns **`None`** for `task_type == "stt"` (no silent STT swap).
4. **`get_engine`** normalizes legacy id **`faster_whisper` → `whisper`** (manifest id).
5. **`UnifiedConfigService.get_engine_for_language`** applies YAML language mapping **only** for `task_type == "tts"` so STT does not inherit TTS language rows.

## Consequences

- **Positive:** Orchestration cannot silently replace `whisper_cpp` with another STT engine; explicit API and router paths align with no-fallbacks doctrine.
- **Negative:** Hosts with only a non-default STT engine must set `defaults.stt` explicitly; no automatic hop.

## Related

- Slice 24 brief: `docs/design/VOICESTUDIO_BOUNDED_SLICE24_STT_ROUTER_FAIL_CLOSED.md`  
- Slice 23: `WhisperCPPEngine` integrity (engine-level honesty)  
- `config/engines.config.yaml` — STT chain comment documents single-effective-default policy  
