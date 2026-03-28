# GOV-VOICESTUDIO-VOICE-CLONING-INTEGRITY-01 — Voice Cloning Integrity

## 0. Status

- **State:** **Closed** (2026-03-29) — Slices 1–4 complete; lane closure [VOICESTUDIO_VOICE_CLONING_INTEGRITY_LANE_CLOSURE_2026-03-29.md](../reports/verification/VOICESTUDIO_VOICE_CLONING_INTEGRITY_LANE_CLOSURE_2026-03-29.md)
- **Opened:** 2026-03-29
- **Owner:** Tyler + agent execution support
- **Predecessor lanes:** `GOV-VOICESTUDIO-WORKFLOW-COHERENCE-ADVANCED-01` (closed), `GOV-VOICESTUDIO-UNIFIED-STARTUP-01` (closed)
- **Evidence basis:** [VOICESTUDIO_PROFESSIONAL_GRADE_AUDIT_2026-03-28.md](../reports/audit/VOICESTUDIO_PROFESSIONAL_GRADE_AUDIT_2026-03-28.md) — headline clone path metadata-only; code-truth in `profile_service.py` + `voice_cloning_wizard.py`.

---

## 1. Objective (Frozen)

Make **cloned-voice creation functionally real** by binding uploaded reference audio to the created profile and proving the profile is consumable by synthesis (`reference_audio.wav` under canonical profile dir).

---

## 2. In Scope

- `create_profile_from_request` optional `reference_audio_source` → copy to `profiles/<id>/reference_audio.wav` before `store.save` (rollback: copy failure must not persist profile).
- Wizard `process_voice_cloning` passes `audio_path` into profile creation.
- `finalize_wizard` must not invent a random `profile_id`; require `job.profile_id` after process step.
- Stored profile dict: `reference_audio_bound`, `reference_audio_url` when bound.
- API: `reference_audio_bound` on list/get/update responses via `exists_reference_audio`.
- Dedicated pytest proof; closure report + verify gates.

---

## 3. Out of Scope (Hard)

- Timeline persistence rewrite, unified project save.
- Prosody stub, fake telemetry, training simulation disclosure (separate lane).
- VST/DAW parity, text-based editing, installer/commercial work.
- Broad profile-system redesign; WinUI XAML changes beyond test pass-through.
- Database migration / backfill for legacy profiles (mark incomplete only).

---

## 4. Slice Map

| Slice | Intent | Proof |
| --- | --- | --- |
| **1** | Bind reference audio in service + wizard | Code + unit tests |
| **2** | Proof tests (service + wizard async path) | `test_profile_service_binding.py`, `test_wizard_binding.py` |
| **3** | Legacy honesty (`reference_audio_bound` on API) | Contract tests / manual spot |
| **4** | Verification + closure | Lane closure report, STATE, CANONICAL_REGISTRY |

---

## 5. Slice 1 — Binary Acceptance

1. `create_profile_from_request(..., reference_audio_source=path)` copies to canonical `reference_audio.wav`, sets `reference_audio_bound=True`.
2. Without source: `reference_audio_bound=False`, no file required.
3. Missing source path raises `ValueError` before save.
4. Wizard passes `reference_audio_source=audio_path` from `AudioRegistry`.
5. `finalize_wizard` returns 400 if `job.profile_id` is missing when status allows finalize.

---

## 6. Slice 2 — Test Acceptance

- Four service tests + two wizard tests (binding + finalize 400) all PASS.

---

## 7. Slice 3 — API Acceptance

- `GET /api/profiles`, `GET /api/profiles/{id}`, `PUT` response include `reference_audio_bound` (computed from disk).

---

## 8. Slice 4 — Closure

- `dotnet build`, full `dotnet test` App.Tests, `pytest tests/ci`, `verify.ps1 -Quick`, `run_verification.py` (no `--skip-guard` when tree clean).
- Execution row §0 **Closed**; proof report under `docs/reports/verification/`.

---

## 9. Mandatory Verification

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
python -m pytest tests/ci/ -q --randomly-seed=12345
.\scripts\verify.ps1 -Quick
python scripts/run_verification.py
```

---

## 10. Execution Records

### 10.1 Slice 1 — Closed (2026-03-29)

- Service + wizard binding: `profile_service.create_profile_from_request`, wizard `process_voice_cloning` + `finalize_wizard` fix.

### 10.2 Slice 2 — Closed (2026-03-29)

- Tests: `tests/unit/backend/services/test_profile_service_binding.py`, `tests/unit/backend/api/routes/test_wizard_binding.py`.

### 10.3 Slice 3 — Closed (2026-03-29)

- API: `reference_audio_bound` on profiles routes; `VoiceProfile` model field.

### 10.4 Slice 4 — Closed (2026-03-29)

- Closure: `docs/reports/verification/VOICESTUDIO_VOICE_CLONING_INTEGRITY_LANE_CLOSURE_2026-03-29.md`; `verify.ps1 -Quick` → `artifacts/verify/20260328_022359/`.
