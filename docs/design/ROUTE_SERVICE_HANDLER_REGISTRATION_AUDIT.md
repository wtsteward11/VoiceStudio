# Route-to-Service Handler Registration Audit

**Date:** 2026-03-22  
**Context:** After fixing `register_synthesize_handler` import error (synthesis route expected a service function that was removed during refactor), audit all similar patterns to prevent recurrence.

---

## Pattern

Route modules import `register_*_handler` from services and call it at module load time. The service stores the handler for later use by other callers (e.g. prosody route → lexicon_service.estimate_phonemes). If a service is refactored to remove the registration seam but routes are not updated, import-time failure occurs.

---

## Current Status: All Valid

| Route | Import | Service | Exports? |
|-------|--------|---------|----------|
| lexicon.py | `register_estimate_phonemes_handler` | lexicon_service | Yes |
| macros.py | `register_execute_macro_handler` | macro_execution_service | Yes |
| image_gen.py | `register_generate_image_handler` | image_gen_service | Yes |
| emotion.py | `register_emotion_analyze_handler` | emotion_service | Yes |
| multilingual.py | `register_translate_handler` | translation_service | Yes |

---

## Stale Case (Fixed 2026-03-22)

- **synthesis.py** → was importing `register_synthesize_handler` from `voice_synthesis_service`
- **voice_synthesis_service** was refactored to a thin wrapper; it no longer exposes handler registration (docstring: "No handler registration")
- **Fix:** Removed the dead import and call from `backend/api/routes/voice/synthesis.py`

---

## Recommendation

When refactoring a service that exports `register_*_handler`:

1. Search for all imports of that function: `grep -r "register_<name>_handler" --include="*.py"`
2. Either preserve the function (stub or delegating) or update/remove all route imports
3. Run verification (import tests, `verify.ps1 -Quick`) after changes

---

## Regression Test

See `tests/unit/backend/api/routes/voice/test_synthesis_import.py` — verifies synthesis module imports without `ImportError`/`AttributeError` from stale service hooks.
