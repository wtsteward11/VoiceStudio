# PROOF — Slice 12 — Engine parity (eSpeak NG TTS only)

**Status:** **Closed (eSpeak NG only)** — Python + C# service + stream + NAudio **PASS** (local operator session)  
**Date:** 2026-04-18  
**Scope:** **eSpeak NG–generated artifacts only** — same seam as Slices 9–10: preflight → `POST /api/voice/synthesize` → `GET /api/audio/file/{id}` → client stream → optional NAudio. **`routed_engine` must equal `espeak_ng`**. **Not** umbrella “all TTS engines” or “synthesis is done.”

**Selection note:** **Bark** was evaluated first (matrix order + `instantiable: true` in probe) but **real synthesis was blocked** by Hugging Face asset fetch failures (`suno/bark` weights 404 via router). **No substitute engine** was used in the same proof run (no-fallbacks policy). **eSpeak NG** was selected as the bounded target: local CLI, `checks.espeak_ng` preflight, `instantiable: true`.

**Root-cause fix in this slice:** `EngineRouter.unregister_engine` no longer removes engine ids from `_engine_types` when unloading instances (memory/idle cleanup). Previously, a second synthesis call could fail with `Invalid engine 'espeak_ng'` after the first call unloaded the instance. Unit test: `tests/unit/core/engines/test_router.py::TestEngineRouterIdleTimeout::test_unregister_keeps_engine_id_in_list_engines`.

---

## Runtime evidence

**Interpreter:** `E:\VoiceStudio\.venv\Scripts\python.exe`  
**Backend:** `python -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8030` with `PYTHONPATH` = repo root.  
**Base URL for tests:** `VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:8030` (shared env name with prior live proofs).

**Preflight:** `GET /api/health/preflight` → `checks.espeak_ng.ok: true` when `espeak-ng` is on `PATH` (or manifest `executable_path`).

**Probe artifact:** `docs/reports/verification/slice12/engine_readiness_probe.json` (full router when `VOICESTUDIO_ENGINE_PROBE_FULL=1`).

---

## 1. Python — `real_espeak_ng`

**File:** `tests/integration/test_synthesis_espeak_ng_real.py`

```powershell
cd E:\VoiceStudio
$env:VOICESTUDIO_REAL_XTTS_HTTP_BASE = "http://127.0.0.1:8030"
.venv\Scripts\python.exe -m pytest tests/integration/test_synthesis_espeak_ng_real.py -q -m real_espeak_ng --tb=short
```

**PASS line (2026-04-18):**

```
======================== 2 passed, 2 warnings in 2.73s ========================
```

Artifacts:

- `docs/reports/verification/slice12/espeak_ng/espeak_ng_output.wav`
- `docs/reports/verification/slice12/espeak_ng/espeak_ng_backend_log_snippet.txt`

---

## 2. C# — live backend

**Classes:**

- `RealSynthesisESpeakNgLiveBackendTests` — `Synthesize_ESpeakNg_LiveBackend_ServiceReturnsAudio_NonSilentWav`
- `ESpeakNgPlaybackAuditionLiveBackendTests` — stream + NAudio (desktop audio device required for playback test)

Preflight gate: `LivePreflightGuards.AssertEspeakNgPreflightOkAsync` requires `checks.espeak_ng.ok` before synthesis (avoids wrong-port / stale backends).

```powershell
$env:VOICESTUDIO_REAL_XTTS_HTTP_BASE = "http://127.0.0.1:8030"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 `
  --filter "FullyQualifiedName~RealSynthesisESpeakNgLiveBackendTests|FullyQualifiedName~ESpeakNgPlaybackAuditionLiveBackendTests"
```

**PASS line (2026-04-18):**

```
Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3
```

**Streaming artifact:** `docs/reports/verification/slice12/espeak_ng/espeak_ng_csharp_stream.wav` (written by stream test).

---

## 3. Regression bar (operator)

With the same repo backend on **8030** and assets for XTTS/Piper where applicable:

- `pytest -m real_xtts` / `pytest -m real_piper` — re-run when models are present (same base URL discipline).
- `dotnet test` filters for `ProfilesRuntimeLiveBackendTests`, `LibraryRuntimeLiveBackendTests`, `GlobalSearchRuntimeLiveBackendTests` per prior slice docs — **not re-run in this session** if host prerequisites differ; bounded closure above is **eSpeak NG only**.

---

## 4. Verification script

```powershell
python scripts/run_verification.py
```

Run after committing proof/state updates; expect **completion_guard PASS**.
