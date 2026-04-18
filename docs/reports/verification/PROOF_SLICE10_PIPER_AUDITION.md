# PROOF — Slice 10 Playback / Artifact Audition Truth (Piper)

**Status:** **Closed (Piper only)** — Python + C# stream/service + NAudio playback completion **PASS**  
**Date:** 2026-04-17 (NAudio PASS 2026-04-18 local, repo backend `http://127.0.0.1:8030`; see §3 note on port)  
**Scope:** **Piper-generated artifact only** — proves the same audition seam as Slice 9 for a **non-XTTS** TTS engine: synthesis → `GET /api/audio/file/{id}` → client stream → optional NAudio playback. **`routed_engine` must equal `piper`** on success. **Not** “all engines” or generic synthesis workflows.

---

## Runtime evidence (healthy backend)

**Backend:** `scripts/backend/start_backend.ps1 -Port <free> -CoquiTosAgreed` (`.venv` uvicorn).  
**Base URL for tests:** `VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:<port>` (shared env name with Slice 8/9 live proofs).

**Preflight (truthful Piper probe):** `GET /api/health/preflight` → `checks.piper`:

- `ok`: **true** when Piper ONNX voice assets are present under the model root (`ensure_piper(auto_download=False)` — **no auto-download** in proof session).
- If `ok` is not **true**, opt-in `real_piper` tests **skip** at fixture gate (honest “not ready” — install assets first).

**Ready:** `GET /api/health/ready` → **200** (when backend up).

---

## 1. Playback truth contract

| Step | Actor | Expected | Failure |
| ---- | ----- | -------- | ------- |
| 1. Synthesis completes | `VoiceSynthesisService` | `AudioId` non-empty; **`RoutedEngine == "piper"`** | 500 from backend; wrong `routed_engine` |
| 2. WorkflowState transitions | ViewModel | `Synthesizing` → `AudioReady` | Stays `Synthesizing` or goes to `Error` |
| 3. PlayAudioCommand enabled | ViewModel | `CanPlayAudio == true` | Button stays disabled |
| 4. Fetch audio stream | `BackendClient.GetAudioStreamAsync` | HTTP 200, WAV bytes > 1024, RIFF header | 404, empty body, non-WAV |
| 5. Write temp file | ViewModel | Temp file written, non-zero | IOException |
| 6. Play | `AudioPlayerService.PlayFileAsync` | `IsPlaying == true`, `PositionChanged` may fire | NAudio error, no audio device |
| 7. Completion | `AudioPlayerService` | `PlaybackCompleted` fires, `IsPlaying == false` | Hang, no event |
| 8. Cleanup | ViewModel | Temp file deleted | File locked (best-effort) |

**Slice 10 automation proves steps 1, 4–7** on the **service + HTTP + file playback** path for **Piper** when preflight reports `checks.piper.ok`. ViewModel steps 2–3 and 8 remain covered by product code and prior slices where applicable.

**Split tests (C#):**

- `PiperPlaybackAuditionLiveBackendTests.Synthesize_PrimaryFileRoute_LiveBackend_StreamPlayable` — RIFF/fmt/data, duration, PCM peak ≥ 1000, writes `docs/reports/verification/slice10/piper/piper_csharp_stream.wav` (no audio device required).
- `PiperPlaybackAuditionLiveBackendTests.Synthesize_ThenPlayback_LiveBackend_PlayableNonSilentWav` — same pipeline + `AudioPlayerService.PlayFileAsync` to completion (desktop session with wave-out).

---

## 2. Evidence — Python (`real_piper`)

**Test file:** `tests/integration/test_synthesis_piper_real.py`  
**Markers:** `@pytest.mark.real_piper`

**Command:**

```powershell
$env:VOICESTUDIO_REAL_XTTS_HTTP_BASE = "http://127.0.0.1:<port>"
python -m pytest tests/integration/test_synthesis_piper_real.py -q -m real_piper --tb=short
```

**Expected when `checks.piper.ok`:** **2 passed**, **0 skipped**, **0 failed**. Artifacts written:

- `docs/reports/verification/slice10/piper/piper_output.wav`
- `docs/reports/verification/slice10/piper/piper_backend_log_snippet.txt`

**Live PASS line (2026-04-17, backend `http://127.0.0.1:8020`, `checks.piper.ok=true`):**

```
============================= test session starts =============================
collected 2 items
tests\integration\test_synthesis_piper_real.py ..                        [100%]
======================== 2 passed, 2 warnings in 3.90s ========================
```

Both tests asserted `synth_data.get("routed_engine") == "piper"` (no silent substitution path triggered). Artifacts on disk after run:

- `docs/reports/verification/slice10/piper/piper_output.wav` — 187,948 bytes, RIFF, PCM peak > 200 (test gate).
- `docs/reports/verification/slice10/piper/piper_backend_log_snippet.txt` — 321 bytes, contains `audio_id`, `wav_bytes`, `duration_s`, `pcm_peak_abs`.

> **Note:** A root-cause fix landed in this slice in `app/core/engines/piper_engine.py::_get_piper_voice_v1` — the installed `piper` package no longer accepts `download_dir=` on `PiperVoice.load`; the loader now passes `config_path=<model>.onnx.json` when present. Without this fix, synthesis returns 500 “engine returned None” (preflight `ok` lies about runtime). Pre-fix failure preserved at `.voicestudio/backend_slice10.log.err` for evidence.

---

## 3. Evidence — C# live backend

**Test classes:**  
- `src/VoiceStudio.App.Tests/ViewModels/RealSynthesisPiperLiveBackendTests.cs`  
- `src/VoiceStudio.App.Tests/ViewModels/PiperPlaybackAuditionLiveBackendTests.cs`  

**Base URL:** `VOICESTUDIO_REAL_XTTS_HTTP_BASE` (see above)

**Stream + PCM proof (no playback device required):**

```powershell
$env:VOICESTUDIO_REAL_XTTS_HTTP_BASE = "http://127.0.0.1:<port>"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 `
  --filter "FullyQualifiedName~PiperPlaybackAuditionLiveBackendTests.Synthesize_PrimaryFileRoute_LiveBackend_StreamPlayable"
```

**NAudio playback completion:**

```powershell
$env:VOICESTUDIO_REAL_XTTS_HTTP_BASE = "http://127.0.0.1:<port>"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 `
  --filter "FullyQualifiedName~PiperPlaybackAuditionLiveBackendTests.Synthesize_ThenPlayback_LiveBackend_PlayableNonSilentWav"
```

**Synthesis service round-trip (non-stream):**

```powershell
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 `
  --filter "FullyQualifiedName~RealSynthesisPiperLiveBackendTests"
```

**Live PASS lines (2026-04-17 headless, backend `http://127.0.0.1:8020`; 2026-04-18 NAudio, repo backend `http://127.0.0.1:8030`):**

- `RealSynthesisPiperLiveBackendTests` — `Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 958 ms`
- `PiperPlaybackAuditionLiveBackendTests.Synthesize_PrimaryFileRoute_LiveBackend_StreamPlayable` — `Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 994 ms`. Wrote `docs/reports/verification/slice10/piper/piper_csharp_stream.wav` (212,524 bytes, RIFF/PCM, peak ≥ 1000).
- `PiperPlaybackAuditionLiveBackendTests.Synthesize_ThenPlayback_LiveBackend_PlayableNonSilentWav` — **PASS (2026-04-18)** — `VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:8030` (uvicorn from repo root; `checks.piper.ok=true`). **Exact stdout (vstest):**
  ```
  Test Run Successful.
  Total tests: 1
       Passed: 1
   Total time: 7.9910 Seconds
  ```
  **Note:** Another process on `:8020` returned `Invalid engine 'piper'` (engine list without Piper). For Piper proofs, use a backend started from this repo (e.g. `scripts/backend/start_backend.ps1 -Port 8030`) with `VOICESTUDIO_MODELS_PATH` pointing at Piper ONNX assets.

---

## 4. Regression gates (XTTS remains authoritative for prior slice)

| Gate | Command | Notes |
| ---- | ------- | ----- |
| Slice 9 XTTS | `pytest -m real_xtts tests/integration/test_synthesis_xtts_real.py` | Must stay **2 passed, 0 skipped** on XTTS-healthy host |
| XTTS C# | `dotnet test ... --filter "FullyQualifiedName~RealSynthesisXttsLiveBackendTests\|FullyQualifiedName~PlaybackAuditionLiveBackendTests"` | Prior slice seam |
| Verification script | `python scripts/run_verification.py` | **PASS** (`completion_guard` as configured) |
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** |

---

## 5. Artifact chain (logical) — Piper only

1. `POST /api/voice/synthesize` with `engine=piper` → `audio_id` in JSON; **`routed_engine` == `piper`**.  
2. `GET /api/audio/file/{audio_id}` → RIFF WAV bytes.  
3. C#: `GetAudioStreamAsync` → temp `.wav` → `AudioPlayerService.PlayFileAsync` → `PlaybackCompleted` / `IsPlaying == false`.

**Inspect-able artifacts:** `docs/reports/verification/slice10/piper/` — `piper_output.wav`, `piper_csharp_stream.wav`, `piper_backend_log_snippet.txt` (after green runs).

---

## 6. Closure phrasing (governance)

- **Correct:** “Playback path **proven using Piper-generated artifact** (primary `/api/audio/file/` retrieval + client stream + NAudio where applicable) on a host where `/api/health/preflight` reports **`checks.piper.ok`**, with **`routed_engine` echo** verified.”  
- **Incorrect:** “All engines’ playback proven,” “synthesis works (generic).”

---

## 7. Supporting artifacts (Slice 10)

- Parity matrix: [`ENGINE_PARITY_MATRIX.md`](ENGINE_PARITY_MATRIX.md)  
- Readiness probe JSON: [`slice10/engine_readiness_probe.json`](slice10/engine_readiness_probe.json)  
- Probe run log (if captured): [`slice10/probe_run.txt`](slice10/probe_run.txt)

---

## Changelog

| Date | Change |
| ---- | ------ |
| 2026-04-17 | Initial proof doc — Piper bounded Slice 10; mirrors Slice 9 structure; `routed_engine` contract. |
| 2026-04-17 | Live runs recorded (Python 2/2, C# stream + service round-trip 1/1 each). NAudio playback completion explicitly recorded as OPERATOR-PENDING. Root-cause fix to `_get_piper_voice_v1` documented. XTTS regression also re-confirmed (Py 2/2 with `VOICESTUDIO_ALLOW_XTTS_DOWNLOAD_IN_TEST=1`; C# 3/3). |
| 2026-04-18 | NAudio filter PASS (`Total tests: 1`, `Passed: 1`, `Total time: 7.9910 Seconds`); repo backend `http://127.0.0.1:8030`; note on `:8020` vs repo uvicorn. |
