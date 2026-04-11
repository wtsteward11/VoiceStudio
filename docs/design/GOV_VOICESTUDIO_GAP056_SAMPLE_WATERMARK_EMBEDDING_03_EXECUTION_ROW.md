# Execution Row: GAP-056 Slice 03 — Sample-Level Watermark Embedding with Detection Parity

**Lane ID:** GOV-VOICESTUDIO-GAP056-SAMPLE-WATERMARK-EMBEDDING-03
**Gap:** GAP-056 (Audio watermarking / provenance — policy-aligned)
**Slice:** 03 of umbrella
**Status:** CLOSED — 2026-04-10
**Date frozen:** 2026-04-10
**Owner Role:** Engine Engineer (Role 5)
**Validator:** Overseer (Role 0) + Skeptical Validator
**Predecessor slices:** Slice 01 (STS transformed-output disclosure, Closed 2026-04-10), Slice 02 (STS durable metadata marking, Closed 2026-04-10)

---

## Context

Slices 01–02 established the **metadata trust chain** for STS transformed audio:
- Consent gate (GAP-055)
- Transformed-output disclosure (slice 01)
- Durable metadata marking: provenance sidecar, registry `is_transformed` / `transformation_type`, `GET /api/audio/{id}/marking`, export headers, UI badge (slice 02)

What was explicitly deferred in slice 02: **actual sample-level watermark embedding in the audio signal, and detection parity to verify it.**

This slice delivers that deferred capability — bounded to the STS output path, with honest detection verification.

---

## Architectural Decision: What "Watermarked" Means

### Existing infrastructure

`WatermarkingService` in `backend/services/security_service.py` has an executable `embed_watermark` (LSB amplitude perturbation, 512-bit payload, deterministic positions via seeded PRNG) and `detect_watermark` / `verify_watermark` (local-window mean heuristic). **Neither is wired into any production path.** The service is dead capability.

### Honest capability assessment

The existing `_embed_invisible` technique is **fragile**:
- **Survives:** lossless WAV copy, same-sample-rate operations, mild amplitude scaling
- **Does not survive:** MP3/OGG lossy compression, resampling to different sample rates, aggressive DSP chains

This is not a defect — it is a constraint of LSB-style embedding. Professional forensic watermarking (e.g., Digimarc, AudioSeal) uses spread-spectrum or learned-model approaches for robustness. Those are out of scope for this lane.

### Decision

1. Wire `WatermarkingService.embed_watermark` into the STS artifact creation path
2. Wire `WatermarkingService.detect_watermark` as the verification seam
3. Surface watermark status **honestly**: `watermark_applied`, `watermark_verified`, `watermark_method`
4. When detection fails (e.g., lossy export destroyed the watermark): surface `watermark_verified = false` — do **not** claim watermarked when verification cannot confirm
5. Metadata marking (slice 02) and sample marking (this slice) must not silently disagree — if sample watermark is not verified, the marking status reflects that honestly

---

## Hard IN (Scope)

1. **Embed watermark in STS output:** After RVC conversion produces WAV, before `create_audio_artifact_from_file`, apply `WatermarkingService.embed_watermark` with a payload encoding `audio_id`, `transformation_type`, and timestamp
2. **Detection/verification seam:** `WatermarkingService.detect_watermark` or `verify_watermark` callable via a backend function; wired into the marking endpoint response as `watermark_verified`
3. **Honest status fields:** Extend `StsMarkingStatus` (and/or `SpeechToSpeechResponse`) with:
   - `watermark_applied: bool` — was embedding attempted?
   - `watermark_verified: bool | None` — did detection confirm the watermark? `None` = not checked
   - `watermark_method: str | None` — which method was used (e.g., `"invisible_lsb"`)
4. **Failure honesty:** If `embed_watermark` raises or fails, the STS conversion still succeeds — but `watermark_applied = false` is surfaced. No silent fallback that claims success.
5. **Export degradation honesty:** When export uses lossy format conversion that destroys the watermark, the export response and/or marking endpoint must not overclaim. The stored artifact retains the watermark; the exported derivative may not.
6. **C# client/UI awareness:** `StsMarkingStatus` DTO updated; `SpeechToSpeechView` marking badge distinguishes metadata-marked vs sample-watermarked
7. **Targeted tests:** Python tests for embed/detect round-trip, failure paths, non-STS-untouched; C# seam tests for VM state
8. **Orphan cleanup:** Remove `backend/security/security_service.py` (confirmed orphaned duplicate; no imports reference it)
9. **Naming hygiene:** Test class for this slice uses `Gap056Slice03Tests` (not mismatched `Gap057`-style naming)

## Hard OUT (Not in scope)

1. **Robust forensic watermarking** — no learned-model, spread-spectrum, or AudioSeal integration
2. **Non-STS output watermarking** — synthesis, recording, import paths are not touched
3. **Product-wide media trust platform** — no trust chains, verification APIs, or public verification endpoints
4. **RBAC, auth, audit trail** — those are separate gaps (GAP-057, GAP-059, GAP-061)
5. **Export-time re-watermarking** — we do not embed a second watermark during export; the stored artifact's watermark is what it is
6. **`AudioWatermarker` in `app/core/security/watermarking.py`** — that stub remains a stub; we use `WatermarkingService` from `backend/services/security_service.py`
7. **Marketing language** — no "secure watermark" or "tamper-proof" claims; what we have is LSB amplitude perturbation, honestly described
8. **Cloning-wide retrofit** — only STS converted outputs
9. **Watermark key management** — uses the default `os.urandom(32)` per-service-instance; persistent key storage is future work

---

## Authority Model

| Concern | Authority | File |
|---------|-----------|------|
| Watermark embed/detect | `WatermarkingService` | `backend/services/security_service.py` |
| STS conversion + watermark application | `SpeechToSpeechService.convert` | `backend/services/speech_to_speech_service.py` |
| Artifact storage + registry metadata | `AudioArtifactStore.store_from_file` | `backend/services/audio_artifacts/store.py` |
| Provenance sidecar | `write_provenance_sidecar` | `backend/services/security_service.py` |
| Marking status API | `GET /api/audio/{id}/marking` | `backend/api/routes/audio.py` |
| C# client DTO | `StsMarkingModels.cs` | `src/VoiceStudio.App/Core/Models/` |
| UI display | `SpeechToSpeechView` + ViewModel | `src/VoiceStudio.App/Views/Panels/` |

No new authority is created. The `WatermarkingService` singleton on `SecurityService` is the sole embed/detect authority. The STS service is the sole caller for STS outputs.

---

## Implementation Plan

### Step 1: Wire embedding into STS `_run_convert`

In `speech_to_speech_service.py`, after RVC writes the output WAV and before `create_audio_artifact_from_file`:

1. Read the WAV file into numpy array
2. Call `get_security_service().watermarking.embed_watermark(samples, sr, payload)`
3. Write watermarked samples back to the temp file
4. If embedding fails: log warning, proceed without watermark, set `watermark_applied = False`
5. Pass `watermark_applied` and `watermark_id` through to response and registry metadata

### Step 2: Extend marking status

- `StsMarkingStatus` model gains: `watermark_applied`, `watermark_verified`, `watermark_method`
- `SpeechToSpeechResponse` gains: `watermark_applied`, `watermark_method`
- Registry metadata gains: `watermark_id`, `watermark_method` (alongside existing `is_transformed`, `transformation_type`)

### Step 3: Detection at marking endpoint

`GET /api/audio/{id}/marking`:
1. Load artifact from registry
2. Read existing metadata fields (as now)
3. If `watermark_applied` in metadata: load audio file, call `detect_watermark`, set `watermark_verified` based on result
4. If detection fails or file unavailable: `watermark_verified = false` honestly

### Step 4: Provenance sidecar extension

`write_provenance_sidecar` gains optional `watermark_id` and `watermark_method` fields when watermark was applied.

### Step 5: C# client + UI

- Update `StsMarkingModels.cs` with new fields
- `SpeechToSpeechViewModel` marking badge logic: metadata-marked + sample-watermarked vs metadata-marked-only

### Step 6: Orphan cleanup

Remove `backend/security/security_service.py` (the orphaned duplicate).

---

## Acceptance Criteria

- [ ] STS converted audio has sample-level watermark embedded (WAV output)
- [ ] `detect_watermark` or `verify_watermark` confirms the watermark on the stored artifact
- [ ] Metadata marking and sample marking do not silently disagree
- [ ] `StsMarkingStatus` response includes `watermark_applied`, `watermark_verified`, `watermark_method`
- [ ] When embedding fails, `watermark_applied = false` is surfaced honestly
- [ ] Non-STS artifacts remain unwatermarked (no scope creep)
- [ ] Orphaned `backend/security/security_service.py` removed
- [ ] Provenance sidecar includes watermark fields when applicable
- [ ] C# `StsMarkingModels` updated; UI badge distinguishes metadata-only vs sample-watermarked
- [ ] Test class named `Gap056Slice03Tests` (not mismatched numbering)

---

## Required Proofs

### Python (targeted)
- [ ] `tests/unit/backend/services/test_sts_sample_watermark.py` — embed + detect round-trip on STS output
- [ ] Embedding applied on eligible transformed STS output → detection succeeds
- [ ] Non-transformed output remains unwatermarked
- [ ] Embed failure → `watermark_applied = false`, conversion still succeeds
- [ ] Marking endpoint returns `watermark_verified` from live detection

### C# (targeted)
- [ ] `Gap056Slice03Tests` — client/VM reflects actual watermark status
- [ ] UI state distinguishes: transformed + metadata + sample-watermarked vs transformed + metadata-only

### Suite gates
- [ ] `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → 0 errors
- [ ] `dotnet test ... --filter "Gap056Slice03Tests|SpeechToSpeechMarkingSeamTests"` → all PASS
- [ ] `python -m pytest tests/unit/backend/services/test_sts_sample_watermark.py` → all PASS
- [ ] `python -m pytest tests/unit/backend/services/test_speech_to_speech_service.py` → all PASS
- [ ] Full `VoiceStudio.App.Tests` → PASS (no regression)
- [ ] `python scripts/ci/check_ibackendclient_creep.py` → PASS
- [ ] `python scripts/check_empty_catches.py` → PASS
- [ ] `.\scripts\verify.ps1 -Quick` → PASS

### Governance
- [ ] This execution row updated to Closed
- [ ] Closure report written to `docs/reports/verification/`
- [ ] `.cursor/STATE.md` ACTIVE WINDOW updated
- [ ] `PROFESSIONAL_GAP_TRACKER.md` tracker addendum added
- [ ] `AUTOMATION_ID_REGISTRY.md` updated if new UI affordances
- [ ] `openmemory.md` updated if new patterns discovered

---

## Evidence Grade

- **Grade S (Static):** Build, lint, creep check, empty-catch audit
- **Grade I (Integration):** Targeted pytest embed/detect round-trip; targeted MSTest seam tests
- **Grade R (Runtime):** Not required for this lane (no backend-up prerequisite for watermark logic tests)

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| LSB watermark destroyed by lossy export | High | Low | Documented constraint; `watermark_verified = false` surfaces honestly; metadata marking remains |
| `WatermarkingService` secret key not persisted between restarts | Medium | Medium | Documented as Hard OUT (key management is future work); detection only works within same service instance |
| Watermark embedding adds latency to STS conversion | Low | Low | Embed is O(n) single pass; negligible vs RVC inference time |
| Per-instance random key means different service instances produce non-cross-verifiable watermarks | Medium | Low | Acceptable for single-user desktop app; documented constraint |

---

## References

- Predecessor: [GOV_VOICESTUDIO_GAP056_STS_DURABLE_MARKING_02_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP056_STS_DURABLE_MARKING_02_EXECUTION_ROW.md)
- Predecessor closure: [VOICESTUDIO_GAP056_STS_DURABLE_MARKING_LANE_CLOSURE_2026-04-10.md](../reports/verification/VOICESTUDIO_GAP056_STS_DURABLE_MARKING_LANE_CLOSURE_2026-04-10.md)
- Policy: [PROVENANCE_POLICY.md](../governance/PROVENANCE_POLICY.md)
- Security service: `backend/services/security_service.py` (lines 390–572: `WatermarkingService`)
- STS service: `backend/services/speech_to_speech_service.py`
- Marking endpoint: `backend/api/routes/audio.py` (`get_audio_marking`)
