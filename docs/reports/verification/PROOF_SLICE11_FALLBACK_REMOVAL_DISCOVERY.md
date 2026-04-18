# PROOF — Slice 11 Discovery: `_try_utility_tts_fallback` / utility TTS removal

**Date:** 2026-04-18  
**Status:** Discovery complete — **recommended scope: Option B**

---

## 1. Consumer map

| Location | Role |
| -------- | ---- |
| `backend/services/synthesis_service.py` | `_try_utility_tts_fallback` definition; call sites after `RuntimeError` max retries (~L644–656) and after synthesis loop when `result is None` (~removed block). |
| `backend/api/routes/voice/_helpers.py` | Duplicate `_try_utility_tts_fallback`; `_perform_synthesis_with_retry` invoked fallback when `result is None` (dead duplicate — no other callers of `_perform_synthesis_with_retry` in repo). |
| `backend/services/tts_utilities_service.py` | Re-exports `synthesize_with_utility` from `backend/tts/tts_utils.py` — **explicit** utility API (not automatic fallback). |
| `backend/tts/tts_utils.py` | Shim to `app.core.tts.tts_utilities.synthesize_with_utility`. |
| `app/core/tts/tts_utilities.py` | Implementation of `synthesize_with_utility` (gTTS / pyttsx3). |
| `tests/integration/test_tts_utilities.py` | Direct tests of `synthesize_with_utility` — **keep** (explicit utility tests). |
| `requirements.txt` | `gtts`, `pyttsx3` — still required for integration tests and explicit utility path. |
| `engines/*.json` | No manifest declares `gtts_utility` / `pyttsx3_utility` as primary engines. |
| C# / WinUI | No references to `gtts_utility` / `pyttsx3_utility` strings in frontend (grep). |
| Docs | `ENGINE_PARITY_MATRIX.md`, `HANDOFF_SLICE10_*` mention utility seam — update matrix; handoff historical. |

---

## 2. Risk analysis

| Risk | Mitigation |
| ---- | ---------- |
| Primary engine throws → user previously got *some* audio via utility | **Intended behavior change:** fail explicit (`EngineProcessingException` / structured 500) per `core/no-fallbacks.mdc`. |
| `_helpers.py` duplicate diverges from service | Remove fallback in **both** places (Option B). |
| `tests/integration/test_tts_utilities.py` breaks | No change to public `synthesize_with_utility` signature. |
| Dependency removal (Option C) breaks tests | **Not** recommended in this slice — keep deps for explicit utility tests. |

---

## 3. Recommended scope

- **Option B (selected):** Remove `_try_utility_tts_fallback` from `synthesis_service.py` and `_helpers.py`; remove all automatic invocation paths. **Retain** `app/core/tts/tts_utilities.py` + `backend/tts/tts_utils.py` + `requirements.txt` entries for explicit callers/tests.
- **Option A** (call sites only): insufficient — duplicate helper in `_helpers.py` would remain misleading.
- **Option C** (remove `gtts`/`pyttsx3` from requirements): deferred — requires ADR + test relocation/removal; out of bounded blast radius.

---

## 4. Required ADR

- **None** for Option B (no dependency removal).

---

## 5. Required tests

- `tests/unit/backend/services/test_synthesis_no_silent_fallback.py` — mocked engine `RuntimeError`; assert HTTP failure and response body does not contain `gtts_utility` / `pyttsx3_utility`.
- `EngineFailureTruthfulnessLiveBackendTests` (C#) — live backend: scan error payloads for forbidden `routed_engine` utility markers.

---

## 6. Reviewer approval

**Scope Option B approved** as part of bounded execution (2026-04-18) — automatic utility substitution removed; explicit utility stack retained.
