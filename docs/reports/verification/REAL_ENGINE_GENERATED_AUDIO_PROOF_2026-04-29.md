# Real Engine Generated Audio Proof — 2026-04-29

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: REAL_ENGINE
proof_type: voice_synthesis
engine_mode_source: runtime_probe
runtime_claim: false
operator_claim: false
-->

**Classification: REAL_ENGINE**
**Date:** 2026-04-29
**HEAD:** f44d7c398d47aa848e48640c15eeb4dd1930b0f2
**Purpose:** Classify the current engine mode and run a deterministic synthesis → library → timeline proof to prevent mock/stub results from being mistaken for real synthesis proof.

---

## 1. Engine Mode Classification

**VERDICT: REAL_ENGINE**

| Evidence Item | Value |
|---|---|
| `VOICESTUDIO_TEST_MODE` | _(empty — not set)_ |
| Stub gate result | Not triggered |
| `routed_engine` in synthesis response | `xtts_v2` |
| Engine class | `XTTSEngine` (instantiable) |
| Model assets present | YES — all 5 XTTS v2 files present |
| `coqui-tts` version | 0.25.3 |
| `torch` / `torchaudio` | 2.8.0 / 2.8.0 |
| `ENGINE_FALLBACK_CHAIN` usage | NOT referenced in `synthesis_service.py` — no fallback path active |

**Stub detection function** (`synthesis_service.py` lines 38–41):
```python
def _is_voice_studio_stub_test_mode() -> bool:
    v = os.environ.get("VOICESTUDIO_TEST_MODE", "").strip().lower()
    return v in ("1", "true", "yes", "stub")
```
`VOICESTUDIO_TEST_MODE` = empty → returned `False` → real engine path taken.

---

## 2. Health / Readiness

| Endpoint | Result |
|---|---|
| `GET /api/health/` | `status: degraded` (GPU unavailable — non-critical for synthesis) |
| `GET /api/health/readiness` | `ready: true` |
| Engines reported | 64 available (manifest-derived, safe mode) |
| Initialized engines at startup | 0 (lazy-load; engine initializes on first synthesis call) |

---

## 3. Engine Probe Result

- **Mode:** `manifest_scan_plus_full_router`
- **Probe file:** `docs/reports/verification/slice12/engine_readiness_probe.json`
- **xtts_v2 probe:**
  - `registered: true`
  - `instantiable: true`
  - `instance_type: XTTSEngine`
  - `preflight_assets.ok: true`
  - `assets_present: true`
  - Model paths verified:
    - `E:\VoiceStudio\models\xtts\tts\tts_models--multilingual--multi-dataset--xtts_v2\config.json`
    - `E:\VoiceStudio\models\xtts\tts\tts_models--multilingual--multi-dataset--xtts_v2\model.pth`
    - `E:\VoiceStudio\models\xtts\tts\tts_models--multilingual--multi-dataset--xtts_v2\speakers_xtts.pth`
    - `E:\VoiceStudio\models\xtts\tts\tts_models--multilingual--multi-dataset--xtts_v2\vocab.json`
    - `E:\VoiceStudio\models\xtts\tts\tts_models--multilingual--multi-dataset--xtts_v2\hash.md5`
- **piper probe:**
  - `registered: true`, `instantiable: true`, `preflight_assets.ok: true`
  - Voice: `en_US-amy-medium.onnx` present at `E:\VoiceStudio\models\piper\`
- **Probe note:** XTTS engine showed `init=None` in probe (lazy-load); successfully initialized on first synthesis call.

---

## 4. Synthesis Request / Response

**Endpoint:** `POST /api/voice/synthesize`

**Request:**
```json
{
  "text": "VoiceStudio real engine generated audio proof.",
  "engine": "xtts_v2",
  "profile_id": "22ebe087-5589-4d35-ab5a-c57049407813"
}
```

**Response:**
```json
{
  "audio_id": "synth_22ebe087-5589-4d35-ab5a-c57049407813_358c5095",
  "audio_url": "/api/voice/audio/synth_22ebe087-5589-4d35-ab5a-c57049407813_358c5095",
  "duration": 3.894,
  "quality_score": 0.9581674194335937,
  "routed_engine": "xtts_v2",
  "quality_metrics": {
    "mos_score": 4.790837097167969,
    "similarity": 0.9594159799632457,
    "naturalness": 1.0,
    "snr_db": 82.56586449811738,
    "artifact_score": 0.0,
    "has_clicks": false,
    "has_distortion": false
  }
}
```

**Key indicators of real synthesis:**
- `routed_engine: "xtts_v2"` (not `"stub"`)
- MOS score: **4.79** (high quality; stub would be undefined/0)
- SNR: **82.57 dB** (real audio; stub produces silent WAV)
- Duration: **3.894 seconds** (calculated from real waveform)
- `artifact_score: 0.0`, `naturalness: 1.0`

---

## 5. Audio Artifact Validation

| Check | Result |
|---|---|
| Audio ID | `synth_22ebe087-5589-4d35-ab5a-c57049407813_358c5095` |
| Download endpoint | `GET /api/voice/audio/synth_22ebe087-5589-4d35-ab5a-c57049407813_358c5095` |
| File size | **186,956 bytes (182.6 KiB)** — well above 1 KiB minimum |
| RIFF header (bytes 0–3) | `52 49 46 46` = `"RIFF"` ✓ |
| WAVE marker (bytes 8–11) | `"WAVE"` ✓ |
| Not an error JSON body | ✓ (does not start with `{`) |

---

## 6. Library Evidence

**Endpoint:** `POST /api/library/assets/upload`

| Field | Value |
|---|---|
| Library asset ID | `7882e9f9-d835-4fb0-9535-bfe6ca33b244` |
| Name | `real_engine_proof` |
| Type | `audio` |
| Size | 186,956 bytes |
| `audio_id` (upload_id) | `bc6176b4-f6b7-4628-98a9-86ad341c1620` |
| Saved path | `C:\Users\Tyler\AppData\Local\VoiceStudio\audio_uploads\bc6176b4-f6b7-4628-98a9-86ad341c1620.wav` |
| `converted_to_wav` | false (already WAV) |
| HTTP status | 201 Created |

---

## 7. Timeline Evidence

**Session ID:** `proof-real-engine-2026-04-29`

| Step | Result |
|---|---|
| Timeline state before | `revision: 0` → after track creation `revision: 1` |
| Track created | `b4602112-ec9d-46e4-825d-1afb77f3ca5f` ("Real Engine Proof Track") |
| Clip added | `c0821565-d70d-42cb-b722-aac0a23d0b18` ("real-engine-xtts-v2-proof") |
| Clip start_time | 0.0 |
| Clip end_time | 3.894 |
| Source path | Library upload WAV path |
| Timeline revision after clip | **2** (advanced from 1 — confirmed persisted) |

---

## 8. `ENGINE_FALLBACK_CHAIN` Audit

`ENGINE_FALLBACK_CHAIN` is defined in `backend/services/engine_service.py` (lines 325–331) but is **not referenced anywhere in `synthesis_service.py`**. The main synthesis path does not invoke fallback chains. No no-fallbacks policy violation.

---

## 9. Verification

| Check | Result |
|---|---|
| `python scripts/run_verification.py` | **PASS** (all gates green) |
| `.\scripts\verify.ps1 -Quick` | **PASS** (VERIFICATION PASSED) |
| Report artifact | `artifacts/verify/20260429_143134/verification_report.md` |

---

## 10. Explicit Non-Claims

- **Mock/stub proof is NOT real-engine proof.** If `VOICESTUDIO_TEST_MODE` ∈ `{1, true, yes, stub}`, synthesis returns `routed_engine="stub"` with a silent WAV (quality metrics absent or zero). That constitutes STUB_ENGINE evidence only, not real synthesis.
- **This is NOT a runtime FULL PASS.** The WinUI 3 app workflow and audio playback were not exercised in this proof.
- **This is NOT an RHVoice proof.** RHVoice was not tested or referenced.
- **This is NOT a GAP-008 slice.** No MainWindow or ShellBridge code was touched.
- **This is NOT an ENGINE_PARITY_MATRIX update.** Engine parity was not assessed.
- **GPU was unavailable** (health: degraded). XTTS v2 ran on CPU. Quality metrics still reflect real synthesis (MOS 4.79 CPU-based inference).

---

## 11. Classification Summary

```
VOICESTUDIO_TEST_MODE  = (empty)   → NOT stub
routed_engine          = xtts_v2   → real engine class XTTSEngine
model assets           = present   → real model loaded
MOS score              = 4.79      → real synthesis quality
SNR                    = 82.6 dB   → real waveform content
artifact size          = 182.6 KiB → real audio data
ENGINE_FALLBACK_CHAIN  = not used  → no policy violation

VERDICT: REAL_ENGINE
```
