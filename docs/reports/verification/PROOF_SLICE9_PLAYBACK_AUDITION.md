# PROOF — Slice 9 Playback / Artifact Audition Truth (XTTS)

**Status:** **Closed — XTTS runtime proof (PASS, no skip)**  
**Date:** 2026-04-17  
**Scope:** **XTTS-generated artifact only** — proves the primary audition seam (synthesis → `GET /api/audio/file/{id}` → client stream → NAudio playback). **Not** “all engines” or generic synthesis workflows.

---

## Runtime evidence (healthy backend)

**Backend:** `scripts/backend/start_backend.ps1 -Port 8020 -CoquiTosAgreed` (`.venv` uvicorn).  
**Base URL for tests:** `VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:8020`

**Preflight (truthful XTTS probe):** `GET /api/health/preflight` → `checks.xtts_v2`:

- `ok`: **true**
- `assets_present`: **true**
- `message`: `XTTS assets ready at E:\VoiceStudio\models\xtts`
- `dependencies.ok`: **true** (coqui-tts / torch / torchaudio reported)
- Note: `downloaded` may be **false** when assets already exist on disk (`ensure_xtts(auto_download=False)` — no download in proof session).

**Ready:** `GET /api/health/ready` → **200**

---

## 1. Playback truth contract

| Step | Actor | Expected | Failure |
| ---- | ----- | -------- | ------- |
| 1. Synthesis completes | `VoiceSynthesisService` | `AudioId` non-empty, `AudioUrl` non-empty | 500 from backend; error state |
| 2. WorkflowState transitions | ViewModel | `Synthesizing` → `AudioReady` | Stays `Synthesizing` or goes to `Error` |
| 3. PlayAudioCommand enabled | ViewModel | `CanPlayAudio == true` | Button stays disabled |
| 4. Fetch audio stream | `BackendClient.GetAudioStreamAsync` | HTTP 200, WAV bytes > 1024, RIFF header | 404, empty body, non-WAV |
| 5. Write temp file | ViewModel | Temp file written, non-zero | IOException |
| 6. Play | `AudioPlayerService.PlayFileAsync` | `IsPlaying == true`, `PositionChanged` may fire | NAudio error, no audio device |
| 7. Completion | `AudioPlayerService` | `PlaybackCompleted` fires, `IsPlaying == false` | Hang, no event |
| 8. Cleanup | ViewModel | Temp file deleted | File locked (best-effort) |

**Slice 9 automation proves steps 1, 4–7** on the **service + HTTP + file playback** path (no ViewModel/dispatcher). ViewModel steps 2–3 and 8 remain covered by product code and Slice 8/operator posture where applicable.

**Split tests (C#):**

- `Synthesize_PrimaryFileRoute_LiveBackend_StreamPlayable` — RIFF/fmt/data, duration, PCM peak ≥ 1000, writes `slice9_csharp_stream.wav` (no audio device required).
- `Synthesize_ThenPlayback_LiveBackend_PlayableNonSilentWav` — same pipeline + `AudioPlayerService.PlayFileAsync` to completion (`AudioDeviceGuard` — desktop session with wave-out).

---

## 2. Evidence — Python (`real_xtts`)

**Test file:** `tests/integration/test_synthesis_xtts_real.py`  
**Markers:** `@pytest.mark.real_xtts`

**Command:**

```powershell
$env:VOICESTUDIO_REAL_XTTS_HTTP_BASE = "http://127.0.0.1:8020"
python -m pytest tests/integration/test_synthesis_xtts_real.py -q -m real_xtts --tb=short
```

**Recorded run (2026-04-17):** **2 passed**, **0 skipped**, **0 failed** (~81.6s). Artifacts written:

- `docs/reports/verification/slice9/slice9_output.wav` (918,092 bytes)
- `docs/reports/verification/slice9/slice9_backend_log_snippet.txt` (345 bytes)

---

## 3. Evidence — C# live backend

**Test class:** `src/VoiceStudio.App.Tests/ViewModels/PlaybackAuditionLiveBackendTests.cs`  
**Base URL:** `VOICESTUDIO_REAL_XTTS_HTTP_BASE` (see above)

**Stream + PCM proof (no playback device required):**

```powershell
$env:VOICESTUDIO_REAL_XTTS_HTTP_BASE = "http://127.0.0.1:8020"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 `
  --filter "FullyQualifiedName~PlaybackAuditionLiveBackendTests.Synthesize_PrimaryFileRoute_LiveBackend_StreamPlayable"
```

**Recorded run (2026-04-17):** **Passed: 1**, Failed: 0, Skipped: 0 (~51s). Artifact: `docs/reports/verification/slice9/slice9_csharp_stream.wav` (1,045,068 bytes).

**NAudio playback completion:**

```powershell
$env:VOICESTUDIO_REAL_XTTS_HTTP_BASE = "http://127.0.0.1:8020"
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 `
  --filter "FullyQualifiedName~PlaybackAuditionLiveBackendTests.Synthesize_ThenPlayback_LiveBackend_PlayableNonSilentWav"
```

**Recorded run (2026-04-17):** **Passed: 1**, Failed: 0, Skipped: 0 (~69s).

---

## 4. Regression gates

| Gate | Command | Result (2026-04-17) |
| ---- | ------- | ------------------- |
| Slice 8 XTTS | `dotnet test ... --filter "FullyQualifiedName~RealSynthesisXttsLiveBackendTests"` | **PASS** (1 passed, 0 skipped) |
| Verification script | `python scripts/run_verification.py` | **PASS** (overall; advisories only for stale unrelated proofs) |
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** |

---

## 5. Artifact chain (logical)

1. `POST /api/voice/synthesize` with `engine=xtts_v2` → `audio_id` in JSON.  
2. `GET /api/audio/file/{audio_id}` → RIFF WAV bytes (same route `BackendClient` uses first).  
3. C#: `GetAudioStreamAsync` → temp `.wav` → `AudioPlayerService.PlayFileAsync` → `PlaybackCompleted` / `IsPlaying == false`.

**Inspect-able artifacts:** `docs/reports/verification/slice9/` — `slice9_output.wav`, `slice9_csharp_stream.wav`, `slice9_backend_log_snippet.txt`.

---

## 6. Closure phrasing (governance)

- **Correct:** “Playback path **proven using XTTS-generated artifact** (primary `/api/audio/file/` retrieval + client stream + NAudio) on a host where `/api/health/preflight` reports `xtts_v2.ok`.”  
- **Incorrect:** “All engines’ playback proven,” “all synthesis workflows proven.”

---

## Changelog

| Date | Change |
| ---- | ------ |
| 2026-04-17 | Initial proof doc for Slice 9 bounded playback/audition truth. |
| 2026-04-17 | **Runtime closure:** healthy backend :8020; pytest `real_xtts` 2/2 PASS; C# stream + playback PASS; artifacts under `slice9/`; regression gates PASS. |
