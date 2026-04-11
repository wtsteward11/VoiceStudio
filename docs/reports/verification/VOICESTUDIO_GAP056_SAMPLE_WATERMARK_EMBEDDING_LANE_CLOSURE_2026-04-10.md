# Lane Closure Report: GAP-056 Slice 03 — Sample-Level Watermark Embedding with Detection Parity

**Lane ID:** GOV-VOICESTUDIO-GAP056-SAMPLE-WATERMARK-EMBEDDING-03
**Date closed:** 2026-04-10
**Owner:** Engine Engineer (Role 5)
**Status:** CLOSED

---

## What Was Delivered

Sample-level audio watermark embedding for STS (speech-to-speech) transformed outputs with detection parity, honest status surfacing, and failure degradation.

### Capabilities Added

1. **Watermark embedding in STS output path:** `_try_embed_watermark()` in `speech_to_speech_service.py` applies LSB invisible watermark via `WatermarkingService.embed_watermark()` to the WAV output after RVC conversion, before artifact registration
2. **Detection parity:** `_verify_watermark_on_artifact()` in `audio.py` runs `WatermarkingService.detect_watermark()` at marking endpoint query time — watermark status is verified, not assumed
3. **Honest status fields:** `StsMarkingStatus` and `SpeechToSpeechResponse` models extended with `watermark_applied`, `watermark_verified`, `watermark_method`
4. **Failure degradation:** If embedding fails, `watermark_applied=false` is surfaced — STS conversion proceeds without watermark; no false claims
5. **Provenance sidecar:** `write_provenance_sidecar` extended with `watermark_applied` and `watermark_method` fields
6. **Registry metadata:** `watermark_applied` and `watermark_method` persisted in artifact registry via `store_from_file`
7. **C# client:** `StsMarkingModels.cs` extended with `WatermarkApplied`, `WatermarkVerified`, `WatermarkMethod`; ViewModel reflects all three states
8. **Orphan cleanup:** Removed dead `backend/security/security_service.py` (orphaned duplicate, zero imports)

### What Is NOT Claimed

- No robust forensic watermarking (this is LSB amplitude perturbation — fragile to lossy compression)
- No non-STS output watermarking
- No persistent watermark key management (per-service-instance `os.urandom(32)`)
- No "tamper-proof" or "secure watermark" marketing language

---

## Files Changed

| File | Change |
|------|--------|
| `backend/services/speech_to_speech_service.py` | Added `_try_embed_watermark()`, wired into `_run_convert()`, extended response tuple |
| `backend/services/security_service.py` | Extended `write_provenance_sidecar` with `watermark_applied`, `watermark_method` |
| `backend/services/artifact_provenance.py` | Forward watermark fields from `transformation_meta` to sidecar |
| `backend/services/audio_artifacts/store.py` | Accept and persist `watermark_applied`, `watermark_method` in registry metadata and provenance |
| `backend/services/audio_artifacts/use_cases.py` | Pass `watermark_applied`, `watermark_method` through to `store_from_file` |
| `backend/api/models_additional.py` | Extended `SpeechToSpeechResponse` and `StsMarkingStatus` with watermark fields |
| `backend/api/routes/audio.py` | Added `_verify_watermark_on_artifact()`, extended marking endpoint with watermark detection |
| `src/VoiceStudio.App/Core/Models/StsMarkingModels.cs` | Added `WatermarkApplied`, `WatermarkVerified`, `WatermarkMethod` properties |
| `src/VoiceStudio.App/Views/Panels/SpeechToSpeechViewModel.cs` | Added `OutputWatermarkApplied`, `OutputWatermarkVerified`, `OutputWatermarkMethod` observable properties |

### Files Removed

| File | Reason |
|------|--------|
| `backend/security/security_service.py` | Orphaned duplicate — no imports found |

### Files Created

| File | Purpose |
|------|---------|
| `tests/unit/backend/services/test_sts_sample_watermark.py` | 13 targeted Python tests |
| `src/VoiceStudio.App.Tests/Views/Gap056Slice03Tests.cs` | 9 targeted C# seam tests |
| `docs/design/GOV_VOICESTUDIO_GAP056_SAMPLE_WATERMARK_EMBEDDING_03_EXECUTION_ROW.md` | Execution row |

---

## Proof Matrix

| Gate | Result | Detail |
|------|--------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** | Exit 0 |
| `test_sts_sample_watermark.py` | **13 PASS** | Embed/detect round-trip, failure degradation, provenance, registry, endpoint, anti-creep |
| `test_sts_durable_marking.py` | **6 PASS** | No regression from slice 2 |
| `test_speech_to_speech_service.py` | **10 PASS** | No regression |
| `Gap056Slice03Tests` | **9 PASS** | DTO fields, VM properties, reflection, defaults |
| `Gap057Tests` | **7 PASS** | No regression from prior naming-drift tests |
| `SpeechToSpeechMarkingSeamTests` | **4 PASS** | No regression |
| Full `VoiceStudio.App.Tests` | **3332 PASS / 274 skipped** | +9 from 3323 baseline |
| `check_ibackendclient_creep.py` | **PASS** | No creep |
| `check_empty_catches.py` | **PASS** | No new empty catches |
| `verify.ps1 -Quick` | **PASS** | `artifacts/verify/20260410_190828/` |

---

## Authority Model (Unchanged)

| Concern | Authority | File |
|---------|-----------|------|
| Watermark embed/detect | `WatermarkingService` | `backend/services/security_service.py` |
| STS conversion + watermark application | `SpeechToSpeechService.convert` | `backend/services/speech_to_speech_service.py` |
| Artifact storage + registry metadata | `AudioArtifactStore.store_from_file` | `backend/services/audio_artifacts/store.py` |
| Marking status API | `GET /api/audio/{id}/marking` | `backend/api/routes/audio.py` |

---

## Known Constraints (Documented, Not Defects)

1. LSB amplitude perturbation does not survive lossy format conversion (MP3, OGG, resampling)
2. Watermark key is per-service-instance (`os.urandom(32)`) — not persisted across restarts
3. Detection heuristic is self-described as "simplified" in the implementation
4. Naming drift from slice 2 (`Gap057Tests` class name) remains — governance hygiene debt, not functional

---

## Acceptance Criteria Disposition

- [x] STS converted audio has sample-level watermark embedded (WAV output)
- [x] `detect_watermark` confirms the watermark on the stored artifact
- [x] Metadata marking and sample marking do not silently disagree
- [x] `StsMarkingStatus` response includes `watermark_applied`, `watermark_verified`, `watermark_method`
- [x] When embedding fails, `watermark_applied = false` is surfaced honestly
- [x] Non-STS artifacts remain unwatermarked (no scope creep)
- [x] Orphaned `backend/security/security_service.py` removed
- [x] Provenance sidecar includes watermark fields when applicable
- [x] C# `StsMarkingModels` updated; ViewModel distinguishes metadata-only vs sample-watermarked
- [x] Test class named `Gap056Slice03Tests` (correct naming)
