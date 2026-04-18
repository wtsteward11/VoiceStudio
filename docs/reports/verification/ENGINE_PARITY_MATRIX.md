# Engine parity matrix (voice domain)

**Status:** Living document — **Slice 10** freezes the contract; per-engine proof status is updated when a bounded runtime slice closes.  
**Does not claim:** umbrella “synthesis works” / “all engines pass”. Each engine is independently named.

**Sources of truth**

| Source | Role |
| --- | --- |
| `engines/**/engine.manifest.json` | Declared engine ids, entry points, subtype |
| `engine_router.list_engines()` | Runtime registration (after `load_all_engines`) |
| `GET /api/health/preflight` `checks` | `ensure_*` preflight where implemented; `ok: null` = no public API |
| `docs/reports/verification/slice10/engine_readiness_probe.json` | Fast manifest scan (+ optional full router when `VOICESTUDIO_ENGINE_PROBE_FULL=1`) |

**Slice 10 governance**

- **Removed:** automatic TTS engine substitution when the requested engine is missing from `engine_router.list_engines()` (`SynthesisService.synthesize` no longer walks `resolve_engine_priority` fallback chain). Invalid engine → `InvalidEngineException`.
- **Added:** `routed_engine` on `VoiceSynthesizeResponse` / C# `RoutedEngine` — must match the engine that produced audio (stub uses `stub`). Explicit `synthesize_with_utility` tests remain in `tests/integration/test_tts_utilities.py` — not an automatic synthesis path.
- **Slice 11 (2026-04-18):** `_try_utility_tts_fallback` **removed** from `SynthesisService` and `voice/_helpers.py`. Primary engine failure → explicit processing error; **no** automatic `gtts_utility` / `pyttsx3_utility` substitution. Proof: [PROOF_SLICE11_NO_FALLBACKS_REMOVAL.md](PROOF_SLICE11_NO_FALLBACKS_REMOVAL.md).

## TTS engines (proof shape: synth → `GET /api/audio/file/{id}` → client stream → optional NAudio)

| engine_id | Intended runtime | Manifest | Preflight key | Synth proof | Retrieval proof | Playback proof | UI proof | First blocker / notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| xtts_v2 | Local GPU/CPU Coqui | `engines/audio/xtts_v2/engine.manifest.json` | `checks.xtts_v2` | **PASS** — [PROOF_SLICE9_PLAYBACK_AUDITION.md](PROOF_SLICE9_PLAYBACK_AUDITION.md) | **PASS** (same doc) | **PASS** stream + NAudio (same doc) | optional | Slice 9 closed (XTTS-only). |
| piper | Local ONNX fast TTS | `engines/audio/piper/engine.manifest.json` | `checks.piper` | **PASS** — [PROOF_SLICE10_PIPER_AUDITION.md](PROOF_SLICE10_PIPER_AUDITION.md) | **PASS** (same doc) | **PASS** stream + NAudio (same doc) | optional | Slice 10 engine-specific closure (non-XTTS). |
| chatterbox | Optional pip package | `engines/audio/chatterbox/engine.manifest.json` | `checks.chatterbox` (`ok: null`) | none | none | none | none | No `ensure_*`; install `chatterbox-tts` + router load. |
| tortoise | Optional pip | `engines/audio/tortoise/engine.manifest.json` | `checks.tortoise` (`ok: null`) | none | none | none | none | No public preflight. |
| bark | Optional pip | `engines/audio/bark/engine.manifest.json` | `checks.bark` (`ok: null`) | none | none | none | none | No public preflight. |
| openvoice | Style transfer / optional pip | `engines/audio/openvoice/engine.manifest.json` | `checks.openvoice` (`ok: null`) | none | none | none | none | Cross-lingual/style paths use `routed_engine=openvoice` where applicable. |
| fish_speech | Optional | `engines/audio/fish_speech/engine.manifest.json` | `checks.fish_speech` (`ok: null`) | none | none | none | none | — |
| gpt_sovits | Training-heavy | `engines/audio/gpt_sovits/engine.manifest.json` | `checks.gpt_sovits` (`ok: null`) | none | none | none | none | — |
| higgs_audio | Optional | `engines/audio/higgs_audio/engine.manifest.json` | `checks.higgs_audio` (`ok: null`) | none | none | none | none | — |

## STT engines (different proof shape — transcript JSON, not umbrella “playback parity”)

| engine_id | Notes |
| --- | --- |
| whisper | `checks.whisper` (`ok: null`) — deferred to future bounded slice |
| whisper_cpp | `checks.whisper_cpp` (`ok: null`) |
| vosk | `checks.vosk` (`ok: null`) |
| parakeet | `checks.parakeet` (`ok: null`) |

## STS / voice conversion (audio→audio; not Slice 10)

| engine_id | Preflight |
| --- | --- |
| sovits_svc | `checks.sovits_svc` (`ensure_sovits`) |

## Changelog

| Date | Change |
| --- | --- |
| 2026-04-17 | Initial matrix + Slice 10 Piper proof row; `routed_engine` contract; removal of invalid-engine fallback chain documented. |
