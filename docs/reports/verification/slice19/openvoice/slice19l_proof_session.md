# Slice 19L — proof session (speech reference + Policy A)

**Date (UTC):** 2026-04-22 (session wall clock local).  
**Bounded brief:** [VOICESTUDIO_BOUNDED_SLICE19L_OPENVOICE_REFERENCE_AUDIO_VAD_CONTRACT.md](../../../design/VOICESTUDIO_BOUNDED_SLICE19L_OPENVOICE_REFERENCE_AUDIO_VAD_CONTRACT.md)

## 1. Policy and harness

| Item | Value |
| --- | --- |
| **Policy** | **A** — OpenVoice live proofs use **speech-like** reference: `tests/fixtures/audio/openvoice_reference_speech.wav` (not `test_440hz_2s.wav`). |
| **Override** | `VOICESTUDIO_OPENVOICE_PROOF_REFERENCE_WAV` (optional) — not set this session. |
| **Provenance** | gTTS (MIT) + FFmpeg → mono **22050 Hz** PCM WAV; see bounded brief. |

## 2. One backend, one port, one base URL

| Field | Value |
| --- | --- |
| **Uvicorn** | `E:\VoiceStudio\.venv\Scripts\python.exe -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8043` |
| **PYTHONPATH** | `e:\VoiceStudio` |
| **VOICESTUDIO_MODELS_PATH** | `e:\VoiceStudio\models` |
| **TORCH_HOME** | `e:\VoiceStudio\models\torch` (session env; aligns with 19K Silero cache layout) |
| **VOICESTUDIO_REAL_XTTS_HTTP_BASE** | `http://127.0.0.1:8043` (pytest + `dotnet test` same value) |

## 3. Preflight gate

- **Artifact:** [`slice19l_preflight_openvoice.json`](slice19l_preflight_openvoice.json) (verbatim `GET /api/health/preflight`).
- **`checks.openvoice.ok`:** **true** (import + **venv_openvoice** + checkpoint layout under `e:\VoiceStudio\models\openvoice\...`).
- **Overall `ok`:** **false** (other engines e.g. **chatterbox** / **rhvoice** unchanged — not a 19L blocker for the OpenVoice contract row).

## 4. Python `real_openvoice`

```text
python -m pytest tests/integration/test_synthesis_openvoice_real.py -m real_openvoice -v --tb=short
```

**Result:** **2 failed, 0 passed** — `POST /api/voice/synthesize` **500** — `"Synthesis failed - engine returned None. Check engine logs for details."`

- **Example `request_id`:** `7f8e50de-dc99-4363-82c3-357d0dbc4b40` (first test); `d1c90f5f-8c20-4ee7-93c0-eb52f16e8326` (second test).
- **`preprocess-reference`:** **200** for both runs using **`openvoice_reference_speech.wav`** — reference **accepted** (differs from **19K** where the primary seam was **VAD stripping the 440 Hz tone** before a successful reference class).

## 5. C# OpenVoice `LiveBackend`

```text
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~OpenVoice&TestCategory=LiveBackend"
```

**Result:** **3 failed, 0 passed** — `BackendServerException: Synthesis failed - engine returned None` (same backend URL as §4).

## 6. Primary seam (19L vs 19K)

| Slice | Primary seam (frozen) |
| --- | --- |
| **19K** | `se_extractor.get_se(..., vad=True)` on **`test_440hz_2s.wav`** → **VAD duration 0.0** (non-speech tone). |
| **19L** | With **Policy A** speech reference + **200** preprocess, synthesis still **500** / *engine returned None* — **downstream of reference/VAD-on-tone** (worker subprocess exit, **OpenVoice** forward pass, **GPU**/**CPU** error, or `SynthesisService` handoff). **Not** the same one-line “440 Hz + VAD” explanation; capture **`openvoice` worker** stderr + structured logs for the next investigation. |

## 7. `return None` + file contract (Task 8)

**N/A** — HTTP **200** for synthesis **not** reached. No **Conclusion A** vs **B** for file-driven `None` + `SynthesisService` in this session (same class as 19K **Task 7** N/A, but for a **different** top-level reason: speech harness + preprocess green, **synthesis** still null).

## 8. Ruling

**Outcome B** — **contract + fixture + harness** landed; **2/2 + 3/3** **red**; **matrix `openvoice` still pending** until a green live ladder. See [PROOF §19L](../../PROOF_SLICE19_OPENVOICE_AUDITION.md).

## 9. Regression bar (governance)

Recorded in **STATE** **Last Verified Commands** after this session: `dotnet build` → `python scripts/run_verification.py` → `.\scripts\verify.ps1 -Quick`.
