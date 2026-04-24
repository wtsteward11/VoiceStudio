# PROOF — Slice 25 — whisper.cpp unit tests (skip-laundering purge)

**Status:** **PASS** — unit file rewritten so **assertion failures fail**; skips only for **narrow** preconditions (e.g. live backend missing), not broad `except Exception: pytest.skip`.

**Date:** 2026-04-23  

## Scope

| In scope | Out of scope |
| --- | --- |
| [`tests/unit/core/engines/test_whisper_cpp_engine.py`](../../tests/unit/core/engines/test_whisper_cpp_engine.py) — deterministic tests aligned to current **`WhisperCPPEngine`** API | `real_whisper_cpp` integration (Slice 27) |
| Retain **`TestWhisperCPPEngineSlice23NoFallback`** (Slice 23) | Router policy (Slice 24 — separate tests) |

## Key files

- [`tests/unit/core/engines/test_whisper_cpp_engine.py`](../../tests/unit/core/engines/test_whisper_cpp_engine.py)
- Implementation reference: [`app/core/engines/whisper_cpp_engine.py`](../../app/core/engines/whisper_cpp_engine.py)

## Verification

```powershell
python -m pytest tests/unit/core/engines/test_whisper_cpp_engine.py -q
```

**Expected:** exit code **0**; no tests that mask assertion errors inside `except Exception: skip`.

## Artifacts

- Bounded brief: [`docs/design/VOICESTUDIO_BOUNDED_SLICE25_WHISPER_CPP_UNIT_TESTS.md`](../../design/VOICESTUDIO_BOUNDED_SLICE25_WHISPER_CPP_UNIT_TESTS.md).

## Open seams

- Optional split into `*_unit.py` / `*_integration.py` if the file grows again — not required for closure.
